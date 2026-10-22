using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using Profile = Loki.VR.ControllerProfiles.Profile;

namespace Loki
{
    /// <summary>
    /// Client wave 22 (w22clips): the tutorial's CASTING WAYS. design note, 2026-10-11: "During the tutorial, make it abundantly clear that there are
    /// ... different casting methods and ask the player which one they are interested in. The player can test multiple of the casting methods
    /// and after they have tested at least one, they are also offered the button to continue with the tutorial", then: "Just list out all of
    /// the currently existing casting methods and have each one have their own explanation ... there's going to be a panel that says casting.
    /// There are different ways to cast your abilities in Loki. And then each one shows up as a column on a large panel. And it describes its
    /// name, how it works, a small video of it that plays on loop, and its best use case scenarios. And then if you click on that column, it
    /// moves you to a following panel that has you test it out. And at the end, it brings you back to the multiple columns. But at that
    /// point, you can actually also choose to progress."
    ///   The ways  every way a skill is cast in the game today, read from the code (<see cref="W22MakeWays"/>; one column each):
    ///             the pad            LokiGame.W12Cast.cs (the casting gesture, VR/Comfort.cs CastGesture 1), UI/CastPad.cs
    ///             the flick          LokiGame.W12Cast.cs (CastGesture 2), Game/FlickRecognizer.cs, UI/FlickHud.cs
    ///             the wand           LokiGame.W12Wand.cs (CastGesture 3), VR/WandHolster.cs, UI/WandWheel.cs
    ///             the forearm bar    UI/QuickBar.cs (LokiGame.cs Quick; VR/Comfort.cs QuickBarMode), its elbow button LokiGame.W13View.cs
    ///             the skill wheels   LokiGame.Wheels.cs, UI/SkillWheel.cs (the walking hand's trigger held; no setting switches them off)
    ///             Quick Use Bubbles  LokiGame.W20Bubbles.cs, UI/QuickBubble.cs
    ///             the Skills page    UI/WristMenu.Skills.cs:127 (a skill's "Use" button; the classic menu's rows UI/WristMenu.cs:365)
    ///             VARIANTS, kept inside their way: the pad's rows by palm or by slap (Comfort.PadSlap); the wand's wheel, its two strokes
    ///             and its quick buttons (Comfort.WandQuick), its aim by wand or by look; the forearm bar always on / off / by the elbow
    ///             button; the bubbles' Touch / Trigger; the menu's Quick bar page (UI/WristMenu.cs:446, the same quick slots listed).
    ///             NOT ways of their own: "Cast when taken" and the spell that waits in the hand (how a target skill is aimed after ANY
    ///             way), the plain attack (the trigger on a monster), an item's Use in the bag.
    ///   The step  the tutorial has ONE step "casting" where the four casting steps were (LokiGame.W18Tutorial.cs): its page is the
    ///             columns' panel (UI/CastingWays.cs), not the guide. A column pressed starts that way's LESSON on the guide (the step
    ///             wave 18 / 20 wrote, or a new one: <see cref="W22BuildLessons"/>), with its clip and ghost controller (points 53 / 59)
    ///             and "Back" always there; the lesson's own start switches the setting the way needs. Its last part done: back to the
    ///             columns, the way's column checked, and from then on "Continue the tutorial" is offered. "Back" before the end: back
    ///             to the columns with no check, the settings as they were before the lesson.
    ///   After     <see cref="W22KeepLastTried"/>: the casting settings stay as the way tried LAST left them (the gesture, the forearm
    ///             bar's mode); with none tried they are the player's own again. The tutorial then goes on with the step after
    ///             (the last page). Trying every way is never needed.
    ///   Again     Settings → Controls → Controls tutorial shows the tutorial again; on its first page a tutorial shown AGAIN has a
    ///             third button, "Casting ways", that goes straight to the columns (<see cref="W22TutMiddle"/>): every way's lesson can be
    ///             played on its own, any time. (The game has no other tutorial entry and no "replay a part" list.)
    ///   Clips     each column's small picture is its way's clip (point 53) played live by a stage of its own (UI/TutorialClip.cs), at
    ///             <see cref="W22ColumnFps"/> pictures a second, one after the other, only while the column is within
    ///             <see cref="W22ColumnViewDeg"/>° of where the head points.
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>After the casting step the casting settings stay as the way tried last left them (false: the player's own come back).
        /// Client wave 25 (w25tut), item 9: NOT USED any more: the player chooses the way to start with (LokiGame.W25Tut.cs); kept false
        /// so nothing reads it as the rule.</summary>
        public const bool W22KeepLastTried = false;
        public const float W22ColumnFps = 15f, W22ColumnViewDeg = 62f, W22TipPressM = 0.006f, W22TipArmM = 0.04f;

        /// <summary>What each way's clip is FRAMED ON (UI/TutorialClip.cs Focus): the beginnings of the names of the way's own tool
        /// panels in its script. In a column and on its lesson's page the picture shows the hands and the tool, not the whole room.</summary>
        private static readonly Dictionary<string, string[]> W22Focus = new Dictionary<string, string[]>
        {
            { "cast-pad", new[] { "CastPad" } }, { "cast-flick", new[] { "Flick" } }, { "cast-wand", new[] { "WandWheel" } },
            { "cast-elbow", new[] { "QuickBar", "ElbowButton" } }, { "cast-wheels", new[] { "SkillWheel" } },
            { "cast-bubbles", new[] { "QuickBubble", "OverheadMenu" } }, { "cast-skills", new[] { "MenuRing", "MenuBoard" } },
        };
        private static string[] W22FocusOf(string key) => key != null && W22Focus.TryGetValue(key, out var f) ? f : null;

        private sealed class W22Way
        {
            public string Key; public Func<string> Name, How, Best, Blocked; public W18TStep Lesson;
        }
        private List<W18TStep> _w22Lessons; private W18TStep _w22Lesson; private List<W22Way> _w22Ways;
        private string _w22LastTried; private readonly List<string> _w22Tried = new List<string>(); private readonly List<string> _w22CastLog = new List<string>();
        private CastingWays _w22Columns; private TutorialClip[] _w22ColClips; private readonly bool[] _w22TipArmed = { true, true };
        private int _w22Gesture0 = -1, _w22Bar0 = -1; private bool _w22Elbow0; private Profile _w22ColProfile = (Profile)(-1); private bool _w22ColLeft;
        /// <summary>The columns' panel (null before the first casting step), the ways' keys in the columns' order, the way whose lesson runs
        /// ("" none), the ways tried in this tutorial, the last one, every lesson's outcome in order ("cast-pad:tried" / "cast-pad:back"),
        /// the columns' clips (smoke, log).</summary>
        public CastingWays W22Columns => _w22Columns;
        public IReadOnlyList<string> W22WayKeys { get { var l = new List<string>(); if (_w22Ways != null) foreach (var w in _w22Ways) l.Add(w.Key); return l; } }
        public string W22LessonKey => _w22Lesson != null ? _w22Lesson.Key : "";
        public IReadOnlyList<string> W22Tried => _w22Tried;
        public string W22LastTried => _w22LastTried ?? "";
        public IReadOnlyList<string> W22CastLog => _w22CastLog;
        public IReadOnlyList<TutorialClip> W22ColumnClips => _w22ColClips;
        public int W22ColumnPicks { get; private set; }
        public int W22TipPicks { get; private set; }
        /// <summary>The welcome page offers "Casting ways": the tutorial is shown again to a character that had it.</summary>
        public bool W22JumpOffered => W18TutRunning && W18TutHad;

        private bool W22NewMenu => Hud != null && Hud.Wrist != null && Hud.Wrist.Ring != null && Hud.Wrist.NewStyle; // (client wave 23, w23lines: the style in force, as the lines are written for)
        private bool W22MenuOpen => Hud != null && Hud.Wrist != null && (W22NewMenu ? Hud.Wrist.Ring.Shown || (Hud.Wrist.Board != null && Hud.Wrist.Board.Shown) : Hud.Wrist.ShownOnWrist);
        private bool W22MenuPageOpen => Hud != null && Hud.Wrist != null && (W22NewMenu ? Hud.Wrist.Board != null && Hud.Wrist.Board.Shown : Hud.Wrist.ShownOnWrist);

        // ------------------------------------------------------------------ the ways
        /// <summary>Every way to cast, in the columns' order. A column's texts: its name, how it works (a few sentences, written from the
        /// way's own code), what it is best for (three short lines: flagged for review's review; the reasons are in design notes).</summary>
        private List<W22Way> W22MakeWays()
        {
            W18TStep Of(string key) => _w22Lessons != null ? _w22Lessons.Find(s => s.Key == key) : null;
            var ways = new List<W22Way>
            {
                new W22Way { Key = "cast-pad", Name = () => Lang.T("The pad"),
                    How = () => Lang.T("Stick out your index finger and squeeze the grip to open a pad. Touch a slot and let go."),
                    Best = () => string.Join("\n", Lang.T("• Seeing your slots"), Lang.T("• Careful picks")) },
                new W22Way { Key = "cast-flick", Name = () => Lang.T("The flick"),
                    How = () => Lang.T("Squeeze the grip with your finger out. Flick toward a row, then toward a slot."),
                    Best = () => string.Join("\n", Lang.T("• Fast casting"), Lang.T("• Eyes on the fight")) },
                new W22Way { Key = "cast-wand", Name = () => Lang.T("The wand"),
                    How = () => Lang.T("Take the wand from your hip. Hold the trigger and point its tip at a slice."),
                    Best = () => string.Join("\n", Lang.T("• Feeling like a mage"), Lang.T("• A few favourites")) },
                new W22Way { Key = "cast-elbow", Name = () => Lang.T("The forearm bar"),
                    How = () => Lang.T("Your quick slots sit along your arm. Point your laser at one and pull the trigger."),
                    Best = () => string.Join("\n", Lang.T("• Starting out"), Lang.T("• Watching cooldowns")) },
                new W22Way { Key = "cast-wheels", Name = () => Lang.T("The skill wheels"),
                    How = () => (Comfort.LeftHanded ? Lang.T("Hold your right trigger to open two wheels. Pick a slot with a stick, then pull the other trigger.") : Lang.T("Hold your left trigger to open two wheels. Pick a slot with a stick, then pull the other trigger.")),
                    Best = () => string.Join("\n", Lang.T("• Thumbs only"), Lang.T("• Playing seated")),
                    Blocked = () => W23NoSticks ? Lang.T("Needs sticks.") : null },
                new W22Way { Key = "cast-bubbles", Name = () => Lang.T("Quick Use Bubbles"),
                    How = () => Lang.T("Bubbles above you each hold one skill or item. Touch one and pull the trigger."),
                    Best = () => string.Join("\n", Lang.T("• Your favourites"), Lang.T("• Quick potions")) },
                new W22Way { Key = "cast-skills", Name = () => Lang.T("The Skills page"),
                    How = () => Lang.T("Open the menu and choose Skills. Pick a skill and press Use."),
                    Best = () => string.Join("\n", Lang.T("• Rarely used skills"), Lang.T("• Reading first")) },
            };
            foreach (var w in ways) w.Lesson = Of(w.Key);
            // client wave 23 (w23lines), item 10: the pad, the flick and the wand begin with a grip: a controller without one (the simple
            // controller: VR/LokiInput.cs Grip has no binding for it) cannot try them
            foreach (var w in ways) if (w.Key == "cast-pad" || w.Key == "cast-flick" || w.Key == "cast-wand") w.Blocked = () => W23NoGrip ? Lang.T("Needs a grip.") : null;
            ways.RemoveAll(w => w.Lesson == null);
            return ways;
        }

        /// <summary>The lessons of the three ways the tutorial did not teach before (the pad's, the flick's, the wand's and the forearm bar's
        /// are wave 18 / 20's steps: LokiGame.W18Tutorial.cs W18TBuildSteps). Each waits on the way's own state, as every step does.</summary>
        private void W22BuildLessons(Func<string, Func<string>, W18TStep> L)
        {
            // the skill wheels (LokiGame.Wheels.cs): opened by the walking hand's trigger held WheelHoldSeconds; a stick picks a slot
            // (UI/SkillWheel.cs SlotAt); the pointing hand's trigger uses a FILLED slot (an empty one does nothing: a new character's are
            // empty, so the lesson waits for the slot lit, not for a use); letting go closes them
            var wheels = L("cast-wheels", () => Lang.T("Casting: the skill wheels"));
            wheels.About = () => Lang.T("Two wheels put your quick slots under your thumbs. The other trigger uses the lit slot.");
            // (client wave 23, w23lines, item 10: each line names the hand and the control: the walking hand's trigger held, either stick, that trigger let go)
            wheels.Phases.Add(W23U(W18TP(() => (Comfort.LeftHanded ? Lang.T("Hold your right trigger until two wheels open.") : Lang.T("Hold your left trigger until two wheels open.")), () => WheelsOpen, () => (string)null), W23In.OffTrigger));
            wheels.Phases.Add(W25Mark(W23U(W18TP(W23WheelPick,
                () => WheelsOpen && ((WheelLeft != null && WheelLeft.Selected >= 0) || (WheelRight != null && WheelRight.Selected >= 0)), () => (string)null), W23In.AnyStick), W25MarkWheels)); // (client wave 25, item 4: the open wheels outlined green)
            wheels.Phases.Add(W23U(W18TP(() => Lang.F("Let go of your {0} trigger to close the wheels.", W23M(false)), () => !WheelsOpen), W23In.OffTrigger));

            // the Quick Use Bubbles (LokiGame.W20Bubbles.cs): four bubbles under the overhead menu, drawn when looked at; an empty bubble does
            // nothing (a new character's are empty), so the lesson shows where they are and where they are filled: the overhead menu's page
            var bubbles = L("cast-bubbles", () => Lang.T("Casting: Quick Use Bubbles"));
            bubbles.About = () => Lang.T("Bubbles float above you. Each holds one skill or item you pick.");
            bool AnyBubble() { if (!W24LooksAtBubble()) return false; for (int i = 0; i < BubbleCount; i++) { var b = W20Bubble(i); if (b != null && b.Drawn && b.Alpha >= 0.5f) return true; } return false; } // (client wave 24, w24: in this lesson the bubbles are always drawn, so the part waits for the LOOK on one: LokiGame.W24Bubbles.cs)
            bubbles.Phases.Add(W25AtMenu(W25Mark(W18TP(() => Lang.T("Look up at the bubbles."), () => AnyBubble() || W20Editing, () => Lang.T("Empty ones are faint.")), W25MarkBubbles))); // (client wave 24, w24: they are always drawn in this lesson: the line was "Look up a little until the bubbles appear."; client wave 25, item 4: the bubbles outlined green)
            // client wave 25 (w25tut), items 4 and 7: the look up to the menu above a part of its own with its check mark (design note: "when I
            // look up, the tutorial doesn't update that I have looked up"), the guide beside the menu from there on, "Edit…" and then the
            // bubbles' page's "Back" outlined green
            bubbles.Phases.Add(W25LookUpPart(() => Lang.T("Look higher until the menu above you appears.")));
            // client wave 23 (w23lines), item 13: the look up and the press are told apart: the line is "look higher" until the menu above
            // is there, then the press it waits for: the button "Edit…" in the row "Quick Use Bubbles" (LokiGame.W20Bubbles.cs:198); its
            // page's "Back" by its name
            bubbles.Phases.Add(W25AtMenu(W25Mark(W23U(W18TP(() => W23PressBeside(Lang.T("Edit…"), Lang.T("Quick Use Bubbles")), () => W20Editing, () => (string)null), W23In.MainTrigger), W25MarkOverhead("action:bubbles"))));
            // client wave 24 (w24), item 16 (design note: "when it says point it back and pull the trigger, it gives the player the impression
            // that they need to press back on the tutorial panel, not the quick use bubble panel"): the line names the panel
            bubbles.Phases.Add(W25AtMenu(W25Mark(W23U(W18TP(() => Lang.F("On the bubbles' page, point at {0} and {1}.", Lang.T("Back"), W23Pull()), () => !W20Editing && (W13Overhead == null || W13Overhead.Page == 0)), W23In.MainTrigger), W25MarkOverhead("extra:bubble:back"))));
            bubbles.Leave = () => { if (W13Overhead != null && W13Overhead.Page != 0) W13Overhead.ShowPage(0, "the tutorial's bubbles lesson ended"); };

            // the Skills page (UI/WristMenu.Skills.cs): every learned skill listed; a skill's details have its "Use" button
            var skills = L("cast-skills", () => Lang.T("Casting: the Skills page"));
            skills.About = () => Lang.T("Every skill you know is on the Skills page. Its Use button casts it.");
            // client wave 23 (w23lines), items 10 and 13 (the design's "one press, one line" for the Settings step, and the same here): the menu
            // button (the ring), the Skills ICON on the ring (its page), the menu button again. "Its page" is the Skills page OR the 3D
            // skill trees, which the ring's Skills icon opens instead of a board with "Skill tree: 3D", the default (UI/WristMenu.Tree.cs
            // TreeOpens: the ring and the board close; the lesson waited for a board for ever). The classic menu: the menu button, the
            // "Skills" tab, the menu button again
            skills.Build = () =>
            {
                skills.Phases.Clear();
                bool TreeShown() => Hud != null && Hud.Wrist != null && Hud.Wrist.Tree != null && Hud.Wrist.Tree.Shown;
                bool There() => TreeShown() || (W22MenuPageOpen && Hud.Wrist.CurrentTab == WristMenu.Tab.Skills);
                if (W22NewMenu)
                {
                    bool OtherPage() => Hud.Wrist.Board != null && Hud.Wrist.Board.Shown && Hud.Wrist.BoardGroup == WristMenu.SkillsGroup && Hud.Wrist.CurrentTab != WristMenu.Tab.Skills;
                    string IconName() => Lang.T("Skills");
                    string TabLine() => Lang.F("On the {0} board, point at {1} and {2}.", Lang.T("Skills"), Lang.T("Skills"), W23Pull());
                    int first = skills.Phases.Count;
                    W23RingWay(skills, IconName, WristMenu.SkillsGroup, There, () => OtherPage() ? TabLine() : W23RingLine(IconName, WristMenu.SkillsGroup)); // (the board on its Quick bar page: its "Skills" tab)
                    var lines = new List<Func<string>>(W23RingLines(IconName)); lines.Add(TabLine);
                    for (int k = first; k < skills.Phases.Count; k++) skills.Phases[k].Alt = lines.ToArray();
                    for (int k = first; k < skills.Phases.Count; k++) skills.Phases[k].Marks = W25MarkRingTo(WristMenu.SkillsGroup, "Skills"); // (client wave 25, item 4: the Skills icon, or the board's "Skills" tab, outlined green)
                }
                else
                {
                    skills.Phases.Add(W23U(W18TP(W23OpenMenu, () => Hud.Wrist.ShownOnWrist || There()), W23In.Menu));
                    skills.Phases.Add(W25Mark(W23Or(() => !Hud.Wrist.ShownOnWrist, W23OpenMenu, W23U(W18TP(() => Lang.F("Point at the {0} tab and {1}.", Lang.T("Skills"), W23Pull()), There), W23In.MainTrigger)), W25MarkWrist("btn:Skills"))); // (client wave 25, item 4)
                }
                skills.Phases.Add(W23U(W18TP(W23CloseMenu, () => !W22MenuOpen && !TreeShown()), W23In.Menu));
            };
        }

        // ------------------------------------------------------------------ the casting step: the columns
        private void W22HookClip(TutorialClip c)
        {
            c.Map = () => _boot.Map != null ? _boot.Map.transform : null;
            c.Gat = () => _boot.Map != null && _boot.Map.Current != null ? _boot.Map.Current.Gat : null;
            c.Assets = () => _boot.Assets;
            c.Self = () => Entities != null ? Entities.Self : null;
            c.RealPoring = () => _w18tDummy;
            c.Units = () => Entities != null ? Entities.Units.Values : null;
        }

        /// <summary>The tutorial came to the casting step (or back to it): the columns in front of the eyes, the guide away.</summary>
        private void W22CastEnter()
        {
            var rig = _boot.Rig; if (rig == null) return;
            _w22Ways = W22MakeWays(); _w22Lesson = null;
            if (_w22Columns == null || _w22Columns.Columns.Count != _w22Ways.Count)
            {
                if (_w22Columns != null) Destroy(_w22Columns.gameObject);
                _w22Columns = CastingWays.Create(rig.transform, () => rig.Head, _w22Ways.Count);
                _w22Columns.Picked = W22Pick; _w22Columns.ContinuePressed = W22Continue; _w22Columns.EndPressed = () => { if (W18TutRunning) W18TutRight(); };
                _w22Columns.EndButton.LaserOnly = true; // client wave 23 (w23lines), item 5: "End tutorial" is the laser's alone, here as on the guide (never the look's)
                if (_w22ColClips != null) foreach (var c in _w22ColClips) if (c != null) Destroy(c.gameObject);
                _w22ColClips = new TutorialClip[_w22Ways.Count];
                for (int i = 0; i < _w22ColClips.Length; i++)
                {
                    var c = TutorialClip.Create(rig, (int)CastingWays.PictureW, (int)CastingWays.PictureH); c.name = "TutorialClipColumn" + (i + 1);
                    c.PicturesPerSecond = W22ColumnFps; c.Stagger = i / (float)_w22ColClips.Length; W22HookClip(c); _w22ColClips[i] = c;
                }
            }
            W22ColumnsShow();
            Debug.Log($"[Tutorial] the casting ways: {_w22Ways.Count} columns ({string.Join(", ", W22WayKeys)}), {CastingWays.PitchDegrees:0.0}° each, {_w22Columns.WidthDegrees:0}° wide; tried so far: {(_w22Tried.Count > 0 ? string.Join(", ", _w22Tried) : "none")}");
        }

        private void W22ColumnsShow()
        {
            W18TGuide.Hide();
            _w22Columns.Show(); _w22TipArmed[0] = _w22TipArmed[1] = false; // (a fingertip already through a column presses nothing)
            _w22ColProfile = (Profile)(-1); // (the columns' clips start anew)
            W22ColumnsTick(0f);
        }

        private void W22ColumnsHide()
        {
            if (_w22Columns != null) _w22Columns.Hide();
            if (_w22ColClips != null) foreach (var c in _w22ColClips) if (c != null) c.Stop();
        }

        /// <summary>The tutorial leaves the casting step (on, or ended): a lesson that was running ends without a mark; the columns go.</summary>
        private void W22CastLeave()
        {
            if (_w22Lesson != null) W22LessonEnd(false, "the casting step ended", false);
            W22ColumnsHide(); W25ChoiceHide(); // (client wave 25, item 9: the page after the casting ways too)
        }

        /// <summary>Once a frame while the columns are the tutorial's page: their texts and marks, the way on once a way was tried, the
        /// clips, a fingertip's press.</summary>
        private void W22ColumnsTick(float dt)
        {
            if (_w22Columns == null || _w22Ways == null) return;
            if (!_w22Columns.Visible) W22ColumnsShow();
            var rig = _boot.Rig; bool any = _w22Tried.Count > 0;
            _w22Columns.SetHead(Lang.T("There are different ways to cast your abilities in Loki."),
                any ? Lang.T("You can change it later in Settings.") : Lang.T("Pick one to try."),
                any, Lang.T("Continue the tutorial"), Lang.T("End tutorial"));
            var profile = ComfortWindow.Active(); if (profile == Profile.None) profile = Profile.Index;
            bool left = Comfort.LeftHanded, anew = profile != _w22ColProfile || left != _w22ColLeft; _w22ColProfile = profile; _w22ColLeft = left;
            var head = rig.Head.transform; var hf = head.forward;
            for (int i = 0; i < _w22Ways.Count; i++)
            {
                var w = _w22Ways[i]; var clip = _w22ColClips[i]; var col = _w22Columns.Columns[i];
                var tl = W22ClipsOff ? null : W22Load(w.Key, profile, left);
                if (tl == null) { if (clip.Playing) clip.Stop(); }
                else if (anew || !clip.Playing || clip.Clip != tl) { clip.Focus = W22FocusOf(w.Key); clip.Play(tl, profile); }
                // a column's picture is drawn only while the column is in view (the arc is wider than the eyes see at once)
                clip.Drawing = Vector3.Angle(hf, col.Panel.position - head.position) <= W22ColumnViewDeg;
                _w22Columns.SetColumn(i, w.Name(), w.How(), w.Best(), _w22Tried.Contains(w.Key), w.Blocked?.Invoke(), tl != null ? clip.Texture : null);
            }
            // a fingertip pushed through a column presses it (the laser and the look press it as any button)
            for (int h = 0; h < 2; h++)
            {
                var hand = h == 0 ? rig.LeftHand : rig.RightHand; var tip = hand.position + hand.forward * TipAhead; bool near = false;
                for (int i = 0; i < _w22Ways.Count && W18TutRunning && _w22Lesson == null; i++)
                {
                    var col = _w22Columns.Columns[i]; var lp = col.Panel.InverseTransformPoint(tip); float depth = lp.z * CastingWays.MetresPerPx; // (− in front of the column's face)
                    if (Mathf.Abs(lp.x) > CastingWays.ColW * 0.5f || Mathf.Abs(lp.y) > CastingWays.ColH * 0.5f || depth < -0.12f || depth > 0.05f) continue;
                    if (depth > -W22TipArmM) near = true;
                    if (_w22TipArmed[h] && depth >= -W22TipPressM && !col.Blocked) { _w22TipArmed[h] = false; W22TipPicks++; Haptics.Pulse(Haptics.Kind.Ui, (h == 1) != Comfort.LeftHanded, 0.3f, 0.03f); Debug.Log($"[Tutorial] a fingertip pressed the column '{_w22Ways[i].Key}'"); W22Pick(i); return; }
                }
                if (!near) _w22TipArmed[h] = true;
            }
        }

        /// <summary>A column was pressed: its way's lesson on the guide (the following panel).</summary>
        private void W22Pick(int i)
        {
            if (!W18TutRunning || _w22Ways == null || i < 0 || i >= _w22Ways.Count || _w22Lesson != null) return;
            if (_w18tSteps[_w18tIndex].Key != "casting") return;
            var w = _w22Ways[i];
            if (w.Blocked != null && w.Blocked() != null) { Debug.Log($"[Tutorial] the way '{w.Key}' cannot be tried now: {w.Blocked()}"); return; }
            W22ColumnPicks++;
            _w22Gesture0 = Comfort.CastGesture; _w22Bar0 = Comfort.QuickBarMode; _w22Elbow0 = Comfort.ElbowBarOpen;
            W22ColumnsHide();
            _w22Lesson = w.Lesson; _w18tPhase = 0; _w18tDoneAt = -1f; _w18tDoneText = null; W25PartBegan(); // (client wave 25, w25tut: a part begins)
            try { w.Lesson.Build?.Invoke(); } catch (Exception e) { Debug.LogException(e); } // client wave 23 (w23lines): the lesson's parts for the controller and the menu's style of now
            try { w.Lesson.Enter?.Invoke(); w.Lesson.Phases[0].Enter?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            W18TGuide.Hide(); // (shown again by the next draw: in front of the eyes)
            Debug.Log($"[Tutorial] the way '{w.Key}': its lesson ({w.Lesson.Phases.Count} parts); casting gesture {_w22Gesture0} → {Comfort.CastGesture}, forearm bar {_w22Bar0} → {Comfort.QuickBarMode}");
        }

        /// <summary>A lesson ends: done (the way is tried: its column checked, the settings stay) or left by "Back" (no mark, the settings as
        /// they were before it). Back to the columns.</summary>
        private void W22LessonEnd(bool tried, string why, bool backToColumns = true)
        {
            var st = _w22Lesson; if (st == null) return;
            try { st.Leave?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            _w22Lesson = null; _w18tPhase = 0; _w18tDoneAt = -1f; _w18tDoneText = null;
            if (W13Reposition) W13SetReposition(false, "the lesson ended"); if (LokiInput.ResizeMode) W13SetResizeMode(false, "the lesson ended");
            if (tried)
            {
                if (!_w22Tried.Contains(st.Key)) _w22Tried.Add(st.Key);
                _w22LastTried = st.Key; _w22CastLog.Add(st.Key + ":tried");
            }
            else _w22CastLog.Add(st.Key + ":back");
            // client wave 25 (w25tut), items 5 and 9: done or left, the settings go back to what they were before the lesson (in the
            // tutorial: no casting way on, so the wand is hidden again); the way to start with is chosen after the casting ways
            // (LokiGame.W25Tut.cs). Wave 22 kept the way tried last on.
            if (_w22Gesture0 >= 0 && Comfort.CastGesture != _w22Gesture0) W13SetCastGesture(_w22Gesture0, tried ? "the lesson was done" : "the lesson was left", false);
            if (_w22Bar0 >= 0 && Comfort.QuickBarMode != _w22Bar0) W13SetBarMode(_w22Bar0, tried ? "the lesson was done" : "the lesson was left");
            if (Comfort.ElbowBarOpen != _w22Elbow0) { Comfort.ElbowBarOpen = _w22Elbow0; Comfort.Save(); }
            Debug.Log($"[Tutorial] the way '{st.Key}': {(tried ? "tried" : "left")} ({why}); casting gesture {Comfort.CastGesture}, forearm bar {Comfort.QuickBarMode}; tried: {(_w22Tried.Count > 0 ? string.Join(", ", _w22Tried) : "none")}");
            if (!backToColumns) return;
            W22ColumnsShow();
            if (tried && _w22Ways != null)
            {
                int i = _w22Ways.FindIndex(w => w.Key == st.Key); var rig = _boot.Rig;
                if (i >= 0 && rig != null) CheckMarkFx.Pop(rig.transform, () => rig.Head, _w22Columns.BadgeWorld(i)); // wave 20's check mark, on the way's column
                W25AfterTried(); // client wave 25 (w25tut), item 9: every way tried: the next page comes by itself (LokiGame.W25Tut.cs)
            }
        }

        /// <summary>The columns shown as they would be (a test: their texts are measured in both languages) or put away again. Never while the
        /// casting step itself runs.</summary>
        public void W22ColumnsPreview(bool show)
        {
            if (W18TutRunning && _w18tSteps[_w18tIndex].Key == "casting") return;
            if (show) { W22CastEnter(); W22ColumnsTick(0f); }
            else { W22ColumnsHide(); }
        }

        /// <summary>"Continue the tutorial" (offered once a way was tried): the step after the casting ways.</summary>
        private void W22Continue()
        {
            if (!W18TutRunning || _w22Lesson != null || _w22Tried.Count == 0 || _w18tSteps[_w18tIndex].Key != "casting") return;
            // client wave 25 (w25tut), item 9: on from here is the question which way to start with (LokiGame.W25Tut.cs: its answer goes on)
            Debug.Log($"[Tutorial] the casting ways: 'Continue the tutorial'; tried {string.Join(", ", _w22Tried)}: which way to start with");
            W25ChoiceShow(2, "'Continue the tutorial' on the columns");
        }

        /// <summary>The guide's middle button: on the first page of a tutorial shown again, straight to the casting ways.</summary>
        private void W22TutMiddle()
        {
            if (!W18TutRunning || !W22JumpOffered || _w18tSteps[_w18tIndex].Key != "welcome") return;
            int i = _w18tSteps.FindIndex(s => s.Key == "casting"); if (i < 0) return;
            _w18tLog.Add("welcome:to the casting ways");
            Debug.Log("[Tutorial] the first page's 'Casting ways': straight to the casting ways");
            W18TutGo(i);
        }
    }
}
