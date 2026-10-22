namespace LokiHost.Core;

/// <summary>Starts and stops the three servers of the host copy in rAthena's order, and reports their state.</summary>
public sealed class HostController
{
    public HostSettings Settings { get; }
    public ServerProcess Login { get; } = new("login-server");
    public ServerProcess Char { get; } = new("char-server");
    public ServerProcess Map { get; } = new("map-server");
    public IEnumerable<ServerProcess> All => new[] { Login, Char, Map };
    /// <summary>The headset pose relay while it runs (started after the map server, stopped with the servers), else null.</summary>
    public PoseRelay? Relay { get; private set; }
    /// <summary>Client wave 8 (w8core): Horde inside the relay while it runs (HostSettings.HordeEnabled), else null.</summary>
    public HordeService? Horde { get; private set; }
    public const string RelayName = "presence";
    public const string DbName = "database";

    /// <summary>(server, line): every console line of the three servers.</summary>
    public event Action<string, string>? Output;
    /// <summary>A server stopped by itself (crash, fatal error, closed console).</summary>
    public event Action<string, int>? Stopped;

    public string PublicIp { get; private set; } = "";
    public string LanIp { get; private set; } = "";
    /// <summary>Loki Host's own MariaDB instance, when the settings use one (HostSettings.PrivateDb).</summary>
    public PrivateDb? Db => Settings.PrivateDb ? (_db ??= NewDb()) : null; // follows the setting: the Setup tab chooses it after the window opened
    PrivateDb? _db;
    PrivateDb NewDb() { var d = new PrivateDb(Settings); d.Log += l => { Output?.Invoke(DbName, l); AppendLog(DbName, l); }; return d; }
    /// <summary>The VPN adapter that carried the internet traffic at the last start (then PublicIp is the VPN's), or null.</summary>
    public string? Vpn { get; private set; }
    /// <summary>Client wave 7 (w7host): what the last Start found when it compared the host's Ragnarok Online install with the
    /// server's map cache (MapDataWatch); null before the first Start of this window.</summary>
    public MapDataWatch.Outcome? MapData { get; private set; }
    /// <summary>How the map-data check waits for a patch in progress, and where the @go points are read from (tests set it).</summary>
    public MapDataWatch.Options MapDataOptions { get; set; } = new();
    bool _stopping;

    public HostController(HostSettings s)
    {
        Settings = s;
        foreach (var p in All)
        {
            p.Output += (n, l) => { Output?.Invoke(n, l); AppendLog(n, l); };
            p.Exited += (n, code) => { if (!_stopping) Stopped?.Invoke(n, code); };
        }
    }

    public bool AnyRunning => All.Any(p => p.Running);

    /// <summary>The servers still running from the host folder without a Loki Host (see ServerProcess.Adopt), taken over.</summary>
    public List<string> AdoptRunning() =>
        Settings.SetUp ? All.Where(p => p.Adopt(Settings.HostDir)).Select(p => p.Name).ToList() : new List<string>();

    readonly object _logGate = new();

