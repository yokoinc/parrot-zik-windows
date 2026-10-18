// ZikTray - icone de zone de notification pour Parrot Zik 2/3.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

// Rangee de boutons a choix unique
class SegmentGroup : Border
{
    readonly List<RadioButton> buttons = new List<RadioButton>();
    bool updating;

    public event Action<string> Selected;

    public SegmentGroup(double fontSize, params string[] labelsAndArgs)
    {
        UniformGrid grid = new UniformGrid();
        grid.Rows = 1;
        CornerRadius = new CornerRadius(10);
        Background = Theme.Raised;
        Padding = new Thickness(3);
        Child = grid;

        for (int i = 0; i < labelsAndArgs.Length; i += 2)
        {
            RadioButton button = new RadioButton();
            button.Content = labelsAndArgs[i];
            button.Tag = labelsAndArgs[i + 1];
            button.FontSize = fontSize;
            button.Style = Theme.Style("Segment");
            button.Checked += delegate(object sender, RoutedEventArgs e)
            {
                if (!updating && Selected != null) Selected((string)((RadioButton)sender).Tag);
            };
            buttons.Add(button);
            grid.Children.Add(button);
        }
    }

    public void Select(string arg)
    {
        updating = true;
        foreach (RadioButton button in buttons) button.IsChecked = (string)button.Tag == arg;
        updating = false;
    }
}

// Disque des ambiances : l'angle choisit l'ambiance, la distance au centre son intensite
class EqDisc : FrameworkElement
{
    public static readonly string[] Names = { "Pop", "Vocal", "Cristal", "Club", "Punchy", "Deep" };

    // Gains des 5 bandes (dB) des ambiances de l'appli Parrot, releves par le projet RMDaye/Ziker
    static readonly double[][] Gains = {
        new[] { 0.0, 6.0, 4.5, 3.5, 1.0 },
        new[] { -3.0, 3.0, 5.0, 4.0, 2.0 },
        new[] { 0.0, 0.0, 2.0, 3.5, 7.5 },
        new[] { 6.0, -4.0, 3.5, -1.0, 6.5 },
        new[] { 6.0, 1.5, 0.5, 1.0, 4.0 },
        new[] { 7.5, 3.0, 2.0, 0.0, 0.0 },
    };

    const double Box = 216;
    const double Radius = 76;
    const double Sector = 360.0 / 6;

    static readonly Typeface Face = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Pen RingPen = new Pen(Theme.Line, 1);
    static readonly Pen ThumbPen = new Pen(Brushes.White, 2);
    static readonly Brush Glow = Theme.Frozen(Color.FromArgb(0x40, 0xFF, 0x6A, 0x1A));

    public double Intensity;
    public double Angle;
    public bool HasThumb = true;

    public event Action Changed;

    public EqDisc()
    {
        Width = Box;
        Height = Box;
        Cursor = Cursors.Hand;
    }

    public double[] CurrentGains()
    {
        double position = Angle / Sector;
        int first = (int)Math.Floor(position) % Names.Length;
        int second = (first + 1) % Names.Length;
        double blend = position - Math.Floor(position);

        double[] gains = new double[5];
        for (int i = 0; i < gains.Length; i++)
        {
            double gain = Intensity * ((1 - blend) * Gains[first][i] + blend * Gains[second][i]);
            gains[i] = Math.Round(gain * 2) / 2;
        }
        return gains;
    }

