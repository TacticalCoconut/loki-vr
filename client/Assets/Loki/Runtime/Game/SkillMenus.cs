using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// The skill menus rAthena opens and then waits on (rAthena e985006, PACKETVER 20211103). While one is unanswered the server
    /// keeps sd->menuskill_id set and clif_parse_skill_toid refuses every other skill ("Can't use skills while a menu is open",
    /// clif.cpp:12947-12955), so every way out of these windows answers:
    ///   0x0afb ZC_AUTOSPELLLIST len.W {skill id.L} (Hindsight / Auto Spell, clif_autospell; menuskill SA_AUTOSPELL)
    ///          → CZ_SELECTAUTOSPELL 0x01ce skill id.L (clif_parse_AutoSpell; roBrowserLegacy answers this one too). Cancel sends 0:
    ///          skill_autospell returns at once for id 0 and the menu is cleared.
    ///   0x0442 ZC_SKILL_SELECT_REQUEST len.W why.L {skill id.W} (Auto Shadow Spell, clif_autoshadowspell_list, why 1)
    ///          → CZ_SKILL_SELECT_RESPONSE 0x0443 why.L skill id.W (clif_parse_SkillSelectMenu). Cancel sends 0: skill_select_menu
    ///          ends SC_STOP and returns, the menu is cleared.
    ///   0x0b65 ZC_REPAIRITEMLIST len.W {index.W itemId.L card[4].L refine.B grade.B} (24 bytes, REPAIRITEM_INFO2; Repair Weapon,
    ///          clif_item_repair_list — the index is the target's raw inventory index) → CZ_REQ_ITEMREPAIR2 0x0b66 + that entry
    ///          (clif_parse_RepairItem reads item.index, then clears the menu). Cancel sends index 0xFFFF, which skill_repairweapon
    ///          reads as "No item selected ('Cancel' clicked)".
    ///   0x01fe ZC_ACK_ITEMREPAIR index.W result.B (0 repaired, 1 failed).
    ///   0x011e ZC_ACK_REMEMBER_WARPPOINT type.B (0 saved, 1 skill level too low, 2 Warp Portal not learned) — the answer to
    ///          CZ_REMEMBER_WARPPOINT 0x011d (/memo, pc_memo); msgstringtable 217 / 214 / 216 as roBrowserLegacy shows them.
    /// </summary>
    public sealed class SkillMenus
    {
        private enum Kind { None, AutoSpell, SkillSelect, Repair }
        private struct RepairEntry { public int Index, ItemId, Refine, Grade; public uint[] Cards; }
        private readonly RoSession _s; private readonly ListWindow _win; private readonly Func<Camera> _head; private readonly Action<string, string> _chat;
        private Kind _kind; private int _why;
        private readonly List<int> _skills = new List<int>();
        private readonly List<RepairEntry> _repair = new List<RepairEntry>();
        public bool IsOpen => _kind != Kind.None;
        public ListWindow Window => _win;
        public string OpenKind => _kind.ToString();
        public int OptionCount => _kind == Kind.Repair ? _repair.Count : _skills.Count;
        /// <summary>For the smoke: the last reply sent ("0x01ce 19"...), the last repair and memo results.</summary>
        public string LastReply { get; private set; } = ""; public int LastRepairResult { get; private set; } = -1; public int LastMemoResult { get; private set; } = -1;

        public SkillMenus(RoSession s, ListWindow win, Func<Camera> head, Action<string, string> chat)
        {
            _s = s; _win = win; _head = head; _chat = chat;
            _win.OnCloseClicked = Cancel;
        }

        /// <summary>A map change clears menuskill on the server.</summary>
        public void Reset() { _kind = Kind.None; _win.Hide(); }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x0afb:
                {
                    r.Skip(4); _skills.Clear();
                    while (r.Remaining >= 4) { int sk = r.S32(); if (sk > 0) _skills.Add(sk); }
                    _kind = Kind.AutoSpell; Refresh(); _win.Open(_head());
                    Debug.Log($"[SkillMenus] auto spell list: {string.Join(", ", _skills.ConvertAll(k => SkillDb.Get(k).Aegis))}");
                    return true;
                }
                case 0x0442:
                {
                    r.Skip(4); _why = r.S32(); _skills.Clear();
                    while (r.Remaining >= 2) { int sk = r.S16(); if (sk > 0) _skills.Add(sk); }
                    _kind = Kind.SkillSelect; Refresh(); _win.Open(_head());
                    Debug.Log($"[SkillMenus] skill select (why {_why}): {string.Join(", ", _skills.ConvertAll(k => SkillDb.Get(k).Aegis))}");
                    return true;
                }
                case 0x0b65:
                {
                    r.Skip(4); _repair.Clear();
                    while (r.Remaining >= 24)
                    {
                        var e = new RepairEntry { Index = r.S16(), ItemId = (int)r.U32(), Cards = new[] { r.U32(), r.U32(), r.U32(), r.U32() } };
                        e.Refine = r.U8(); e.Grade = r.U8();
                        _repair.Add(e);
                    }
                    _kind = Kind.Repair; Refresh(); _win.Open(_head());
                    Debug.Log($"[SkillMenus] repair list: {string.Join(", ", _repair.ConvertAll(e => e.Index + ":" + GameDb.GetItem(e.ItemId).English))}");
                    return true;
                }
                case 0x01fe:
                {
                    r.Skip(2); int idx = r.S16(); int res = r.U8(); LastRepairResult = res;
                    _chat(res == 0 ? Lang.T("The item has been repaired.") : Lang.T("The repair failed."), res == 0 ? "9fe39a" : "ffb070");
                    Debug.Log($"[SkillMenus] repair result {res} (index {idx})");
                    return true;
                }
                case 0x011e:
                {
                    r.Skip(2); int type = r.U8(); LastMemoResult = type;
                    _chat(ClientText.Msg(type == 0 ? 217 : type == 1 ? 214 : 216), type == 0 ? "8ad6ff" : "ff7a6a");
                    Debug.Log($"[SkillMenus] memo result {type}");
                    return true;
                }
                default: return false;
            }
        }

        private void Pick(int i)
        {
            switch (_kind)
            {
                case Kind.AutoSpell: if (i < _skills.Count) SendAutoSpell(_skills[i]); break;
                case Kind.SkillSelect: if (i < _skills.Count) SendSkillSelect(_skills[i]); break;
                case Kind.Repair: if (i < _repair.Count) SendRepair(_repair[i]); break;
            }
            _kind = Kind.None; _win.Hide();
        }

        private void Cancel()
        {
            switch (_kind)
            {
                case Kind.AutoSpell: SendAutoSpell(0); break;
                case Kind.SkillSelect: SendSkillSelect(0); break;
                case Kind.Repair: SendRepair(new RepairEntry { Index = 0xFFFF, Cards = new uint[4] }); break; // "No item selected ('Cancel' clicked)" (skill_repairweapon)
            }
            Debug.Log($"[SkillMenus] cancelled {_kind}");
            _kind = Kind.None; _win.Hide();
        }

        private void SendAutoSpell(int skill) { _s.SendPacket(new PacketWriter(0x01ce).U32((uint)skill).ToArray()); LastReply = $"0x01ce {skill}"; Debug.Log("[SkillMenus] sent " + LastReply); }
        private void SendSkillSelect(int skill) { _s.SendPacket(new PacketWriter(0x0443).S32(_why).S16((short)skill).ToArray()); LastReply = $"0x0443 {_why} {skill}"; Debug.Log("[SkillMenus] sent " + LastReply); }
        private void SendRepair(RepairEntry e)
        {
            var w = new PacketWriter(0x0b66).S16((short)e.Index).U32((uint)e.ItemId);
            foreach (var c in e.Cards) w.U32(c);
            w.U8((byte)e.Refine).U8((byte)e.Grade);
            _s.SendPacket(w.ToArray()); LastReply = $"0x0b66 {e.Index} {e.ItemId}"; Debug.Log("[SkillMenus] sent " + LastReply);
        }


        private void Refresh()
        {
            var rows = new List<ListWindow.Row>();
            switch (_kind)
            {
                case Kind.AutoSpell:
                case Kind.SkillSelect:
                    for (int i = 0; i < _skills.Count; i++)
                    {
                        int k = i; var info = SkillDb.Get(_skills[i]);
                        rows.Add(new ListWindow.Row { Icon = WristMenu.SkillIcon(info), Text = info.Name, A = Lang.T("Choose"), OnA = () => Pick(k) });
                    }
                    _win.SetTitle(_kind == Kind.AutoSpell ? ClientText.Msg(697) : "Auto Shadow Spell"); // "Spell List"
                    _win.SetInfo(_kind == Kind.AutoSpell ? Lang.T("Choose the spell to cast by itself while you attack.") : Lang.T("Choose the copied skill to cast by itself."));
                    break;
                case Kind.Repair:
                    for (int i = 0; i < _repair.Count; i++)
                    {
                        int k = i; var e = _repair[i]; var info = GameDb.GetItem(e.ItemId);
                        rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(info), Text = (e.Refine > 0 ? "+" + e.Refine + " " : "") + info.Display, A = Lang.T("Repair"), OnA = () => Pick(k) });
                    }
                    _win.SetTitle(ClientText.Msg(812)); // "Repairable items"
                    _win.SetInfo(Lang.T("Choose the damaged item to repair."));
                    break;
                default: return;
            }
            _win.SetTabs(null, 0, null);
            _win.SetFooter((Lang.T("Cancel"), Cancel));
            _win.SetRows(rows, true);
        }
    }
}
