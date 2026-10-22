using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 13 (w13look), deploy step 3: what the LOOK may choose ("sight selection", "choose by looking": the calibrated gaze
    /// ray of client wave 11, VR/DioramaRig.Ui.cs GazeRay). design note, 2026-10-07 ~17:15: "add a toggle on the overhead menu that turns sight
    /// selection completely on or off. At the bottom of every panel, there should be a tiny button that toggles whether or not that
    /// panel should be a valid choice for sight selection. THIS IS REMEMBERED ... Turning the sight selection completely on or off does
    /// NOT change this parameter on any panel".
    ///   THE BASELINE (item 23) "Look select: on / off" (the overhead menu and Settings → Comfort, one stored value, per player:
    ///             loki-user.json lookSelect, default on). ONE gate with two parts (<see cref="UiOn"/>, <see cref="WorldOn"/>): off =
    ///             no panel button is chosen by the look (VR/DioramaRig.Ui.cs Choosable) and the look locks no unit and gives no
    ///             ground cell (Game/Targeting.cs), the look ring (the dot where the look lands) is hidden and "Wand aim: look" aims
    ///             with the wand (LokiGame.W13Look.cs W13AimLook). The laser does everything. if desired only the UI part: set
    ///             <see cref="OffStopsWorld"/> to false (ONE line). While it is off the look on a panel no longer holds back a laser
    ///             pull on the world either (the coordinator's reading: the look takes no part at all; VR/DioramaRig.cs).
    ///   PER PANEL (item 24) every panel the player opens carries a small eye button at its bottom (<see cref="LookEye"/>): it switches
    ///             THAT panel kind out of the look's choices (crossed eye) or back in. Remembered per panel kind (<see cref="KeyOf"/>: a
    ///             stable key per panel kind: the window's name, "Menu:&lt;group&gt;" for a board or the classic wrist menu's page group,
    ///             "NpcDialog", "Chat") in loki-user.json lookOff (the keys switched out), default: every panel valid. An excluded panel
    ///             STILL BLOCKS the look (the design's standing rule: every panel with a background blocks gaze selection): the look on it
    ///             chooses nothing there and reaches nothing behind it. Its eye is pressed by the laser always, by the look only while
    ///             the panel is a valid choice. The baseline never changes these choices: off, nothing is chosen anywhere; on again,
    ///             each panel's own choice applies as it was.
    ///   THE MINIMAP (item 22) the see-through minimap's colliders are on <see cref="LaserOnlyLayer"/>: the laser's ray meets them
    ///             (it takes every layer: VR/DioramaRig.cs), the look's rays never do (<see cref="GazeMask"/>: VR/DioramaRig.Ui.cs
    ///             RayOnUi and the look ring's ray), so the look neither stops on it nor is blocked by it.
    /// </summary>
    public static class LookSelect
    {
        /// <summary>Item 22: the layer of colliders only the laser meets (an unnamed user layer: nothing else uses 30; World/SpectatorView.cs
        /// leaves it as it is when it moves the panels' parts onto the UI layer).</summary>
        public const int LaserOnlyLayer = 30;
        /// <summary>The layers every ray of the look takes: all but <see cref="LaserOnlyLayer"/>.</summary>
        public const int GazeMask = ~(1 << LaserOnlyLayer);

        /// <summary>Item 23: what "Look select: off" switches off: the UI part (panel buttons) and the world part (units, the ground cell).
        /// THE ONE LINE to change if desired only the UI part: OffStopsWorld = false.</summary>
        public const bool OffStopsUi = true, OffStopsWorld = true;

        private static bool _loaded, _on = true;
        private static readonly HashSet<string> _off = new HashSet<string>(StringComparer.Ordinal);
        private static void Load()
        {
            if (_loaded) return; _loaded = true;
            var s = World.UserSettings.Prefs; _on = s.lookSelect; _off.Clear();
            if (s.lookOff != null) foreach (var k in s.lookOff) if (!string.IsNullOrEmpty(k)) _off.Add(k);
        }
        /// <summary>Tests: read the file's values again (after UserSettings.Reload()).</summary>
        public static void Reload() { _loaded = false; }

        /// <summary>The baseline: the look may choose (true, the default) or nothing is chosen by looking.</summary>
        public static bool On { get { Load(); return _on; } }
        /// <summary>The look may press panel buttons now (the baseline's UI part).</summary>
        public static bool UiOn => !OffStopsUi || On;
        /// <summary>The look may lock units and give the ground cell now (the baseline's world part).</summary>
        public static bool WorldOn => !OffStopsWorld || On;
        /// <summary>The baseline changed: its new value, and what changed it (the game shows a note).</summary>
        public static event Action<bool, string> Changed;
        /// <summary>A panel kind was switched out of the look's choices (true) or back in: its key, the new state, what did it.</summary>
        public static event Action<string, bool, string> PanelChanged;
        /// <summary>Baseline changes and panel choices made (smoke, log).</summary>
        public static int Switches { get; private set; }
        public static int PanelSwitches { get; private set; }

        /// <summary>The baseline on / off (the overhead menu, the Settings row): saved per player; the panels' own choices are not touched.</summary>
        public static void SetOn(bool on, string why)
        {
            Load(); if (_on == on) return;
            _on = on; Switches++;
            World.UserSettings.Prefs.lookSelect = on;
            Debug.Log($"[Look] look select {(on ? "ON" : "off")} ({why}); the panels switched out stay as they are: {(_off.Count > 0 ? string.Join(", ", ExcludedKeys) : "none")}");
            VR.Comfort.Save(); // (writes loki-user.json; the Settings page redraws)
            Changed?.Invoke(on, why);
        }

        // ------------------------------------------------------------------ item 24: per panel kind
        private static readonly Dictionary<Transform, Func<string>> _keys = new Dictionary<Transform, Func<string>>();
        /// <summary>Every eye made (smoke, log).</summary>
        public static readonly List<LookEye> Eyes = new List<LookEye>();

        /// <summary>The panel whose root is <paramref name="panel"/> has this key now (a board's changes with its group).</summary>
        public static void Register(Transform panel, Func<string> key) { if (panel != null && key != null) _keys[panel] = key; }
        /// <summary>The panel kind's key of a panel root (null: a panel without an eye: always a valid choice).</summary>
        public static string KeyOf(Transform panel)
        {
            if (panel == null || !_keys.TryGetValue(panel, out var f)) return null;
            try { return f(); } catch (Exception e) { Debug.LogException(e); return null; }
        }
        /// <summary>This panel kind is switched out of the look's choices.</summary>
        public static bool Excluded(string key) { Load(); return key != null && _off.Contains(key); }
        /// <summary>The look may choose on this panel now: the baseline's UI part and the panel's own choice (null: no panel).</summary>
        public static bool PanelChoosable(Transform panel) => UiOn && !Excluded(KeyOf(panel));

        /// <summary>A panel kind out of the look's choices (true) or back in; saved per player at once.</summary>
        public static void SetExcluded(string key, bool off, string why)
        {
            if (string.IsNullOrEmpty(key)) return;
            Load(); bool was = _off.Contains(key); if (was == off) return;
            if (off) _off.Add(key); else _off.Remove(key);
            PanelSwitches++;
            World.UserSettings.Prefs.lookOff = ExcludedKeys;
            Debug.Log($"[Look] the panel '{key}' {(off ? "out of" : "back in")} the look's choices ({why}); out now: {(_off.Count > 0 ? string.Join(", ", ExcludedKeys) : "none")}");
            VR.Comfort.Save();
            PanelChanged?.Invoke(key, off, why);
        }
        /// <summary>The keys switched out, sorted (the stored list).</summary>
        public static string[] ExcludedKeys { get { Load(); var a = new string[_off.Count]; _off.CopyTo(a); Array.Sort(a, StringComparer.Ordinal); return a; } }
    }

    /// <summary>
    /// Client wave 13 (w13look), item 24: the eye at the bottom of a panel. Pressed (the laser always; the look only while the panel is
    /// a valid choice), it switches the panel's kind out of the look's choices or back in (<see cref="LookSelect.SetExcluded"/>).
    /// Drawn as an eye on a dark button: gold while the look may choose there, grey and crossed in red while it may not. Its size and
    /// place are each panel's (in that panel's pixels; at least <see cref="MinDeg"/> on its smaller side from the panel's usual distance,
    /// wave 11's smallest button, and <see cref="RimCm"/> inside the panel's rim, clear of item 19's edge band: w13look.md lists them).
    /// Its collider is off while the panel's canvas is not drawn (the chat hidden, the classic wrist menu while the boards are used).
    /// </summary>
    public sealed class LookEye : MonoBehaviour
    {
        public const float MinDeg = 2.5f, RimCm = 1.5f;
        public static readonly Color GlyphOn = new Color(1f, 0.86f, 0.45f, 1f), GlyphOff = new Color(0.55f, 0.57f, 0.62f, 0.85f), BgOff = new Color(0.10f, 0.10f, 0.13f, 1f), Cross = new Color(1f, 0.32f, 0.26f, 0.95f);
        public UiButton Button { get; private set; }
        public RectTransform Panel { get; private set; }
        public float SizePx { get; private set; }
        public string Key => _key != null ? _key() : null;
        /// <summary>Drawn crossed now (the panel is out of the look's choices).</summary>
        public bool DrawnOff { get; private set; }
        /// <summary>Presses (smoke, log).</summary>
        public int Presses { get; private set; }
        private Func<string> _key; private Image _glyph; private GameObject _cross; private Canvas _canvas; private BoxCollider _col; private bool _painted;

        /// <summary>An eye on <paramref name="panel"/> (a UiKit.Panel root) at <paramref name="pos"/> (the panel's pixels, from its middle),
        /// <paramref name="sizePx"/> square; the panel is registered with its key.</summary>
        public static LookEye Add(RectTransform panel, Func<string> key, Vector2 pos, float sizePx)
        {
            LookSelect.Register(panel, key);
            var b = UiKit.Button(panel, "", pos, new Vector2(sizePx, sizePx), null, 20);
            b.name = "btn:LookEye"; b.Priority = 1; // (the world map's picture is a button under it)
            var e = b.gameObject.AddComponent<LookEye>();
            e.Button = b; e.Panel = panel; e.SizePx = sizePx; e._key = key; e._canvas = panel.GetComponent<Canvas>(); e._col = b.GetComponent<BoxCollider>();
            var rt = (RectTransform)b.transform;
            e._glyph = UiKit.Icon(rt, Vector2.zero, new Vector2(sizePx * 0.86f, sizePx * 0.86f));
            e._glyph.sprite = EyeSprite; e._glyph.preserveAspect = true; e._glyph.enabled = true;
            var x = UiKit.Box(rt, "cross", Vector2.zero, new Vector2(sizePx * 1.02f, Mathf.Max(3f, sizePx * 0.11f)), Cross);
            x.localRotation = Quaternion.Euler(0f, 0f, -38f); e._cross = x.gameObject; e._cross.SetActive(false);
            b.SetAction(e.Press);
            // client wave 14 (w14menu), point 7 (design note: "When I mouse over the I symbol that turns vision select on and off, just have a pop-up that
            // describes what that button does"): its tip after the laser rests on it (UI/ButtonTip.cs), with the state it is in
            b.Tip = () => LookSelect.Excluded(e.Key) ? Lang.T("Look select here: off") : Lang.T("Look select here: on");
            e.Paint(true);
            if (LookSelect.Eyes.Count >= 64) LookSelect.Eyes.RemoveAll(y => y == null);
            LookSelect.Eyes.Add(e);
            return e;
        }

        private void Press()
        {
            var k = Key; if (string.IsNullOrEmpty(k)) return;
            Presses++;
            LookSelect.SetExcluded(k, !LookSelect.Excluded(k), "its eye");
            Paint(true);
        }

        private void Paint(bool force)
        {
            bool off = LookSelect.Excluded(Key);
            if (!force && _painted && off == DrawnOff) return;
            _painted = true; DrawnOff = off;
            // client wave 21 (w21kit): on the theme's canvas the eye is dark ink on a plain button (faint and crossed while off)
            if (Button.IsThemed) { Button.Normal = off ? Theme.CellOff : Theme.Cell; Button.Hover = Theme.CellHover; _glyph.color = off ? Theme.InkFaint : Theme.Ink; }
            else { Button.Normal = off ? BgOff : UiKit.ButtonBg; Button.Hover = UiKit.ButtonHover; _glyph.color = off ? GlyphOff : GlyphOn; }
            if (_cross.activeSelf != off) _cross.SetActive(off);
            Button.SetHover(Button.Hovered);
        }

        private void LateUpdate()
        {
            Paint(false);
            bool drawn = _canvas == null || _canvas.enabled;
            if (_col != null && _col.enabled != drawn) _col.enabled = drawn;
        }

        // ---- the picture: an eye (a lens outline, an iris ring), white, tinted by the Image's colour
        private static Sprite _sprite;
        public static Sprite EyeSprite
        {
            get
            {
                if (_sprite != null) return _sprite;
                const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "LookEye", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                // the lens: inside two circles through (±1, 0), their apexes at ±0.55 (c = 0.634, R = 1.184); a stroke of 0.13 along its rim;
                // the iris a ring from 0.15 to 0.36 inside it; edges smoothed over about one and a half pixels
                const float c = 0.634f, R = 1.184f, stroke = 0.13f, irisOut = 0.36f, irisIn = 0.15f, soft = 1.5f * 2f / n;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float lens = Mathf.Max(new Vector2(u, v + c).magnitude - R, new Vector2(u, v - c).magnitude - R); // < 0 inside
                        float rim = Mathf.Abs(lens + stroke * 0.5f) - stroke * 0.5f;
                        float r = new Vector2(u, v).magnitude, iris = Mathf.Max(r - irisOut, irisIn - r);
                        float a = Mathf.Max(Cover(rim, soft), Mathf.Min(Cover(iris, soft), Cover(lens, soft)));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                tex.SetPixels32(px); tex.Apply(false, true);
                _sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                return _sprite;
            }
        }
        private static float Cover(float d, float soft) => Mathf.Clamp01(0.5f - d / soft);
    }
}
