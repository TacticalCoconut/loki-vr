using System.Net;
using LokiHost.Core;

// The headset pose relay as a plain console program: the same PoseRelay Loki Host runs with its servers, for tests (two
// clients against a private rAthena copy) or for a server without Loki Host.
//   LokiPoseRelay [--port 7903] [--bind 0.0.0.0] [--log file] [--no-voice] [--stats seconds]
//                 [--ro-folder dir | --data-stamp stamp [--data-label text]]
//   LokiPoseRelay --print-data-stamp dir
// The default port is Loki Host's (7903); a client looks for the relay at its server entry's presencePort, or the login
// port + 3. Stops on Ctrl+C or a "quit" line. Every notable event is one line on stdout (and in --log).
// Client wave 6 (w6voice): voice is relayed unless --no-voice; --stats N writes the counters (voice frames in / forwarded /
// dropped, poses, queue drops) every N seconds while they change (the voice smokes read them from the log).
// Client wave 7 (w7host): the game-data check. --ro-folder: the host's Ragnarok Online install (only read): its data stamp
// (a hash of its map cells, client IO/MapDataStamp.cs) and patch number are what players' clients are compared with, as in
// Loki Host; or the two given directly with --data-stamp / --data-label. Without either nothing is compared (as before).
// --stats also writes the list of players with their game data while it changes. --print-data-stamp: the stamp and label of
// an install, one line, and nothing else.
// Client wave 18 (w18share): the casting menus friends see (Loki.Net.GestureWire) are forwarded unless --no-gestures;
// --relations-db "<MySQL connection string>" reads players' friends, families and guilds in that rAthena database (RelationsDb)
// for the menus a player shows only to some of them (without it those go to nobody; "Everyone" still goes to everyone). Client wave 18
// merge: those menus travel only between accounts proved by their login's token, so they also need --accounts-db.
// Client wave 20 (w20pen): the air pen's drawings (Loki.Net.DrawWire) are forwarded unless --no-drawings, by the casting menus' rule
// (the same --relations-db and --accounts-db); --stats writes their counters too.
// Free movement (wfree): the players' exact spots (Loki.Net.SpotWire) are forwarded unless --no-spots; --stats writes their counters too.
// Client wave 8 (w8core): --horde runs Horde in the relay (the bots' smoke, the Unity client's smoke against a standalone
// relay): --horde-minutes N (the run's length, default 15), --horde-invite seconds (default 30), --horde-towns a,b,c (the
// maps the prompt counts as towns; default: none known). A standalone relay has no rAthena database: it grants no reward
// (the results say so). --horde-bots N --horde-bot-aid A: N headless players (HordeBot) join this relay's runs (for a lone
// Unity client to play with; their AIDs from A up).
// Client wave 18 (w18acct): --accounts-db "<MySQL connection string>" keeps each account's settings in that rAthena database (the
// table loki_account_settings is made there; the proofs are checked against its login table: AccountSettingsDb), as Loki Host
// does with its own. The copy's login server must give web tokens (use_web_auth_token: yes). Without it no settings are kept.

int port = 7903;
IPAddress bind = IPAddress.Any;
string? logPath = null;
bool voice = true;
double statsSeconds = 0;
string dataStamp = "", dataLabel = "";
bool gestures = true; string? relationsDb = null; // client wave 18 (w18share)
bool drawings = true; // client wave 20 (w20pen)
bool spots = true; // free movement (wfree)
string? roFolder = null, printFolder = null;
bool horde = false; double hordeMinutes = 15, hordeInvite = 30; var hordeTowns = new List<string>(); int hordeBots = 0; uint hordeBotAid = 3900001;
string? accountsDb = null; // client wave 18 (w18acct)
const string Usage = "LokiPoseRelay [--port 7903] [--bind 0.0.0.0] [--log file] [--no-voice] [--stats seconds] [--ro-folder dir | --data-stamp stamp [--data-label text]] [--horde [--horde-minutes 15] [--horde-invite 30] [--horde-towns a,b] [--horde-bots N [--horde-bot-aid A]]] [--accounts-db \"connection string\"] [--no-gestures] [--no-drawings] [--no-spots] [--relations-db \"connection string\"] | --print-data-stamp dir";
for (int i = 0; i < args.Length; i++)
{
    string next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value after " + args[i]);
    switch (args[i])
    {
        case "--port": port = int.Parse(next()); break;
        case "--bind": bind = IPAddress.Parse(next()); break;
        case "--log": logPath = Path.GetFullPath(next()); break;
        case "--no-voice": voice = false; break;
        case "--stats": statsSeconds = double.Parse(next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--ro-folder": roFolder = Path.GetFullPath(next()); break;
        case "--data-stamp": dataStamp = next(); break;
        case "--data-label": dataLabel = next(); break;
        case "--print-data-stamp": printFolder = Path.GetFullPath(next()); break;
        case "--no-gestures": gestures = false; break; // client wave 18 (w18share)
        case "--no-drawings": drawings = false; break; // client wave 20 (w20pen)
        case "--no-spots": spots = false; break; // free movement (wfree)
        case "--relations-db": relationsDb = next(); break;
        case "--horde": horde = true; break;
        case "--horde-minutes": hordeMinutes = double.Parse(next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--horde-invite": hordeInvite = double.Parse(next(), System.Globalization.CultureInfo.InvariantCulture); break;
        case "--horde-towns": hordeTowns.AddRange(next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
        case "--horde-bots": hordeBots = int.Parse(next()); break;
        case "--horde-bot-aid": hordeBotAid = uint.Parse(next()); break;
        case "--accounts-db": accountsDb = next(); break; // client wave 18 (w18acct)
        case "-h": case "--help":
            Console.WriteLine(Usage);
            return 0;
        default: Console.Error.WriteLine("unknown argument " + args[i]); return 2;
    }
}

var gate = new object();
void Write(string line)
{
    var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}";
    lock (gate)
    {
        Console.WriteLine(text);
        if (logPath != null) { try { File.AppendAllText(logPath, text + Environment.NewLine); } catch (IOException) { } }
    }
}

if (printFolder != null || roFolder != null)
{
    string folder = printFolder ?? roFolder!;
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var install = HostInstall.Open(folder);
        var st = install.DataStamp();
        dataStamp = st.Stamp; dataLabel = HostInstall.PatchLabel(folder);
        string line = $"data stamp of {folder}: {st.Stamp}{(dataLabel != "" ? ", " + dataLabel : "")} ({st.Maps} maps, {st.Cells} cells, {st.Unreadable} unreadable; {sw.Elapsed.TotalSeconds:0.0} s)";
        if (printFolder != null) { Console.WriteLine(line); return 0; }
        Write(line);
    }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        if (printFolder != null) { Console.Error.WriteLine($"{folder}: {e.Message}"); return 1; }
        Write($"the game data of {folder} cannot be read ({e.Message}): players' game data is not compared");
        dataStamp = ""; dataLabel = "";
    }
}

