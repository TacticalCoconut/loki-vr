using MySqlConnector;

namespace LokiHost.Core;

/// <summary>
/// Horde's reward on Loki Host's own rAthena copy (client wave 8, w8core). The host decides; rAthena grants:
///   1. at a run's end the relay's HordeService computes each player's share of its next base / job level (RewardDef: Loki's
///      own numbers; the host setting picks the size) and <see cref="HordeRewardDb.RecordAsync"/> writes ONE row per player in
///      `loki_horde_reward` of Loki Host's database (the map-server's own database: conf/import/inter_conf.txt map_server_db,
///      HostSetup.ApplyConf), with the character's char_id (found by account id and character name);
///   2. the NPC script <see cref="HordeNpc"/> (installed into the copy at every Start) looks every 5 seconds for rows of
///      characters that are online on this map-server, attaches each player and decides BEFORE giving anything whether that
///      character can gain EXP now (pc_gainexp gives nothing to a dead player, one between two maps, or on a PvP map without
///      pvp_exp: pc.cpp:8459-8465): when it cannot, the row is left as it is for a later round and `getexp` is not called;
///      when it can, the row is taken (granted = 1) and the EXP given with rAthena's own `getexp`, once, whatever the EXP
///      then did (a character at its level's cap is done: it is never tried again);
///   3. Loki Host reads the granted rows back (granted = 1 → 2) and tells the player's client (H_REWARD).
/// Nothing comes from a client: the amount is the host's, the grant is the map-server's. A player who logged out before the
/// end gets its row like the others, granted when its character is next online. See the script's comments for the C++.
/// </summary>
public sealed class HordeRewardDb : IHordeRewards
{
    public const string Table = "loki_horde_reward";
    readonly string _conn;
    public event Action<string>? Log;

    public HordeRewardDb(string connStr) { _conn = connStr; }
    public HordeRewardDb(HostSettings s) : this(Database.ConnStr(s)) { }

    public const string CreateSql = "CREATE TABLE IF NOT EXISTS `" + Table + "` (" +
        "`id` INT UNSIGNED NOT NULL AUTO_INCREMENT," +
        "`run_id` INT UNSIGNED NOT NULL," +
        "`account_id` INT UNSIGNED NOT NULL," +
        "`char_id` INT UNSIGNED NOT NULL," +
        "`char_name` VARCHAR(30) NOT NULL DEFAULT ''," +
        "`base_share` SMALLINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '1/100 % of the next base level'," +
        "`job_share` SMALLINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '1/100 % of the next job level'," +
        "`created_at` DATETIME NOT NULL," +
        "`granted` TINYINT NOT NULL DEFAULT 0 COMMENT '0 waiting, 1 granted by the map-server, 2 reported to the player'," +
        "`granted_at` DATETIME NULL," +
        "`base_exp` BIGINT NOT NULL DEFAULT 0 COMMENT 'handed to getexp (before quest_exp_rate)'," +
        "`job_exp` BIGINT NOT NULL DEFAULT 0," +
        "PRIMARY KEY (`id`), KEY `waiting` (`granted`, `char_id`)) ENGINE=InnoDB";

