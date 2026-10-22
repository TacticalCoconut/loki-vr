using System;
using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using Loki.VR;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 18 (w18tut), point 34 of the design's notes after wave 17 (design notes; design note: "the first time someone creates a
    /// character, when they log in, they don't immediately enter the world of Ragnarok. An interactive tutorial explaining the different
    /// methods of casting and some of the important UI elements, such as shaping to unlock UI panels, rotation, the menu that floats above,
    /// reposition mode, scaling and moving the map, the different view modes, and other such things"). The INTERACTIVE TUTORIAL: the
    /// player DOES each thing; each step waits until the game's own state says it was done (nothing is ticked by a button), with "Skip
    /// step" and "End tutorial" on the guide (UI/TutorialGuide.cs).
    ///   When     a NEW character's first login: the character list said base level 1, job level 1, no base or job EXP (what rAthena
    ///            writes for a new character: rathena sql-files/main.sql:215-218, char.cpp:1504-1509), and this account's settings do not
    ///            list it as having had the tutorial (World/UserSettings.cs tutorialChars, "Name#charId": per account with w18acct's
    ///            sync). Once per login (a map change does not ask again). Finished or ended, it is listed and never opens by itself
    ///            again; Settings → Controls → "Controls tutorial" shows it again for any character (it was the page tutorial's row).
    ///            Smokes: only -lokiSmokeOnly w18tut* (no other smoke's fresh character sees it).
    ///   Where    rAthena keeps a character only on a map: the char server drops a session that sends nothing for stall_time (60 s,
    ///            conf/packet_athena.conf:22, socket.cpp:1016), and what the gestures read (the hotkeys, the skills: ZC_SHORTCUT_KEY_LIST
    ///            ...) comes from the map server at map entry. So, with NO server change, the client enters the map as usual and HOLDS
    ///            the world while the tutorial runs: the character stands at its start point (renewal start_point iz_int*, 18 26:
    ///            conf/char_athena.conf:115, the Academy's inside: no monster spawns there, npc/re/jobs/novice/academy.txt), the steps
    ///            that need the world (the laser, the look, walking, the views, the map) use that real map, and the practice target is
    ///            a Poring only this client draws (<see cref="W18TDummyId"/>: Sprites/EntityManager.cs AddLocal): the look, the laser
    ///            and the trigger take it as a unit, its clicks are answered here and never sent (a skill aimed at it would be dropped
    ///            by the server: rathena unit.cpp:2200-2204, an unknown target returns 0). The check-in window (rAthena opens it at
    ///            login: pc.cpp:14796) and the first-time voice notice wait until the tutorial ends; the first-launch page tutorial
    ///            does not open on top of it (LokiGame.cs OnEnteredMap).
    ///   Steps    welcome; the laser; the look; acting on a target; laser walking; walking with the stick; turning; the views;
    ///            the overhead menu; Resize mode (on, the map moved, the map resized, ended by a shake); Reposition (on, the
    ///            minimap carried, ended); panels (held by the edge, moved, shaken: tilt free, tilted, shaken: upright); the menu
    ///            ring (opened, twisted or pointed, a group opened, closed); the Settings help (Settings opened, a setting's help
    ///            shown, closed); casting with the pad, the flick, the wand (taken, a choice, put back) and the forearm bar's elbow
    ///            button; the end. A step the player cannot do with their settings (Look select off, Laser walking off, tracked
    ///            hands without sticks) is passed with a line saying why. Each casting step sets that gesture for its step (the
    ///            same setting as the overhead menu's "Casting gesture"); the player's own gesture, forearm bar mode and minimap come
    ///            back when the tutorial ends.
    ///   Casting  the steps read the gestures' OWN state, so they follow what the gestures do (wave 18's w18flick and w18wand change them):
    ///            a slot chosen by any gesture (<see cref="W12Chosen"/>, LokiGame.W12Cast.cs W12Take: an empty slot too, a new character's
    ///            bar is empty), the wand in a hand (W12WandHand), a wand pull let go on one of the wheel's slices or cells (W12WheelRegion
    ///            against Game/WandWheelGeo.cs' own region numbers), the elbow button's touches and presses (LokiGame.W13View.cs).
    /// Hooks (marked "client wave 18 (w18tut)"): LokiGame.cs (InitW18Tut after the Tutorial, UpdateW18Tut in Update, W18TutOnEnteredMap and
    /// the page tutorial in OnEnteredMap, W18TutTargetClicked first in OnTargetClicked, the smoke's dispatch), LokiGame.W12Cast.cs (W12Chosen),
    /// LokiGame.W6Voice.cs (the voice notice waits), Sprites/EntityManager.cs (AddLocal / RemoveLocal), World/UserSettings.cs (tutorialChars).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The practice Poring's id: high above every id rAthena gives (accounts from 2000000, NPCs and monsters from 110000000:
        /// START_NPC_NUM) and only on this client.</summary>
        public const uint W18TDummyId = 0x7FFFFF01u;
        /// <summary>How long a done step stays green before the next one; the distance of the practice Poring from the character (cells).</summary>
        public const float W18TDonePause = 1.0f; public const int W18TDummyCells = 3;
        /// <summary>What a step needs before it counts: the map moved (m of the table), resized (a factor), the world turned (degrees),
        /// a panel moved (m), tilted (degrees from upright).</summary>
        public const float W18TMapMoved = 0.10f, W18TMapScaled = 1.15f, W18TTurnDeg = 20f, W18TPanelMoved = 0.08f, W18TPanelTilt = 20f;

        private sealed class W18TPhase
        {
            public Func<string> Body, How; public Func<bool> Done; public Action Enter;
            /// <summary>Client wave 23 (w23lines), item 10: the inputs this part's line names (LokiGame.W23Lines.cs W23Control is their table;
            /// null: none, the part is done with the look or the hand alone); every line the part can show when it has more than one
            /// (null: Body only).</summary>
            public W23In[] Uses; public Func<string>[] Alt;
            /// <summary>Client wave 25 (w25tut), LokiGame.W25Tut.cs: item 2, the green arrow over the practice Poring shows in this part
            /// until this says the player's laser or look reached the Poring (once per part; null: no arrow); item 4, the things this
            /// part's line asks the player to press, take or drag (a green outline on each one drawn now; null: none); item 5, the wand
            /// glows green; item 7, the line is about the menu above (the guide goes up beside it).</summary>
            public Func<bool> Poring; public Func<IEnumerable<Transform>> Marks; public bool Wand, AtMenu;
            /// <summary>Wave 28 (wcodex): the creature the green arrow stands over in this part (null: the practice Poring; the Codex step's
            /// practice Lunatic: LokiGame.WCodexTut.cs).</summary>
            public Func<Unit> ArrowOver;
        }
        private sealed class W18TStep
        {
            public string Key; public Func<string> Title; public List<W18TPhase> Phases = new List<W18TPhase>();
            public Action Enter, Leave;
            /// <summary>Non-null: the step cannot be done with the player's settings now; the text says why (it is passed).</summary>
            public Func<string> Unavailable;
            public Func<string> Left, Right;
            /// <summary>Client wave 20 (w20tut), point 40: the step's explanation in full (the guide's middle text).</summary>
            public Func<string> About;
            /// <summary>Client wave 20 (w20tut): once a frame while a part waits (before its test: the look step's filling ring, LokiGame.W20Tut.cs);
            /// where the check mark of a part done pops (null: along the look, UI/CheckMarkFx.cs); the green line when the step is done
            /// (null: "Done!").</summary>
            public Action<float> Tick;
            public Func<Vector3?> CheckAt;
            public Func<string> DoneText;
            /// <summary>Client wave 22 (w22clips, the casting ways): a third button on the guide between the two (null: none).</summary>
            public Func<string> Middle;
            /// <summary>Client wave 23 (w23lines), items 10 and 13: makes the step's parts anew for the controller in the hands, the view and
            /// the menu's style in force (null: the parts never change). Called when the step is made and again when it begins.</summary>
            public Action Build;
        }

        public TutorialGuide W18TGuide { get; private set; }
        private List<W18TStep> _w18tSteps; private int _w18tIndex = -1, _w18tPhase; private float _w18tDoneAt = -1f; private string _w18tDoneText;
        private bool _w18tAsked, _w18tStartPressed, _w18tCheckInLater, _w18tWasDrawing, _w18tMapOn;
        private int _w18tWandRegion = -1, _w18tWandEnds, _w18tWandEndRegion = -1, _w18tSeen;
        /// <summary>Client wave 23, item 11 (the design's option A): the Resize step in force was built for the Table view's free grab: it starts
        /// at the grips, with no "turn Resize mode on" and no "shake to stop" (set where the step's parts are built).</summary>
        private bool _w23ResizeFree;
        private int _w18tOwnGesture = -1, _w18tOwnBar = -1; private bool _w18tOwnElbow;
        private bool _w18tChatWas; // (client wave 19, point 46: the chat as it was when the tutorial started)
        private readonly bool[] _w18tPadWasOpen = new bool[2]; private readonly int[] _w18tPadRow = new int[2]; private int _w18tPadSlaps;
        /// <summary>Client wave 18 (w18flick)'s "Pad rows: Slap" (VR/Comfort.cs PadSlap; "Pad slap" until client wave 19): the pad step teaches the slap in Slap mode (client wave 18
        /// merge: a plain read now that w18flick is merged; it was read by name before). The slap itself is read from the pad's own state: its
        /// row changes while it is open (w18flick's slap: CastPad.Show of the new row).</summary>
        private static bool W18TPadSlapOn => Comfort.PadSlap;
        /// <summary>Pads switched to another row while open (a slap), counted from the pads' own state (smoke, log).</summary>
        public int W18TutPadSlaps => _w18tPadSlaps;
        private Unit _w18tDummy; private ListWindow _w18tPanel;
        private readonly List<string> _w18tLog = new List<string>();
        public bool W18TutRunning => _w18tIndex >= 0;
        public string W18TutStepKey => W18TutRunning ? _w18tSteps[_w18tIndex].Key : null;
        public int W18TutStepIndex => _w18tIndex;
        public int W18TutPhase => _w18tPhase;
        public int W18TutStepCount => _w18tSteps != null ? _w18tSteps.Count : 0;
        /// <summary>The step is done and shown green (the next one comes after <see cref="W18TDonePause"/>).</summary>
        public bool W18TutStepDone => _w18tDoneAt >= 0f;
        /// <summary>Tutorials started, finished (the last step's button), ended early; each step's outcome in order ("key:done" / "key:skipped" /
        /// "key:passed"); the practice Poring and its clicks; the tutorial's practice panel.</summary>
        public int W18TutStarts { get; private set; }
        public int W18TutFinishes { get; private set; }
        public int W18TutEnds { get; private set; }
        public IReadOnlyList<string> W18TutLog => _w18tLog;
        public Unit W18TDummy => _w18tDummy;
        public int W18TDummyClicks { get; private set; }
        /// <summary>The check-in window was put off until the end (smoke, log).</summary>
        public int W18TutCheckInWaits { get; private set; }
        public ListWindow W18TPanel => _w18tPanel;
        /// <summary>This character's key in the settings' list ("Name#charId").</summary>
        private static string W18TutKey(CharInfo c) => c == null ? "" : c.Name + "#" + c.CharId;
        public bool W18TutHad => Session != null && Session.SelectedChar != null && Loki.World.UserSettings.TutorialHad(W18TutKey(Session.SelectedChar));

        private void InitW18Tut(LokiBootstrap boot)
        {
            var rig = boot.Rig; if (rig == null) return;
            W18TGuide = TutorialGuide.Create(rig.transform, () => rig.Head);
            if (Hud != null) Hud.ChatLocked = () => W18TutRunning; // client wave 19 (w19world), point 46: no chat during the tutorial
            W18TGuide.LeftPressed = W18TutLeft; W18TGuide.RightPressed = W18TutRight; W18TGuide.MiddlePressed = W22TutMiddle; // (client wave 22: the welcome page's "Casting ways")
            InitW24Tut(); // client wave 24 (w24), item 14: the guide's pointer (LokiGame.W24Tut.cs)
            InitW25Tut(); // client wave 25 (w25tut), items 2, 4, 5, 7, 9: the green marks, the guide beside the menu above, the way to start with (LokiGame.W25Tut.cs)
            W18TGuide.Hold = () => W13PanelHeld; // (a held panel: the hands are busy; in Resize mode or Reposition it still comes back, it says how to end them)
            if (ComfortUi != null) ComfortUi.ShowTutorial = () => W18TutStart("Settings → Controls tutorial");
            if (View != null) View.Changed += m => _w18tSeen |= 1 << (int)m;
        }

        /// <summary>OnEnteredMap: a new character's first login opens the tutorial. True when it will (the page tutorial then stays shut).</summary>
        private bool W18TutOnEnteredMap(RoSession s)
        {
            if (_w18tAsked || W18TGuide == null) return false;
            _w18tAsked = true; // once per login: the scene starts over at the next one
            var ch = s.SelectedChar; if (ch == null) return false;
            bool fresh = ch.BaseLevel <= 1 && ch.JobLevel <= 1 && ch.BaseExp == 0 && ch.JobExp == 0;
            bool had = Loki.World.UserSettings.TutorialHad(W18TutKey(ch));
            Debug.Log($"[Tutorial] {ch.Name}: new character {fresh} (base {ch.BaseLevel} / job {ch.JobLevel}, EXP {ch.BaseExp} / {ch.JobExp}), had the tutorial {had}");
            if (!fresh || had) return false;
            _w21StartSoon = true; // (client wave 21, w21map: a tutorial copy of an earlier login is not given back meanwhile)
            StartCoroutine(W18TutStartSoon());
            return true;
        }

        private System.Collections.IEnumerator W18TutStartSoon()
        {
            float until = Time.time + 20f;
            while (Time.time < until && (Entities == null || Entities.Self == null || _boot.Map.Current == null)) yield return null;
            // client wave 21 (w21map), point 47: the tutorial's own map first (a private copy; LokiGame.W21Map.cs), asked for at once; a
            // server without one says so at once and the tutorial runs here, as before
            yield return W21TutPlace("a new character's first login", true);
            yield return new WaitForSeconds(1.5f); // the map drawn, the first packets handled
            _w21StartSoon = false;
            if (Session != null && Session.State == RoSession.Stage.InGame && Entities != null && Entities.Self != null && !W18TutRunning) W18TutBegin("a new character's first login");
        }

        /// <summary>Starts the tutorial (a new character's first login; Settings → Controls tutorial). Running: puts the guide back over the view.</summary>
        public void W18TutStart(string why)
        {
            if (W18TGuide == null) return;
            if (W18TutRunning) { W18TGuide.PlaceInFront(); return; }
            if (Session == null || Session.State != RoSession.Stage.InGame || Entities == null || Entities.Self == null) { Hud?.Notify(Lang.T("Enter the world first.")); return; }
            // client wave 21 (w21map), point 47: the tutorial's own map first (a private copy, when this server has one: LokiGame.W21Map.cs)
            if (_w21Placing) return;
            if (W21PlaceWanted) { StartCoroutine(W21PlaceThenStart(why)); return; }
            W18TutBegin(why);
        }

        /// <summary>The tutorial opens where the character stands now (client wave 21: its own map when the server gave one; else the map
        /// the character was on).</summary>
        private void W18TutBegin(string why)
        {
            if (W18TGuide == null || W18TutRunning) return;
            W20CalStandaloneEnd("the tutorial started"); // (client wave 20, w20tut: a calibration from Settings gives way to the tutorial, which calibrates first)
            _w18tOwnGesture = Comfort.CastGesture; _w18tOwnBar = Comfort.QuickBarMode; _w18tOwnElbow = Comfort.ElbowBarOpen; _w18tMapOn = false;
            // client wave 19 (w19world), point 46: the chat (and the whisper window) closed for the tutorial, given back at its end
            _w18tChatWas = Hud != null && Hud.Chat.Visible; if (_w18tChatWas) Hud.Chat.SetVisible(false);
            if (Whispers != null) Whispers.Locked = () => W18TutRunning;
            _w18tSteps = W18TBuildSteps(); _w18tLog.Clear(); _w18tStartPressed = false; _w18tCheckInLater = false;
            _w22Lesson = null; _w22LastTried = null; _w22Tried.Clear(); _w22CastLog.Clear(); W22ColumnsHide(); // (client wave 22: the casting ways)
            W25TutBegin(); // client wave 25 (w25tut), items 5 and 9: no casting way on until its lesson (the wand hidden); no way chosen yet (LokiGame.W25Tut.cs)
            _w18tIndex = -1; W18TutStarts++;
            Debug.Log($"[Tutorial] started ({why}): {_w18tSteps.Count} steps; casting gesture {_w18tOwnGesture}, forearm bar {_w18tOwnBar}");
            W18TGuide.Hide(); W18TutGo(0);
        }

        /// <summary>Into step <paramref name="i"/> (the end when past the last).</summary>
        private void W18TutGo(int i)
        {
            if (_w18tIndex >= 0 && _w18tIndex < _w18tSteps.Count) { try { _w18tSteps[_w18tIndex].Leave?.Invoke(); } catch (Exception e) { Debug.LogException(e); } }
            if (i >= _w18tSteps.Count) { W18TutFinish(true); return; }
            _w18tIndex = i; _w18tPhase = 0; _w18tDoneAt = -1f; _w18tDoneText = null;
            W25PartBegan(); // client wave 25 (w25tut): a part begins (its arrow, its lift: LokiGame.W25Tut.cs)
            var st = _w18tSteps[i];
            try { st.Build?.Invoke(); } catch (Exception e) { Debug.LogException(e); } // client wave 23 (w23lines): the step's parts for the controller, the view and the menu's style of now
            string not = st.Unavailable?.Invoke();
            if (not != null) { _w18tLog.Add(st.Key + ":passed"); Debug.Log($"[Tutorial] step {i + 1} '{st.Key}' passed: {not}"); _w18tDoneAt = Time.unscaledTime; _w18tDoneText = not; return; }
            try { st.Enter?.Invoke(); st.Phases[0].Enter?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"[Tutorial] step {i + 1} / {_w18tSteps.Count} '{st.Key}'");
        }

        private void W18TutLeft()
        {
            if (!W18TutRunning) { W20CalGuideLeft(); return; } // (client wave 20, w20tut: the look calibration from Settings uses the guide too)
            var st = _w18tSteps[_w18tIndex];
            if (st.Key == "welcome") { _w18tStartPressed = true; return; }
            if (_w22Lesson != null) { W22LessonEnd(_w18tDoneAt >= 0f, "Back"); return; } // client wave 22 (the casting ways): "Back" leaves a way's lesson for the columns
            if (_w18tDoneAt >= 0f) return;
            _w18tLog.Add(st.Key + ":skipped"); Debug.Log($"[Tutorial] step {_w18tIndex + 1} '{st.Key}' skipped at its part {_w18tPhase + 1}");
            W18TutGo(_w18tIndex + 1);
        }

        private void W18TutRight()
        {
            if (!W18TutRunning) { W20CalGuideRight(); return; } // (client wave 20, w20tut)
            bool last = _w18tIndex == _w18tSteps.Count - 1;
            if (last) { _w18tLog.Add(_w18tSteps[_w18tIndex].Key + ":done"); W18TutGo(_w18tSteps.Count); return; }
            Debug.Log($"[Tutorial] ended at step {_w18tIndex + 1} '{_w18tSteps[_w18tIndex].Key}'");
            W18TutFinish(false);
        }

        /// <summary>The end (the last step's button, or "End tutorial"): the player's own settings back, the practice things gone, the character
        /// listed as having had it, the check-in window if it waited.</summary>
        private void W18TutFinish(bool completed)
        {
            if (_w18tIndex >= 0 && _w18tIndex < _w18tSteps.Count && !completed) { try { _w18tSteps[_w18tIndex].Leave?.Invoke(); } catch (Exception e) { Debug.LogException(e); } }
            _w18tIndex = -1; _w18tDoneAt = -1f;
            if (completed) W18TutFinishes++; else W18TutEnds++;
            if (LokiInput.ResizeMode) W13SetResizeMode(false, "the tutorial ended");
            if (W13Reposition) W13SetReposition(false, "the tutorial ended");
            // client wave 25 (w25tut), item 9 (design note: "when the game opens, have that be their chosen casting method. And disable all the
            // other ones"): the way the player chose to start with is on (set when it was chosen: LokiGame.W25Tut.cs W25StartWith), every
            // other one off; with none chosen (the tutorial ended before) the player's own settings come back, as before. (This replaces
            // wave 22's "the casting stays as the way tried last left it": W22KeepLastTried.)
            if (_w25Chosen != null) Debug.Log($"[Tutorial] the casting starts as the player chose: '{_w25Chosen}': casting gesture {Comfort.CastGesture} (the player's own was {_w18tOwnGesture}), forearm bar {Comfort.QuickBarMode} (was {_w18tOwnBar})");
            else
            {
                if (_w18tOwnGesture >= 0 && Comfort.CastGesture != _w18tOwnGesture) W13SetCastGesture(_w18tOwnGesture, "the tutorial ended");
                if (_w18tOwnBar >= 0 && Comfort.QuickBarMode != _w18tOwnBar) W13SetBarMode(_w18tOwnBar, "the tutorial ended");
                if (Comfort.ElbowBarOpen != _w18tOwnElbow) { Comfort.ElbowBarOpen = _w18tOwnElbow; Comfort.Save(); }
            }
            if (_w18tMapOn) { _w18tMapOn = false; W13SetMinimap(false, "the tutorial ended"); }
            if (_w18tChatWas && Hud != null) { _w18tChatWas = false; Hud.Chat.SetVisible(true); Hud.PlaceChat(); } // (client wave 19, point 46)
            if (_w18tPanel != null) _w18tPanel.Hide();
            W18TDummyRemove();
            W22ColumnsHide(); // (client wave 22: the casting ways' columns)
            W25ChoiceHide(); // (client wave 25, item 9: the page after the casting ways)
            W18TGuide.Hide();
            var ch = Session != null ? Session.SelectedChar : null;
            if (ch != null) Loki.World.UserSettings.SetTutorialHad(W18TutKey(ch));
            Comfort.TutorialDone = true; Comfort.Save();
            Debug.Log($"[Tutorial] {(completed ? "finished" : "ended early")}: {string.Join(", ", _w18tLog)}; {(ch != null ? ch.Name : "?")} listed as having had it; casting ways tried: {(_w22Tried.Count > 0 ? string.Join(", ", _w22Tried) : "none")}");
            Hud?.Notify(Lang.T("Tutorial done. Settings → Controls replays it."), 5f);
            if (_w18tCheckInLater) { _w18tCheckInLater = false; CheckIn?.Show(); }
            W21TutLeave(completed ? "the tutorial finished" : "the tutorial ended early"); // client wave 21 (w21map), point 47: back where the character came from (a new character: its start point)
        }

        /// <summary>Once a frame (Update, after the casting): the step's state, the guide, the practice Poring.</summary>
        private void UpdateW18Tut()
        {
            if (!W18TutRunning) return;
            if (Session == null || Session.State != RoSession.Stage.InGame) return; // (leaving: the scene starts over and takes all of this with it)
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            if (CheckIn != null && CheckIn.Visible) { CheckIn.Hide(); _w18tCheckInLater = true; W18TutCheckInWaits++; Debug.Log("[Tutorial] the check-in window waits until the tutorial ends"); }
            // the wand: a pull let go, and where (read every frame: W12WheelRegion is the wheel's own region while the trigger is held)
            bool drawing = W12WandDrawing;
            if (drawing) _w18tWandRegion = W12WheelRegion;
            else if (_w18tWasDrawing) { _w18tWandEnds++; _w18tWandEndRegion = _w18tWandRegion; _w18tWandRegion = -1; }
            _w18tWasDrawing = drawing;
            // the pads: a row changed while a pad stays open is a slap (client wave 18, w18flick)
            for (int i = 0; i < 2; i++)
            {
                var pd = W12Pad(i == 0); bool open = W12Open(i == 0) && pd != null && pd.Shown;
                if (open && !_w18tPadWasOpen[i]) _w18tPadRow[i] = pd.Row;
                else if (open && pd.Row != _w18tPadRow[i]) { _w18tPadSlaps++; _w18tPadRow[i] = pd.Row; Debug.Log($"[Tutorial] a pad switched rows while open: a slap ({_w18tPadSlaps})"); }
                _w18tPadWasOpen[i] = open;
            }
            var st = _w18tSteps[_w18tIndex];
            // client wave 22 (w22clips, the casting ways): the casting step's page is the columns panel (LokiGame.W22Cast.cs); a way's lesson
            // runs exactly as a step does (its parts, its check marks, its clip), and its end goes back to the columns
            if (st.Key == "casting")
            {
                if (_w22Lesson == null) { if (W25ChoiceTick(dt)) return; W22ColumnsTick(dt); return; } // (client wave 25, item 9: the page after the casting ways, when it is up)
                st = _w22Lesson;
            }
            if (_w18tDoneAt >= 0f)
            {
                if (_w22Lesson != null) { if (now - _w18tDoneAt >= W18TDonePause) { W22LessonEnd(true, "done"); return; } }
                else if (now - _w18tDoneAt >= W18TDonePause) { if (_w18tLog.Count == 0 || !_w18tLog[_w18tLog.Count - 1].StartsWith(st.Key + ":")) _w18tLog.Add(st.Key + ":done"); W18TutGo(_w18tIndex + 1); if (!W18TutRunning) return; st = _w18tSteps[_w18tIndex]; if (st.Key == "casting" && _w22Lesson == null) return; } // (client wave 23, w23place, the design's answer 49: the casting step's page is the columns' panel; this frame's draw below put an empty guide "Casting" with "Skip step" in the middle of it, and nothing hid it again)
            }
            else
            {
                var ph = st.Phases[_w18tPhase];
                try { st.Tick?.Invoke(dt); } catch (Exception e) { Debug.LogException(e); } // (client wave 20, w20tut: the look step's filling ring)
                bool done = false; try { done = ph.Done(); } catch (Exception e) { Debug.LogException(e); }
                if (done)
                {
                    Debug.Log($"[Tutorial] step {_w18tIndex + 1} '{st.Key}' part {_w18tPhase + 1} / {st.Phases.Count} done");
                    W20TutCheck(st, _w18tPhase); // client wave 20 (w20tut), point 43: the check mark, its tick and its rumble (it replaces wave 18's rumble here)
                    if (_w18tPhase + 1 < st.Phases.Count) W18TGuide.ComeHome(); // client wave 24 (w24), item 14: a new action line is never given on a guide left behind
                    if (_w18tPhase + 1 < st.Phases.Count) { _w18tPhase++; W25PartBegan(); try { st.Phases[_w18tPhase].Enter?.Invoke(); } catch (Exception e) { Debug.LogException(e); } } // (client wave 25: W25PartBegan)
                    else { _w18tDoneAt = now; string dt2 = null; try { dt2 = st.DoneText?.Invoke(); } catch (Exception e) { Debug.LogException(e); } _w18tDoneText = dt2 ?? Lang.T("Done!"); }
                }
            }
            W18TutDraw(st);
            W18TGuide.Tick(dt);
        }

        private void W18TutDraw(W18TStep st)
        {
            bool done = _w18tDoneAt >= 0f;
            var ph = st.Phases[Mathf.Clamp(_w18tPhase, 0, st.Phases.Count - 1)];
            bool passed = done && _w18tLog.Count > 0 && _w18tLog[_w18tLog.Count - 1] == st.Key + ":passed"; // (client wave 20: a passed step asks for nothing)
            string body = passed ? "" : ph.Body != null ? ph.Body() : "";
            _w24TutLine = body ?? ""; // client wave 24 (w24), item 15: the menu above shows it too while the player looks up at it (LokiGame.W24Tut.cs)
            string status = done ? _w18tDoneText : W18TutStatus(st, ph);
            bool last = _w18tIndex == _w18tSteps.Count - 1;
            string left = st.Left != null ? st.Left() : last || done ? null : Lang.T("Skip step");
            string right = st.Right != null ? st.Right() : Lang.T("End tutorial");
            string about = st.About != null ? st.About() : ""; // (client wave 20, w20tut, point 40: the step's explanation)
            W18TGuide.MiddleLabel = st.Middle != null ? st.Middle() : null; // (client wave 22: the welcome page's "Casting ways" when the tutorial is shown again)
            W24GuideStep(st); // client wave 24 (w24), item 14: a step that begins brings the guide home (LokiGame.W24Tut.cs)
            W23GuideSide(st); // client wave 23 (w23place), the design's answers 47 and 50: beside what the player works on in this step, not behind it (LokiGame.W23Place.cs)
            W18TGuide.Show(_w18tIndex, _w18tSteps.Count, st.Title(), about, body, status, done, left, right);
            W23GuideLaserOnly(st.Right == null, st.Left == null && left != null); // client wave 23 (w23lines), item 5: "End tutorial" and "Skip step" are the laser's alone
        }

        /// <summary>The status line while waiting: what blocks the casting now (a casting step), else how on this controller.</summary>
        private string W18TutStatus(W18TStep st, W18TPhase ph)
        {
            if ((st.Key == "cast-pad" || st.Key == "cast-flick" || st.Key == "cast-wand" || st.Key == "cast-elbow") && !W13CastingAllowed) // (client wave 22: these four, as before; the wheels', the bubbles' and the Skills page's lessons open a menu themselves)
                return W13CastingBlocker == "resize mode" ? Lang.T("Turn Resize mode off first.") : W13CastingBlocker == "reposition" ? Lang.T("Turn Reposition off first.")
                     : W13CastingBlocker == "a panel held" ? Lang.T("Let go of the panel first.") : Lang.T("Close the open menu first.");
            return ph.How != null ? ph.How() : "";
        }

        /// <summary>The controller's own line for an action (VR/ControllerProfiles.cs: the controller in the hands; none known: the Index's,
        /// as the Settings page shows); null when it has none.</summary>
        private static string W18THow(string action)
        {
            var p = ComfortWindow.Active(); if (p == ControllerProfiles.Profile.None) p = ControllerProfiles.Profile.Index;
            foreach (var (a, h) in ControllerProfiles.Buttons(p, Comfort.LeftHanded)) if (a == action) return h;
            return null;
        }
        private bool W18THands => W23Hand; // (client wave 23, w23lines: the controller every line is written for: LokiGame.W23Lines.cs W23P)
        private string W18TShakeToStop => W18THands ? (Comfort.LeftHanded ? Lang.T("Shake your left hand to stop.") : Lang.T("Shake your right hand to stop."))
                                                  : (Comfort.LeftHanded ? Lang.T("Shake your left controller to stop.") : Lang.T("Shake your right controller to stop."));

        // ------------------------------------------------------------------ the practice Poring
        /// <summary>The practice Poring is there, near the character (made again after a map change or a long walk).</summary>
        private void W18TDummyEnsure()
        {
            // client wave 23 (the merge), GAME FIX of item 4: the look locks no unit farther than Targeting.MaxCells (18) from the character,
            // and the Poring now stands where the head faces, 20 to 35 degrees below eye level: 24 cells away on the tutorial's own map in
            // the Table view, so "Look at the Poring until its ring fills." could never be done there (w18tut B 4 with the tutorial's map)
            if (Target != null && Target.NoRangeLimit == null) Target.NoRangeLimit = x => x != null && x == _w18tDummy;
            var self = Entities != null ? Entities.Self : null; var gat = _boot.Map.Current != null ? _boot.Map.Current.Gat : null;
            if (self == null || gat == null) return;
            // client wave 23 (w23place), the design's answer 38 ("it is placed directly under the player ... It needs to be placed much further
            // ahead so that the player can very clearly see it"): the Poring stood on the cell W18TDummyCells from the character that was
            // NEAREST the eyes: 57° below eye level in the Follow view. Now: where the head faces when its step begins, 20 to 35° below
            // eye level straight ahead, on a walkable cell of the room drawn (LokiGame.W23Place.cs W23PoringCell: by its angles from the
            // eyes, the same rule in every view). It stays there while it is in that part of the view when a step begins; a long walk or
            // a map change places it anew, as before
            string stepNow = (_w22Lesson != null ? _w22Lesson.Key : W18TutStepKey) ?? ""; bool began = stepNow != _w23PoringStep; _w23PoringStep = stepNow;
            bool have = _w18tDummy != null && Entities.Get(W18TDummyId) == _w18tDummy && Vector2.Distance(_w18tDummy.Cell, self.Cell) <= W23PoringSearchCells + 12f;
            // client wave 24 (w24), item 5: on the tutorial's own map the Poring stands on the centre of the round floor in the middle
            // of the platform, from its first step to its last (LokiGame.W24Tut.cs TutorialDais); wave 23's by-angles place below is
            // for every other map
            if (W24DaisPoring(have, stepNow)) return;
            if (have && !(began && !_w18tDummy.Walking && !W23PoringInView(_w18tDummy))) return;
            if (!W23PoringCell(out int bx, out int by, out float down, out float bearing)) return;
            int sx = Mathf.RoundToInt(self.Cell.x), sy = Mathf.RoundToInt(self.Cell.y);
            Entities.RemoveLocal(W18TDummyId);
            _w18tDummy = Entities.AddLocal(W18TDummyId, 1002, 5, Lang.T("Practice Poring"), bx, by, 0); // (1002: the Poring; 5: a monster, as the server's own units)
            W23PoringPlacings++;
            Debug.Log($"[Tutorial] the practice Poring at {bx},{by} (the character at {sx},{sy}): {down:0.0}° below eye level, {bearing:+0.0;-0.0;0.0}° from where the head faces ({(down >= W23PoringDownMin && down <= W23PoringDownMax ? "in the band" : "the walkable cell nearest the band")}; step '{stepNow}', view {(View != null ? View.Mode.ToString() : "?")})");
        }

        private void W18TDummyRemove()
        {
            if (Entities != null) Entities.RemoveLocal(W18TDummyId);
            _w18tDummy = null;
        }

        /// <summary>LokiGame.cs OnTargetClicked, first: a click on the practice Poring is the tutorial's (never sent: the server has no such unit).</summary>
        private bool W18TutTargetClicked(Targetable t)
        {
            if (t == null || t.Unit == null || (t.Unit.Id != W18TDummyId && t.Unit.Id != WCodexTutLunaticId)) return false; // (wave 28, wcodex: the Codex step's practice Lunatic too)
            W18TDummyClicks++;
            var u = t.Unit;
            u.Overhead?.PopNumber(Lang.T("Hit!"), new Color(1f, 0.85f, 0.4f));
            Fx.Burst(_boot.Map.transform, u.transform.localPosition + Vector3.up * 0.5f, new Color(1f, 0.6f, 0.75f), 1.2f, 20);
            Haptics.Pulse(Haptics.Kind.Ui, true, 0.3f, 0.04f);
            Debug.Log($"[Tutorial] the practice Poring clicked ({W18TDummyClicks})");
            return true;
        }

        /// <summary>The tutorial's practice panel (a window like every other: taken by its edge, shaken).</summary>
        private void W18TPanelOpen()
        {
            var rig = _boot.Rig;
            if (_w18tPanel == null) _w18tPanel = ListWindow.Create(rig.transform, "TutorialPanel");
            _w18tPanel.OnCloseClicked = null; _w18tPanel.SetCloseVisible(false);
            _w18tPanel.SetTabs(null, 0, null); _w18tPanel.SetInput(false);
            _w18tPanel.SetTitle(Lang.T("Practice panel")); _w18tPanel.SetInfo("");
            _w18tPanel.SetRows(new List<ListWindow.Row> { new ListWindow.Row { Text = Lang.T("Hold me by my edge.") } }, true);
            _w18tPanel.SetFooter();
            _w18tPanel.Hide(); _w18tPanel.Open(rig.Head);
            // client wave 23 (w23place), the design's answer 47 ("gets occluded by the tutorial panel. So I can't even read that it says hold me by
            // my edge"): it stood 0.15 m right of the middle, its line inside the guide's box, and the guide was drawn over it. Now a
            // little to the pointing hand's side, the guide on the other side and drawn under every panel (LokiGame.W23Place.cs)
            W23PanelPlace(_w18tPanel.transform);
        }
        private Transform W18TPanelHeldBy => _w18tPanel == null ? null : W13Holding(0) == _w18tPanel.transform ? (Transform)_w18tPanel.transform : W13Holding(1) == _w18tPanel.transform ? _w18tPanel.transform : null;
        private bool W18TPanelHeld => W18TPanelHeldBy != null;

        // ------------------------------------------------------------------ the steps
        private static W18TPhase W18TP(Func<string> body, Func<bool> done, Func<string> how = null, Action enter = null) => new W18TPhase { Body = body, Done = done, How = how, Enter = enter };

        private List<W18TStep> W18TBuildSteps()
        {
            var rig = _boot.Rig;
            var steps = new List<W18TStep>();
            W18TStep S(string key, Func<string> title) { var s = new W18TStep { Key = key, Title = title }; steps.Add(s); return s; }
            bool OnDummy(Targetable t) => t != null && _w18tDummy != null && t.Unit == _w18tDummy;
            // client wave 20 (w20tut), point 40 (design note: "You can be very verbose on the tutorial ... require the user to be very informed about how
            // things work"): every step has its full explanation (About), written from the code of its system (the facts and their files:
            // design notes), and what to do now in plain words; the casting blockers and the controller's own lines as before

            // welcome
            var w = S("welcome", () => Lang.T("Welcome to Loki"));
            w.About = () => Lang.T("Loki is Ragnarok Online in VR. This tour takes a few minutes.");
            // client wave 23 (w23lines), items 10 and 13 (design note: "make sure that you are properly naming and identifying what buttons need to be
            // interacted with and in what way"; "you need to make that clear"): from here on every action line names the hand, the control by
            // its name on the controller in the player's hands and what to do with it (LokiGame.W23Lines.cs builds the lines from its table
            // W23Control; W23U notes which inputs a part's line names), one line per press; the controller's own line under it is gone
            // where the action line now says the same. A step whose parts depend on the controller, the view or the menu's style makes
            // them when it begins (Build)
            w.Phases.Add(W25Mark(W23U(W18TP(() => W23Press(Lang.T("Start")), () => _w18tStartPressed), W23In.MainTrigger), () => W25Of(W18TGuide != null ? W18TGuide.LeftButton : null))); // (client wave 25, w25tut, item 4: "Start" outlined green)
            w.Left = () => Lang.T("Start");
            w.Middle = () => W22JumpOffered ? Lang.T("Casting ways") : null; // client wave 22 (the casting ways): a tutorial shown again goes straight to them

            // client wave 20 (w20tut), point 65: the look calibration, before everything that uses the look (LokiGame.W20Tut.cs)
            W20CalStep(S("calibrate", () => Lang.T("Calibrate your look")));

            // the laser on a target
            var laser = S("laser", () => Lang.T("The laser"));
            laser.About = () => (Comfort.LeftHanded ? Lang.T("A laser comes out of your left hand. Point it at something to choose it.") : Lang.T("A laser comes out of your right hand. Point it at something to choose it."));
            laser.Enter = W18TDummyEnsure;
            laser.CheckAt = W20DummyTop;
            laser.Phases.Add(W25Arrow(W18TP(() => Lang.F("Point your {0} hand's laser at the Poring.", W23F(true)), () => { W18TDummyEnsure(); return OnDummy(rig.HoveredTarget); }), () => OnDummy(rig.HoveredTarget))); // (client wave 25, w25tut, item 2: the green arrow over the Poring until the laser reaches it)

            // the look: client wave 20 (w20tut), point 42: a ring round the Poring fills while it is looked at, then the Poring hops away; three times
            var look = S("look", () => Lang.T("Look select"));
            look.About = () => Lang.T("You can also choose things by looking at them. Keep looking until the ring fills.");
            look.Unavailable = () => LookSelect.On ? null : Lang.T("Look select is off. Skipped.");
            W20LookStep(look);

            // acting on a target
            int clicks0 = 0;
            var act = S("act", () => Lang.T("Act on it"));
            act.About = () => Lang.T("The trigger uses whatever you've chosen. On a monster, it attacks.");
            act.Enter = () => clicks0 = W18TDummyClicks;
            act.Leave = W18TDummyRemove; // (the walking steps: the look on it would take the trigger: LokiGame.cs OnFreeTrigger)
            act.CheckAt = W20DummyTop;
            act.Phases.Add(W25Arrow(W23U(W18TP(() => Lang.F("Point at the Poring and {0}.", W23Pull()), () => { W18TDummyEnsure(); return W18TDummyClicks > clicks0; }), W23In.MainTrigger), () => OnDummy(rig.HoveredTarget) || W25LookOnPoring())); // (client wave 25, item 2)

            // laser walking
            int trips0 = 0;
            var walk = S("walk", () => Lang.T("Laser walking"));
            walk.About = () => Lang.T("Point at the ground and pull the trigger to walk there.");
            walk.Unavailable = () => Comfort.LaserWalk ? null : Lang.T("Laser walking is off. Skipped.");
            walk.Enter = () => trips0 = W3WalkRoutes != null ? W3WalkRoutes.Trips : 0;
            walk.Phases.Add(W23U(W18TP(() => Lang.F("Point at the ground and {0}.", W23Pull()), () => W3WalkRoutes != null && W3WalkRoutes.Trips > trips0 && W3WalkRoutes.Tag == "cell"), W23In.MainTrigger));

            // the stick
            int steer0 = 0;
            var stick = S("stick", () => Lang.T("Walk with the stick"));
            stick.About = () => (Comfort.LeftHanded ? Lang.T("Walk with your right stick. Forward is wherever you're looking.") : Lang.T("Walk with your left stick. Forward is wherever you're looking."));
            stick.Unavailable = () => W23NoSticks ? Lang.T("No sticks. Skipped.") : null; // (client wave 23: the simple controller has none either: VR/LokiInput.cs RawStick)
            stick.Enter = () => steer0 = Target != null ? Target.Steer.Requests + Target.ClassicRequests : 0;
            stick.Phases.Add(W23U(W18TP(W23StickForward, () => Target != null && Target.Steer.Requests + Target.ClassicRequests > steer0), W23In.OffStick));

            // turning
            float yaw0 = 0f;
            var turn = S("turn", () => Lang.T("Turn"));
            turn.About = () => (Comfort.LeftHanded ? Lang.T("Your left stick turns the world around you.") : Lang.T("Your right stick turns the world around you."));
            turn.Unavailable = () => W23NoSticks ? Lang.T("No sticks. Skipped.") : null;
            turn.Enter = () => yaw0 = rig.Diorama.eulerAngles.y;
            turn.Phases.Add(W23U(W18TP(W23StickTurn, () => Mathf.Abs(Mathf.DeltaAngle(rig.Diorama.eulerAngles.y, yaw0)) >= W18TTurnDeg), W23In.MainStick));

            // the views (client wave 20, point 40: "On the tutorial on the views section, it is very badly written": what each view is, what you see,
            // when to use it, how to switch (VR/ViewController.cs, VR/DioramaRig.cs ViewHoldSeconds), what stays the same). The view shown at the
            // start counts as seen (a new player starts in Follow since client wave 19, point 55)
            var views = S("views", () => Lang.T("Views"));
            views.About = () => Lang.T("There are three views: Table, Follow and First person.");
            views.Enter = () => _w18tSeen = View != null ? 1 << (int)View.Mode : 0;
            // client wave 23 (w23lines), items 10 and 13 (design note: "you just say that hold the stick down ... It could mean holding the joystick
            // down. You don't say ... which one of the sides"): ONE part per change of view, each naming the control (the walking hand's
            // stick clicked in and held VR/DioramaRig.cs ViewHoldSeconds; a quick click only recentres) and the view that comes next
            // (VR/ViewController.cs Cycle: Table, Follow, First person, Table), from the view the step begins in until all three were
            // seen and the Table is back: 2 parts from Follow (a new player's), 3 from the Table, 4 from First person
            views.Build = () =>
            {
                views.Phases.Clear();
                var m = View != null ? View.Mode : ViewController.DefaultMode; int seen = 1 << (int)m;
                for (int k = 0; k < 4; k++)
                {
                    m = (ViewController.ViewMode)(((int)m + 1) % 3); seen |= 1 << (int)m; var target = m;
                    views.Phases.Add(W23U(W18TP(() => W23ViewLine(W23ViewName(target)), () => View != null && View.Mode == target), W23In.ViewButton));
                    if (seen == 7 && m == ViewController.ViewMode.Table) break;
                }
            };

            // the overhead menu (client wave 20, point 43: design note, "Nothing moves the part of the tutorial that says the menu above you on the moment
            // the player looks up": the check mark pops where the player looks, up there, the moment it wakes)
            int wakes0 = 0;
            var over = S("overhead", () => Lang.T("The menu above you"));
            over.About = () => Lang.T("There's a menu above you for quick switches.");
            over.Enter = () => wakes0 = W13Overhead != null ? W13Overhead.Wakes : 0;
            over.Phases.Add(W25AtMenu(W18TP(() => Lang.T("Look up until the menu above you appears."), () => W13Overhead != null && W13Overhead.Wakes > wakes0))); // (client wave 25, w25tut, item 7: the guide goes up beside the menu above)

            // Resize mode: the map moved, resized
            var resize = S("resize", () => Lang.T("Move and resize the map"));
            // client wave 23 (the merge), item 11, the design's answer 58 ("1) Option A"): in the Table view the grips move and resize the map
            // with no Resize mode (VR/LokiInput.cs ClaimForTable, LokiGame.W14Laser.cs W14TableNo), so a step that begins there says so in
            // one line, starts at the grips and has no "turn it on" and no "shake to stop" (no mode was switched on). In the other views,
            // and in the Table view with Resize mode already on, the step is as w23lines wrote it
            resize.About = () => _w23ResizeFree ? Lang.T("In the Table view, your grips move and resize the map without Resize mode.") : Lang.T("Resize mode lets you move and scale the map with your grips.");
            // client wave 23 (w23lines): the simple controller has no grip: passed. The first part's line is "look up" until the menu above
            // is there, then the press it waits for (the laser, or the look, on "On" in the row "Resize mode"). The map MOVES by the grip and
            // its stick in the Table view only (LokiGame.cs:192 PanEnabled) and RESIZES in every view but First person (ScaleEnabled): a
            // part the view the step begins in cannot do is not asked. (Item 11, design note: "explain that the resize mode is on by default on
            // the table view mode": NOT written: the mode is off in every view until it is switched on (VR/LokiInput.cs:62,
            // LokiGame.W13Resize.cs:117); what the Table view has is its free grab (VR/LokiInput.cs ClaimForTable): the facts are in
            // design notes, the sentence is the design's to give.)
            bool MenuAbove() => W13Overhead != null && W13Overhead.Usable;
            string LookUp() => Lang.T("Look up until the menu above you appears.");
            resize.Unavailable = () => W23NoGrip ? Lang.T("No grips. Skipped.") : null;
            resize.Build = () =>
            {
                resize.Phases.Clear();
                var mode = View != null ? View.Mode : ViewController.ViewMode.Table;
                _w23ResizeFree = mode == ViewController.ViewMode.Table && !LokiInput.ResizeMode; // (item 11, option A: the Table view's free grab)
                // client wave 25 (w25tut), item 4 (design note: "when I look up, the tutorial doesn't update that I have looked up"): the look
                // up is a part of its own, with its check mark, before the press (LokiGame.W25Tut.cs W25LookUpPart); item 7: the guide
                // goes up beside the menu above for both; item 4: "On" outlined green
                if (!_w23ResizeFree) resize.Phases.Add(W25LookUpPart(LookUp));
                if (!_w23ResizeFree) resize.Phases.Add(W25AtMenu(W25Mark(W23U(W18TP(() => W23PressBeside(Lang.T("~switch~On"), Lang.T("Resize mode")), () => LokiInput.ResizeMode), W23In.MainTrigger), W25MarkOverhead("switch:resize:1"))));
                // client wave 25 (w25tut), item 3 (design note: "the moment that I move two different parts of the tutorial are completed in
                // sequence without me being able to tell what I just completed on the second one"): both grips moved the map too (a
                // resize turns it about the hands' middle: VR/DioramaRig.cs), so one squeeze of both grips did the move part and then
                // the resize part. Now the move counts only what the line asks (one grip and its stick, a hand's fist: the rig's
                // PanningNow), and the resize only a squeeze of both grips that BEGINS in its own part (W25MoveDone, W25ResizeDone)
                if (mode == ViewController.ViewMode.Table)
                    resize.Phases.Add(W23U(W18TP(W23MoveMap, W25MoveDone, null, W25MoveEnter),
                        W23Hand ? new[] { W23In.OffGrip } : new[] { W23In.OffGrip, W23In.OffStick }));
                if (mode != ViewController.ViewMode.FirstPerson)
                    resize.Phases.Add(W23U(W18TP(W23ResizeMap, W25ResizeDone, null, W25ResizeEnter), W23In.BothGrips));
                if (!_w23ResizeFree) resize.Phases.Add(W18TP(() => W18TShakeToStop, () => !LokiInput.ResizeMode, () => (string)null));
                // client wave 24 (w24), item 6 (design note, 2026-10-12: "After the player has gone through all different view modes, and
                // they have resized the table mode, have them go back to follow mode on the tutorial."): the step's LAST part, by
                // the player's own action, in the views step's own words ("Click in your left stick and hold until the view is
                // Follow."); done when the view IS Follow (already Follow: passed at once). The steps after it run in the Follow
                // view. (A controller without grips skips this whole step and stays in the view the views step ended in.)
                // (client wave 25, w25tut, item 3: a step that begins in the Follow view has no such part: it was done the moment the part
                // before it was, a second check mark with nothing done for it)
                if (mode != ViewController.ViewMode.Follow)
                    resize.Phases.Add(W23U(W18TP(() => W23ViewLine(W23ViewName(ViewController.ViewMode.Follow)), () => View != null && View.Mode == ViewController.ViewMode.Follow), W23In.ViewButton));
            };

            // Reposition: the minimap carried
            int grabs0 = 0;
            var repo = S("reposition", () => Lang.T("Reposition"));
            repo.About = () => Lang.T("Reposition lets you grab parts of your HUD and move them.");
            repo.Enter = () => { if (MiniMap != null && !MiniMap.Visible) { _w18tMapOn = true; W13SetMinimap(true, "the tutorial's Reposition step"); } };
            repo.Unavailable = () => W23NoGrip ? Lang.T("No grips. Skipped.") : null; // (client wave 23: a thing is carried by a grip: LokiGame.W13View.cs)
            // (client wave 25, w25tut, items 4 and 7: the look up a part of its own with its check mark; the guide beside the menu above;
            // "On" and then the minimap outlined green)
            repo.Phases.Add(W25LookUpPart(LookUp));
            repo.Phases.Add(W25AtMenu(W25Mark(W23U(W18TP(() => W23PressBeside(Lang.T("~switch~On"), Lang.T("Reposition")), () => W13Reposition), W23In.MainTrigger), W25MarkOverhead("switch:reposition:1"))));
            repo.Phases.Add(W25Mark(W23U(W18TP(W23CarryMinimap, () => W13PlaceGrabs > grabs0 && W13Carried == null,
                () => (string)null, () => grabs0 = W13PlaceGrabs), W23In.MainGrip), () => W25Of(MiniMap != null && MiniMap.Visible && W13Carried == null ? MiniMap.transform : null)));
            repo.Phases.Add(W18TP(() => W18TShakeToStop, () => !W13Reposition, () => (string)null));
            repo.Leave = () => { if (W13Reposition) W13SetReposition(false, "the tutorial's next step"); if (_w18tMapOn) { _w18tMapOn = false; W13SetMinimap(false, "the tutorial's Reposition step ended"); } };

            // panels: held by the edge, moved, shaken (tilt free), tilted, shaken (upright)
            Vector3 panel0 = Vector3.zero;
            var panel = S("panel", () => Lang.T("Panels"));
            panel.About = () => Lang.T("Grab any window by its edge to move it. Shake it while holding to lock or free its tilt.");
            panel.Enter = W18TPanelOpen;
            // (client wave 23, w23lines: the edge is taken by the pointing hand's laser and its grip, the line's way; a hand at the edge and its
            // grip, or the trigger held with the laser on the edge, take it too: LokiGame.W13Grab.cs, VR/LokiInput.cs ClaimForTrigger: the
            // simple controller's only way)
            panel.Phases.Add(W25Mark(W23U(W18TP(W23GrabPanel, () => W18TPanelHeld, null,
                () => { if (_w18tPanel != null) panel0 = _w18tPanel.transform.position; }), W23NoGrip ? W23In.MainTrigger : W23In.MainGrip), W25MarkPracticePanel)); // (client wave 25, item 4: the practice panel outlined green until it is held)
            panel.Phases.Add(W18TP(() => Lang.T("Move it, then let go."), () => !W18TPanelHeld && _w18tPanel != null && (_w18tPanel.transform.position - panel0).magnitude >= W18TPanelMoved));
            panel.Phases.Add(W25Mark(W18TP(() => Lang.T("Grab it again and shake that hand."),
                () => _w18tPanel != null && W13Shaken(_w18tPanel.transform)), W25MarkPracticePanel));
            panel.Phases.Add(W18TP(() => Lang.T("Keep holding and roll your wrist to tilt it."), () => _w18tPanel != null && (!W13TiltFree(_w18tPanel.transform) || (W18TPanelHeld && Vector3.Angle(_w18tPanel.transform.up, Vector3.up) >= W18TPanelTilt))));
            panel.Phases.Add(W18TP(() => Lang.T("Shake that hand again to lock it upright."), () => _w18tPanel != null && !W13Shaken(_w18tPanel.transform), () => (string)null));
            panel.Leave = () => { if (_w18tPanel != null) _w18tPanel.Hide(); };

            // the menu: the ring (new style: twisted or pointed) or the wrist menu (classic)
            int twist0 = 0;
            var ring = S("menu", () => Lang.T("The menu"));
            // client wave 23 (w23lines), items 10 and 13: the style IN FORCE (UI/WristMenu.Board.cs NewStyle); a group "opened" is its board, or
            // the 3D skill trees the ring's Skills icon opens instead of a board (UI/WristMenu.Tree.cs: the step waited for a board for ever).
            // The About line no longer says "menu button": the Touch controllers' button of that name opens Settings, the ring's is Y / B
            bool NewStyle() => Hud != null && Hud.Wrist != null && Hud.Wrist.Ring != null && Hud.Wrist.NewStyle;
            bool RingOpen() => Hud.Wrist.Ring.Shown;
            bool BoardOpen() => Hud.Wrist.Board != null && Hud.Wrist.Board.Shown;
            bool PageOpen() => BoardOpen() || (Hud.Wrist.Tree != null && Hud.Wrist.Tree.Shown);
            ring.About = () => NewStyle()
                ? Lang.F("The menu ring opens over your {0} hand. Each icon opens a group of pages.", W23F(false))
                : Lang.F("The menu opens on your {0} wrist.", W23M(false));
            // one part per press: the menu button (the ring / the wrist menu is there); an icon marked the ring's own way (the walking
            // hand's stick toward it; "Menu ring: Twist": the wrist rolled two icons along; no stick: pointed at); that icon opened (the
            // stick let go, else the trigger); the menu button again (everything closed: LokiGame.W14Ui.cs W14CloseEverything)
            ring.Build = () =>
            {
                ring.Phases.Clear();
                if (!NewStyle())
                {
                    var tab0 = WristMenu.Tab.Status;
                    ring.Phases.Add(W23U(W18TP(W23OpenMenu, () => Hud.Wrist.ShownOnWrist), W23In.Menu));
                    ring.Phases.Add(W23U(W18TP(() => Lang.F("Point at any tab on it and {0}.", W23Pull()), () => Hud.Wrist.ShownOnWrist && Hud.Wrist.CurrentTab != tab0, null, () => tab0 = Hud.Wrist.CurrentTab), W23In.MainTrigger));
                    ring.Phases.Add(W23U(W18TP(W23CloseMenu, () => !Hud.Wrist.ShownOnWrist), W23In.Menu));
                    return;
                }
                bool Shut() => !RingOpen() && !PageOpen(); // (the ring closed again before an icon was opened: the menu button again)
                ring.Phases.Add(W23U(W18TP(W23OpenRing, () => RingOpen() || PageOpen()), W23In.Menu));
                switch (W23Ring)
                {
                    case W23RingHow.Twist:
                        ring.Phases.Add(W25Mark(W23Or(Shut, W23OpenRing, W18TP(() => Lang.F("Roll your {0} wrist to the {1} icon.", W23M(false), Lang.T("Character")), () => PageOpen() || (RingOpen() && ((Hud.Wrist.Ring.TwistSteps > twist0 && Hud.Wrist.Ring.Highlighted == 0) || Hud.Wrist.Ring.TwistSteps >= twist0 + 2)), null, () => twist0 = Hud.Wrist.Ring.TwistSteps)), W25MarkRingPick));
                        ring.Phases.Add(W25Mark(W23Or(Shut, W23OpenRing, W23U(W18TP(() => Lang.F("Then {0} to open the {1} menu.", W23Pull(), Lang.T("Character")), PageOpen), W23In.MainTrigger)), W25MarkRingPick)); // (client wave 25, item 4: the icon it opens outlined green)
                        break;
                    case W23RingHow.Point:
                        ring.Phases.Add(W25Mark(W23Or(Shut, W23OpenRing, W18TP(() => Lang.F("Point at the {0} icon on the ring.", Lang.T("Character")), () => PageOpen() || (RingOpen() && Hud.Wrist.Ring.Highlighted == 0))), W25MarkRingPick));
                        ring.Phases.Add(W25Mark(W23Or(Shut, W23OpenRing, W23U(W18TP(() => Lang.F("Then {0} to open the {1} menu.", W23Pull(), Lang.T("Character")), PageOpen), W23In.MainTrigger)), W25MarkRingPick));
                        break;
                    default:
                        ring.Phases.Add(W25Mark(W23Or(Shut, W23OpenRing, W23U(W18TP(W23StickToAnIcon, () => PageOpen() || (RingOpen() && Hud.Wrist.Ring.Highlighted == 0 && rig.OffStick.magnitude >= W23StickPushed)), W23In.OffStick)), W25MarkRingPick));
                        ring.Phases.Add(W25Mark(W23Or(Shut, W23OpenRing, W23U(W18TP(W23StickLetGo, PageOpen), W23In.OffStick)), W25MarkRingPick));
                        break;
                }
                ring.Phases.Add(W23U(W18TP(W23CloseMenu, () => !RingOpen() && !PageOpen()), W23In.Menu));
            };

            // wave 28 (wcodex), design note (2026-10-16): "add a tutorial section for the bestiary that unlocks the Poring and Lunatic as examples":
            // the scan ring on the practice Poring and a practice Lunatic, the Codex opened from the menu, closed (LokiGame.WCodexTut.cs)
            WCodexTutStep(S("codex", () => Lang.T("The Codex")));

            // the Settings help
            // client wave 23 (w23lines), item 13 (design note: "when it tells me to open the settings menu, it actually wants me to open the settings
            // menu inside of the settings menu. You need to make that clear. Because it might give the player the wrong impression that they
            // didn't do anything, or that they clicked on the wrong button"): ONE PART PER PRESS, each with its own check mark and told
            // apart by what the player sees. A controller with a Settings button (the Touch family's left menu button, either of the
            // Reverb G2's: VR/LokiInput.cs SettingsDown): that one press. Every other one: the menu button (the ring), the Settings ICON
            // on the ring (its board), the "Settings…" TAB on that board (the window). The classic menu: the menu button, the "View" tab,
            // its "Settings…" button. Then the help, then the window's own "Close"
            int helps0 = 0; float helpSince = -1f;
            var help = S("settings", () => Lang.T("Settings help"));
            help.About = () => Lang.T("Rest your laser on any setting to see what it does.");
            help.Build = () =>
            {
                help.Phases.Clear();
                bool Open() => ComfortUi != null && ComfortUi.Visible;
                if (W23Control(W23In.Settings, W23P, Comfort.LeftHanded).Exists) help.Phases.Add(W23U(W18TP(W23SettingsButton, Open), W23In.Settings));
                else if (NewStyle())
                {
                    bool OnBoard() => BoardOpen() && Hud.Wrist.BoardGroup == WristMenu.SettingsGroup;
                    string IconName() => Lang.T("Settings");
                    string TabLine() => Lang.F("On the {0} board, point at {1} and {2}.", Lang.T("Settings"), Lang.T("Settings…"), W23Pull());
                    string NextLine() => OnBoard() ? TabLine() : W23RingLine(IconName, WristMenu.SettingsGroup); // (always the next thing to do: a board closed again leads back through the ring)
                    int first = help.Phases.Count;
                    W23RingWay(help, IconName, WristMenu.SettingsGroup, () => OnBoard() || Open(), NextLine);
                    var tabPart = W23U(W18TP(NextLine, Open), W23In.MainTrigger);
                    var lines = new List<Func<string>>(W23RingLines(IconName)); lines.Add(TabLine); tabPart.Alt = lines.ToArray();
                    help.Phases.Add(tabPart);
                    // (client wave 25, w25tut, item 4: the Settings icon on the ring, then the "Settings…" tab on its board, outlined green)
                    for (int k = first; k < help.Phases.Count; k++) help.Phases[k].Marks = W25MarkRingTo(WristMenu.SettingsGroup, "Settings…");
                }
                else
                {
                    help.Phases.Add(W23U(W18TP(W23OpenMenu, () => Hud.Wrist.ShownOnWrist || Open()), W23In.Menu));
                    help.Phases.Add(W25Mark(W23Or(() => !Hud.Wrist.ShownOnWrist, W23OpenMenu, W23U(W18TP(() => Lang.F("Point at the {0} tab and {1}.", Lang.T("View"), W23Pull()), () => (Hud.Wrist.ShownOnWrist && Hud.Wrist.CurrentTab == WristMenu.Tab.View) || Open()), W23In.MainTrigger)), W25MarkWrist("btn:View"))); // (client wave 25, item 4)
                    help.Phases.Add(W25Mark(W23Or(() => !Hud.Wrist.ShownOnWrist, W23OpenMenu, W23U(W18TP(() => W23Press(Lang.T("Settings…")), Open), W23In.MainTrigger)), W25MarkWrist("btn:Settings…")));
                }
                // client wave 23 (the merge), item 13's rule (one check mark per thing done): the Settings window opens where the board's
                // "Settings…" tab was, so the laser that pressed the tab rests on a row at once and that row's help appears in the same
                // moment (the help has no delay since wave 18: UI/SettingHelp.cs Delay 0): this part's check popped together with the
                // press's, and its line was never read (w23text D: "2 parts done and 2 check marks for one press"). Now the part stays
                // W23HelpPartSeconds with a help in view before it is done; a help already shown when the part begins counts
                help.Phases.Add(W18TP(() => Lang.T("Rest your laser on a setting until its help appears."),
                    () => { bool shown = W15Help != null && (W15Help.Shows > helps0 || W15Help.ShowingRow >= 0); if (!shown) helpSince = -1f; else if (helpSince < 0f) helpSince = Time.unscaledTime; return shown && Time.unscaledTime - helpSince >= W23HelpPartSeconds; },
                    null, () => { helps0 = W15Help != null ? W15Help.Shows : 0; helpSince = -1f; }));
                help.Phases.Add(W25Mark(W23U(W18TP(() => Lang.F("On the Settings window, point at {0} and {1}.", Lang.T("Close"), W23Pull()), () => ComfortUi == null || !ComfortUi.Visible), W23In.MainTrigger), W25MarkSettingsClose)); // (client wave 24, w24, item 16: the line names the panel its button is on; client wave 25, item 4: Close outlined green)
            };

            // client wave 22 (w22clips, the casting ways; design note, 2026-10-11: "list out all of the currently existing casting methods and have
            // each one have their own explanation ... each one shows up as a column on a large panel ... if you click on that column, it moves
            // you to a following panel that has you test it out. And at the end, it brings you back to the multiple columns. But at that point,
            // you can actually also choose to progress"): ONE step, "casting", whose page is the columns panel (LokiGame.W22Cast.cs). The
            // ways' LESSONS are the steps below (the pad, the flick, the wand and the forearm bar as wave 18 / 20 wrote them; the skill
            // wheels, the Quick Use Bubbles and the Skills page new): not in the tutorial's own list, run from a column
            var lessons = new List<W18TStep>();
            W18TStep L(string key, Func<string> title) { var s = new W18TStep { Key = key, Title = title, Left = () => Lang.T("Back") }; lessons.Add(s); return s; }
            // casting: the pad, the flick, the wand, the forearm bar's elbow button (each reads the gesture's own state)
            int chosen0 = 0;
            var pad = L("cast-pad", () => Lang.T("Casting: the pad"));
            pad.About = () => W18TPadSlapOn
                ? Lang.T("Stick out your index finger and squeeze the grip. A pad of your quick slots opens.")
                : Lang.T("Stick out your index finger and squeeze the grip. A pad of your quick slots opens.");
            pad.Enter = () => { W18TDummyEnsure(); W13SetCastGesture(1, "the tutorial's pad step"); chosen0 = W12Chosen; };
            // (client wave 23, w23lines: the gesture's own start, named: the index finger off the trigger and that hand's grip, either hand
            // (VR/LokiInput.cs CastFrame); a tracked hand: its pointing pose, no control)
            pad.Phases.Add(W25Mark(W23U(W18TP(W23PadLine, () => W12Chosen > chosen0,
                () => Lang.T("Empty slots count too.")), W23Hand ? new W23In[0] : new[] { W23In.AnyGrip }), W25MarkPads)); // (client wave 25, item 4: the open pad, whose slot is touched, outlined green)
            // client wave 24 (w24), item 8 (design note, 2026-10-12: "the pad tutorial should also mention that the orientation of your palm
            // chooses what pad you are opening"). THE RULE (LokiGame.W18Flick.cs:82 W18FBaseRow, "Pad rows: Palm", the default): a pad
            // opens on the row its hand's palm picks at that moment: the pointing hand palm down row 1, palm up row 2; the other
            // hand palm down row 3, palm up row 4 (of the bar's shown page; up / down by the palm's angle: LokiGame.W12Cast.cs:56
            // PalmUpOn 80 / PalmUpOff 55 degrees). ONE more part: open the pad again with the palm turned the other way
            // (done only by an opening whose palm differs from the first one's: another row shows). With "Pad rows: Slap" the palm
            // picks nothing and the lesson asks for the slap, as before.
            int opens0 = 0; bool palm0 = false;
            // (the line names no control: the first part's line said how the pad opens; wave 23's rule: a part that names an input says its word)
            if (!W18TPadSlapOn) pad.Phases.Add(W25Mark(W18TP(() => palm0 ? Lang.T("Open the pad again with your palm down.") : Lang.T("Open the pad again with your palm up."),
                () => W24PadOpens > opens0 && W24PadPalmUp != palm0, () => Lang.T("Which way your palm faces picks the pad you open."),
                () => { opens0 = W24PadOpens; palm0 = W24PadPalmUp; }), W25MarkPads));
            int slaps0 = 0;
            if (W18TPadSlapOn) pad.Phases.Add(W18TP(() => Lang.T("Open it again and slap through it."), () => _w18tPadSlaps > slaps0,
                () => (string)null, () => slaps0 = _w18tPadSlaps));

            var flick = L("cast-flick", () => Lang.T("Casting: the flick"));
            flick.About = () => Lang.T("Your rows appear around your hand. Flick toward a row, then toward a slot.");
            flick.Enter = () => { W18TDummyEnsure(); W13SetCastGesture(2, "the tutorial's flick step"); chosen0 = W12Chosen; };
            flick.Phases.Add(W23U(W18TP(W23FlickLine, () => W12Chosen > chosen0,
                () => (string)null), W23Hand ? new W23In[0] : new[] { W23In.AnyGrip }));

            int ends0 = 0;
            var wand = L("cast-wand", () => Lang.T("Casting: the wand"));
            wand.About = () => Lang.T("The wand hangs at your hip. Its wheel holds your quick slots.");
            wand.Enter = () => { W18TDummyEnsure(); W13SetCastGesture(3, "the tutorial's wand step"); };
            // (client wave 25, w25tut, item 5, design note: "when it's time to use the wand to cast, make the wand also have a glowing green
            // outline too. Get attention to it": the wand glows green in all three parts: taken, used, put back)
            wand.Phases.Add(W25WandGlow(W23U(W18TP(W23WandTake, () => W12WandHand >= 0), W23In.AnyGrip)));
            wand.Phases.Add(W25WandGlow(W23U(W18TP(() => W23WandWheel((Comfort.WandQuick & 1) != 0),
                () => W12Chosen > chosen0 || (_w18tWandEnds > ends0 && (_w18tWandEndRegion >= WandWheelGeo.Cell || (_w18tWandEndRegion >= 0 && _w18tWandEndRegion < WandWheelGeo.Outer))),
                null, () => { chosen0 = W12Chosen; ends0 = _w18tWandEnds; }), W23In.AnyTrigger)));
            wand.Phases.Add(W25WandGlow(W23U(W18TP(W23WandBack, () => W12WandHand < 0), W23In.AnyGrip)));

            int elbow0 = 0, barPress0 = 0;
            var bar = L("cast-elbow", () => Lang.T("Casting: the forearm bar"));
            // (client wave 22: the chat button's sentence, which stood here, is on the last page now: this lesson is one of several ways and
            // not every player reads it. The bar starts hidden so the elbow button's touch SHOWS it; a second part: the laser on the bar)
            bar.About = () => Lang.T("Your quick slots sit along your arm. The button at your elbow shows and hides them.");
            bar.Enter = () => { W13SetBarMode(2, "the tutorial's forearm bar lesson"); if (Comfort.ElbowBarOpen) { Comfort.ElbowBarOpen = false; Comfort.Save(); } elbow0 = W13ElbowTouches + W13ElbowPresses; };
            // (client wave 25, w25tut, item 4: the elbow button, then the bar, outlined green)
            bar.Phases.Add(W25Mark(W18TP(() => Comfort.LeftHanded ? Lang.T("Touch the button at your right elbow with your left hand.") : Lang.T("Touch the button at your left elbow with your right hand."),
                () => W13ElbowTouches + W13ElbowPresses > elbow0 && Comfort.QuickBarShown, () => (string)null), () => W25Of(W13ElbowShown ? W13ElbowButton : null)));
            bar.Phases.Add(W25Mark(W18TP(() => Lang.F("Point your {0} hand's laser at the bar on your arm.", W23F(true)),
                () => Quick != null && (_boot.Rig.LaserPanel == Quick.transform || Quick.Presses > barPress0), () => (string)null, () => barPress0 = Quick != null ? Quick.Presses : 0), () => W25Of(Quick != null && Comfort.QuickBarShown ? Quick.transform : null)));
            W22BuildLessons(L); // the skill wheels, the Quick Use Bubbles, the Skills page (LokiGame.W22Cast.cs)
            _w22Lessons = lessons;
            var cast = S("casting", () => Lang.T("Casting"));
            cast.Phases.Add(W18TP(() => "", () => false));
            cast.Enter = W22CastEnter; cast.Leave = W22CastLeave;

            // the end
            var end = S("end", () => Lang.T("You're ready"));
            // (client wave 22: + the chat button's sentence, which the forearm bar's page had; + where the casting ways are found again)
            end.About = () => (Comfort.LeftHanded ? Lang.T("That's everything. To open or close the chat, touch the chat button by your right elbow.") : Lang.T("That's everything. To open or close the chat, touch the chat button by your left elbow."));
            end.Phases.Add(W25Mark(W23U(W18TP(() => W23Press(Lang.T("Enter the world")), () => false), W23In.MainTrigger), () => W25Of(W18TGuide != null ? W18TGuide.RightButton : null))); // (client wave 25, item 4: "Enter the world" outlined green)
            end.Right = () => Lang.T("Enter the world");
            // client wave 23 (w23lines): the parts of the steps and lessons that are made for the controller, the view and the menu's style
            // (made again when each begins: W18TutGo, LokiGame.W22Cast.cs W22Pick)
            foreach (var made in steps) made.Build?.Invoke();
            foreach (var made in lessons) made.Build?.Invoke();
            return steps;
        }
    }
}