    static Point Polar(Point center, double radius, double degrees)
    {
        double a = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Sin(a), center.Y - radius * Math.Cos(a));
    }

    protected override void OnRender(DrawingContext dc)
    {
        Point center = new Point(Box / 2, Box / 2);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Box, Box));
        dc.DrawEllipse(Theme.Raised, RingPen, center, Radius, Radius);
        dc.DrawEllipse(null, RingPen, center, Radius * 0.66, Radius * 0.66);
        dc.DrawEllipse(null, RingPen, center, Radius * 0.33, Radius * 0.33);
        dc.DrawEllipse(Theme.Muted, null, center, 2.5, 2.5);

        int nearest = HasThumb && Intensity > 0 ? (int)Math.Round(Angle / Sector) % Names.Length : -1;
        for (int i = 0; i < Names.Length; i++)
        {
            FormattedText text = new FormattedText(Names[i], CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, Face, 12, i == nearest ? Theme.Text : Theme.Muted);
            Point p = Polar(center, Radius + 20, i * Sector);
            dc.DrawText(text, new Point(p.X - text.Width / 2, p.Y - text.Height / 2));
        }

        if (HasThumb)
        {
            Point thumb = Polar(center, Intensity * Radius, Angle);
            dc.DrawEllipse(Glow, null, thumb, 18, 18);
            dc.DrawEllipse(Theme.Accent, ThumbPen, thumb, 9, 9);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        MoveThumb(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) MoveThumb(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
    }

    void MoveThumb(Point p)
    {
        double dx = p.X - Box / 2;
        double dy = p.Y - Box / 2;
        double intensity = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / Radius);
        Intensity = intensity < 0.08 ? 0 : intensity;
        Angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        HasThumb = true;
        InvalidateVisual();
        if (Changed != null) Changed();
    }
}

class ZikWindow : Window
{
    const double MaxGainDb = 8;
    static readonly string[] BandNames = { "70 Hz", "380 Hz", "1 kHz", "2,4 kHz", "11 kHz" };

    readonly ZikClient client;
    readonly TextBlock title = new TextBlock();
    readonly TextBlock subtitle = new TextBlock();
    readonly TextBlock battery = new TextBlock();
    readonly Border batteryPill = new Border();
    readonly StackPanel body = new StackPanel();
    readonly CheckBox autoNoise = new CheckBox();
    readonly SegmentGroup noise = new SegmentGroup(11.5,
        "Rue max", "aoc&value=2", "Rue", "aoc&value=1", "Off", "off&value=1",
        "ANC", "anc&value=1", "ANC max", "anc&value=2");
    readonly CheckBox equalizer = new CheckBox();
    readonly SegmentGroup eqView = new SegmentGroup(12, "Ambiance", "disc", "Bandes", "bands");
    readonly EqDisc disc = new EqDisc();
    readonly Grid bandsPanel = new Grid();
    readonly Slider[] bands = new Slider[5];
    readonly CheckBox concertHall = new CheckBox();
    readonly StackPanel concertOptions = new StackPanel();
    readonly SegmentGroup room = new SegmentGroup(12,
        "Silence", "silent", "Salon", "living", "Jazz", "jazz", "Concert", "concert");
    readonly SegmentGroup angle = new SegmentGroup(12,
        "30°", "30", "60°", "60", "90°", "90", "120°", "120", "150°", "150", "180°", "180");
    readonly CheckBox headDetection = new CheckBox();
    readonly DispatcherTimer eqTimer = new DispatcherTimer();
    bool updating;

    public DateTime HiddenAt = DateTime.MinValue;
    public FrameworkElement Root;

    public ZikWindow(ZikClient client)
    {
        this.client = client;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 364;
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
        frame.Padding = new Thickness(18, 16, 18, 18);
        DropShadowEffect shadow = new DropShadowEffect();
        shadow.BlurRadius = 14;
        shadow.ShadowDepth = 2;
        shadow.Opacity = 0.45;
        frame.Effect = shadow;
        Content = frame;
        Root = frame;

        StackPanel stack = new StackPanel();
        frame.Child = stack;
        stack.Children.Add(BuildHeader());
        stack.Children.Add(body);

        BuildNoise();
        BuildEqualizer();
        BuildConcertHall();
        SetupSwitch(headDetection, "Détection de tête", "/api/system/head_detection/enabled/set");
        headDetection.Margin = new Thickness(0, 20, 0, 0);
        body.Children.Add(headDetection);

        Deactivated += delegate
        {
            HiddenAt = DateTime.UtcNow;
            Hide();
        };

        LoadEqualizer();
        Apply(new ZikState());
    }

