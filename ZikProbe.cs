// ZikProbe - outil console de validation du protocole Parrot Zik sous Windows.
// Protocole repris de picaso/parrot-zik-status (MIT) et m0sia/pyParrotZik.
//
// Usage :
//   ZikProbe                      etat complet du casque
//   ZikProbe get <chemin>         ex. get /api/system/battery/get
//   ZikProbe set <chemin> <arg>   ex. set /api/audio/noise_cancellation/enabled/set true
//   Option : --mac 90:03:B7:xx:xx:xx pour forcer l'adresse du casque
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

class BluetoothEndPoint : EndPoint
{
    const int AfBth = 32;
    const int SockaddrBthSize = 30;

    readonly ulong address;
    readonly Guid service;

    public BluetoothEndPoint(ulong address, Guid service)
    {
        this.address = address;
        this.service = service;
    }

    public override AddressFamily AddressFamily
    {
        get { return (AddressFamily)AfBth; }
    }

    // SOCKADDR_BTH : famille (2) + adresse (8) + GUID du service (16) + port (4)
    public override SocketAddress Serialize()
    {
        SocketAddress sa = new SocketAddress(AddressFamily, SockaddrBthSize);
        byte[] addr = BitConverter.GetBytes(address);
        for (int i = 0; i < 8; i++) sa[2 + i] = addr[i];
        byte[] guid = service.ToByteArray();
        for (int i = 0; i < 16; i++) sa[10 + i] = guid[i];
        return sa;
    }

    public override EndPoint Create(SocketAddress socketAddress)
    {
        return this;
    }
}

static class ZikProbe
{
    const int BthProtoRfcomm = 3;

    static readonly string[][] Services = {
        new[] { "Zik 3", "8B6814D3-6CE7-4498-9700-9312C1711F64" },
        new[] { "Zik 2", "8B6814D3-6CE7-4498-9700-9312C1711F63" },
        new[] { "Zik 1", "0EF0F502-F0EE-46C9-986C-54ED027807FB" },
    };

    static readonly string[] StatusPaths = {
        "/api/software/version/get",
        "/api/bluetooth/friendlyname/get",
        "/api/system/battery/get",
        "/api/audio/noise_cancellation/enabled/get",
        "/api/audio/noise_control/enabled/get",
        "/api/audio/noise_control/get",
        "/api/audio/equalizer/enabled/get",
        "/api/audio/sound_effect/enabled/get",
        "/api/flight_mode/get",
        "/api/system/head_detection/enabled/get",
        "/api/system/auto_power_off/get",
    };

    static int Main(string[] args)
    {
        List<string> rest = new List<string>();
        string mac = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--mac" && i + 1 < args.Length) mac = args[++i];
            else rest.Add(args[i]);
        }

        try
        {
            ulong address = mac != null ? ParseMac(mac) : FindPairedZik();
            Console.WriteLine("Casque : " + FormatMac(address));

            using (Socket socket = Connect(address))
            using (NetworkStream stream = new NetworkStream(socket))
            {
                Handshake(stream);

                if (rest.Count >= 2 && rest[0] == "get")
                {
                    Console.WriteLine(Request(stream, "GET " + rest[1]));
                }
                else if (rest.Count >= 3 && rest[0] == "set")
                {
                    Console.WriteLine(Request(stream, "SET " + rest[1] + "?arg=" + rest[2]));
                }
                else
                {
                    foreach (string path in StatusPaths)
                    {
                        Console.WriteLine();
                        Console.WriteLine("> GET " + path);
                        Console.WriteLine(Request(stream, "GET " + path));
                    }
                }
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("ERREUR : " + e.Message);
            return 1;
        }
    }

