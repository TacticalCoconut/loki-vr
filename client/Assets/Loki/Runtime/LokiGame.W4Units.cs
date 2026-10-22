using System.Collections.Generic;
using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4, w4units: units and their looks (design notes, row w4units). The pieces live in their own files; this part
    /// wires them to the game:
    ///  • Sprites/UnitLooks.cs — what a player is drawn as: mounts (the dragon's colour), boarding, costume outfits, the orc head, babies at
    ///    4/5 size, body names the name table lacks (EntityManager re-applies the look when those option bits change);
    ///  • pets' accessories — ZC_CHANGESTATE_PET type 3 for any pet in view (Companions.PetAccessorySeen) → Unit.PetAccessory;
    ///  • Sprites/SpiritSpheres.cs, Game/CombatExtras.cs — spirit spheres / coins, the Millennium Shield effect, Blade Stop's look;
    ///  • Game/Companions.W4Ai.cs — the official default homunculus / mercenary AI (it needs every unit's last target: V_TARGET);
    ///  • Game/Companions.W4Elemental.cs — the Sorcerer's elemental (0x081d / 0x081e, its page, its HP bar);
    ///  • Sprites/UnitEfstLooks.cs — other units' status looks (Run, Ting, kick stances, Play Dead, Stop, Doram hide / stoop,
    ///    transformation scrolls, Camouflage / Stealth Field / Shadow Form, our intravision).
    /// Smoke: LokiGame.W4UnitsSmoke.cs (-lokiSmokeOnly w4units-host / w4units-guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        private readonly Dictionary<uint, int> _w4PetAcc = new Dictionary<uint, int>();
        private static readonly Color W4ElemColor = new Color(0.6f, 1f, 0.75f);

        private void BeginW4Units(LokiBootstrap boot)
        {
            if (Companions != null)
            {
                // pets' accessories (any pet in view)
                Companions.PetAccessorySeen += (pid, view) =>
                {
                    if (view > 0) _w4PetAcc[pid] = view; else _w4PetAcc.Remove(pid);
                    var u = Entities.Get(pid);
                    if (u != null && u.PetAccessory != view) { u.PetAccessory = view; u.ApplyLook(boot.Assets); Debug.Log($"[W4Units] pet {pid}: accessory view {view}"); }
                };
                // the AI's view of the world
                Companions.W4AiActors = () => Entities.Units.Values;
                Entities.Damage += (src, dst, amount, crit) => { if (src != null && dst != null) Companions.W4AiNoteAction(src.Id, dst.Id); };
                if (Combat != null) Combat.SkillDamage += (src, dst, dmg, skill) => { if (src != null && dst != null) Companions.W4AiNoteAction(src.Id, dst.Id); };
                // the elemental: its name and HP bar like the other companions (DecorateCompanion), its page when it comes back into view
                Companions.UnitChanged += id => { if (id == Companions.ElemId) W4DecorateElemental(); };
            }
            Entities.UnitAdded += u =>
            {
                if (_w4PetAcc.TryGetValue(u.Id, out int v) && u.PetAccessory != v) { u.PetAccessory = v; u.ApplyLook(boot.Assets); }
                if (Companions != null && u.Id == Companions.ElemId) { Companions.ElemOnUnitAdded(u.Id); W4DecorateElemental(); }
                W4HatsOnUnitAdded(u);
            };
            Entities.Vanished += (id, type) => _w4PetAcc.Remove(id); // the server re-sends type 3 to whoever the pet comes into view of (clif.cpp:5130-5135)
            if (CombatExtras != null) { CombatExtras.Assets = boot.Assets; CombatExtras.Effects = Effects; }
            // other units' status looks (after w3status's handler, which already routed the icon to UnitState)
            Prog.AnyEffectChanged += OnW4UnitsEfst;
        }

        private void OnW4UnitsEfst(uint aid, int type, bool on, int val1)
        {
            var u = Entities.Get(aid);
            if (u == null) return; // as roBrowserLegacy (Entity.js:1957-1963): the entry packets / 0x0984 bring what shows when it comes back
            bool self = aid == Session.AccountId;
            if (UnitEfstLooks.OnEfst(u, type, on, val1, Effects, _boot.Assets, self, x => x.ApplyLook(_boot.Assets)))
            {
                if (type == UnitEfstLooks.EfstClairvoyance) Entities.RefreshAllVisibility();
                else Entities.RefreshVisibility(u);
            }
        }

        private void W4DecorateElemental()
        {
            var c = Companions; var u = c != null ? Entities.Get(c.ElemId) : null;
            if (u == null || u.Overhead == null) return;
            u.Overhead.AlwaysShowName = true; u.Overhead.SetNameColor(W4ElemColor);
            u.Overhead.SetHp(c.ElemMaxHp > 0 ? c.ElemHp / (float)c.ElemMaxHp : -1f, true);
        }

        private void OnMapChangeW4Units(string map)
        {
            _w4PetAcc.Clear(); _w4Hats.Clear();
        }

        // ---- hat effects: ZC_EQUIPMENT_EFFECT 0x0a3b len.W aid.L status.B { effect.W }* (packets_struct.hpp:4228-4235; Sprites/HatEffects.cs)
        private readonly Dictionary<uint, List<int>> _w4Hats = new Dictionary<uint, List<int>>(); // before the unit exists (kept per id)
        /// <summary>Hat-effect packets seen (smoke): aid, status, ids.</summary>
        public event System.Action<uint, bool, List<int>> W4HatPacket;

        private bool HandleW4Units(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x0a3b) return false;
            var r = new Net.PacketReader(b, o, n);
            r.Skip(4); uint aid = r.U32(); bool on = r.U8() != 0;
            var ids = new List<int>();
            while (r.Remaining >= 2) ids.Add(r.U16());
            W4HatPacket?.Invoke(aid, on, ids);
            var u = Entities.Get(aid);
            if (u == null)
            {
                // clif_hat_effects follows the unit's spawn (clif.cpp:1761, 5139), so this is rare: keep it for when the unit appears
                if (!_w4Hats.TryGetValue(aid, out var l)) _w4Hats[aid] = l = new List<int>();
                if (on) { foreach (int x in ids) if (!l.Contains(x)) l.Add(x); } else l.RemoveAll(ids.Contains);
                return true;
            }
            int changed = UnitHatEffects.Of(u).Apply(on, ids, Effects);
            Debug.Log($"[W4Units] {aid} ({u.Name}): hat effects {(on ? "on" : "off")} [{string.Join(", ", ids)}] → {changed} changed, now [{string.Join(", ", UnitHatEffects.Of(u).Ids)}]");
            return true;
        }

        private void W4HatsOnUnitAdded(Unit u)
        {
            if (u == null || !_w4Hats.TryGetValue(u.Id, out var l)) return;
            _w4Hats.Remove(u.Id);
            if (l.Count > 0) UnitHatEffects.Of(u).Apply(true, l, Effects);
        }
    }
}