    UIElement BuildHeader()
    {
        Grid header = new Grid();
        header.Margin = new Thickness(0, 0, 0, 16);
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel names = new StackPanel();
        title.FontSize = 19;
        title.FontWeight = FontWeights.SemiBold;
        subtitle.Foreground = Theme.Muted;
        subtitle.FontSize = 12;
        subtitle.Margin = new Thickness(0, 1, 0, 0);
        names.Children.Add(title);
        names.Children.Add(subtitle);
        header.Children.Add(names);

        batteryPill.CornerRadius = new CornerRadius(11);
        batteryPill.Background = Theme.Raised;
        batteryPill.Padding = new Thickness(10, 3, 10, 4);
        batteryPill.VerticalAlignment = VerticalAlignment.Top;
        battery.FontWeight = FontWeights.SemiBold;
        batteryPill.Child = battery;
        Grid.SetColumn(batteryPill, 1);
        header.Children.Add(batteryPill);
        return header;
    }

    static TextBlock Caption(string text)
    {
        TextBlock caption = new TextBlock();
        caption.Text = text;
        caption.FontSize = 14;
        caption.FontWeight = FontWeights.SemiBold;
        return caption;
    }

    void BuildNoise()
    {
        Grid row = new Grid();
        row.Margin = new Thickness(0, 0, 0, 8);
        row.Children.Add(Caption("Contrôle du bruit"));
        SetupSwitch(autoNoise, "Auto", "/api/audio/noise_control/auto_nc/set");
        TextBlock autoLabel = (TextBlock)autoNoise.Content;
        autoLabel.FontSize = 12;
        autoLabel.FontWeight = FontWeights.Normal;
        autoLabel.Foreground = Theme.Muted;
        autoLabel.Margin = new Thickness(0, 0, 8, 0);
        autoNoise.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(autoNoise);
        body.Children.Add(row);

        noise.Selected += delegate(string arg)
        {
            // en mode automatique le casque ignore le niveau choisi a la main
            if (autoNoise.IsChecked == true) client.Set("/api/audio/noise_control/auto_nc/set", "false");
            client.Set("/api/audio/noise_control/set", arg);
        };
        body.Children.Add(noise);
    }

    void BuildEqualizer()
    {
        SetupSwitch(equalizer, "Égaliseur", "/api/audio/equalizer/enabled/set");
        equalizer.Margin = new Thickness(0, 20, 0, 8);
        body.Children.Add(equalizer);

        eqView.Selected += delegate(string view) { ShowEqView(view); };
        body.Children.Add(eqView);

        Grid area = new Grid();
        area.Height = 216;
        area.Margin = new Thickness(0, 4, 0, 0);
        disc.HorizontalAlignment = HorizontalAlignment.Center;
        disc.Changed += delegate
        {
            SetBands(disc.CurrentGains());
            QueueEqualizer();
        };
        area.Children.Add(disc);

        bandsPanel.Margin = new Thickness(0, 14, 0, 0);
        for (int i = 0; i < bands.Length; i++)
        {
            bandsPanel.ColumnDefinitions.Add(new ColumnDefinition());
            Slider band = new Slider();
            band.Style = Theme.Style("Band");
            band.Minimum = -MaxGainDb;
            band.Maximum = MaxGainDb;
            band.TickFrequency = 0.5;
            band.IsSnapToTickEnabled = true;
            band.LargeChange = 1;
            band.Height = 150;
            band.HorizontalAlignment = HorizontalAlignment.Center;
            band.ValueChanged += delegate
            {
                if (updating) return;
                disc.HasThumb = false;
                disc.InvalidateVisual();
                QueueEqualizer();
            };
            bands[i] = band;

            TextBlock name = new TextBlock();
            name.Text = BandNames[i];
            name.Foreground = Theme.Muted;
            name.FontSize = 11;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(0, 8, 0, 0);

            StackPanel column = new StackPanel();
            column.Children.Add(band);
            column.Children.Add(name);
            Grid.SetColumn(column, i);
            bandsPanel.Children.Add(column);
        }
        area.Children.Add(bandsPanel);
        body.Children.Add(area);

        // on attend la fin du geste avant d'envoyer
        eqTimer.Interval = TimeSpan.FromMilliseconds(200);
        eqTimer.Tick += delegate
        {
            eqTimer.Stop();
            SendEqualizer();
        };

        eqView.Select("disc");
        ShowEqView("disc");
    }

