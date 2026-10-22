using System.Collections.Generic;
using System.IO;
using System.Text;
using Loki.Net;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4, w4fx (design notes "w4fx — skills and their effects"; progress design notes). Hooks:
    ///  • the GRF's ez2streffect effects tied to skills once the client's skill table is there (the first map entry: w3data builds skills.tsv
    ///    on first run) — Fx/Ez2Effects.cs;
    ///  • ZC_SPRITE_CHANGE 0x01d7 LOOK_BASE on a skill unit's id re-draws the unit (the Warp Portal opening, rAthena skill.cpp:12281-12284) —
    ///    peeked before EntityManager consumes it (EntityManager only knows units, not ground skill units).
    /// The rest lives in the owners' files: Fx/* (renderers, ez2, ground FUNCs, RSM traps), Game/CombatFx.cs (ground units, graffiti, the
    /// fail text), Game/SkillFail.cs, Game/DeathSync.cs, Sprites/ActorSprite.cs (every layer's ACT sound events).
    /// Smoke: LokiGame.W4FxSmoke.cs (-lokiSmokeOnly w4fx-host / w4fx-guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        private bool _w4fxEz2Loaded;
        /// <summary>Client data skill table (skills.tsv: id, aegis …) upper-cased aegis name → id; empty before the first-run tables exist.</summary>
        public static Dictionary<string, int> W4FxSkillIdsByAegis()
        {
            var map = new Dictionary<string, int>();
            try
            {
                string p = Loki.Data.ClientData.File("skills.tsv");
                if (!File.Exists(p)) return map;
                foreach (var line in File.ReadLines(p, Encoding.UTF8))
                {
                    var c = line.Split('\t'); if (c.Length < 2 || !int.TryParse(c[0], out int id) || c[1].Length == 0) continue;
                    string k = c[1].ToUpperInvariant(); if (!map.ContainsKey(k)) map[k] = id;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[W4Fx] skills.tsv: " + e.Message); }
            return map;
        }

        private void BeginW4Fx(LokiBootstrap boot)
        {
            if (Session != null) Session.OnEnteredMap += s => W4FxLoadEz2();
        }

        private void W4FxLoadEz2()
        {
            if (_w4fxEz2Loaded || Effects == null) return;
            var map = W4FxSkillIdsByAegis();
            if (map.Count == 0) { Debug.LogWarning("[W4Fx] no skill table yet: ez2 effects not tied to skills"); return; }
            _w4fxEz2Loaded = true;
            Effects.LoadEz2(map);
            Effects.LoadGrfSkillEffects(map); // client wave 4c (w4fx2): the GRF's official skill effect table + effect files named after skills
            Effects.Own?.Load(); // client wave 5 (w5fxown): Loki's own effects (a design) for the skills that still draw nothing (Fx/OwnSkillFx.cs)
        }

        /// <summary>Peek (the packet goes on to its owner): a skill unit changing its look.</summary>
        private void W4FxPeek(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x01d7 || n < 15 || Combat == null) return; // ZC_SPRITE_CHANGE AID.L type.B val.L val2.L (clif_changelook → clif_sprite_change)
            var r = new PacketReader(b, o, n); r.Skip(2); uint aid = r.U32(); int type = r.U8(); int val = r.S32();
            if (type == 0 && Combat.MorphUnit(aid, val)) Debug.Log($"[W4Fx] unit {aid} now unit id 0x{val:x} (LOOK_BASE)"); // LOOK_BASE = 0 (pc.hpp look enum)
        }
    }
}
