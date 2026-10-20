// Liaison Bluetooth avec un Parrot Zik 2/3 : connexion RFCOMM, requetes, etat.
// Protocole repris de picaso/parrot-zik-status (MIT) et m0sia/pyParrotZik.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

class ZikState
{
    public bool Connected;
    public string Name = "";
    public string Firmware = "";
    public int Battery = -1;
    public string BatteryState = "";
    public string NoiseType = "";
    public int NoiseValue;
    public bool AutoNoise;
    public bool Equalizer;
    public bool ConcertHall;
    public string Room = "";
    public string Angle = "";
    public bool HeadDetection;

    public bool Charging
    {
        get { return BatteryState == "charging" || BatteryState == "charged"; }
    }
}

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

class ZikClient
{
    const int BthProtoRfcomm = 3;
    const int PollIntervalMs = 60000;
    const int RetryIntervalMs = 5000;

    // Zik 3, Zik 2 (meme API)
    static readonly Guid[] Services = {
        new Guid("8B6814D3-6CE7-4498-9700-9312C1711F64"),
        new Guid("8B6814D3-6CE7-4498-9700-9312C1711F63"),
    };

    readonly BlockingCollection<string> commands = new BlockingCollection<string>();
    int preferredService;
    string lastError;

    public static Action<string> Log = delegate { };

    // Leve depuis le thread de communication
    public event Action<ZikState> StateChanged;

    public void Start()
    {
        Thread thread = new Thread(Run);
        thread.IsBackground = true;
        thread.Name = "ZikClient";
        thread.Start();
    }

    public void Set(string path, string arg)
    {
        commands.Add("SET " + path + "?arg=" + arg);
    }

    public void Send(string command)
    {
        commands.Add(command);
    }

    public void Refresh()
    {
        commands.Add("");
    }

    void Run()
    {
        while (true)
        {
            try
            {
                ulong address = FindPairedZik();
                // Ne jamais appeler le casque nous-memes : une tentative de connexion quand il est eteint
                // ou relie a un autre appareil peut perturber sa liaison audio. On attend que Windows l'ait connecte.
                if (!IsLinkUp(address)) throw new IOException("Casque non connecte a Windows.");
                using (Socket socket = Connect(address))
                using (NetworkStream stream = new NetworkStream(socket))
                {
                    Handshake(stream);
                    Log("Connecte");
                    lastError = null;
                    Publish(ReadState(stream));
                    while (true)
                    {
                        string command;
                        if (commands.TryTake(out command, PollIntervalMs))
                        {
                            do
                            {
                                if (command.Length > 0) Log(command + " -> " + Request(stream, command).OuterXml);
                            }
                            while (commands.TryTake(out command));
                            // laisse au casque le temps d'appliquer avant de relire son etat
                            Thread.Sleep(500);
                        }
                        Publish(ReadState(stream));
                    }
                }
            }
            catch (Exception e)
            {
                // casque eteint : on ne journalise que le premier echec, pas chaque nouvel essai
                if (e.Message != lastError) Log("Deconnecte : " + e.Message);
                lastError = e.Message;
                // casque eteint, hors de portee ou non appaire : on reessaie plus tard
            }

            Publish(new ZikState());
            string dropped;
            while (commands.TryTake(out dropped)) { }
            Thread.Sleep(RetryIntervalMs);
        }
    }