    void ShowEqView(string view)
    {
        eqView.Select(view);
        disc.Visibility = view == "disc" ? Visibility.Visible : Visibility.Hidden;
        bandsPanel.Visibility = view == "bands" ? Visibility.Visible : Visibility.Hidden;
    }

    void BuildConcertHall()
    {
        SetupSwitch(concertHall, "Concert Hall", "/api/audio/sound_effect/enabled/set");
        concertHall.Margin = new Thickness(0, 14, 0, 8);
        body.Children.Add(concertHall);

        room.Selected += delegate(string arg) { client.Set("/api/audio/sound_effect/room_size/set", arg); };
        angle.Selected += delegate(string arg) { client.Set("/api/audio/sound_effect/angle/set", arg); };
        angle.Margin = new Thickness(0, 6, 0, 0);
        concertOptions.Children.Add(room);
        concertOptions.Children.Add(angle);
        body.Children.Add(concertOptions);
    }

    void SetupSwitch(CheckBox box, string text, string path)
    {
        box.Style = Theme.Style("Switch");
        box.Foreground = Theme.Text;
        box.Content = Caption(text);
        RoutedEventHandler changed = delegate
        {
            if (!updating) client.Set(path, box.IsChecked == true ? "true" : "false");
        };
        box.Checked += changed;
        box.Unchecked += changed;
    }

    public void Apply(ZikState state)
    {
        updating = true;
        try
        {
            title.Text = state.Connected && state.Name.Length > 0 ? state.Name : "Parrot Zik";
            subtitle.Text = !state.Connected ? "Casque non connecté"
                : state.Firmware.Length > 0 ? "Firmware " + state.Firmware : "Connecté";

            batteryPill.Visibility = state.Connected && state.Battery >= 0 ? Visibility.Visible : Visibility.Collapsed;
            battery.Text = state.Battery + " %" + (state.Charging ? "  ⚡" : "");
            battery.Foreground = state.Charging ? Theme.Good : state.Battery <= 20 ? Theme.Bad : Theme.Text;

            body.IsEnabled = state.Connected;
            body.Opacity = state.Connected ? 1 : 0.4;

            string mode = state.NoiseType == "off" ? "off&value=1" : state.NoiseType + "&value=" + state.NoiseValue;
            noise.Select(mode);
            autoNoise.IsChecked = state.AutoNoise;
            noise.Opacity = state.AutoNoise ? 0.55 : 1;

            equalizer.IsChecked = state.Equalizer;
            concertHall.IsChecked = state.ConcertHall;
            concertOptions.IsEnabled = state.ConcertHall;
            concertOptions.Opacity = state.ConcertHall ? 1 : 0.4;
            room.Select(state.Room);
            angle.Select(state.Angle);
            headDetection.IsChecked = state.HeadDetection;
        }
        finally
        {
            updating = false;
        }
    }

    static string EqualizerFile
    {
        get { return Path.ChangeExtension(Program.ExecutablePath, ".eq"); }
    }

