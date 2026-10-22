namespace LokiHost.Core;

/// <summary>First-time setup and the configuration written before every start.</summary>
public static class HostSetup
{
    static string Import(HostSettings s, string file) => Path.Combine(s.HostDir, "conf", "import", file);

    /// <summary>
    /// Everything the host owns in the copy's conf/import files (conf/*_athena.conf keys; the base files stay as
    /// rAthena ships them), applied again at every start so a new public / LAN address or a new port is picked up:
    /// - inter_conf.txt: the six database connections (inter_athena.conf *_server_* / log_db_* / ipban_db_*);
    /// - login_conf.txt: login_port, new_account, use_web_auth_token (client wave 18: settings per account);
    /// - char_conf.txt: the servers' account (userid / passwd), server_name, login_ip / login_port (the login server,
    ///   local), char_ip (the address the login server hands to players: public), char_port;
    /// - map_conf.txt: userid / passwd, char_ip / char_port (the char server, local), map_ip (the address the char
    ///   server hands to players: public), map_port;
    /// - battle_conf.txt: area_size (HostSettings.ViewRange: how far players see; design note, 2026-10-01, doubled 2026-10-05);
    /// - log_conf.txt: log_zeny (HostSettings.LogZeny: 1 = every zeny change in the zenylog table; design note, 2026-10-07);
    /// - db/import/skill_db.yml: the skill ranges the host changes (HostSettings.SkillRanges; SkillImport's block);
    /// - conf/subnet_athena.conf: players on this PC and on the LAN get local addresses instead (Conf.SubnetText);
    /// - db/import/instance_db.yml, npc/custom/loki_tutorial.txt and its line in npc/scripts_custom.conf: the controls tutorial's
    ///   private map (HostSettings.TutorialMap; TutorialInstance; client wave 21, the design's point 47). <paramref name="log"/> gets its lines;
    /// - the text overhaul's files (NPC scripts, db/mob_chat_db.yml, conf/msg_conf/map_msg.conf, conf/motd.txt), the originals kept in
    ///   textpack-originals (HostSettings.TextOverhaul; TextOverhaul; wave 28): once per pack version. <paramref name="log"/> gets its line.
    /// - npc/custom/loki_codex.txt and its line in npc/scripts_custom.conf: the Codex's bounties (HostSettings.CodexBounties; CodexBounty;
    ///   wave 28, wcxp). <paramref name="log"/> gets its line.
    /// </summary>
    public static void ApplyConf(HostSettings s, string publicIp, string lanIp, string lanMask, Action<string>? log = null)
    {
        var inter = Import(s, "inter_conf.txt");
        foreach (var p in new[] { "login_server", "ipban_db", "char_server", "map_server", "web_server", "log_db" })
        {
            Conf.SetKey(inter, p + "_ip", s.DbHost);
            Conf.SetKey(inter, p + "_port", s.DbPort.ToString());
            Conf.SetKey(inter, p + "_id", s.DbUser);
            Conf.SetKey(inter, p + "_pw", s.DbPass);
            Conf.SetKey(inter, p + "_db", s.DbName);
        }
        var login = Import(s, "login_conf.txt");
        Conf.SetKey(login, "login_port", s.LoginPort.ToString());
        Conf.SetKey(login, "new_account", s.AllowRegistration ? "yes" : "no");
        // client wave 18 (w18acct): settings per account. The relay takes a client's settings only with the web token of its login,
        // which rAthena makes and sends only with use_web_auth_token on (rA:login/login.cpp:426-428, account.cpp:651, loginclif.cpp:127;
        // conf/login_athena.conf:174 ships "yes", the build's import says "no"). Off, the line is left as the build has it.
        if (s.AccountSettings && s.PresenceEnabled) Conf.SetKey(login, "use_web_auth_token", "yes");

        var chr = Import(s, "char_conf.txt");
        Conf.SetKey(chr, "userid", s.ServerUser);
        Conf.SetKey(chr, "passwd", s.ServerPass);
        Conf.SetKey(chr, "server_name", s.ServerName);
        Conf.SetKey(chr, "login_ip", "127.0.0.1");
        Conf.SetKey(chr, "login_port", s.LoginPort.ToString());
        Conf.SetKey(chr, "char_ip", publicIp);
        Conf.SetKey(chr, "char_port", s.CharPort.ToString());

        var map = Import(s, "map_conf.txt");
        Conf.SetKey(map, "userid", s.ServerUser);
        Conf.SetKey(map, "passwd", s.ServerPass);
        Conf.SetKey(map, "char_ip", "127.0.0.1");
        Conf.SetKey(map, "char_port", s.CharPort.ToString());
        Conf.SetKey(map, "map_ip", publicIp);
        Conf.SetKey(map, "map_port", s.MapPort.ToString());

        // battle_conf.txt: area_size, how far players see (HostSettings.ViewRange); rAthena reads conf/import/battle_conf.txt last
        // (conf/battle_athena.conf:64), the build's own lines in it (rates, Loki's sit regen) stay
        if (s.ViewRange > 0) Conf.SetKey(Import(s, "battle_conf.txt"), "area_size", s.ViewRange.ToString());

        // log_conf.txt: log_zeny, rAthena's zeny log (HostSettings.LogZeny; design note, 2026-10-07): conf/log_athena.conf:92-93 "0 - don't
        // log; 1 - log any zeny changes", read last from its import (log_athena.conf:169); log.cpp log_zeny writes the zenylog table
        Conf.SetKey(Import(s, "log_conf.txt"), "log_zeny", s.LogZeny ? "1" : "0");

        // db/import/skill_db.yml: the skill ranges this host changes (HostSettings.SkillRanges), in the one block Loki Host owns there
        SkillImport.Write(s.HostDir, s.SkillRanges);

        // the controls tutorial's private map: one instance_db entry in the block Loki Host owns, and the NPC the client whispers
        foreach (var line in TutorialInstance.Write(s.HostDir, s.TutorialMap)) log?.Invoke(line);

        // wave 28 (wtexts): the text overhaul's verified server files in (or, set off, the originals back)
        foreach (var line in TextOverhaul.Apply(s.HostDir, s.TextOverhaul)) log?.Invoke(line);

        // wave 28 (wcxp): the Codex's bounties, the NPC the client whispers (the script and its line in npc/scripts_custom.conf)
        foreach (var line in CodexBounty.Write(s.HostDir, s.CodexBounties)) log?.Invoke(line);

        File.WriteAllText(Path.Combine(s.HostDir, "conf", "subnet_athena.conf"), Conf.SubnetText(lanIp, lanMask));
    }