    public async Task EnsureTableAsync()
    {
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        await using var cmd = new MySqlCommand(CreateSql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> RecordAsync(HordeRewardRow row)
    {
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        long charId;
        await using (var q = new MySqlCommand("SELECT `char_id` FROM `char` WHERE `account_id` = @a AND `name` = @n LIMIT 1", c))
        {
            q.Parameters.AddWithValue("@a", row.AccountId); q.Parameters.AddWithValue("@n", row.CharName);
            var o = await q.ExecuteScalarAsync();
            if (o == null || o is DBNull) { Log?.Invoke($"[horde] no character '{row.CharName}' on account {row.AccountId}: its reward is not written"); return false; }
            charId = Convert.ToInt64(o);
        }
        await using var ins = new MySqlCommand("INSERT INTO `" + Table + "` (`run_id`,`account_id`,`char_id`,`char_name`,`base_share`,`job_share`,`created_at`) VALUES (@r,@a,@c,@n,@b,@j,NOW())", c);
        ins.Parameters.AddWithValue("@r", row.RunId); ins.Parameters.AddWithValue("@a", row.AccountId); ins.Parameters.AddWithValue("@c", charId);
        ins.Parameters.AddWithValue("@n", row.CharName); ins.Parameters.AddWithValue("@b", Math.Clamp(row.BaseShare, 0, Loki.Horde.HordeReward.MaxShare));
        ins.Parameters.AddWithValue("@j", Math.Clamp(row.JobShare, 0, Loki.Horde.HordeReward.MaxShare));
        await ins.ExecuteNonQueryAsync();
        row.Id = ins.LastInsertedId;
        return true;
    }

    public async Task<List<HordeRewardRow>> TakeGrantedAsync()
    {
        var list = new List<HordeRewardRow>();
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        await using (var q = new MySqlCommand("SELECT `id`,`run_id`,`account_id`,`char_name`,`base_share`,`job_share`,`base_exp`,`job_exp` FROM `" + Table + "` WHERE `granted` = 1 ORDER BY `id` LIMIT 50", c))
        await using (var r = await q.ExecuteReaderAsync())
            while (await r.ReadAsync())
                list.Add(new HordeRewardRow { Id = r.GetInt64(0), RunId = (uint)r.GetInt64(1), AccountId = (uint)r.GetInt64(2), CharName = r.GetString(3), BaseShare = r.GetInt32(4), JobShare = r.GetInt32(5), BaseExp = r.GetInt64(6), JobExp = r.GetInt64(7) });
        foreach (var row in list)
        {
            await using var u = new MySqlCommand("UPDATE `" + Table + "` SET `granted` = 2 WHERE `id` = @i AND `granted` = 1", c);
            u.Parameters.AddWithValue("@i", row.Id);
            await u.ExecuteNonQueryAsync();
        }
        return list;
    }
}

/// <summary>The NPC script Loki Host installs into ITS rAthena copy (never the build it copies from) to grant Horde's rewards.</summary>
public static class HordeNpc
{
    public const string RelPath = "npc/custom/loki_horde.txt";
    public const string ImportLine = "npc: " + RelPath;

    /// <summary>
    /// Every command here was checked in rAthena e985006 (C:\rAthena):
    ///   - OnInit + initnpctimer / OnTimer5000: script.cpp:11710-11745 (initnpctimer starts the NPC's own timer, attached to
    ///     st->rid: called with no player attached, so the timer belongs to no player); detachrid before it (script.cpp:13535).
    ///   - query_sql: script.cpp:18041-18128, on its own connection to map_server_db (map.cpp:4400-4404 = Loki Host's database),
    ///     synchronous (Sql_QueryStr), numbers into .@ arrays (strtoll), returns the rows stored (0 none, -1 error).
    ///   - isloggedin(aid, cid): script.cpp:13543-13551 (this map-server has that account with that character in play).
    ///   - attachrid(aid): script.cpp:13507-13531, force (its default): also while the player is in another NPC's dialog: the
    ///     player's own script state and npc_id are backed up (script_attach_state, script.cpp:4347-4376) and put back by detachrid
    ///     (script_detach_rid → script_detach_state, script.cpp:13343-13350, 4303-4345). This script never waits between the two
    ///     (no mes / next / sleep), so the player's dialog goes on untouched. (With force 0 a player talking to a Kafra when the
    ///     timer comes would simply wait for the next round.)
    ///   - getcharid(0) = the attached character's char_id; NextBaseExp / NextJobExp / BaseExp / JobExp / BaseLevel / JobLevel
    ///     are script parameters (script_constants.hpp:574-590; NextBaseExp = pc_nextbaseexp, pc.cpp:10226, 8640-8647: at the
    ///     class's highest base level it is 99,999,999, the most base EXP such a character can hold; NextJobExp alike,
    ///     pc.cpp:10227, 8666-8673: 999,999,999).
    ///     CLIENT WAVE 15 (w15horde), the design's point 27: until then the share was multiplied by that cap too, so a character at
    ///     its class's highest level was "given" 7.67 % of 99,999,999 = 7,669,999 base EXP and 76,699,999 job EXP (Loki Host's
    ///     log 2026-10-08 14:13; pc_gainexp then cuts the amount to nothing at the cap, pc.cpp:8482-8493, but the row, the
    ///     player's message and the homunculus's share, script.cpp:11177-11180, carried the huge number). Now the cap values
    ///     give NO share: a level's own EXP in db/re/job_exp.yml is never exactly 99,999,999 (base) or 999,999,999 (job)
    ///     below the highest level (the 999,999,999 rows of the tables are the highest levels' own: pc_nextjobexp never reads them).
    ///   - getexp: script.cpp:11142-11182: base / job × battle_config.quest_exp_rate / 100 (rAthena's rate for EXP given by
    ///     NPCs), then pc_gainexp(sd, nullptr, base, job, 1) (quest EXP: a guild's EXP tax and the player's own EXP boosts
    ///     apply as for any quest, pc.cpp:8462-8477; max_exp_gain_rate caps it, pc.cpp:8495-8508), then, whatever pc_gainexp
    ///     did, the homunculus's share: hom_gainexp(sd->hd, base × homunculus_exp_gain / 100) (Renewal, script.cpp:11177-11180).
    ///   - WHEN getexp MAY BE CALLED. pc_gainexp returns without giving anything in exactly three cases: the player is not
    ///     on a map (sd->prev == nullptr: between two maps) or is dead (pc.cpp:8459), or is on a PvP map without pvp_exp
    ///     (pc.cpp:8464-8465). getexp reports none of it and gives the homunculus its share all the same, so the script
    ///     looks at the three BEFORE it calls getexp, and a character that cannot gain now keeps its row for a later round:
    ///       dead            isdead() (script.cpp:27532-27541: pc_isdead of the attached player);
    ///       between maps    no script command reads sd->prev, so two counts of the character's own cell (getmapxy,
    ///                       script.cpp:16451-16597: bl->m's name, bl->x, bl->y) are compared: getareausers
    ///                       (script.cpp:12167-12185: map_foreachinallarea, the players in the map's BLOCK LIST on that
    ///                       cell) and getareaunits(BL_PC, …) (script.cpp:12192-12279: every player SESSION, pc_db,
    ///                       map.cpp:2644 / 2175, whose m / x / y are that cell, on a map or not). A player in the block
    ///                       list is a session of that cell, so equal counts mean that every session of that cell is on
    ///                       the map, this character among them; fewer in the block list means that someone of that cell
    ///                       is between two maps (pc_setpos takes the player off its map and then sets sd->m / x / y,
    ///                       pc.cpp:7116-7125; the player is in a block list again at its client's LoadEndAck,
    ///                       clif.cpp:10815), maybe this character: it waits (it is looked at again 5 s later).
    ///                       getareaunits is asked only when getareausers found someone: it ends the script on a map
    ///                       name it does not know (script.cpp:12220-12224), and the timer would not be started again;
    ///       PvP map         getmapflag(map, MF_PVP) (script.cpp:13584) and getbattleflag("pvp_exp") (script.cpp:17053;
    ///                       battle.cpp:8296).
    ///     A character that CAN gain is given its EXP once and its row is granted WHATEVER the EXP then did: at its base
    ///     level's cap with its base EXP full, pc_gainexp cuts the amount to nothing (pc.cpp:8482-8487; the job EXP alike,
    ///     pc.cpp:8488-8493) and the character is done. (Until port wave I the script compared the EXP before and after
    ///     getexp and gave the row back when nothing had changed: a character at both caps was tried again every 5 s for
    ///     ever, and each try gave its homunculus 10 % of the amount: conf/battle/homunc.conf homunculus_exp_gain.)
    /// Granted once: the map-server runs scripts one at a time and only this NPC changes `granted`, from 0 to 1, before
    /// getexp; nothing sets it back. A map-server crash between the two loses that one reward (at most once, never twice); a
    /// crash before rAthena's next character save loses the EXP as it loses everything else since that save.
    /// </summary>
    public const string Script = @"//===== Loki Host: Horde's reward (client wave 8) =============
//= Installed by Loki Host into its own copy at every Start; do not edit
//= (the next Start writes it again). Loki Host writes one row per player
//= into `loki_horde_reward` when a Horde run ends; this NPC gives that
//= row's EXP to the character once, with getexp, when the character can
//= gain EXP: online, alive, on a map, not on a PvP map without pvp_exp.
//= Until then the row waits and getexp is not called.
//= The shares are in 1/100 % of what the character needs for its next
//= base / job level (Loki's own reward rule, decided by the host).
//============================================================
-	script	LokiHordeReward	-1,{
OnInit:
	initnpctimer;
	end;

OnTimer5000:
	.@n = query_sql(""SELECT r.`id`, r.`account_id`, r.`char_id`, r.`base_share`, r.`job_share` FROM `loki_horde_reward` r JOIN `char` c ON c.`char_id` = r.`char_id` WHERE r.`granted` = 0 AND c.`online` = 1 ORDER BY r.`id` LIMIT 50"", .@id, .@aid, .@cid, .@bs, .@js);
	for (.@i = 0; .@i < .@n; .@i++) {
		if (!isloggedin(.@aid[.@i], .@cid[.@i]))
			continue;
		if (!attachrid(.@aid[.@i]))
			continue;
		if (getcharid(0) == .@cid[.@i]) {
			// Can this character gain EXP now? pc_gainexp gives nothing to a dead character, to one between two
			// maps, or on a PvP map without pvp_exp: then the row waits for a later round and getexp is NOT called.
			.@can = !isdead();
			if (.@can && getmapxy(.@m$, .@x, .@y) != 0)
				.@can = 0;
			if (.@can) {
				// on its map: everyone whose place is this cell is in the map's block list
				.@on = getareausers(.@m$, .@x, .@y, .@x, .@y);
				if (.@on < 1)
					.@can = 0;
				else if (.@on != getareaunits(BL_PC, .@m$, .@x, .@y, .@x, .@y))
					.@can = 0;
				else if (getmapflag(.@m$, MF_PVP) && !getbattleflag(""pvp_exp""))
					.@can = 0;
			}
			if (.@can) {
				// NextBaseExp / NextJobExp: the EXP of the character's next level in rAthena's exp
				// tables (db/re/job_exp.yml). At the class's highest level they are NOT a level's EXP but
				// the cap a character can hold (99,999,999 / 999,999,999): nothing to gain, so no share.
				.@nb = NextBaseExp;
				.@nj = NextJobExp;
				.@base = 0;
				.@job = 0;
				if (.@nb > 0 && .@nb != 99999999)
					.@base = .@nb * .@bs[.@i] / 10000;
				if (.@nj > 0 && .@nj != 999999999)
					.@job = .@nj * .@js[.@i] / 10000;
				query_sql ""UPDATE `loki_horde_reward` SET `granted` = 1, `granted_at` = NOW(), `base_exp` = "" + .@base + "", `job_exp` = "" + .@job + "" WHERE `id` = "" + .@id[.@i] + "" AND `granted` = 0"";
				if (.@base > 0 || .@job > 0) {
					.@bl = BaseLevel; .@be = BaseExp; .@jl = JobLevel; .@je = JobExp;
					getexp .@base, .@job;
					// granted, whatever the EXP did (a character at its level's cap is done: it is never tried again)
					if (BaseLevel != .@bl || BaseExp != .@be || JobLevel != .@jl || JobExp != .@je)
						dispbottom ""Horde: +"" + .@base + "" base EXP, +"" + .@job + "" job EXP."";
				}
			}
		}
		detachrid;
	}
	detachrid;
	initnpctimer;
	end;
}
";

    /// <summary>Writes the script into the copy and its line into conf/import/map_conf.txt (map.cpp:4170 map_config_read reads
    /// "npc:" there as in npc/scripts_*.conf); removes the line when <paramref name="enabled"/> is false.</summary>
    public static void Install(string hostDir, bool enabled)
    {
        var import = Path.Combine(hostDir, "conf", "import", "map_conf.txt");
        var lines = File.Exists(import) ? File.ReadAllLines(import).ToList() : new List<string>();
        lines.RemoveAll(l => l.Trim().Equals(ImportLine, StringComparison.OrdinalIgnoreCase));
        if (enabled)
        {
            var file = Path.Combine(hostDir, RelPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Script.Replace("\r\n", "\n"));
            lines.Add(ImportLine);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(import)!);
        File.WriteAllLines(import, lines);
    }

    /// <summary>The copy's town maps: its "&lt;map&gt; mapflag town" lines (npc/mapflag/town.txt, npc/re/mapflag/town.txt).</summary>
    public static List<string> Towns(string hostDir)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in new[] { "npc/mapflag/town.txt", "npc/re/mapflag/town.txt", "npc/pre-re/mapflag/town.txt" })
        {
            var f = Path.Combine(hostDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(f)) continue;
            foreach (var raw in File.ReadLines(f))
            {
                var l = raw.Trim();
                if (l.StartsWith("//")) continue;
                var parts = l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[1].Equals("mapflag", StringComparison.OrdinalIgnoreCase) && parts[2].Equals("town", StringComparison.OrdinalIgnoreCase))
                    set.Add(parts[0]);
            }
        }
        return set.ToList();
    }
}