var relay = new PoseRelay(port, voice: voice, dataStamp: dataStamp, dataLabel: dataLabel);
relay.Log += Write;
relay.GesturesEnabled = gestures; // client wave 18 (w18share): casting menus, and who may see them
relay.DrawingsEnabled = drawings; // client wave 20 (w20pen): the air pen's drawings, by the same rule
relay.SpotsEnabled = spots; // free movement (wfree): the players' exact spots
if (relationsDb != null) { relay.Relations = new RelationsDb(relationsDb); Write($"casting menus: relations read from {new MySqlConnector.MySqlConnectionStringBuilder(relationsDb).Database}"); }
else if (gestures) Write("casting menus: no relations database (--relations-db): menus shown only to friends, family or guild go to nobody");
if (gestures && relationsDb != null && accountsDb == null) Write("casting menus: no account proofs (--accounts-db): menus shown only to friends, family or guild go to nobody"); // (client wave 18 merge: restricted menus need verified accounts)
HordeService? hordeService = null;
if (horde)
{
    hordeService = new HordeService(Loki.Horde.Content.HordeTables.Build(), new HordeSettings { RunSeconds = (float)(hordeMinutes * 60), InviteSeconds = (float)hordeInvite, Towns = hordeTowns });
    hordeService.Log += Write;
    hordeService.Transport = relay;
    relay.Horde = hordeService;
}
if (accountsDb != null) // client wave 18 (w18acct): settings per account in that rAthena database
{
    var store = new AccountSettingsDb(accountsDb);
    try { await store.EnsureTableAsync(); relay.AccountSettings = store; Write($"settings per account: kept in {new MySqlConnector.MySqlConnectionStringBuilder(accountsDb).Database}.{AccountSettingsDb.Table}"); }
    catch (Exception e) { Write($"settings per account: OFF, the database cannot be used ({e.Message})"); }
}
try { relay.Start(bind); }
catch (System.Net.Sockets.SocketException e) { Write($"cannot listen on port {port}: {e.Message}"); return 1; }
hordeService?.Start();
var bots = new List<HordeBot>();
if (hordeService != null && hordeBots > 0)
    _ = Task.Run(async () =>
    {
        await Task.Delay(500);
        for (int b = 0; b < hordeBots; b++)
        {
            try { bots.Add(new HordeBot("127.0.0.1", relay.Port, hordeBotAid + (uint)b, "Bot" + (b + 1), hordeService.Content, (byte)(b % 2 + 1))); }
            catch (Exception e) { Write($"bot {b + 1} could not connect: {e.Message}"); }
        }
        Write($"{bots.Count} Horde bot(s) connected (they join every run)");
        while (true) { lock (bots) foreach (var x in bots) x.Update(); await Task.Delay(15); }
    });

var stop = new TaskCompletionSource();
if (statsSeconds > 0)
    _ = Task.Run(async () =>
    {
        string last = "", lastPlayers = "", lastMenus = "", lastDraws = "", lastSpots = "";
        while (!stop.Task.IsCompleted)
        {
            await Task.Delay(TimeSpan.FromSeconds(statsSeconds));
            string line = relay.StatsLine();
            if (line != last) { Write(line); last = line; }
            string menus = relay.GestureStatsLine(); // client wave 18 (w18share)
            if (menus != lastMenus) { Write(menus); lastMenus = menus; }
            string draws = relay.DrawStatsLine(); // client wave 20 (w20pen)
            if (draws != lastDraws) { Write(draws); lastDraws = draws; }
            string spotsLine = relay.SpotStatsLine(); // free movement (wfree)
            if (spotsLine != lastSpots) { Write(spotsLine); lastSpots = spotsLine; }
            string players = relay.PlayersLine();
            if (players != lastPlayers) { Write(players); lastPlayers = players; }
        }
    });
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
_ = Task.Run(() =>
{
    // a "quit" line stops it too; an input that ends (redirected from NUL) does not: runners stop it by its PID
    try { string? l; while ((l = Console.ReadLine()) != null) if (l.Trim() == "quit") { stop.TrySetResult(); break; } }
    catch (Exception) { }
});
await stop.Task;
hordeService?.Stop();
lock (bots) foreach (var x in bots) x.Dispose();
await relay.StopAsync();
return 0;
