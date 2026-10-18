// ZikSetup - installateur de ZikTray pour l'utilisateur courant (sans droits administrateur).
//
//   ZikTray-Setup.exe               installation avec fenetre
//   ZikTray-Setup.exe /silent       installation sans fenetre (demarrage avec Windows active)
//   uninstall.exe /uninstall        desinstallation (enregistree dans Parametres > Applications)
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

static class Installer
{
    public const string Version = "1.1";
    const string AppName = "ZikTray";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ZikTray";

    public static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string AppExe
    {
        get { return Path.Combine(InstallDir, "ZikTray.exe"); }
    }

    static string UninstallerExe
    {
        get { return Path.Combine(InstallDir, "uninstall.exe"); }
    }

    static string StartMenuShortcut
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
        }
    }

    static string DesktopShortcut
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
        }
    }

    public static bool IsInstalled
    {
        get { return File.Exists(AppExe); }
    }

    public static void Install(bool startWithWindows, bool desktopShortcut)
    {
        StopApp();
        Directory.CreateDirectory(InstallDir);

        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZikTray.exe"))
        using (FileStream target = File.Create(AppExe))
        {
            source.CopyTo(target);
        }
        string self = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallerExe), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, UninstallerExe, true);
        }

        CreateShortcut(StartMenuShortcut);
        if (desktopShortcut) CreateShortcut(DesktopShortcut);
        else File.Delete(DesktopShortcut);

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (startWithWindows) key.SetValue(AppName, "\"" + AppExe + "\" --hidden");
            else key.DeleteValue(AppName, false);
        }

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "yokoinc");
            key.SetValue("DisplayIcon", AppExe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", "\"" + UninstallerExe + "\" /uninstall");
            key.SetValue("QuietUninstallString", "\"" + UninstallerExe + "\" /uninstall /silent");
            key.SetValue("URLInfoAbout", "https://github.com/yokoinc/parrot-zik-windows");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new FileInfo(AppExe).Length / 1024 + 20), RegistryValueKind.DWord);
        }
    }

    public static void Launch()
    {
        Process.Start(AppExe);
    }

    public static void Uninstall()
    {
        StopApp();
        File.Delete(StartMenuShortcut);
        File.Delete(DesktopShortcut);
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (key != null) key.DeleteValue(AppName, false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

        // uninstall.exe est en cours d'execution : le dossier est supprime juste apres sa fermeture
        ProcessStartInfo cleanup = new ProcessStartInfo("cmd.exe",
            "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"");
        cleanup.CreateNoWindow = true;
        cleanup.UseShellExecute = false;
        cleanup.WorkingDirectory = Path.GetTempPath();
        Process.Start(cleanup);
    }

    static void StopApp()
    {
        foreach (Process process in Process.GetProcessesByName("ZikTray"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception)
            {
                // deja ferme
            }
        }
    }

    static void CreateShortcut(string path)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = AppExe;
        shortcut.WorkingDirectory = InstallDir;
        shortcut.IconLocation = AppExe + ",0";
        shortcut.Description = "Réglages du casque Parrot Zik";
        shortcut.Save();
    }
}

class SetupWindow : Window
{
    readonly bool uninstall;
    readonly TextBlock heading = new TextBlock();
    readonly TextBlock message = new TextBlock();
    readonly StackPanel options = new StackPanel();
    readonly CheckBox startup = new CheckBox();
    readonly CheckBox desktop = new CheckBox();
    readonly Button cancel = new Button();
    readonly Button confirm = new Button();
    bool done;