    /// <summary>
    /// First-time setup: copy the build, take the test settings out, create the database and its user, import
    /// rAthena's SQL (main, logs, web if present, the roulette prizes: rAthena's CI imports the same,
    /// tools/ci/sql.sh), give the servers' account a random name and password. Settings are saved as it goes.
    /// </summary>
    public static void Run(HostSettings s, string adminUser, string adminPass, Action<string> log)
    {
        // the database first: a refused login stops the setup before anything is copied or created (2026-09-30: the design's first
        // setup copied rAthena, then MariaDB refused the root login, and a second try refused the non-empty folder)
        string adminConn;
        PrivateDb? own = null;
        if (s.PrivateDb)
        {
            own = new PrivateDb(s);
            own.Log += l => log("[database] " + l);
            own.Init(log);
            s.Save(); // the private root password must be kept even if a later step fails
            own.StartAsync(log).GetAwaiter().GetResult();
            s.DbHost = "127.0.0.1"; s.DbPort = s.PrivateDbPort;
            adminConn = own.RootConnStr();
        }
        else
        {
            adminConn = Database.ConnStr(s.DbHost, s.DbPort, adminUser, adminPass);
            var refused = Database.Probe(adminConn);
            if (refused != null)
                throw new InvalidOperationException($"MariaDB refused the admin login '{adminUser}' ({refused}). Nothing was copied or created. " +
                    "The password is the one this account already has (for root: chosen when MariaDB was installed); it is not a new one.");
        }
        try
        {
            bool resume = Directory.Exists(s.HostDir) && Directory.EnumerateFileSystemEntries(s.HostDir).Any() && RathenaCopy.IsOwnCopy(s.HostDir);
            log($"Copying rAthena from {s.SourceDir} to {s.HostDir} …");
            RathenaCopy.Create(s.SourceDir, s.HostDir, log, resume);
            foreach (var line in RathenaCopy.RemoveTestSettings(s.HostDir)) log(line);

            if (string.IsNullOrEmpty(s.DbPass)) s.DbPass = Database.RandomSecret();
            log($"Creating database '{s.DbName}' and its user '{s.DbUser}' …");
            Database.CreateDatabaseAndUser(adminConn, s.DbName, s.DbUser, s.DbPass);
            s.Save();

            var conn = Database.ConnStr(s);
            foreach (var f in new[] { "main.sql", "logs.sql", "web.sql", "roulette_default_data.sql" })
            {
                var path = Path.Combine(s.HostDir, "sql-files", f);
                if (!File.Exists(path)) { log($"(no sql-files/{f}: skipped)"); continue; }
                log($"Importing sql-files/{f} …");
                Database.ImportSql(conn, path);
            }

            s.ServerUser = "srv" + Database.RandomSecret(8);
            s.ServerPass = Database.RandomSecret();
            Database.SetServerAccount(conn, s.ServerUser, s.ServerPass, Database.LoginRules.Read(s.HostDir).Md5);
            log("The servers' own account got a random name and password (instead of rAthena's s1 / p1).");
            s.SetUp = true;
            s.Save();
            log("Setup done.");
        }
        finally
        {
            // Loki Host's own database runs only while it is needed: Start (and the Accounts tab) start it again
            own?.StopAsync(log).GetAwaiter().GetResult();
        }
    }
}
