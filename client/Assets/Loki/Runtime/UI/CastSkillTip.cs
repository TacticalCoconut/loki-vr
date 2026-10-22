using System.Text;
using Loki.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// Non-obstructing 1-second hover information tab for all casting modes and sub-widgets:
    /// Wand Wheel, Casting Pad, Casting Flick, Wand Strokes, Forearm Quick Bar ("the arm"),
    /// Top Quick Use Bubbles, and Trigger-Hold Radial Skill Wheels.
    /// Built without colliders or UiBlockers so it never blocks laser rays, gaze selection, or casting controls.
    /// </summary>
    public sealed class CastSkillTip : MonoBehaviour
    {
        public const float HoverDelaySeconds = 1.0f;
        public const float HoverDelay = 1.0f;
        public const float W = 460f, H = 276f, Scale = 0.00028f;

        private RectTransform _rt;
        private Camera _head;
        private Image _icon;
        private Text _title, _meta, _desc;
        private CanvasGroup _group;
        private int _shownId = -1;
        private bool _shownItem;
        private int _shownLv = -1;
        private float _popT;

        public bool Shown => _rt != null && _rt.gameObject.activeSelf;
        public int SkillIdShown => Shown && !_shownItem ? _shownId : 0;
        public int ItemIdShown => Shown && _shownItem ? _shownId : 0;

        public static CastSkillTip Create(Transform parent, Camera head)
        {
            var rt = UiKit.Panel("CastSkillTip", parent, new Vector2(W, H), Scale, true, PanelKind.Own, PanelLook.SeeThrough);
            HudLook.Backing(rt);
            var col = rt.GetComponent<BoxCollider>();
            if (col != null) Destroy(col);
            var blk = rt.GetComponent<UiBlocker>();
            if (blk != null) Destroy(blk);
            foreach (var g in rt.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            var cv = rt.GetComponent<Canvas>();
            if (cv != null) { cv.overrideSorting = true; cv.sortingOrder = WandWheel.FrontSortingOrder + 2; }

            var tip = rt.gameObject.AddComponent<CastSkillTip>();
            tip._rt = rt;
            tip._head = head;
            tip._group = rt.gameObject.AddComponent<CanvasGroup>();
            tip._group.interactable = false;
            tip._group.blocksRaycasts = false;

            tip._icon = UiKit.Icon(rt, new Vector2(-W * 0.5f + 42f, H * 0.5f - 42f), new Vector2(56f, 56f));
            tip._icon.raycastTarget = false;

            tip._title = UiKit.Label(rt, "", new Vector2(28f, H * 0.5f - 28f), new Vector2(W - 96f, 34f), 22, TextAnchor.MiddleLeft, HudLook.Heading);
            tip._title.raycastTarget = false;
            tip._title.resizeTextForBestFit = true;
            tip._title.resizeTextMinSize = 15;
            tip._title.resizeTextMaxSize = 22;

            tip._meta = UiKit.Label(rt, "", new Vector2(28f, H * 0.5f - 58f), new Vector2(W - 96f, 26f), 16, TextAnchor.MiddleLeft, new Color(0.75f, 0.86f, 1f, 0.95f));
            tip._meta.raycastTarget = false;
            tip._meta.resizeTextForBestFit = true;
            tip._meta.resizeTextMinSize = 12;
            tip._meta.resizeTextMaxSize = 16;

            var div = UiKit.Box(rt, "div", new Vector2(0f, H * 0.5f - 78f), new Vector2(W - 32f, 2f), new Color(1f, 0.85f, 0.45f, 0.28f));
            div.GetComponent<Graphic>().raycastTarget = false;

            tip._desc = UiKit.Label(rt, "", new Vector2(0f, -38f), new Vector2(W - 36f, H - 94f), 16, TextAnchor.UpperLeft, HudLook.Text);
            tip._desc.raycastTarget = false;
            tip._desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            tip._desc.verticalOverflow = VerticalWrapMode.Truncate;
            tip._desc.lineSpacing = 0.96f;

            rt.gameObject.SetActive(false);
            return tip;
        }

        public void ShowSkill(int skillId, Progression prog, int job, Vector3 worldPos, Quaternion worldRot)
        {
            var info = SkillDb.Get(skillId);
            Progression.SkillEntry entry = null;
            if (prog != null) prog.Skills.TryGetValue(skillId, out entry);
            int lv = entry != null && entry.Level > 0 ? prog.CastLevel(entry) : Mathf.Max(1, info.MaxLevel);

            if (!Shown || _shownItem || _shownId != skillId || _shownLv != lv)
            {
                if (!Shown) _popT = 0f;
                _shownItem = false;
                _shownId = skillId;
                _shownLv = lv;

                _icon.sprite = WristMenu.SkillIcon(info);
                _icon.enabled = _icon.sprite != null;

                int maxLv = Mathf.Max(info.MaxLevel, entry != null ? entry.Level : 0);
                _title.text = entry != null && entry.Level > 0
                    ? Lang.F("{0}  <color=#ffd060>Lv {1}/{2}</color>", info.Name, lv, maxLv)
                    : maxLv > 0 ? Lang.F("{0}  <color=#9aa3b5>(Max Lv {1})</color>", info.Name, maxLv) : info.Name;

                var ex = SkillTreeDb.ExtraOf(skillId);
                string kind = entry == null ? ""
                    : entry.Passive ? Lang.T("Passive")
                    : entry.Ground ? Lang.T("Ground target")
                    : entry.SelfCast ? Lang.T("Self")
                    : entry.Support ? Lang.T("Support")
                    : Lang.T("~kind~Active");

                var metaSb = new StringBuilder();
                if (!string.IsNullOrEmpty(kind)) metaSb.Append(kind);
                if (info.Sp != null && info.Sp.Length > 0)
                {
                    int spIdx = Mathf.Clamp(lv - 1, 0, info.Sp.Length - 1);
                    int sp = info.Sp[spIdx];
                    if (sp > 0)
                    {
                        if (metaSb.Length > 0) metaSb.Append("  ·  ");
                        metaSb.Append(Lang.F("<color=#7fc8ff>SP {0}</color>", sp));
                    }
                }
                if (ex.Range != null && ex.Range.Length > 0)
                {
                    int rIdx = Mathf.Clamp(lv - 1, 0, ex.Range.Length - 1);
                    int rng = ex.Range[rIdx];
                    if (rng > 1)
                    {
                        if (metaSb.Length > 0) metaSb.Append("  ·  ");
                        metaSb.Append(Lang.F("Range {0}", rng));
                    }
                }
                _meta.text = metaSb.ToString();

                var lines = WristMenu.DescriptionLines(info, job);
                _desc.text = lines.Count > 0
                    ? string.Join("\n", lines)
                    : Lang.T("<color=#8890a0>No description available.</color>");

                if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            }

            PlaceAndAnimate(worldPos, worldRot);
        }

        public void ShowItem(int itemId, Inventory inv, Vector3 worldPos, Quaternion worldRot)
        {
            var info = GameDb.GetItem(itemId);
            int count = inv != null ? inv.Count(itemId) : 0;

            if (!Shown || !_shownItem || _shownId != itemId || _shownLv != count)
            {
                if (!Shown) _popT = 0f;
                _shownItem = true;
                _shownId = itemId;
                _shownLv = count;

                _icon.sprite = WristMenu.ItemIcon(info);
                _icon.enabled = _icon.sprite != null;

                _title.text = Lang.F("{0}  <color=#ffd060>×{1}</color>", UiKit.StripTags(info.Name), count);
                _meta.text = Lang.T("Quick Item");

                string raw = info.Description ?? "";
                _desc.text = !string.IsNullOrEmpty(raw)
                    ? WristMenu.ReadableRo(raw)
                    : Lang.T("<color=#8890a0>No description available.</color>");

                if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            }

            PlaceAndAnimate(worldPos, worldRot);
        }

        private void PlaceAndAnimate(Vector3 worldPos, Quaternion worldRot)
        {
            _popT = Mathf.MoveTowards(_popT, 1f, Time.unscaledDeltaTime / 0.14f);
            float ease = 1f - (1f - _popT) * (1f - _popT);
            _group.alpha = ease;
            transform.position = worldPos;
            transform.rotation = worldRot;
            transform.localScale = Vector3.one * (Scale * Mathf.Lerp(0.84f, 1f, ease));
        }

        public void Hide()
        {
            _shownId = -1;
            _shownLv = -1;
            _popT = 0f;
            if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false);
        }
    }
}
