using System;
using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using Loki.Net;
using Loki.UI;
using Loki.VR;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 20 (w20bubbles), the design's point 56 (playtest notes row 56): the QUICK USE BUBBLES. design note: "create a
    /// quick use menu that holds up to 4 items inside spheric transparent bubbles that is hidden above the player along with the menu that
    /// floats above the player. This quick use menu can hold items, abilities, anything that the player wants to put there. And each bubble
    /// is completely toggleable, resizable and movable. It is not meant to use any of the different hotbars that the game natively carries.
    /// require it to be something that does not interfere with any of the other casting methods. in order to control the specific location,
    /// size, and toggling of each individual bubble, the floating menu above the player will have a section called Quick Use Bubbles that
    /// you click, and it transforms that floating menu into a menu that controls the bubbles." Additions: per bubble "always visible, or ...
    /// only appear visible once the player looks at the bubble"; "They should work even if you're not looking at them"; "a toggle that
    /// decides whether they activate upon being touched or upon the trigger being pulled" (default Trigger: the design's answer "Yes").
    ///   Bubbles   four see-through spheres (UI/QuickBubble.cs), each holding one skill or one item (what the forearm bar can hold), or
    ///             nothing. By default in a gentle arc above the eyes, under the overhead menu (UI/OverheadMenu.cs, 60° up 0.75 m) and in
    ///             reach of a raised hand: <see cref="BubbleElevation"/>° up, <see cref="BubbleDistance"/> m from the eyes, at
    ///             <see cref="BubbleSpread"/>° apart (-27°, -9°, +9°, +27° from the body's heading), radius <see cref="QuickBubble.BaseRadius"/>
    ///             m. Their places are in the BODY's frame from the eyes, as the overhead menu's (VR/WandHolster.cs BodyYaw): they go with
    ///             the head's position and the body's heading, never with a glance.
    ///   Kept      the LAYOUT (On / Off, place, size, Shown, Use by) with the account (World/UserSettings.cs bubbles: every field not
    ///             per PC goes with the account, World/AccountSettingsSync.cs), the CONTENTS per character (bubbleSets, "Name#charId"),
    ///             client-side only: no RO hotkey row is read or written.
    ///   Shown     "Always": drawn all the time in the world. "When looked at" (the default, the design's "hidden"): drawn only while the look
    ///             (the calibrated gaze ray, VR/DioramaRig.Ui.cs GazeRay) is within <see cref="BubbleLookFull"/>° of the bubble's own
    ///             outline, fading out by <see cref="BubbleLookFrom"/>° (<see cref="BubbleFade"/> s fades). An empty bubble is drawn
    ///             fainter. While the bubbles' editor is drawn or Reposition is on, every bubble is drawn (an Off one as a grey ghost).
    ///   Use       works whether drawn or not (design note: "They should work even if you're not looking at them"). "Use by: Touch": a hand's tip
    ///             (the controller's fingertip point, a tracked hand's index tip: LokiGame.W12Cast.cs W12Tip) entering the bubble uses it
    ///             once; again only after the tip left it by <see cref="BubbleRearm"/> m and <see cref="BubbleLockout"/> s passed.
    ///             "Use by: Trigger" (the default): the tip in the bubble (<see cref="BubbleTriggerMargin"/> m of slack) and that hand's
    ///             trigger pulled: the pull is the bubble's alone (VR/LokiInput.cs BubbleProbe: no click, no walk, no skill wheels from it).
    ///             A light tick in the hand when its tip enters a bubble that can be used. A skill is taken by the casting rule of every
    ///             gesture (LokiGame.W12Cast.cs W12TakeSkillId: "Cast when taken" casts a self skill at once and a target skill at what the
    ///             laser or the look has; else it waits in that hand for the trigger); an item is used as a forearm bar slot's
    ///             (UI/WristMenu.cs UseItemById). An empty bubble does nothing and takes no pull.
    ///   Inert     the bubbles never get in the way of the other ways of casting (design note: "does not interfere"; the one casting rule of wave
    ///             13 item 7, LokiGame.W13Resize.cs): they do nothing while resize mode, Reposition or the holster's placement is on, a
    ///             panel is held, the skill wheels are open, the bubbles' editor is drawn, or any MENU is open (UI/UiKit.cs PanelKinds) but
    ///             the overhead menu's main page (the bubbles sit by it: looking at them may wake it); and a hand that holds the wand, has
    ///             a pad / flick gesture open, holds a grab or the table does not use them. The other ways ignore them: no collider
    ///             (the laser and the look pass through), panel kind Own (never a menu), and a pull a bubble takes is no click.
    ///   Editor    the overhead menu's last row "Quick Use Bubbles" [Edit…] turns the menu to the bubbles' page (UI/OverheadMenu.cs
    ///             Pages): "Bubble" 1-4 (which one; Back), "Holds" (its skill / item: a picker window with Skills and Items; Clear),
    ///             "Active" Off / On, "Shown" Always / When looked at, "Use by" Touch / Trigger, "Move / resize" Off / On (Reposition, where
    ///             a grip carries a bubble and the other grip resizes it) with − / + (size steps of <see cref="BubbleStep"/>) and Reset
    ///             (its default place and size). The section is self-contained here (wave 21 redesigns the overhead menu: point 58).
    /// Hooks: LokiGame.W13Resize.cs InitW13Resize (after the main page's rows) and UpdateW13Resize; VR/LokiInput.cs BubbleProbe;
    /// LokiGame.W18Account.cs OnW18AccountApplied.
    /// </summary>
    public sealed partial class LokiGame
    {
        public const int BubbleCount = UserSettings.BubbleCount, BubblePage = 1;
        public const float BubbleElevation = 32f, BubbleDistance = 0.45f, BubbleSpread = 18f;
        public const float BubbleTriggerMargin = 0.01f, BubbleRearm = 0.02f, BubbleLockout = 0.4f;
        public const float BubbleLookFull = 4f, BubbleLookFrom = 12f, BubbleFade = 0.15f, BubbleGhostAlpha = 0.25f, BubbleEmptyAlpha = 0.6f;
        public const float BubbleStep = 1.15f, BubbleNearest = 0.15f, BubbleFarthest = 1.0f;
        /// <summary>Reposition takes a bubble with the hand's grip point IN it or within this of its outline (metres), or with the laser on it
        /// grown by as much: a bubble is large enough to reach into, and Reposition's 7 cm (the small holster and elbow markers: PlaceReach)
        /// let a hand reaching past the bubbles for the minimap take one (run w13view, F).</summary>
        public const float BubblePlaceReach = 0.02f;
        public const int BubbleNameMax = 24;

        private QuickBubble[] _w20;
        private int _w20Sel;
        private float _w20Yaw;
        private readonly bool[,] _w20In = new bool[BubbleCount, 2], _w20Armed = new bool[BubbleCount, 2];
        private readonly float[,] _w20Lock = new float[BubbleCount, 2];
        private readonly float[] _w20Alpha = new float[BubbleCount];
        private readonly bool[] _w20PlaceLit = new bool[BubbleCount];
        private readonly int[] _w20IconId = new int[BubbleCount]; private readonly bool[] _w20IconItem = new bool[BubbleCount]; private readonly Sprite[] _w20Icon = new Sprite[BubbleCount];
        private int _w20PendBubble = -1, _w20PendHand = -1;
        private ListWindow _w20Picker; private int _w20PickTab;

        /// <summary>The bubbles' pictures (null before the game's rig).</summary>
        public QuickBubble W20Bubble(int i) => _w20 != null && i >= 0 && i < BubbleCount ? _w20[i] : null;
        /// <summary>The bubble the editor works on (0-3).</summary>
        public int W20Selected => _w20Sel;
        /// <summary>Uses so far: all, by touch, by trigger; the last one (bubble, hand 0 left / 1 right, "touch" / "trigger"; -1 none).</summary>
        public int W20Uses { get; private set; }
        public int W20TouchUses { get; private set; }
        public int W20TriggerUses { get; private set; }
        public (int bubble, int hand, string how) W20LastUse { get; private set; } = (-1, -1, "");
        /// <summary>Pulls a bubble took (the probe said yes) and uses refused after it (the rule changed in between).</summary>
        public int W20PullsTaken { get; private set; }
        /// <summary>The picker window (null before the game's rig) and whether it is open.</summary>
        public ListWindow W20Picker => _w20Picker;
        public bool W20PickerOpen => _w20Picker != null && _w20Picker.Visible;

        // ------------------------------------------------------------------ where they are, what they hold
        /// <summary>This character's key for the contents ("Name#charId"; "" with no character).</summary>
        public string W20CharKey => Session != null && Session.SelectedChar != null ? Session.SelectedChar.Name + "#" + Session.SelectedChar.CharId : "";
        /// <summary>What bubble <paramref name="i"/> holds for this character: a skill id or an item id (item true); 0: nothing.</summary>
        public (int id, bool item) W20Holds(int i)
        {
            var s = UserSettings.BubbleSetFound(W20CharKey);
            if (s == null || s.ids == null || i < 0 || i >= s.ids.Length) return (0, false);
            return (s.ids[i], s.item != null && i < s.item.Length && s.item[i]);
        }
        private static string W20Name(int id, bool item) => id <= 0 ? "" : UiKit.StripTags(item ? GameDb.GetItem(id).Name : SkillDb.Get(id).Name);
        /// <summary>Bubble i's default place: <see cref="BubbleElevation"/>° up, <see cref="BubbleDistance"/> m from the eyes, at (i - 1.5) ×
        /// <see cref="BubbleSpread"/>° from the body's heading (metres in the body's frame: right, up, ahead).</summary>
        public static Vector3 W20Default(int i) => Quaternion.Euler(-BubbleElevation, (i - 1.5f) * BubbleSpread, 0f) * Vector3.forward * BubbleDistance;
        /// <summary>A place a bubble may take (the body's frame from the eyes): within the overhead menu's box, between
        /// <see cref="BubbleNearest"/> and <see cref="BubbleFarthest"/> m from the eyes (a hand must reach it; it never sits in the head).</summary>
        public static Vector3 W20Clamp(Vector3 v)
        {
            v = new Vector3(Mathf.Clamp(v.x, -0.8f, 0.8f), Mathf.Clamp(v.y, -0.6f, 1.0f), Mathf.Clamp(v.z, -0.3f, 1.2f));
            float m = v.magnitude; if (m < BubbleNearest) v = m > 1e-4f ? v * (BubbleNearest / m) : new Vector3(0f, BubbleNearest, 0f); else if (m > BubbleFarthest) v *= BubbleFarthest / m;
            return v;
        }
        /// <summary>Bubble i's place now in the body's frame from the eyes.</summary>
        public Vector3 W20Offset(int i) { var b = UserSettings.BubblesNow[i]; return b.placed ? W20Clamp(new Vector3(b.x, b.y, b.z)) : W20Default(i); }
        /// <summary>Bubble i's size factor (Reposition's range, VR/Comfort.cs PlaceMinScale..PlaceMaxScale) and radius (metres).</summary>
        public float W20Scale(int i) { var b = UserSettings.BubblesNow[i]; return Mathf.Clamp(b.scale > 0f ? b.scale : 1f, Comfort.PlaceMinScale, Comfort.PlaceMaxScale); }
        public float W20Radius(int i) => QuickBubble.BaseRadius * W20Scale(i);
        /// <summary>Bubble i's middle in the world now (the eyes' position, the body's heading of the last update).</summary>
        public Vector3 W20Centre(int i) => _boot.Rig.Head.transform.position + Quaternion.Euler(0f, _w20Yaw, 0f) * W20Offset(i);

        // ------------------------------------------------------------------ the rule (when they do nothing)
        private bool W20InWorld => W13InGame && !W8HordeBlocksWorld && _boot.Rig != null;
        /// <summary>The bubbles' editor is the overhead menu's page and drawn now.</summary>
        public bool W20Editing => W13Overhead != null && W13Overhead.Page == BubblePage && W13Overhead.Alpha > 0f;
        /// <summary>Why every bubble does nothing now (null: they work).</summary>
        public string W20Blocker
        {
            get
            {
                if (!W20InWorld) return "not in the world";
                if (W24BubblesHidden) return "the tutorial"; // client wave 24 (w24), item 7: hidden and unusable while the tutorial runs, but for their own lesson (LokiGame.W24Bubbles.cs)
                if (Player.Dead) return "dead";
                if (LokiInput.ResizeMode) return "resize mode";
                if (W13Reposition) return "reposition";
                if (W12WandPlacing) return "the wand holster's placement";
                if (W13PanelHeld) return "a panel held";
                if (WheelsOpen) return "the skill wheels";
                if (W20Editing) return "the bubbles' editor";
                if (PanelKinds.MenuOpen(out var which, "OverheadMenu")) return which; // (the overhead menu's main page: not a menu for the bubbles)
                return null;
            }
        }
        /// <summary>Why hand <paramref name="h"/> (0 left, 1 right) uses no bubble now (null: it may).</summary>
        public string W20HandBlocker(int h)
        {
            if (_w12 == null) return "no hands";
            var c = _w12[h];
            if (_wandHand == c) return "it holds the wand";
            if (W20PenIn(c)) return "it holds the pen"; // client wave 20 merge: the air pen (LokiGame.W20Pen.cs) is held as the wand is: its trigger draws, its tip passing a bubble uses nothing
            if (c.Open || c.In.CastHeld) return "its casting gesture is open";
            if (c.In.GrabClaimed) return "it holds a grab";
            if (c.In.TableClaimed) return "it holds the table";
            return null;
        }
        /// <summary>The On bubble with "Use by" <paramref name="useBy"/> (0 touch, 1 trigger) whose outline (grown by <paramref name="margin"/>)
        /// holds hand h's tip, the nearest middle first (-1: none).</summary>
        private int W20BubbleAt(int h, float margin, int useBy)
        {
            var tip = W12Tip(_w12[h]); var all = UserSettings.BubblesNow; int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < BubbleCount; i++)
            {
                if (!all[i].on || (all[i].useBy == 0 ? 0 : 1) != useBy) continue;
                float d = Vector3.Distance(tip, W20Centre(i));
                if (d <= W20Radius(i) + margin && d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>VR/LokiInput.cs BubbleProbe: the pull of <paramref name="input"/>'s trigger, reaching its click point now, is a bubble's
        /// (its tip in a filled "Use by: Trigger" bubble, the rule allowing it): yes takes the pull; the use follows in the next update.</summary>
        private bool W20BubbleProbe(LokiInput.Hand input)
        {
            if (_w20 == null || _w12 == null || _boot.Rig == null) return false;
            int h = input == _boot.Rig.Input.Left ? 0 : 1;
            if (W20Blocker != null || W20HandBlocker(h) != null) return false;
            int b = W20BubbleAt(h, BubbleTriggerMargin, 1);
            if (b < 0 || W20Holds(b).id <= 0) return false;
            _w20PendBubble = b; _w20PendHand = h; W20PullsTaken++;
            Debug.Log($"[Bubbles] the {(h == 0 ? "left" : "right")} hand's trigger pulled in bubble {b + 1}: the pull is the bubble's (no click, no wheels)");
            return true;
        }

        // ------------------------------------------------------------------ set up
        /// <summary>Called by InitW13Resize after every control of the overhead menu's main page: the bubbles, the main page's last row, the
        /// editor's page, Reposition's four bubbles, the picker, the trigger's probe.</summary>
        private void InitW20Bubbles(LokiBootstrap boot, OverheadMenu m)
        {
            var rig = boot.Rig; if (rig == null || m == null) return;
            _w20 = new QuickBubble[BubbleCount];
            for (int i = 0; i < BubbleCount; i++) { _w20[i] = QuickBubble.Create(rig.transform, rig.Head, i); _w20[i].gameObject.name = "QuickBubble"; }
            // the main page's entry (its last row)
            m.AddAction("bubbles", () => Lang.T("Quick Use Bubbles"), () => Lang.T("Edit…"), () => W20OpenEditor("the overhead menu"));
            W24AddOpacityAll(m); // client wave 24 (w24), item 11: every bubble's opacity, a slider after "Edit…" (LokiGame.W24Bubbles.cs)
            // the editor (the overhead menu's page 1)
            m.BeginPage(BubblePage);
            m.SetPageName(BubblePage, () => Lang.T("Quick Use Bubbles")); // client wave 21 (w21over), point 58: the page's name on the frame's gold plate
            m.AddSwitch("bubble", () => Lang.T("Bubble"), BubbleCount, () => _w20Sel, k => W20Select(k, "the editor"), k => (k + 1).ToString());
            m.AddLead("back", () => Lang.T("Back"), 160f, () => W20CloseEditor("its Back")); // (client wave 24, w24, item 16: at the row's LEFT end, before the icon: Back, the icon, "Bubble", 1 2 3 4; until then after the four choices)
            m.AddAction("holds", () => Lang.T("Holds"), () => W20HoldsLabel(_w20Sel), () => W20OpenPicker("the editor"));
            m.AddExtra("clear", () => Lang.T("Clear"), 160f, () => W20Assign(_w20Sel, 0, false, "the editor's Clear"), () => W20Holds(_w20Sel).id > 0);
            m.AddSwitch("active", () => Lang.T("Active"), 2, () => UserSettings.BubblesNow[_w20Sel].on ? 1 : 0, k => W20Change(_w20Sel, b => b.on = k == 1, k == 1 ? "on" : "off"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            W24AddOpacityOwn(m); // client wave 24 (w24), item 11: the chosen bubble's own opacity, a slider after "Active"'s choices
            m.AddSwitch("shown", () => Lang.T("Shown"), 2, () => UserSettings.BubblesNow[_w20Sel].shown == 0 ? 0 : 1, k => W20Change(_w20Sel, b => b.shown = k, k == 0 ? "shown always" : "shown when looked at"),
                k => k == 0 ? Lang.T("~shown~Always") : Lang.T("When looked at"));
            m.AddSwitch("useby", () => Lang.T("Use by"), 2, () => UserSettings.BubblesNow[_w20Sel].useBy == 0 ? 0 : 1, k => W20Change(_w20Sel, b => b.useBy = k, k == 0 ? "used by touch" : "used by the trigger"),
                k => k == 0 ? Lang.T("~use~Touch") : Lang.T("~use~Trigger"));
            m.AddSwitch("move", () => Lang.T("Move / resize"), 2, () => W13Reposition ? 1 : 0, k => W13SetReposition(k == 1, "the Quick Use Bubbles' editor"),
                k => k == 0 ? Lang.T("~switch~Off") : Lang.T("~switch~On"));
            m.AddExtra("smaller", () => "−", 80f, () => W20Resize(_w20Sel, 1f / BubbleStep, "the editor's −"), null, 72).Tip = () => Lang.T("Smaller");
            m.AddExtra("larger", () => "+", 80f, () => W20Resize(_w20Sel, BubbleStep, "the editor's +"), null, 72).Tip = () => Lang.T("Larger");
            m.AddExtra("reset", () => Lang.T("~place~Reset"), 160f, () => W20ResetPlace(_w20Sel, "the editor's Reset"), null);
            m.BeginPage(0);
            // Reposition: each bubble is one of its things (taken by the hand at it or the pointing laser + grip, the other grip resizes it)
            for (int i = 0; i < BubbleCount; i++)
            {
                int k = i;
                _w13Places.Add(new Placeable
                {
                    Name = $"quick use bubble {k + 1}", Shown = () => W13Reposition && W20InWorld && UserSettings.BubblesNow[k].on, Centre = () => W20Centre(k),
                    Near = p => Vector3.Distance(p, W20Centre(k)) <= W20Radius(k) + BubblePlaceReach, Laser = ray => W20RaySphere(ray, W20Centre(k), W20Radius(k) + BubblePlaceReach),
                    Factor = () => W20Scale(k), Resizable = true, Carry = (w, f) => W20CarryTo(k, w, f), Look = lit => _w20PlaceLit[k] = lit,
                    Release = () => { UserSettings.Save(); Debug.Log($"[Place] quick use bubble {k + 1}: {W13Cm(W20Offset(k))} from the eyes in the body's frame ({W13Deg(W20Offset(k))}), size x{W20Scale(k):0.00}"); },
                });
            }
            // the picker (a list window: a menu while it is open)
            _w20Picker = ListWindow.Create(rig.transform, "BubblePicker");
            _w20Picker.OnCloseClicked = () => _w20Picker.Hide();
            LokiInput.Hand.BubbleProbe = W20BubbleProbe;
        }

        /// <summary>The distance along the ray to a sphere (-1: it misses).</summary>
        private static float W20RaySphere(Ray ray, Vector3 c, float r)
        {
            var oc = ray.origin - c; float b = Vector3.Dot(oc, ray.direction), q = oc.sqrMagnitude - r * r, disc = b * b - q;
            if (disc < 0f) return -1f;
            float t = -b - Mathf.Sqrt(disc); if (t < 0f) t = -b + Mathf.Sqrt(disc);
            return t >= 0f ? t : -1f;
        }

        // ------------------------------------------------------------------ the editor's jobs
        public void W20OpenEditor(string why)
        {
            if (W13Overhead == null) return;
            W13Overhead.ShowPage(BubblePage, why);
            Debug.Log($"[Bubbles] the editor opened ({why}): bubble {_w20Sel + 1}");
        }
        public void W20CloseEditor(string why)
        {
            if (W13Overhead == null) return;
            W13Overhead.ShowPage(0, why);
            if (_w20Picker != null) _w20Picker.Hide();
        }
        public void W20Select(int i, string why)
        {
            _w20Sel = Mathf.Clamp(i, 0, BubbleCount - 1); W13Overhead?.Refresh();
            Debug.Log($"[Bubbles] bubble {_w20Sel + 1} chosen in the editor ({why})");
        }
        private string W20HoldsLabel(int i)
        {
            var (id, item) = W20Holds(i);
            if (id <= 0) return Lang.T("Choose…");
            string n = W20Name(id, item);
            return n.Length > BubbleNameMax ? n.Substring(0, BubbleNameMax - 1) + "…" : n;
        }
        /// <summary>A change of bubble i's layout (saved: the account's part).</summary>
        private void W20Change(int i, Action<UserSettings.Bubble> change, string what)
        {
            var b = UserSettings.BubblesForEdit()[i]; change(b); UserSettings.Save(); W13Overhead?.Refresh();
            Debug.Log($"[Bubbles] bubble {i + 1}: {what} (on {b.on}, shown {(b.shown == 0 ? "always" : "when looked at")}, use by {(b.useBy == 0 ? "touch" : "trigger")})");
        }
        /// <summary>Bubble i's size × <paramref name="factor"/> (within Reposition's range).</summary>
        public void W20Resize(int i, float factor, string why)
        {
            var b = UserSettings.BubblesForEdit()[i];
            b.scale = Mathf.Clamp((b.scale > 0f ? b.scale : 1f) * factor, Comfort.PlaceMinScale, Comfort.PlaceMaxScale); UserSettings.Save();
            Debug.Log($"[Bubbles] bubble {i + 1}: size x{b.scale:0.00} ({why})");
        }
        /// <summary>Bubble i back at its default place and size.</summary>
        public void W20ResetPlace(int i, string why)
        {
            var b = UserSettings.BubblesForEdit()[i]; b.placed = false; b.scale = 1f; UserSettings.Save();
            Debug.Log($"[Bubbles] bubble {i + 1}: back at its default place and size ({why})");
            Hud?.Notify(Lang.F("Bubble {0}: place reset.", i + 1), 3f);
        }
        /// <summary>Reposition carries bubble i to <paramref name="world"/> at size <paramref name="factor"/> (saved when let go).</summary>
        private void W20CarryTo(int i, Vector3 world, float factor)
        {
            var b = UserSettings.BubblesForEdit()[i];
            var local = W20Clamp(Quaternion.Inverse(Quaternion.Euler(0f, _w20Yaw, 0f)) * (world - _boot.Rig.Head.transform.position));
            b.placed = true; b.x = local.x; b.y = local.y; b.z = local.z;
            b.scale = Mathf.Clamp(factor, Comfort.PlaceMinScale, Comfort.PlaceMaxScale);
        }
        /// <summary>Bubble i now holds skill / item <paramref name="id"/> for this character (0: nothing).</summary>
        public void W20Assign(int i, int id, bool item, string why)
        {
            string key = W20CharKey; if (key == "") return;
            var s = UserSettings.BubbleSetOf(key); s.ids[i] = Mathf.Max(0, id); s.item[i] = id > 0 && item; UserSettings.Save();
            W13Overhead?.Refresh();
            Debug.Log($"[Bubbles] bubble {i + 1} of {key}: {(id > 0 ? (item ? "item " : "skill ") + id + " '" + W20Name(id, item) + "'" : "nothing")} ({why})");
            Hud?.Notify(id > 0 ? Lang.F("Bubble {0}: {1}.", i + 1, W20Name(id, item)) : Lang.F("Bubble {0} is empty.", i + 1), 3f);
        }

        // ------------------------------------------------------------------ the picker: what a bubble holds
        public void W20OpenPicker(string why)
        {
            if (_w20Picker == null) return;
            _w20PickTab = W20Holds(_w20Sel).item ? 1 : 0;
            W20FillPicker();
            _w20Picker.Open(_boot.Rig.Head);
            Debug.Log($"[Bubbles] the picker opened for bubble {_w20Sel + 1} ({why})");
        }
        private void W20FillPicker()
        {
            var w = _w20Picker; int sel = _w20Sel; var (heldId, heldItem) = W20Holds(sel);
            w.SetTitle(Lang.F("Bubble {0}: what it holds", sel + 1));
            w.SetInfo("");
            w.SetTabs(new[] { Lang.T("Skills"), Lang.T("Items") }, _w20PickTab, t => { _w20PickTab = t; W20FillPicker(); });
            var rows = new List<ListWindow.Row>();
            if (_w20PickTab == 0)
            {
                foreach (var k in Prog.Skills.Values.Where(k => k.Level > 0 && !k.Passive).OrderBy(k => k.Id))
                {
                    int id = k.Id;
                    rows.Add(new ListWindow.Row { Icon = WristMenu.SkillIcon(k.Info), Text = W20Name(id, false), A = Lang.T("Choose"), OnA = () => W20Picked(id, false), Highlight = !heldItem && heldId == id });
                }
            }
            else
            {
                var ids = new List<int>(); foreach (var e in Inventory.Items.Values) if (!ids.Contains(e.ItemId)) ids.Add(e.ItemId);
                bool Usable(int t) => t == 0 || t == 2 || t == 11 || t == 18; // (rAthena IT_HEALING, IT_USABLE, IT_DELAYCONSUME, IT_CASH: first)
                foreach (int id in ids.OrderBy(i => Usable(GameDb.GetItem(i).Type) ? 0 : 1).ThenBy(i => W20Name(i, true), StringComparer.CurrentCulture))
                {
                    int iid = id;
                    rows.Add(new ListWindow.Row { Icon = WristMenu.ItemIcon(GameDb.GetItem(iid)), Text = $"{W20Name(iid, true)}  ×{Inventory.Count(iid)}", A = Lang.T("Choose"), OnA = () => W20Picked(iid, true), Highlight = heldItem && heldId == iid });
                }
            }
            w.SetRows(rows, true);
            w.SetFooter();
        }
        private void W20Picked(int id, bool item)
        {
            W20Assign(_w20Sel, id, item, "the picker");
            _w20Picker.Hide();
        }

        // ------------------------------------------------------------------ the use
        /// <summary>Bubble i used by hand h (0 left, 1 right): its skill taken by the casting rule, its item used.</summary>
        private void W20Use(int i, int h, string how)
        {
            var (id, item) = W20Holds(i);
            if (id <= 0) return;
            W20Uses++; if (how == "touch") W20TouchUses++; else W20TriggerUses++;
            W20LastUse = (i, h, how);
            Debug.Log($"[Bubbles] bubble {i + 1} used by {how} of the {(h == 0 ? "left" : "right")} hand: {(item ? "item" : "skill")} {id} '{W20Name(id, item)}'");
            Haptics.Pulse(Haptics.Kind.Casting, _w12[h].In.IsMain, 0.25f, 0.04f);
            _w20[i].Pop();
            if (item) { Hud.Wrist.UseItemById(id); return; } // (as a forearm bar slot's item: its first stack; none in the bag: said so)
            W12TakeSkillId(id, h == 0, Comfort.CastOnTake, Comfort.VisionAimSpells); // (the casting rule of the pad and the flick: LokiGame.W12Cast.cs)
        }

        // ------------------------------------------------------------------ once a frame (from UpdateW13Resize)
        private void UpdateW20Bubbles()
        {
            var rig = _boot.Rig; if (_w20 == null || rig == null || _w12 == null) return;
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            _w20Yaw = W12Wand != null ? W12Wand.BodyYaw : rig.Head.transform.eulerAngles.y; // (the overhead menu's heading)
            bool world = W20InWorld;
            if (!world && W13Overhead != null && W13Overhead.Page == BubblePage) W20CloseEditor("the world went away");
            string block = W20Blocker; bool editing = W20Editing, placing = W13Reposition;
            // a trigger pull the probe gave to a bubble
            if (_w20PendBubble >= 0)
            {
                int pb = _w20PendBubble, ph = _w20PendHand; _w20PendBubble = -1;
                if (block == null && W20HandBlocker(ph) == null) W20Use(pb, ph, "trigger");
                else Debug.Log($"[Bubbles] bubble {pb + 1}'s pull not used: {block ?? W20HandBlocker(ph)}");
            }
            var all = UserSettings.BubblesNow; var gaze = rig.GazeRay;
            for (int i = 0; i < BubbleCount; i++)
            {
                var b = all[i]; var c = W20Centre(i); float r = W20Radius(i);
                var (id, item) = W20Holds(i);
                bool lit = false;
                for (int h = 0; h < 2; h++)
                {
                    float d = Vector3.Distance(W12Tip(_w12[h]), c);
                    bool usable = world && b.on && id > 0 && block == null && W20HandBlocker(h) == null;
                    bool inside = d <= r + (b.useBy == 0 ? 0f : BubbleTriggerMargin);
                    if (inside && !_w20In[i, h] && usable) Haptics.Pulse(Haptics.Kind.Ui, _w12[h].In.IsMain, 0.1f, 0.015f); // (a light tick: the tip is in a bubble it may use)
                    _w20In[i, h] = inside; lit |= inside && usable;
                    // "Use by: Touch": once on entering; again after leaving by BubbleRearm and BubbleLockout s (a tip already in it when it
                    // could not be used, or when it was switched on, must leave first)
                    if (d > r + BubbleRearm && now >= _w20Lock[i, h]) _w20Armed[i, h] = true;
                    if (d <= r)
                    {
                        if (b.useBy == 0 && usable && _w20Armed[i, h]) { _w20Lock[i, h] = now + BubbleLockout; W20Use(i, h, "touch"); }
                        _w20Armed[i, h] = false;
                    }
                }
                // how solid it is drawn
                float want;
                if (!world || W24BubblesHidden) want = 0f; // (client wave 24, w24, item 7: not drawn while the tutorial runs, but for their own lesson)
                else if (!b.on) want = editing || placing ? BubbleGhostAlpha : 0f;
                else if (editing || placing || b.shown == 0 || W24BubblesLesson) want = 1f; // (client wave 24: in their lesson always shown, whatever "Shown" says: the setting itself is not written)
                else
                {
                    float dist = Vector3.Distance(gaze.origin, c), own = Mathf.Atan2(r, Mathf.Max(dist, 1e-3f)) * Mathf.Rad2Deg, ang = Vector3.Angle(gaze.direction, c - gaze.origin);
                    want = Mathf.Clamp01((own + BubbleLookFrom - ang) / (BubbleLookFrom - BubbleLookFull));
                }
                if (b.on && id <= 0 && !(editing || placing)) want *= BubbleEmptyAlpha;
                _w20Alpha[i] = Mathf.MoveTowards(_w20Alpha[i], want, dt / BubbleFade);
                var pic = _w20[i];
                pic.Place(c, r);
                if (_w20Alpha[i] <= 0f) { pic.Draw(0f, null, "", 0f, "", false, false, false, false); continue; }
                // what it shows: the icon (kept while the thing is the same), an item's count, a skill's wait (the forearm bar's own)
                if (_w20IconId[i] != id || _w20IconItem[i] != item) { _w20IconId[i] = id; _w20IconItem[i] = item; _w20Icon[i] = id <= 0 ? null : item ? WristMenu.ItemIcon(GameDb.GetItem(id)) : WristMenu.SkillIcon(SkillDb.Get(id)); }
                string count = id > 0 && item ? Inventory.Count(id).ToString() : id > 0 && !Prog.Skills.ContainsKey(id) ? "—" : "";
                float fill = 0f; string cool = "";
                if (id > 0 && !item && Quick != null) { var (left, total) = Quick.BlockedSkill(id); if (left > 0.02f && total > 0f) { fill = left / total; cool = left >= 1f ? Mathf.CeilToInt(left).ToString() : left.ToString("0.0"); } }
                bool selected = editing && i == _w20Sel;
                pic.Solid = W24BubbleSolid(i); pic.Contrast = W24BubbleContrast; // client wave 24 (w24), items 11 and 12
                pic.Draw(_w20Alpha[i], _w20Icon[i], count, fill, cool, lit || _w20PlaceLit[i], selected, editing || placing, !b.on);
            }
        }

        /// <summary>Hook: the account's settings arrived (LokiGame.W18Account.cs): the editor's labels anew (the contents are the account's too).</summary>
        private void W20AccountApplied() { W13Overhead?.Refresh(); }
    }
}
