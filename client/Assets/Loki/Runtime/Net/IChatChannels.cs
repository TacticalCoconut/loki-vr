using System;

namespace Loki.Net
{
    /// <summary>
    /// Server chat channels (client wave 4, w4social): the parts the RO protocol has no packet for. What IS in the protocol stays in
    /// gameplay code (Game/Channels.cs): a channel's lines arrive as ZC_NPC_CHAT 0x02c1 with id 0 ("[Alias] Name : text", the channel's
    /// colour; rAthena clif_channel_msg, clif.cpp:6804-6834 — every other 0x02c1 carries a unit id), and a line is said by whispering
    /// to "#name" (CZ_WHISPER 0x0096; rAthena clif_parse_WisMessage joins a channel without a password on the way, clif.cpp:11914-11930).
    /// Joining with a password, leaving and listing exist on rAthena only as @commands with text answers (atcommand.cpp:10434-10444
    /// "@join", 10536-10584 "@channel leave / list"; channel.cpp:727-829 prints map_msg 1403 "You're now in the '%s' channel." and 1426
    /// "You've left the '%s' channel."). This interface is the thinnest adapter for those parts: <see cref="RathenaChatChannels"/> today;
    /// the SpacetimeDB module can implement it with its own channel table (join / leave reducers, a membership row per player) and
    /// send the same 0x02c1 lines, without any gameplay change.
    /// </summary>
    public interface IChatChannels
    {
        /// <summary>For logs: "rAthena @commands" …</summary>
        string Describe { get; }
        /// <summary>Join a channel ("#name"); the password may be empty.</summary>
        void Join(string channel, string password);
        void Leave(string channel);
        /// <summary>One line to a channel the player is in (or one without a password: rAthena joins it on the way).</summary>
        void Say(string channel, string text);
        /// <summary>Ask the server for its public channels (rAthena answers in chat lines).</summary>
        void RequestList();
        /// <summary>The server confirmed a membership change: (channel, now a member). A server that cannot say leaves it to the caller.</summary>
        event Action<string, bool> Membership;
        /// <summary>Feed for adapters that learn membership from the server's own messages (rAthena: map_msg 1403 / 1426 / 1434 / 1425).
        /// Called with every server line addressed to the player (0x008e without a speaker, 0x02c1 to self).</summary>
        void OnServerLine(string text);
    }

    /// <summary>
    /// rAthena's side of <see cref="IChatChannels"/>: join / leave / list are the @commands every player may use (conf/groups.yml gives
    /// "join" and "channel" to group 0), a line is the whisper to "#name" (a real packet). Membership is read from the server's
    /// English texts map_msg 1403 / 1435 / 1426 / 1434 / 1425 (conf/msg_conf/map_msg.conf:1658, 1690, 1681, 1689, 1680) — the only place rAthena
    /// says it; with another server language the membership stays what the player asked for.
    /// </summary>
    public sealed class RathenaChatChannels : IChatChannels
    {
        private readonly Action<string> _command; private readonly Action<string, string> _whisper;
        public RathenaChatChannels(Action<string> sendChatLine, Action<string, string> whisper) { _command = sendChatLine; _whisper = whisper; }
        public string Describe => "rAthena (@join / @channel, whisper to #channel)";
        public event Action<string, bool> Membership;

        public void Join(string channel, string password) => _command("@join " + channel + (string.IsNullOrEmpty(password) ? "" : " " + password));
        public void Leave(string channel) => _command("@channel leave " + channel);
        public void Say(string channel, string text) => _whisper(channel, text);
        public void RequestList() => _command("@channel list");

        public void OnServerLine(string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            // 1403 "You're now in the '%s' channel." / 1434 "You're already in the '%s' channel." / 1426 "You've left the '%s' channel." /
            // 1425 "You're not part of the '%s' channel."
            // 1435 "You're now in the '#%s' channel for '%s'." (the map channel, channel.cpp:264-268)
            string ch = Quoted(t, "You're now in the '", "' channel.") ?? Quoted(t, "You're now in the '", "' channel for ") ?? Quoted(t, "You're already in the '", "' channel.");
            if (ch != null) { Membership?.Invoke(ch, true); return; }
            ch = Quoted(t, "You've left the '", "' channel.") ?? Quoted(t, "You're not part of the '", "' channel.");
            if (ch != null) Membership?.Invoke(ch, false);
        }

        private static string Quoted(string t, string head, string tail)
        {
            int a = t.IndexOf(head, StringComparison.Ordinal); if (a < 0) return null;
            int s = a + head.Length, b = t.IndexOf(tail, s, StringComparison.Ordinal);
            return b > s ? t.Substring(s, b - s) : null;
        }
    }
}
