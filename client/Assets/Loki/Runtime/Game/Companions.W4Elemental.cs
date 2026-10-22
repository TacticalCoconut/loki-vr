using System.Collections.Generic;
using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 4 (w4units): a Sorcerer's elemental (rAthena e985006, PACKETVER 20211103). The elemental is a unit like any other
    /// (objecttype 0xA, clif_bl_type; classes 2114-2125 EL_AGNI_S … EL_TERA_L, db/re/elemental_db.yml; its sprite data\sprite\몬스터\el_*),
    /// and it has its own server AI (elemental.cpp): the owner only picks its mode. Packets, both to the owner only (SELF):
    ///  • ZC_EL_INIT 0x081d id.L hp.L maxhp.L sp.L maxsp.L (22 bytes; clif_elemental_info, clif.cpp:19122-19141): at the summon
    ///    (elemental.cpp:284) and after every map load (clif.cpp:9885, 10897);
    ///  • ZC_EL_PAR_CHANGE 0x081e type.W value.L (8 bytes; clif_elemental_updatestatus, clif.cpp:19093-19120; packets.hpp:1398-1403):
    ///    type SP_HP 5, SP_MAXHP 6, SP_SP 7, SP_MAXSP 8 (map.hpp _sp).
    /// It leaves with a plain ZC_NOTIFY_VANISH type 0 (elemental_delete, elemental.cpp:196-211: unit_free / unit_remove_map CLR_OUTSIGHT):
    /// when its time runs out, on SO_EL_CONTROL level 4, when the owner dies (pc.cpp:9855-9856) or changes job (pc.cpp:11010-11011).
    /// The mode: the owner's skill SO_EL_CONTROL 2456 (skills/mage/spiritcontrol.cpp): level 1 passive (EL_MODE_PASSIVE), 2 defensive
    /// (EL_MODE_ASSIST), 3 offensive (EL_MODE_AGGRESSIVE), 4 dismiss; it needs the elemental out (skill.cpp:8904-8909). The mode is not
    /// sent back to the client (clif_skill_nodamage only), so the window shows the last one asked for.
    /// </summary>
    public sealed partial class Companions
    {
        /// <summary>ZC_CHANGESTATE_PET type 3 for any pet in view: pet id, accessory view id (0 = none) — its look (LokiGame.W4Units).</summary>
        public event System.Action<uint, int> PetAccessorySeen;

        public uint ElemId { get; private set; }
        public bool HasElemental => ElemId != 0;
        public long ElemHp, ElemMaxHp, ElemSp, ElemMaxSp;
        /// <summary>The elemental's unit left view (vanish): it is back when its unit comes into view again, or gone for good.</summary>
        public bool ElemAway { get; private set; }
        /// <summary>The last SO_EL_CONTROL level asked for (0 = none yet this summon).</summary>
        public int ElemModeAsked { get; private set; }
        public int ElemInits; // 0x081d seen (smoke)
        public const int SkillElControl = 2456;

        private bool HandleW4Elemental(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x081d:
                {
                    r.Skip(2); uint eid = r.U32(); ElemHp = r.U32(); ElemMaxHp = r.U32(); ElemSp = r.U32(); ElemMaxSp = r.U32();
                    if (eid != ElemId) ElemModeAsked = 0;
                    ElemId = eid; ElemAway = false; ElemInits++;
                    Debug.Log($"[W4Units] elemental {eid}: HP {ElemHp}/{ElemMaxHp} SP {ElemSp}/{ElemMaxSp}");
                    UnitChanged?.Invoke(eid); Changed?.Invoke(); Refresh();
                    return true;
                }
                case 0x081e:
                {
                    r.Skip(2); int type = r.U16(); long v = r.U32();
                    switch (type) { case 5: ElemHp = v; break; case 6: ElemMaxHp = v; break; case 7: ElemSp = v; break; case 8: ElemMaxSp = v; break; }
                    if (ElemId != 0) UnitChanged?.Invoke(ElemId);
                    Changed?.Invoke(); Refresh();
                    return true;
                }
                default: return false;
            }
        }

        private void ElemReset() { ElemId = 0; ElemAway = false; ElemModeAsked = 0; ElemHp = ElemMaxHp = ElemSp = ElemMaxSp = 0; }

        private void ElemOnVanished(uint id, int type)
        {
            if (id == 0 || id != ElemId) return;
            ElemAway = true; // gone, or only out of view: the packets can't tell (see the summary)
            Debug.Log($"[W4Units] elemental {id} left view (vanish type {type})");
            Changed?.Invoke(); Refresh();
        }

        /// <summary>The elemental's unit came into view again (EntityManager.UnitAdded).</summary>
        public void ElemOnUnitAdded(uint id) { if (id != 0 && id == ElemId && ElemAway) { ElemAway = false; Changed?.Invoke(); Refresh(); } }

        /// <summary>SO_EL_CONTROL at a level (1 passive, 2 defensive, 3 offensive, 4 dismiss): a self skill of the owner.</summary>
        public void ElementalMode(int level)
        {
            if (!HasElemental) return;
            ElemModeAsked = level;
            _s.UseSkill(SkillElControl, level, _s.AccountId);
            Debug.Log($"[W4Units] elemental mode {level}");
            Refresh();
        }

        /// <summary>Which window page a unit of ours opens (the pointer on our own companion).</summary>
        public Page PageOf(uint id) => id == PetId ? Page.Pet : id == HomId ? Page.Homunculus : id == ElemId ? Page.Elemental : Page.Mercenary;

        private static readonly string[] ElemModeNames = { "", Lang.N("~elemental~Passive"), Lang.N("~elemental~Defensive"), Lang.N("~elemental~Offensive") }; // w4lang: shown with Lang.T

        private void RefreshElemental(List<ListWindow.Row> rows)
        {
            _win.SetTitle(Lang.T("Elemental")); _win.SetInput(false);
            if (!HasElemental)
            {
                _win.SetInfo(Lang.T("No elemental."));
                rows.Add(new ListWindow.Row { Text = Lang.T("<color=#9aa3b5>Sorcerers summon one with Summon Agni, Aqua, Ventus or Tera (each needs its catalyst).</color>") });
                _win.SetFooter((Lang.T("Close"), Hide));
                return;
            }
            var u = _unit(ElemId);
            string name = u != null ? u.DisplayName : "";
            string state = ElemAway ? Lang.T("<color=#9aa3b5>not in view</color>") : Lang.T("<color=#7fd67f>out</color>");
            _win.SetInfo(string.Format("{0} — {1}", name.Length > 0 ? name : Lang.T("Elemental"), state));
            rows.Add(new ListWindow.Row { Text = string.Format("<color=#ffd060>{0}</color>\n<size=19>HP {1} · SP {2}</size>", name.Length > 0 ? name : Lang.T("Elemental"), Bar(ElemHp, ElemMaxHp), Bar(ElemSp, ElemMaxSp)) });
            string asked = ElemModeAsked >= 1 && ElemModeAsked <= 3 ? Lang.T(ElemModeNames[ElemModeAsked]) : "?";
            rows.Add(new ListWindow.Row { Text = Lang.F("Mode: {0}  <color=#9aa3b5>(Spirit Control; the server does not report the mode back)</color>", asked) });
            rows.Add(new ListWindow.Row { Text = Lang.T("Passive: it only follows you."), A = Lang.T("~elemental~Passive"), OnA = () => ElementalMode(1) });
            rows.Add(new ListWindow.Row { Text = Lang.T("Defensive: it fights what attacks you."), A = Lang.T("~elemental~Defensive"), OnA = () => ElementalMode(2) });
            rows.Add(new ListWindow.Row { Text = Lang.T("Offensive: it attacks monsters around you."), A = Lang.T("~elemental~Offensive"), OnA = () => ElementalMode(3) });
            _win.SetFooter((Armed("elem", Lang.T("Dismiss")), () => { if (Confirm("elem", Lang.T("Dismiss the elemental"))) ElementalMode(4); }), (Lang.T("Close"), Hide));
        }
    }
}
