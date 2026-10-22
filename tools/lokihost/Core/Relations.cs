using MySqlConnector;

namespace LokiHost.Core;

/// <summary>
/// Client wave 18 (w18share), the design's point 33 ("can be toggled for privacy to only be visible to friends, family, or clan members";
/// design note at the merge: "I meant my Guild"):
/// who a character is to another in rAthena's own terms, for the headset relay's filter of casting menus (PoseRelay.Allowed). One
/// character's row: its id, its spouse, its parents and its adopted child, its guild, and every character on a friend list with it.
/// </summary>
public sealed record CharRelations(uint CharId, uint Partner, uint Father, uint Mother, uint Child, uint Guild, IReadOnlySet<uint> Friends)
{
    /// <summary>On a friend list with the other: rAthena keeps a list per character (`friends`: char_id → friend_id); an accepted
    /// request puts the acceptor on the asker's list (map/clif.cpp clif_parse_FriendsListReply) and, with friend_auto_add, the asker on
    /// the acceptor's: either list counts, both players agreed.</summary>
    public bool FriendOf(CharRelations o) => Friends.Contains(o.CharId) || o.Friends.Contains(CharId);
    /// <summary>Family: the spouse (`char`.partner_id, set by marriage: map/pc.cpp pc_marriage), the parents and the child of an adoption
    /// (`char`.father / mother / child: pc_adoption), either way round.</summary>
    public bool FamilyOf(CharRelations o) => Holds(o.CharId) || o.Holds(CharId);
    bool Holds(uint id) => id != 0 && (Partner == id || Father == id || Mother == id || Child == id);
    /// <summary>The same guild (`char`.guild_id, 0: none). rAthena (char/int_guild.cpp) writes it: a NEW member's at the guild's next save
    /// (inter_guild_tosql GS_MEMBER_NEW, :245-249; guild_save_timer saves one guild per char autosave_time / guilds loaded, conf/char_athena.conf
    /// autosave_time 60 s) or that character's next save (char/char.cpp:309 char_mmo_char_tosql); leaving, expulsion (:93-99
    /// inter_guild_removemember_tosql, :1315) and a broken guild (:1455) set 0 at once.</summary>
    public bool GuildOf(CharRelations o) => Guild != 0 && Guild == o.Guild;
}

/// <summary>Where the relay learns relations: Loki Host's rAthena database (<see cref="RelationsDb"/>), or a test's table.</summary>
public interface IRelationsSource
{
    /// <summary>The character a connection plays: account <paramref name="aid"/>, the name from its HELLO. Null: no such character.</summary>
    Task<CharRelations?> LoadAsync(uint aid, string name);
}

/// <summary>
/// The relations in rAthena's own tables (sql-files/main.sql): `char` (char_id, account_id, name: UNIQUE name_key, partner_id, father,
/// mother, child, guild_id, online) and `friends` (char_id, friend_id: PRIMARY KEY both). Read only. rAthena writes them when it saves a
/// character: an accepted friend or a wedding reach the database at that character's next save (conf/map_athena.conf
/// autosave_time 300: each one within 5 minutes; at once on logout, a map-server change, @save: map/atcommand.cpp ACMD_FUNC(save)
/// chrif_save; an adoption saves all three at once: map/pc.cpp pc_adoption chrif_save); a new guild member within about a minute (see
/// <see cref="CharRelations.GuildOf"/>), a member who left at once.
/// The character: the account's row with the HELLO's name, else (a name whose letters the client and the database spell differently)
/// the account's character that is online now (char/char.cpp char_set_char_online sets `online` 1).
/// </summary>
public sealed class RelationsDb : IRelationsSource
{
    readonly string _conn;
    public RelationsDb(string connectionString) { _conn = connectionString; }

    public async Task<CharRelations?> LoadAsync(uint aid, string name)
    {
        await using var c = new MySqlConnection(_conn);
        await c.OpenAsync();
        uint cid, partner, father, mother, child, guild;
        await using (var q = new MySqlCommand("SELECT `char_id`, `partner_id`, `father`, `mother`, `child`, `guild_id` FROM `char` WHERE `account_id` = @a AND (`name` = @n OR `online` = 1) ORDER BY (`name` = @n) DESC, `online` DESC LIMIT 1", c))
        {
            q.Parameters.AddWithValue("@a", aid); q.Parameters.AddWithValue("@n", name);
            await using var r = await q.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            cid = Convert.ToUInt32(r.GetValue(0)); partner = Convert.ToUInt32(r.GetValue(1)); father = Convert.ToUInt32(r.GetValue(2));
            mother = Convert.ToUInt32(r.GetValue(3)); child = Convert.ToUInt32(r.GetValue(4)); guild = Convert.ToUInt32(r.GetValue(5));
        }
        var friends = new HashSet<uint>();
        await using (var q = new MySqlCommand("SELECT `friend_id` FROM `friends` WHERE `char_id` = @c UNION SELECT `char_id` FROM `friends` WHERE `friend_id` = @c", c))
        {
            q.Parameters.AddWithValue("@c", cid);
            await using var r = await q.ExecuteReaderAsync();
            while (await r.ReadAsync()) friends.Add(Convert.ToUInt32(r.GetValue(0)));
        }
        return new CharRelations(cid, partner, father, mother, child, guild, friends);
    }
}
