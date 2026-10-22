using System;
using System.Collections.Generic;
using Loki.VR;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// The Settings window (View tab; "Comfort &amp; controls" before client wave 4), five pages, every choice saved per Windows user in
    /// loki-user.json (VR/Comfort.cs, World/UserSettings.cs):
    ///   Comfort   turning, vignette, knockbacks, seated play, table height, the rain's lightning flashes (client wave 5, w5weather), camera shake and Hallucination's view (client wave 5, w5looks),
    ///             movement drawn smooth or classic (client wave 10, w10move: the last row)
    ///   Controls  the controllers found on each hand, left-handed mode, rumble (on / off, strength, buttons, low-HP heartbeat, a test),
    ///             the laser's pose, the button strip, the forearm quick bar, the tutorial, and the button table of every controller
    ///             Loki knows (VR/ControllerProfiles.cs: the one in your hands first, "Other controller" steps through the rest)
    ///   Audio     master, music and sound-effect volumes
    ///   Graphics  render scale, anti-aliasing, shadows, skill effects, Loki's own skill effects (client wave 5, w5fxown), name tag size
    ///   Headsets  this headset, friends' floating headsets, sharing mine (w3presence)
    ///   Voice     sharing my voice, the microphone, its gain, the noise gate and a live level meter, hearing friends (and on other
    ///             maps), the voice volume, each friend's own volume and mute (client wave 6, w6voice: Voice/VoiceChat.cs)
    ///   Language  English or Brazilian Portuguese (client wave 4b, w4lang: Lang/Lang.cs)
    ///   Horde     the co-op side mode: invitations shown, how the table follows the run character, the HUD's size (client wave 9,
    ///             h9flow: Horde/Client/HordeHud.cs SettingsRows; the older pages keep their row counts, which older smokes assert)
    /// Client wave 14 (w14menu), point 17 (design note: "subdivide the controls menu on the settings tab along with the comfort tabs so that it
    /// is easier to know where require to go to change specific settings"; the grouping, answer 13: "approved layout"):
    /// Comfort and Controls have SECTIONS, a second row of tabs (UI/ListWindow.cs SetSubTabs; <see cref="SectionsOf"/>):
    ///   Comfort   Turning &amp; view (turning, vignette, knockbacks, movement) / Look select (the look's aim, its ring, on / off) /
    ///             Table &amp; seating (seated play, table height) / Effects (lightning, camera shake, Hallucination's view)
    ///   Controls  Buttons &amp; laser (controllers, left-handed, the laser, the wrist buttons, the tutorial, the button table) / Casting
    ///             (the gesture, Cast immediately, the pad's size, the forearm bar) / Wand (holster, quick cast, aim, its wheel) / Menus
    ///             (the menu's style, the ring: point / twist, its shape, its icons, its rotation sensitivity; B and X; panel tilt)
    /// A page always opens on its first section (Toggle keeps the one shown). Point 9 (answer 7): Graphics has "Costume looks" (the character's
    /// costumes seen by everyone or not; it was a large button on the View page).
    /// </summary>
    public sealed class ComfortWindow
    {
        public enum Page { Comfort, Controls, Audio, Graphics, Headsets, Voice, Language, Horde, Rumble }
        private static readonly string[] PageNames = { Lang.N("Comfort"), Lang.N("Controls"), Lang.N("Audio"), Lang.N("Graphics"), Lang.N("Headsets"), Lang.N("Voice"), Lang.N("Language"), Lang.N("Horde"), Lang.N("Rumble") };
        private readonly ListWindow _win; private readonly Func<Camera> _head;
        public Action ShowTutorial;
        /// <summary>Client wave 20 (w20tut), point 65: Comfort → Look select "Recalibrate look" (LokiGame.W20Tut.cs W20Recalibrate).</summary>
        public Action Recalibrate;
        public Page Current { get; private set; }
        /// <summary>The controller whose buttons the Controls page lists (None = the one in your hands).</summary>
        public ControllerProfiles.Profile Browsing { get; private set; } = ControllerProfiles.Profile.None;
        public ComfortWindow(ListWindow win, Func<Camera> head) { _win = win; _head = head; _win.OnCloseClicked = Hide; Comfort.Changed += Refresh; }
        // ---- client wave 14 (w14menu), point 17: the sections of the Comfort and Controls pages
        private static readonly string[] ComfortSections = { Lang.N("Turning & view"), Lang.N("Look select"), Lang.N("Table & seating"), Lang.N("Effects") };
        private static readonly string[] ControlsSections = { Lang.N("Buttons & laser"), Lang.N("Casting"), Lang.N("Wand"), Lang.N("Menus") };
        /// <summary>A page's sections (their English names; null: the page has none).</summary>
        public static string[] SectionsOf(Page p) => p == Page.Comfort ? ComfortSections : p == Page.Controls ? ControlsSections : null;
        private readonly int[] _section = new int[16];
        /// <summary>The section of the shown page (0 on a page without sections).</summary>
        public int Section => _section[(int)Current];
        /// <summary>Shows section <paramref name="s"/> of the shown page (its sub-tab's press).</summary>
        public void ShowSection(int s)
        {
            var secs = SectionsOf(Current); if (secs == null) return;
            _section[(int)Current] = Mathf.Clamp(s, 0, secs.Length - 1); _animPanel = false; _reset = true; Refresh();
        }
        /// <summary>Client wave 14 (w14menu), point 9 (answer 7): the character's costumes seen by everyone (false) or hidden (the game's state,
        /// per character on the server: LokiGame.CostumesHidden / SetCostumesHidden); null: not in the game (the row is not shown).</summary>
        public static Func<bool?> CostumesHidden;
        public static Action<bool> SetCostumesHidden;
        public bool Visible => _win.Visible;
        /// <summary>Client wave 15 (w15menu), point 7: its window (UI/SettingHelp.cs stands beside it).</summary>
        public ListWindow Window => _win;
        public int RowCount => _win.RowCount;
        public string RowText(int i) => _win.RowText(i);
        /// <summary>Client wave 13 (w13resize): the first row of the shown page whose text (tags off) starts with this (-1: none). The
        /// smokes find a row by what it says: a row added above it does not move their checks.</summary>
        public int RowOf(string startsWith)
        {
            int at = RowOfShown(startsWith);
            if (at >= 0 || !_win.Visible) return at;
            // client wave 14 (w14menu), point 17: a row of another section of this page: that section is shown and the row found there
            return OtherSection(rows => rows.Exists(r => UiKit.StripTags(r.Text ?? "").StartsWith(startsWith, StringComparison.Ordinal))) ? RowOfShown(startsWith) : -1;
        }
        private int RowOfShown(string startsWith)
        {
            for (int i = 0; i < _win.RowCount; i++) if (UiKit.StripTags(_win.RowText(i) ?? "").StartsWith(startsWith, StringComparison.Ordinal)) return i;
            return -1;
        }
        /// <summary>Client wave 14 (w14menu), point 17: the first other section of the shown page whose rows pass <paramref name="has"/> is
        /// shown (true); none: false, nothing changes.</summary>
        private bool OtherSection(Predicate<List<ListWindow.Row>> has)
        {
            var secs = SectionsOf(Current); if (secs == null) return false;
            for (int sct = 0; sct < secs.Length; sct++)
            {
                if (sct == Section) continue;
                var rows = new List<ListWindow.Row>(); BuildRows(Current, sct, rows);
                if (has(rows)) { ShowSection(sct); return true; }
            }
            return false;
        }
        private static bool KeyIs(ListWindow.Row r, string englishName) => r != null && r.Key != null && (r.Key == englishName || Lang.Plain(r.Key) == englishName);
        /// <summary>Client wave 13, item 5 (w13ring): the row of the shown page made by UI/SettingRow.cs for the setting with this English
        /// name (in either language; -1: none), and a row's data.</summary>
        public int RowNamed(string englishName)
        {
            int at = RowNamedShown(englishName);
            if (at >= 0 || !_win.Visible) return at;
            // client wave 14 (w14menu), point 17: a setting of another section of this page: that section is shown and the row found there
            return OtherSection(rows => rows.Exists(r => KeyIs(r, englishName))) ? RowNamedShown(englishName) : -1;
        }
        private int RowNamedShown(string englishName)
        {
            for (int i = 0; i < _win.RowCount; i++) if (KeyIs(_win.RowAt(i), englishName)) return i;
            return -1;
        }
        public ListWindow.Row RowAt(int i) => _win.RowAt(i);
        /// <summary>Client wave 6 (w6voice): the Voice page's level meter, redrawn in place while that page is open (Voice/VoiceChat.cs MeterRow).</summary>
        public void SetRowText(int i, string text) => _win.SetRowText(i, text);
        /// <summary>Test hook: presses the A (true) or B button of row i as the laser would.</summary>
        public void Press(int i, bool a = true) => _win.Press(i, a);
        public void Show(Page page = Page.Comfort) { Current = page; _section[(int)page] = 0; _animPanel = false; _reset = true; _win.SetInput(false); _win.Open(_head()); Refresh(); } // (client wave 14: its first section; client wave 21: never the menu animations' list)
        private bool _reset = true;
        // ---- client wave 21 (w21anim), the design's point 63: the sub-panel of Controls → Menus → "Menu animations…": this window shows one
        // switch per menu (UI/MenuAnim.cs Menus: every menu the wrist opens, default On, kept with the account), All on / All off, and Back
        private bool _animPanel;
        /// <summary>The window shows the menu animations' switches now.</summary>
        public bool AnimPanelShown => _animPanel && _win.Visible;
        /// <summary>Turns the window into the menu animations' switches (true: "Menu animations…"'s button) or back into Controls → Menus.</summary>
        public void ShowAnimPanel(bool on)
        {
            if (on && !_win.Visible) Show(Page.Controls);
            if (on || _animPanel) { Current = Page.Controls; _section[(int)Page.Controls] = ControlsSections.Length - 1; }
            _animPanel = on; _reset = true; Refresh();
            if (!on && _win.Visible) { int at = RowNamedShown("Menu animations…"); if (at >= 0) _win.ShowRow(at); } // (back on the list's page that holds its row)
        }
        private void RefreshAnimPanel()
        {
            _win.SetTitle(Lang.T("Menu animations"));
            _win.SetSubTabs(null, 0, null);
            _win.SetInfo(Lang.T("Opening animation of each menu")); // (client wave 23, design note: no explaining how a menu opens; it said "Menus fly out of your wrist")
            _win.SetTabs(null, 0, null);
            var rows = new List<ListWindow.Row>();
            foreach (var m in MenuAnim.Menus)
            {
                string key = m.Key;
                rows.Add(SettingRow.Toggle(Lang.T(m.Name), null, () => MenuAnim.IsOn(key), v => { MenuAnim.SetOn(key, v); Refresh(); }));
            }
            _win.SetRows(rows, _reset); _reset = false;
            _win.SetFooter((Lang.T("Back"), () => ShowAnimPanel(false)), (Lang.T("All on"), () => { MenuAnim.SetAll(true); Refresh(); }), (Lang.T("All off"), () => { MenuAnim.SetAll(false); Refresh(); }));
        }
        public void Hide() => _win.Hide();
        public void Toggle() { if (Visible) Hide(); else { int sct = Section; Show(Current); if (sct != 0) ShowSection(sct); } }
        private static string Dim(string s) => $"<color=#9aa3b5>{s}</color>";
        private static void Set(Action a) { a(); Comfort.Save(); }

        /// <summary>The profile of the controller on each hand now (none on a desktop run).</summary>
        public static (ControllerProfiles.Profile left, ControllerProfiles.Profile right) InHands()
        {
            var input = LokiBootstrap.Instance != null && LokiBootstrap.Instance.Rig != null ? LokiBootstrap.Instance.Rig.Input : null;
            return input == null ? (ControllerProfiles.Profile.None, ControllerProfiles.Profile.None) : (input.Left.Profile, input.Right.Profile);
        }
        /// <summary>The profile the buttons are explained for: the pointer hand's controller, else the other hand's, else Index-style.</summary>
        public static ControllerProfiles.Profile Active()
        {
            var (l, r) = InHands();
            var main = Comfort.LeftHanded ? l : r; var off = Comfort.LeftHanded ? r : l;
            return main != ControllerProfiles.Profile.None ? main : off;
        }

        public void Refresh()
        {
            if (!_win.Visible) return;
            if (_animPanel) { RefreshAnimPanel(); return; } // client wave 21 (w21anim), point 63
            _win.SetTitle(Lang.T("Settings"));
            _win.SetInfo(Lang.T("Saved on this PC, for every character"));
            // client wave 14 (w14menu), point 17: the page's sections (a second row of tabs), set before the tabs (they move up for it)
            var secs = SectionsOf(Current);
            _win.SetSubTabs(secs != null ? Array.ConvertAll(secs, Lang.T) : null, Section, ShowSection);
            _win.SetTabs(TabNames(), (int)Current, t => { Current = (Page)t; _animPanel = false; _reset = true; Refresh(); });
            var rows = new List<ListWindow.Row>();
            BuildRows(Current, Section, rows);
            _win.SetRows(rows, _reset); _reset = false; // a new page starts at its top; a changed setting keeps the page
            _win.SetFooter((Lang.T("Close"), Hide));
        }

        /// <summary>The rows of section <paramref name="section"/> of page <paramref name="page"/> (client wave 14: Refresh's own list,
        /// made apart so the rows of every section can be looked through).</summary>
        private void BuildRows(Page page, int section, List<ListWindow.Row> rows)
        {
            bool lh = Comfort.LeftHanded;
            // client wave 13, item 5 (w13ring): every row is made by UI/SettingRow.cs: its control shows the CURRENT state and every
            // choice (the current one lit), never what a press would choose; item 17: short names, a short explanation only where needed
            switch (page)
            {
                case Page.Comfort:
                    switch (section)
                    {
                        case 0: // Turning & view
                            rows.Add(SettingRow.Choice(Lang.T("Turning"), lh ? Lang.T("Left stick") : Lang.T("Right stick"), new[] { Lang.T("~turn~Smooth"), Lang.T("~turn~Snap") }, () => Comfort.SnapTurn ? 1 : 0, i => Set(() => Comfort.SnapTurn = i == 1)));
                            // client wave 15 (w15menu), point 6 (design note: "make it so that I have more granularity"): every number is a slider in fine
                            // steps over the same range as its old list (VR/Comfort.cs Fine*: snap angle 30-90° in 5°, turn speed 45-180°/s in 5°/s)
                            rows.Add(Comfort.SnapTurn
                                ? SettingRow.Slider(Lang.T("Snap angle"), null, Comfort.SnapAngles[0], Comfort.SnapAngles[Comfort.SnapAngles.Length - 1], Comfort.FineSnapAngle, () => Comfort.SnapAngle, v => Comfort.SnapAngle = Mathf.RoundToInt(v), v => Mathf.RoundToInt(v) + "°", Comfort.Save)
                                : SettingRow.Slider(Lang.T("Turn speed"), null, Comfort.TurnSpeeds[0], Comfort.TurnSpeeds[Comfort.TurnSpeeds.Length - 1], Comfort.FineTurnSpeed, () => Comfort.TurnSpeed, v => Comfort.TurnSpeed = v, v => Lang.F("{0:0}°/s", v), Comfort.Save));
                            rows.Add(SettingRow.Toggle(Lang.T("Vignette"), Lang.T("Narrows the view as you move"), () => Comfort.Vignette, v => Set(() => Comfort.Vignette = v)));
                            rows.Add(SettingRow.Slider(Lang.T("Vignette strength"), null, 0.35f, 0.95f, Comfort.FinePercent, () => Comfort.VignetteStrength, v => Comfort.VignetteStrength = v, SettingRow.Percent, Comfort.Save));
                            // client wave 19 (w19world), point 48: the Follow view's character edge glow (VR/EdgeGlow.cs); its sliders while it is on
                            rows.Add(SettingRow.Toggle(Lang.T("Character edge glow"), Lang.T("Shows where your character is"), () => Comfort.EdgeGlow, v => Set(() => Comfort.EdgeGlow = v)));
                            if (Comfort.EdgeGlow)
                            {
                                rows.Add(SettingRow.Slider(Lang.T("Glow strength"), null, Comfort.EdgeGlowStrengthMin, Comfort.EdgeGlowStrengthMax, Comfort.FinePercent, () => Comfort.EdgeGlowStrength, v => Comfort.EdgeGlowStrength = v, SettingRow.Percent, Comfort.Save));
                                rows.Add(SettingRow.Slider(Lang.T("Glow sensitivity"), null, Comfort.EdgeGlowSensitivityMin, Comfort.EdgeGlowSensitivityMax, Comfort.FinePercent, () => Comfort.EdgeGlowSensitivity, v => Comfort.EdgeGlowSensitivity = v, SettingRow.Percent, Comfort.Save));
                            }
                            rows.Add(SettingRow.Choice(Lang.T("Knockbacks"), Lang.T("When you are pushed"), new[] { Lang.T("~knock~Slide"), Lang.T("~knock~Snap") }, () => Comfort.SnapKnockback ? 1 : 0, i => Set(() => Comfort.SnapKnockback = i == 1)));
                            // client wave 10 (w10move): Smooth (the default: smoothed paths for every unit, the stick steers in any direction) or Classic (cell to cell)
                            rows.Add(SettingRow.Choice(Lang.T("Movement"), Lang.T("Classic: cell by cell"), new[] { Lang.T("~move~Free"), Lang.T("~move~Classic") }, () => Comfort.SmoothMove ? 0 : 1, i => Set(() => Comfort.SmoothMove = i == 0))); // (client wave 26 merge, design note: "Smooth" renamed "Free"; the saved field smoothMove kept)
                            break;
                        case 1: // Look select
                            GazeRows(rows); // client wave 11 (w11menu): where "choose by looking" aims
                            break;
                        case 2: // Table & seating
                            rows.Add(SettingRow.Toggle(Lang.T("Seated play"), Lang.T("Table 25 cm lower"), () => Comfort.Seated, v => Set(() => Comfort.Seated = v)));
                            {
                                // what the slider shows is the table's height as used (seated play lowers it 25 cm)
                                float sd = Comfort.Seated ? 0.25f : 0f;
                                rows.Add(SettingRow.Slider(Lang.T("Table height"), null, Mathf.Max(0.4f, 0.5f - sd), 1.4f - sd, Comfort.FineTableHeight, () => Comfort.EffectiveTableHeight, v => Comfort.TableHeight = Mathf.Clamp(v + sd, 0.5f, 1.4f), v => Mathf.RoundToInt(v * 100) + " cm", Comfort.Save));
                            }
                            break;
                        default: // Effects
                            // client wave 5 (w5weather): the rain's lightning flashes, off by default (VR/Comfort.cs LightningFlashes)
                            rows.Add(SettingRow.Toggle(Lang.T("Lightning flashes"), Lang.T("In the rain"), () => Comfort.LightningFlashes, v => Set(() => Comfort.LightningFlashes = v)));
                            // client wave 5 (w5looks): the original client's camera quakes and Hallucination's flipped view, off unless chosen (Fx/CameraQuake.cs, Fx/ScreenFlip.cs)
                            rows.Add(SettingRow.Toggle(Lang.T("Camera shake"), Lang.T("From skills"), () => Comfort.CameraShake, v => Set(() => Comfort.CameraShake = v)));
                            rows.Add(SettingRow.Toggle(Lang.T("Hallucination view"), Lang.T("Turns the view upside down"), () => Comfort.HallucinationFlip, v => Set(() => Comfort.HallucinationFlip = v)));
                            // wave 28 (wcodex), design note (2026-10-16): "add a scan on/off toggle": the Codex's scan ring (LokiGame.WCodex.cs UpdateWCodexLook)
                            // beside the Codex's other switches (English only)
                            rows.Add(SettingRow.Toggle("Codex scan", "Looking at a creature writes it", () => !World.UserSettings.Prefs.codexScanOff, v => { World.UserSettings.Prefs.codexScanOff = !v; Comfort.Save(); }));
                            // wave 28 (wcodex): the Codex's flip-through to a chosen page (UI/CodexBook.cs Jump; English only: design note, 2026-10-15)
                            rows.Add(SettingRow.Toggle("Codex flip-through", "Pages riffle to the chosen page", () => !World.UserSettings.Prefs.codexFlipsOff, v => { World.UserSettings.Prefs.codexFlipsOff = !v; Comfort.Save(); }));
                            // (the book's size: hand-held, the default, or large; read when the book next opens: UI/CodexBook.cs Open)
                            rows.Add(SettingRow.Choice("Codex book", "Its size", new[] { "Hand-held", "Large" }, () => World.UserSettings.Prefs.codexLarge ? 1 : 0, i => { World.UserSettings.Prefs.codexLarge = i == 1; Comfort.Save(); }));
                            break;
                    }
                    break;
                case Page.Controls:
                    switch (section)
                    {
                        case 0: // Buttons & laser
                        {
                            var (l, r) = InHands();
                            rows.Add(SettingRow.Info(Lang.F("Controllers: left {0}, right {1}.", ControllerProfiles.NameOf(l), ControllerProfiles.NameOf(r)), true));
                            rows.Add(SettingRow.Toggle(Lang.T("Left-handed"), Lang.T("Laser on the left hand"), () => Comfort.LeftHanded, v => Set(() => Comfort.LeftHanded = v)));
                            rows.Add(SettingRow.Choice(Lang.T("Laser"), Lang.T("Hands need the pointing pose"), new[] { Lang.T("~laser~Pointing pose"), Lang.T("~laser~Along the controller") }, () => Comfort.LaserFromAim ? 0 : 1, i => Set(() => Comfort.LaserFromAim = i == 0)));
                            // client wave 14 (w14laser), point 20: the same stored value as the overhead menu's "Laser walking" (VR/Comfort.cs LaserWalk)
                            rows.Add(SettingRow.Choice(Lang.T("Laser walking"), null, new[] { Lang.T("~switch~Off"), Lang.T("~switch~On") }, () => Comfort.LaserWalk ? 1 : 0, i => Set(() => Comfort.LaserWalk = i == 1)));
                            rows.Add(SettingRow.Choice(lh ? Lang.T("Right wrist buttons") : Lang.T("Left wrist buttons"), Lang.T("Automatic: for tracked hands"), new[] { Lang.T("~strip~Automatic"), Lang.T("~strip~Always"), Lang.T("~strip~Never") }, () => Comfort.ButtonStrip, i => Set(() => Comfort.ButtonStrip = i)));
                            rows.Add(SettingRow.Button(Lang.T("Controls tutorial"), null, Lang.T("Show"), () => { Hide(); ShowTutorial?.Invoke(); }));
                            var shown = Browsing != ControllerProfiles.Profile.None ? Browsing : Active();
                            if (shown == ControllerProfiles.Profile.None) shown = ControllerProfiles.Profile.Index;
                            rows.Add(new ListWindow.Row { Text = Lang.F("<b>Buttons: {0}</b>", ControllerProfiles.NameOf(shown)), Highlight = true, A = Lang.T("Other controller"), OnA = () => { Browsing = NextProfile(shown); Refresh(); } });
                            foreach (var (action, how) in ControllerProfiles.Buttons(shown, Comfort.LeftHanded))
                                rows.Add(new ListWindow.Row { Text = $"{Lang.T(action)}: {Dim(how)}" }); // the action's English name is its key (ControllerProfiles: Lang.N)
                            break;
                        }
                        case 1: // Casting
                            // client wave 12 (w12cast): the casting gesture (off / pad / flick / wand) and "cast when taken", since client wave 14 (point 4,
                            // the design's answer 4: "Call that one "Cast immediately"") "Cast immediately": a chosen skill that needs no aiming is cast at once
                            rows.Add(SettingRow.Choice(Lang.T("Casting gesture"), CastHelp(Comfort.CastGesture), new[] { CastName(0), CastName(1), CastName(2), CastName(3) }, () => Comfort.CastGesture, i => Set(() => Comfort.CastGesture = i)));
                            rows.Add(SettingRow.Toggle(Lang.T("Cast immediately"), Lang.T("When no aim is needed"), () => Comfort.CastOnTake, v => Set(() => Comfort.CastOnTake = v)));
                            // client wave 14 (w14cast), point 5: how firmly the casting grip is squeezed (the Index's force: VR/LokiInput.cs CastGripOn; light by default)
                            rows.Add(SettingRow.Choice(Lang.T("Cast grip"), Lang.T("How hard to squeeze"), new[] { Lang.T("~grip~Light"), Lang.T("~grip~Medium"), Lang.T("~grip~Firm") }, () => Comfort.CastGrip, i => Set(() => Comfort.CastGrip = i)));
                            // client wave 13 (w13resize), the brief's fifteenth item: the casting pad opens around the hand; its cell size
                            rows.Add(SettingRow.Slider(Lang.T("Pad size"), Lang.T("One cell"), Comfort.PadCellMin, Comfort.PadCellMax, Comfort.PadCellStep, () => Comfort.PadCell, v => Comfort.PadCell = v, v => Lang.F("{0:0.0} cm", v * 100f), Comfort.Save));
                            // client wave 13 (w13view), item 13: always on / always off / elbow button, the same stored value as the overhead menu's
                            // "Forearm bar" (VR/Comfort.cs QuickBarMode)
                            rows.Add(SettingRow.Choice(Lang.T("Forearm quick bar"), Lang.T("9 slots on your forearm"), new[] { Lang.T("~bar~Always on"), Lang.T("~bar~Always off"), Lang.T("~bar~Elbow button") }, () => Comfort.QuickBarMode, i => Set(() => Comfort.QuickBarMode = i)));
                            // client wave 18 (w18flick), point 16 and the design's change ("Keep the slap as an alternate mode to swap. The default is still the palm
                            // method"): how a pad picks its row, Palm (the default) or Slap (LokiGame.W18Flick.cs), and in Slap mode only the push a slap needs;
                            // point 7: how long the flick's row middle follows the hand after the first stroke (0 by default: Game/FlickRecognizer.cs FollowSeconds)
                            rows.Add(SettingRow.Choice(Lang.T("Pad rows"), Lang.T("How the pad picks its row"), new[] { Lang.T("~padrows~Palm"), Lang.T("~padrows~Slap") }, () => Comfort.PadSlap ? 1 : 0, i => { Set(() => Comfort.PadSlap = i == 1); Refresh(); })); // (Refresh: "Slap strength" shown / hidden)
                            if (Comfort.PadSlap) rows.Add(SettingRow.Slider(Lang.T("Slap strength"), Lang.T("Push speed for a slap"), Comfort.SlapSpeedMin, Comfort.SlapSpeedMax, Comfort.SlapSpeedStep, () => Comfort.SlapSpeed, v => Comfort.SlapSpeed = v, v => Lang.F("{0:0.00} m/s", v), Comfort.Save));
                            rows.Add(SettingRow.Slider(Lang.T("Flick follow"), Lang.T("Row's middle follows the hand"), 0f, Comfort.FlickFollowMax, Comfort.FlickFollowStep, () => Comfort.FlickFollow, v => Comfort.FlickFollow = v, v => Lang.F("{0:0.00} s", v), Comfort.Save));
                            // client wave 18 (w18flick), point 32: where the flick's picture is drawn, as "Wand wheel shown" (at your hand: the default)
                            rows.Add(SettingRow.Choice(Lang.T("Flick shown"), null, new[] { Lang.T("~flick~In front of you"), Lang.T("~flick~At your hand") }, () => Comfort.FlickFront ? 0 : 1, i => Set(() => Comfort.FlickFront = i == 0)));
                            // client wave 21 (w21tools), the design's toggle: the casting tools (pad, flick, wand wheel, Quick Use Bubbles) with a light touch of
                            // THE PANEL THEME (the default) or as waves 12-20 drew them (UI/CastLook.cs); the row colours are the same in both
                            rows.Add(SettingRow.Choice(Lang.T("Casting look"), null, new[] { Lang.T("~castlook~Themed"), Lang.T("~castlook~Plain") }, () => Comfort.CastPlain ? 1 : 0, i => Set(() => Comfort.CastPlain = i == 1)));
                            // Item 43 (design note): slider for grip pressure needed to open the flick menu, and selector for which hand can open it
                            rows.Add(SettingRow.Slider(Lang.T("Flick grip pressure"), Lang.T("Squeeze needed to open flick"), Comfort.FlickGripThresholdMin, Comfort.FlickGripThresholdMax, Comfort.FinePercent, () => Comfort.FlickGripThreshold, v => Comfort.FlickGripThreshold = Mathf.Clamp(v, Comfort.FlickGripThresholdMin, Comfort.FlickGripThresholdMax), SettingRow.Percent, Comfort.Save));
                            rows.Add(SettingRow.Choice(Lang.T("Flick menu hand"), Lang.T("Which hand opens flick"), new[] { Lang.T("~flickhand~Dominant"), Lang.T("~flickhand~Off-hand"), Lang.T("~flickhand~Both") }, () => Mathf.Clamp(Comfort.FlickHandMode, 0, 2), i => Set(() => Comfort.FlickHandMode = i)));
                            rows.Add(SettingRow.Toggle(Lang.T("Aim spells with vision"), Lang.T("Cast where you look"), () => Comfort.VisionAimSpells, v => Set(() => Comfort.VisionAimSpells = v)));
                            break;
                        case 2: // Wand
                            rows.Add(SettingRow.Button(Lang.T("Wand holster"), Comfort.WandPlaced ? Lang.T("Where you put it") : Lang.T("On your hip"), Lang.T("Place…"), () => { Hide(); PlaceWand?.Invoke(); }, Lang.T("~wand~Reset"), () => Set(() => Comfort.WandPlaced = false), true, Comfort.WandPlaced));
                            rows.Add(SettingRow.Choice(Lang.T("Wand quick cast"), WandQuickHelp(Comfort.WandQuick), new[] { WandQuickName(0), WandQuickName(1), WandQuickName(2), WandQuickName(3) }, () => Comfort.WandQuick, i => Set(() => Comfort.WandQuick = i)));
                            rows.Add(SettingRow.Choice(Lang.T("Wand aim"), Lang.T("Look: cast where you look"), new[] { Lang.T("~aim~Wand"), Lang.T("~aim~Look") }, () => Comfort.WandAimLook ? 1 : 0, i => Set(() => Comfort.WandAimLook = i == 1)));
                            // client wave 13 (w13view), item 3: where the wand's wheel is drawn (in front of you: the default)
                            rows.Add(SettingRow.Choice(Lang.T("Wand wheel shown"), null, new[] { Lang.T("~wheel~In front of you"), Lang.T("~wheel~At the wand") }, () => Comfort.WandWheelFront ? 0 : 1, i => Set(() => Comfort.WandWheelFront = i == 0)));
                            // client wave 14 (w14cast): point 14 + answer 11, where a row's wheel of the wand opens (in place: the default); point 32, the wand's size
                            rows.Add(SettingRow.Choice(Lang.T("Wand rows"), Lang.T("Where a row's wheel opens"), new[] { Lang.T("~rows~In place"), Lang.T("~rows~Beside") }, () => Comfort.WandRows, i => Set(() => Comfort.WandRows = i)));
                            rows.Add(SettingRow.Slider(Lang.T("Wand size"), null, Comfort.WandMinScale, Comfort.WandMaxScale, Comfort.FinePercent, () => Comfort.WandScale, v => Comfort.WandScale = Mathf.Clamp(v, Comfort.WandMinScale, Comfort.WandMaxScale), SettingRow.Percent, Comfort.Save)); // (client wave 21, w21wand: the fingertip .. the default, 33 % - 100 %; the smallest is no whole step: kept exact)
                            rows.Add(SettingRow.Slider(Lang.T("Wheel size"), null, Comfort.PlaceMinScale, Comfort.PlaceMaxScale, Comfort.FinePercent, () => Comfort.WheelScale, v => Comfort.WheelScale = Mathf.Clamp(v, Comfort.PlaceMinScale, Comfort.PlaceMaxScale), SettingRow.Percent, Comfort.Save)); // client wave 23 (w23wand), the design's answer 55: the wand's wheel has its own size (the value Reposition's two grips on its picture always set: VR/Comfort.cs WheelScale)
                            // client wave 18 (w18wand), point 14: how many slices the wand's inner wheel has (2-8, default 4: VR/Comfort.cs WandSlices)
                            rows.Add(SettingRow.Slider(Lang.T("Wand slices"), Lang.T("The inner wheel"), Comfort.WandSlicesMin, Comfort.WandSlicesMax, 1f, () => Comfort.WandSlices, v => Comfort.WandSlices = Mathf.Clamp(Mathf.RoundToInt(v), Comfort.WandSlicesMin, Comfort.WandSlicesMax), v => Mathf.RoundToInt(v).ToString(), Comfort.Save));
                            break;
                        default: // Menus
                            // client wave 11 (w11menu), part 2: the menu's style: New (a ring over the hand, boards in front of you) or Classic (the wrist menu)
                            rows.Add(SettingRow.Choice(Lang.T("Menu"), Lang.T("New: ring. Classic: wrist."), new[] { Lang.T("~menu~New"), Lang.T("~menu~Classic") }, () => Comfort.MenuStyle == 0 ? 0 : 1, i => { WristMenu.ClearSessionStyle(); Set(() => Comfort.MenuStyle = i); }));
                            // the ring's control (specified, 2026-10-06): point (stick, laser, look) or twist (roll the hand that carries it)
                            rows.Add(SettingRow.Choice(Lang.T("Menu ring"), Lang.T("Twist: roll your wrist"), new[] { Lang.T("~ring~Point"), Lang.T("~ring~Twist") }, () => Comfort.RingTwist ? 1 : 0, i => Set(() => Comfort.RingTwist = i == 1)));
                            // client wave 14 (w14menu), answer 3 and point 3: twist's shape (the arch, the default, or the circle), its icons (still with a
                            // moving selector, the default chosen by w14menu, or sliding under a fixed marker), and the rotation sensitivity (wrist
                            // degrees per step, more sensitive to the right: VR/Comfort.cs RingTurnSteps)
                            rows.Add(SettingRow.Choice(Lang.T("Ring shape"), Lang.T("For twist"), new[] { Lang.T("~ringshape~Arch"), Lang.T("~ringshape~Circle") }, () => Comfort.RingArch ? 0 : 1, i => Set(() => Comfort.RingArch = i == 0)));
                            // client wave 15 (w15menu), point 4 (design note: "Do rename the Still/Slide options"): "Ring icons: Still / Slide" is "Wrist moves:
                            // Selector / Icons" (the same stored value, VR/Comfort.cs RingStill); point 1: "Ring direction: Normal / Reversed" (RingReversed,
                            // Reversed the default)
                            rows.Add(SettingRow.Choice(Lang.T("Wrist moves"), null, new[] { Lang.T("~wristmoves~Selector"), Lang.T("~wristmoves~Icons") }, () => Comfort.RingStill ? 0 : 1, i => Set(() => Comfort.RingStill = i == 0)));
                            rows.Add(SettingRow.Choice(Lang.T("Ring direction"), null, new[] { Lang.T("~ringdir~Normal"), Lang.T("~ringdir~Reversed") }, () => Comfort.RingReversed ? 1 : 0, i => Set(() => Comfort.RingReversed = i == 1)));
                            // (client wave 15, point 6: a slider over the wrist degrees per icon, 30° to 6° in 1° steps, more sensitive to the right; its
                            // value is the degrees, "10°" (wave 14: a level over 12 values, "8 (10°)"))
                            rows.Add(SettingRow.Slider(Lang.T("Rotation sensitivity"), Lang.T("Wrist turn per icon"), -Comfort.RingTwistStepMax, -Comfort.RingTwistStepMin, Comfort.FineRingTurn, () => -Comfort.RingTwistStep, v => Comfort.RingTwistStep = -v, v => RingTurnShown(-v), Comfort.Save));
                            // client wave 18 (w18menu), point 2: the twist ring's snap (On by default) and, while it is on, its strength and range (UI/MenuRing.cs SnapPicture / LeaveBorder since client wave 19, point 49)
                            rows.Add(SettingRow.Toggle(Lang.T("Ring snap"), Lang.T("Pulls the selector onto an icon"), () => Comfort.RingSnap, v => Set(() => Comfort.RingSnap = v)));
                            if (Comfort.RingSnap)
                            {
                                rows.Add(SettingRow.Slider(Lang.T("Snap strength"), null, Comfort.RingSnapStrengthMin, Comfort.RingSnapStrengthMax, Comfort.RingSnapStep, () => Comfort.RingSnapStrength, v => Comfort.RingSnapStrength = v, SettingRow.Percent, Comfort.Save));
                                rows.Add(SettingRow.Slider(Lang.T("Snap range"), Lang.T("Of the gap between icons"), Comfort.RingSnapRangeMin, Comfort.RingSnapRangeMax, Comfort.RingSnapStep, () => Comfort.RingSnapRange, v => Comfort.RingSnapRange = v, SettingRow.Percent, Comfort.Save));
                            }
                            // client wave 13 (w13items), item 11 (design note: "Toggleable, with the default being 'close the menu outright'"): what B and X do on a menu
                            // opened from another: close it (the default) or go back (the arrow does either way)
                            rows.Add(SettingRow.Choice(Lang.T("B and X"), Lang.T("On a menu opened from another"), new[] { Lang.T("~bx~Close"), Lang.T("~bx~Back") }, () => Comfort.MenuBack == 1 ? 1 : 0, i => Set(() => Comfort.MenuBack = i == 1 ? 1 : 0)));
                            // client wave 13 (w13grab), item 19: a held panel stays upright or follows the hand's tilt; the same stored value as the overhead
                            // menu's "Panel tilt" (VR/Comfort.cs PanelTiltFree; a shaken panel gets the other until it is closed: LokiGame.W13Grab.cs)
                            rows.Add(SettingRow.Choice(Lang.T("Panel tilt"), Lang.T("Shake a held panel to switch"), new[] { Lang.T("~tilt~Upright"), Lang.T("~tilt~Free") }, () => Comfort.PanelTiltFree ? 1 : 0, i => Set(() => Comfort.PanelTiltFree = i == 1)));
                            // client wave 15 (w15grab), point 8: how much a held panel is steadied (VR/Comfort.cs PanelSmoothing, 0 off to 100 %, default 50 %:
                            // LokiGame.W15Grab.cs)
                            // (client wave 18, w18menu, point 6: to 200 %, Comfort.PanelSmoothingMax)
                            rows.Add(SettingRow.Slider(Lang.T("Panel smoothing"), Lang.T("Steadies a held panel"), 0f, Comfort.PanelSmoothingMax, 0.05f, () => Comfort.PanelSmoothing, v => Comfort.PanelSmoothing = v, SettingRow.Percent, Comfort.Save));
                            // client wave 21 (w21anim), the design's point 63 ("On the settings menu, avoid this to crowd the options, so have a single
                            // button that opens up into its own sub-settings panel controlling the animation for each panel"): ONE row; its button turns
                            // this window into the list of every menu's switch (ShowAnimPanel)
                            // client wave 21 (w21inv), the design's point 62: the new inventory, or the old bag page (kept with the account: World/UserSettings.cs invOld)
                            rows.Add(SettingRow.Choice(Lang.T("Inventory"), Lang.T("New: curved, by type. Old: one grid."), new[] { Lang.T("~inv~New"), Lang.T("~inv~Old") }, () => WristMenu.InvNew ? 0 : 1, i => Set(() => WristMenu.SetInvOld(i == 1))));
                            rows.Add(SettingRow.Button(Lang.T("Menu animations…"), null, Lang.T("Open"), () => ShowAnimPanel(true)));
                            // client wave 21 (w21jobui; design note, 2026-10-11): the panels wear our job's variant of the theme, or the base look (UI/JobTheme.cs;
                            // the account's; the section's last row: no older row moves)
                            rows.Add(SettingRow.Choice(Lang.T("Panel theme"), null, new[] { Lang.T("~paneltheme~My job"), Lang.T("~paneltheme~Base") }, () => JobTheme.UseJob ? 0 : 1, i => Set(() => Comfort.PanelTheme = i == 1 ? 1 : 0)));
                            // client wave 22 (w22tree), the design's point 64 ("Keeping the current default flat display as a fallback"): the Skills page as
                            // the 3D skill tree in front of you (the default) or the flat page (VR/Comfort.cs SkillTree3D; the last row: no older row moves)
                            rows.Add(SettingRow.Choice(Lang.T("Skill tree"), null, new[] { Lang.T("~tree~3D"), Lang.T("~tree~Flat") }, () => Comfort.SkillTree3D ? 0 : 1, i => { WristMenu.ClearSessionTree(); Set(() => Comfort.SkillTree3D = i == 0); }));
                            // the trees of the character's jobs around the player (the default) or one at a time (VR/Comfort.cs SkillTreesAround)
                            rows.Add(SettingRow.Choice(Lang.T("Skill trees"), null, new[] { Lang.T("~trees~Around you"), Lang.T("~trees~One at a time") }, () => Comfort.SkillTreesAround ? 0 : 1, i => Set(() => Comfort.SkillTreesAround = i == 0)));
                            break;
                    }
                    break;
                case Page.Rumble:
                    RumbleRows(rows);
                    break;
                case Page.Audio:
                    rows.Add(SettingRow.Slider(Lang.T("All sound"), null, 0f, 1f, Comfort.FinePercent, () => Comfort.MasterVolume, v => Comfort.MasterVolume = v, SettingRow.Percent, Comfort.Save));
                    rows.Add(SettingRow.Slider(Lang.T("Music"), null, 0f, 1f, Comfort.FinePercent, () => Comfort.MusicVolume, v => Comfort.MusicVolume = v, SettingRow.Percent, Comfort.Save));
                    rows.Add(SettingRow.Slider(Lang.T("Sound effects"), null, 0f, 1f, Comfort.FinePercent, () => Comfort.EffectsVolume, v => Comfort.EffectsVolume = v, SettingRow.Percent, () => { Comfort.Save(); Audio.LokiAudio.Instance?.PlayUi(); }));
                    // client wave 23 (w23tree), item 24: the open skill trees muffle every other sound (Audio/LokiAudio.cs Muffled)
                    rows.Add(SettingRow.Toggle(Lang.T("Skill trees: muffle other sounds"), null, () => Comfort.SkillTreeMuffle, v => Set(() => Comfort.SkillTreeMuffle = v)));
                    break;
                case Page.Graphics:
                    rows.Add(SettingRow.Slider(Lang.T("Render scale"), Lang.T("Lower: faster. Higher: sharper."), Comfort.RenderScales[0], Comfort.RenderScales[Comfort.RenderScales.Length - 1], Comfort.FineScale, () => Comfort.RenderScale, v => Comfort.RenderScale = v, SettingRow.Percent, Comfort.Save));
                    rows.Add(SettingRow.Choice(Lang.T("Anti-aliasing"), Lang.T("Smoother edges, slower"), Array.ConvertAll(Comfort.MsaaLevels, MsaaName), () => Mathf.Max(0, Array.IndexOf(Comfort.MsaaLevels, Comfort.Msaa)), i => Set(() => Comfort.Msaa = Comfort.MsaaLevels[i])));
                    rows.Add(SettingRow.Choice(Lang.T("Shadows"), null, new[] { ShadowName(0), ShadowName(1), ShadowName(2) }, () => Comfort.Shadows, i => Set(() => Comfort.Shadows = i)));
                    rows.Add(SettingRow.Choice(Lang.T("Skill effects"), EffectsHelp(Comfort.EffectsLevel), new[] { EffectsName(0), EffectsName(1), EffectsName(2) }, () => Comfort.EffectsLevel, i => Set(() => Comfort.EffectsLevel = i)));
                    // client wave 5 (w5fxown): Loki's own effects for the skills the original data draws nothing for (Fx/OwnSkillFx.cs), saved in loki-user.json
                    rows.Add(SettingRow.Toggle(Lang.T("Loki's skill effects"), Lang.T("For skills with none"), () => Loki.Effects.OwnSkillFx.Enabled, v => Set(() => { Loki.Effects.OwnSkillFx.Enabled = v; Loki.World.UserSettings.Prefs.ownSkillEffects = v; })));
                    rows.Add(SettingRow.Slider(Lang.T("Name tags"), Lang.T("Names, HP bars, damage"), Comfort.NameScales[0], Comfort.NameScales[Comfort.NameScales.Length - 1], Comfort.FineScale, () => Comfort.NameScale, v => Comfort.NameScale = v, SettingRow.Percent, Comfort.Save));
                    // client wave 14 (w14menu), point 9 (design note: "that button is being given a lot of emphasis when it is a minor thing"; answer 7): the
                    // View page's "My costumes: shown / hidden" as a Graphics row. The state is the server's, per character (rAthena
                    // pc_set_costume_view): hidden, everyone sees your real headgear and garment instead of the costume ones. In the game only;
                    // the row shows the state the server last confirmed (a press asks; ZC_CONFIG's answer redraws the page: LokiGame.cs)
                    var hid = CostumesHidden?.Invoke();
                    if (hid.HasValue)
                        rows.Add(SettingRow.Choice(Lang.T("Costume looks"), Lang.T("Hidden: others see your real gear"), new[] { Lang.T("~costume~Shown"), Lang.T("~costume~Hidden") }, () => CostumesHidden?.Invoke() == true ? 1 : 0, i => SetCostumesHidden?.Invoke(i == 1)));
                    break;
                case Page.Voice:
                    Loki.Voice.VoiceChat.SettingsRows(rows, Set); // client wave 6 (w6voice): voice between friends (Voice/VoiceChat.cs)
                    // client wave 9 (h9feel): monsters' and other players' sounds (Audio/LokiAudio.cs MonsterSounds: a budget a second, nearest first). On this
                    // page because the Audio page's row count (3) is asserted by client wave 4's w4vr smoke; saved in loki-user.json (monsterSounds)
                    rows.Add(SettingRow.Choice(Lang.T("Monster sounds"), Lang.T("Monsters and other players"), new[] { MonsterSoundsName(0), MonsterSoundsName(1), MonsterSoundsName(2) }, () => Audio.LokiAudio.MonsterSounds, i => Set(() => Audio.LokiAudio.MonsterSounds = i)));
                    break;
                case Page.Language:
                    LanguageRows(rows); // client wave 4b (w4lang)
                    rows.Add(Game.JokeLines.SettingsRow(Set)); // client wave 5 (w5ui): Frost Joke / Scream's joke line (Game/JokeLines.cs)
                    break;
                case Page.Horde:
                    Loki.Horde.Client.HordeHud.SettingsRows(rows, Set); // client wave 9 (h9flow): the Horde side mode's three rows (invitations, table follow, HUD size)
                    break;
                default:
                {
                    string hmd = UnityEngine.XR.XRSettings.isDeviceActive ? UnityEngine.XR.XRSettings.loadedDeviceName : "";
                    rows.Add(SettingRow.Info(string.IsNullOrEmpty(hmd) ? Lang.T("This headset: none (desktop window).") : Lang.F("This headset: {0}.", hmd), true));
                    HeadsetAvatars.ComfortRows(rows, Set); // client wave 3: friends' floating headsets, sharing mine (VR/HeadsetAvatars.cs)
                    break;
                }
            }
        }

        /// <summary>Client wave 13, item 4 (w13ring): the Rumble page (design note: "separate the different rumble options. So that I can choose
        /// what triggers it and at what intensity"). The master switch and strength (wave 4's), then one row per kind a player can tell
        /// apart (VR/Comfort.cs Haptics.Named: its on / off and its own strength, 0-100 % in 10 % steps; a change plays that kind's own
        /// rumble once, as it now is), and "Defaults" (every kind on, 100 %: wave 12's rumble exactly).</summary>
        private void RumbleRows(List<ListWindow.Row> rows)
        {
            rows.Add(SettingRow.Toggle(Lang.T("Rumble"), null, () => Comfort.Haptics, v => Set(() => { Comfort.Haptics = v; if (v) Haptics.Both(Haptics.Kind.Test, 0.6f, 0.12f); })));
            rows.Add(SettingRow.Slider(Lang.T("Rumble strength"), Lang.T("For all kinds"), Comfort.HapticStrengths[0], Comfort.HapticStrengths[Comfort.HapticStrengths.Length - 1], Comfort.RumbleStep, () => Comfort.HapticStrength, v => Comfort.HapticStrength = v, SettingRow.Percent, () => { Comfort.Save(); Haptics.Both(Haptics.Kind.Test, 0.6f, 0.12f); }));
            foreach (var kind in Haptics.Named)
            {
                var k = kind; int i = (int)k; string help = Haptics.HelpOf(k);
                rows.Add(SettingRow.ToggleSlider(Lang.T(Haptics.NameOf(k)), help != null ? Lang.T(help) : null,
                    () => Comfort.RumbleOn[i], v => Set(() => { Comfort.RumbleOn[i] = v; if (v) Haptics.Sample(k); }),
                    0f, 1f, Comfort.RumbleStep, () => Comfort.RumbleStrength[i], v => Comfort.RumbleStrength[i] = v, SettingRow.Percent, () => { Comfort.Save(); Haptics.Sample(k); }));
            }
            rows.Add(SettingRow.Button(Lang.T("Rumble kinds"), Lang.T("All on, 100 %"), Lang.T("Defaults"), () => Set(Comfort.RumbleDefaults)));
        }

        /// <summary>Client wave 4b (w4lang): the Language page — English or Brazilian Portuguese, saved per Windows user
        /// (Lang.Set → loki-user.json); until one is chosen the game follows Windows' UI language. Each language is named in itself.
        /// Client wave 13, item 5: one row, both languages side by side, the one in use lit (pressing it while the game still follows
        /// Windows keeps it as your choice).</summary>
        private void LanguageRows(List<ListWindow.Row> rows)
        {
            if (Lang.EnglishOnly) return; // wave 28 (wtextc), design note: English only: no language choice (the page keeps the jokes switch, its tab reads "Chat")
            var codes = new[] { Lang.Pt, Lang.En };
            rows.Add(SettingRow.Choice(Lang.T("Language"), Lang.FromWindows ? Lang.T("Windows' language for now") : null, new[] { LanguageName(Lang.Pt), LanguageName(Lang.En) },
                () => Array.IndexOf(codes, Lang.Code), i => { Lang.Set(codes[i]); Refresh(); }, true));
            rows.Add(SettingRow.Info(Dim(Lang.T("Names and NPC text come from your Ragnarok Online data."))));
        }
        /// <summary>Client wave 11 (w11menu): the gaze calibration (specified after the 2026-10-06 playtest: "a slider to calibrate where eye-gaze selection lands"). Two sliders (UI/UiSlider.cs: the
        /// laser drags the knob, the stick or a gaze + trigger steps it, − / + step it too): sideways moves the gaze ray's START from
        /// the left eye through the middle to the right eye (so the correction holds at every distance), up / down turns the ray; then
        /// whether the ring where the ray lands is always shown (it is shown while this page is open either way: LokiGame sets
        /// DioramaRig.ShowGazeDot), and a button that puts both sliders back to the middle. One ray for the buttons and for the world's
        /// gaze lock (VR/DioramaRig.Ui.cs GazeRay).</summary>
        private void GazeRows(List<ListWindow.Row> rows)
        {
            string SideName(float side) => Mathf.Abs(side) < 0.001f ? Lang.T("Middle") : side <= -0.999f ? Lang.T("Left eye") : side >= 0.999f ? Lang.T("Right eye")
                : side < 0 ? Lang.F("{0} % left", Mathf.RoundToInt(-side * 100)) : Lang.F("{0} % right", Mathf.RoundToInt(side * 100));
            string UpName(float up) => Mathf.Abs(up) < 0.001f ? Lang.T("Straight") : up > 0 ? Lang.F("{0:0.#}° up", up) : Lang.F("{0:0.#}° down", -up);
            rows.Add(SettingRow.Slider(Lang.T("Look aim: sideways"), Lang.T("Put the ring where you look"), -1f, 1f, Comfort.GazeSideStep, () => Comfort.GazeSide, v => Comfort.GazeSide = v, SideName, Comfort.Save));
            rows.Add(SettingRow.Slider(Lang.T("Look aim: up / down"), null, -Comfort.GazeUpMax, Comfort.GazeUpMax, Comfort.GazeUpStep, () => Comfort.GazeUp, v => Comfort.GazeUp = v, UpName, Comfort.Save));
            // (client wave 15, w15menu, point 9: design note: "just put it like look ring and underneath it, show where you are looking")
            rows.Add(SettingRow.Choice(Lang.T("Look ring"), Lang.T("Show where you are looking"), new[] { Lang.T("~dot~This page"), Lang.T("~dot~Always") }, () => Comfort.GazeDot ? 1 : 0, i => Set(() => Comfort.GazeDot = i == 1)));
            rows.Add(SettingRow.Button(Lang.T("Look aim"), Lang.T("Back to the middle"), Lang.T("~gaze~Reset"), () => Set(() => { Comfort.GazeSide = 0f; Comfort.GazeUp = 0f; })));
            // client wave 20 (w20tut), point 65: the look calibration again (UI/LookCalibrator.cs; this PC's: VR/LookCalibration.cs). Settings closes, the dots
            // appear in front of you, the tutorial's guide says what to do (LokiGame.W20Tut.cs W20Recalibrate)
            rows.Add(SettingRow.Button(Lang.T("Recalibrate look"), LookCalibration.Done ? Lang.T("Calibrated on this PC") : Lang.T("Not calibrated on this PC"), Lang.T("Start"), () => { Hide(); Recalibrate?.Invoke(); }));
            // client wave 13 (w13look), item 23: the overhead menu's "Look select" (the same stored value: UI/LookSelect.cs; it saves and redraws)
            rows.Add(SettingRow.Toggle(Lang.T("Look select"), Lang.T("Choose by looking"), () => LookSelect.On, v => LookSelect.SetOn(v, "Settings")));
        }
        /// <summary>Client wave 11 (w11menu): the slider drawn for a row of the page shown (smoke).</summary>
        public UiSlider SliderOf(int row) => _win.SliderOf(row);
        public void ShowRow(int row) => _win.ShowRow(row);

        /// <summary>The page tabs in the current language. Wave 28 (wtextc): while English only, the Language page has no language row
        /// left (only the Bard and Dancer jokes switch): its tab reads "Chat".</summary>
        public static string[] TabNames()
        {
            var t = Array.ConvertAll(PageNames, Lang.T);
            if (Lang.EnglishOnly) t[(int)Page.Language] = Lang.T("Chat");
            return t;
        }

        /// <summary>A language's name in that language (never translated).</summary>
        public static string LanguageName(string code) => code == Lang.Pt ? "Português (Brasil)" : "English";

        private static ControllerProfiles.Profile NextProfile(ControllerProfiles.Profile p)
        {
            int i = Array.FindIndex(ControllerProfiles.All, x => x.Id == p);
            return ControllerProfiles.All[(i + 1) % ControllerProfiles.All.Length].Id;
        }

        /// <summary>Client wave 14 (w14menu): the rotation sensitivity as shown. Client wave 15 (w15menu), point 6: the wrist turn per icon
        /// alone, "10°" (wave 14: a level of 12 and the degrees, "8 (10°)").</summary>
        public static string RingTurnShown(float deg) => Mathf.RoundToInt(deg) + "°";
        private static string MsaaName(int m) => m < 0 ? Lang.T("~msaa~Default") : m == 0 ? Lang.T("~msaa~Off") : m + "×";
        /// <summary>Comfort.ShadowLevels / EffectsLevels in the current language (Comfort keeps the English for its log).</summary>
        private static string ShadowName(int i) => i == 0 ? Lang.T("~shadows~Off") : i == 1 ? Lang.T("~shadows~Near") : Lang.T("~shadows~Far");
        private static string EffectsName(int i) => i == 0 ? Lang.T("~effects~All") : i == 1 ? Lang.T("~effects~Fewer") : Lang.T("~effects~Quiet");
        private static string MonsterSoundsName(int i) => i == 0 ? Lang.T("~sounds~Off") : i == 1 ? Lang.T("~sounds~Few") : Lang.T("~sounds~Normal"); // client wave 9 (h9feel)
        /// <summary>Client wave 12 (w12cast): a casting gesture's name (client wave 13: the overhead menu shows the same names) and what it is.</summary>
        public static string CastName(int i) => i == 0 ? Lang.T("~cast~Off") : i == 1 ? Lang.T("~cast~Pad") : i == 2 ? Lang.T("~cast~Flick") : Lang.T("~cast~Wand");
        private static string CastHelp(int i) => i == 0 ? Lang.T("Wheels and menus only") : i == 1 ? Lang.T("Grip + point: a 3 × 3 pad") : i == 2 ? Lang.T("Grip + point, then flick") : Lang.T("Grip the wand at your hip");
        private static string WandQuickName(int i) => i == 0 ? Lang.T("~wq~Off") : i == 1 ? Lang.T("~wq~Wheel") : i == 2 ? Lang.T("~wq~Buttons") : Lang.T("~wq~Both");
        private static string WandQuickHelp(int i) => i == 0 ? Lang.T("The trigger draws two strokes") : i == 1 ? Lang.T("Hold the trigger: a wheel") : i == 2 ? Lang.T("A, stick click, stick up / down") : Lang.T("The wheel and the buttons");
        /// <summary>Client wave 12 (w12cast): the Controls page's "Place…" (the wand's placement mode: VR/WandHolster.cs).</summary>
        public Action PlaceWand;

        private static string EffectsHelp(int level) => level switch
        {
            0 => Lang.T("As in the original game"),
            1 => Lang.T("Others' effects only near you"),
            _ => Lang.T("Other players' effects hidden"),
        };
    }

    /// <summary>First-launch tutorial: how to move, look, act and find the menus (shown once; the Settings window can show it again).
    /// Client wave 4 (w4vr): the buttons named are those of the controller in your hands (VR/ControllerProfiles.cs).</summary>
    public sealed class Tutorial
    {
        private readonly ListWindow _win; private readonly Func<Camera> _head; private int _page;
        public event Action Finished;
        public Tutorial(ListWindow win, Func<Camera> head) { _win = win; _head = head; _win.OnCloseClicked = Done; }
        public bool Visible => _win.Visible;
        public int Page => _page;
        public void Show() { _page = 0; _win.SetTabs(null, 0, null); _win.SetInput(false); _win.Open(_head()); Refresh(); }
        public void Next() { if (_page < Pages().Count - 1) { _page++; Refresh(); } else Done(); }
        public void Back() { if (_page > 0) { _page--; Refresh(); } }
        private void Done() { _win.Hide(); if (!Comfort.TutorialDone) { Comfort.TutorialDone = true; Comfort.Save(); } Finished?.Invoke(); }
        /// <summary>Client wave 18 (w18acct): the account's settings say it was seen (on another PC): it closes.</summary>
        public void CloseIfDone() { if (Comfort.TutorialDone && Visible) Done(); }

        private static List<(string title, string[] lines)> Pages()
        {
            var p = ComfortWindow.Active();
            var table = ControllerProfiles.Buttons(p, Comfort.LeftHanded);
            string How(string action) { foreach (var (a, h) in table) if (a == action) return h; return null; }
            string Line(string what, string action) { var h = How(action); return h == null ? null : $"{what}: <color=#ffd060>{h}</color>"; }
            var hi = new List<string> { Lang.T("Ragnarok Online on a table in front of you.") };
            foreach (var l in new[] { Line(Lang.T("Walk"), "Walk"), Line(Lang.T("Turn"), "Turn"), Line(Lang.T("Move the table"), "Move the table"), Line(Lang.T("~tutorial~Zoom"), "Zoom the table"), Line(Lang.T("~tutorial~Recentre"), "Recentre / switch view") }) if (l != null) hi.Add(l);
            var menus = new List<string>();
            foreach (var (a, h) in table)
                if (a.StartsWith("Wrist menu") || a.StartsWith("Minimap") || a.StartsWith("NPC next") || a.StartsWith("Cancel skill") || a == "Settings" || a == "Resize mode, microphone") // (client wave 13, w13view: + the freed button)
                    menus.Add($"{Lang.T(a)}: <color=#ffd060>{h}</color>");
            menus.Add(Lang.T("Text boxes open the VR keyboard."));
            string click = How("Click, attack, talk, pick up") ?? Lang.T("Pull the trigger.");
            return new List<(string, string[])>
            {
                (Lang.T("Welcome to Loki"), hi.ToArray()),
                (Lang.T("Look, then act"), new[] {
                    Lang.T("Look at something: a ring locks onto it."),
                    Lang.F("Act on it: <color=#ffd060>{0}</color>", click),
                    Lang.T("Or point the laser at it."),
                    Lang.T("Skills: choose one (menu or quick bar), aim, pull the trigger.") }),
                (Lang.T("Menus and buttons"), menus.ToArray()),
                (Lang.T("Views and comfort"), new[] {
                    // client wave 11 (w11menu): where the views and the settings are depends on the menu's style
                    Comfort.MenuStyle == 0 ? Lang.T("Views: Table, Follow, First person (ring → Settings → View).")
                                           : Lang.T("Views: Table, Follow, First person (wrist menu → View)."),
                    Lang.T("Follow: in Resize mode, hold one grip and raise or lower it to tilt."), // client wave 11 (w11menu); client wave 14 (w14laser), point 11
                    // (client wave 14, w14menu: the Menu row is in Controls → Menus)
                    Comfort.MenuStyle == 0 ? Lang.T("Settings: ring → Settings → Settings…. Classic menu: Controls → Menus.")
                                           : Lang.T("Settings: wrist menu → View → Settings."),
                    Lang.T("Have fun — and take breaks!") }),
            };
        }

        public void Refresh()
        {
            if (!_win.Visible) return;
            var pages = Pages(); _page = Mathf.Clamp(_page, 0, pages.Count - 1);
            var (title, lines) = pages[_page];
            _win.SetTitle(title); _win.SetInfo($"{title}  ·  {_page + 1} / {pages.Count}");
            var rows = new List<ListWindow.Row>();
            foreach (var l in lines) rows.Add(new ListWindow.Row { Text = l }); // w4lang: the row shrinks a long line itself (ListWindow best fit, 23 → 17 px)
            _win.SetRows(rows, true);
            _win.SetFooter((_page < pages.Count - 1 ? Lang.T("Next") : Lang.T("Done"), Next), (_page > 0 ? Lang.T("Back") : null, Back), (Lang.T("Skip"), Done));
        }
    }
}
