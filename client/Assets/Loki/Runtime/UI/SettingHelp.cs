using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 15 (w15menu), point 7 of the design's notes on wave 14 ("when I mouse over a specific setting, an extra small little panel opens
    /// up to the right of the settings panel like a pop-up that explains in brief clear understandable terms what that specific setting does
    /// remember not to be too verbose"). The Settings window's HELP PANEL:
    ///   When   the pointing hand's LASER rests on a control of a setting row (a choice, the − / + or ◄ / ►, the slider's track, an action
    ///          button: UI/ListWindow.cs RowOfButton) for <see cref="Delay"/> s, wave 14's tip rule (UI/ButtonTip.cs: the laser's own
    ///          button, never the look's). Once shown it follows the laser to another row at once; it goes <see cref="Grace"/> s after the
    ///          laser left every setting row (the gaps between a row's controls do not make it blink), and with the window.
    ///   Where  to the RIGHT of the Settings window, in its plane, <see cref="GapPx"/> px from its edge, level with the row (kept within the
    ///          window's height); drawn over the panels (sorting order 20), no collider: the laser passes through it.
    ///   What   the setting's name, what it does in a few plain words, then each choice with its own few words: the current one marked "●",
    ///          the one under the laser in gold (<see cref="Texts"/>, English and Brazilian Portuguese: Resources/Lang/pt-BR.txt). A row with
    ///          no text of its own (a setting added later) shows its name and the explanation under it.
    /// </summary>
    public sealed class SettingHelp : MonoBehaviour
    {
        /// <summary>Client wave 18 (w18menu), point 17 (design note, 2026-10-09: "reduce the amount of time that needs to be hovered over a specific setting
        /// before the menu shows up explaining what it is. To zero. And remember to keep it only showing up if I point at it with the laser"):
        /// the help's own delay, 0 (the frame the laser reaches a row); the icon buttons' tips keep ButtonTip.Delay (0.6 s). Still the laser
        /// only (LokiGame.W15Menu.cs hands Tick the lasers' buttons, never the look's); the grace keeps it from blinking between rows.</summary>
        public const float Delay = 0f, Grace = 0.3f, WidthPx = 600f, GapPx = 22f, PadPx = 22f; // (client wave 18, point 19: 600 px wide, was 470: the longer texts)
        public const int TitlePx = 30, BodyPx = 26;
        private RectTransform _rt, _rim, _bg; private Text _text;
        private ListWindow _win; private Func<ListWindow.Row, string> _keyOf;
        private int _on = -1, _choice = -1; private float _since, _lastSeen; private bool _shown; private string _shownText;
        /// <summary>The row whose help is shown (-1: none), its text as drawn, how many times it was shown (smoke, log).</summary>
        public int ShowingRow => _shown ? _on : -1;
        public string ShownText => _shown ? _shownText : null;
        public int Shows { get; private set; }
        /// <summary>The panel's middle and its frame in the world (smoke: it is right of the window).</summary>
        public Transform Panel => _rt;
        public Vector2 SizePx => _rt.sizeDelta;

        /// <summary>The help panel of window <paramref name="win"/>; <paramref name="keyOf"/> gives a row's help key (null: none).</summary>
        public static SettingHelp Create(Transform parent, ListWindow win, Func<ListWindow.Row, string> keyOf)
        {
            var rt = UiKit.Panel("SettingHelp", parent, new Vector2(WidthPx, 200f), 0.00052f, false, PanelKind.ByName, PanelLook.Card); // client wave 21 (w21kit): a parchment card of the theme
            var canvas = rt.GetComponent<Canvas>(); if (canvas != null) canvas.sortingOrder = 20;
            var h = rt.gameObject.AddComponent<SettingHelp>();
            h._rt = rt; h._win = win; h._keyOf = keyOf;
            h._rim = (RectTransform)rt.Find("border"); h._bg = (RectTransform)rt.Find("bg"); // (the card's paper and its unused inner box: Fit sizes both)
            h._text = UiKit.Label(rt, "", Vector2.zero, new Vector2(WidthPx - 2f * PadPx, 160f), BodyPx, TextAnchor.UpperLeft, Theme.Ink);
            h._text.horizontalOverflow = HorizontalWrapMode.Wrap; h._text.verticalOverflow = VerticalWrapMode.Overflow; h._text.resizeTextForBestFit = false;
            rt.gameObject.SetActive(false);
            return h;
        }

        /// <summary>Once a frame: the button the pointing hand's laser is on (null: none).</summary>
        public void Tick(UiButton laserButton)
        {
            float now = Time.unscaledTime;
            if (_win == null || !_win.Visible) { _on = -1; Hide(); return; }
            int row = -1, choice = -1; float y = 0f;
            if (laserButton != null && laserButton.isActiveAndEnabled) { row = _win.RowOfButton(laserButton, out choice, out y); if (row >= 0 && !_win.RowAt(row).IsSetting) row = -1; }
            if (row < 0)
            {
                if (_shown && now - _lastSeen > Grace) { _on = -1; Hide(); }
                else if (!_shown) _on = -1;
                return;
            }
            _lastSeen = now;
            if (row != _on) { _on = row; _since = now; if (!_shown && Delay > 0f) return; } // shown: it follows the laser to another row at once (client wave 18: no delay: shown this frame)
            if (!_shown && now - _since < Delay) return;
            _choice = choice;
            string text = TextOf(_win.RowAt(row), choice);
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            if (!_shown) { _rt.gameObject.SetActive(true); _shown = true; Shows++; _shownText = null; Debug.Log($"[Help] '{_win.RowAt(row).Key}' after {now - _since:0.00} s of the laser"); }
            if (text != _shownText) Fit(text);
            PlaceBeside(y);
        }

        public void Hide() { _shown = false; if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false); }

        /// <summary>The help of a row: its name, what it does, each choice (the current one "●", <paramref name="hovered"/> in gold).</summary>
        public string TextOf(ListWindow.Row r, int hovered)
        {
            if (r == null || !r.IsSetting) return null;
            string name = UiKit.StripTags((r.Text ?? "").Split('\n')[0]);
            var sb = new System.Text.StringBuilder();
            sb.Append("<size=").Append(TitlePx).Append("><b><color=#ffd060>").Append(name).Append("</color></b></size>");
            string key = _keyOf != null ? _keyOf(r) : r.Key;
            if (key != null && Texts.TryGetValue(key, out var e))
            {
                sb.Append('\n').Append(Lang.T(e[0]));
                int cur = r.Choices != null ? r.Current : -1;
                if (r.Choices != null && e.Length - 1 == r.Choices.Length)
                    for (int k = 0; k < r.Choices.Length; k++)
                    {
                        bool lit = k == hovered;
                        sb.Append('\n').Append(k == cur ? SettingRow.Mark : "    ").Append(lit ? "<color=#ffd060>" : "<color=#c8cdd8>").Append("<b>").Append(r.Choices[k]).Append("</b>: ").Append(Lang.T(e[k + 1])).Append("</color>");
                    }
                return sb.ToString();
            }
            // a row with no text here (a setting added later): its own explanation, if it has one
            var lines = (r.Text ?? "").Split('\n');
            if (lines.Length > 1) sb.Append('\n').Append(UiKit.StripTags(lines[1]));
            return sb.ToString();
        }

        /// <summary>The panel as tall as its text.</summary>
        private void Fit(string text)
        {
            _shownText = text; _text.text = text;
            float tw = WidthPx - 2f * PadPx;
            float h = HeightFor(text);
            _rt.sizeDelta = new Vector2(WidthPx, h); _rim.sizeDelta = new Vector2(WidthPx, h); _bg.sizeDelta = new Vector2(WidthPx - 4f, h - 4f);
            ((RectTransform)_text.transform).sizeDelta = new Vector2(tw, h - 2f * PadPx);
        }

        /// <summary>The panel's height for <paramref name="text"/>, in px (client wave 18, w18menu, point 19: the smoke checks every text fits
        /// beside the window, <see cref="ListWindow.SizePx"/>).</summary>
        public float HeightFor(string text)
        {
            var gs = _text.GetGenerationSettings(new Vector2(WidthPx - 2f * PadPx, 10000f));
            gs.scaleFactor = 1f;
            float h = Mathf.Ceil(_text.cachedTextGeneratorForLayout.GetPreferredHeight(text ?? "", gs)) + 2f * PadPx;
            return Mathf.Max(h, 90f);
        }

        /// <summary>Right of the window, in its plane, level with the row (px <paramref name="rowY"/> on the window), within its height.</summary>
        private void PlaceBeside(float rowY)
        {
            var w = _win.transform; var size = ListWindow.SizePx; float h = _rt.sizeDelta.y;
            float y = Mathf.Clamp(rowY, -size.y / 2 + h / 2, size.y / 2 - h / 2);
            var pos = w.TransformPoint(new Vector3(size.x / 2 + UiKit.OutsetOf(w) + GapPx + WidthPx / 2, y, 0f)); // (client wave 21: clear of the window's wood frame)
            transform.SetPositionAndRotation(pos, w.rotation);
            transform.localScale = w.lossyScale; // (its parent is the rig, as every panel's)
        }

        // ------------------------------------------------------------------ the texts
        /// <summary>A setting's help by its row's key (the English name; the Rumble page's rows "rumble:" + it): what it does, then one line
        /// per choice in the row's order (none: a switch or a number). English; Lang.T gives the player's language.
        /// Client wave 18 (w18menu), point 19 (design note, 2026-10-09: "Your explanation for some of the settings is insufficiently clear ... on the
        /// pop-ups you can be a little bit more verbose"): EVERY text rewritten from the code of its setting (the facts and their file:line in
        /// design notes): 2-4 plain sentences per setting (what it does in the game, when you would want it), 1-2 per choice
        /// (what that choice changes). The row names and choices themselves stay short (item 17).</summary>
        public static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
        {
            // Comfort
            { "Turning", new[] { Lang.N("Push the pointing hand's stick left or right to turn. The world turns around you, in every view. Snap is gentler on the stomach for many players."),
                Lang.N("You turn steadily while you hold the stick. The further you push it, the faster you turn."),
                Lang.N("Each push turns you at once by the snap angle. Let the stick come back to the middle before the next one.") } },
            { "Snap angle", new[] { Lang.N("How far one snap turn takes you, from 30° to 90°. Small angles aim finer; big ones turn you around in fewer pushes.") } },
            { "Turn speed", new[] { Lang.N("How fast smooth turning goes with the stick pushed all the way, from 45° to 180° a second. A light push turns slower. Lower it if turning makes you uneasy.") } },
            { "Vignette", new[] { Lang.N("Darkens the edges of your view while the world moves around you: smooth turns, moving the table, walking in Follow or First person. It also flashes on snap turns and knockbacks. Keep it on if moving in VR makes you queasy.") } },
            { "Vignette strength", new[] { Lang.N("How much of your view the vignette covers. Low darkens only the far edges. High leaves a narrow window in the middle.") } },
            // client wave 19 (w19world), point 48: the Follow view's character edge glow
            { "Character edge glow", new[] { Lang.N("In the Follow view, a soft glow lights the edge of your view on the side where your character is, as it starts to leave your view. It grows brighter the farther out your character is, and fades when it is back in view.") } },
            { "Glow strength", new[] { Lang.N("How bright the glow is when your character is at the edge of your view or beyond. Low is a faint hint; high is easy to notice.") } },
            { "Glow sensitivity", new[] { Lang.N("How early the glow starts, as a share of the way from the middle of your view to its edge. At 40 % it starts at 80 % of the way; higher starts sooner.") } },
            { "Knockbacks", new[] { Lang.N("How your own character moves when it is pushed or jumps: knockbacks, Back Slide, Body Relocation, pulls. It matters in Follow and First person, where your view moves with it."),
                Lang.N("Your view glides quickly to the new spot."),
                Lang.N("Your view jumps to the new spot at once. In the Table view it always glides.") } },
            // (client wave 26 merge, w26int, design note 2026-10-14: the choice "Smooth" is now "Free": stop anywhere, free movement)
            { "Movement", new[] { Lang.N("How characters and monsters walk on screen, and what the walking stick does. The server keeps everyone on cells either way."),
                Lang.N("They glide along rounded paths, and the stick walks in any direction. Let go and you stop right there, even between cells."),
                Lang.N("They step from cell to cell, as in the original game. In Follow and First person your view zig-zags with them.") } },
            { "Look aim: sideways", new[] { Lang.N("Moves where your look starts: from your left eye, through the middle, to your right eye. Set it so the look ring lands on what you look at. Useful if you aim with one eye.") } },
            { "Look aim: up / down", new[] { Lang.N("Tilts your look up or down, up to 10°. Use it if the look ring sits a little above or below what you look at.") } },
            { "Look ring", new[] { Lang.N("A small ring where your look lands. It helps you set the two Look aim sliders."),
                Lang.N("Shown only while this page is open."),
                Lang.N("Shown all the time while you play. Hidden while Look select is off.") } },
            { "Look aim", new[] { Lang.N("Puts both Look aim sliders back to the middle. Your look then starts between your eyes and goes straight ahead.") } },
            // client wave 20 (w20tut), point 65: the look calibration (UI/LookCalibrator.cs, VR/LookCalibration.cs)
            { "Recalibrate look", new[] { Lang.N("Measures again where you really look. Dots appear one at a time: turn your head to each and hold still while its ring fills. Loki then corrects your look on this PC, on top of the two Look aim sliders. The tutorial does this once on every new PC.") } },
            { "Look select", new[] { Lang.N("Lets your look choose: look at a button and pull the trigger to press it, and a ring locks onto the monster or NPC you look at. Off: only the laser chooses and aims. The overhead menu has the same switch.") } },
            { "Seated play", new[] { Lang.N("Lowers the table by 25 cm, for playing seated. Nothing else changes: Table height then shows the lowered height.") } },
            { "Table height", new[] { Lang.N("How high the table stands above your floor. In the Follow view your character stands at this height too. The First person view does not use it.") } },
            { "Lightning flashes", new[] { Lang.N("White flashes over your whole view when lightning strikes in the rain. Off by default: the thunder still sounds and the rain stays.") } },
            { "Camera shake", new[] { Lang.N("Some big skills shake your whole view, as in the original game: Meteor Storm, Heaven's Drive, Magnum Break and others. Off by default; turn it on only if a shaking view does not bother you.") } },
            { "Hallucination view", new[] { Lang.N("While your character has Hallucination, the original game turns the view upside down. On does the same in VR. Off by default: your view stays upright.") } },
            { "Codex scan", new[] { "Look at a creature you haven't written down and a ring fills around it; when it's full, the creature is in your Codex. Off: no ring. A kill still writes it." } }, // wave 28 (wcodex; English only)
            { "Codex flip-through", new[] { "When you pick a page in the Codex, its pages riffle quickly to it. Off: the page opens at once. A page you saw a few seconds ago always opens at once." } }, // wave 28 (wcodex; English only)
            { "Codex book", new[] { "Hand-held: a book you hold; grip its edge to take it or pass it to the other hand, and touch a corner arrow to turn a page. Large: a big book in front of you. Applies the next time you open it." } }, // wave 28 (wcodex; English only)
            // Controls
            { "Left-handed", new[] { Lang.N("Swaps the hands' jobs. On: the left hand points the laser, clicks and turns; the right hand walks and carries the menu, the minimap and the quick bar. The buttons table and the tutorial follow.") } },
            { "Laser", new[] { Lang.N("Which way the laser leaves your pointing hand. Try the other choice if the laser does not go where you point."),
                Lang.N("Along the pointing ray your headset defines for each controller. Tracked hands need this one."),
                Lang.N("Straight out of the front of the controller.") } },
            { "Laser walking", new[] { Lang.N("Walk by pulling the trigger with the laser on the ground. The overhead menu has the same switch. Skills, attacks and the walking stick work either way."),
                Lang.N("The trigger on the ground walks nowhere. The laser and the wand's line show only near a panel, a monster, a character or an item, or on the ground while a ground skill waits."), // (client wave 18 merge: w18wand point 11, the wand's line follows this rule)
                Lang.N("The trigger on the ground walks to that spot. A cursor marks the cell.") } },
            { "Left wrist buttons", new[] { Lang.N("Six buttons over your walking forearm: Menu, Map, Back, Next, Centre and View. They replace controller buttons that tracked hands and simple controllers lack."),
                Lang.N("Shown only with a tracked hand, a Vive wand or a simple controller."), Lang.N("Always shown."), Lang.N("Never shown.") } },
            { "Right wrist buttons", new[] { Lang.N("Six buttons over your walking forearm: Menu, Map, Back, Next, Centre and View. They replace controller buttons that tracked hands and simple controllers lack."),
                Lang.N("Shown only with a tracked hand, a Vive wand or a simple controller."), Lang.N("Always shown."), Lang.N("Never shown.") } },
            { "Controls tutorial", new[] { Lang.N("Plays the hands-on tutorial again. Each step waits until you do it: the laser, the look, walking, the views, the overhead menu, the map, Reposition, panels, the menu, the Settings help and every way to cast. Skip step passes one; End tutorial stops it. On a Loki Host server it runs on a private map, then brings you back where you were.") } }, // (client wave 18 merge: w18tut's interactive tutorial, its own proposed text; client wave 21, w21map: its last sentence, the tutorial's own map)
            { "Casting gesture", new[] { Lang.N("How you take a skill or item from your quick slots with a hand gesture. Pad and Flick start when you squeeze the grip with your index finger off the trigger, on either hand. Letting go of the grip takes what you chose."),
                Lang.N("No gesture. Use the skill wheels, the forearm quick bar or the menus."),
                Lang.N("A 3 × 3 pad of one quick-slot row opens at your fingertip. Move onto a slot and let go."),
                Lang.N("Draw two short strokes in the air. The first picks one of 8 rows, the second a slot."),
                Lang.N("Take a wand from your hip with the grip and cast with its trigger. The Wand settings say how.") } },
            { "Cast immediately", new[] { Lang.N("A skill you take with the pad, the flick or the wand is cast at once when it needs no aiming: a skill on yourself, or on a target you already point at or look at. Off: it waits on your hand until you pull the trigger at a target. Ground skills from the pad and the flick always wait for that pull.") } },
            { "Cast grip", new[] { Lang.N("How hard you squeeze the grip to open the pad or the flick, take the wand or hold a panel. On the Index it is the squeeze's force; on other controllers how far the grip goes in. Choose a firmer level if gestures start when you only rest your hand."),
                Lang.N("A light squeeze is enough."), Lang.N("A firmer squeeze."), Lang.N("A full squeeze, as for resize mode.") } },
            { "Pad size", new[] { Lang.N("The size of one cell of the casting pad, from 4 to 10 cm. Bigger cells need longer moves but are harder to miss. The flick is not affected.") } },
            { "Forearm quick bar", new[] { Lang.N("Nine quick slots over your walking forearm, with arrows to change the row. Press a slot with the laser, or look at it and pull the trigger: an item is used, a skill cast or readied. The overhead menu has the same switch."),
                Lang.N("The bar is always shown."),
                Lang.N("The bar is hidden. Your slots still work on the pad, the flick and the wheels."),
                Lang.N("A button at your elbow shows or hides the bar. Touch it with your other hand, or press it with the laser.") } },
            { "Wand holster", new[] { Lang.N("Where the wand hangs, for the Wand casting gesture. Place: grip the marker, move it where you like, let go; press B when done. Reset: back to your hip, on the pointing hand's side.") } },
            { "Wand quick cast", new[] { Lang.N("Faster ways to cast while you hold the wand. The wheel opens with the wand's trigger; the buttons are on the pointing hand. Put things on the wand with the ★ in the menus."),
                Lang.N("Hold the trigger and draw two strokes with the wand, as with the flick. The first picks a row, the second a slot."),
                Lang.N("Hold the trigger: a wheel opens. Move the tip onto one of its inner slots and let go to take it; touch an outer part to open one of your rows."),
                Lang.N("Your lower face button, stick click, stick up and stick down each cast their own skill at once. The trigger draws two strokes, as with Off."),
                Lang.N("The wheel and the buttons, both.") } },
            { "Wand aim", new[] { Lang.N("Where a skill cast with the wand goes. Look lets you cast without aiming the wand; it needs Look select on."),
                Lang.N("To what the wand points at. If it points at nothing, to what your look has locked."),
                Lang.N("The wand's direction does not matter: the skill goes at once to what you look at. If you look at nothing, it waits on the wand until your next pull.") } },
            { "Wand wheel shown", new[] { Lang.N("Where the wand's wheel is drawn while you hold the trigger. It works the same either way; only the picture moves."),
                Lang.N("A small wheel in front of your eyes that follows your head, with a dot for the wand's tip. Reposition can move and resize it."),
                Lang.N("Life size, in the air where the wand's tip was when you pulled.") } },
            { "Wand rows", new[] { Lang.N("What the wheel does when the wand's tip touches one of its outer parts. Each opens a quick-slot row: slots 1 to 8 of that row."),
                Lang.N("The row's wheel takes the main wheel's place; choose from its middle. To get back, pull again."),
                Lang.N("The row's wheel opens next to the main wheel, which stays open.") } },
            { "Wand size", new[] { Lang.N("The size of the wand, from the full wand (100 %) down to one whose tip sits on your fingertip, where a flick starts. In Resize mode you can also grip the wand at its holster and resize it with both grips. How close your hand must come to take it does not change.") } },
            { "Wheel size", new[] { Lang.N("How large the wand's wheel is. A smaller wheel needs shorter moves of the tip.") } }, // client wave 23 (w23wand), the design's answer 55: the wand's wheel has its own size
            { "Inventory", new[] { Lang.N("How your bag looks in the new menu. Both do the same things."),
                Lang.N("A curved panel around you, your items in sections by type. Each section has its own sort and filter."),
                Lang.N("One grid with all your items, as before.") } },
            { "Menu", new[] { Lang.N("Which menu the menu button opens. Both have the same pages."),
                Lang.N("A ring of 8 big icons over your hand. Each opens that part of the menu as a board in front of you."),
                Lang.N("A panel on your wrist with all the tabs.") } },
            { "Menu ring", new[] { Lang.N("How you choose an icon on the ring of the New menu. Pressing the menu button again closes the ring without opening anything."),
                Lang.N("Push the walking stick towards an icon and let go. Or point the laser or your look at it and pull the trigger."),
                Lang.N("Roll the wrist of the hand under the ring to move the choice, then pull either trigger. The rows below tune it.") } },
            { "Ring shape", new[] { Lang.N("The ring's shape in Twist mode. It opens on Character either way."),
                Lang.N("The 8 icons on an arch over your hand. It has two ends: rolling past one does nothing."),
                Lang.N("The 8 icons in a full circle. The far side is reached both ways.") } },
            { "Wrist moves", new[] { Lang.N("What moves when you roll your wrist in Twist mode. The choice is the same either way."),
                Lang.N("The icons stay still; a gold frame moves between them."),
                Lang.N("The icons slide under a fixed mark at the top.") } },
            { "Ring direction", new[] { Lang.N("Which way rolling your wrist moves the choice in Twist mode. Try the other one if it feels backwards."),
                Lang.N("Roll the top of your hand left: the choice moves right."),
                Lang.N("The choice moves the same way as the top of your hand.") } },
            { "Rotation sensitivity", new[] { Lang.N("How far you roll your wrist to move one icon in Twist mode, from 30° to 6°. Fewer degrees reach the ends with less wrist; more degrees are steadier.") } },
            { "Panel smoothing", new[] { Lang.N("Steadies a panel you hold, so a shaky hand does not shake it; quick moves still keep up. 0 % turns it off. 200 % steadies twice as much as 100 %, but the panel trails slow moves more.") } }, // (wave 15 merge: w15grab's row; client wave 18, point 6: to 200 %)
            // client wave 21 (w21anim), the design's point 63: the one row of the menus' opening animations, and the one text of every switch in its list
            // client wave 23 (the merge), design note 2026-10-11: "The Player knows what opening a menu in a video game is like ... There's no need to explain how
            // the menu flies out of your wrist or other such inconsequential minutia.": ONE plain sentence that says what the switch does
            { "Menu animations…", new[] { Lang.N("Turns the opening animation of each menu on or off.") } },
            // client wave 22 (w22tree), the design's point 64: the 3D skill tree or the flat page
            { "Skill tree", new[] { Lang.N("How the Skills page is shown. Both learn skills, show their details and put them on your quick slots."),
                Lang.N("A tree for each job of your line, one medallion per skill. Rest the laser or a hand on a skill to see it played."),
                Lang.N("The page of rows and tabs on the board.") } },
            { "Skill trees", new[] { Lang.N("Where the skill trees stand when you open Skills. Each job of your line has its own tree."),
                Lang.N("They rise side by side in an arc in front of you: your first job on the left, your current job on the right. Grip a tree and pull sideways to turn the arc."), // (client wave 23, w23tree: one arc in front, no circle)
                Lang.N("One tree ahead of you, your current job's first. The round buttons above it, or a grip pulled sideways, flip to the next.") } },
            { "Skill trees: muffle other sounds", new[] { Lang.N("Other sounds get quieter and duller while the skill trees are open. Friends' voices stay clear.") } }, // client wave 23 (w23tree), item 24
            { "menuanim", new[] { Lang.N("Turns this menu's opening animation on or off.") } },
            { "B and X", new[] { Lang.N("What B and a window's X do on a menu you opened from another menu. The arrow at the top left always goes back."),
                Lang.N("Close it, and forget the menus before it."),
                Lang.N("Go back to the menu you came from.") } },
            { "Panel tilt", new[] { Lang.N("How a panel turns while you hold it. Shake a held panel to switch until it closes. The overhead menu has the same switch. In Reposition it also sets how a pad you carry turns."), // (client wave 18 merge: w18flick point 15)
                Lang.N("It stays upright and keeps facing you."),
                Lang.N("It tilts and turns with your hand.") } },
            // client wave 18 (w18menu), point 2: the ring snap's rows
            { "Ring snap", new[] { Lang.N("In Twist mode, holds the selector on the chosen icon while your wrist wobbles a little. It helps shaky hands choose quickly. The choice also moves on a little later, so it does not flicker between two icons.") } },
            { "Snap strength", new[] { Lang.N("How firmly the snap holds the chosen icon. At 100 % the selector stays still on it inside the snap range, and the choice moves on only when your wrist is almost at the next icon.") } },
            { "Snap range", new[] { Lang.N("How far from an icon the snap holds, as a share of the gap to the next icon. At 50 % it holds all the way to the halfway point.") } },
            // Rumble (its rows' keys get "rumble:": "Menu ring" is a Controls row too)
            { "rumble:Rumble", new[] { Lang.N("Turns all controller rumble on or off. Each kind below also has its own switch and strength.") } },
            { "rumble:Rumble strength", new[] { Lang.N("One strength for every kind, from 20 % to 100 %. At 60 % each rumble plays as designed; 100 % is stronger, 20 % a third.") } },
            { "rumble:Buttons", new[] { Lang.N("When the laser or your look moves onto a button, or presses one. Also a few switches, such as Look select and Reposition.") } },
            { "rumble:Menu ring", new[] { Lang.N("Each step of the menu ring in Twist mode. Also moving the highlight over Horde's cards with the stick.") } },
            { "rumble:Casting", new[] { Lang.N("Opening the pad or the flick, and each cell or stroke. Also each part of the wand's wheel and of the skill wheels.") } },
            { "rumble:Wand", new[] { Lang.N("Your hand reaching the holster, taking or putting back the wand. Also its quick buttons.") } },
            { "rumble:Moving the table", new[] { Lang.N("Grabbing a panel, a board or the bag, and shaking a held panel. Also resize mode turning on or off.") } },
            { "rumble:Hits you deal", new[] { Lang.N("Each hit of yours that deals damage, a little stronger for a critical. In Horde, your critical hits.") } },
            { "rumble:Hits you take", new[] { Lang.N("Each hit that damages you, on both hands. In Horde it is stronger on the side the hit came from, and warns just before a red area strikes.") } },
            { "rumble:Item pickup", new[] { Lang.N("When an item enters your inventory. Picking up loot is the usual case.") } },
            { "rumble:Level up", new[] { Lang.N("When your base or job level goes up. In Horde, each level-up offer and each objective's gift.") } },
            { "rumble:Low HP heartbeat", new[] { Lang.N("A double heartbeat on both hands while your HP is at a quarter or less. It stops when you heal.") } },
            { "rumble:Death", new[] { Lang.N("When your character dies. In Horde, when your runner falls.") } },
            { "rumble:Rumble kinds", new[] { Lang.N("Puts every kind back to on and 100 %. The main switch and strength stay as they are.") } },
            // Audio
            { "All sound", new[] { Lang.N("The volume of everything you hear in Loki: music, effects and friends' voices. 0 % is silence.") } },
            { "Music", new[] { Lang.N("The volume of the map's music. All sound applies on top.") } },
            { "Sound effects", new[] { Lang.N("The volume of skills, hits, monsters, buttons, rain and the Horde arena. Friends' voices have their own volume on the Voice page.") } },
            // Graphics
            { "Render scale", new[] { Lang.N("How sharp the picture is in the headset, from 70 % to 150 %. Lower runs faster on a weaker PC; higher is sharper but slower. A change may stutter for a moment.") } },
            { "Anti-aliasing", new[] { Lang.N("Smooths the jagged edges of the map and the models. Higher looks smoother but costs speed."),
                Lang.N("The level the game is built with."), Lang.N("No smoothing: the fastest."), Lang.N("Light smoothing."), Lang.N("Good smoothing, a little slower."), Lang.N("The smoothest, the slowest.") } },
            { "Shadows", new[] { Lang.N("Shadows of the map, its models and the characters, from the map's sun. They cost speed."),
                Lang.N("No shadows: the fastest."), Lang.N("Shadows close to you only."), Lang.N("Shadows further away too.") } },
            { "Skill effects", new[] { Lang.N("How many skill effects of other people are drawn. Your own effects and effects on you are always drawn."),
                Lang.N("Every effect, as in the original game."),
                Lang.N("Effects that do not involve you are drawn only within 12 cells of you, at most 2 of each."),
                Lang.N("As Fewer, and other players' effects are never drawn.") } },
            { "Loki's skill effects", new[] { Lang.N("Loki's own effects for the skills your game data draws nothing for. Off: only the original effects are shown.") } },
            { "Name tags", new[] { Lang.N("The size of everything over characters and monsters: names, HP bars, chat bubbles, damage numbers. From 80 % to 200 %.") } },
            { "Costume looks", new[] { Lang.N("Whether others see your costume gear. The server keeps it per character."),
                Lang.N("Everyone sees your costume headgear and garment."),
                Lang.N("Everyone sees your real headgear and garment instead.") } },
            // Headsets
            { "Friends' headsets", new[] { Lang.N("Shows other players' headsets and hands over the table, with their names, when they share them. Off: their voices come from their characters instead.") } },
            { "Share my headset", new[] { Lang.N("Lets other players see your headset and hands while you play in VR. Off: they stop seeing them at once.") } },
            // Voice
            { "Share my voice", new[] { Lang.N("Lets other players on this server hear you through your microphone. It is an open mic: whatever passes the noise gate is sent.") } },
            { "Microphone", new[] { Lang.N("Which microphone to use. Windows' default follows the one chosen in Windows.") } },
            { "Microphone gain", new[] { Lang.N("How loud your microphone is, from 50 % to 400 %. More gain also opens the noise gate more easily; too much distorts.") } },
            { "Noise gate", new[] { Lang.N("Holds back quiet sounds, so room noise is not sent. Your voice must be louder than the level chosen."),
                Lang.N("Everything is sent."), Lang.N("Cuts only very quiet sounds."), Lang.N("Cuts quiet sounds."), Lang.N("Sends only clear speech.") } },
            { "Friends' voices", new[] { Lang.N("Hear other players who share their voice. Off: you hear nobody.") } },
            { "Friends on other maps", new[] { Lang.N("Also hear friends who are on another map, at a plain lower volume. Off: only friends on your map.") } },
            { "Voice volume", new[] { Lang.N("The volume of every friend's voice, from 25 % to 200 %. Each friend you have heard also gets a row of their own below.") } },
            { "Monster sounds", new[] { Lang.N("Sounds of monsters and other characters around you: attacks, steps, deaths. Your own sounds are not affected."),
                Lang.N("None of their sounds."), Lang.N("Up to 6 a second, the nearest first."), Lang.N("Up to 12 a second, from further away.") } },
            // Language
            { "Language", new[] { Lang.N("The language of Loki's own texts. Names and NPC lines come from your Ragnarok Online data. Until you choose, it follows Windows.") } },
            { "Bard and Dancer jokes", new[] { Lang.N("When your character uses Frost Joke or Scream, a joke line is said in public chat, as in the original game. Off: your lines are not sent.") } },
            // Horde
            { "Horde invitations", new[] { Lang.N("When another player starts a Horde run, a prompt asks if you want to join. The Horde panel has the same switch."),
                Lang.N("No prompt. You can still join from the Horde panel."), Lang.N("The prompt appears, with a countdown.") } },
            { "Table follow", new[] { Lang.N("How the table follows your runner during a Horde run. It keeps your runner near its middle."),
                Lang.N("It glides after your runner, a little ahead of where you walk."),
                Lang.N("It stays right on your runner."),
                Lang.N("It stays still until your runner is 5 cells from its middle.") } },
            { "Horde HUD size", new[] { Lang.N("The size of the Horde HUD, from 80 % to 150 %. It holds your bars, the fight's numbers and the boss bar.") } },
            { "Combat effects", new[] { Lang.N("How big shots and warnings are drawn in Horde. The real danger areas keep their size."),
                Lang.N("The usual size."), Lang.N("One and a half times bigger, for a far table.") } }, // client wave 17 (h17read), F1
            // client wave 18 (w18wand), point 14: Settings -> Controls -> Wand "Wand slices"
            { "Wand slices", new[] { Lang.N("How many slices the wand's inner wheel has, from 2 to 8. Each slice holds a skill or an item you pin there with ★. Fewer slices are bigger and easier to hit; more slices give you more quick picks. The outer ring with your bar's four rows stays the same.") } },
            // client wave 18 (w18flick): points 16 and 7 (Controls → Casting)
            { "Pad rows", new[] { Lang.N("How a pad chooses which row of your quick bar it shows. Each pad shows one row of nine slots; this decides which row opens and how you reach the others."),
                Lang.N("Your palm picks the row as the pad opens: your pointing hand shows row 1 palm down and row 2 palm up, your other hand rows 3 and 4."),
                Lang.N("Your pointing hand opens row 1 and your other hand row 2; slap the open pad with the same hand to switch to row 3 or 4, and again to switch back. Letting go starts over.") } },
            { "Slap strength", new[] { Lang.N("How fast your hand must push through the pad, away from you, to count as a slap. Raise it if a quick move between cells switches the row by mistake. Lower it if your slaps are missed.") } },
            { "Flick shown", new[] { Lang.N("Where the flick's rows and slots appear while you flick. Your hand moves the same way in both: only the picture moves."),
                Lang.N("A picture fixed in front of your eyes; it turns with your head."), Lang.N("Standing in the air where your flick began.") } },
            { "Flick follow", new[] { Lang.N("How long the middle of a row keeps following your hand after your first flick. At 0 the row opens where your first flick counted and stays there, so moving on picks a slot. Raise it if your first flicks are long and land on the wrong slot.") } },
            // client wave 21 (w21tools): Settings -> Controls -> Casting "Casting look"
            { "Casting look", new[] { Lang.N("How the pad, the flick, the wand's wheel and the Quick Use Bubbles are drawn. Each row keeps its colour in both looks. Friends' menus you see are drawn your way too."),
                Lang.N("A light touch of the panel theme: warm wood, cream and gold."), Lang.N("The plain look they had before: dark blue and white.") } },
            // Item 43 (design note): Settings -> Controls -> Casting "Flick grip pressure" & "Flick menu hand"
            { "Flick grip pressure", new[] { Lang.N("How far you must squeeze the grip to open the flick menu, from 15 % to 95 %. Raise it if the flick menu opens while you only rest your fingers on the grip. Lower it if you prefer a lighter squeeze.") } },
            { "Flick menu hand", new[] { Lang.N("Which hand can open the flick menu when you squeeze the grip. Choose one hand if the other hand opens it by accident while walking or turning."),
                Lang.N("Only your pointing hand opens the flick menu."),
                Lang.N("Only your walking hand opens the flick menu."),
                Lang.N("Either hand can open the flick menu.") } },
            // client wave 18 (w18share), the design's point 33 and its addition (Settings -> Headsets): my casting menus, and who sees them
            { "Casting menus shown", new[] { Lang.N("Lets friends who see your headset also see the casting menus you open: the pad, the flick, the wand's wheel, the forearm bar and the wand in your hand. They see the same icons and the choice you point at, see-through and only to look at. Seen by picks who gets them."), Lang.N("Nobody gets your menus; nothing about them leaves your PC."), Lang.N("The players picked under Seen by see them while they see your headset.") } },
            { "Seen by", new[] { Lang.N("Who may see your casting menus. The server sends them only to these players, so the others never receive them. Friends, family and guild count as the server last saved them: a new friend may take a few minutes."), Lang.N("Every player on your map who sees your headset."), Lang.N("Only the players the switches below pick: friends, family, guild, in any mix.") } },
            { "Friends", new[] { Lang.N("Players on your friends list see your casting menus, and so do players who have you on theirs. Off: a friend sees them only through Family or Guild.") } },
            { "Family", new[] { Lang.N("Your spouse, your parents and your adopted child see your casting menus. Off: they see them only through Friends or Guild.") } },
            { "Guild", new[] { Lang.N("The members of your guild see your casting menus. Off: they see them only through Friends or Family.") } }, // (design note at the merge: "I meant my Guild")
            // client wave 20 (w20pen), the design's point 57 and the design notes of 2026-10-10 (Settings -> Headsets): who sees my pen drawings, whose I see
            { "Drawings seen by", new[] { Lang.N("Who may see what you draw with the pen (Social page: Take pen). The server sends your drawings only to these players, so the others never receive them. Friends, family and guild count as the server last saved them: a new friend may take a few minutes."),
                Lang.N("Every player on your map, while your headset is shared."), Lang.N("Only the players the three switches below pick, in any mix. With all three off, nobody.") } },
            { "Friends see mine", new[] { Lang.N("Players on your friends list see your drawings, and so do players who have you on theirs. Off: a friend sees them only through Family or Guild.") } },
            { "Family sees mine", new[] { Lang.N("Your spouse, your parents and your adopted child see your drawings. Off: they see them only through Friends or Guild.") } },
            { "Guild sees mine", new[] { Lang.N("The members of your guild see your drawings. Off: they see them only through Friends or Family.") } },
            { "Drawings I see", new[] { Lang.N("Whose pen drawings show on your table. Your own always show. A change shows or hides the drawings at once."),
                Lang.N("Every drawing the server sends you."), Lang.N("Only the drawings of the players the three switches below pick. With all three off, only yours.") } },
            { "Friends' drawings", new[] { Lang.N("Show the drawings of players on your friends list, and of players who have you on theirs. Off: a friend's drawings show only through Family or Guild.") } },
            { "Family's drawings", new[] { Lang.N("Show the drawings of your spouse, your parents and your adopted child. Off: they show only through Friends or Guild.") } },
            { "Guild's drawings", new[] { Lang.N("Show the drawings of the members of your guild. Off: they show only through Friends or Family.") } },
            // client wave 21 (w21jobui): the panel theme's job variants (UI/JobTheme.cs)
            { "Panel theme", new[] { Lang.N("Your panels can wear your job's look: a small emblem, one colour, and richer corners as your job advances. Sizes, layout and text stay the same."),
                Lang.N("Follows your job. It changes at once when you change job."), Lang.N("The plain wood and gold look, the same for every job.") } },
        };
    }
}
