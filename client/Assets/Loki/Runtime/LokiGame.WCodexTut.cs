using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Wave 28 (wcodex), design note (2026-10-16): "Just add a tutorial section for the bestiary that unlocks the Poring and Lunatic as examples."
    /// The controls tutorial's CODEX step (LokiGame.W18Tutorial.cs builds it after "The menu"; its key "codex"), in the tutorial's own
    /// style: the guide with its explanation (20-120 characters) and one line per thing done, a check mark each, the green arrow over the
    /// creature to look at, the green outline on what to press, and its clip (Resources/Clips/codex.txt, recorded by LokiGame.W22ClipsRec.cs).
    ///   The scan    the step's own practice creatures, drawn by this client only as the practice Poring is (Sprites/EntityManager.cs
    ///               AddLocal; never sent): the practice Poring (LokiGame.W18Tutorial.cs W18TDummyId, on the tutorial map's dais) and a
    ///               practice Lunatic beside it (<see cref="WCodexTutLunaticId"/>). Each is scanned with the game's own scan ring
    ///               (LokiGame.WCodex.cs UpdateWCodexLook: the rest, the ring's fill, its flourish; UI/CodexInkRing.cs), whatever the book
    ///               holds and whatever Settings' "Codex scan" says: during the tutorial the scan takes only the creature the part asks for.
    ///   Written     a finished ring writes the creature into the account's Codex IN FULL, "look": exactly what a scan in the world writes
    ///               (CodexJournal.AddCreature; FLAGGED: they count as SCANNED in the book). A creature already in the book (a tutorial shown
    ///               again) gets the ring and its flourish all the same and is not written twice. Their Codex BOUNTIES: wcxp's server places
    ///               one only for a living creature it sees near the character (loki_codex.txt "n#" / "b#": its own anti-cheat), and these
    ///               two are this client's alone, so no bounty is asked for them; the first real Poring / Lunatic looked at runs the ring
    ///               again for its bounty (LokiGame.Wcxp.cs CxpScanWanted: a creature written with no bounty yet). FLAGGED: a server rule
    ///               for the tutorial's two (once per character, trusted) would need a change of wcxp's script.
    ///   The book    then the way to the Codex (the ring's own Codex icon, one part per press: LokiGame.W23Lines.cs W23RingWay; the classic
    ///               wrist menu: its Achievements page's Codex button); it opens on the Lunatic's page (UI/CodexBook.cs OpenOnce: the
    ///               example just written); the book's X closes it.
    ///   Passed      with Look select off (nothing can be looked at): passed with a line, as the look step is; nothing written (FLAGGED).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The practice Lunatic's id (beside the practice Poring's W18TDummyId; only on this client).</summary>
        public const uint WCodexTutLunaticId = 0x7FFFFF02u;
        public const int WCodexTutPoring = 1002, WCodexTutLunatic = 1063;
        private Unit _wcxtLunatic; private Unit _wcxtScan;
        private readonly HashSet<int> _wcxtScanned = new HashSet<int>();
        /// <summary>The practice Lunatic (null: none now); the creatures the step's scan rings filled, newly written or not (smoke, log).</summary>
        public Unit WCodexTutLunaticUnit => _wcxtLunatic;
        public int WCodexTutScanCount { get; private set; }
        public int WCodexTutWriteCount { get; private set; }

        /// <summary>LokiGame.WCodex.cs WCodexScanTarget during the tutorial: only the creature the Codex step's part asks to scan now.</summary>
        private bool WCodexTutScans(Unit u) => u != null && _wcxtScan != null && u == _wcxtScan && !_wcxtScanned.Contains(u.Job) && W18TutStepKey == "codex";

        /// <summary>LokiGame.WCodex.cs WCodexFilled during the tutorial: the part's creature was scanned (<paramref name="wrote"/>: newly
        /// written into the book).</summary>
        private void WCodexTutScanned(int mob, bool wrote)
        {
            _wcxtScanned.Add(mob); _wcxtScan = null; WCodexTutScanCount++; if (wrote) WCodexTutWriteCount++;
            Debug.Log($"[Tutorial] the Codex step: {CodexBook.NameOf(mob)} ({mob}) scanned, {(wrote ? "written into the book" : "in the book already")}");
        }

        /// <summary>The practice Lunatic is there: beside the practice Poring as the eyes see it (2 cells across the line from the eyes, the
        /// walking hand's side first), on a walkable cell of the room drawn; the look may lock it however far the Poring stands.</summary>
        private void WCodexTutLunaticEnsure()
        {
            var por = _w18tDummy; var gat = _boot.Map.Current != null ? _boot.Map.Current.Gat : null; var rig = _boot.Rig;
            if (por == null || gat == null || rig == null || Entities == null) return;
            if (Target != null) Target.NoRangeLimit = x => x != null && (x == _w18tDummy || x == _wcxtLunatic); // (the practice Poring's rule: W18TDummyEnsure)
            if (_wcxtLunatic != null && Entities.Get(WCodexTutLunaticId) == _wcxtLunatic) return;
            var side = _boot.Map.transform.InverseTransformDirection(rig.Head.transform.right); var sd = new Vector2(side.x, side.z);
            if (sd.sqrMagnitude < 1e-6f) sd = Vector2.right; sd.Normalize();
            int px = Mathf.RoundToInt(por.Cell.x), py = Mathf.RoundToInt(por.Cell.y);
            var tries = new List<Vector2Int>();
            foreach (float k in Comfort.LeftHanded ? new[] { 2f, -2f, 3f, -3f } : new[] { -2f, 2f, -3f, 3f }) tries.Add(new Vector2Int(px + Mathf.RoundToInt(sd.x * k), py + Mathf.RoundToInt(sd.y * k)));
            for (int r = 2; r <= 3; r++) for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++) if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == r) tries.Add(new Vector2Int(px + dx, py + dy));
            foreach (var c in tries)
            {
                if (!gat.InBounds(c.x, c.y) || !gat.Get(c.x, c.y).Walkable || (c.x == px && c.y == py)) continue;
                if (Room != null && !Room.Visible(new Vector2(c.x, c.y))) continue;
                Entities.RemoveLocal(WCodexTutLunaticId);
                _wcxtLunatic = Entities.AddLocal(WCodexTutLunaticId, WCodexTutLunatic, 5, Lang.T("Practice Lunatic"), c.x, c.y, 0); // (5: a monster, as the server's own units)
                Debug.Log($"[Tutorial] the practice Lunatic at {c.x},{c.y}, beside the practice Poring at {px},{py}");
                return;
            }
            Debug.Log($"[Tutorial] no walkable cell beside the practice Poring at {px},{py} for the practice Lunatic");
        }
        private void WCodexTutLunaticRemove() { if (Entities != null) Entities.RemoveLocal(WCodexTutLunaticId); _wcxtLunatic = null; }

        /// <summary>Just above a practice creature (its check mark, the green arrow); null: none.</summary>
        private static Vector3? WCodexTutTop(Unit u)
        {
            if (u == null) return null;
            var col = u.GetComponent<Collider>();
            return col != null ? col.bounds.center + Vector3.up * (col.bounds.extents.y + 0.03f) : u.transform.position + Vector3.up * 0.08f;
        }

        /// <summary>The step (LokiGame.W18Tutorial.cs W18TBuildSteps).</summary>
        private void WCodexTutStep(W18TStep st)
        {
            st.About = () => Lang.T("The Codex is your book of creatures. Look at one until its ring fills to write it in.");
            st.Unavailable = () => LookSelect.On ? null : Lang.T("Look select is off. Skipped.");
            st.Enter = () => { _wcxtScanned.Clear(); _wcxtScan = null; W18TDummyEnsure(); };
            st.Leave = () =>
            {
                _wcxtScan = null; WCodexTutLunaticRemove(); W18TDummyRemove(); // (the steps after it had no Poring: it comes back for the casting ways)
                if (CodexBook != null && CodexBook.IsOpen) CodexBook.Close("the tutorial's next step");
                if (CodexBook != null) CodexBook.OpenOnce = null;
            };
            st.Tick = dt => { W18TDummyEnsure(); if (_w18tPhase >= 1) WCodexTutLunaticEnsure(); };
            st.CheckAt = () => _w18tPhase == 0 ? WCodexTutTop(_w18tDummy) : _w18tPhase == 1 ? WCodexTutTop(_wcxtLunatic) : null;
            st.DoneText = () => Lang.T("The Poring and the Lunatic are in your Codex.");
            bool NewStyle() => Hud != null && Hud.Wrist != null && Hud.Wrist.Ring != null && Hud.Wrist.NewStyle;
            bool BookOpen() => CodexBook != null && CodexBook.IsOpen;
            st.Build = () =>
            {
                st.Phases.Clear();
                // the scans: the Poring, then the Lunatic beside it (each ring the game's own: LokiGame.WCodex.cs)
                var poring = W18TP(() => Lang.T("Look at the Poring until the scan ring fills."), () => _wcxtScanned.Contains(WCodexTutPoring), null,
                    () => { W18TDummyEnsure(); _wcxtScan = _w18tDummy; _codexFill.Remove(WCodexTutPoring); });
                st.Phases.Add(W25Arrow(poring, () => _wcxtScan != null && Target != null && Target.Locked != null && Target.Locked.Unit == _wcxtScan));
                var lunatic = W18TP(() => Lang.T("Now look at the Lunatic until its ring fills."), () => _wcxtScanned.Contains(WCodexTutLunatic), null,
                    () => { WCodexTutLunaticEnsure(); _wcxtScan = _wcxtLunatic; _codexFill.Remove(WCodexTutLunatic); });
                lunatic.ArrowOver = () => _wcxtLunatic;
                st.Phases.Add(W25Arrow(lunatic, () => _wcxtScan != null && Target != null && Target.Locked != null && Target.Locked.Unit == _wcxtScan));
                // the book: the way to it, one part per press; it opens on the Lunatic's page (the example just written)
                int first = st.Phases.Count;
                if (NewStyle())
                {
                    int g = WristMenu.CodexGroup;
                    string IconName() => "Codex"; // (the ring's icon and the book are English only: design note, 2026-10-15)
                    W23RingWay(st, IconName, g, BookOpen, () => W23RingLine(IconName, g));
                    for (int k = first; k < st.Phases.Count; k++) st.Phases[k].Marks = W25MarkRingTo(g, "Codex");
                }
                else
                {
                    st.Phases.Add(W23U(W18TP(W23OpenMenu, () => Hud.Wrist.ShownOnWrist || BookOpen()), W23In.Menu));
                    st.Phases.Add(W25Mark(W23Or(() => !Hud.Wrist.ShownOnWrist && !BookOpen(), W23OpenMenu, W23U(W18TP(() => Lang.F("Point at the {0} tab and {1}.", Lang.T("Achievements"), W23Pull()),
                        () => (Hud.Wrist.ShownOnWrist && Hud.Wrist.CurrentTab == WristMenu.Tab.Achievements) || BookOpen()), W23In.MainTrigger)), W25MarkWrist("btn:Achievements")));
                    st.Phases.Add(W25Mark(W23Or(() => !Hud.Wrist.ShownOnWrist && !BookOpen(), W23OpenMenu, W23U(W18TP(() => W23Press("Codex"), BookOpen), W23In.MainTrigger)), W25MarkWrist("btn:Codex")));
                }
                var firstWay = st.Phases[first]; var enter0 = firstWay.Enter;
                firstWay.Enter = () => { _wcxtScan = null; enter0?.Invoke(); if (CodexBook != null) CodexBook.OpenOnce = CodexBook.PageRef.Creature(WCodexTutLunatic); };
                // closed by its X
                st.Phases.Add(W25Mark(W23U(W18TP(() => Lang.F("On the Codex, point at {0} and {1}.", "X", W23Pull()), () => !BookOpen()), W23In.MainTrigger),
                    () => W25Of(BookOpen() && CodexBook.CloseButton != null ? CodexBook.CloseButton.transform : null)));
            };
        }
    }
}
