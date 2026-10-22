using System;

namespace Loki.Net
{
    /// <summary>
    /// Duels (client wave 4, w4social; the re-audit's §3 leak): the parts of a duel the RO protocol has no packet for. What IS in the
    /// protocol stays in gameplay code: being in a duel is ZC_MAPPROPERTY_R2 0x099b type 1 (FREEPVPZONE) with flag bit 0 ("attack
    /// non-party players") and without bit 5 (COUNT_PK, the PvP-map counter) — rAthena sends it when a duel is created or accepted and
    /// type 0 when the player leaves (duel.cpp:176, 269, 243; the flag bits clif.cpp:6890-6900); the duel ends by itself on death
    /// (pc.cpp:9858-9864, duel_autoleave_when_die) and on any warp (unit_remove_map_ → duel_leave, unit.cpp:3687-3688, which sends the
    /// type 0 as well).
    /// Creating, inviting, accepting, refusing and leaving exist on rAthena only as @commands (atcommand.cpp @duel / @invite /
    /// @accept / @reject / @leave), and the invitation reaches the invitee only as a text (map_msg 374, a BC_BLUE broadcast,
    /// duel.cpp:201-202). This interface is the thinnest adapter for those: <see cref="RathenaDuelChannel"/> today; the SpacetimeDB
    /// module can implement it with a duel table (reducers create / invite / accept / reject / leave, an invitation row the client
    /// subscribes to) and send the same 0x099b, with no gameplay change.
    /// </summary>
    public interface IDuelChannel
    {
        string Describe { get; }
        /// <summary>Open a duel others can be invited to.</summary>
        void Create();
        /// <summary>Invite a player into our duel; <paramref name="newDuel"/> opens a two-player duel with them first.</summary>
        void Invite(string characterName, bool newDuel = false);
        void Accept();
        void Reject();
        void Leave();
        /// <summary>Ask for the duel's member list (rAthena answers in chat lines).</summary>
        void ShowInfo();
        /// <summary>Someone invited us: their character name.</summary>
        event Action<string> Invited;
        /// <summary>The server refused a duel request outright (e.g. the player may not use duels on this server): a text for the player.</summary>
        event Action<string> Refused;
        /// <summary>Feed for adapters that read the server's own messages (rAthena: the invitation text). Called with every server line
        /// addressed to the player (broadcasts 0x009a, 0x008e without a speaker, 0x02c1 to self).</summary>
        void OnServerLine(string text);
    }

    /// <summary>
    /// rAthena's side of <see cref="IDuelChannel"/>: the @commands (conf/msg_conf/map_msg.conf 350-377 answer them), and the one text
    /// that carries a duel invitation, map_msg 374 "Blue -- Player %s invites you to PVP duel (Use @accept/@reject) --" (sent with
    /// clif_broadcast BC_BLUE, which prefixes "blue"). Stock rAthena gives the duel @commands to no player group (conf/groups.yml
    /// lists none of them): on such a server the command comes back as the player's own public chat line, which the game reports
    /// through <see cref="Refused"/> (LokiGame.W4Social.cs, the command guard).
    /// </summary>
    public sealed class RathenaDuelChannel : IDuelChannel
    {
        private readonly Action<string> _command;
        public RathenaDuelChannel(Action<string> sendCommand) { _command = sendCommand; }
        public string Describe => "rAthena (@duel / @invite / @accept / @reject / @leave)";
        public event Action<string> Invited;
        public event Action<string> Refused;

        public void Create() => _command("@duel");
        /// <summary>"@invite Name", or "@duel Name" for a new two-player duel with them (atcommand.cpp ACMD_FUNC(duel): duel_create(sd, 2) then duel_invite).</summary>
        public void Invite(string characterName, bool newDuel = false) { if (!string.IsNullOrWhiteSpace(characterName)) _command((newDuel ? "@duel " : "@invite ") + characterName.Trim()); }
        public void Accept() => _command("@accept");
        public void Reject() => _command("@reject");
        public void Leave() => _command("@leave");
        public void ShowInfo() => _command("@duel");
        /// <summary>The game's command guard saw a duel command come back as public chat.</summary>
        public void CommandRefused(string text) => Refused?.Invoke(text);

        public void OnServerLine(string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            // map_msg 374 (English): "Blue -- Player %s invites you to PVP duel (Use @accept/@reject) --"
            int k = t.IndexOf(" invites you to PVP duel", StringComparison.Ordinal);
            if (k < 0) return;
            int a = t.LastIndexOf("Player ", k, StringComparison.Ordinal);
            string who = a >= 0 ? t.Substring(a + 7, k - a - 7) : "?";
            Invited?.Invoke(who);
        }
    }
}