    public SetupWindow(bool uninstall)
    {
        this.uninstall = uninstall;

        Title = uninstall ? "Désinstaller ZikTray" : "Installer ZikTray";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 444;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Font;
        FontSize = 13;
        Foreground = Theme.Text;
        UseLayoutRounding = true;

        Border frame = new Border();
        frame.Margin = new Thickness(12);
        frame.CornerRadius = new CornerRadius(14);
        frame.Background = Theme.Surface;
        frame.BorderBrush = Theme.Line;
        frame.BorderThickness = new Thickness(1);
        frame.Padding = new Thickness(24, 22, 24, 20);
        DropShadowEffect shadow = new DropShadowEffect();
        shadow.BlurRadius = 14;
        shadow.ShadowDepth = 2;
        shadow.Opacity = 0.45;
        frame.Effect = shadow;
        frame.MouseLeftButtonDown += delegate { DragMove(); };
        Content = frame;

        StackPanel stack = new StackPanel();
        frame.Child = stack;

        Grid header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        Image icon = new Image();
        icon.Source = AppIcon();
        icon.Width = 52;
        icon.Height = 52;
        icon.Margin = new Thickness(0, 0, 16, 0);
        header.Children.Add(icon);

        StackPanel names = new StackPanel();
        names.VerticalAlignment = VerticalAlignment.Center;
        TextBlock name = new TextBlock();
        name.Text = "ZikTray " + Installer.Version;
        name.FontSize = 21;
        name.FontWeight = FontWeights.SemiBold;
        TextBlock tagline = new TextBlock();
        tagline.Text = "Réglages du Parrot Zik depuis la barre des tâches";
        tagline.Foreground = Theme.Muted;
        names.Children.Add(name);
        names.Children.Add(tagline);
        Grid.SetColumn(names, 1);
        header.Children.Add(names);
        stack.Children.Add(header);

        heading.FontSize = 15;
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(0, 22, 0, 4);
        message.Foreground = Theme.Muted;
        message.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(heading);
        stack.Children.Add(message);

        options.Margin = new Thickness(0, 14, 0, 0);
        SetupSwitch(startup, "Démarrer avec Windows", true);
        SetupSwitch(desktop, "Raccourci sur le bureau", false);
        options.Children.Add(startup);
        options.Children.Add(desktop);
        stack.Children.Add(options);

        StackPanel buttons = new StackPanel();
        buttons.Orientation = Orientation.Horizontal;
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 24, 0, 0);
        cancel.Style = Theme.Style("Button");
        cancel.Content = "Annuler";
        cancel.Margin = new Thickness(0, 0, 10, 0);
        cancel.Click += delegate { Close(); };
        confirm.Style = Theme.Style("Button");
        confirm.Background = Theme.Accent;
        confirm.Click += delegate { Confirm(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        stack.Children.Add(buttons);

        if (uninstall)
        {
            heading.Text = "Désinstaller ZikTray ?";
            message.Text = "L'application, ses raccourcis et son lancement au démarrage seront supprimés.";
            options.Visibility = Visibility.Collapsed;
            confirm.Content = "Désinstaller";
        }
        else
        {
            bool update = Installer.IsInstalled;
            heading.Text = update ? "Mettre à jour ZikTray" : "Installer ZikTray";
            message.Text = "Dossier : " + Installer.InstallDir;
            confirm.Content = update ? "Mettre à jour" : "Installer";
        }
    }

    static ImageSource AppIcon()
    {
        using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location))
        using (System.Drawing.Icon large = new System.Drawing.Icon(icon, 64, 64))
        {
            return Imaging.CreateBitmapSourceFromHIcon(large.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
    }

    static void SetupSwitch(CheckBox box, string text, bool on)
    {
        box.Style = Theme.Style("Switch");
        box.Foreground = Theme.Text;
        box.IsChecked = on;
        box.Margin = new Thickness(0, 0, 0, 10);
        TextBlock label = new TextBlock();
        label.Text = text;
        box.Content = label;
    }

    void Confirm()
    {
        if (done)
        {
            Close();
            return;
        }

        try
        {
            Cursor = Cursors.Wait;
            if (uninstall)
            {
                Installer.Uninstall();
                heading.Text = "ZikTray est désinstallé.";
                message.Text = "Le fichier ZikTray.eq (dernier réglage d'égaliseur) a été supprimé avec le reste.";
            }
            else
            {
                Installer.Install(startup.IsChecked == true, desktop.IsChecked == true);
                Installer.Launch();
                heading.Text = "ZikTray est installé.";
                message.Text = "L'icône est dans la zone de notification (près de l'horloge, parfois derrière la flèche ^). "
                    + "On le retrouve aussi dans le menu Démarrer.";
                options.Visibility = Visibility.Collapsed;
            }
            done = true;
            cancel.Visibility = Visibility.Collapsed;
            confirm.Content = "Fermer";
        }
        catch (Exception e)
        {
            heading.Text = "Échec";
            message.Text = e.Message;
        }
        finally
        {
            Cursor = null;
        }
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool uninstall = Array.IndexOf(args, "/uninstall") >= 0;
        bool silent = Array.IndexOf(args, "/silent") >= 0;

        if (silent)
        {
            try
            {
                if (uninstall) Installer.Uninstall();
                else Installer.Install(true, false);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }

        Application app = new Application();
        return app.Run(new SetupWindow(uninstall));
    }
}
