using System.Collections.Generic;
using System.IO;
using Loki.IO;
using Loki.Sprites;
using UnityEngine;

namespace Loki.Audio
{
    /// <summary>
    /// Weapon hit and swing sounds (client wave 3, w3combat; re-audit §2g). The tables are roBrowserLegacy's (the only reference; rB
    /// is a reimplementation, not the official client), shipped as StreamingAssets/data/combat_sounds.json by
    /// tools/combat/rb_combat_tables.mjs + build_combat_data.py:
    ///  • a hit on a player sounds by the TARGET's job (rB DB/Jobs/JobHitSoundTable.js; DBManager.getJobHitSound falls back to row 0),
    ///    any other hit by the ATTACKER's right-hand weapon type (rB DB/Items/WeaponHitSoundTable.js, a random pick when a row lists
    ///    several; DBManager.getWeaponHitSound → getWeaponType(id, true, true)) — rB Renderer/Effects/Damage.js:481-492;
    ///  • the ACT frame event "atk" is the attacker's weapon swing (rB DB/Items/WeaponSoundTable.js; Renderer/Entity/EntitySound.js:57-63):
    ///    only a unit holding a weapon has one (rB EntityView.js:697-701 sets attackFile for weapon values &gt; 0 only), so a monster's
    ///    "atk" — its damage frame (rAthena doc/mob_db.txt:207-215) — plays nothing.
    /// Weapon types are rAthena's W_* numbers, which rB's WeaponType uses too (0 fist … 23 two-handed staff, 25-30 dual wield).
    /// </summary>
    public static class WeaponSounds
    {
        private static Dictionary<int, string[]> _weaponHit, _jobHit, _swing;
        public static bool Loaded => _weaponHit != null;

        public static void Load()
        {
            if (_weaponHit != null) return;
            _weaponHit = new Dictionary<int, string[]>(); _jobHit = new Dictionary<int, string[]>(); _swing = new Dictionary<int, string[]>();
            string p = Path.Combine(Application.streamingAssetsPath, "data", "combat_sounds.json");
            try
            {
                var o = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(p)));
                Fill(o, "weaponHit", _weaponHit); Fill(o, "jobHit", _jobHit); Fill(o, "weaponSwing", _swing);
                Debug.Log($"[WeaponSounds] {_weaponHit.Count} weapon hit rows, {_jobHit.Count} job hit rows, {_swing.Count} swing rows");
            }
            catch (System.Exception e) { Debug.LogError("[WeaponSounds] failed to load " + p + ": " + e.Message); }
        }

        private static void Fill(Dictionary<string, object> o, string key, Dictionary<int, string[]> into)
        {
            if (o == null || !o.TryGetValue(key, out var t)) return;
            var m = MiniJson.AsObject(t); if (m == null) return;
            foreach (var kv in m)
            {
                if (!int.TryParse(kv.Key, out int id)) continue;
                var l = MiniJson.AsList(kv.Value); if (l == null) continue;
                var names = new List<string>();
                foreach (var x in l) { var s = MiniJson.Str(x, null); if (!string.IsNullOrEmpty(s)) names.Add(s); }
                into[id] = names.ToArray();
            }
        }

        private static string Pick(string[] list) => list == null || list.Length == 0 ? null : list.Length == 1 ? list[0] : list[Random.Range(0, list.Length)];

        /// <summary>rB DBManager.getWeaponType(id, realType, considerDualHandIds) for a unit packet's weapon value: a W_* type below 103
        /// stays (char select sends status.weapon), an item id becomes its view (ClassNum, else the rAthena weapon SubType) and then its
        /// base type (weapontable.lub Expansion_Weapon_IDs) — Sprites/PlayerAppearance.cs AppearanceDb.WeaponView / BaseWeaponType.</summary>
        public static int WeaponType(int weaponValue)
        {
            int view = AppearanceDb.WeaponView(weaponValue);
            return view <= 0 ? 0 : AppearanceDb.BaseWeaponType(view);
        }

        /// <summary>The weapon type of a unit's right hand (rB srcEntity.weapon; 0 = none, monsters usually).</summary>
        public static int WeaponTypeOf(Unit u) => u == null || u.Look == null ? 0 : WeaponType(u.Look.Weapon);

        public static string WeaponHit(int weaponType)
        {
            Load();
            return _weaponHit.TryGetValue(weaponType, out var l) ? Pick(l) : null; // rB: WeaponHitSoundTable[type], undefined → no sound
        }

        public static string JobHit(int job)
        {
            Load();
            if (job != 0 && _jobHit.TryGetValue(job, out var l)) return Pick(l);
            return _jobHit.TryGetValue(0, out var d) ? Pick(d) : null; // DBManager.getJobHitSound: JobHitSoundTable[job] || JobHitSoundTable[0]
        }

        /// <summary>The sound of one hit that shows a number (Damage.js:481-492): a player target by its job, anything else by the attacker's weapon.</summary>
        public static string HitSound(Unit src, Unit dst)
        {
            if (dst == null) return null;
            if (dst.IsPlayerType) return JobHit(dst.Job);
            return WeaponHit(WeaponTypeOf(src));
        }

        /// <summary>The swing sound for an ACT "atk" event of this unit, or null (no weapon: rB leaves attackFile unset).</summary>
        public static string Swing(Unit u)
        {
            if (u == null || u.Look == null || u.Look.Weapon <= 0) return null;
            Load();
            return _swing.TryGetValue(WeaponType(u.Look.Weapon), out var l) ? Pick(l) : null;
        }

        /// <summary>What an ACT frame event of this unit plays: a ".wav" name as is, "atk" the weapon swing, anything else nothing.
        /// Client wave 9 (h9feel, the design's "too many sound effects everywhere"): the sound is PLAYED HERE through <see cref="LokiAudio.PlayUnitWav"/>
        /// (the player's own actions as before; every other unit's under the Monster sounds setting, a budget a second, nearest first, a
        /// tighter attenuation and the same-wav gap) and null is returned, so the caller (Sprites/EntityManager.cs HookSounds, which plays
        /// what it gets) plays nothing twice. Nothing but Audio/ changes for this. <see cref="ActEventName"/> is the name alone.</summary>
        public static string ActEvent(Unit u, string ev)
        {
            string wav = ActEventName(u, ev);
            var audio = LokiAudio.Instance;
            if (wav == null || audio == null) return wav;
            audio.PlayUnitWav(wav, u != null ? u.Cell : Vector2.zero, u != null && u.IsSelf);
            return null;
        }

        /// <summary>The wav an ACT frame event of this unit names (not played): a ".wav" name as is, "atk" the weapon swing, anything else nothing.</summary>
        public static string ActEventName(Unit u, string ev)
        {
            if (string.IsNullOrEmpty(ev)) return null;
            if (ev == "atk") return Swing(u);
            return ev.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) ? ev : null;
        }
    }
}
