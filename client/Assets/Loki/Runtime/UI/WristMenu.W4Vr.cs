using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 4 (w4vr): the wrist Quick tab follows the forearm bar's row (Game/Hotkeys.cs: 2 tabs × 4 rows of 9) — a row switcher
    /// on the page (▲ / ▼, the same as the forearm bar's) and each slot's cooldown seconds (0x043d and the global after-cast delay,
    /// UI/QuickBar.cs Blocked). Hooks in WristMenu.cs: BuildQuick, the Quick case of Refresh, LateUpdate.
    /// </summary>
    public sealed partial class WristMenu
    {
        private Text _w4RowLabel;
        /// <summary>The forearm bar (its cooldown bookkeeping is reused for the seconds shown here).</summary>
        public QuickBar QuickBarRef;

        private void BuildW4VrQuick(RectTransform p, float top)
        {
            _w4RowLabel = UiKit.Label(p, "", new Vector2(W / 2 - 250, top - 30), new Vector2(170, 40), 20, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.45f));
            UiKit.Button(p, "▲", new Vector2(W / 2 - 130, top - 30), new Vector2(64, 44), () => { Hotkeys?.SwitchRow(-1); _dirty = true; }, 20).Tip = () => Lang.T("Previous row"); // (client wave 14, point 7: their tips)
            UiKit.Button(p, "▼", new Vector2(W / 2 - 60, top - 30), new Vector2(64, 44), () => { Hotkeys?.SwitchRow(1); _dirty = true; }, 20).Tip = () => Lang.T("Next row");
        }

        private void RefreshW4VrQuick()
        {
            if (Hotkeys == null) return;
            if (_w4RowLabel != null) _w4RowLabel.text = Lang.F("Bar {0} · row {1}", Hotkeys.CurrentTab + 1, Hotkeys.CurrentRowInTab + 1);
            if (QuickBarRef == null) return;
            for (int i = 0; i < _quickRows.Count; i++)
            {
                var (left, _) = QuickBarRef.Blocked(i);
                if (left > 0.05f) _quickRows[i].label.text += $"  <color=#ff9a7a>{Mathf.CeilToInt(left)} s</color>";
            }
        }

        /// <summary>While a quick-tab skill cools down, refresh once a second (the seconds count down).</summary>
        private void W4VrQuickTick()
        {
            if (_tab != Tab.Quick || QuickBarRef == null) return;
            bool any = false; for (int i = 0; i < _quickRows.Count && !any; i++) any = QuickBarRef.Blocked(i).left > 0.05f;
            if (any) { _w4Tick += Time.deltaTime; if (_w4Tick > 0.5f) { _w4Tick = 0; _dirty = true; } }
        }
        private float _w4Tick;

    }
}