    // Le casque ne renvoie pas ses gains : on memorise les derniers envoyes
    void LoadEqualizer()
    {
        double[] gains = new double[bands.Length];
        try
        {
            string[] parts = File.ReadAllText(EqualizerFile).Trim().Split(',');
            for (int i = 0; i < gains.Length; i++) gains[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
            disc.HasThumb = parts.Length >= 7;
            if (disc.HasThumb)
            {
                disc.Intensity = double.Parse(parts[5], CultureInfo.InvariantCulture);
                disc.Angle = double.Parse(parts[6], CultureInfo.InvariantCulture);
            }
        }
        catch (Exception)
        {
            gains = new double[bands.Length];
            disc.HasThumb = true;
            disc.Intensity = 0;
        }
        SetBands(gains);
    }

    void SetBands(double[] gains)
    {
        bool previous = updating;
        updating = true;
        for (int i = 0; i < bands.Length; i++) bands[i].Value = gains[i];
        updating = previous;
    }

    void QueueEqualizer()
    {
        eqTimer.Stop();
        eqTimer.Start();
    }

    void SendEqualizer()
    {
        string[] gains = new string[bands.Length];
        for (int i = 0; i < bands.Length; i++) gains[i] = bands[i].Value.ToString("0.0", CultureInfo.InvariantCulture);
        string csv = string.Join(",", gains);

        if (equalizer.IsChecked != true) client.Set("/api/audio/equalizer/enabled/set", "true");
        client.Send("GET /api/audio/thumb_equalizer/value/set?arg=" + csv + ",0,0");

        if (disc.HasThumb)
        {
            csv += "," + disc.Intensity.ToString("0.###", CultureInfo.InvariantCulture)
                + "," + disc.Angle.ToString("0.#", CultureInfo.InvariantCulture);
        }
        try
        {
            File.WriteAllText(EqualizerFile, csv);
        }
        catch (Exception)
        {
            // memorisation facultative
        }
    }

    public void ShowNearTray()
    {
        Rect area = SystemParameters.WorkArea;
        if (ActualHeight > 0)
        {
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        }
        Show();
        UpdateLayout();
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;
        Activate();
    }
}

class TrayIcon
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ZikTray";

    readonly ZikClient client = new ZikClient();
    readonly WinForms.NotifyIcon tray = new WinForms.NotifyIcon();
    readonly ZikWindow window;

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public TrayIcon(bool showWindow)
    {
        window = new ZikWindow(client);

        WinForms.MenuItem startup = new WinForms.MenuItem("Démarrer avec Windows");
        startup.Checked = IsStartupEnabled();
        startup.Click += delegate
        {
            SetStartup(!startup.Checked);
            startup.Checked = IsStartupEnabled();
        };

        tray.ContextMenu = new WinForms.ContextMenu(new[] {
            new WinForms.MenuItem("Ouvrir", delegate { window.ShowNearTray(); }),
            new WinForms.MenuItem("Actualiser", delegate { client.Refresh(); }),
            startup,
            new WinForms.MenuItem("-"),
            new WinForms.MenuItem("Quitter", delegate { Quit(); }),
        });
        tray.MouseClick += delegate(object sender, WinForms.MouseEventArgs e)
        {
            if (e.Button != WinForms.MouseButtons.Left) return;
            // un clic sur l'icone alors que le panneau est ouvert vient de le fermer : ne pas le rouvrir
            if ((DateTime.UtcNow - window.HiddenAt).TotalMilliseconds < 300) return;
            window.ShowNearTray();
        };

        Apply(new ZikState());
        tray.Visible = true;

        client.StateChanged += delegate(ZikState state)
        {
            window.Dispatcher.BeginInvoke((Action)delegate { Apply(state); });
        };
        client.Start();
        if (showWindow) window.ShowNearTray();
    }

    void Quit()
    {
        tray.Visible = false;
        tray.Dispose();
        Application.Current.Shutdown();
    }