    // Les Zik ont une adresse en 90:03:... ou A0:14:... (prefixes Parrot)
    static ulong FindPairedZik()
    {
        const string devicesKey = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(devicesKey))
        {
            if (key == null) throw new Exception("Bluetooth introuvable sur ce PC.");
            foreach (string name in key.GetSubKeyNames())
            {
                if (name.Length != 12) continue;
                string prefix = name.Substring(0, 4).ToUpperInvariant();
                if (prefix == "9003" || prefix == "A014") return Convert.ToUInt64(name, 16);
            }
        }
        throw new Exception("Aucun Parrot Zik appaire. Appaire le casque dans les parametres "
            + "Bluetooth de Windows, ou passe son adresse avec --mac.");
    }

    static Socket Connect(ulong address)
    {
        string errors = "";
        foreach (string[] service in Services)
        {
            Socket socket = new Socket((AddressFamily)32, SocketType.Stream, (ProtocolType)BthProtoRfcomm);
            try
            {
                socket.Connect(new BluetoothEndPoint(address, new Guid(service[1])));
                socket.ReceiveTimeout = 5000;
                Console.WriteLine("Connecte au service " + service[0]);
                return socket;
            }
            catch (SocketException e)
            {
                errors += "\n  " + service[0] + " : " + e.SocketErrorCode + " (" + e.ErrorCode + ")";
                socket.Close();
            }
        }
        throw new Exception("Connexion RFCOMM impossible. Le casque est-il allume et a portee ?" + errors);
    }

    static void Handshake(Stream stream)
    {
        byte[] hello = { 0x00, 0x03, 0x00 };
        stream.Write(hello, 0, hello.Length);
        byte[] reply = ReadFrame(stream);
        Console.WriteLine("Handshake : " + BitConverter.ToString(reply));
    }

    static string Request(Stream stream, string command)
    {
        byte[] body = Encoding.UTF8.GetBytes(command);
        int length = body.Length + 3;
        byte[] frame = new byte[length];
        frame[0] = (byte)(length >> 8);
        frame[1] = (byte)(length & 0xFF);
        frame[2] = 0x80;
        Buffer.BlockCopy(body, 0, frame, 3, body.Length);
        stream.Write(frame, 0, frame.Length);

        // Le casque peut intercaler des <notify> avant la reponse attendue
        StringBuilder output = new StringBuilder();
        for (int i = 0; i < 5; i++)
        {
            string xml = ExtractXml(ReadFrame(stream));
            if (output.Length > 0) output.AppendLine();
            output.Append(xml);
            if (!Regex.IsMatch(xml, @"^\s*<notify")) break;
        }
        return output.ToString();
    }

    // Trame : longueur totale sur 2 octets (big-endian), puis le reste
    static byte[] ReadFrame(Stream stream)
    {
        byte[] header = ReadExact(stream, 2);
        int length = (header[0] << 8) | header[1];
        if (length < 2) throw new Exception("Trame invalide (longueur " + length + ").");
        byte[] frame = new byte[length];
        frame[0] = header[0];
        frame[1] = header[1];
        byte[] rest = ReadExact(stream, length - 2);
        Buffer.BlockCopy(rest, 0, frame, 2, rest.Length);
        return frame;
    }

    static byte[] ReadExact(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n == 0) throw new Exception("Connexion fermee par le casque.");
            read += n;
        }
        return buffer;
    }

    static string ExtractXml(byte[] frame)
    {
        int start = Array.IndexOf(frame, (byte)'<');
        if (start < 0) return "(pas de XML) " + BitConverter.ToString(frame);
        return Encoding.UTF8.GetString(frame, start, frame.Length - start);
    }

    static ulong ParseMac(string mac)
    {
        string hex = mac.Replace(":", "").Replace("-", "");
        if (hex.Length != 12) throw new Exception("Adresse invalide : " + mac);
        return Convert.ToUInt64(hex, 16);
    }

    static string FormatMac(ulong address)
    {
        string hex = address.ToString("X12");
        return Regex.Replace(hex, "(..)(?!$)", "$1:");
    }
}
