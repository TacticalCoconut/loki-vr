using Loki.Game;
using Loki.UI;
using Loki.VR;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 22 (w22tree), the design's point 64: the game's side of the 3D skill trees (UI/SkillTree3D.cs; the menu's side is
    /// UI/WristMenu.Tree.cs).
    ///   The hands   the trees are told where the fingertips are (LokiGame.W12Cast.cs W12Tip: a controller's point ahead of the grip, a
    ///               tracked hand's index tip): a tip held on a medallion or a button presses it.
    ///   The pull    a grip that starts with the hand in a tree, or with the pointing hand's laser on one, is the PULL's from its first
    ///               frame, by the panel grab's own claim (LokiGame.W13Grab.cs W13GrabCandidate / VR/LokiInput.cs ClaimForGrab: no
    ///               casting gesture, no camera, no wand from it); while it is held the trees follow the hand SIDEWAYS (the laser: its
    ///               point at the distance it had when the grip closed): around you the ring turns with the hand; one at a time the
    ///               tree leans with it and flips to the next (UI/SkillTree3D.cs PullBy / PullEnd). With one tree there is nothing to
    ///               pull: the grip is not taken.
    ///   The sounds  a tree's rise plays ONE sound of the player's own install, by its name, if the install has it (Audio/LokiAudio.cs
    ///               HasWav / PlayWav); nothing sounds afterwards.
    ///   The preview the scene beside a medallion (UI/SkillDiorama.cs) gets the player's own look and game data.
    ///   The lock    client wave 23 (w23tree): a trigger pull on nothing takes the locked skill's lock off (W23TreeEmptyPull).
    ///   The table   the trees are told how high the ground under the character is and how large a cell is drawn: a tree whose foot
    ///               would stand in the table's houses is lifted (UI/SkillTree3D.cs Lifts).
    ///   Notices     while the trees are open the notices in front of the eyes are drawn over them (they are drawn over the panels).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>How loud a rise's sound is played (of the effects' volume).</summary>
        public const float W22RiseVolume = 0.7f;
        private readonly bool[] _w22Pull = new bool[2], _w22Laser = new bool[2]; private readonly float[] _w22X = new float[2], _w22Dist = new float[2];
        private int _w22NotesOrder = -1; private bool _w22NotesRaised;
        /// <summary>Pulls of the trees begun / let go (smoke, log).</summary>
        public int W22Pulls { get; private set; }
        public int W22PullEnds { get; private set; }
        public bool W22Pulling(int hand) => _w22Pull[hand];
        private SkillTree3D W22Tree => Hud != null && Hud.Wrist != null ? Hud.Wrist.Tree : null;

        private void BeginW22Tree(LokiBootstrap boot)
        {
            var tree = W22Tree; if (tree == null) return;
            tree.Tip = hand => _w12 != null && hand >= 0 && hand < _w12.Length && _w12[hand] != null && !_w12[hand].In.WandHeld ? W12Tip(_w12[hand]) : (Vector3?)null;
            tree.LaserButton = () => boot.Rig != null ? boot.Rig.LaserButton : null;
            // THE TABLE: the ground the character stands on (the table's top in the Table view) and the map's scale now
            tree.GroundY = () => Entities != null && Entities.Self != null ? Entities.Self.transform.position.y : boot.Rig != null ? boot.Rig.TableHeight : (float?)null;
            tree.MetresPerCell = () => boot.Map != null ? Mathf.Abs(boot.Map.transform.lossyScale.x) : boot.DioramaScale;
            tree.HasSound = name => { string wav = W22Wav(name); return Audio != null && !string.IsNullOrEmpty(wav) && Audio.HasWav(wav); };
            // client wave 23 (item 24): through the trees' own voices (never muffled); the muffle is asked for before a rise's first sound starts
            tree.PlaySound = name => { string wav = W22Wav(name); if (Audio != null && !string.IsNullOrEmpty(wav)) { Audio.Muffled = tree.Shown && Comfort.SkillTreeMuffle; Audio.PlayTreeWav(wav, W22RiseVolume); } };
            var d = SkillDiorama.Create(tree.transform);
            d.Assets = () => boot.Assets;
            d.Look = () => Entities != null ? Entities.SelfLook : null;
            d.Entry = skill => Prog != null && Prog.Skills.TryGetValue(skill, out var k) ? k : null;
            d.Own = skill =>
            {
                var row = Effects != null && Effects.Own != null ? Effects.Own.RowOf(skill) : null;
                return row != null ? (row.Kind ?? "", row.Target ?? "", row.Template ?? "") : ((string, string, string)?)null;
            };
            // client wave 23 (item 24): a preview's sound: the skill's own, at the preview's side of the head, through the trees' own voices
            d.SoundsOf = W24SkillSounds;
            d.GainOf = skill => SkillGains.For(skill, W26Who()); // client wave 26 (w26skill): what a passive or a buff gains the player (LokiGame.W26Skill.cs)
            d.PlaySound = wav =>
            {
                if (Audio == null) return;
                float pan = 0f; var head = boot.Rig != null && boot.Rig.Head != null ? boot.Rig.Head.transform : null;
                if (head != null) { var dir = d.transform.position - head.position; dir.y = 0f; if (dir.sqrMagnitude > 1e-6f) pan = Mathf.Clamp(Vector3.Dot(head.right, dir.normalized), -1f, 1f) * 0.7f; }
                Audio.PlayTreeWav(wav, W24PreviewVolume, pan, d.transform.position);
            };
            d.StopSound = () => Audio?.StopPreviewWavs();
            tree.Diorama = d;
            if (boot.Rig != null) boot.Rig.EmptyPull = W23TreeEmptyPull; // client wave 23 (w23tree): a pull on nothing takes the lock off
        }

        /// <summary>Client wave 23 (w23tree), design note 2026-10-11: "if I click on nothing while this skill tree is open, it should remove any
        /// locked in skill. Like it should remove the lock." The rig's pull that pressed no button (VR/DioramaRig.cs EmptyPull): with
        /// the trees open and a skill locked, a pull that landed on NOTHING takes the lock off and is used up: the rig does nothing
        /// else with it (no walking, no attack). What it landed on is the rig's own answer for a pull: the laser's panel, else, while
        /// the look may choose there, the panel the look is on. NOTHING = no panel at all (the world, the air), or a tree's own empty
        /// space between its medallions; the plate's, the card's, the strip's or another window's empty canvas keeps the lock
        /// (FLAGGED). The pointing hand's trigger only (the walking hand's is its wheel's, as always: FLAGGED).</summary>
        private bool W23TreeEmptyPull()
        {
            var tree = W22Tree; var rig = _boot != null ? _boot.Rig : null;
            if (tree == null || rig == null || !tree.Shown || tree.Selected == 0) return false;
            Transform panel = rig.LaserPanel; Vector3? at = rig.PointedWorld;
            if (panel == null && rig.GazeOnPanel && LookSelect.PanelChoosable(rig.GazePanel)) { panel = rig.GazePanel; at = rig.GazePanelPoint; }
            if (panel != null && (panel != tree.transform || !at.HasValue || tree.OnPanel(at.Value))) return false;
            return tree.Unlock(panel == null ? "a trigger pull on nothing" : "a trigger pull on a tree's empty space");
        }

        /// <summary>Client wave 23 (item 24): how loud a preview's skill sound is played (of the effects' volume. FLAGGED).</summary>
        public const float W24PreviewVolume = 0.8f;
        private bool _w24Counted;

        /// <summary>Client wave 23 (w23tree), item 24, design note: "the sound effects coming from the skill preview animations". A skill's OWN
        /// sounds for its preview, as the game plays them when the skill is used: from the effect tables the world uses (Fx/EffectDb.cs:
        /// the skill's effect ids, Fx/EffectManager.cs SpamSkill / SpamSkillHit / OnCastStart; each effect's parts name a "wav", played
        /// by EffectManager :181-185, :223-225), files of the player's own install. One sound a phase, at the scene's moments: the
        /// cast's (BeginCastEffectId) at the start, the skill's (EffectId, on the caster, on the ground, on success) at the cast
        /// moment, the hit's (BeforeHit, Hit) at the first hit; each later by its part's own delay. Empty: the tables name none, or
        /// the install has none of the files: the preview is silent.</summary>
        private System.Collections.Generic.List<(float at, string wav)> W24SkillSounds(int skill)
        {
            if (!_w24Counted) { _w24Counted = true; W24CountSounds(); }
            return W24SoundsOf(skill);
        }
        private System.Collections.Generic.List<(float at, string wav)> W24SoundsOf(int skill)
        {
            var l = new System.Collections.Generic.List<(float at, string wav)>();
            var s = Loki.Effects.EffectDb.Skill(skill); if (s == null || Audio == null) { W25AddedSkillSound(skill, l); return l; } // (client wave 25, w25misc, item 8: LokiGame.W25Misc.cs)
            int job = Entities != null && Entities.SelfLook != null ? Entities.SelfLook.Job : -1;
            void Phase(float at, params string[][] sets)
            {
                foreach (var set in sets)
                {
                    if (set == null) continue;
                    foreach (var key in set)
                    {
                        var parts = Loki.Effects.EffectDb.Effect(key); if (parts == null) continue;
                        foreach (var p in parts)
                        {
                            if (!p.Has("wav")) continue;
                            string w = p.S("wav"); if (string.IsNullOrEmpty(w)) continue;
                            w = w.Replace("%d", "1").Replace('/', '\\'); if (!w.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase)) w += ".wav";
                            bool have = false; foreach (var e in l) if (e.wav == w) have = true;
                            if (have || !Audio.HasWav(w)) continue;
                            l.Add((at + Mathf.Max(0f, p.F("delayWav")) / 1000f, w)); return; // (one sound a phase)
                        }
                    }
                }
            }
            Phase(0.05f, s.BeginCastEffectId);
            Phase(SkillDiorama.TCast, Loki.Effects.EffectDb.EffectIdFor(skill, job), s.EffectIdOnCaster, s.GroundEffectId, s.SuccessEffectId, s.SuccessEffectIdOnCaster);
            Phase(SkillDiorama.THit, s.BeforeHitEffectId, s.HitEffectId);
            l.Sort((a, b) => a.at.CompareTo(b.at));
            W25AddedSkillSound(skill, l); // client wave 25 (w25misc), item 8: a sound Loki adds where the tables name none (Play Dead: LokiGame.W25Misc.cs)
            return l;
        }
        /// <summary>Once, when the first preview shows: how many skills of the job trees have a sound in the tables (log).</summary>
        private void W24CountSounds()
        {
            int all = 0, with = 0; var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var job in SkillTreeDb.AllJobs()) { var grid = SkillTreeDb.TreeOf(job.Id); if (grid == null) continue; foreach (int sk in grid.Values) if (seen.Add(sk)) { all++; if (W24SoundsOf(sk).Count > 0) with++; } }
            W24WithSound = with; W24Skills = all;
            Debug.Log($"[Tree] preview sounds: {with} of the {all} skills of the job trees have a sound of their own in the effect tables and this install; the other {all - with} previews are silent");
        }
        /// <summary>Skills of the job trees with a sound of their own / in all (counted when the first preview shows).</summary>
        public int W24WithSound { get; private set; }
        public int W24Skills { get; private set; }

        /// <summary>A rise's sound by its name in UI/TreeThemes.cs: a wav's name under data\wav as it is, or "weapon:N": the hit sound the
        /// client's own table has for weapon type N (null: none).</summary>
        private static string W22Wav(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("weapon:")) return name;
            return int.TryParse(name.Substring(7), out int type) ? Loki.Audio.WeaponSounds.WeaponHit(type) : null;
        }

        /// <summary>A grip of hand i now would pull the trees: they are open, there is more than one, and the hand is in a tree or it
        /// is the pointing hand and its laser is on a tree (not on the plate, the card or the strip).</summary>
        private bool W22TreeAt(int hand)
        {
            var tree = W22Tree; var rig = _boot.Rig;
            if (tree == null || !tree.Shown || tree.RingCount < 2 || rig == null || _w12 == null) return false;
            if (_w22Pull[hand]) return true; // (held: what it started on stays)
            var h = _w12[hand];
            if (tree.Holds(h.T.position)) { _w22Laser[hand] = false; return true; }
            if (h.In.IsMain && rig.LaserPanel == tree.transform && rig.PointedWorld.HasValue && rig.Dragging == null && tree.Holds(rig.PointedWorld.Value, 0.06f))
            { _w22Laser[hand] = true; _w22Dist[hand] = Vector3.Distance(rig.PointerOrigin, rig.PointedWorld.Value); return true; }
            return false;
        }

        /// <summary>How far to the trees' right the pull is now (metres): the hand, or the laser's point at the distance it had when the grip closed.</summary>
        private float W22PullX(int hand)
        {
            var tree = W22Tree; var rig = _boot.Rig; var p = _w12[hand].T.position;
            if (_w22Laser[hand]) { var ray = rig.PointerRayNow(); p = ray.GetPoint(_w22Dist[hand]); }
            return Vector3.Dot(p - tree.transform.position, tree.transform.right);
        }

        /// <summary>LokiGame.W13Grab.cs W13GrabStart: the grip claimed for the trees begins a pull.</summary>
        private void W22PullStart(int hand)
        {
            var tree = W22Tree; if (tree == null || !tree.Shown) return;
            _w22Pull[hand] = true; _w22X[hand] = W22PullX(hand); W22Pulls++;
            Haptics.Pulse(Haptics.Kind.Grab, _w12[hand].In.IsMain, 0.2f, 0.03f);
            Debug.Log($"[Tree] pull begun by the {(hand == 0 ? "left" : "right")} {(_w22Laser[hand] ? "hand's laser" : "hand")}");
        }

        /// <summary>Once a frame per hand (LokiGame.W13Grab.cs UpdateW13Grab): a pull follows the hand while its grip is claimed, and ends with it.</summary>
        private void W22PullTick(int hand, bool claimed)
        {
            if (!_w22Pull[hand]) return;
            var tree = W22Tree;
            if (tree == null || !tree.Shown || !claimed)
            {
                _w22Pull[hand] = false; W22PullEnds++;
                if (tree != null && tree.Shown) tree.PullEnd();
                return;
            }
            float x = W22PullX(hand), dx = x - _w22X[hand]; _w22X[hand] = x;
            if (Mathf.Abs(dx) > 1e-5f) tree.PullBy(dx / Mathf.Max(1e-6f, Mathf.Abs(tree.transform.lossyScale.x)));
        }

        /// <summary>Once a frame: while the trees are open, the notices in front of the eyes are drawn over them.</summary>
        private void UpdateW22Tree()
        {
            var tree = W22Tree;
            if (Audio != null) Audio.Muffled = tree != null && tree.Shown && Comfort.SkillTreeMuffle; // client wave 23 (item 24): the other sounds muffled while the trees are open
            if (tree == null || Hud == null || Hud.NotesPanel == null) return;
            var canvas = Hud.NotesPanel.GetComponent<Canvas>(); if (canvas == null) return;
            bool raise = tree.Shown;
            if (raise == _w22NotesRaised) return;
            if (raise) { _w22NotesOrder = canvas.sortingOrder; canvas.sortingOrder = SkillTree3D.SortingOrder + 4; }
            else if (_w22NotesOrder >= 0) canvas.sortingOrder = _w22NotesOrder;
            _w22NotesRaised = raise;
        }
    }
}
