namespace KeyboardShare;
internal static class Log
{
    static readonly object Gate = new();
    public static void Write(string message)
    {
        try { lock (Gate) { Directory.CreateDirectory(Config.DirectoryPath); string path = Path.Combine(Config.DirectoryPath, "app.log"); if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true); File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } } catch { }
    }
}
internal static class Program
{
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Local\\KeyboardShare.Tray", out bool first);
        if (!first) { MessageBox.Show("KeyboardShare is already running in this Windows session."); return; }
        Application.ThreadException += (_, e) => { Log.Write(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "KeyboardShare"); };
        try { Application.Run(new TrayContext()); }
        catch (Exception ex) { Log.Write(ex.ToString()); MessageBox.Show(ex.Message, "KeyboardShare could not start"); }
    }
}
internal sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly Control dispatcher = new();
    Network? network;
    KeyboardRouter? router;
    Config config;
    bool closing;
    public TrayContext()
    {
        dispatcher.CreateControl();
        try { config = Config.Load(); } catch (Exception ex) { Log.Write(ex.Message); config = new(); }
        tray = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "KeyboardShare — Disconnected" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Keyboard → Laptop 1", null, (_, _) => router?.Select(false));
        menu.Items.Add("Keyboard → Laptop 2", null, (_, _) => router?.Select(true));
        menu.Items.Add("Settings…", null, (_, _) => Settings());
        menu.Items.Add("Open logs", null, (_, _) => { Directory.CreateDirectory(Config.DirectoryPath); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Config.DirectoryPath) { UseShellExecute = true }); });
        menu.Items.Add("Exit", null, (_, _) => ExitThread()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Settings();
        if (!File.Exists(Config.FilePath)) Settings(); else Start();
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
    }
    void SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e) { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) Post(StopForSession); else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) Post(Start); }
    void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Suspend) Post(StopForSession); else if (e.Mode == Microsoft.Win32.PowerModes.Resume) Post(Start); }
    void StopForSession() { Stop(); Update(); }
    void Post(Action action) { if (!closing && !dispatcher.IsDisposed) { try { dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { } } }
    void Stop() { router?.Dispose(); router = null; network?.Dispose(); network = null; }
    void Start()
    {
        Stop();
        try
        {
            config.Validate(); network = new Network(config); var current = network;
            network.Changed += () => Post(() => { if (network != current) return; if (!current.Connected) router?.Select(false); Update(); });
            if (config.Mode == "Host") { router = new KeyboardRouter(config, network.Send, () => current.Connected); router.Changed += () => { current.SelectTarget(router?.Remote == true); Update(); }; }
            _ = network.Run().ContinueWith(t => { if (t.Exception != null) Log.Write(t.Exception.ToString()); }, TaskScheduler.Default); Update();
        }
        catch (Exception ex) { Stop(); MessageBox.Show(ex.Message, "KeyboardShare"); Update(); }
    }
    void Update()
    {
        string state = network?.Connected == true ? "Connected" : "Disconnected";
        string target = (config.Mode == "Client" ? network?.TargetRemote == true : router?.Remote == true) ? "Keyboard → Laptop 2" : "Keyboard → Laptop 1";
        tray.Text = $"{state} | {target}";
        tray.Icon = network?.Connected == true ? (router?.Remote == true ? SystemIcons.Warning : SystemIcons.Information) : SystemIcons.Error;
        tray.ContextMenuStrip!.Items[0].Enabled = config.Mode == "Host";
        tray.ContextMenuStrip.Items[1].Enabled = config.Mode == "Host" && network?.Connected == true;
    }
    void Settings()
    {
        router?.Select(false);
        using var form = new SettingsForm(config);
        if (form.ShowDialog() == DialogResult.OK) { config = form.Result!; Start(); }
    }
    protected override void ExitThreadCore()
    {
        closing = true; Microsoft.Win32.SystemEvents.SessionSwitch -= SessionSwitch; Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged;
        Stop(); tray.Visible = false; tray.Dispose(); dispatcher.Dispose(); base.ExitThreadCore();
    }
}
internal sealed class SettingsForm : Form
{
    public Config? Result { get; private set; }
    public SettingsForm(Config config)
    {
        Text = "KeyboardShare settings"; ClientSize = new Size(550, 510); MinimumSize = new Size(400, 300); StartPosition = FormStartPosition.CenterScreen;
        // Keep actions outside the scrolling fields so DPI scaling cannot hide Save.
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        layout.Controls.Add(scroll, 0, 0);
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(12), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink }; grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroll.Controls.Add(grid);
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill }; mode.Items.AddRange(["Host", "Client"]); mode.SelectedItem = config.Mode;
        TextBox Box(string text, bool secret = false) => new() { Text = text, Dock = DockStyle.Fill, UseSystemPasswordChar = secret };
        var peer = Box(config.PeerIP); var bind = Box(config.BindIP); var port = Box(config.Port.ToString()); var token = Box(config.Token, true); var local = Box(config.LocalHotkey); var remote = Box(config.RemoteHotkey);
        void Row(string label, Control input) { grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); grid.Controls.Add(input); }
        Row("Mode", mode); Row("Other laptop IP", peer); Row("Host listen IP", bind); Row("TCP port", port); Row("Shared token", token); Row("Laptop 1 hotkey", local); Row("Laptop 2 hotkey", remote);
        var show = new CheckBox { Text = "Show token", AutoSize = true }; show.CheckedChanged += (_, _) => token.UseSystemPasswordChar = !show.Checked; Row("", show);
        Row("Emergency (fixed)", new Label { Text = "Control+Alt+Escape", AutoSize = true });
        var help = new Label { Text = "Host listens; Client connects to Host. Enter the OTHER laptop’s IP on each. Copy the same random token to both. Settings are stored locally for your Windows account.", AutoSize = true, MaximumSize = new Size(470, 0) }; grid.Controls.Add(help); grid.SetColumnSpan(help, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), WrapContents = false };
        layout.Controls.Add(actions, 0, 1);
        var save = new Button { Text = "Save and start", AutoSize = true }; actions.Controls.Add(save); AcceptButton = save;
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel }; actions.Controls.Add(cancel); CancelButton = cancel;
        Shown += (_, _) => { var area = Screen.FromControl(this).WorkingArea; if (Height > area.Height) Height = area.Height; if (Width > area.Width) Width = area.Width; Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height))); };
        save.Click += (_, _) => { try { var c = new Config { Mode = mode.Text, PeerIP = peer.Text.Trim(), BindIP = bind.Text.Trim(), Port = int.Parse(port.Text), Token = token.Text, LocalHotkey = local.Text, RemoteHotkey = remote.Text }; c.Save(); Result = c; DialogResult = DialogResult.OK; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Check settings"); } };
    }
}
