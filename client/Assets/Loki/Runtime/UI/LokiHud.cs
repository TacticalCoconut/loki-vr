using Loki.Game;
using Loki.VR;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Owns the VR panels and the controller buttons that open them:
    ///   left  X / A   (primary)   = a HOLD of <see cref="HoldSeconds"/>: resize mode on / off (client wave 13, w13view, the brief's
    ///                               eighteenth item; a short press: no job yet, pending design: <see cref="FreePress"/>)
    ///   left  Y / B (secondary)   = the menu (the ring, or the classic wrist menu) on/off (client wave 13, w13view, the tenth item:
    ///                               the menu took the minimap's button; the minimap is on the overhead menu)
    ///   (until client wave 12: left X / A = wrist menu, left Y / B = minimap)
    ///   right A       (primary)   = NPC dialog Next / Close / OK, otherwise sit / stand
    ///   right B       (secondary) = cancel an armed skill, else stop attacking (client wave 19, w19view41: no longer the chat panel)
    /// Works the same with Valve Index (A/B on both hands) and Quest Touch (X/Y left, A/B right) through OpenXR's
    /// generic primaryButton / secondaryButton; client wave 4 (w4vr): controllers without them get stand-ins (VR/LokiInput.cs,
    /// the Settings window's Controls page) and the button strip over the walking wrist (UI/QuickBar.cs).
    /// </summary>
    public sealed class LokiHud : MonoBehaviour
    {
        public DialogPanel Dialog { get; private set; }
        public WristMenu Wrist { get; private set; }
        public ChatPanel Chat { get; private set; }
        public Minimap Map { get; private set; }
        private RectTransform _armedPanel; private Text _armedText;
        /// <summary>Right B cancels an armed skill before it toggles the chat window.</summary>
        public System.Func<bool> CancelArmed;
        /// <summary>Right B next stops a continuous attack (true = there was one).</summary>
        public System.Func<bool> StopAttacking;
        private RectTransform _death; private Text _deathText;
        private DioramaRig _rig; private NpcDialog _npc; private Net.RoSession _s;
        private bool _sitting;

        public void Build(DioramaRig rig, NpcDialog npc, PlayerState ps, Inventory inv, QuestLog quests, Net.RoSession s, System.Func<string> charName,
            Progression prog, ViewController view, Minimap minimap, System.Action<Progression.SkillEntry> useSkill)
        {
            _rig = rig; _npc = npc; _s = s; Map = minimap;
            var parent = rig.transform;
            Dialog = DialogPanel.Create(parent, npc);
            Wrist = WristMenu.Create(parent, rig.LeftHand, rig.Head, ps, inv, quests, s, charName, prog, view, minimap, useSkill);
            Wrist.InitBoard(rig); // client wave 11 (w11menu): the new menu's ring and board (UI/WristMenu.Board.cs)
            Wrist.InitTree(); // client wave 22 (w22tree), point 64: the 3D skill tree (UI/WristMenu.Tree.cs)
            _armedPanel = UiKit.Panel("ArmedSkill", parent, new Vector2(560, 90), 0.00035f, true, PanelKind.ByName, HudLook.LabelLook); if (HudLook.LabelLook == PanelLook.SeeThrough) HudLook.Backing(_armedPanel); // client wave 21 (w21kit: a parchment card; w21tools: see-through by one constant, UI/HudLook.cs LabelLook: flagged)
            _armedText = UiKit.Label(_armedPanel, "", Vector2.zero, new Vector2(540, 84), 24, TextAnchor.MiddleCenter, HudLook.LabelLook == PanelLook.SeeThrough ? HudLook.Text : new Color(0.75f, 0.9f, 1f));
            Object.Destroy(_armedPanel.GetComponent<BoxCollider>()); // never blocks the laser
            _armedPanel.gameObject.SetActive(false);
            Chat = ChatPanel.Create(parent);
            npc.Changed += () => { if (npc.IsOpen && !_dialogWasOpen) Dialog.PlaceInFront(_rig.Head); _dialogWasOpen = npc.IsOpen; };

            _death = UiKit.Panel("Death", parent, new Vector2(700, 300), 0.0007f);
            _deathText = UiKit.Label(_death, Lang.T("You have been defeated."), new Vector2(0, 60), new Vector2(640, 80), 36, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.5f));
            UiKit.Button(_death, Lang.T("Return to save point"), new Vector2(0, -60), new Vector2(460, 80), () => { _s.Restart(0); HideDeath(); }, 30);
            _death.gameObject.SetActive(false);
            BuildNotes(parent);
            PlaceChat();
            // (client wave 19, w19view41, point 41: the stick's tap only recentres the table now; the chat is the elbow's chat button's alone)
        }
        private bool _dialogWasOpen;

        // ---- client wave 11 (w11menu): short messages where the player looks (the design's 2026-10-06 playtest: "I can't cast the steal
        // ability": the refusal's reason, "no way to get within range of it from here", was a toast INSIDE the wrist menu, seen only
        // with the menu open). A small panel without a collider (the laser and the gaze go through it), put once in front of the
        // eyes a little under the line of sight when a message comes (it does not follow the head: VR comfort), up to three lines,
        // each for its own time. No sound. A wrist-menu toast comes here when the menu is closed, a refusal / failure always (Notify).
        private RectTransform _notes; private Text _notesText;
        private readonly System.Collections.Generic.List<(string text, float until)> _noteLines = new System.Collections.Generic.List<(string, float)>();
        /// <summary>Messages shown in front of the player so far, and the last one (smoke, log).</summary>
        public int NotesShown { get; private set; }
        public string LastNote { get; private set; } = "";
        public bool NotesVisible => _notes != null && _notes.gameObject.activeSelf;
        public Transform NotesPanel => _notes;
        public const float NotesMetresPerPx = 0.00065f, NotesDistance = 0.8f, NotesBelowDeg = 14f;
        public const int NotesFont = 30;

        private void BuildNotes(Transform parent)
        {
            _notes = UiKit.Panel("Notes", parent, new Vector2(960, 150), NotesMetresPerPx, true, PanelKind.ByName, HudLook.NotesLook); if (HudLook.NotesLook == PanelLook.SeeThrough) HudLook.Backing(_notes); // client wave 21 (w21tools): see-through HUD (FLAG: UI/HudLook.cs NotesLook)
            Object.Destroy(_notes.GetComponent<BoxCollider>()); Object.Destroy(_notes.GetComponent<UiBlocker>()); // never in the laser's or the gaze's way
            _notesText = UiKit.Label(_notes, "", Vector2.zero, new Vector2(930, 140), NotesFont, TextAnchor.MiddleCenter, HudLook.NotesLook == PanelLook.SeeThrough ? HudLook.Heading : new Color(1f, 0.85f, 0.55f)); // (a card: w21kit's own colour, drawn readable on parchment)
            _notesText.resizeTextForBestFit = true; _notesText.resizeTextMinSize = 22; _notesText.resizeTextMaxSize = NotesFont;
            _notes.gameObject.SetActive(false);
            Wrist.ToastShown += (m, sec, front) => { if (front || !Wrist.Visible) ShowNote(m, sec); }; // (New style: a board's own line shows it while one is open)
        }

        /// <summary>A refusal / failure the player must see whether or not a menu is open: in front of the eyes, and on the wrist
        /// menu's toast line as before.</summary>
        public void Notify(string msg, float seconds = 4f) { if (Wrist != null) Wrist.Toast(msg, seconds, true); else ShowNote(msg, seconds); }

        // ---- client wave 18 (w18wand), point 18
        /// <summary>A notice taken back before its time (a placing hint whose placing ended): its line in front of the eyes goes at once (the
        /// panel with it when it was the last line), and the wrist menu's toast line when it still says it. Other lines stay.</summary>
        public void Dismiss(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            Wrist?.ClearToast(msg);
            if (_noteLines.RemoveAll(l => l.text == msg) == 0) return;
            if (_noteLines.Count == 0) { if (_notes != null) _notes.gameObject.SetActive(false); }
            else RefreshNotes();
            Debug.Log("[Note] taken back: " + UiKit.StripTags(msg));
        }
        /// <summary>The notice is in front of the eyes now (smoke, log).</summary>
        public bool NoteShowing(string msg) => NotesVisible && _noteLines.Exists(l => l.text == msg && l.until > Time.time);

        private void ShowNote(string msg, float seconds)
        {
            if (_notes == null || string.IsNullOrEmpty(msg) || _rig == null) return;
            float now = Time.time;
            _noteLines.RemoveAll(l => l.until <= now);
            int same = _noteLines.FindIndex(l => l.text == msg);
            if (same >= 0) _noteLines.RemoveAt(same); // said again: one line, its time renewed
            _noteLines.Add((msg, now + Mathf.Max(seconds, 3f)));
            while (_noteLines.Count > 3) _noteLines.RemoveAt(0);
            NotesShown++; LastNote = msg;
            Debug.Log("[Note] " + UiKit.StripTags(msg));
            var gaze = _rig.GazeRay; var head = _rig.Head.transform;
            bool away = !_notes.gameObject.activeSelf || Vector3.Angle(gaze.direction, _notes.position - gaze.origin) > 32f;
            if (away)
            {
                // once, where the player looks now: a little under the line of sight, facing the eyes
                var right = Vector3.Cross(Vector3.up, gaze.direction); if (right.sqrMagnitude < 1e-4f) right = head.right;
                var dir = Quaternion.AngleAxis(NotesBelowDeg, right.normalized) * gaze.direction;
                _notes.position = gaze.origin + dir * NotesDistance;
                UiKit.FaceHead(_notes, _rig.Head);
            }
            _notes.gameObject.SetActive(true);
            RefreshNotes();
        }

        private void RefreshNotes()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _noteLines.Count; i++) { if (i > 0) sb.Append('\n'); sb.Append(_noteLines[i].text); }
            _notesText.text = sb.ToString();
        }

        private void UpdateNotes()
        {
            if (_notes == null || !_notes.gameObject.activeSelf) return;
            float now = Time.time; int n = _noteLines.Count;
            _noteLines.RemoveAll(l => l.until <= now);
            if (_noteLines.Count == 0) { _notes.gameObject.SetActive(false); return; }
            if (_noteLines.Count != n) RefreshNotes();
        }

        public void PlaceChat() { if (Chat != null && _rig != null) Chat.PlaceInFront(_rig.Head); }

        public void ShowDeath()
        {
            _death.gameObject.SetActive(true);
            var head = _rig.Head; var f = head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            _death.position = head.transform.position + f * 0.6f; UiKit.FaceHead(_death, head);
        }
        public void HideDeath() { if (_death != null) _death.gameObject.SetActive(false); }
        public void SetSitting(bool s) { _sitting = s; Wrist?.SetSitting(s); }

        /// <summary>Shows the armed skill next to the right hand (null hides it).</summary>
        public void SetArmed(string text)
        {
            if (_armedPanel == null) return;
            _armedPanel.gameObject.SetActive(!string.IsNullOrEmpty(text));
            _armedText.text = text ?? "";
        }

        private void LateUpdate()
        {
            MenuNav.Tick(); // client wave 13 (w13items), item 11: a player's menu closed by another way ends the history
            UpdateNotes(); // client wave 11 (w11menu)
            if (_armedPanel != null && _armedPanel.gameObject.activeSelf && _rig != null)
            {
                _armedPanel.position = _rig.PointerHand.position + Vector3.up * 0.09f;
                UiKit.FaceHead(_armedPanel, _rig.Head);
            }
        }

        private void Update()
        {
            var input = _rig != null ? _rig.Input : null;
            if (input == null) return;
            var off = _rig.Off; var main = _rig.Main; // left-handed mode swaps them (Comfort.LeftHanded)
            UpdateFreeButton(off); // client wave 13 (w13view): X / A held = resize mode; a short press acted on at its release
            if (off.SecondaryDown) { if (RunMenuWanted != null && RunMenuWanted()) { _menuSince = Time.unscaledTime; _menuHoldFired = false; } else OffSecondary(); } // (w15hfx, point 19: in a Horde run the press is decided at its release)
            UpdateMenuHold(off);
            if (main.SecondaryDown) MainSecondary();
            if (main.PrimaryDown) MainPrimary();
        }

        /// <summary>Client wave 19 (w19view41), point 41 (design note, 2026-10-10: "The only way to open and close the chat should be by pressing a
        /// second button that rests on the player's left elbow as well"): the chat button beside the elbow button (LokiGame.W13View.cs
        /// W19ChatButton) hides the chat if it is shown, else shows it in front of you. Nothing else a player presses shows or hides it: not
        /// the walking hand's stick tap (it only recentres the table: w19small had it toggle the chat), not B (<see cref="MainSecondary"/>),
        /// not the menus' chat lines (LokiGame.cs PrefillChat). The game's own: the login screen hides it, entering the game shows it as the
        /// player last left it (<see cref="ChatWanted"/>), the tutorial closes it and gives it back (point 46).</summary>
        public void ToggleChat() => ToggleChat(true);
        /// <summary>Client wave 24 (w24), item 9: the same switch for the chat box's own close button and its open tab
        /// (<paramref name="place"/> false: the tab shows the chat where it stands, the tab being at its corner). Item 17: the
        /// choice is kept with the account's settings (<see cref="ChatWanted"/>).</summary>
        public void ToggleChat(bool place)
        {
            if (Chat == null) return;
            if (ChatRefused()) return; // (client wave 19, point 46: no chat during the tutorial)
            ChatToggles++;
            if (Chat.Visible) { Chat.SetVisible(false); KeepChatWanted(false); return; }
            Chat.SetVisible(true); KeepChatWanted(true); if (place) PlaceChat();
        }
        private static void KeepChatWanted(bool shown)
        {
            var s = Loki.World.UserSettings.Prefs; int v = shown ? 1 : 2;
            if (s.chatShown == v) return;
            s.chatShown = v; Loki.World.UserSettings.Save();
        }
        /// <summary>Client wave 19 (w19view41), point 41: the chat as the player last left it with the chat button (shown until they hide it;
        /// this launch only): entering the game shows it so (LokiGame.cs).</summary>
        /// Client wave 24 (w24), item 17 (design note, 2026-10-12: "The chat box should not be open by default. On the start of a new
        /// character, when they log in after the tutorial."): the choice is now KEPT (UserSettings chatShown, the account's) and a
        /// player who never chose starts with the chat HIDDEN (0 = never chosen; before wave 24 nothing was stored, so every
        /// player is "never chosen" once: FLAGGED).
        public bool ChatWanted => Loki.World.UserSettings.Prefs.chatShown == 1;
        /// <summary>Presses of the chat button that showed / hid the chat (smoke, log).</summary>
        public int ChatToggles { get; private set; }

        // ---- client wave 13 (w13view), the brief's eighteenth item (design note, 2026-10-07: "Press for sit and get up. Hold for resize mode.
        // Don't remove the button on the floating menu, however."): the walking hand's lower face button, freed by the tenth item. Held
        // HoldSeconds: resize mode on / off through the overhead menu's own toggle (FreeHold: LokiGame.W13SetResizeMode), with a short
        // rumble at that moment so the player lets go; let go sooner: a short press (FreePress: no job yet, the coordinator ~15:05:
        // "reconsidering the short press"). The press is decided at the release, so a hold never does the press's job.
        public const float HoldSeconds = 0.5f;
        /// <summary>A hold of the freed button reached <see cref="HoldSeconds"/> (LokiGame: resize mode on / off).</summary>
        public System.Action FreeHold;
        /// <summary>A short press of the freed button, at its release (null: no job yet: pending design).</summary>
        public System.Action FreePress;
        /// <summary>Short presses and holds of the freed button so far (smoke, log).</summary>
        public int FreeShortPresses { get; private set; }
        public int FreeHolds { get; private set; }
        private float _freeSince = -1f; private bool _freeFired;
        private void UpdateFreeButton(LokiInput.Hand off)
        {
            bool held = off.FreeHeld; float now = Time.unscaledTime;
            if (held)
            {
                if (_freeSince < 0f) { _freeSince = now; _freeFired = false; }
                else if (!_freeFired && now - _freeSince >= HoldSeconds)
                {
                    _freeFired = true; FreeHolds++;
                    Haptics.Pulse(Haptics.Kind.Ui, false, 0.35f, 0.06f); // the hold is reached: let go
                    Debug.Log($"[Hud] the freed button held {HoldSeconds:0.0} s");
                    try { FreeHold?.Invoke(); } catch (System.Exception e) { Debug.LogException(e); }
                }
            }
            else if (_freeSince >= 0f)
            {
                if (!_freeFired) OffPrimary(); // a short press, at its release
                _freeSince = -1f; _freeFired = false;
            }
        }

        // client wave 4 (w4vr): the four face-button actions as methods, so controllers without the buttons (Vive, the simple controller,
        // tracked hands) reach them through the button strip over the walking wrist (UI/QuickBar.cs ButtonStrip)
        /// <summary>X / A on the walking hand, a short press (client wave 13: the menu moved to Y / B; the press has no job yet: pending
        /// design note, <see cref="FreePress"/>).</summary>
        public void OffPrimary()
        {
            FreeShortPresses++;
            if (FreePress != null) FreePress(); else Debug.Log("[Hud] the freed button: a short press (no job yet)");
        }
        /// <summary>Y / B on the walking hand: the menu (client wave 13; the minimap until then).</summary>
        public void OffSecondary() => MenuButton();

        // ---- client wave 15 (w15hfx), point 19 of the design's wave 14 list: in a Horde run the menu button has two jobs. design note: "when I press the
        // button to open up the menu, it has its own Horde-specific menu ... You can still access those original menus by quick clicking and
        // holding the menu button". A SHORT press (let go before MenuHoldSeconds) opens the run's menu (RunMenuPress: LokiGame.W8Horde.cs);
        // HELD MenuHoldSeconds the usual menu opens at once (ring or wrist menu, MenuButton) with a short rumble, and the release does
        // nothing more. Outside a run nothing changes (the press acts at once). The strip's Menu (no hold) is a short press.
        /// <summary>Seconds the menu button is held in a Horde run for the usual menu (the freed button's hold time).</summary>
        public const float MenuHoldSeconds = HoldSeconds;
        /// <summary>True while a Horde run is shown (the press is then decided at its release).</summary>
        public System.Func<bool> RunMenuWanted;
        /// <summary>A short press in a run: the run's menu.</summary>
        public System.Action RunMenuPress;
        public int RunMenuPresses { get; private set; }
        public int RunMenuHolds { get; private set; }
        private float _menuSince = -1f; private bool _menuHoldFired;
        private void UpdateMenuHold(LokiInput.Hand off)
        {
            if (_menuSince < 0f) return;
            if (off.SecondaryHeld)
            {
                if (!_menuHoldFired && Time.unscaledTime - _menuSince >= MenuHoldSeconds)
                {
                    _menuHoldFired = true; RunMenuHolds++;
                    Haptics.Pulse(Haptics.Kind.Ui, false, 0.35f, 0.06f); // the hold is reached: the usual menu
                    Debug.Log($"[Hud] the menu button held {MenuHoldSeconds:0.0} s in a run: the usual menu");
                    MenuButton();
                }
                return;
            }
            bool shortPress = !_menuHoldFired; _menuSince = -1f; _menuHoldFired = false;
            if (shortPress) MenuPress();
        }
        /// <summary>A short press of the menu button (also the strip's Menu): in a Horde run the run's menu, else the usual menu.</summary>
        public void MenuPress()
        {
            if (RunMenuWanted != null && RunMenuWanted() && RunMenuPress != null) { RunMenuPresses++; RunMenuPress(); return; }
            MenuButton();
        }
        /// <summary>The menu button (Y / B on the walking hand, the strip's Menu): the ring, or the classic wrist menu. Client wave 14 (w14ui),
        /// point 29: asked first, true = it closed every menu the player had open, and the press does nothing else (LokiGame.W14Ui.cs).</summary>
        public void MenuButton() { if (MenuButtonFirst != null && MenuButtonFirst()) return; Wrist.Toggle(); }
        public System.Func<bool> MenuButtonFirst;
        /// <summary>Client wave 19 (w19world), point 46 (design note: "During the tutorial opening up the chat menu should not be possible"): true while
        /// nothing may open the chat (LokiGame.W18Tutorial.cs: the tutorial runs); ChatRefused says so to the player and counts it.</summary>
        public System.Func<bool> ChatLocked;
        public int ChatRefusals { get; private set; }
        public bool ChatRefused()
        {
            if (ChatLocked == null || !ChatLocked()) return false;
            ChatRefusals++; Notify(Lang.T("No chat during the tutorial."), 2f);
            return true;
        }
        /// <summary>B: cancel an armed skill, else stop a continuous attack (CZ_CANCEL_LOCKON). Client wave 19 (w19view41), point 41 (design note: "B
        /// should not toggle the chat"): until then B's last job was the chat window; now it has none there.</summary>
        public void MainSecondary()
        {
            if (CancelArmed != null && CancelArmed()) return;
            if (MenuNav.B()) return; // client wave 13 (w13items), item 11: on a menu opened from another, B closes it outright or goes back (Settings: "B and X")
            if (Wrist != null && Wrist.CloseNewMenu()) return; // client wave 11 (w11menu): B closes the new menu's ring / board
            StopAttacking?.Invoke();
        }
        /// <summary>Client wave 8 (w8game): asked first when A is pressed; true = it took the press (the Horde arena: a card, never sit / stand).</summary>
        public System.Func<bool> PrimaryFirst;
        /// <summary>A: NPC dialog Next / Close / OK, otherwise sit / stand.</summary>
        public void MainPrimary()
        {
            if (PrimaryFirst != null && PrimaryFirst()) return; // client wave 8 (w8game, LokiGame.W8Horde.cs)
            switch (_npc.State)
            {
                case NpcDialog.Mode.Next: _npc.PressNext(); break;
                case NpcDialog.Mode.Close: _npc.PressClose(); break;
                case NpcDialog.Mode.InputNumber: case NpcDialog.Mode.InputString: _npc.SubmitInput(); break;
                case NpcDialog.Mode.Closed: if (_sitting) _s.Stand(); else _s.Sit(); break;
            }
        }
    }
}