    void Apply(ZikState state)
    {
        window.Apply(state);

        Drawing.Icon previous = tray.Icon;
        tray.Icon = DrawIcon(state);
        if (previous != null) previous.Dispose();

        string name = state.Connected && state.Name.Length > 0 ? state.Name : "Parrot Zik";
        string text = name + " - " + (state.Connected ? state.Battery + " %" : "non connecté");
        tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    static Drawing.Icon DrawIcon(ZikState state)
    {
        Drawing.Color back = Drawing.Color.FromArgb(110, 110, 110);
        if (state.Connected)
        {
            back = Drawing.Color.FromArgb(255, 106, 26);
            if (state.Charging) back = Drawing.Color.FromArgb(46, 160, 67);
            else if (state.Battery >= 0 && state.Battery <= 20) back = Drawing.Color.FromArgb(207, 34, 46);
        }
        string text = state.Connected && state.Battery >= 0 ? state.Battery.ToString() : "Z";

        using (Drawing.Bitmap bitmap = new Drawing.Bitmap(32, 32))
        using (Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (Drawing.Drawing2D.GraphicsPath path = RoundedRectangle(new Drawing.Rectangle(0, 0, 31, 31), 7))
            using (Drawing.Brush brush = new Drawing.SolidBrush(back))
            {
                g.FillPath(brush, path);
            }
            float size = text.Length >= 3 ? 14f : 20f;
            using (Drawing.Font font = new Drawing.Font("Segoe UI", size, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            using (Drawing.StringFormat format = new Drawing.StringFormat())
            {
                format.Alignment = Drawing.StringAlignment.Center;
                format.LineAlignment = Drawing.StringAlignment.Center;
                g.DrawString(text, font, Drawing.Brushes.White, new Drawing.RectangleF(-4, 0, 40, 32), format);
            }

            IntPtr handle = bitmap.GetHicon();
            try
            {
                return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
    }

    static Drawing.Drawing2D.GraphicsPath RoundedRectangle(Drawing.Rectangle r, int radius)
    {
        int d = radius * 2;
        Drawing.Drawing2D.GraphicsPath path = new Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static bool IsStartupEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            return key != null && key.GetValue(RunName) != null;
        }
    }

    static void SetStartup(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) key.SetValue(RunName, "\"" + Program.ExecutablePath + "\" --hidden");
            else key.DeleteValue(RunName, false);
        }
    }
}

static class Program
{
    static readonly object LogLock = new object();

    public static string ExecutablePath
    {
        get { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
    }

    static void WriteLog(string message)
    {
        try
        {
            string path = Path.ChangeExtension(ExecutablePath, ".log");
            lock (LogLock)
            {
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // le journal ne doit jamais faire tomber l'app
        }
    }

    // Diagnostic : attend l'etat du casque puis enregistre une image du panneau
    static void Snapshot(string file, string view)
    {
        ZikClient client = new ZikClient();
        ZikState latest = null;
        client.StateChanged += delegate(ZikState state) { if (state.Connected) latest = state; };
        client.Start();
        for (int i = 0; i < 300 && latest == null && view != "demo"; i++) Thread.Sleep(100);

        // Etat fictif pour la capture d'ecran de la documentation
        if (view == "demo")
        {
            latest = new ZikState();
            latest.Connected = true;
            latest.Name = "Parrot ZIK 3";
            latest.Firmware = "3.07";
            latest.Battery = 72;
            latest.NoiseType = "anc";
            latest.NoiseValue = 2;
            latest.Equalizer = true;
            latest.ConcertHall = true;
            latest.Room = "jazz";
            latest.Angle = "90";
            latest.HeadDetection = true;
        }

        ZikWindow window = new ZikWindow(client);
        window.Apply(latest ?? new ZikState());
        if (view == "bands")
        {
            typeof(ZikWindow).GetMethod("ShowEqView", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance).Invoke(window, new object[] { "bands" });
        }
        window.Left = -5000;
        window.Top = -5000;
        window.Show();
        window.Dispatcher.Invoke((Action)delegate { }, DispatcherPriority.ApplicationIdle);

        const double scale = 1.5;
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)(window.ActualWidth * scale),
            (int)(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(file)) encoder.Save(stream);
        WriteLog("Panneau : " + window.ActualWidth + " x " + window.ActualHeight);
    }

    [STAThread]
    static void Main(string[] args)
    {
        bool first;
        using (Mutex mutex = new Mutex(true, "ZikTray.SingleInstance", out first))
        {
            if (!first) return;
            ZikClient.Log = WriteLog;

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                WriteLog("Erreur interface : " + e.Exception);
                e.Handled = true;
            };

            if (args.Length >= 2 && args[0] == "--snapshot")
            {
                Snapshot(args[1], args.Length > 2 ? args[2] : "disc");
                return;
            }

            TrayIcon tray = new TrayIcon(Array.IndexOf(args, "--hidden") < 0);
            app.Run();
            GC.KeepAlive(tray);
        }
    }
}
