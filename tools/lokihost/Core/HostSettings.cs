using System.Text.Json;

namespace LokiHost.Core;

/// <summary>Everything Loki Host remembers, in %APPDATA%\LokiHost\lokihost.json (never an installed MariaDB's admin password;
/// the root password of Loki Host's own private instance, which listens on 127.0.0.1 only, is kept here).</summary>
public sealed class HostSettings
{
    /// <summary>The rAthena build copied from (its *.exe / *.dll, conf, db, npc, sql-files).</summary>
    public string SourceDir { get; set; } = @"C:\rAthena";
    /// <summary>The host's own rAthena copy.</summary>
    public string HostDir { get; set; } = @"C:\LokiHost\rathena";

    /// <summary>Loki Host runs its own MariaDB instance (PrivateDb) instead of using an installed server's admin login. False
    /// here so a settings file from before this existed keeps meaning an installed server; new setups choose in the window.</summary>
    public bool PrivateDb { get; set; } = false;
    /// <summary>The private instance's folder (its data in "data"), its port (127.0.0.1 only) and its root password.</summary>
    public string PrivateDbDir { get; set; } = @"C:\LokiHost\mariadb";
    public int PrivateDbPort { get; set; } = 3307;
    public string PrivateDbRootPass { get; set; } = "";
    /// <summary>The installed MariaDB's bin folder the private instance runs from ("" = find it: PrivateDb.FindBin).</summary>
    public string MariaDbBin { get; set; } = "";

    public string DbHost { get; set; } = "127.0.0.1";
    public int DbPort { get; set; } = 3306;
    public string DbName { get; set; } = "loki_host";
    public string DbUser { get; set; } = "lokihost";
    public string DbPass { get; set; } = "";

    /// <summary>One range to forward on the router: 7900-7902 (the PC's rAthena keeps 6900 / 6121 / 5121, the parity
    /// oracles 6910-7210 / 6131-6431 / 5131-5431).</summary>
    public int LoginPort { get; set; } = 7900;
    public int CharPort { get; set; } = 7901;
    public int MapPort { get; set; } = 7902;
    /// <summary>The headset pose relay (PoseRelay): friends in VR see each other's head and hands above the table. Next to the
    /// map port, so the router forwards one range (7900-7903) and one firewall rule covers it; clients find it through the
    /// server entry's presencePort (the share text carries it), or the login port + 3.</summary>
    public int PresencePort { get; set; } = 7903;
    /// <summary>Run the pose relay with the servers (a settings file from before the relay existed gets true).</summary>
    public bool PresenceEnabled { get; set; } = true;
    /// <summary>Client wave 6 (w6voice): the relay also forwards the players' voices (the same port, the same connections; a
    /// settings file from before voice existed gets true). Off: friends still see each other's headsets, they just don't hear
    /// each other through Loki. It needs the relay: with <see cref="PresenceEnabled"/> off there is no voice either.</summary>
    public bool VoiceEnabled { get; set; } = true;

    /// <summary>Client wave 7 (w7host): the host's own Ragnarok Online install folder (the one with data.grf), only ever read.
    /// At each Start Loki Host compares it with what the server's map cache was made from and rebuilds the cache when the
    /// official patcher changed the maps (MapDataWatch), and players whose game data differs from it are told (PoseRelay).
    /// "" (a settings file from before this existed): neither is done, the server walks on the map cache it has.</summary>
    public string RoDir { get; set; } = "";

    /// <summary>Client wave 8 (w8core): Horde, the co-op side mode, runs in the relay (it needs the relay: with
    /// <see cref="PresenceEnabled"/> off there is no Horde either). A settings file from before Horde gets true.</summary>
    public bool HordeEnabled { get; set; } = true;
    /// <summary>A run's length in minutes (the final boss comes then).</summary>
    public int HordeRunMinutes { get; set; } = 15;
    /// <summary>The real EXP a run gives: 0 off, 1 low, 2 normal, 3 high (Loki.Horde.HordeRewardSize).</summary>
    public int HordeReward { get; set; } = 2;
    /// <summary>0 = a random arena of the tables; else that arena's id.</summary>
    public int HordeArena { get; set; } = 0;
    /// <summary>Client wave 18 (w18acct), the design's point 1: the relay keeps each account's settings (every setting and Reposition
    /// place but Audio and Graphics) in this host's database, so a player's new PC gets them (AccountSettingsDb; it needs the relay:
    /// with <see cref="PresenceEnabled"/> off there is none). On, the copy's login server gives each login its web token
    /// (use_web_auth_token: yes, HostSetup.ApplyConf), the proof the relay checks. A settings file from before this line gets true.</summary>
    public bool AccountSettings { get; set; } = true;