    void Publish(ZikState state)
    {
        Action<ZikState> handler = StateChanged;
        if (handler != null) handler(state);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct BluetoothDeviceInfo
    {
        public int Size;
        public ulong Address;
        public uint ClassOfDevice;
        public int Connected;
        public int Remembered;
        public int Authenticated;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public ushort[] LastSeen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public ushort[] LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [DllImport("BluetoothApis.dll")]
    static extern int BluetoothGetDeviceInfo(IntPtr radio, ref BluetoothDeviceInfo info);

    // Etat de la liaison Bluetooth vu par Windows (lecture locale, aucun trafic radio)
    static bool IsLinkUp(ulong address)
    {
        try
        {
            BluetoothDeviceInfo info = new BluetoothDeviceInfo();
            info.Size = Marshal.SizeOf(typeof(BluetoothDeviceInfo));
            info.Address = address;
            if (BluetoothGetDeviceInfo(IntPtr.Zero, ref info) != 0) return true;
            return info.Connected != 0;
        }
        catch (Exception)
        {
            return true; // API indisponible : on tente la connexion comme avant
        }
    }

    // Les Zik ont une adresse en 90:03:... ou A0:14:... (prefixes Parrot)
    static ulong FindPairedZik()
    {
        const string devicesKey = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(devicesKey))
        {
            if (key == null) throw new IOException("Bluetooth introuvable.");
            foreach (string name in key.GetSubKeyNames())
            {
                if (name.Length != 12) continue;
                string prefix = name.Substring(0, 4).ToUpperInvariant();
                if (prefix == "9003" || prefix == "A014") return Convert.ToUInt64(name, 16);
            }
        }
        throw new IOException("Aucun Parrot Zik appaire.");
    }

    Socket Connect(ulong address)
    {
        SocketException last = null;
        for (int i = 0; i < Services.Length; i++)
        {
            int index = (preferredService + i) % Services.Length;
            Socket socket = new Socket((AddressFamily)32, SocketType.Stream, (ProtocolType)BthProtoRfcomm);
            try
            {
                socket.Connect(new BluetoothEndPoint(address, Services[index]));
                socket.ReceiveTimeout = 5000;
                socket.SendTimeout = 5000;
                preferredService = index;
                return socket;
            }
            catch (SocketException e)
            {
                last = e;
                socket.Close();
            }
        }
        throw last;
    }

    static void Handshake(Stream stream)
    {
        byte[] hello = { 0x00, 0x03, 0x00 };
        stream.Write(hello, 0, hello.Length);
        ReadFrame(stream);
    }

    static ZikState ReadState(Stream stream)
    {
        ZikState state = new ZikState();
        state.Connected = true;

        state.Firmware = Get(stream, "/api/software/version/get", "//software", "sip6");
        state.Name = Get(stream, "/api/bluetooth/friendlyname/get", "//bluetooth", "friendlyname");

        XmlDocument battery = Request(stream, "GET /api/system/battery/get");
        state.BatteryState = Attribute(battery, "//battery", "state");
        int percent;
        if (int.TryParse(Attribute(battery, "//battery", "percent"), out percent)) state.Battery = percent;

        XmlDocument noise = Request(stream, "GET /api/audio/noise_control/get");
        state.NoiseType = Attribute(noise, "//noise_control", "type");
        int.TryParse(Attribute(noise, "//noise_control", "value"), out state.NoiseValue);
        state.AutoNoise = Attribute(noise, "//noise_control", "auto_nc") == "true";

        state.Equalizer = Get(stream, "/api/audio/equalizer/enabled/get", "//equalizer", "enabled") == "true";

        XmlDocument effect = Request(stream, "GET /api/audio/sound_effect/get");
        state.ConcertHall = Attribute(effect, "//sound_effect", "enabled") == "true";
        state.Room = Attribute(effect, "//sound_effect", "room_size");
        state.Angle = Attribute(effect, "//sound_effect", "angle");

        state.HeadDetection =
            Get(stream, "/api/system/head_detection/enabled/get", "//head_detection", "enabled") == "true";
        return state;
    }

    static string Get(Stream stream, string path, string xpath, string attribute)
    {
        return Attribute(Request(stream, "GET " + path), xpath, attribute);
    }

    static string Attribute(XmlDocument document, string xpath, string attribute)
    {
        XmlElement element = document.SelectSingleNode(xpath) as XmlElement;
        return element == null ? "" : element.GetAttribute(attribute);
    }

    static XmlDocument Request(Stream stream, string command)
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
        while (true)
        {
            XmlDocument document = ParseXml(ReadFrame(stream));
            if (document.DocumentElement != null && document.DocumentElement.Name == "answer") return document;
        }
    }

    // Trame : longueur totale sur 2 octets (big-endian), puis le reste
    static byte[] ReadFrame(Stream stream)
    {
        byte[] header = ReadExact(stream, 2);
        int length = (header[0] << 8) | header[1];
        if (length < 2) throw new IOException("Trame invalide.");
        return ReadExact(stream, length - 2);
    }

    static byte[] ReadExact(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n == 0) throw new IOException("Connexion fermee par le casque.");
            read += n;
        }
        return buffer;
    }

    static XmlDocument ParseXml(byte[] payload)
    {
        XmlDocument document = new XmlDocument();
        int start = Array.IndexOf(payload, (byte)'<');
        if (start >= 0) document.LoadXml(Encoding.UTF8.GetString(payload, start, payload.Length - start));
        return document;
    }
}
