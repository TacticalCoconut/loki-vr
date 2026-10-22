using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// The VR stand-in for the official client's right-click menu on a player: the trigger on a player (laser or gaze lock)
    /// opens it. Every action is a request rAthena already parses, sent the way the other windows send it:
    ///   Whisper → the chat console prefilled with "/w Name : " and the VR keyboard (CZ_WHISPER 0x0096 on Enter)
    ///   Trade → CZ_REQ_EXCHANGE_ITEM 0x00e4 aid.L (Trade.Request)          Party invite → CZ_PARTY_JOIN_REQ 0x00fc aid.L (leader only)
    ///   Add friend → CZ_ADD_FRIENDS 0x0202 name[24]                        Guild invite → CZ_REQ_JOIN_GUILD 0x0168 (a member who may invite)
    ///   Guild alliance / antagonist → 0x0170 / 0x0180 (the guild master; the server re-checks both guilds)
    ///   View equipment → CZ_EQUIPWIN_MICROSCOPE 0x02d6 aid.L (clif_parse_ViewPlayerEquip): same map, and the other player's
    ///     "show equipment" setting (or a GM permission) — else ZC_MSG MSI_OPEN_EQUIPEDITEM_REFUSED. The answer is
    ///     ZC_EQUIPWIN_MICROSCOPE 0x0b37 len.W name[24] job.W head.W accessory.W accessory2.W accessory3.W robe.W headpalette.W
    ///     bodypalette.W body2.W sex.B then EQUIPITEM_INFO[] (68 bytes, as in the inventory) — clif_viewequip_ack, packets_struct.hpp:1308.
    ///   Walk there.
    /// </summary>
    public sealed class PlayerMenu
    {
        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head;
        private uint _aid; private string _name = ""; private Unit _unit;
        private bool _showingEquip; private string _equipName = "";
        private readonly List<Inventory.Entry> _equip = new List<Inventory.Entry>();

        public Trade Trade; public Party Party; public SocialHub Social; public Guild Guild;
        public Action<string> PrefillChat; public Action<Unit> WalkTo; public Action<string> Toast;
        /// <summary>Client wave 2: more rows for this player (text, button, action) — join their chat room, block whispers, adopt…</summary>
        public Action<Unit, Action<string, string, Action>> Extra;
        /// <summary>For the smoke: the last menu's labels and the last equipment view.</summary>
        public readonly List<string> Labels = new List<string>();
        public string LastEquipOf { get; private set; } = ""; public int LastEquipCount { get; private set; } = -1;
        public uint TargetAid => _aid;
        public bool Visible => _win.Visible;
        /// <summary>Whether the window shows the equipment view (0x0b37) rather than the actions.</summary>
        public bool ShowingEquipment => _showingEquip && _win.Visible;
        public ListWindow Window => _win;

        public PlayerMenu(RoSession s, ListWindow win, Func<Camera> head)
        {
            _s = s; _win = win; _head = head;
            _win.OnCloseClicked = Close;
        }

        public void Reset() { _win.Hide(); _unit = null; _aid = 0; _showingEquip = false; }
        public void Close() { _win.Hide(); _showingEquip = false; }

        /// <summary>Opens the menu for a player unit (not ourselves).</summary>
        public void Open(Unit u)
        {
            if (u == null || u.IsSelf) return;
            _unit = u; _aid = u.Id; _name = u.Name ?? ""; _showingEquip = false;
            Build();
            _win.Open(_head());
            Debug.Log($"[PlayerMenu] {_name} ({_aid}): {string.Join(" | ", Labels)}");
        }

        private void Build()
        {
            var rows = new List<ListWindow.Row>();
            Labels.Clear();
            // w4lang: text and button come translated; Labels keeps the button's English label (the smoke presses by it, Lang.SourceOf)
            void Add(string text, string button, Action a) { rows.Add(new ListWindow.Row { Text = text, A = button, OnA = a }); Labels.Add(Lang.SourceOf(button)); }
            Add(Lang.T("Send a private message"), Lang.T("Whisper"), Whisper);
            Add(Lang.T("Ask to trade items and zeny"), Lang.T("~menu~Trade"), DoTrade);
            if (Party != null && Party.InParty && Party.IsLeader && !Party.Members.ContainsKey(_aid)) Add(Lang.F("Invite to your party '{0}'", Party.Name), Lang.T("Invite"), PartyInvite);
            Add(Lang.T("Send a friend request"), Lang.T("Add friend"), FriendAdd);
            if (Guild != null && Guild.InGuild && Guild.CanInvite && (_unit == null || _unit.GuildId != Guild.Id)) Add(Lang.F("Invite to your guild '{0}'", Guild.Name), Lang.T("Guild inv."), GuildInvite);
            bool otherGuild = _unit != null && (_unit.GuildId != 0 || _unit.GuildName.Length > 0) && (_unit.GuildId == 0 || _unit.GuildId != Guild?.Id);
            if (Guild != null && Guild.InGuild && Guild.AmMaster && otherGuild)
            {
                Add(_unit.GuildName.Length > 0 ? Lang.F("Ask '{0}' for an alliance", _unit.GuildName) : Lang.T("Ask their guild for an alliance"), Lang.T("Ally"), () => { Guild.RequestAlly(_aid); Done(Lang.T("Alliance request sent.")); });
                Add(Lang.T("Declare their guild an antagonist"), Lang.T("Antagonist"), () => { Guild.RequestHostile(_aid); Done(Lang.T("Antagonist request sent.")); });
            }
            Extra?.Invoke(_unit, (t, l, a) => Add(t, l, () => { a?.Invoke(); if (_win.Visible && !_showingEquip) Close(); }));
            Add(Lang.T("See what they are wearing"), Lang.T("Equipment"), ViewEquip);
            Add(Lang.T("Walk up to them"), Lang.T("Walk"), () => { WalkTo?.Invoke(_unit); Close(); });
            string guild = _unit != null && _unit.GuildName.Length > 0 ? "  ·  " + Lang.F("guild {0}", _unit.GuildName) : "";
            string party = _unit != null && _unit.PartyName.Length > 0 ? "  ·  " + Lang.F("party {0}", _unit.PartyName) : "";
            _win.SetTitle(NpcDialog.CleanName(_name));
            _win.SetInfo($"{Loki.UI.LoginPanel.JobName(_unit != null ? _unit.Job : 0)}{party}{guild}");
            _win.SetInput(false);
            _win.SetTabs(null, 0, null);
            _win.SetFooter((Lang.T("Close"), Close));
            _win.SetRows(rows, true);
        }

        private void Done(string msg) { Toast?.Invoke(msg); Close(); }

        // ---- actions (public: the smoke presses them by name)
        public void Whisper() { PrefillChat?.Invoke("/w " + _name + " : "); Debug.Log("[PlayerMenu] whisper " + _name); Close(); }
        public void DoTrade() { Trade?.Request(_aid, _name); Close(); }
        public void PartyInvite() { Party?.InviteByAid(_aid); Debug.Log($"[PlayerMenu] party invite {_aid}"); Done(Lang.F("Party invitation sent to {0}.", _name)); }
        public void FriendAdd() { Social?.AddFriend(_name); Close(); }
        public void GuildInvite() { Guild?.InviteByAid(_aid); Debug.Log($"[PlayerMenu] guild invite {_aid}"); Done(Lang.F("Guild invitation sent to {0}.", _name)); }
        public void ViewEquip()
        {
            _s.SendPacket(new PacketWriter(0x02d6).U32(_aid).ToArray());
            Debug.Log($"[PlayerMenu] view equipment of {_aid}");
            _win.SetInfo(Lang.T("Asking to see their equipment…"));
        }
        /// <summary>Test hook: runs the action whose button reads <paramref name="label"/>; false if the menu has none.</summary>
        public bool Press(string label)
        {
            int i = Labels.IndexOf(label);
            if (i < 0 || !_win.Visible) return false;
            _win.Press(i); return true;
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x0b37) return false;
            var r = new PacketReader(b, o, n);
            r.Skip(4); _equipName = r.Str(24); int job = r.S16(); r.Skip(2 * 8); r.U8(); // head, 3 accessories, robe, 2 palettes, body2, sex
            _equip.Clear();
            while (r.Remaining >= 68) { var e = Inventory.ReadEquip(ref r); if (!_equip.Exists(x => x.Index == e.Index)) _equip.Add(e); } // one entry per slot: a two-handed weapon or a multi-slot headgear comes twice with the same index (clif.cpp:10352-10373)
            LastEquipOf = _equipName; LastEquipCount = _equip.Count;
            Debug.Log($"[PlayerMenu] equipment of {_equipName} (job {job}): {_equip.Count} item(s): {string.Join(", ", _equip.ConvertAll(e => GameDb.GetItem(e.ItemId).English + "@" + EquipSwitch.SlotName(e.Wearing)))}");
            ShowEquip(job);
            return true;
        }

        private void ShowEquip(int job)
        {
            _showingEquip = true;
            var rows = new List<ListWindow.Row>();
            var list = new List<Inventory.Entry>(_equip); list.Sort((a, c) => a.Wearing.CompareTo(c.Wearing));
            foreach (var e in list)
                rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(e.Info), Text = $"{Inventory.FullName(e)}  <color=#9aa3b5>{EquipSwitch.SlotName(e.Wearing)}</color>" });
            if (rows.Count == 0) rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Nothing equipped.</color>") });
            _win.SetTitle(Lang.F("{0}'s equipment", NpcDialog.CleanName(_equipName)));
            _win.SetInfo(Loki.UI.LoginPanel.JobName(job));
            _win.SetFooter((Lang.T("Close"), Close), (Lang.T("Back"), () => { if (_unit != null) Build(); }));
            _win.SetRows(rows, true);
            _win.Open(_head());
        }
    }
}
