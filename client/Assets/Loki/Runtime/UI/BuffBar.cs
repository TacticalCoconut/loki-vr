using System.Collections.Generic;
using System.IO;
using System.Linq;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// The status-icon bar: one 32-px icon per active effect on us (Progression.Effects, from 0x0983/0x0196/0x0984), with the
    /// remaining seconds under it and a coloured frame (the official bar's priority colour: gold, red = debuff, blue, green,
    /// white). Icons are data\texture\effect\&lt;file&gt;.tga; the file per EFST comes from data/stateicons.tsv (the client's
    /// stateiconimginfo.lub for EFST ≥ 70, its hard-coded list for 0-69 via roBrowser — research/qol_packets.md §4).
    /// Effects without an icon show their name. Floats just above the back of the left hand like a wristwatch, and moves
    /// up over the wrist menu while that is open.
    /// </summary>
    public sealed class BuffBar : MonoBehaviour
    {
        private const int Max = 12; private const float Cell = 64;
        private RectTransform _rt; private Transform _hand; private Camera _head; private Progression _prog; private System.Func<bool> _menuOpen;
        private readonly List<(RectTransform frame, Image icon, Text label, Text time)> _slots = new List<(RectTransform, Image, Text, Text)>();
        private static Dictionary<int, (string file, int colour)> _table;
        private static readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();
        private float _tick; private int _lastCount = -1;
        private static readonly Color[] Colours = { new Color(1f, 0.8f, 0.2f), new Color(1f, 0.35f, 0.3f), new Color(0.4f, 0.65f, 1f), new Color(0.4f, 0.9f, 0.4f), new Color(0.9f, 0.9f, 0.9f) };

        public static BuffBar Create(Transform parent, Transform leftHand, Camera head, Progression prog, System.Func<bool> menuOpen)
        {
            var rt = UiKit.Panel("BuffBar", parent, new Vector2(Max * Cell + 20, 100), 0.00042f, false);
            var b = rt.gameObject.AddComponent<BuffBar>();
            b._rt = rt; b._hand = leftHand; b._head = head; b._prog = prog; b._menuOpen = menuOpen;
            for (int i = 0; i < Max; i++)
            {
                float x = -(Max * Cell) / 2 + Cell / 2 + i * Cell;
                var frame = UiKit.Box(rt, "frame", new Vector2(x, 14), new Vector2(52, 52), new Color(0.9f, 0.9f, 0.9f, 0.9f));
                UiKit.Box(frame, "bg", Vector2.zero, new Vector2(46, 46), new Color(0.05f, 0.06f, 0.09f, 0.95f));
                var icon = UiKit.Icon(frame, Vector2.zero, new Vector2(44, 44));
                var label = UiKit.Label(frame, "", Vector2.zero, new Vector2(46, 46), 12, TextAnchor.MiddleCenter, HudLook.Text);
                var time = UiKit.Label(rt, "", new Vector2(x, -26), new Vector2(Cell, 24), 16, TextAnchor.MiddleCenter, HudLook.Heading); // (client wave 21, w21tools: the theme's lettering and its outline; the frames keep the status colours)
                var o = time.gameObject.AddComponent<Outline>(); o.effectColor = HudLook.Outline; o.effectDistance = new Vector2(1, -1);
                frame.gameObject.SetActive(false); time.gameObject.SetActive(false);
                b._slots.Add((frame, icon, label, time));
            }
            prog.Changed += () => b._lastCount = -1;
            return b;
        }

        private static void LoadTable()
        {
            if (_table != null) return;
            _table = new Dictionary<int, (string, int)>();
            try
            {
                var latin1 = System.Text.Encoding.GetEncoding(28591);
                foreach (var line in File.ReadLines(Loki.Data.ClientData.File("stateicons.tsv")))
                {
                    var c = line.Split('\t');
                    if (c.Length < 3 || !int.TryParse(c[0], out int id)) continue;
                    var bytes = new byte[c[1].Length / 2];
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = System.Convert.ToByte(c[1].Substring(2 * i, 2), 16);
                    _table[id] = (latin1.GetString(bytes), int.TryParse(c[2], out var col) ? col : 4);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[BuffBar] stateicons: " + e.Message); }
        }

        /// <summary>Icon sprite for an EFST id (null when the client has none).</summary>
        public static Sprite IconOf(int efst)
        {
            LoadTable();
            if (_sprites.TryGetValue(efst, out var s)) return s;
            s = null;
            var a = LokiBootstrap.Instance != null ? LokiBootstrap.Instance.Assets : null;
            if (a != null && _table.TryGetValue(efst, out var t))
            {
                var tex = a.Texture("effect\\" + t.file, false);
                if (tex != a.MissingTexture) s = UiKit.ToSprite(tex);
            }
            _sprites[efst] = s;
            return s;
        }
        public static int ColourOf(int efst) { LoadTable(); return _table.TryGetValue(efst, out var t) ? t.colour : 4; }
        public static bool HasIcon(int efst) { LoadTable(); return _table.ContainsKey(efst); }

        /// <summary>Shown effects in bar order (debuffs first, then by id). For the smoke test.</summary>
        public List<int> Shown { get; } = new List<int>();

        /// <summary>Re-attach to another hand (left-handed mode).</summary>
        public void SetHand(Transform hand) { _hand = hand; }

        private void LateUpdate()
        {
            if (_hand != null && _head != null)
            {
                var toHead = _head.transform.position - _hand.position; toHead.y = 0; if (toHead.sqrMagnitude > 1e-4f) toHead.Normalize();
                bool open = _menuOpen != null && _menuOpen();
                transform.position = _hand.position + Vector3.up * (open ? 0.43f + WristMenu.ClassicGrewTop : 0.07f) + toHead * 0.03f; // client wave 11: the wrist menu's top edge is higher (25 % larger)
                UiKit.FaceHead(transform, _head);
            }
            _tick += Time.deltaTime;
            if (_lastCount == _prog.Effects.Count && _tick < 0.5f) return;
            _tick = 0; _lastCount = _prog.Effects.Count;
            var list = _prog.Effects.Values.Where(e => e.Listed || HasIcon(e.Type)).OrderBy(e => e.Kind == "debuff" ? 0 : 1).ThenBy(e => e.Type).Take(Max).ToList();
            Shown.Clear();
            for (int i = 0; i < Max; i++)
            {
                var (frame, icon, label, time) = _slots[i];
                bool show = i < list.Count;
                frame.gameObject.SetActive(show); time.gameObject.SetActive(show);
                if (!show) continue;
                var e = list[i]; Shown.Add(e.Type);
                var sp = IconOf(e.Type);
                icon.sprite = sp; icon.enabled = sp != null;
                label.text = sp == null ? e.Name : "";
                int col = e.Kind == "debuff" ? 1 : ColourOf(e.Type);
                frame.GetComponent<Image>().color = Colours[Mathf.Clamp(col, 0, 4)];
                time.text = e.Timed ? Fmt(e.EndsAt - Time.time) : "";
            }
        }

        private static string Fmt(float s)
        {
            if (s < 0) s = 0;
            if (s >= 3600) return $"{(int)(s / 3600)}h";
            if (s >= 90) return $"{(int)(s / 60)}m";
            return $"{Mathf.CeilToInt(s)}s";
        }
    }
}
