using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Loki
{
    /// <summary>
    /// Client wave 4b (w4lang): the language of every text the client writes itself — Brazilian Portuguese or English.
    ///
    /// One string table: the English source text is written in the code, wrapped in <see cref="T"/> (a fixed text) or <see cref="F"/>
    /// (a whole sentence with {0} {1} … placeholders, string.Format rules); its Brazilian Portuguese translation is the line with the
    /// same English text in <c>Assets/Loki/Resources/Lang/pt-BR.txt</c> (tab-separated: English, Portuguese; \n \t \\ escaped; lines
    /// starting with # are comments). A text with no Portuguese line shows in English and is logged once ("[Lang] no Portuguese
    /// for …"); <c>client/tools/lang/check_strings.py</c> lists every T / F text of the code against the table.
    ///
    /// Game data text (item, skill, map and monster names, NPC dialog, the msgstringtable) comes from the player's own GRF and is
    /// shown as it is; where the GRF holds both languages (msgstringtable, map names, the world map, achievements, navigation),
    /// the language setting picks the column (<see cref="Game.ClientText.Portuguese"/>).
    ///
    /// The setting is per Windows user (World/UserSettings.cs <c>settings.language</c>, chosen in the Settings window; "" until a
    /// player chose one): the first start follows Windows' UI language (Portuguese for pt-*, else English: Unity's
    /// Application.systemLanguage, which reads GetUserDefaultUILanguage on Windows). Changing it relabels every fixed text on the
    /// open panels at once (<see cref="LangText"/>); lists and pages show the new language when they are next drawn.
    /// Test arguments: <c>-lokiLang en|pt-BR</c> (this run only, nothing saved) and <c>-lokiSystemLang &lt;culture&gt;</c> (stands in
    /// for Windows' UI language) — smoke-only, like every <c>-loki*</c> test argument.
    /// Nothing here is rAthena-specific.
    /// </summary>
    public static class Lang
    {
        public const string En = "en", Pt = "pt-BR";
        /// <summary>Wave 28 (wtextc), design note 2026-10-15: "English-only UI text policy." English is the only language:
        /// a saved Portuguese choice, Windows' Portuguese, the account's setting and -lokiLang pt-BR are all read as English, the
        /// language rows (Settings, the login screen's button) are not shown, and every game-data loader picks its English text (never
        /// a Portuguese row). The Portuguese table (Resources/Lang/pt-BR.txt) and every Portuguese code path stay in the repo, unused:
        /// false here undoes it. (static readonly, not const: the Portuguese branches stay compiled without unreachable-code warnings)</summary>
        public static readonly bool EnglishOnly = true;
        /// <summary>"en" or "pt-BR".</summary>
        public static string Code { get; private set; } = En;
        public static bool Portuguese => Code == Pt;
        /// <summary>Why this language: "your choice", "Windows' UI language (…)" or "-lokiLang" (logged, shown in Settings).</summary>
        public static string Source { get; private set; } = "";
        /// <summary>True while the language follows Windows (no choice saved yet).</summary>
        public static bool FromWindows { get; private set; }
        /// <summary>Raised after a change of language (the fixed texts are already relabelled).</summary>
        public static event Action Changed;

        private static Dictionary<string, string> _pt;      // English → Portuguese
        private static Dictionary<string, string> _rev;     // Portuguese → English (first English text wins)
        private static readonly HashSet<string> _missing = new HashSet<string>();
        private static bool _inited;
        private static CultureInfo _ptCulture;

        /// <summary>Texts asked for in Portuguese that the table does not have (each logged once).</summary>
        public static IEnumerable<string> Missing => _missing;
        public static int MissingCount => _missing.Count;
        /// <summary>Lines in the Portuguese table.</summary>
        public static int TableCount { get { Load(); return _pt.Count; } }

        /// <summary>Picks the language once per process (LokiBootstrap.Awake, before any panel).</summary>
        public static void Init()
        {
            if (_inited) return; _inited = true;
            Load();
            string saved = null;
            try { saved = World.UserSettings.Prefs.language; } catch (Exception e) { Debug.LogWarning("[Lang] settings: " + e.Message); }
            if (!string.IsNullOrEmpty(saved)) { Code = Normalize(saved); Source = "your choice"; FromWindows = false; }
            else
            {
                string sys = SystemLanguage(out string how);
                Code = Normalize(sys); // (wave 28: English only reads Windows' Portuguese as English too)
                Source = $"Windows' UI language ({how})"; FromWindows = true;
            }
            if (EnglishOnly) { if (!string.IsNullOrEmpty(saved) && saved != En) Source += $" (saved '{saved}' read as English)"; Source = "English only (wave 28); was: " + Source; FromWindows = false; }
            Game.ClientText.Portuguese = Portuguese;
            Debug.Log($"[Lang] language {Code} from {Source}; {_pt.Count} Portuguese strings in the table (unused while English only: {EnglishOnly}); settings file {World.UserSettings.FilePath}");
        }

        /// <summary>Windows' UI language as a culture name ("pt-BR", "en-US", …): <c>-lokiSystemLang</c> in tests, else
        /// Application.systemLanguage (Portuguese → "pt"), else CultureInfo.CurrentUICulture.</summary>
        public static string SystemLanguage(out string how)
        {
            string ui = "";
            try { ui = CultureInfo.CurrentUICulture.Name; } catch { }
            var sl = Application.systemLanguage;
            how = $"systemLanguage {sl}, UI culture '{ui}'";
            if (sl == UnityEngine.SystemLanguage.Portuguese) return "pt";
            if (sl == UnityEngine.SystemLanguage.Unknown && ui.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return ui;
            return sl == UnityEngine.SystemLanguage.Unknown && ui.Length > 0 ? ui : "en";
        }

        private static string Normalize(string s) => !EnglishOnly && s != null && s.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? Pt : En;

        /// <summary>The player's choice (Settings window): saved in loki-user.json, the fixed texts relabelled, then <see cref="Changed"/>.
        /// Wave 28: while <see cref="EnglishOnly"/>, a choice is read as English and, the game being English already, nothing is saved
        /// (the player's file keeps what it holds).</summary>
        public static void Set(string code)
        {
            Init();
            if (EnglishOnly && Code == En) { Debug.Log($"[Lang] language '{code}' asked: read as English (English only); nothing saved"); return; }
            string c = Normalize(code);
            bool changed = c != Code;
            Code = c; Source = "your choice"; FromWindows = false;
            Game.ClientText.Portuguese = Portuguese;
            World.UserSettings.Prefs.language = c;
            VR.Comfort.Load(); // (the rig loaded it already: Save writes every Comfort value back, so they must be the player's)
            VR.Comfort.Save(); // writes loki-user.json (every setting as shown) and tells the open Settings window to redraw
            Debug.Log($"[Lang] language set to {c} (saved in {World.UserSettings.FilePath}); changed {changed}");
            if (!changed) return;
            Relabel();
            try { Changed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>A scene reload drops the listeners of the old scene (as Comfort.ClearListeners).</summary>
        public static void ClearListeners() => Changed = null;

        // ---- lookups

        /// <summary>The text in the current language (the English source when English, or when the table has no line for it).
        /// A text whose English is the same as another's but whose meaning differs starts with a context: "~key~Back" (the keyboard's
        /// backspace) is "Back" in English and has its own Portuguese line.</summary>
        public static string T(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            string shown;
            if (Code == En) shown = Plain(en);
            else
            {
                Load();
                if (!_pt.TryGetValue(en, out shown))
                {
                    if (_missing.Add(en)) Debug.LogWarning("[Lang] no Portuguese for \"" + Escape(en) + "\"");
                    shown = Plain(en);
                }
            }
            if (_recent.Count > 20000) _recent.Clear();
            _recent[shown] = en; // the text a label is given right after this call comes from this key (Track, SourceOf)
            return shown;
        }
        private static readonly Dictionary<string, string> _recent = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>A no-op marker: the English text of a table line that the code translates later from a variable
        /// (<c>Lang.T(names[i])</c>); check_strings.py collects these.</summary>
        public static string N(string en) => en;

        /// <summary>The key's text in the current language (nothing recorded, nothing logged).</summary>
        private static string Current(string key) => Code == En ? Plain(key) : _pt != null && _pt.TryGetValue(key, out var pt) ? pt : Plain(key);

        /// <summary>"~ctx~Text" → "Text".</summary>
        public static string Plain(string key)
        {
            if (key == null || key.Length < 3 || key[0] != '~') return key;
            int j = key.IndexOf('~', 1);
            return j > 0 ? key.Substring(j + 1) : key;
        }

        /// <summary>A whole sentence with placeholders: string.Format of the current language's text, numbers written the
        /// language's way (Portuguese: 1.234,5).</summary>
        public static string F(string en, params object[] args)
        {
            string fmt = T(en);
            try { return string.Format(Culture, fmt, args); }
            catch (FormatException e)
            {
                Debug.LogWarning("[Lang] bad format \"" + Escape(fmt) + "\": " + e.Message);
                try { return string.Format(CultureInfo.CurrentCulture, en, args); } catch { return en; }
            }
        }

        /// <summary>Numbers in the current language (English keeps the culture the client always used).</summary>
        public static IFormatProvider Culture
        {
            get
            {
                if (!Portuguese) return CultureInfo.CurrentCulture;
                if (_ptCulture == null)
                {
                    try { _ptCulture = CultureInfo.GetCultureInfo("pt-BR"); }
                    catch
                    {
                        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                        c.NumberFormat.NumberDecimalSeparator = ","; c.NumberFormat.NumberGroupSeparator = ".";
                        c.NumberFormat.PercentDecimalSeparator = ","; c.NumberFormat.PercentGroupSeparator = ".";
                        _ptCulture = c;
                    }
                }
                return _ptCulture;
            }
        }

        /// <summary>The English text of a text shown in either language (the text itself when it is not a table text): button
        /// objects keep the name "btn:&lt;English&gt;" in both languages, and tests press buttons by their English label.</summary>
        public static string SourceOf(string shown) => Plain(KeyOf(shown) ?? shown);

        /// <summary>A known text (English or Portuguese) again in the current language; anything else unchanged (placeholders
        /// kept in a field, e.g. VrTextField.Placeholder).</summary>
        public static string Again(string shown) { var k = KeyOf(shown); return k != null ? T(k) : shown; }

        /// <summary>The table key of a text shown in either language, or null: the key it was last translated from (so "~key~Back"
        /// and "Back" stay apart), else the Portuguese table read backwards, else the text itself when it is a table key. (A text
        /// made in the other language before a change of language is found too: <see cref="Again"/> relies on it.)</summary>
        public static string KeyOf(string shown)
        {
            if (string.IsNullOrEmpty(shown)) return null;
            Load();
            if (_recent.TryGetValue(shown, out var k) && (Plain(k) == shown || (_pt.TryGetValue(k, out var ptk) && ptk == shown))) return k;
            if (_rev.TryGetValue(shown, out var en)) return en;
            return _pt.ContainsKey(shown) ? shown : null;
        }

        /// <summary>UiKit.Label / UiButton.SetLabel: a fixed text gets a <see cref="LangText"/> so a change of language relabels it.</summary>
        public static void Track(Text t)
        {
            if (t == null) return;
            string key = KeyOf(t.text);
            var lt = t.GetComponent<LangText>();
            if (key == null) { if (lt != null) lt.Key = null; return; }
            if (lt == null) lt = t.gameObject.AddComponent<LangText>();
            lt.Key = key; lt.Shown = t.text;
        }

        /// <summary>Every fixed text of the scene (active or not) in the current language; text fields redraw their placeholder.</summary>
        public static int Relabel()
        {
            int n = 0;
            foreach (var lt in UnityEngine.Object.FindObjectsOfType<LangText>(true)) if (lt.Apply()) n++;
            foreach (var f in UnityEngine.Object.FindObjectsOfType<UI.VrTextField>(true)) f.Relabel();
            Debug.Log($"[Lang] relabelled {n} fixed texts in {Code}");
            return n;
        }

        // ---- the table

        private static void Load()
        {
            if (_pt != null) return;
            _pt = new Dictionary<string, string>(StringComparer.Ordinal);
            _rev = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var ta = Resources.Load<TextAsset>("Lang/pt-BR");
                if (ta == null) { Debug.LogWarning("[Lang] Resources/Lang/pt-BR missing: every text stays English"); return; }
                int lineNo = 0, dup = 0;
                foreach (var raw in ta.text.Split('\n'))
                {
                    lineNo++;
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == '#') continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) { Debug.LogWarning($"[Lang] pt-BR line {lineNo}: no tab"); continue; }
                    string en = Unescape(line.Substring(0, tab)), pt = Unescape(line.Substring(tab + 1));
                    if (pt.Length == 0) continue;
                    if (_pt.ContainsKey(en)) { dup++; continue; }
                    _pt[en] = pt;
                    if (!_rev.ContainsKey(pt)) _rev[pt] = en;
                }
                if (dup > 0) Debug.LogWarning($"[Lang] pt-BR: {dup} repeated English texts (the first line kept)");
            }
            catch (Exception e) { Debug.LogWarning("[Lang] table: " + e.Message); }
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char d = s[++i];
                    sb.Append(d == 'n' ? '\n' : d == 't' ? '\t' : d == 'r' ? '\r' : d);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Escape(string s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");

        private static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }
    }

    /// <summary>A fixed text of a panel (a table text when it was drawn): relabelled when the language changes, unless the owner
    /// wrote another text into it since.</summary>
    [DisallowMultipleComponent]
    public sealed class LangText : MonoBehaviour
    {
        public string Key, Shown;
        private Text _t;
        public bool Apply()
        {
            if (string.IsNullOrEmpty(Key)) return false;
            if (_t == null) _t = GetComponent<Text>();
            if (_t == null || _t.text != Shown) { Key = null; return false; }
            Shown = Lang.T(Key); _t.text = Shown;
            return true;
        }
    }
}
