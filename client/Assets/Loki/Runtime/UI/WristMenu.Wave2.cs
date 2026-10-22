using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 2: the wrist menu's fifth row of tabs (Chat rooms, Booking, Modes, Family). Each page is a hub like the Mail /
    /// Bank pages: a title, a summary the owning system refreshes, and buttons that open that system's window (a ListWindow in
    /// front of the head) or act at once. The game fills them in with <see cref="SetHub"/> once its systems exist.
    /// </summary>
    public sealed partial class WristMenu
    {
        public sealed class HubButton
        {
            public System.Func<string> Label; public System.Action Action; public System.Func<bool> Visible;
            public HubButton(string label, System.Action action, System.Func<bool> visible = null) { Label = () => Lang.Again(label); Action = action; Visible = visible; } // w4lang: a fixed label follows a change of language
            public HubButton(System.Func<string> label, System.Action action, System.Func<bool> visible = null) { Label = label; Action = action; Visible = visible; }
        }

        private sealed class Hub
        {
            public Text Title, Info; public System.Func<string> TitleFn, InfoFn;
            public readonly List<(UiButton b, HubButton h)> Buttons = new List<(UiButton, HubButton)>();
        }
        private readonly Dictionary<Tab, Hub> _hubs = new Dictionary<Tab, Hub>();
        /// <summary>An extra line on the Status page (spirit spheres, charms, souls, shields, a combo window).</summary>
        public System.Func<string> StatusExtra;

        private void BuildWave2Pages() { } // the pages stay empty until SetHub fills them (the systems are created after the wrist menu)

        /// <summary>Fills a wave-2 page: the title and summary are read on every refresh; buttons are laid out two per row from the
        /// bottom up (a hidden button leaves its place empty so the others don't jump around).</summary>
        public void SetHub(Tab t, System.Func<string> title, System.Func<string> info, params HubButton[] buttons)
        {
            if (!_pages.TryGetValue(t, out var p)) return;
            float top = PH / 2;
            var h = new Hub { TitleFn = title, InfoFn = info };
            h.Title = UiKit.Label(p, "", new Vector2(0, top - 30), new Vector2(W - 70, 40), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            int rows = (buttons.Length + 1) / 2;
            float infoH = PH - 80 - rows * 72 - 20;
            h.Info = UiKit.Label(p, "", new Vector2(0, top - 65 - infoH / 2), new Vector2(W - 70, infoH), 21, TextAnchor.UpperLeft);
            float bw = (W - 100) / 2f;
            for (int i = 0; i < buttons.Length; i++)
            {
                int row = i / 2, col = i % 2; var hb = buttons[i];
                float y = -top + 50 + (rows - 1 - row) * 72;
                float x = buttons.Length == 1 || (i == buttons.Length - 1 && buttons.Length % 2 == 1 && col == 0) ? 0 : (col == 0 ? -bw / 2 - 5 : bw / 2 + 5);
                float w = x == 0 ? W - 90 : bw;
                var b = UiKit.Button(p, hb.Label?.Invoke() ?? "", new Vector2(x, y), new Vector2(w, 62), () => { hb.Action?.Invoke(); _dirty = true; }, 21);
                h.Buttons.Add((b, hb));
            }
            _hubs[t] = h;
            _dirty = true;
        }

        private void RefreshWave2()
        {
            if (!_hubs.TryGetValue(_tab, out var h)) return;
            h.Title.text = h.TitleFn?.Invoke() ?? "";
            h.Info.text = h.InfoFn?.Invoke() ?? "";
            foreach (var (b, hb) in h.Buttons)
            {
                bool show = hb.Visible == null || hb.Visible();
                b.gameObject.SetActive(show);
                if (show) b.SetLabel(hb.Label?.Invoke() ?? "");
            }
        }

        /// <summary>Test hook: the page's current summary text.</summary>
        public string HubInfo(Tab t) => _hubs.TryGetValue(t, out var h) ? h.InfoFn?.Invoke() ?? "" : "";
    }
}
