using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Server chat channels (client wave 4, w4social). Protocol side, here: a channel line is ZC_NPC_CHAT 0x02c1 len.W id.L color.L
    /// text with id 0 — rAthena's clif_channel_msg (clif.cpp:6804-6834) is the only sender of a 0x02c1 without a unit id (the others go
    /// through clif_messagecolor_target, id = the unit, clif.cpp:9782-9800); the text is "[Alias] Name : text" (channel_send,
    /// channel.cpp:446-466) or a join / leave notice "[Alias] Name has joined." (channel.cpp:231-236, map_msg 761 / 763), in the
    /// channel's colour (BGR, as every 0x02c1). Those lines go to the chat window's Channel tab. Everything else (join with a password,
    /// leave, the server's list) goes through <see cref="IChatChannels"/> (Net/IChatChannels.cs), rAthena's @commands today.
    /// The channels the player is in are kept by the client: the ones joined here (confirmed by the adapter when the server says so)
    /// plus any channel a line was said to. A console line "#name text" goes to channel #name when the player is in it.
    /// </summary>
    public sealed class Channels
    {
        public readonly List<string> Joined = new List<string>();
        /// <summary>The last channel lines (text, colour) for the window.</summary>
        public readonly List<(string text, string color)> Recent = new List<(string, string)>();
        public event Action Changed;
        public Action<string> Toast;
        /// <summary>Smoke: channel lines seen so far.</summary>
        public int LinesSeen { get; private set; }

        private readonly IChatChannels _ch; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private readonly Action<string, string, ChatKind> _chat; private readonly Action<string> _prefill;

        public Channels(IChatChannels channels, ListWindow win, Func<Camera> head, Action<string, string, ChatKind> chat, Action<string> prefillChat)
        {
            _ch = channels; _win = win; _head = head; _chat = chat; _prefill = prefillChat;
            _win.OnCloseClicked = Hide;
            _ch.Membership += (name, member) =>
            {
                bool had = IndexOf(name) >= 0;
                if (member && !had) Joined.Add(name);
                if (!member && had) Joined.RemoveAt(IndexOf(name));
                Debug.Log($"[Channels] {name}: member {member}");
                Refresh(); Changed?.Invoke();
            };
        }

        public IChatChannels Adapter => _ch;
        public ListWindow Window => _win;
        private int IndexOf(string name) => Joined.FindIndex(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        public bool IsIn(string name) => IndexOf(name) >= 0;
        public void ResetForCharacter() { Joined.Clear(); Recent.Clear(); Changed?.Invoke(); } // rAthena leaves every channel at logout (unit_free → channel_pcquit 0xF, unit.cpp:3946)
        /// <summary>A map change leaves the map channel (pc_setpos → channel_pcquit 4, pc.cpp:7033); the others stay. An autojoin map channel is
        /// joined again at LoadEndAck (clif.cpp:11105-11107) and says so (map_msg 1435, read by the adapter).</summary>
        public void OnMapChange() { int i = IndexOf("#map"); if (i >= 0) { Joined.RemoveAt(i); Changed?.Invoke(); } }

        /// <summary>ZC_NPC_CHAT with id 0: a channel line.</summary>
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x02c1 || n < 12) return false;
            var r = new PacketReader(b, o, n); r.Skip(4); uint gid = r.U32(); uint bgr = r.U32();
            if (gid != 0) return false;
            string text = r.ChatLine(n - 12);
            string col = ((bgr & 0xFF) << 16 | (bgr & 0xFF00) | (bgr >> 16) & 0xFF).ToString("x6");
            LinesSeen++;
            Recent.Add((text, col)); if (Recent.Count > 40) Recent.RemoveAt(0);
            _chat(text, col, ChatKind.Channel);
            Refresh();
            return true;
        }

        public static string Normalize(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 0 && name[0] != '#') name = "#" + name;
            return name;
        }

        public void Join(string name, string password)
        {
            name = Normalize(name); if (name.Length < 2) return;
            _ch.Join(name, password ?? "");
            Debug.Log($"[Channels] join {name}{(string.IsNullOrEmpty(password) ? "" : " (password)")}");
        }
        /// <summary>The server has no @join for this player: a channel without a password is joined by its first line (the whisper route,
        /// clif.cpp:11918-11926); until the player says something there, none of its lines arrive.</summary>
        public void JoinBySaying(string name)
        {
            name = Normalize(name); if (name.Length < 2) return;
            if (!IsIn(name)) Joined.Add(name);
            _chat(Lang.F("Say something in {0} (type  {0} your text) to join it: this server doesn't let you use @join.", name), "ffd060", ChatKind.System);
            Refresh(); Changed?.Invoke();
        }
        public void Leave(string name) { name = Normalize(name); _ch.Leave(name); Debug.Log("[Channels] leave " + name); }
        public void Say(string name, string text)
        {
            name = Normalize(name); if (name.Length < 2 || string.IsNullOrEmpty(text)) return;
            if (!IsIn(name)) { Joined.Add(name); Changed?.Invoke(); } // rAthena joins a channel without a password on the first line (clif.cpp:11918-11926)
            _ch.Say(name, text);
            Debug.Log($"[Channels] say {name}: {text}");
        }
        public void RequestList() { _ch.RequestList(); }

        /// <summary>"#name text" on the console: to that channel when the player is in it.</summary>
        public bool TryConsole(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '#') return false;
            int sp = line.IndexOf(' '); if (sp < 2) return false;
            string name = line.Substring(0, sp);
            if (!IsIn(name)) return false;
            Say(name, line.Substring(sp + 1).Trim());
            return true;
        }

        // ---- the window
        public bool Visible => _win.Visible;
        public void Show() { _win.Open(_head()); _win.Input.SetText(""); Refresh(); }
        public void Hide() { _win.SetInput(false); _win.Hide(); }
        public void Refresh()
        {
            if (!_win.Visible) return;
            _win.SetTitle(Lang.T("Chat channels"));
            _win.SetTabs(null, 0, null);
            _win.SetInfo(Lang.T("Join:"));
            _win.SetInput(true, Lang.T("~input~#channel [password]"), null, SubmitJoin, null, 60);
            var rows = new List<ListWindow.Row>();
            foreach (var j in Joined)
            {
                string c = j;
                rows.Add(new ListWindow.Row { Text = Lang.F("<color=#ffd060>{0}</color>\n<size=19>type  {0} your text  on the console</size>", c), A = Lang.T("Say"), OnA = () => { Hide(); _prefill?.Invoke(c + " "); }, B = Lang.T("Leave"), OnB = () => Leave(c) });
            }
            if (Joined.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>You are in no channel. Type a channel's name (like #global) above and press Join.</color>") });
            for (int i = Mathf.Max(0, Recent.Count - 6); i < Recent.Count; i++)
                rows.Add(new ListWindow.Row { Text = $"<size=19><color=#{Recent[i].color}>{UiKit.RoColors(Recent[i].text)}</color></size>" });
            _win.SetFooter((Lang.T("Join"), SubmitJoin), (Lang.T("Server list"), RequestList));
            _win.SetRows(rows);
        }
        private void SubmitJoin()
        {
            string t = (_win.Input.Text ?? "").Trim(); if (t.Length == 0) return;
            int sp = t.IndexOf(' ');
            Join(sp < 0 ? t : t.Substring(0, sp), sp < 0 ? "" : t.Substring(sp + 1).Trim());
            _win.Input.SetText("");
        }
    }
}
