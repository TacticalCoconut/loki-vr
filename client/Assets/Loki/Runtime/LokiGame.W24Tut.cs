using System;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 24 (w24): the tutorial. the design's list of 2026-10-12.
    ///   Item 6   back to the Follow view as the resize step's last part: LokiGame.W18Tutorial.cs (the resize step's Build).
    ///   Item 10  "During the tutorial, if while player is holding the wand, they try to click on a button with the laser, give a
    ///            little pop-up that says to click on a button, you must let go of the wand." and "I mean buttons like end
    ///            tutorial, back, skip, any kind of panel button". WHAT HAPPENS TODAY: a hand that holds the wand keeps its grip and
    ///            trigger for the wand alone (VR/LokiInput.cs:562 drops that hand's click; LokiGame.W12Wand.cs:378 hides the
    ///            pointing hand's laser), unless a MENU is open (then the casting waits and the trigger presses the menu's buttons:
    ///            LokiGame.W12Cast.cs MenuClicksOnly). The tutorial's guide, the Casting panel, the NPC dialog are no menus, so
    ///            a pull at their buttons did nothing and said nothing. The wand in the OTHER hand never hindered the pointing
    ///            hand's laser (only the holding hand's input is the wand's). NOW, while the tutorial runs
    ///            (<see cref="WandHintAlways"/>: always), a trigger pull of the pointing hand that holds the wand, with its
    ///            pointing line on a button of any panel that is not a casting tool (LokiGame.W23Place.cs W23ToolNames), shows one
    ///            notice, at most every <see cref="WandHintEvery"/> seconds. Nothing else changes: the pull is still the wand's.
    ///   Item 15  "on the quick use bubble tutorial. It tells me to look up at the menu above me, but it doesn't tell me to open up
    ///            the quick use bubble submenu." THE CAUSE: the line does change the moment the menu above wakes (the guide is
    ///            drawn anew every frame: LokiGame.W18Tutorial.cs W18TutDraw; the either-or line: LokiGame.W23Lines.cs W23Or), but
    ///            the guide stands at eye level and the menu 60 degrees up: looking at the menu, the player cannot see the guide.
    ///            NOW the tutorial's current action line is ALSO written on the menu above's own hint line while the tutorial
    ///            runs (<see cref="W24TutHint"/>; LokiGame.W13Resize.cs), so "Point at Edit… beside Quick Use Bubbles and pull your
    ///            right trigger." is where the player looks. The same for Resize and Reposition (the same look up, then press).
    ///            FLAGGED: on the menu itself, not a separate notice.
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>Seconds between two wand hints; the hint outside the tutorial too (FLAGGED: specified: "During the tutorial").</summary>
        public const float WandHintEvery = 6f;
        public const bool WandHintAlways = false;
        /// <summary>Wand hints shown; pulls at a button that came within <see cref="WandHintEvery"/> of the last hint (smoke, log).</summary>
        public int W24WandHints { get; private set; }
        public int W24WandHintsHeldBack { get; private set; }
        private bool _w24WandTrigWas; private float _w24WandHintAt = -100f;
        private string _w24TutLine = "";

        /// <summary>The tutorial's current action line for the menu above's hint line ("" when the tutorial does not run or the
        /// step's parts are done).</summary>
        private string W24TutHint => W18TutRunning && _w18tDoneAt < 0f ? _w24TutLine ?? "" : "";

        // ------------------------------------------------------------------ item 5: the Poring on the round floor of the tutorial's map
        // design note (2026-10-12): "the Poring still is [not] in a good location. On the map of the tutorial, there is a large round
        // structure right in the center of the area that the player initially spawns in. Like a large dais looking structure. Put
        // the Poring right in the center of it, and I don't mean the place that is located further away ... I mean the one that
        // is in the same floating bit that the player is located."
        // FOUND IN THE PLAYER'S MAP DATA (data.grf, read only: valkyrie.gat's heights and walkable cells, valkyrie.rsw's models;
        // design notes has the text map): the walkable floor joined to the spawn cell 49,36 is ONE platform of 615 cells
        // (x 39-58, y 6-67). Its level floor is at height -20.0 (340 cells; in the GAT a lower number is HIGHER). In its middle
        // lies ONE large ROUND area, 125 cells, x 39-58, y 42-55: a SUNKEN ring with four arms, three steps under the floor
        // (height -17: 0.6 cell lower), and in the ring's middle a RAISED 6 x 6 block, x 46-51, y 46-51, two steps up from the
        // ring (height -19: 0.4 cell above it, 0.2 cell under the floor): THE DAIS. Its centre is 48.5, 48.5 (the corner the cells
        // 48-49 / 48-49 share), 12.5 cells north of the spawn. The other two off-level things on the platform are not round: the
        // stair going down south of the spawn (x 46-51, y 6-29) and a 4 x 2 step at the north end (30 cells away). The models
        // round the platform are its pillars and arcades, none in its middle. So there is one candidate.
        // THE CELL: 49, 48 (FLAGGED: the centre is a corner of four cells; this one is in the spawn's own column, the nearer row).
        // THE HOPS of the look step: to a cell of the dais's own top (as high as the centre: the 6 x 6 block), 2 to
        // TutorialDaisHop (3) cells from the centre, then back to the centre, in turn: never off the dais. (Wave 20's hops
        // were at least 4 cells apart: the dais is 6 cells wide, so on it a hop is 2 or 3.)
        /// <summary>The tutorial's own map (the client's side of lokihost's TutorialInstance.DefaultMap "valkyrie,49,36": change the
        /// two together), the cell the practice Poring stands on there, and how far from it a hop goes (cells).</summary>
        public static readonly (string Map, int X, int Y) TutorialDais = ("valkyrie", 49, 48);
        public const int TutorialDaisHop = 6;
        /// <summary>Poring placings on the dais so far; hops made on it (smoke, log).</summary>
        public const int TutorialDaisHopMin = 2;
        private static readonly (int dx, int dy)[] W24TriangleOffsets = { (-5, -3), (5, -3), (0, 5) };
        private int _w24TriangleVertex;
        public int W24DaisPlacings { get; private set; }
        public int W24DaisHops { get; private set; }
        /// <summary>The tutorial's map is the one loaded and the dais's cell can be stood on.</summary>
        public bool W24DaisHere
        {
            get
            {
                var m = _boot != null && _boot.Map != null ? _boot.Map.Current : null;
                return m != null && m.Gat != null && string.Equals(m.Name, TutorialDais.Map, StringComparison.OrdinalIgnoreCase)
                    && m.Gat.InBounds(TutorialDais.X, TutorialDais.Y) && m.Gat.Get(TutorialDais.X, TutorialDais.Y).Walkable;
            }
        }
        /// <summary>LokiGame.W18Tutorial.cs W18TDummyEnsure: on the tutorial's map the Poring is (put) on the dais; true = done here.</summary>
        private bool W24DaisPoring(bool have, string step)
        {
            if (!W24DaisHere) return false;
            if (have && (_w18tDummy.Walking || W24OnDais(_w18tDummy.Cell))) return true; // (it is there, or on a hop of its own)
            Entities.RemoveLocal(W18TDummyId);
            _w18tDummy = Entities.AddLocal(W18TDummyId, 1002, 5, Lang.T("Practice Poring"), TutorialDais.X, TutorialDais.Y, 0);
            _w24TriangleVertex = 0;
            W24DaisPlacings++; W23PoringPlacings++;
            bool seen = W23Angles(_boot.Map.transform.TransformPoint(new Vector3(TutorialDais.X + 0.5f, _w18tDummy.transform.localPosition.y, TutorialDais.Y + 0.5f)), out float down, out float bearing);
            Debug.Log($"[Tutorial] the practice Poring on the centre of the tutorial map's dais, {TutorialDais.X},{TutorialDais.Y} (step '{step}', view {(View != null ? View.Mode.ToString() : "?")}): {(seen ? $"{down:0.0}° below eye level, {bearing:+0.0;-0.0;0.0}° from where the head faces" : "its angles could not be measured")}");
            return true;
        }
        /// <summary>A cell is on the dais: within the hop's reach of its centre.</summary>
        private static bool W24OnDais(Vector2 cell) => Mathf.Max(Mathf.Abs(cell.x - TutorialDais.X), Mathf.Abs(cell.y - TutorialDais.Y)) <= TutorialDaisHop + 0.5f;
        /// <summary>LokiGame.W20Tut.cs W20PoringHop: on the tutorial's map a hop walks around the 3 vertices of a large equilateral
        /// triangle centred on the dais so the Poring never moves back and forth between the same two spots. True = done here.</summary>
        private bool W24DaisHop(Sprites.Unit u, int ox, int oy)
        {
            if (!W24DaisHere || u == null) return false;
            var gat = _boot.Map.Current.Gat; int cx = TutorialDais.X, cy = TutorialDais.Y;
            (int x, int y) to = (cx, cy);
            bool found = false;
            for (int k = 0; k < W24TriangleOffsets.Length; k++)
            {
                int idx = (_w24TriangleVertex + k) % W24TriangleOffsets.Length;
                var (dx, dy) = W24TriangleOffsets[idx];
                int tx = cx + dx, ty = cy + dy;
                if (tx == ox && ty == oy) continue;
                if (gat.InBounds(tx, ty) && gat.Get(tx, ty).Walkable)
                {
                    to = (tx, ty);
                    _w24TriangleVertex = (idx + 1) % W24TriangleOffsets.Length;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                for (int dx = -TutorialDaisHop; dx <= TutorialDaisHop && !found; dx++)
                    for (int dy = -TutorialDaisHop; dy <= TutorialDaisHop && !found; dy++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (Mathf.Max(Mathf.Abs(x - ox), Mathf.Abs(y - oy)) < TutorialDaisHopMin) continue;
                        if (gat.InBounds(x, y) && gat.Get(x, y).Walkable) { to = (x, y); found = true; }
                    }
            }
            if (!found) { Debug.Log("[Tutorial] the Poring has no cell of the dais to hop to: it stays on the centre"); return true; }
            u.WalkTo(ox, oy, to.x, to.y);
            W24DaisHops++; W20PoringHops++;
            Debug.Log($"[Tutorial] the Poring hops {ox},{oy} → {to.x},{to.y} along the tutorial map's triangle (vertex {(_w24TriangleVertex + W24TriangleOffsets.Length - 1) % W24TriangleOffsets.Length + 1} of {W24TriangleOffsets.Length})");
            return true;
        }

        // ------------------------------------------------------------------ item 14: the guide is found again
        // design note: "do revise where the tutorial panel is whenever I am doing the tutorial, because I have frequently lost its position
        // when I am looking around and doing things". The rule is the guide's own (UI/TutorialGuide.cs: Follow, Line, Pointer).
        // Here: the POINTER, a gold glow at the view's edge on the guide's side while it is away or gliding home (wave 19's edge
        // glow of the Follow view, in the theme's gold; FLAGGED: a glow, not an arrow's shape), and the step's hook.
        /// <summary>The guide's pointer (null before the tutorial was first set up).</summary>
        public VR.EdgeGlow W24GuidePointer { get; private set; }
        private string _w24GuideStep;
        private void InitW24Tut()
        {
            if (W24GuidePointer != null || _boot.Rig == null || _boot.Rig.Head == null) return;
            W24GuidePointer = VR.EdgeGlow.Attach(_boot.Rig.Head);
            W24GuidePointer.gameObject.name = "GuidePointer"; W24GuidePointer.Own = true; W24GuidePointer.Tint = Theme.Gold;
            W24GuidePointer.Target = () => W18TutRunning && W18TGuide != null && W18TGuide.Visible && W18TGuide.Away ? W18TGuide.transform.position : (Vector3?)null;
        }
        /// <summary>LokiGame.W18Tutorial.cs W18TutDraw, every frame: when the step (or the casting lesson) is a new one, the guide
        /// comes home if it was left behind.</summary>
        private void W24GuideStep(W18TStep st)
        {
            string key = st != null ? st.Key : "";
            if (key == _w24GuideStep) return;
            _w24GuideStep = key; if (W18TGuide != null) W18TGuide.ComeHome();
        }

        /// <summary>Once a frame, after the wand's own update (LokiGame.W12Cast.cs): the hint of item 10.</summary>
        private void UpdateW24WandHint()
        {
            var rig = _boot.Rig; var h = _wandHand;
            bool held = rig != null && h != null && h.In.IsMain && h.In.WandHeld;
            bool trig = held && h.In.TriggerValue > 0.6f, down = trig && !_w24WandTrigWas; _w24WandTrigWas = trig;
            if (!down || !(W18TutRunning || WandHintAlways)) return;
            if (!rig.LaserOnPanel(new Ray(rig.PointerOrigin, rig.PointerDirection), 50f, out var hit)) return;
            var b = hit.collider.GetComponent<UiButton>(); if (b == null) return;
            var panel = UiButton.PanelOf(hit.collider.transform);
            if (panel == null || Array.IndexOf(W23ToolNames, panel.name) >= 0) return; // (a casting tool's own slots are the tool's)
            if (Time.unscaledTime - _w24WandHintAt < WandHintEvery) { W24WandHintsHeldBack++; return; }
            _w24WandHintAt = Time.unscaledTime; W24WandHints++;
            W13LastNote = W23Hand ? Lang.T("Put the wand away to press buttons. Make a fist at your hip.") : Lang.T("Put the wand away to press buttons. Squeeze your grip at your hip.");
            Hud?.Notify(W13LastNote, 4f);
            Debug.Log($"[Tutorial] the wand hand's trigger pulled on the button '{b.name}' of '{panel.name}': the hint shown ({W24WandHints})");
        }
    }
}