    void AppendLog(string server, string line)
    {
        // one writer at a time: the relay's lines come from thread-pool threads, and two appends at once would lose one
        lock (_logGate)
        {
            try { File.AppendAllText(Path.Combine(Settings.HostDir, "log", $"lokihost-{server}.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Finds the addresses, writes the configuration (HostSetup.ApplyConf), then starts login, char and map, each
    /// once the previous one listens (char connects to login, map to char). <paramref name="publicIpOverride"/>:
    /// use this address instead of asking the internet (tests, or a LAN-only session).
    /// </summary>
    public async Task StartAsync(Action<string> log, string? publicIpOverride = null, int waitSeconds = 60)
    {
        if (!Settings.SetUp) throw new InvalidOperationException("Run the setup first (Setup tab).");
        if (AnyRunning) throw new InvalidOperationException("The server is already running.");
        if (Db != null) await Db.StartAsync(log);
        var err = Database.Probe(Database.ConnStr(Settings));
        if (err != null) throw new InvalidOperationException("The database cannot be reached: " + err);
        await BackupAsync(log);

        var adapters = Net.Adapters();
        (LanIp, var mask) = Net.PickLan(adapters);
        Vpn = Net.VpnAdapter(adapters, LanIp, Net.InternetSourceAddress());
        if (Vpn != null) log("WARNING: " + Net.VpnWarning(Vpn));
        if (publicIpOverride != null) PublicIp = publicIpOverride;
        else
        {
            var (ip, why) = await Net.PublicIpAsync();
            if (ip == null) throw new InvalidOperationException("Could not find this network's public address (" + why + ")");
            PublicIp = ip;
        }
        log($"Public address {PublicIp}, LAN address {(LanIp == "" ? "(none)" : LanIp)}");
        HostSetup.ApplyConf(Settings, PublicIp, LanIp, mask, log);
        await PrepareHordeAsync(log);
        await PrepareAccountSettingsAsync(log); // client wave 18 (w18acct)
        Directory.CreateDirectory(Path.Combine(Settings.HostDir, "log"));

        // a server already running from the host folder (left by an earlier run) is taken over, never started twice: a second
        // copy fails on the port (seen 2026-09-30 in a test run: char-server "bind failed, error 10048" next to a running one)
        foreach (var n in AdoptRunning()) log($"{n} was already running from {Settings.HostDir}: taken over");
        await CheckMapDataAsync(log);
        foreach (var (p, port) in new[] { (Login, Settings.LoginPort), (Char, Settings.CharPort), (Map, Settings.MapPort) })
        {
            if (p.Running && await Net.ListeningLocallyAsync(port)) { log($"{p.Name} is listening on port {port}"); continue; }
            if (await Net.ListeningLocallyAsync(port))
                throw new InvalidOperationException($"Port {port} is already in use by another program (another rAthena?).");
            log($"Starting {p.Name} …");
            p.Start(Settings.HostDir);
            var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (!await Net.ListeningLocallyAsync(port))
            {
                if (!p.Running) { await StopAsync(log); throw new InvalidOperationException($"{p.Name} stopped while starting: see its log."); }
                if (DateTime.UtcNow > deadline) { await StopAsync(log); throw new InvalidOperationException($"{p.Name} did not open port {port} within {waitSeconds} s."); }
                await Task.Delay(300);
            }
            log($"{p.Name} is listening on port {port}");
        }
        await StartPresenceAsync(log);
    }

    /// <summary>What the last backup of this window did (<see cref="DbBackup"/>), or null before the first one.</summary>
    public DbBackup.Result? LastBackup { get; private set; }

    /// <summary>
    /// design note, 2026-10-07: a backup of the host's database at every Start (HostSettings.BackupOnStart), once it answers and before
    /// the login / char / map servers start. Never fatal: a failure is said plainly in the window and in log\lokihost-backup.log,
    /// and the servers start anyway.
    /// </summary>
    public async Task BackupAsync(Action<string> log)
    {
        if (!Settings.BackupOnStart) { log("Database backup: off (Setup tab)."); return; }
        log($"Backing up the database '{Settings.DbName}' into {DbBackup.Folder(Settings)} …");
        LastBackup = await DbBackup.RunAsync(Settings);
        var line = LastBackup.Ok ? LastBackup.Summary : "WARNING: " + LastBackup.Summary;
        Directory.CreateDirectory(Path.Combine(Settings.HostDir, "log"));
        AppendLog("backup", line);
        log(line);
    }

    /// <summary>
    /// Client wave 7 (w7host): before any server starts, the host's Ragnarok Online install is compared with what the map
    /// cache was made from; when the official patcher changed it, the import map cache is rebuilt, checked and put in place
    /// (MapDataWatch.Check: seconds of work, off the caller's thread). Never while a server runs from the host folder: the
    /// map-server reads the cache once, at its own start. Never fatal: whatever happens, the servers start with a cache.
    /// </summary>
    public async Task CheckMapDataAsync(Action<string> log)
    {
        if (AnyRunning)
        {
            MapData = new MapDataWatch.Outcome
            {
                Kind = MapDataWatch.Kind.Skipped, Reason = "a server is already running from the host folder",
                Summary = "Map data: not checked now (a server is already running from the host folder): checked at the next Start.",
            };
            (MapData.DataStamp, MapData.DataLabel) = Settings.RoDir.Trim() == "" ? ("", "") : MapDataWatch.KeptDataStamp(Settings.HostDir);
            log(MapData.Summary);
            return;
        }
        string hostDir = Settings.HostDir, roDir = Settings.RoDir.Trim();
        // the @go points (check_points.py's third argument) are read from the rAthena build folder when it holds the source
        // (src/map/atcommand.cpp: a build made in a checkout does); else they are not checked, and the log says so
        var opt = MapDataOptions;
        if (opt.SourceDir == null) opt = new MapDataWatch.Options { Quiet = opt.Quiet, MaxWait = opt.MaxWait, UtcNow = opt.UtcNow, Sleep = opt.Sleep, SourceDir = Settings.SourceDir };
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Line(string l) { AppendLog("mapdata", l); lines.Enqueue(l); }
        var work = Task.Run(() =>
        {
            try { return MapDataWatch.Check(hostDir, roDir, Line, opt); }
            catch (Exception e)
            {
                // whatever went wrong (a damaged archive read in an unexpected way …): the cache in place is untouched
                var kept = MapDataWatch.KeptDataStamp(hostDir);
                return new MapDataWatch.Outcome
                {
                    Kind = MapDataWatch.Kind.Failed, Reason = e.GetType().Name + ": " + e.Message.Trim(), DataStamp = kept.Stamp, DataLabel = kept.Label,
                    Summary = $"Map data: NOT rebuilt, the map cache in place is kept: {e.GetType().Name}: {e.Message.Trim()}. The next Start tries again.",
                };
            }
        });
        // the lines reach the window on the caller's own thread, while the work goes on
        while (!work.IsCompleted) { while (lines.TryDequeue(out var l)) log(l); await Task.Delay(100); }
        MapData = await work;
        while (lines.TryDequeue(out var l)) log(l);
        AppendLog("mapdata", MapData.Summary);
        log(MapData.Summary);
    }

    /// <summary>
    /// Starts the headset pose relay on <see cref="HostSettings.PresencePort"/> (all interfaces, like the servers), when it is
    /// enabled and not running yet. Not fatal: without it friends play as usual, only the floating headsets are missing.
    /// Also called when servers left running by a closed Loki Host are taken over (AdoptRunning): the relay lived in that process.
    /// </summary>
    public async Task StartPresenceAsync(Action<string> log)
    {
        if (!Settings.PresenceEnabled || Relay != null) return;
        if (await Net.ListeningLocallyAsync(Settings.PresencePort))
        {
            log($"WARNING: port {Settings.PresencePort} is in use by another program: the headset relay is off (friends still play; they just don't see each other's headsets or hear each other's voices).");
            return;
        }
        // client wave 7 (w7host): the data stamp of the install the server's map cache was made from; players whose own differs are
        // told once and listed in the window. From the last Start's check, or (servers taken over by a new window) from the file.
        var (dataStamp, dataLabel) = Settings.RoDir.Trim() == "" ? ("", "")
            : MapData != null ? (MapData.DataStamp, MapData.DataLabel) : MapDataWatch.KeptDataStamp(Settings.HostDir);
        var r = new PoseRelay(Settings.PresencePort, voice: Settings.VoiceEnabled, dataStamp: dataStamp, dataLabel: dataLabel); // client wave 6 (w6voice): voice on the same relay
        r.Log += l => { Output?.Invoke(RelayName, l); AppendLog(RelayName, l); };
        r.GesturesEnabled = true; r.Relations = new RelationsDb(Database.ConnStr(Settings)); // client wave 18 (w18share): casting menus; friends, family, guild for their privacy
        r.DrawingsEnabled = true; // client wave 20 (w20pen): the air pen's drawings, by the same relations
        r.SpotsEnabled = true; // free movement (wfree): the players' exact spots
        var h = NewHorde();
        if (h != null) { r.Horde = h; h.Transport = r; }
        if (Settings.AccountSettings) r.AccountSettings = new AccountSettingsDb(Settings); // client wave 18 (w18acct): settings per account
        try { r.Start(); Relay = r; }
        catch (System.Net.Sockets.SocketException e) { log($"WARNING: the headset relay could not listen on port {Settings.PresencePort} ({e.Message}): it is off."); return; }
        if (h != null) { h.Start(); Horde = h; }
    }

    /// <summary>
    /// Client wave 8 (w8core): before the map-server starts, the Horde reward's NPC script is written into the host's copy (or its
    /// line taken out when Horde or its reward is off) and its table made in the host's database. Never fatal: without it the
    /// mode still plays, its results just promise no reward (the window's log says so).
    /// </summary>
    public async Task PrepareHordeAsync(Action<string> log)
    {
        bool reward = Settings.PresenceEnabled && Settings.HordeEnabled && Settings.HordeReward > 0;
        try { HordeNpc.Install(Settings.HostDir, reward); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log("WARNING: Horde's reward script could not be written into the host copy: " + e.Message); reward = false; }
        _hordeRewardReady = false;
        if (!reward) return;
        try { await new HordeRewardDb(Settings).EnsureTableAsync(); _hordeRewardReady = true; }
        catch (Exception e) { log("WARNING: Horde's reward table could not be made in the database (" + e.Message + "): runs give no real EXP until the next Start."); }
    }

    bool _hordeRewardReady;

    /// <summary>
    /// Client wave 18 (w18acct), the design's point 1: before the servers start, the table of the accounts' settings is made in the host's
    /// database (AccountSettingsDb.CreateSql; nothing is ever dropped or emptied). Never fatal: without it the relay answers
    /// "the host could not read its store" and every player keeps the settings of their own PC, as before.
    /// </summary>
    public async Task PrepareAccountSettingsAsync(Action<string> log)
    {
        if (!Settings.AccountSettings || !Settings.PresenceEnabled) { log("Settings per account: off (players keep each PC's own)."); return; }
        try { await new AccountSettingsDb(Settings).EnsureTableAsync(); log($"Settings per account: on (table {AccountSettingsDb.Table})."); }
        catch (Exception e) { log("WARNING: the table of players' settings could not be made in the database (" + e.Message + "): players keep each PC's own settings until the next Start."); }
    }

    HordeService? NewHorde()
    {
        if (!Settings.HordeEnabled) return null;
        var content = Loki.Horde.Content.HordeTables.Build();
        var hs = new HordeSettings
        {
            Enabled = true, RunSeconds = Math.Clamp(Settings.HordeRunMinutes, 1, 120) * 60f, Reward = (Loki.Horde.HordeRewardSize)Math.Clamp(Settings.HordeReward, 0, 3),
            ArenaId = Settings.HordeArena, Towns = SafeTowns(),
        };
        var h = new HordeService(content, hs);
        h.Log += l => { Output?.Invoke(RelayName, l); AppendLog(RelayName, l); };
        // servers taken over by a new window: PrepareHordeAsync did not run in this one; the table exists if a Start made it
        if (hs.Reward != Loki.Horde.HordeRewardSize.Off && (_hordeRewardReady || File.Exists(Path.Combine(Settings.HostDir, HordeNpc.RelPath.Replace('/', Path.DirectorySeparatorChar)))))
        {
            var db = new HordeRewardDb(Settings);
            db.Log += l => { Output?.Invoke(RelayName, l); AppendLog(RelayName, l); };
            h.Rewards = db;
        }
        return h;
    }

    List<string> SafeTowns()
    {
        try { return HordeNpc.Towns(Settings.HostDir); } catch (Exception) { return new List<string>(); }
    }

    async Task StopPresenceAsync(Action<string> log)
    {
        var r = Relay;
        if (r == null) return;
        Relay = null;
        var h = Horde; Horde = null;
        h?.Stop(); // a run still on ends as "stopped" (its players get their results; rewards are decided and written as at any end)
        log("Stopping the headset relay …");
        await r.StopAsync();
    }

    /// <summary>Map, then char, then login, each asked to shut down cleanly (it saves), forced after 30 s.</summary>
    public async Task StopAsync(Action<string> log)
    {
        _stopping = true;
        try
        {
            foreach (var p in new[] { Map, Char, Login })
            {
                if (!p.Running) continue;
                log($"Stopping {p.Name} …");
                bool clean = await p.StopAsync(TimeSpan.FromSeconds(30));
                log(clean ? $"{p.Name} stopped" : $"{p.Name} had to be ended by force");
            }
            // then any server still running from the host folder that this window did not start (an earlier run's): stopped too
            foreach (var n in AdoptRunning())
            {
                var p = All.First(x => x.Name == n);
                log($"{n} was still running from {Settings.HostDir} (not started by this window): stopping it …");
                bool clean = await p.StopAsync(TimeSpan.FromSeconds(30));
                log(clean ? $"{n} stopped" : $"{n} had to be ended by force");
            }
            await StopPresenceAsync(log);
            if (Db != null) await Db.StopAsync(log);
        }
        finally { _stopping = false; }
    }

    /// <summary>The database answers, starting Loki Host's own one when needed (the Accounts tab while the servers are stopped).
    /// Returns true when this call started it (the caller stops it again when it is done, unless the servers run).</summary>
    public async Task<bool> EnsureDatabaseAsync(Action<string> log)
    {
        if (Db == null || Db.Answers()) return false;
        await Db.StartAsync(log);
        return true;
    }

    /// <summary>Presence: the headset relay listens (always false when it is switched off).</summary>
    public sealed record Status(bool Database, bool Login, bool Char, bool Map, bool Presence = false);

    public async Task<Status> StatusAsync(bool checkDatabase)
    {
        bool db = !checkDatabase || Database.Probe(Database.ConnStr(Settings)) == null;
        return new Status(db,
            Login.Running && await Net.ListeningLocallyAsync(Settings.LoginPort),
            Char.Running && await Net.ListeningLocallyAsync(Settings.CharPort),
            Map.Running && await Net.ListeningLocallyAsync(Settings.MapPort),
            Relay is { Running: true } && await Net.ListeningLocallyAsync(Settings.PresencePort));
    }
}
