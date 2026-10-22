using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LokiHost.Core;

namespace LokiHost;

/// <summary>Loki Host: one window to set up, run and share an rAthena server for friends.</summary>
public sealed class MainWindow : Window
{
    readonly HostSettings _s = HostSettings.Load(Program.SettingsPath);
    readonly HostController _host;

    // Server tab
    readonly Ellipse _ledDb = Led(), _ledLogin = Led(), _ledChar = Led(), _ledMap = Led(), _ledPresence = Led();
    readonly Button _start = new() { Content = "Start server" }, _stop = new() { Content = "Stop server", IsEnabled = false };
    readonly TextBlock _stateText = new() { Text = "Stopped", VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _share = ReadOnlyBox(4);
    readonly ComboBox _shareLang = new() { ItemsSource = ShareText.Languages.Select(ShareText.LanguageName).ToArray() };
    readonly TextBox _log = ReadOnlyBox(0);
    readonly StringBuilder _logText = new();
    // client wave 7 (w7host): what the last Start found about the host's Ragnarok Online install and the server's map cache,
    // and who is connected to the relay with which game data (the players whose data differs from the host's are told once)
    readonly TextBlock _mapData = Wrap("");
    // design note, 2026-10-07: the database backup made at every Start (its time, size and folder), or why it failed
    readonly TextBlock _backup = Wrap("");
    readonly TextBox _players = ReadOnlyBox(3);

    // Ports tab
    readonly TextBlock _routerHint = Wrap("");
    readonly TextBox _portResult = ReadOnlyBox(8);
    readonly TextBox _friendHost = new() { Watermark = "their address, e.g. 203.0.113.7" };
    readonly TextBox _friendPorts = new() { Text = "7900, 7901, 7902" };
    readonly TextBox _friendResult = ReadOnlyBox(4);

    // Accounts tab
    readonly ListBox _accounts = new() { Height = 220 };
    readonly TextBox _newUser = new() { Watermark = "account name" }, _newPass = new() { Watermark = "password", PasswordChar = '•' };
    readonly ComboBox _newSex = new() { ItemsSource = new[] { "M", "F" }, SelectedIndex = 0 };
    readonly CheckBox _newAdmin = new() { Content = "GM (all commands)" };
    readonly TextBox _setPass = new() { Watermark = "new password for the selected account", PasswordChar = '•' };
    readonly CheckBox _allowReg = new() { Content = "Players may create their own account from the login screen (applies at the next start)" };
    readonly TextBlock _accMsg = Wrap("");

    // Setup tab
    readonly TextBox _src = new(), _dst = new(), _dbHost = new(), _dbPort = new(), _dbName = new(), _adminUser = new() { Text = "root" },
        _adminPass = new() { PasswordChar = '•' }, _pLogin = new(), _pChar = new(), _pMap = new(), _pPresence = new(), _name = new(), _mariaBin = new(),
        _roDir = new() { Watermark = "empty: the map data is not followed" }; // client wave 7 (w7host): the host's own Ragnarok Online install, only read
    // the database: Loki Host's own private MariaDB (the design's choice 2026-09-30) or an installed server's admin login
    readonly RadioButton _dbPrivate = new() { GroupName = "lokihost-db", Content = "Its own private MariaDB (recommended: no password needed; your installed MariaDB and its data are never touched)" },
        _dbExisting = new() { GroupName = "lokihost-db", Content = "A database inside an installed MariaDB / MySQL (needs that server's admin login)" };
    bool _dbStartedForAccounts;
    readonly CheckBox _presenceOn = new() { Content = "Relay the players' headsets (friends in VR see each other's head and hands)" };
    // client wave 6 (w6voice): voice on the same relay and port; it needs the relay, so it greys out with the box above
    readonly CheckBox _voiceOn = new() { Content = "Relay the players' voices too (friends hear each other: open microphones with a noise gate, same port)" };
    readonly TextBlock _presenceLedText = new() { Text = "Headsets", VerticalAlignment = VerticalAlignment.Center };
    // client wave 8 (w8core): Horde, the co-op side mode in the relay; its run length and reward size
    readonly CheckBox _hordeOn = new() { Content = "Horde: friends can start a co-op run from the game's wrist menu (it runs in the relay; needs the box above)" };
    readonly ComboBox _hordeMinutes = new() { ItemsSource = new[] { "5 minutes", "10 minutes", "15 minutes", "20 minutes", "30 minutes" } };
    readonly ComboBox _hordeReward = new() { ItemsSource = new[] { "Off (no real EXP)", "Low", "Normal", "High" } };
    static readonly int[] HordeMinuteChoices = { 5, 10, 15, 20, 30 };
    readonly TextBox _hordeInfo = ReadOnlyBox(2);
    // design note, 2026-10-07: "Turn on both, the zeny log and the startup backup"
    readonly CheckBox _zenyLogOn = new() { Content = "Zeny log: record every zeny change (rAthena's zenylog table)" };
    readonly CheckBox _backupOn = new() { Content = "Back up the database at every Start (before the servers start)" };
    readonly CheckBox _textOverhaulOn = new() { Content = "Text overhaul: rewritten NPC dialogs (originals kept)" }; // wave 28 (wtexts)
    readonly Button _setupBtn = new() { Content = "Set up" };
    readonly TextBox _setupLog = ReadOnlyBox(10);

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    int _tick;
    bool _closingAfterStop;

    public MainWindow()
    {
        Title = Program.SettingsPath == null ? "Loki Host" : $"Loki Host ({Program.SettingsPath})";
        Width = 900; Height = 720; MinWidth = 700; MinHeight = 560;
        _host = new HostController(_s);
        if (_s.LoadError != null)
        {
            // never fall back to the defaults (the usual MariaDB and C:\LokiHost) behind the user's back: say so and stop here
            Content = new TextBlock
            {
                Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap, FontSize = 15,
                Text = $"The settings file could not be read:\n{_s.FilePath}\n\n{_s.LoadError}\n\nNothing was changed. Fix the file (or move it away "
                    + "to start over), then open Loki Host again.",
            };
            return;
        }
        _host.Output += (n, l) => Dispatcher.UIThread.Post(() => AppendLog($"[{n.Replace("-server", "")}] {l}"));
        _host.Stopped += (n, code) => Dispatcher.UIThread.Post(() =>
        {
            AppendLog($"!! {n} stopped by itself (exit code {code}). Read its last lines above; press Stop, then Start again.");
            _ = RefreshStatus();
        });

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Server", Content = ServerTab() });
        tabs.Items.Add(new TabItem { Header = "Ports", Content = PortsTab() });
        tabs.Items.Add(new TabItem { Header = "Accounts", Content = AccountsTab() });
        tabs.Items.Add(new TabItem { Header = "Setup", Content = SetupTab() });
        tabs.SelectedIndex = _s.SetUp ? 0 : 3;
        Content = tabs;

        LoadSetupFields();
        var adopted = _host.AdoptRunning();
        if (adopted.Count > 0)
        {
            _stateText.Text = "Running";
            AppendLog($"{string.Join(", ", adopted)} {(adopted.Count > 1 ? "were" : "was")} still running from {_s.HostDir} (an earlier Loki Host "
                + "was closed without stopping them). Their output cannot be shown here; Stop stops them cleanly.");
            _ = _host.StartPresenceAsync(AppendLog); // the headset relay lived in the closed Loki Host
        }
        UpdateShare();
        _timer.Tick += async (_, _) => await RefreshStatus();
        _timer.Start();
        Closing += OnClosing;
        _ = RefreshStatus();
    }

    // ------------------------------------------------------------------------------------------------ Server tab

    Control ServerTab()
    {
        _start.Click += async (_, _) => await StartServer();
        _stop.Click += async (_, _) => await StopServer();
        var leds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var (l, t) in new[] { (_ledDb, "Database"), (_ledLogin, "Login"), (_ledChar, "Char"), (_ledMap, "Map") })
            leds.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { l, new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center } } });
        // the relay's light: "Headsets & voice" while voice is relayed too (client wave 6, w6voice)
        leds.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _ledPresence, _presenceLedText } });
        _presenceLedText.Text = PresenceLedLabel(_s);
        var copy = new Button { Content = "Copy" };
        copy.Click += async (_, _) => await CopyText(_share.Text ?? "");
        // the text in the friends' language (client wave 5, w5friends): chosen once, kept in the settings
        _shareLang.SelectedIndex = Math.Max(0, Array.IndexOf(ShareText.Languages, _s.ShareLanguage));
        _shareLang.SelectionChanged += (_, _) =>
        {
            var lang = ShareText.Languages[Math.Max(0, _shareLang.SelectedIndex)];
            if (lang == _s.ShareLanguage) return;
            _s.ShareLanguage = lang;
            try { _s.Save(); } catch (Exception e) { AppendLog("Could not save the language choice: " + e.Message); }
            UpdateShare();
        };
        var clear = new Button { Content = "Clear log" };
        clear.Click += (_, _) => { _logText.Clear(); _log.Text = ""; };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*"), Margin = new Thickness(12) };
        Add(grid, 0, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _start, _stop, _stateText } });
        Add(grid, 1, new Border { Margin = new Thickness(0, 10), Child = leds });
        // client wave 7 (w7host): the map data line and the players' list
        Avalonia.Automation.AutomationProperties.SetName(_players, "Players");
        _mapData.Text = ShareText.GameDataHint(_s);
        UpdateBackup();
        Add(grid, 2, new StackPanel { Spacing = 4, Children = { _mapData, _backup } });
        Add(grid, 3, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0), Children = { Bold("Players and their game data") } });
        _players.Text = PlayersText();
        // client wave 8 (w8core): under the players, the Horde run being played (its players), or the last one
        Avalonia.Automation.AutomationProperties.SetName(_hordeInfo, "Horde");
        _hordeInfo.Text = HordeText();
        Add(grid, 4, new StackPanel { Spacing = 4, Children = { _players, Bold("Horde"), _hordeInfo } });
        Add(grid, 5, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0), Children = { Bold("What to send your friends"), _shareLang, copy } });
        Add(grid, 6, _share);
        Add(grid, 7, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0), Children = { Bold("Server log"), clear } });
        Add(grid, 8, _log);
        return grid;
    }

    async Task StartServer()
    {
        _start.IsEnabled = false;
        _stateText.Text = "Starting …";
        SaveRoDir(); // (a folder typed on the Setup tab and not yet left)
        try
        {
            await _host.StartAsync(AppendLog);
            _stateText.Text = "Running";
            UpdateShare();
        }
        catch (Exception e)
        {
            AppendLog("Could not start: " + e.Message);
            _stateText.Text = "Stopped";
        }
        UpdateMapData();
        UpdateBackup();
        await RefreshStatus();
    }

    /// <summary>The Server tab's backup line: what this window's last Start did, else the newest backup in the folder.</summary>
    void UpdateBackup()
    {
        var folder = DbBackup.Folder(_s);
        var last = _host.LastBackup;
        string text;
        if (last != null && !last.Ok)
        {
            var good = DbBackup.Latest(folder);
            text = last.Summary + (good != null ? $" Last good backup: {good.Time:yyyy-MM-dd HH:mm:ss}, {DbBackup.Size(good.Bytes)}." : "") + $" Folder: {folder}";
        }
        else if (last != null) text = last.Summary;
        else if (DbBackup.Latest(folder) is { } l) text = l.Summary;
        else text = $"Database backup: none yet{(_s.BackupOnStart ? " (made at every Start)" : " (off: Setup tab)")}. Folder: {folder}";
        if (!_s.BackupOnStart && last == null) text += " Backups are off (Setup tab).";
        _backup.Text = text;
        if (last is { Ok: false }) _backup.Foreground = Brushes.IndianRed; else _backup.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>The Server tab's line about the map data: what the last Start found (MapDataWatch), else what will be done.</summary>
    void UpdateMapData() => _mapData.Text = _host.MapData?.Summary ?? ShareText.GameDataHint(_s);

    /// <summary>The players connected to the relay and their game data against the host's (client wave 7, w7host).</summary>
    string PlayersText()
    {
        var relay = _host.Relay;
        if (relay == null || !relay.Running) return "(the relay is not running: shown while the server runs)";
        var l = relay.Players().OrderBy(p => p.Aid).ToList();
        string head = relay.HostDataStamp == "" ? "Players' game data is not compared (no Ragnarok Online folder set on the Setup tab, or its maps not read yet)."
            : $"The host's game data: {relay.HostDataStamp}{(relay.HostDataLabel != "" ? ", " + relay.HostDataLabel : "")}.";
        if (l.Count == 0) return head + " Nobody is connected.";
        int differ = l.Count(p => p.Data == PoseRelay.DataState.Differs);
        var sb = new StringBuilder(head).Append(differ > 0 ? $" {differ} player(s) with OTHER game data (told in their chat): walking may not match on some maps until both sides are patched." : "").AppendLine();
        foreach (var p in l) sb.AppendLine($"{p.Name} (account {p.Aid}{(p.Map != null ? ", on " + p.Map : "")}): {PoseRelay.DataText(p)}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Horde (client wave 8): the run being played with its players, or the last one.</summary>
    string HordeText()
    {
        if (!_s.HordeEnabled || !_s.PresenceEnabled) return "Horde is off (Setup tab).";
        var h = _host.Horde;
        if (h == null) return $"Horde: runs of {_s.HordeRunMinutes} minutes, real EXP {HordeRewardName(_s.HordeReward)}. Shown while the server runs.";
        var i = h.GetInfo();
        string now = i.Phase switch
        {
            Loki.Horde.HordePhase.Gathering => $"Run {i.RunId}: players are being asked ({string.Join(", ", i.Players.Select(p => p.Name))} in so far).",
            Loki.Horde.HordePhase.Running => $"Run {i.RunId}: {i.Seconds / 60:0.0} of {i.RunLength / 60:0} min, {i.Enemies} monsters; " +
                string.Join("; ", i.Players.Select(p => $"{p.Name} lv {p.Level} job {p.JobId}{(p.Fallen ? " (fallen)" : "")}{(p.Away ? " (link lost)" : "")}")),
            _ => $"No run now ({i.Connected} player(s) can play Horde; runs of {_s.HordeRunMinutes} minutes, real EXP {HordeRewardName(_s.HordeReward)}).",
        };
        return i.LastRun == "" ? now : now + Environment.NewLine + "Last: " + i.LastRun;
    }

    static string HordeRewardName(int r) => r switch { 0 => "off", 1 => "low", 3 => "high", _ => "normal" };

    async Task StopServer()
    {
        _stop.IsEnabled = false;
        _stateText.Text = "Stopping (the servers save first) …";
        await _host.StopAsync(AppendLog);
        _stateText.Text = "Stopped";
        await RefreshStatus();
    }

    async Task RefreshStatus()
    {
        bool checkDb = _s.SetUp && _tick++ % 5 == 0; // the database every ~15 s, the servers every 3 s
        var st = await _host.StatusAsync(checkDb);
        if (checkDb) SetLed(_ledDb, st.Database);
        SetLed(_ledLogin, st.Login);
        SetLed(_ledChar, st.Char);
        SetLed(_ledMap, st.Map);
        if (_s.PresenceEnabled) SetLed(_ledPresence, st.Presence); else _ledPresence.Fill = Brushes.Gray;
        var players = PlayersText();
        if (_players.Text != players) _players.Text = players; // (only when it changed: a selection in the box survives the timer)
        var horde = HordeText();
        if (_hordeInfo.Text != horde) _hordeInfo.Text = horde;
        bool running = _host.AnyRunning;
        _start.IsEnabled = !running && _s.SetUp && _stateText.Text != "Starting …";
        _stop.IsEnabled = running && !(_stateText.Text?.StartsWith("Stopping") ?? false);
        if (!running && _stateText.Text == "Running") _stateText.Text = "Stopped";
    }

    void UpdateShare()
    {
        var (lan, _) = Net.LanAddress();
        var vpn = _host.PublicIp == "" ? Net.VpnAdapter() : _host.Vpn;
        // client wave 5 (w5friends): the steps a friend takes in Loki itself, in English or Portuguese (Core/ShareText.cs)
        var rules = Database.LoginRules.Read(_s.HostDir);
        _share.Text = ShareText.Build(_s, _host.PublicIp, lan, vpn, _s.ShareLanguage, rules.NameMin, rules.PassMin);
        _routerHint.Text = lan == ""
            ? "No network with a router was found on this PC."
            : $"On your router, forward TCP ports {string.Join(", ", _s.Ports)} (or the range {_s.LoginPort}-{_s.LastPort}) to this PC: {lan}. "
              + (_s.PresenceEnabled ? ShareText.RelayPortHint(_s) + " " : "")
              + "Windows Firewall must let them in too (button below, or answer \"Allow\" when Windows asks at the first start).";
    }

    bool _logRefreshPending;

    void AppendLog(string line)
    {
        _logText.AppendLine(line);
        if (_logText.Length > 200_000) _logText.Remove(0, _logText.Length - 150_000);
        // one refresh of the box for a burst of lines, not one per line: the box holds up to 200,000 characters, and setting its
        // text once per line kept the window's thread busy for seconds while the servers started or a player logged in (client
        // wave 7: the fifth Start in one window took 57 s, the first 8 s)
        if (_logRefreshPending) return;
        _logRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _logRefreshPending = false;
            _log.Text = _logText.ToString();
            _log.CaretIndex = _log.Text.Length;
        }, DispatcherPriority.Background);
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        bool dbOnly = !_host.AnyRunning && _dbStartedForAccounts && _host.Db != null && _host.Db.Answers();
        if (_closingAfterStop || (!_host.AnyRunning && !dbOnly)) return;
        e.Cancel = true;
        _closingAfterStop = true;
        if (_host.AnyRunning) await StopServer(); else await _host.Db!.StopAsync(AppendLog);
        Close();
    }

    // ------------------------------------------------------------------------------------------------ Ports tab

    Control PortsTab()
    {
        var test = new Button { Content = "Test my ports from the internet" };
        test.Click += async (_, _) => await TestMyPorts(test);
        var fwAdd = new Button { Content = "Add Windows Firewall rule (asks for admin)" };
        fwAdd.Click += (_, _) => RunFirewall(Net.FirewallAddArgs(_s));
        var fwDel = new Button { Content = "Remove that rule" };
        fwDel.Click += (_, _) => RunFirewall(Net.FirewallDeleteArgs(_s));
        var friendTest = new Button { Content = "Test" };
        friendTest.Click += async (_, _) => await TestFriend(friendTest);
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(12), Spacing = 10,
                Children =
                {
                    Bold("1. Router"), _routerHint,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { fwAdd, fwDel } },
                    Bold("2. Check"), Wrap("Opens the ports on this PC (the servers, or temporary listeners if they are stopped) and asks a public port checker "
                        + "(portchecker.io) to connect from the internet. If that service cannot be reached the result is \"unknown\": then ask a friend to use "
                        + "\"Test a server\" below with your address. That test is the definitive one."),
                    test, _portResult,
                    Bold("Test a server (for example a friend's)"),
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,160,Auto"),
                        Children = { _friendHost, Col(_friendPorts, 1), Col(friendTest, 2) },
                    },
                    _friendResult,
                },
            },
        };
    }

    async Task TestMyPorts(Button b)
    {
        b.IsEnabled = false;
        _portResult.Text = "Testing …";
        var sb = new StringBuilder();
        try
        {
            var (ip, why) = await Net.PublicIpAsync();
            if (ip == null) { _portResult.Text = "Could not find your public address: " + why; return; }
            sb.AppendLine($"Your public address: {ip}");
            if (Net.VpnAdapter() is string vpn) sb.AppendLine("WARNING: " + Net.VpnWarning(vpn));
            using var temp = await Net.TempListeners.OpenAsync(_s.Ports);
            foreach (var (port, err) in temp.Failed) sb.AppendLine($"Port {port}: could not listen on it on this PC ({err}): another program uses it.");
            var (res, note) = await Net.ExternalCheckAsync(ip, _s.Ports);
            foreach (var p in _s.Ports)
                sb.AppendLine($"Port {p}: " + res[p] switch
                {
                    Net.PortState.Open => "OPEN from the internet",
                    Net.PortState.Closed => "CLOSED from the internet (router forwarding or Windows Firewall)",
                    _ => "unknown",
                });
            if (note != "") sb.AppendLine("Internet check: " + note + " → ask a friend to run \"Test a server\" with " + ip);
            // informational: some routers let the PC reach its own public address (NAT loopback), many do not
            var hair = await Net.CanConnectAsync(ip, _s.LoginPort, 2500);
            sb.AppendLine(hair ? $"(This PC could also reach {ip}:{_s.LoginPort} through the router.)" : "");
            if (res.Values.Any(v => v == Net.PortState.Closed))
                sb.AppendLine("If CLOSED: check the router forwards TCP to " + Net.LanAddress().ip + ", the Windows Firewall rule, and that your provider gives you a real public address "
                    + "(some use carrier-grade NAT: then port forwarding cannot work; ask them for a public IPv4 address).");
        }
        catch (Exception e) { sb.AppendLine("Test failed: " + e.Message); }
        finally { b.IsEnabled = true; }
        _portResult.Text = sb.ToString();
    }

    async Task TestFriend(Button b)
    {
        var host = (_friendHost.Text ?? "").Trim();
        var ports = (_friendPorts.Text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n is > 0 and < 65536).ToArray();
        if (host == "" || ports.Length == 0) { _friendResult.Text = "Enter an address and the ports."; return; }
        b.IsEnabled = false;
        _friendResult.Text = "Testing …";
        var sb = new StringBuilder();
        foreach (var p in ports)
            sb.AppendLine($"{host}:{p}: " + (await Net.CanConnectAsync(host, p, 4000) ? "OPEN (something answers)" : "CLOSED / no answer"));
        b.IsEnabled = true;
        _friendResult.Text = sb.ToString();
    }

    void RunFirewall(string args)
    {
        if (!OperatingSystem.IsWindows()) { _portResult.Text = "Windows only."; return; }
        try
        {
            var p = Process.Start(new ProcessStartInfo("netsh", args) { UseShellExecute = true, Verb = "runas" });
            p?.WaitForExit();
            _portResult.Text = p?.ExitCode == 0 ? "Windows Firewall rule done: " + Net.FirewallRuleName(_s) : $"netsh ended with code {p?.ExitCode}.";
        }
        catch (System.ComponentModel.Win32Exception) { _portResult.Text = "Cancelled (admin permission was not given)."; }
    }

    // ------------------------------------------------------------------------------------------------ Accounts tab

    Control AccountsTab()
    {
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += (_, _) => LoadAccounts();
        var add = new Button { Content = "Add account" };
        add.Click += (_, _) => AccountAction(() =>
        {
            Database.AddAccount(Database.ConnStr(_s), (_newUser.Text ?? "").Trim(), _newPass.Text ?? "",
                (_newSex.SelectedItem as string ?? "M")[0], _newAdmin.IsChecked == true ? 99 : 0, Database.LoginRules.Read(_s.HostDir));
            _newUser.Text = _newPass.Text = "";
            return "Account added. Tell your friend its name and password yourself (not in a public channel); the Server tab's text says how to connect.";
        });
        var setPw = new Button { Content = "Set password" };
        setPw.Click += (_, _) => AccountAction(() =>
        {
            Database.SetPassword(Database.ConnStr(_s), SelectedId(), _setPass.Text ?? "", Database.LoginRules.Read(_s.HostDir));
            _setPass.Text = "";
            return "Password changed.";
        });
        var mkGm = new Button { Content = "Make GM" };
        mkGm.Click += (_, _) => AccountAction(() => { Database.SetGroup(Database.ConnStr(_s), SelectedId(), 99); return "Now a GM (applies at their next login)."; });
        var mkPl = new Button { Content = "Make player" };
        mkPl.Click += (_, _) => AccountAction(() => { Database.SetGroup(Database.ConnStr(_s), SelectedId(), 0); return "Now a normal player (applies at their next login)."; });
        _allowReg.IsChecked = _s.AllowRegistration;
        _allowReg.IsCheckedChanged += (_, _) => { _s.AllowRegistration = _allowReg.IsChecked == true; _s.Save(); UpdateShare(); };
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(12), Spacing = 10,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Bold("Accounts"), refresh } },
                    _accounts,
                    new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,70,Auto,Auto"),
                        Children = { _newUser, Col(_newPass, 1), Col(_newSex, 2), Col(_newAdmin, 3), Col(add, 4) } },
                    new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
                        Children = { _setPass, Col(setPw, 1), Col(mkGm, 2), Col(mkPl, 3) } },
                    _accMsg,
                    _allowReg,
                    Wrap("With self-registration on, anyone who can reach your server can make an account (rAthena's _M / _F rule, "
                        + "one per 10 s), and the new account's password crosses the internet unencrypted that once (rAthena's login has no "
                        + "other way). Off is safer on the internet: make your friends' accounts here."),
                    Wrap("Names: letters A-Z, digits and _ - . (what Loki's login screen types), not ending in _M / _F. Passwords: letters, "
                        + "digits and English-keyboard symbols, no spaces or accents."),
                },
            },
        };
    }

    int SelectedId() =>
        _accounts.SelectedItem is AccountItem a ? a.Id : throw new InvalidOperationException("Select an account in the list first.");

    async void AccountAction(Func<string> f)
    {
        try { await EnsureDbForAccounts(); _accMsg.Text = f(); LoadAccounts(); }
        catch (Exception e) { _accMsg.Text = e.Message; }
    }

    /// <summary>Loki Host's own database, started for the Accounts tab while the servers are stopped (stopped again on close).</summary>
    async Task EnsureDbForAccounts()
    {
        if (await _host.EnsureDatabaseAsync(l => Dispatcher.UIThread.Post(() => _accMsg.Text = l))) _dbStartedForAccounts = true;
    }

    sealed record AccountItem(int Id, string Text) { public override string ToString() => Text; }

    async void LoadAccounts()
    {
        if (!_s.SetUp) { _accMsg.Text = "Run the setup first."; return; }
        try
        {
            await EnsureDbForAccounts();
            _accounts.ItemsSource = Database.Accounts(Database.ConnStr(_s)).Select(a => new AccountItem(a.Id,
                $"{a.User}   ({a.Sex}{(a.Group >= 99 ? ", GM" : a.Group > 0 ? $", group {a.Group}" : "")}{(a.State != 0 ? ", blocked" : "")})   logins: {a.LoginCount}   last: {a.LastLogin}")).ToList();
        }
        catch (Exception e) { _accMsg.Text = "Could not read the accounts: " + e.Message; }
    }

    // ------------------------------------------------------------------------------------------------ Setup tab

    Control SetupTab()
    {
        var browseSrc = new Button { Content = "Browse…" };
        browseSrc.Click += async (_, _) => await Browse(_src, "The folder of your built rAthena (with login-server.exe)");
        var browseDst = new Button { Content = "Browse…" };
        browseDst.Click += async (_, _) => await Browse(_dst, "An empty folder for the host's own copy");
        _setupBtn.Click += async (_, _) => await RunSetup();
        _pPresence.LostFocus += (_, _) => { if (_pPresence.Text != _s.PresencePort.ToString()) SavePresenceFields(); };
        _presenceOn.IsCheckedChanged += (_, _) => { if (_presenceOn.IsChecked != _s.PresenceEnabled) SavePresenceFields(); };
        _voiceOn.IsCheckedChanged += (_, _) => { if (_voiceOn.IsChecked != _s.VoiceEnabled) SavePresenceFields(); };
        _hordeOn.IsCheckedChanged += (_, _) => { if (_hordeOn.IsChecked != _s.HordeEnabled) SavePresenceFields(); };
        _zenyLogOn.IsCheckedChanged += (_, _) => { if (_zenyLogOn.IsChecked != _s.LogZeny) SavePresenceFields(); };
        _backupOn.IsCheckedChanged += (_, _) => { if (_backupOn.IsChecked != _s.BackupOnStart) SavePresenceFields(); };
        _textOverhaulOn.IsCheckedChanged += (_, _) => { if (_textOverhaulOn.IsChecked != _s.TextOverhaul) SavePresenceFields(); };
        _hordeMinutes.SelectionChanged += (_, _) => { if (_loaded) SavePresenceFields(); };
        _hordeReward.SelectionChanged += (_, _) => { if (_loaded) SavePresenceFields(); };
        var browseRo = new Button { Content = "Browse…" };
        browseRo.Click += async (_, _) => { await Browse(_roDir, "Your Ragnarok Online folder (the one with data.grf)"); SaveRoDir(); };
        _roDir.LostFocus += (_, _) => SaveRoDir();
        var update = new Button { Content = "Copy the server programs again (after rebuilding rAthena)" };
        update.Click += (_, _) =>
        {
            try { SaveSetupFields(); RathenaCopy.UpdatePrograms(_s.SourceDir, _s.HostDir, l => _setupLog.Text += l + "\n"); }
            catch (Exception e) { _setupLog.Text += e.Message + "\n"; }
        };
        // 260: "MariaDB admin password (not saved)" was clipped at 220 on Windows (Segoe UI, 100% scale)
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,Auto") };
        int r = 0;
        void Row(string label, Control c, Control? extra = null)
        {
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(l, r); Grid.SetRow(c, r); Grid.SetColumn(c, 1);
            l.Margin = new Thickness(0, 3); c.Margin = new Thickness(8, 3, 0, 3);
            form.Children.Add(l); form.Children.Add(c);
            if (extra != null) { Grid.SetRow(extra, r); Grid.SetColumn(extra, 2); extra.Margin = new Thickness(8, 3, 0, 3); form.Children.Add(extra); }
            r++;
        }
        Row("rAthena build to copy", _src, browseSrc);
        Row("Folder for the host's copy", _dst, browseDst);
        Row("Server name (in the server list)", _name);
        Row("Ports: login", _pLogin);
        Row("Ports: char", _pChar);
        Row("Ports: map", _pMap);
        Row("Ports: headsets and voice (relay)", _pPresence);
        Row("Your Ragnarok Online folder (data.grf)", _roDir, browseRo);
        var browseBin = new Button { Content = "Browse…" };
        browseBin.Click += async (_, _) => await Browse(_mariaBin, "MariaDB's bin folder (with mariadbd.exe and mariadb-install-db.exe)");
        Row("MariaDB programs (for the private one)", _mariaBin, browseBin);
        Row("Database port", _dbPort);
        Row("New database name", _dbName);
        Row("Installed MariaDB: address", _dbHost);
        Row("Installed MariaDB: admin user", _adminUser);
        Row("Its CURRENT admin password", _adminPass);
        _dbPrivate.IsCheckedChanged += (_, _) => UpdateDbFields();
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(12), Spacing = 10,
                Children =
                {
                    Wrap("Makes a separate copy of your rAthena build with its own database, so your other rAthena and its data are never touched. "
                        + "Needs a built rAthena (login-server.exe, char-server.exe, map-server.exe) and MariaDB installed on this PC."),
                    Bold("Database"), _dbPrivate, _dbExisting,
                    Wrap("With an installed MariaDB, the admin password is the one that account ALREADY has (for root: chosen when MariaDB was "
                        + "installed), not a new one; it is used once, to create the database and its own user, and never saved."),
                    form,
                    _presenceOn,
                    _voiceOn,
                    _zenyLogOn,
                    _backupOn,
                    _textOverhaulOn,
                    Wrap($"Backups go to {DbBackup.Folder(_s)}, one file per Start. Loki Host never removes old backups: delete them yourself "
                        + "when the folder grows. Both used at the next Start."),
                    _hordeOn,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(24, 0, 0, 0),
                        Children = { new TextBlock { Text = "A run lasts", VerticalAlignment = VerticalAlignment.Center }, _hordeMinutes,
                                     new TextBlock { Text = "Real EXP at the end", VerticalAlignment = VerticalAlignment.Center }, _hordeReward } },
                    Wrap("Horde's real EXP is a share of what each character needs for its next base / job level, decided by Loki Host at the "
                        + "run's end and given once by a script Loki Host installs into its own copy (a character that is offline then gets it "
                        + "when it is next online). Used at the next Start."),
                    Wrap("Your Ragnarok Online folder is only read. At every Start Loki Host looks whether the official patcher changed it and, if the maps "
                        + "changed, rebuilds the server's map data from them (the old file is kept beside the new one), so the server walks the maps you "
                        + "and your friends see; friends whose game data differs from yours are told in their chat and listed on the Server tab. "
                        + "Editable after the setup; used at the next Start."),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _setupBtn, update } },
                    _setupLog,
                },
            },
        };
    }

    void LoadSetupFields()
    {
        _src.Text = _s.SourceDir; _dst.Text = _s.HostDir; _name.Text = _s.ServerName;
        _pLogin.Text = _s.LoginPort.ToString(); _pChar.Text = _s.CharPort.ToString(); _pMap.Text = _s.MapPort.ToString();
        _pPresence.Text = _s.PresencePort.ToString(); _presenceOn.IsChecked = _s.PresenceEnabled;
        _roDir.Text = _s.RoDir;
        _voiceOn.IsChecked = _s.VoiceEnabled; _voiceOn.IsEnabled = _s.PresenceEnabled;
        _loaded = false;
        _hordeOn.IsChecked = _s.HordeEnabled; _hordeOn.IsEnabled = _s.PresenceEnabled;
        _zenyLogOn.IsChecked = _s.LogZeny; _backupOn.IsChecked = _s.BackupOnStart; _textOverhaulOn.IsChecked = _s.TextOverhaul;
        int mi = Array.IndexOf(HordeMinuteChoices, _s.HordeRunMinutes);
        _hordeMinutes.SelectedIndex = mi >= 0 ? mi : 2;
        _hordeReward.SelectedIndex = Math.Clamp(_s.HordeReward, 0, 3);
        _loaded = true;
        _dbHost.Text = _s.DbHost; _dbName.Text = _s.DbName;
        // a new setup offers the private database first; a finished one shows what it uses
        if (_s.SetUp ? _s.PrivateDb : (_s.PrivateDb || _dbExisting.IsChecked != true)) _dbPrivate.IsChecked = true; else _dbExisting.IsChecked = true;
        _mariaBin.Text = PrivateDb.FindBin(_s.MariaDbBin) ?? "";
        _mariaBin.Watermark = "not found: choose MariaDB's bin folder";
        foreach (var c in new Control[] { _src, _dst, _pLogin, _pChar, _pMap, _dbHost, _dbPort, _dbName, _dbPrivate, _dbExisting, _mariaBin }) c.IsEnabled = !_s.SetUp;
        UpdateDbFields();
        _setupBtn.IsEnabled = !_s.SetUp;
        _friendPorts.Text = string.Join(", ", _s.Ports);
        // appended: right after a setup the box still holds its step-by-step log
        if (_s.SetUp) _setupLog.Text = (_setupLog.Text ?? "") + $"Set up: {_s.HostDir}, database {_s.DbName}. (To start over, choose a new "
            + $"folder and database: move {_s.FilePath ?? HostSettings.DefaultPath} away yourself.)\n";
        else LoadAccounts();
    }

    void SaveSetupFields()
    {
        static int Port(TextBox t, string what) =>
            int.TryParse(t.Text, out var n) && n is > 0 and < 65536 ? n : throw new ArgumentException($"{what}: not a port number");
        _s.SourceDir = (_src.Text ?? "").Trim(); _s.HostDir = (_dst.Text ?? "").Trim();
        _s.ServerName = string.IsNullOrWhiteSpace(_name.Text) ? "Loki" : _name.Text.Trim();
        if (!_s.SetUp)
        {
            _s.LoginPort = Port(_pLogin, "login port"); _s.CharPort = Port(_pChar, "char port"); _s.MapPort = Port(_pMap, "map port");
            _s.DbName = (_dbName.Text ?? "").Trim();
            _s.PrivateDb = _dbPrivate.IsChecked == true;
            if (_s.PrivateDb)
            {
                _s.PrivateDbPort = Port(_dbPort, "database port");
                _s.DbHost = "127.0.0.1"; _s.DbPort = _s.PrivateDbPort;
                var bin = (_mariaBin.Text ?? "").Trim();
                if (!PrivateDb.IsBinDir(bin)) throw new ArgumentException("MariaDB's programs were not found: choose its bin folder (with mariadbd.exe and mariadb-install-db.exe)");
                _s.MariaDbBin = bin;
            }
            else { _s.DbHost = (_dbHost.Text ?? "").Trim(); _s.DbPort = Port(_dbPort, "database port"); }
        }
        SaveRoDir();
        SavePresenceFields();
    }

    /// <summary>Private database: its port in the port field, the installed server's address and admin login greyed out.</summary>
    void UpdateDbFields()
    {
        bool priv = _dbPrivate.IsChecked == true;
        _dbPort.Text = (priv ? (_s.PrivateDb || !_s.SetUp ? _s.PrivateDbPort : _s.DbPort) : (_s.PrivateDb ? 3306 : _s.DbPort)).ToString();
        bool editable = !_s.SetUp;
        _dbHost.IsEnabled = editable && !priv; _adminUser.IsEnabled = editable && !priv; _adminPass.IsEnabled = editable && !priv;
        _mariaBin.IsEnabled = editable && priv;
    }

    /// <summary>The host's Ragnarok Online folder (client wave 7, w7host): not part of rAthena's configuration, so it stays editable
    /// after the setup; saved as soon as it changes, used at the next Start. A folder without data.grf is refused (the field goes
    /// back to what it was): Loki Host would have nothing to read there.</summary>
    void SaveRoDir()
    {
        string dir = (_roDir.Text ?? "").Trim();
        if (dir == _s.RoDir) return;
        if (dir != "" && !File.Exists(System.IO.Path.Combine(dir, "data.grf")))
        {
            _setupLog.Text += $"{dir} has no data.grf: choose the folder of your Ragnarok Online install (the LATAM installer makes a \"Ragnarok\" folder inside the one you picked).\n";
            _roDir.Text = _s.RoDir;
            return;
        }
        _s.RoDir = dir;
        try { _s.Save(); } catch (Exception e) { _setupLog.Text += "Could not save the Ragnarok Online folder: " + e.Message + "\n"; return; }
        _setupLog.Text += (dir == "" ? "Ragnarok Online folder cleared: the map data is not followed." : $"Ragnarok Online folder: {dir}. Its maps are checked at the next Start.") + "\n";
        if (_host.MapData == null) _mapData.Text = ShareText.GameDataHint(_s);
        UpdateShare();
    }

    /// <summary>"Headsets", or "Headsets &amp; voice" while the relay forwards voice too.</summary>
    bool _loaded; // (the Horde boxes' SelectionChanged fires while LoadSetupFields fills them: not a user's change)

    static string PresenceLedLabel(HostSettings s) => s.PresenceEnabled && s.VoiceEnabled ? "Headsets & voice" : "Headsets";

    /// <summary>The headset relay's port and switches (headsets, voice): not part of rAthena's configuration, so they stay editable
    /// after the setup; saved as soon as they change, used at the next Start.</summary>
    void SavePresenceFields()
    {
        if (int.TryParse(_pPresence.Text, out var n) && n is > 0 and < 65536) _s.PresencePort = n;
        else _pPresence.Text = _s.PresencePort.ToString();
        _s.PresenceEnabled = _presenceOn.IsChecked == true;
        _s.VoiceEnabled = _voiceOn.IsChecked == true;
        _voiceOn.IsEnabled = _s.PresenceEnabled;
        _s.HordeEnabled = _hordeOn.IsChecked == true;
        _hordeOn.IsEnabled = _s.PresenceEnabled;
        if (_hordeMinutes.SelectedIndex >= 0) _s.HordeRunMinutes = HordeMinuteChoices[_hordeMinutes.SelectedIndex];
        if (_hordeReward.SelectedIndex >= 0) _s.HordeReward = _hordeReward.SelectedIndex;
        _s.LogZeny = _zenyLogOn.IsChecked == true;
        _s.BackupOnStart = _backupOn.IsChecked == true;
        _s.TextOverhaul = _textOverhaulOn.IsChecked == true;
        _presenceLedText.Text = PresenceLedLabel(_s);
        _s.Save();
        _friendPorts.Text = string.Join(", ", _s.Ports);
        UpdateShare();
        UpdateBackup();
    }

    async Task RunSetup()
    {
        _setupBtn.IsEnabled = false;
        _setupLog.Text = "";
        void Log(string l) => Dispatcher.UIThread.Post(() => _setupLog.Text += l + "\n");
        try
        {
            SaveSetupFields();
            string au = _s.PrivateDb ? "" : _adminUser.Text ?? "", ap = _s.PrivateDb ? "" : _adminPass.Text ?? "";
            await Task.Run(() => HostSetup.Run(_s, au, ap, Log));
            _adminPass.Text = "";
            LoadSetupFields();
            UpdateShare();
            LoadAccounts();
            Log("Next: Ports tab (router + test), Accounts tab (your friends' accounts), then Start on the Server tab.");
        }
        catch (Exception e)
        {
            Log("Setup stopped: " + e.Message);
            Log("Nothing was deleted. Fix the cause and press Set up again (an unfinished copy of this setup is reused).");
            _setupBtn.IsEnabled = !_s.SetUp;
        }
        await RefreshStatus();
    }

    async Task Browse(TextBox target, string title)
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        var path = r.Count > 0 ? r[0].TryGetLocalPath() : null;
        if (path != null) target.Text = path;
    }

    // ------------------------------------------------------------------------------------------------ helpers

    async Task CopyText(string text)
    {
        var cb = Clipboard;
        if (cb != null) await cb.SetTextAsync(text);
    }

    static Ellipse Led() => new() { Width = 14, Height = 14, Fill = Brushes.Gray };
    static void SetLed(Ellipse e, bool on) => e.Fill = on ? Brushes.LimeGreen : Brushes.IndianRed;
    static TextBlock Bold(string t) => new() { Text = t, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
    static TextBlock Wrap(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap };

    static TextBox ReadOnlyBox(int lines) => new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12,
        MinHeight = lines > 0 ? lines * 18 : 0,
    };

    static void Add(Grid g, int row, Control c) { Grid.SetRow(c, row); g.Children.Add(c); }
    /// <summary>A control in grid column <paramref name="col"/>, 8 px from the previous column.</summary>
    static Control Col(Control c, int col) { Grid.SetColumn(c, col); c.Margin = new Thickness(col > 0 ? 8 : 0, 0, 0, 0); return c; }
}