    public string ServerName { get; set; } = "Loki";
    /// <summary>The login/char/map servers' own account (login table account 1; rAthena ships it as s1 / p1).</summary>
    public string ServerUser { get; set; } = "";
    public string ServerPass { get; set; } = "";
    /// <summary>login_athena.conf new_account: players may create their account from the login screen (_M / _F).</summary>
    public bool AllowRegistration { get; set; } = false;
    /// <summary>battle_athena.conf area_size (conf/battle/client.conf:39, rAthena's default 14): how far, in cells, players see units
    /// around them (a square: x and y each ± this). the design's choice of 2026-10-01: about twice the screen, 30; doubled on
    /// 2026-10-05 ("double the vision range"): 60, a square of 121 x 121 cells, four times the cells of 30. rAthena takes any value
    /// (battle.cpp:8553: 0 .. INT_MAX). It is also rAthena's range
    /// for several rules (map.hpp:66 AREA_SIZE: public chat reaches area_size - 5 (clif.cpp:527-529), NPCs answer clicks within
    /// area_size + 1 (npc.cpp:2159), monsters run their full AI within area_size + 4 of a player (mob.cpp:46, :2351), party quest /
    /// achievement kill credit (mob.cpp:3576-3582), the pet / homunculus leash (unit.cpp:408-410), the skills whose area is "the
    /// screen" (skill.cpp:225-228: SplashArea -1, e.g. Frost Joker, Scream, Adrenaline Rush), …), which grow with it.</summary>
    public int ViewRange { get; set; } = 60;
    /// <summary>The view range of the settings files written before <see cref="CurrentVersion"/> 2.</summary>
    public const int ViewRangeBefore2 = 30;
    /// <summary>What <see cref="SettingsVersion"/> is after <see cref="Load"/>. 2 (2026-10-05): the view range was doubled.</summary>
    public const int CurrentVersion = 2;
    /// <summary>The version of the settings in the file. A file without this line is from before it existed: version 1 (which is
    /// why 1, not <see cref="CurrentVersion"/>, is the value a file without the line is read with).</summary>
    public int SettingsVersion { get; set; } = 1;
    /// <summary>Skills whose cast range this host changes (written into the copy's db/import/skill_db.yml at every start:
    /// <see cref="SkillImport"/>). design note, 2026-10-06: "increase the range of steal to two" (TF_STEAL 50, rAthena's range 1: a
    /// Thief could not steal from a monster behind a one-cell fence, as in the Academy's training cages). A settings file from
    /// before this line existed gets this default; an empty list switches every change off.</summary>
    public List<SkillRange> SkillRanges { get; set; } = new() { new SkillRange(50, "TF_STEAL", 2) };
    /// <summary>Client wave 21 (w21map), the design's point 47: the controls tutorial's own map, "map,x,y": the map each character gets a
    /// private copy of (an rAthena instance without NPCs, warps or monsters) and the cell it enters at, written into the copy's
    /// db/import/instance_db.yml at every start (<see cref="TutorialInstance"/>). the design's choice, 2026-10-10: the Valkyrie's hall.
    /// A settings file from before this line existed gets this default; "" = no private map (the tutorial runs where the character
    /// stands, as on any rAthena server).</summary>
    public string TutorialMap { get; set; } = TutorialInstance.DefaultMap;
    /// <summary>Wave 28 (wcxp): the Codex's bounties (design note, 2026-10-15: a scan places a bounty, a kill claims it for Base EXP): the
    /// script <see cref="CodexBounty"/> writes into the copy at every start. A settings file from before this line existed gets true;
    /// false comments its line out (the players' bounties stay in their account variables).</summary>
    public bool CodexBounties { get; set; } = true;
    /// <summary>rAthena's zeny log (conf/log_athena.conf log_zeny: "1 - log any zeny changes", into the database's zenylog table),
    /// written into the copy's conf/import/log_conf.txt at every start: true = 1, false = 0. design note, 2026-10-07: "Turn on both, the
    /// zeny log and the startup backup". A settings file from before this line existed gets true.</summary>
    public bool LogZeny { get; set; } = true;
    /// <summary>At every Start, once the database answers and before the login / char / map servers start, the host's database
    /// (<see cref="DbName"/>) is dumped into backups\ beside the settings file (<see cref="DbBackup"/>). design note, 2026-10-07. A
    /// settings file from before this line existed gets true. Old backups are never removed by Loki Host.</summary>
    public bool BackupOnStart { get; set; } = true;
    /// <summary>Wave 28 (wtexts): the text overhaul (rewritten NPC dialogs, monster chat, server messages, the message of the day)
    /// is put into the host's copy at Start, its originals kept (<see cref="Core.TextOverhaul"/>); off: the originals go back at the
    /// next Start. Default on; a settings file from before this line existed gets true.</summary>
    public bool TextOverhaul { get; set; } = true;
    /// <summary>The language of "What to send your friends" (ShareText.English or ShareText.Portuguese).</summary>
    public string ShareLanguage { get; set; } = ShareText.English;
    /// <summary>Optional GitHub repository ("owner/repo") to check for Loki VR updates. Empty = disabled.</summary>
    public string UpdateGitHubRepo { get; set; } = "";
    /// <summary>Optional automatic update check on Loki Host startup. False by default so updates only happen when requested.</summary>
    public bool AutoCheckUpdates { get; set; } = false;
    public bool SetUp { get; set; } = false;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LokiHost", "lokihost.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The file these settings are saved to (DefaultPath unless set).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>
    /// Why the settings file exists but could not be read (null when it could, or when there is none yet). The settings are
    /// then the defaults, which point at the usual MariaDB (3306) and C:\LokiHost: the window refuses to work with them, and
    /// <see cref="Save"/> refuses to overwrite the file (found 2026-09-30: a scratch file with a JSON error silently became
    /// the defaults, one click away from setting up against the real MariaDB and folder).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LoadError { get; private set; }

