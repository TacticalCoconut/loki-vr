using System.Collections.Generic;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 12 (w12cast): how large the area of a ground-targeted skill is, for the "spell in hand" preview on the ground.
    /// The numbers are rAthena's (db/re/skill_db.yml, e985006), written into Resources/Skills/ground_areas.txt by
    /// client/tools/w12/build_ground_areas.py: the skill list the server sends carries a skill's range, not its area
    /// (Game/Progression.cs SkillEntry: 0x0b32), and Loki reads no area from the player's game data.
    ///   a skill that places units: a square layout n puts (2n + 1) x (2n + 1) units around the target cell (skill_init_unit_layout,
    ///   skill.cpp:14119-14126) and each unit acts on what is within its Range (skill.cpp:11722, :12452-12457): the cells touched
    ///   are the square of half side n + range around the target;
    ///   a ground skill without units acts within its SplashArea of the target cell.
    /// Not known here: a skill that is not in the table, a special (direction-dependent) layout such as Fire Wall's or Ice Wall's
    /// (layout -1), a splash of -1 (the whole screen): then <see cref="HalfSide"/> is -1 and the preview shows the target cell only.
    /// A host that changes a skill's area in its own skill_db import is not seen here either (the table is the stock one).
    /// </summary>
    public static class GroundAreas
    {
        private sealed class Row { public bool Unit; public int[] Layout, Range, Splash; }
        private static Dictionary<int, Row> _rows;
        public static int Count { get { Load(); return _rows.Count; } }
        public static bool Has(int skillId) { Load(); return _rows.ContainsKey(skillId); }

        private static void Load()
        {
            if (_rows != null) return;
            _rows = new Dictionary<int, Row>();
            var ta = Resources.Load<TextAsset>("Skills/ground_areas");
            if (ta == null) { Debug.LogWarning("[GroundAreas] Resources/Skills/ground_areas is missing: ground previews show the target cell only"); return; }
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r'); if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t'); if (c.Length < 7 || !int.TryParse(c[0], out int id)) continue;
                _rows[id] = new Row { Unit = c[3] == "1", Layout = Ints(c[4]), Range = Ints(c[5]), Splash = Ints(c[6]) };
            }
            Debug.Log($"[GroundAreas] {_rows.Count} ground skills with their areas (rAthena skill_db.yml e985006)");
        }

        private static int[] Ints(string s) { var p = s.Split(','); var a = new int[p.Length]; for (int i = 0; i < p.Length; i++) int.TryParse(p[i], out a[i]); return a; }
        private static int At(int[] a, int level) => a.Length == 0 ? 0 : a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        /// <summary>The half side, in cells, of the square a ground skill touches around its target cell at this level (0 = that one
        /// cell; 2 = 5 x 5), or -1 when the client does not know it (see the class comment).</summary>
        public static int HalfSide(int skillId, int level)
        {
            Load();
            if (!_rows.TryGetValue(skillId, out var r)) return -1;
            if (r.Unit)
            {
                int layout = At(r.Layout, level);
                if (layout < 0) return -1;
                return layout + Mathf.Max(0, At(r.Range, level));
            }
            int splash = At(r.Splash, level);
            return splash < 0 ? -1 : splash;
        }
    }
}
