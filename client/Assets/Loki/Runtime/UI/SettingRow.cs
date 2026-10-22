using System;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 13, item 5 (w13ring): THE way to make a row of the Settings window (and of any other window that holds a setting).
    /// design note, 2026-10-07: "instead of the button telling you what is currently chosen, it tells you what will be chosen if you click
    /// it ... make it much, much clearer". A row made here always shows the CURRENT state and every choice, never what a press
    /// would switch to; pressing a choice selects THAT choice.
    ///
    /// HOW TO USE (inside a page's row list; <c>set</c> = the page's "change and save" wrapper: ComfortWindow's Set, or the
    /// <c>Action&lt;Action&gt; set</c> handed to a SettingsRows method; it saves and redraws the page):
    /// <code>
    ///   // on / off (two choices, side by side, the current one lit):
    ///   rows.Add(SettingRow.Toggle(Lang.T("Camera shake"), Lang.T("Screen-shaking skills"), () => Comfort.CameraShake, v => set(() => Comfort.CameraShake = v)));
    ///   // named choices (2-3: side by side; 4 or more: a stepper "◄ ● current ►"):
    ///   rows.Add(SettingRow.Choice(Lang.T("Turning"), null, new[] { Lang.T("~turn~Smooth"), Lang.T("~turn~Snap") }, () => Comfort.SnapTurn ? 1 : 0, i => set(() => Comfort.SnapTurn = i == 1)));
    ///   // a number in even steps (a slider with − / + and its value shown): set = the live value, save = keep it
    ///   rows.Add(SettingRow.Slider(Lang.T("Music"), null, 0f, 1f, 0.1f, () => Comfort.MusicVolume, v => Comfort.MusicVolume = v, SettingRow.Percent, Comfort.Save));
    ///   // a number from a list of values (a slider over the list):
    ///   rows.Add(SettingRow.Steps(Lang.T("Snap angle"), null, new float[] { 30, 45, 60, 90 }, () => Comfort.SnapAngle, v => Comfort.SnapAngle = (int)v, v => v + "°", Comfort.Save));
    ///   // an action button (a verb: Show, Test, Reset, Place…):
    ///   rows.Add(SettingRow.Button(Lang.T("Controls tutorial"), null, Lang.T("Show"), () => ShowTutorial()));
    ///   // a line of information (no control):
    ///   rows.Add(SettingRow.Info(text));
    /// </code>
    /// Never label a button with a value (On / Off / Snap / Classic ...): values are choices. Names and explanations follow item 17:
    /// a short name; an explanation only where the name alone is unclear, at most ~6 words. The name is passed through Lang.T by
    /// the caller (check_strings finds it there); the row keeps its English text in <see cref="ListWindow.Row.Key"/> (tests find a
    /// row by it in either language: ComfortWindow.RowNamed).
    /// The look is the overhead menu's (UI/OverheadMenu.cs): the current choice gold with a dot and bold dark text, the others plain.
    /// </summary>
    public static class SettingRow
    {
        /// <summary>The most choices drawn side by side (each at least as large as a row button was before: 170 × 62 px); more
        /// become a stepper.</summary>
        public const int MaxSide = 3;
        /// <summary>The mark before the current choice (the overhead menu's).</summary>
        public const string Mark = "● ";

        /// <summary>A row's text: the setting's name, and under it its short explanation, dimmed (none: null or "").</summary>
        public static string Text(string name, string help) => string.IsNullOrEmpty(help) ? name : name + "\n<color=#9aa3b5>" + help + "</color>";

        /// <summary>Off / On side by side (or two other words for the two states: <paramref name="off"/>, <paramref name="on"/>).</summary>
        public static ListWindow.Row Toggle(string name, string help, Func<bool> get, Action<bool> set, string off = null, string on = null)
            => Choice(name, help, new[] { off ?? Lang.T("~switch~Off"), on ?? Lang.T("~switch~On") }, () => get() ? 1 : 0, i => set(i == 1));

        /// <summary>Named choices: up to <see cref="MaxSide"/> side by side, more as a stepper. <paramref name="get"/> reads the current
        /// choice's index; <paramref name="set"/> is called with the index pressed (not when it is the current one, unless
        /// <paramref name="repick"/>: the Language row, where pressing the language in use keeps it as your choice).</summary>
        public static ListWindow.Row Choice(string name, string help, string[] labels, Func<int> get, Action<int> set, bool repick = false)
        {
            var keys = new string[labels.Length];
            for (int i = 0; i < labels.Length; i++) keys[i] = Lang.SourceOf(labels[i]);
            return new ListWindow.Row
            {
                Text = Text(name, help), Key = Lang.SourceOf(name),
                Choices = labels, ChoiceKeys = keys, Chosen = get, OnChoose = set, Repick = repick, Stepper = labels.Length > MaxSide,
            };
        }

        /// <summary>A number in even steps from <paramref name="min"/> to <paramref name="max"/>: wave 11's slider under the name,
        /// "− value +" beside it. <paramref name="set"/> changes the live value (also while the knob is dragged), <paramref name="save"/>
        /// keeps it (after a drag, a step, a press of − / +).</summary>
        public static ListWindow.Row Slider(string name, string help, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> show, Action save)
        {
            // − / + add or take one step from the value as it is (a value off the grid, e.g. music 45 %, stays off it: 35 %, 55 % ...)
            void By(int dir) { float v = Mathf.Round(Mathf.Clamp(get() + dir * step, min, max) * 10000f) / 10000f; if (Mathf.Abs(v - get()) > 1e-6f) { set(v); save?.Invoke(); } }
            float now = get();
            return new ListWindow.Row
            {
                Text = Text(name, help), Key = Lang.SourceOf(name),
                Slider = new UiSlider.Spec { Min = min, Max = max, Step = step, Get = get, Set = set, Commit = save },
                Value = () => show(get()),
                A = "−", OnA = () => By(-1), AEnabled = now > min + step * 0.01f,
                B = "+", OnB = () => By(1), BEnabled = now < max - step * 0.01f,
            };
        }

        /// <summary>A number from a list of values (uneven steps: snap angles, render scales ...): a slider over the list.</summary>
        public static ListWindow.Row Steps(string name, string help, float[] values, Func<float> get, Action<float> set, Func<float, string> show, Action save)
        {
            int Index() { float v = get(); int best = 0; for (int i = 1; i < values.Length; i++) if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i; return best; }
            var row = Slider(name, help, 0f, values.Length - 1, 1f, () => Index(), i => set(values[Mathf.Clamp(Mathf.RoundToInt(i), 0, values.Length - 1)]), i => show(values[Mathf.Clamp(Mathf.RoundToInt(i), 0, values.Length - 1)]), save);
            row.StepValues = values;
            return row;
        }

        /// <summary>A switch with its own strength (the rumble kinds): Off / On side by side, a slider under the name with its value at
        /// its end. <paramref name="set"/> / <paramref name="save"/> as in <see cref="Slider"/>.</summary>
        public static ListWindow.Row ToggleSlider(string name, string help, Func<bool> getOn, Action<bool> setOn, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> show, Action save)
        {
            var row = Toggle(name, help, getOn, setOn);
            row.Slider = new UiSlider.Spec { Min = min, Max = max, Step = step, Get = get, Set = set, Commit = save };
            row.Value = () => show(get());
            return row;
        }

        /// <summary>An action: one or two buttons with a verb (Show, Test, Reset, Place…). Never a value.</summary>
        public static ListWindow.Row Button(string name, string help, string verb, Action act, string verb2 = null, Action act2 = null, bool enabled = true, bool enabled2 = true)
            => new ListWindow.Row { Text = Text(name, help), Key = Lang.SourceOf(name), A = verb, OnA = act, AEnabled = enabled, B = verb2, OnB = act2, BEnabled = enabled2, IsAction = true };

        /// <summary>A line with no control (a heading, a state, a table row).</summary>
        public static ListWindow.Row Info(string text, bool highlight = false) => new ListWindow.Row { Text = text, Highlight = highlight };

        /// <summary>0.65 → "65 %".</summary>
        public static string Percent(float v) => Mathf.RoundToInt(v * 100f) + " %";
    }
}