    public static HostSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        HostSettings s;
        string? error = null;
        try { s = File.Exists(path) ? JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(path)) ?? throw new JsonException("the file holds null") : new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { s = new(); error = e.Message; }
        s.FilePath = path;
        s.LoadError = error;
        if (error == null && s.Upgrade() && File.Exists(path))
        {
            // the upgraded settings are written back at once, so that a value the host chooses later (30 again) is never raised twice
            try { s.Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return s;
    }

    /// <summary>
    /// Brings settings read from an older file to <see cref="CurrentVersion"/>; true when something changed.
    /// Version 1 → 2: a view range still at the old default (<see cref="ViewRangeBefore2"/>, 30) becomes twice that; any other
    /// value was the host's own choice and stays.
    /// </summary>
    public bool Upgrade()
    {
        if (SettingsVersion >= CurrentVersion) return false;
        if (SettingsVersion < 2 && ViewRange == ViewRangeBefore2) ViewRange = ViewRangeBefore2 * 2;
        SettingsVersion = CurrentVersion;
        return true;
    }

    public void Save(string? path = null)
    {
        if (LoadError != null) throw new InvalidOperationException($"{FilePath} could not be read ({LoadError}): it is not overwritten.");
        path ??= FilePath ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Every port friends must reach: the three servers and, when it runs, the pose relay.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int[] Ports => PresenceEnabled ? new[] { LoginPort, CharPort, MapPort, PresencePort } : new[] { LoginPort, CharPort, MapPort };

    /// <summary>The last port of the range to forward (the relay's when it runs).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int LastPort => Ports.Max();
}
