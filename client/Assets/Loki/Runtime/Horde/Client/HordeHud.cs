using System.Collections.Generic;
using Loki.UI;
using Loki.VR;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game) / client wave 9 (h9flow): the run's HUD, rebuilt for a headset. A wide band floating above the far
    /// edge of the table at about eye height (a glance up from the arena reads it; it turns with the head only when the head
    /// turned more than 35°, so it never swims in view). Its MIDDLE is kept clear (nothing sits in the centre of view): the left
    /// block is me (run level and job, an HP bar that FLASHES at low HP (the bar, never the screen), the run EXP bar, the weapons
    /// and passives with their icons and level pips), the right block is the fight (the run clock, kills, every ally's HP bar);
    /// the top centre holds the boss bar h9feel draws (HordeBossBar) and the banners (the 3-2-1 count-in, a job change, a boss's
    /// arrival, the end) that show for a moment and go. Everything is sized to read at arm's length; Settings → Horde → "HUD size"
    /// scales the whole band.
    /// </summary>
    public sealed class HordeHud : MonoBehaviour
    {
        private const float W = 1240, H = 400, MetresPerPx = 0.00055f;
        /// <summary>Client wave 21 (w21horde): the band's scale, metres a px (before Settings -> Horde's HUD size), for the pieces others put on it.</summary>
        public const float PxMetres = MetresPerPx;
        private const float Left = -W / 2 + 24, BarW = 500, Right = W / 2 - 24;
        private RectTransform _rt;
        private Text _title, _time, _kills, _hpText, _expText, _status, _banner;
        private RectTransform _hpFill, _hpShield, _hpBg, _expFill;
        public float ShieldFraction { get; private set; }
        public bool ShieldVisible => _hpShield != null && _hpShield.gameObject.activeSelf;
        private readonly List<(Image icon, Text lv, RectTransform[] pips, RectTransform box)> _weapons = new List<(Image, Text, RectTransform[], RectTransform)>(), _passives = new List<(Image, Text, RectTransform[], RectTransform)>();
        private readonly List<(RectTransform row, Text name, RectTransform fill, Text state)> _party = new List<(RectTransform, Text, RectTransform, Text)>();
        private Camera _head; private float _yaw = float.NaN; private float _bannerUntil; private float _appliedScale = -1f;
        public HordeBossBar BossBar { get; private set; }
        public string LastTitle => _title != null ? _title.text : "";
        public string LastStatus => _status != null ? _status.text : "";
        /// <summary>The banner's text while one shows ("" otherwise), and the last one shown (the smoke).</summary>
        public string Banner => _banner != null && _banner.gameObject.activeSelf ? _banner.text : "";
        public string LastBanner { get; private set; } = "";
        public bool LowHpFlashing { get; private set; }
        /// <summary>Client wave 17 (h17pass): passive slot i's box and the HP bar (HordePassivesDraw puts its meters and the shell's strip by them).</summary>
        public RectTransform PassiveSlot(int i) => i >= 0 && i < _passives.Count ? _passives[i].box : null;
        public RectTransform HpBar => _hpBg;
        public int PartyRows { get; private set; }

        /// <summary>Settings → Horde (h9flow; World/UserSettings.cs hordeHudScale / hordeFollow). The HUD's size factor, and how the table
        /// follows the run character (0 smooth, 1 tight, 2 fixed table): h9move's follow code (HordeFollow.cs) reads <see cref="FollowMode"/>.</summary>
        public static float Scale => Mathf.Clamp(World.UserSettings.Prefs.hordeHudScale <= 0f ? 1f : World.UserSettings.Prefs.hordeHudScale, 0.5f, 2f);
        public static int FollowMode => Mathf.Clamp(World.UserSettings.Prefs.hordeFollow, 0, 2);
        public static readonly float[] Scales = { 0.8f, 1f, 1.25f, 1.5f };
        public static string FollowName(int mode) => mode == 1 ? Lang.T("~follow~Tight") : mode == 2 ? Lang.T("~follow~Fixed") : Lang.T("~follow~Smooth");

        /// <summary>design note (2026-10-05 00:10): every player shares ONE experience bar and ONE level (the party's pool against the party's
        /// next-level requirement; h9move's protocol 2 keeps them party-wide). Read from my own player's values (the host gives every
        /// player the same) until h9move's party fields land: ONE place to switch.</summary>
        public static void PartyProgress(HordeWorldView w, int mySlot, out int level, out int exp, out int expToNext)
        {
            level = 1; exp = 0; expToNext = 1;
            if (w == null) return;
            if (w.PartyExpToNext > 0) { level = w.PartyLevel; exp = w.PartyExp; expToNext = w.PartyExpToNext; return; } // h9move's protocol 2 (007461ae)
            var me = mySlot >= 0 && mySlot < w.Players.Length && w.Players[mySlot].Present ? w.Players[mySlot] : null;
            if (me == null) foreach (var p in w.Players) if (p.Present) { me = p; break; }
            if (me == null) return;
            level = me.Level; exp = me.Exp; expToNext = me.ExpToNext;
        }

        public static HordeHud Create(Transform parent, Camera head)
        {
            var rt = UiKit.Panel("HordeHud", parent, new Vector2(W, H), MetresPerPx, false);
            var h = rt.gameObject.AddComponent<HordeHud>();
            h._rt = rt; h._head = head; h.Build();
            rt.gameObject.SetActive(false);
            return h;
        }

        /// <summary>Client wave 21 (w21horde): the HUD's two blocks (me, the fight) as rectangles of the band, px: where waves 8-20 drew a
        /// dark block each, the theme's see-through frame stands now.</summary>
        public static readonly Vector2 BlockSize = new Vector2(560, H);
        public static Vector2 LeftBlock => new Vector2(Left + 270, 0);
        public static Vector2 RightBlock => new Vector2(Right - 270, 0);
        private RectTransform _bannerFrame, _fightPlank, _fightFrame; private int _fightLetters = -1;
        /// <summary>Client wave 21 (w21horde): the fight's frame is as tall as what it holds: the clock and the kills (104 px) and 36 px for
        /// every ally shown (waves 8-20: the block's full 400 px whoever was there).</summary>
        public static float FightFrameHeight(int allies) => 104f + 36f * Mathf.Max(0, allies);
        /// <summary>The fight's see-through frame (the smoke reads its size).</summary>
        public RectTransform FightFrame => _fightFrame;

        /// <summary>A see-through frame's new place and size (the kit's picture "border" and its unused box "bg" with it).</summary>
        private static void SizeFrame(RectTransform frame, Vector2 pos, Vector2 size)
        {
            if (frame == null || (frame.sizeDelta == size && frame.anchoredPosition == pos)) return;
            frame.anchoredPosition = pos; frame.sizeDelta = size;
            foreach (RectTransform ch in frame) if (ch.name == "border") ch.sizeDelta = size; else if (ch.name == "bg") ch.sizeDelta = size - new Vector2(6, 6);
        }

        private void Build()
        {
            // client wave 21 (w21horde), THE PANEL THEME: the HUD sits over the action, so it takes the theme's SEE-THROUGH look
            // (Horde/Client/HordeLook.cs): where waves 8-20 drew two dark blocks (560 x 400 px, 78 % opaque) stand two see-through frames
            // (the gold line and the corner scrolls), and the lettering stands on planks of the frame's darkest wood only as large as
            // the letters need; every text, bar and icon is where and as large as it was; the middle stays open
            HordeLook.SeeFrame(_rt, "th:left", LeftBlock, BlockSize, MetresPerPx);
            _fightFrame = HordeLook.SeeFrame(_rt, "th:right", RightBlock, BlockSize, MetresPerPx);
            SizeFrame(_fightFrame, new Vector2(RightBlock.x, H / 2 - FightFrameHeight(0) / 2f), new Vector2(BlockSize.x, FightFrameHeight(0)));
            HordeLook.PlankBox(_rt, "th:plank-me", new Vector2(Left + BarW / 2, H / 2 - 80), new Vector2(BarW + 28, 148)); // the title, the HP bar, the EXP bar and its line
            _fightPlank = HordeLook.PlankBox(_rt, "th:plank-fight", new Vector2(Right - 150, H / 2 - 50), new Vector2(328, 84)); // the clock and the kills
            // me
            _title = UiKit.Label(_rt, "", new Vector2(Left + BarW / 2, H / 2 - 32), new Vector2(BarW, 44), 32, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.45f));
            _title.gameObject.AddComponent<Outline>().effectColor = Color.black;
            _hpBg = UiKit.Box(_rt, "hpbg", new Vector2(Left + BarW / 2, H / 2 - 82), new Vector2(BarW, 38), new Color(0, 0, 0, 0.75f));
            HordeLook.GoldRim(_hpBg); // (client wave 21: the theme's thin gold edge)
            _hpFill = UiKit.Box(_hpBg, "fill", Vector2.zero, new Vector2(BarW - 4, 34), new Color(0.25f, 0.85f, 0.35f));
            _hpFill.anchorMin = _hpFill.anchorMax = new Vector2(0, 0.5f); _hpFill.pivot = new Vector2(0, 0.5f); _hpFill.anchoredPosition = new Vector2(2, 0);
            _hpShield = UiKit.Box(_hpBg, "shield", Vector2.zero, new Vector2(0, 34), new Color(0.45f, 0.90f, 1f, 0.92f));
            _hpShield.anchorMin = _hpShield.anchorMax = new Vector2(0, 0.5f); _hpShield.pivot = new Vector2(0, 0.5f); _hpShield.anchoredPosition = new Vector2(2, 0);
            _hpShield.gameObject.SetActive(false);
            _hpText = UiKit.Label(_hpBg, "", Vector2.zero, new Vector2(BarW - 10, 38), 24, TextAnchor.MiddleCenter, Color.white);
            _hpText.gameObject.AddComponent<Outline>().effectColor = Color.black;
            var expBg = UiKit.Box(_rt, "expbg", new Vector2(Left + BarW / 2, H / 2 - 114), new Vector2(BarW, 16), new Color(0, 0, 0, 0.75f));
            HordeLook.GoldRim(expBg, 1.5f, true);
            _expFill = UiKit.Box(expBg, "fill", Vector2.zero, new Vector2(BarW - 4, 12), new Color(0.35f, 0.6f, 1f));
            _expFill.anchorMin = _expFill.anchorMax = new Vector2(0, 0.5f); _expFill.pivot = new Vector2(0, 0.5f); _expFill.anchoredPosition = new Vector2(2, 0);
            _expText = UiKit.Label(_rt, "", new Vector2(Left + BarW / 2, H / 2 - 136), new Vector2(BarW, 24), 18, TextAnchor.MiddleLeft, UiKit.Dim);
            for (int i = 0; i < 6; i++)
            {
                _weapons.Add(Slot(new Vector2(Left + 36 + i * 84, -20), 68));
                _passives.Add(Slot(new Vector2(Left + 36 + i * 84, -118), 50));
            }
            // the fight
            _time = UiKit.Label(_rt, "", new Vector2(Right - BarW / 2, H / 2 - 32), new Vector2(BarW, 46), 36, TextAnchor.MiddleRight, Color.white);
            _time.gameObject.AddComponent<Outline>().effectColor = Color.black;
            _kills = UiKit.Label(_rt, "", new Vector2(Right - BarW / 2, H / 2 - 72), new Vector2(BarW, 34), 26, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.45f));
            for (int i = 0; i < HordeLimits.MaxPlayers - 1; i++)
            {
                float y = H / 2 - 112 - i * 36;
                var row = HordeLook.PlankBox(_rt, "ally", new Vector2(Right - BarW / 2, y), new Vector2(BarW, 32)); // (client wave 21: an ally's row is its own plank, there only while the ally is)
                var name = UiKit.Label(row, "", new Vector2(-BarW / 2 + 110, 0), new Vector2(210, 30), 21, TextAnchor.MiddleLeft, Color.white);
                var bg = UiKit.Box(row, "bg", new Vector2(40, 0), new Vector2(200, 14), new Color(0, 0, 0, 0.75f));
                HordeLook.GoldRim(bg, 1.5f, true);
                var fill = UiKit.Box(bg, "fill", Vector2.zero, new Vector2(196, 10), new Color(0.25f, 0.85f, 0.35f));
                fill.anchorMin = fill.anchorMax = new Vector2(0, 0.5f); fill.pivot = new Vector2(0, 0.5f); fill.anchoredPosition = new Vector2(2, 0);
                var state = UiKit.Label(row, "", new Vector2(BarW / 2 - 85, 0), new Vector2(160, 30), 19, TextAnchor.MiddleRight, UiKit.Dim);
                row.gameObject.SetActive(false);
                _party.Add((row, name, fill, state));
            }
            // the middle: the boss bar (h9feel's) at the top, the banners under it, the status line at the bottom
            BossBar = HordeBossBar.Create(_rt, new Vector2(0, H / 2 - 40), 520f);
            // client wave 21 (w21horde): a banner is the theme's see-through notice: the gold line and the corner scrolls around its
            // lettering, as large as the lettering (ShowBanner sizes it), nothing behind
            _bannerFrame = HordeLook.SeeFrame(_rt, "th:banner", new Vector2(0, 20), new Vector2(560, 160), MetresPerPx);
            _bannerFrame.gameObject.SetActive(false);
            _banner = UiKit.Label(_rt, "", new Vector2(0, 20), new Vector2(560, 160), 56, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.55f));
            _banner.resizeTextForBestFit = true; _banner.resizeTextMinSize = 24; _banner.resizeTextMaxSize = 120;
            var bo = _banner.gameObject.AddComponent<Outline>(); bo.effectColor = Color.black; bo.effectDistance = new Vector2(3, -3);
            _banner.gameObject.SetActive(false);
            _status = UiKit.Label(_rt, "", new Vector2(0, -H / 2 + 24), new Vector2(560, 34), 23, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
            _status.gameObject.AddComponent<Outline>().effectColor = Color.black;
        }

        private (Image, Text, RectTransform[], RectTransform) Slot(Vector2 pos, float size)
        {
            // client wave 21 (w21horde): a slot is the theme's soft square with a thin gold edge: an empty one the faint edge alone, a held
            // one the darkest wood under its icon (SetSlot); as large as before (the icon's size + 8 px)
            var box = HordeLook.Raw(UiKit.Soft(_rt, "slot", pos, new Vector2(size + 8, size + 8), HordeLook.SlotEmpty));
            HordeLook.GoldRim(box, 0f, true);
            var icon = UiKit.Icon(_rt, pos, new Vector2(size, size));
            var lv = UiKit.Label(_rt, "", pos + new Vector2(size / 2 - 6, -size / 2 + 10), new Vector2(40, 24), 18, TextAnchor.MiddleRight, Color.white);
            lv.gameObject.AddComponent<Outline>().effectColor = Color.black;
            lv.gameObject.SetActive(false); // the pips say the level; the number only beyond five
            var pips = new RectTransform[5];
            for (int p = 0; p < 5; p++) { pips[p] = UiKit.Box(_rt, "pip", pos + new Vector2(-24 + p * 12, -size / 2 - 11), new Vector2(9, 9), new Color(1f, 0.85f, 0.45f)); pips[p].gameObject.SetActive(false); }
            return (icon, lv, pips, box);
        }

        public void Show(bool on)
        {
            if (_rt == null || _rt.gameObject.activeSelf == on) return;
            _rt.gameObject.SetActive(on); _yaw = float.NaN;
            if (!on) { _banner.gameObject.SetActive(false); _bannerFrame.gameObject.SetActive(false); _bannerUntil = 0f; }
            else Prewarm();
        }

        /// <summary>Client wave 21 (w21horde): the HUD's first build (its frames, planks and edges, the count-in's first digit in its frame)
        /// is made NOW, in the frame the arena opens (where wave 17's h17fix and the wave 18 merge put the other first-use work), not in
        /// the run's third frame, which w9feel measures (the frame after the first enemies are drawn: 9.9 ms on w19int, 11.2 ms on
        /// w20int, 12.1 and 17.0 ms with the theme's HUD before this).</summary>
        private void Prewarm()
        {
            _banner.text = "3"; _banner.resizeTextMaxSize = 120;
            _banner.gameObject.SetActive(true); _bannerFrame.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            _banner.gameObject.SetActive(false); _bannerFrame.gameObject.SetActive(false); _banner.text = "";
        }

        /// <summary>Client wave 21 (w21horde): a banner's see-through frame: this far around its lettering, px (sideways, up and down).</summary>
        public const float BannerPadX = 44f, BannerPadY = 16f;
        /// <summary>The banner's frame while a banner shows (null otherwise; the smoke).</summary>
        public RectTransform BannerFrame => _bannerFrame != null && _bannerFrame.gameObject.activeSelf ? _bannerFrame : null;

        /// <summary>A big word in the middle for <paramref name="seconds"/> (the count-in's digits, "Go!", a job change, a boss, the end).</summary>
        public void ShowBanner(string text, float seconds, Color? color = null, int size = 56)
        {
            if (_banner == null) return;
            _banner.text = text ?? ""; _banner.resizeTextMaxSize = size; _banner.color = color ?? new Color(1f, 0.9f, 0.55f);
            bool on = !string.IsNullOrEmpty(text);
            _banner.gameObject.SetActive(on);
            _bannerUntil = Time.unscaledTime + Mathf.Max(0.05f, seconds);
            if (on) LastBanner = text;
            // (client wave 21) the frame: as wide as the lettering at this size (at most the banner's own 560 px box), one line or the whole box
            if (_bannerFrame.gameObject.activeSelf != on) _bannerFrame.gameObject.SetActive(on);
            if (on)
            {
                var gs = _banner.GetGenerationSettings(Vector2.zero);
                gs.resizeTextForBestFit = false; gs.fontSize = size; gs.scaleFactor = 1f; gs.horizontalOverflow = HorizontalWrapMode.Overflow;
                float tw = _banner.cachedTextGeneratorForLayout.GetPreferredWidth(_banner.text, gs);
                bool oneLine = tw <= 560f;
                var fs = new Vector2(Mathf.Min(560f, tw) + 2f * BannerPadX, oneLine ? Mathf.Min(160f, size * 1.2f) + 2f * BannerPadY : 160f + 2f * BannerPadY);
                SizeFrame(_bannerFrame, _bannerFrame.anchoredPosition, fs);
            }
        }

        public void Refresh(HordeWorldView w, HordeContent c, HordeRunInfo run, int mySlot, string status)
        {
            if (_rt == null || !_rt.gameObject.activeSelf || w == null || mySlot < 0 || mySlot >= w.Players.Length) return;
            var me = w.Players[mySlot];
            PartyProgress(w, mySlot, out int level, out int exp, out int expToNext);
            _title.text = Lang.F("Party level {0} · {1}", level, HordeText.Job(me.JobId));
            _time.text = HordeText.Clock(w.Seconds);
            _kills.text = Lang.F("{0} kills", me.Kills);
            float hp = me.MaxHp > 0 ? Mathf.Clamp01(me.Hp / (float)me.MaxHp) : 0f;
            _hpFill.sizeDelta = new Vector2((BarW - 4) * hp, 34);
            float shieldFrac = ComputeShieldFraction(me, c);
            ShieldFraction = shieldFrac;
            if (_hpShield != null)
            {
                bool showShield = shieldFrac > 0.001f;
                if (_hpShield.gameObject.activeSelf != showShield) _hpShield.gameObject.SetActive(showShield);
                if (showShield) _hpShield.sizeDelta = new Vector2((BarW - 4) * shieldFrac, 34);
            }
            _hpText.text = Lang.F("HP {0} / {1}", me.Hp, me.MaxHp);
            LowHpFlashing = !me.Fallen && hp > 0f && hp < 0.3f;
            _expFill.sizeDelta = new Vector2((BarW - 4) * Mathf.Clamp01(expToNext > 0 ? exp / (float)expToNext : 0f), 12);
            _expText.text = Lang.F("Party EXP {0} / {1}", exp, expToNext);
            var berry = w.BerryOf(mySlot); BerryShown = ""; // client wave 18 (h18feel), point 37: the Yggdrasil Berry's slot burnt (a cross) or resting (its seconds)
            int activeHand = 0;
            for (int i = 0; i < 6; i++)
            {
                SetSlot(_weapons[i], i < me.Weapons.Count ? HordeText.WeaponIcon(c, me.Weapons[i].DefId) : null, i < me.Weapons.Count ? me.Weapons[i].Level : 0, i < me.Weapons.Count);
                BerrySlot(i, i < me.Weapons.Count && IsBerry(c, me.Weapons[i].DefId) ? berry : new HordeBerryView { Slot = -1 }, w);
                bool isAct = i < me.Weapons.Count && HordeSim.IsActiveWeapon(me.Weapons[i].DefId) && activeHand < 2;
                ActiveSlotBadge(i, isAct ? activeHand++ : -1);
                SetSlot(_passives[i], i < me.Passives.Count ? HordeText.PassiveIcon(c, me.Passives[i].DefId) : null, i < me.Passives.Count ? me.Passives[i].Level : 0, i < me.Passives.Count);
            }
            int row = 0;
            foreach (var p in w.Players)
            {
                if (!p.Present || p.Slot == mySlot || row >= _party.Count) continue;
                var (r, name, fill, state) = _party[row++];
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                name.text = p.Name;
                float f = p.MaxHp > 0 ? Mathf.Clamp01(p.Hp / (float)p.MaxHp) : 0f;
                fill.sizeDelta = new Vector2(196 * (p.Fallen ? p.Revive : f), 10);
                fill.GetComponent<Image>().color = p.Fallen ? new Color(0.9f, 0.55f, 0.3f) : f < 0.3f ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.25f, 0.85f, 0.35f);
                state.text = p.Fallen ? Lang.F("<color=#ff8a7a>fallen</color> {0} %", Mathf.RoundToInt(p.Revive * 100)) : (p.Flags & HordePlayerFlags.Away) != 0 ? Lang.T("~horde~away")
                           : (p.Flags & HordePlayerFlags.Choosing) != 0 ? Lang.T("~horde~choosing…") : HordeText.Job(p.JobId);
            }
            PartyRows = row;
            SizeFrame(_fightFrame, new Vector2(RightBlock.x, H / 2 - FightFrameHeight(row) / 2f), new Vector2(BlockSize.x, FightFrameHeight(row))); // (client wave 21)
            for (; row < _party.Count; row++) if (_party[row].row.gameObject.activeSelf) _party[row].row.gameObject.SetActive(false);
            // (client wave 21: the clock's plank is as wide as the clock and the kills need: both right-aligned)
            int letters = _time.text.Length * 100 + _kills.text.Length; // (measured only when a text's length changes: the digits are near one width)
            if (_fightPlank != null && letters != _fightLetters) { _fightLetters = letters; float need = Mathf.Clamp(Mathf.Max(_time.preferredWidth, _kills.preferredWidth) + 44f, 200f, BarW + 28f); if (Mathf.Abs(_fightPlank.sizeDelta.x - need) > 0.5f) { _fightPlank.sizeDelta = new Vector2(need, 84); _fightPlank.anchoredPosition = new Vector2(Right + 14f - need / 2f, H / 2 - 50); } }
            _status.text = status ?? "";
            BossBar?.Refresh(w, c);
        }

        private static float ComputeShieldFraction(HordePlayerView me, HordeContent c)
        {
            if (me == null || me.MaxHp <= 0 || c == null || me.Passives == null) return 0f;
            float frac = 0f;
            foreach (var o in me.Passives)
            {
                var pd = c.Passive(o.DefId);
                if (pd?.Proc == null) continue;
                int lv = Mathf.Clamp(o.Level, 1, Mathf.Max(1, pd.Levels.Length));
                var st = new HordeProcView { Kind = pd.Proc.Kind };
                bool found = false;
                if (me.Procs != null)
                    foreach (var s in me.Procs)
                        if (s.Kind == pd.Proc.Kind) { st = s; found = true; break; }
                if (pd.Proc.Kind == ProcKind.Coat)
                {
                    float charge = found ? st.Charge / 255f : 1f;
                    float capPct = Mathf.Max(0.15f, PassiveProcDef.At(pd.Proc.Power, pd.Proc.PowerPerLevel, lv));
                    if (capPct > 1f) capPct /= me.MaxHp;
                    frac += Mathf.Clamp01(capPct) * charge;
                }
                else if (pd.Proc.Kind == ProcKind.Shell || pd.Proc.Kind == ProcKind.BossBlock)
                {
                    if (!found || st.Ready) frac += 0.20f;
                }
            }
            return Mathf.Clamp01(frac);
        }

        private readonly Dictionary<int, Text> _activeBadges = new Dictionary<int, Text>();
        private void ActiveSlotBadge(int i, int hand)
        {
            if (!_activeBadges.TryGetValue(i, out var lbl))
            {
                if (hand < 0) return;
                var s = _weapons[i];
                var pos = s.box.anchoredPosition + new Vector2(-s.box.sizeDelta.x / 2f + 14f, s.box.sizeDelta.y / 2f - 12f);
                lbl = UiKit.Label(_rt, "", pos, new Vector2(36, 22), 16, TextAnchor.MiddleLeft, new Color(0.4f, 0.95f, 1f));
                lbl.fontStyle = FontStyle.Bold;
                lbl.gameObject.AddComponent<Outline>().effectColor = Color.black;
                _activeBadges[i] = lbl;
            }
            bool on = hand >= 0;
            if (lbl.gameObject.activeSelf != on) lbl.gameObject.SetActive(on);
            if (on)
            {
                lbl.text = hand == 0 ? "R" : "L";
                lbl.color = hand == 0 ? new Color(0.38f, 0.95f, 1f) : new Color(1f, 0.85f, 0.32f);
            }
        }

        // ---- client wave 18 (h18feel), the design's point 37: the Yggdrasil Berry's slot: spent and burnt (alone) = its icon grey under a red
        // cross; resting after a save (a party) = its icon grey with the seconds left over it
        private readonly Dictionary<int, (Text cross, Text secs)> _berryMarks = new Dictionary<int, (Text, Text)>();
        /// <summary>What the HUD shows on the berry's slot now: "" none, "burnt", or the seconds left (the smoke).</summary>
        public string BerryShown { get; private set; } = "";
        private static bool IsBerry(HordeContent c, int defId) { var wd = c?.Weapon(defId); return wd != null && wd.ItemId > 0 && wd.ItemId == (c.Souls != null ? c.Souls.SaveItemId : SoulRulesDef.YggdrasilBerry); }

        private void BerrySlot(int i, HordeBerryView b, HordeWorldView w)
        {
            var s = _weapons[i];
            bool burnt = b.Slot >= 0 && b.Burnt, resting = b.Slot >= 0 && !b.Burnt && b.Until > w.Tick;
            if (!_berryMarks.TryGetValue(i, out var m))
            {
                if (s.icon.color != Color.white) s.icon.color = Color.white;
                if (!burnt && !resting) return;
                var pos = s.box.anchoredPosition; float size = s.box.sizeDelta.x;
                var cross = UiKit.Label(_rt, "X", pos, new Vector2(size, size), (int)(size * 0.8f), TextAnchor.MiddleCenter, new Color(1f, 0.25f, 0.2f));
                cross.gameObject.AddComponent<Outline>().effectColor = Color.black;
                var secs = UiKit.Label(_rt, "", pos, new Vector2(size, size), 22, TextAnchor.MiddleCenter, Color.white);
                secs.gameObject.AddComponent<Outline>().effectColor = Color.black;
                _berryMarks[i] = m = (cross, secs);
            }
            s.icon.color = burnt || resting ? new Color(0.45f, 0.45f, 0.45f, 0.8f) : Color.white;
            if (m.cross.gameObject.activeSelf != burnt) m.cross.gameObject.SetActive(burnt);
            if (m.secs.gameObject.activeSelf != resting) m.secs.gameObject.SetActive(resting);
            int left = resting ? Mathf.CeilToInt((b.Until - w.Tick) / (float)Mathf.Max(1, w.TickRate)) : 0;
            if (resting) m.secs.text = left.ToString();
            if (burnt) BerryShown = "burnt"; else if (resting) BerryShown = left.ToString();
        }

        private static void SetSlot((Image icon, Text lv, RectTransform[] pips, RectTransform box) s, Sprite sp, int level, bool held)
        {
            s.icon.sprite = sp; s.icon.enabled = sp != null;
            // (client wave 21: a held slot stands on the darkest wood, an empty one is its faint gold edge alone)
            var fill = s.box.GetComponent<Image>(); var want = held ? HordeLook.SlotHeld : HordeLook.SlotEmpty;
            if (fill != null && fill.color != want)
            {
                fill.color = want;
                var rim = s.box.childCount > 0 ? s.box.GetChild(0).GetComponent<Image>() : null; if (rim != null) rim.color = held ? HordeLook.Edge : HordeLook.EdgeFaint;
            }
            for (int p = 0; p < s.pips.Length; p++) { bool on = held && level <= 5 && p < level; if (s.pips[p].gameObject.activeSelf != on) s.pips[p].gameObject.SetActive(on); }
            bool number = held && level > 5;
            if (s.lv.gameObject.activeSelf != number) s.lv.gameObject.SetActive(number);
            if (number) s.lv.text = level.ToString();
        }

        private void LateUpdate()
        {
            if (_head == null || _rt == null) return;
            float scale = MetresPerPx * Scale;
            if (Mathf.Abs(scale - _appliedScale) > 1e-7f) { _appliedScale = scale; _rt.localScale = Vector3.one * scale; }
            float yaw = _head.transform.eulerAngles.y;
            if (float.IsNaN(_yaw) || Mathf.Abs(Mathf.DeltaAngle(yaw, _yaw)) > 35f) _yaw = yaw;
            var f = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
            var target = _head.transform.position + f * 0.9f + Vector3.up * -0.1f;
            _rt.position = Vector3.Lerp(_rt.position, target, Mathf.Clamp01(Time.deltaTime * 6f));
            UiKit.FaceHead(_rt, _head);
            // the HP bar flashes at low HP: the bar's own fill between red and amber, never anything over the view
            if (_hpFill != null)
            {
                var img = _hpFill.GetComponent<Image>();
                img.color = LowHpFlashing ? Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(1f, 0.75f, 0.3f), Mathf.PingPong(Time.unscaledTime * 2.5f, 1f)) : new Color(0.25f, 0.85f, 0.35f);
            }
            if (_banner != null && _banner.gameObject.activeSelf && Time.unscaledTime > _bannerUntil) { _banner.gameObject.SetActive(false); _bannerFrame.gameObject.SetActive(false); }
        }

        /// <summary>Settings → Horde (client wave 9, h9flow): the mode's three rows, announced in COORD; the older pages keep their counts.</summary>
        public static void SettingsRows(List<ListWindow.Row> rows, System.Action<System.Action> set)
        {
            // client wave 13, item 5 (w13ring): rows by UI/SettingRow.cs (the current state lit, every choice shown); item 17: short texts
            var p = World.UserSettings.Prefs;
            rows.Add(SettingRow.Toggle(Lang.T("Horde invitations"), Lang.T("When others start a run"), () => p.hordeInvites, v => set(() => p.hordeInvites = v), Lang.T("~invites~Hidden"), Lang.T("~invites~Shown")));
            rows.Add(SettingRow.Choice(Lang.T("Table follow"), Lang.T("Keeps your runner centred"), new[] { FollowName(0), FollowName(1), FollowName(2) }, () => FollowMode, i => set(() => p.hordeFollow = i)));
            rows.Add(SettingRow.Slider(Lang.T("Horde HUD size"), null, Scales[0], Scales[Scales.Length - 1], VR.Comfort.FineScale, () => Scale, v => p.hordeHudScale = v, SettingRow.Percent, () => set(() => { })));
            HordeReadability.SettingsRow(rows, set); // client wave 17 (h17read), the readability proposal's F1: "Combat effects: Normal / Large"
        }
    }

    /// <summary>
    /// Client wave 8 (w8game) / client wave 9 (h9flow): the three-card choice after a level-up, and the job choice as a ceremony.
    /// Quick to answer in VR: the card under the laser or the gaze is PRE-HIGHLIGHTED (the rig hovers it), and A or the trigger
    /// takes it; a flick of the walking stick (left / right) moves the highlight instead; a hand pushed through a card's face takes
    /// it. Each card shows the weapon's icon (its Ragnarok skill's, or its item's), its name, "level n → n+1" and the numbers that
    /// change; a job card the job's own sprite and its signature weapon. The world does not stop: the chooser is safe while the
    /// panel is open (the host says so), the bar shows the time left, and when it is nearly out the client takes the highlighted
    /// card itself (the first when none is highlighted) so the host never has to pick for an idle chooser.
    /// </summary>
    public sealed class HordeChoice : MonoBehaviour
    {
        private const float W = 1340, H = 660;
        /// <summary>Cards drawn at most (the engine offers up to six: the Novice's six first jobs).</summary>
        public const int MaxCards = 6;
        /// <summary>Seconds before the host's deadline at which the highlighted card is taken by itself (Loki's own number).</summary>
        public const float AutoPickBefore = 0.6f;
        private RectTransform _rt;
        private Text _title, _hint, _party;
        private RectTransform _timeFill, _timeBg;
        /// <summary>Client wave 21 (w21horde): a card of the theme: a parchment card (the kit's UiKit.Card: it is the button, as large as the
        /// laser's collider), its icon in a gold ring (a job: its picture in the ring and its signature weapon's icon in a small ring at
        /// the corner; a stat / a heal: a glyph), its name, its tag on a band of the kind's colour (Horde/Client/HordeLook.cs KindColor;
        /// a NEW weapon / passive / job: bold, the band edged in gold), what it does and its numbers, the gold outline while A would take it.</summary>
        private sealed class Card
        {
            public UiButton b; public UiRingIcon ring, sig; public Image icon, portrait, bandImg, bandRim; public Text name, line, detail, glyph; public RectTransform frame, band;
        }
        private readonly List<Card> _cards = new List<Card>();
        /// <summary>Client wave 21 (w21horde): a card's size, px (wide: up to three; narrow: up to six), and its middle's height in the panel.
        /// Waves 8-20: 410 / 205 x 480 at -14; the title now stands on the frame's gold plate, so a card is 60 px taller (the laser's
        /// collider with it) and nothing on it is smaller.</summary>
        public const float CardWide = 410f, CardNarrow = 205f, CardH = 540f, CardY = 46f;
        /// <summary>The rings' diameters, px: a card's icon (wide / narrow), a job's signature weapon at the corner (wide / narrow).</summary>
        public const float RingWide = 190f, RingNarrow = 150f, SigWide = 136f, SigNarrow = 84f;
        /// <summary>The card buttons shown now, in order (the smoke).</summary>
        public IEnumerable<UiButton> CardButtons { get { foreach (var c in _cards) if (c.b.gameObject.activeSelf) yield return c.b; } }
        private UiTitlePlate _plate;
        private HordeOffer _offer; private float _opened, _seconds, _runMax; private int _highlight = -1; private bool _stickArmed = true; private int _shown = -1;
        private bool _coop; private Text _limit;
        /// <summary>The time bar and the auto-pick: whenever the host's offer carries a deadline (design note, 2026-10-05 01:25: every run, solo too, has a
        /// pause limit, 30 s at first and 10 s less each time it is met, floor 5 s: HordeOffer.Seconds carries the current limit, always &gt; 0).</summary>
        public bool TimerShown => _seconds > 0f;
        /// <summary>The current limit the offer carries (seconds), and the line that names it (the smoke).</summary>
        public float LimitSeconds => _seconds;
        public string LimitLine => _limit != null ? _limit.text : "";
        /// <summary>The "who has answered" line's text (the smoke).</summary>
        public string PartyLine => _party != null ? _party.text : "";
        /// <summary>A new run: the limit seen so far is forgotten (the "quicker now" line compares against this run's first).</summary>
        public void ResetRun() { _runMax = 0f; }
        private DioramaRig _rig; private Camera _head;
        public System.Action<int, int> Chosen; // (offer id, card index)
        public bool Open => _rt != null && _rt.gameObject.activeSelf && _offer != null;
        public HordeOffer Offer => _offer;
        /// <summary>The stick's highlight (-1 none).</summary>
        public int Highlight => _highlight;
        /// <summary>The card the laser or the gaze is on now (-1 none).</summary>
        public int Hovered { get { for (int i = 0; i < _cards.Count; i++) if (_cards[i].b.gameObject.activeSelf && _cards[i].b.Hovered) return i; return -1; } }
        /// <summary>The card A or the timeout takes: the stick's highlight, else the hovered card (-1 none).</summary>
        public int Current => _highlight >= 0 ? _highlight : Hovered;
        /// <summary>Seconds before the host's deadline (0 when there is none: alone, or an offer without a timeout).</summary>
        public float SecondsLeft => Open && TimerShown ? Mathf.Max(0f, _seconds - (Time.time - _opened)) : 0f;
        public string HowLast { get; private set; } = "";
        public int AutoPicks { get; private set; }
        /// <summary>The player's sex for the job portraits (1 male, 0 female; HordeGame sets it).</summary>
        public int Sex = 1;
        /// <summary>Client wave 15 (w15hfx), point 18: the player's hair style and colour for the portraits' heads (HordeGame sets them).</summary>
        public int Hair = 1, HairColor;
        /// <summary>Client wave 18 (h18rules), the design's point 22: the job cards' help panel (the laser on a job card: HordeJobHelp.cs).</summary>
        public HordeJobHelp JobHelp { get; private set; }
        private HordeContent _content;

        public static HordeChoice Create(Transform parent, DioramaRig rig)
        {
            var rt = UiKit.Panel("HordeChoice", parent, new Vector2(W, H), 0.0005f);
            var c = rt.gameObject.AddComponent<HordeChoice>();
            c._rt = rt; c._rig = rig; c._head = rig != null ? rig.Head : Camera.main;
            c.Build();
            c.JobHelp = HordeJobHelp.Create(parent, c._head, 0.0005f); // client wave 18 (h18rules), point 22
            rt.gameObject.SetActive(false);
            return c;
        }

        private void Build()
        {
            // client wave 21 (w21horde), THE PANEL THEME: the choice is a window of the theme (the wood frame and the cream canvas: the
            // panel's name is out of Theme.LegacyNames), its title on the frame's gold plate (the same 38 px letters), its cards
            // parchment cards (see Card), the time bar a groove on the canvas, the lines dark ink
            _plate = UiKit.TitlePlate(_rt, "", 38, W - 220); _title = _plate.Text; _title.color = HordeLook.DeepInk;
            // up to six cards (the Novice's job choice offers the six first jobs; wave 8 drew only three of them): three wide, or up to six narrow (Show lays them out)
            for (int i = 0; i < MaxCards; i++)
            {
                int idx = i; float x = (i - 1) * 430f;
                var k = new Card();
                // the card is the button: the kit's parchment card with the laser's collider (the name starts with "btn:" as every button's)
                var brt = UiKit.Card(_rt, "btn:", new Vector2(x, CardY), new Vector2(CardWide, CardH));
                var col = brt.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(CardWide, CardH, 8f); col.center = new Vector3(0, 0, -2f);
                var none = UiKit.Label(brt, "", Vector2.zero, new Vector2(10, 10), 30, TextAnchor.MiddleCenter); // (a button's label: none; waves 8-20 made it empty too)
                k.b = brt.gameObject.AddComponent<UiButton>(); k.b.Init(brt.GetComponent<Image>(), none, () => Pick(idx, "laser or gaze"));
                k.b.Normal = new Color(0.955f, 0.945f, 0.925f, 1f); k.b.Hover = Color.white; k.b.SetHover(false); // (the paper a shade lighter under the laser or the look)
                var hover = UiKit.HoverOutline(brt); hover.enabled = true; k.frame = hover.rectTransform; // the kit's gold outline: around the card A would take
                k.frame.offsetMin = new Vector2(-7, -7); k.frame.offsetMax = new Vector2(7, 7); k.frame.gameObject.SetActive(false);
                k.ring = UiKit.RingIcon(brt, new Vector2(0, 161), RingWide);
                k.icon = k.ring.Icon; k.icon.color = Color.white; // (a skill's or an item's own picture, in its own colours)
                k.glyph = k.ring.Glyph; k.glyph.color = HordeLook.DeepInk; k.glyph.fontStyle = FontStyle.Bold; k.glyph.resizeTextForBestFit = true; k.glyph.resizeTextMinSize = 12;
                k.portrait = UiKit.Icon((RectTransform)k.ring.transform, Vector2.zero, new Vector2(150, 150));
                k.sig = UiKit.RingIcon(brt, new Vector2(112, 141), SigWide); k.sig.Icon.color = Color.white; k.sig.gameObject.SetActive(false);
                k.name = UiKit.Label(brt, "", new Vector2(0, 35), new Vector2(380, 64), 32, TextAnchor.MiddleCenter, HordeLook.DeepInk);
                k.name.resizeTextForBestFit = true; k.name.resizeTextMinSize = 18; k.name.resizeTextMaxSize = 32;
                k.band = HordeLook.Raw(UiKit.Soft(brt, "kind", new Vector2(0, -22), new Vector2(388, 40), HordeLook.KindColor(HordeCardKind.NewWeapon)));
                k.bandImg = k.band.GetComponent<Image>(); k.bandRim = HordeLook.GoldRim(k.band, 0f);
                k.line = UiKit.Label(k.band, "", Vector2.zero, new Vector2(380, 40), 23, TextAnchor.MiddleCenter, HordeLook.DeepInk);
                k.detail = UiKit.Label(brt, "", new Vector2(0, -154.5f), new Vector2(376, 205), 20, TextAnchor.UpperCenter, HordeLook.DeepInk);
                k.detail.resizeTextForBestFit = true; k.detail.resizeTextMinSize = 14; k.detail.resizeTextMaxSize = 20;
                _cards.Add(k);
            }
            _timeBg = UiKit.Soft(_rt, "timebg", new Vector2(0, -H / 2 + 62), new Vector2(W - 120, 18), Theme.Field); HordeLook.GoldRim(_timeBg, 1.5f);
            _timeFill = HordeLook.Raw(UiKit.Box(_timeBg, "time", Vector2.zero, new Vector2(W - 124, 14), Theme.Accent)); // (dark on the light groove: the time left)
            _timeFill.anchorMin = _timeFill.anchorMax = new Vector2(0, 0.5f); _timeFill.pivot = new Vector2(0, 0.5f); _timeFill.anchoredPosition = new Vector2(2, 0);
            _party = UiKit.Label(_rt, "", new Vector2(0, -H / 2 + 86), new Vector2(W - 60, 28), 21, TextAnchor.MiddleCenter, Theme.Wood);
            _limit = UiKit.Label(_rt, "", new Vector2(W / 2 - 170, -H / 2 + 86), new Vector2(280, 28), 21, TextAnchor.MiddleRight, Theme.Ink);
            _hint = UiKit.Label(_rt, Lang.T("Look at a card or point the laser at it, then A or the trigger · touch it with your hand · walking stick left / right"), new Vector2(0, -H / 2 + 28), new Vector2(W - 60, 30), 20, TextAnchor.MiddleCenter, Theme.Wood);
        }

        /// <summary>Client wave 21 (w21horde): what a card says under its name and tag, in the theme's inks: what it does (the darkest
        /// ink), then its numbers (<see cref="HordeLook.NumberInk"/>): the sentences and numbers of HordeText.CardDetail, which coloured
        /// them for the dark cards (white, gold).</summary>
        public static string DetailText(HordeContent c, HordeCard k)
        {
            if (c == null) return "";
            string does = HordeText.CardDoes(c, k); var nums = HordeText.CardNumbers(c, k);
            if (k.HasBonusStat)
            {
                string bonus = HordeCardText.BonusStatLine(c, k);
                if (!string.IsNullOrEmpty(bonus)) nums.Add(bonus);
            }
            var sb = new System.Text.StringBuilder();
            if (does.Length > 0) sb.Append(does);
            if (nums.Count > 0)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append("<color=#").Append(Theme.Hex(HordeLook.NumberInk)).Append('>').Append(string.Join("\n", nums)).Append("</color>");
            }
            return sb.ToString();
        }

        /// <summary>Who has already answered during the shared pause (the design's rule of 2026-10-05: everyone chooses in the same pause): a
        /// tick per player who has, "…" per player still choosing; nothing when alone. Also decides whether the time bar shows (co-op only).</summary>
        public void SetParty(HordeWorldView w, int mySlot)
        {
            if (_party == null) return;
            int present = 0; var sb = new System.Text.StringBuilder();
            if (w != null)
                foreach (var p in w.Players)
                {
                    if (!p.Present) continue;
                    present++;
                    bool choosing = p.Slot == mySlot ? Open : (p.Flags & HordePlayerFlags.Choosing) != 0;
                    if (sb.Length > 0) sb.Append("   ");
                    sb.Append(p.Name).Append(choosing ? " …" : " <color=#7fd67f>✔</color>");
                }
            _coop = present >= 2;
            _party.text = _coop ? sb.ToString() : "";
        }

        public void Show(HordeOffer offer, HordeContent content)
        {
            _offer = offer; _opened = Time.time; _seconds = Mathf.Max(offer.Seconds, 0f); _highlight = -1; _stickArmed = true; _shown = -1;
            _content = content; JobHelp?.Hide(); // client wave 18 (h18rules), point 22
            _plate.Set(offer.IsJob ? Lang.T("Choose your next job") : Lang.T("Level up! Choose a card")); // (client wave 21: on the frame's gold plate)
            // the pause limit this offer carries, and whether it has shrunk since this run's first (the host met a limit: 30 → 20 → 10 → 5)
            if (_seconds > _runMax) _runMax = _seconds;
            int limit = Mathf.RoundToInt(_seconds);
            _limit.text = _seconds <= 0f ? "" : _seconds < _runMax - 0.5f ? Lang.F("Choices are quicker now: {0} s", limit) : Lang.F("Time to choose: {0} s", limit);
            if (_timeBg != null && _timeBg.gameObject.activeSelf != TimerShown) _timeBg.gameObject.SetActive(TimerShown);
            int n = Mathf.Min(offer.Cards.Length, _cards.Count);
            bool wide = n <= 3; float cw = wide ? CardWide : CardNarrow, gap = wide ? 430f : 218f, textW = cw - 30f;
            for (int i = 0; i < _cards.Count; i++)
            {
                var cd = _cards[i];
                bool on = i < n;
                cd.b.gameObject.SetActive(on); cd.frame.gameObject.SetActive(false);
                if (!on) continue;
                var k = offer.Cards[i];
                var sp = HordeText.CardIcon(content, k);
                Sprite pt = k.Kind == HordeCardKind.Job ? HordeText.JobPortrait(k.DefId, Sex, Hair, HairColor) : null;
                cd.portrait.sprite = pt; cd.portrait.enabled = pt != null;
                // the layout by count: three wide cards, or up to six narrow ones. Client wave 21 (w21horde): the picture stands in a gold
                // ring (as large as before: 120 / 96 px; a job's own picture 150 / 110 px, its signature weapon's icon 90 / 56 px in a
                // small ring at the corner); a stat or a heal has no picture: a glyph in the ring
                cd.b.Resize(new Vector2(cw, CardH));
                float ringD = wide ? RingWide : RingNarrow, ringY = CardH / 2f - 14f - ringD / 2f;
                var ringRt = (RectTransform)cd.ring.transform; ringRt.sizeDelta = new Vector2(ringD, ringD); ringRt.anchoredPosition = pt != null && wide ? new Vector2(-60, ringY) : new Vector2(0, ringY);
                cd.portrait.rectTransform.sizeDelta = wide ? new Vector2(150, 150) : new Vector2(110, 110);
                bool sigOn = pt != null && sp != null;
                if (cd.sig.gameObject.activeSelf != sigOn) cd.sig.gameObject.SetActive(sigOn);
                if (pt != null)
                {
                    float sd = wide ? SigWide : SigNarrow; var sigRt = (RectTransform)cd.sig.transform;
                    sigRt.sizeDelta = new Vector2(sd, sd); sigRt.anchoredPosition = wide ? new Vector2(112, 141) : new Vector2(58, 118);
                    cd.sig.Icon.sprite = sp; cd.sig.Icon.enabled = sp != null; cd.sig.Icon.rectTransform.sizeDelta = wide ? new Vector2(90, 90) : new Vector2(56, 56);
                    cd.icon.sprite = null; cd.icon.enabled = false; cd.glyph.text = "";
                }
                else
                {
                    cd.icon.sprite = sp; cd.icon.enabled = sp != null; cd.icon.rectTransform.sizeDelta = wide ? new Vector2(120, 120) : new Vector2(96, 96);
                    cd.glyph.text = sp != null ? "" : k.Kind == HordeCardKind.Stat ? HordeText.Stat(k.DefId) : k.Kind == HordeCardKind.Heal ? "+" : "";
                    cd.glyph.rectTransform.sizeDelta = new Vector2(ringD * 0.72f, ringD * 0.6f); cd.glyph.resizeTextMaxSize = Mathf.RoundToInt(ringD * 0.5f);
                }
                cd.name.rectTransform.sizeDelta = new Vector2(textW, 64); cd.name.resizeTextMaxSize = wide ? 32 : 24; cd.name.resizeTextMinSize = wide ? 18 : 14;
                // the tag on its band: the band's colour is the card's kind; a NEW weapon / passive / job is bold with a gold edge and warm golden card tint
                bool isNew = HordeLook.IsNew(k.Kind);
                cd.b.Normal = isNew ? new Color(1f, 0.965f, 0.875f, 1f) : new Color(0.945f, 0.94f, 0.925f, 1f);
                cd.b.SetHover(cd.b.Hovered);
                cd.band.sizeDelta = new Vector2(textW + 8, 42); cd.bandImg.color = HordeLook.KindColor(k.Kind);
                if (cd.bandRim.gameObject.activeSelf != isNew) cd.bandRim.gameObject.SetActive(isNew);
                cd.line.rectTransform.sizeDelta = new Vector2(textW, 42); cd.line.resizeTextForBestFit = true; cd.line.resizeTextMaxSize = wide ? 23 : 18; cd.line.resizeTextMinSize = 12;
                cd.line.fontStyle = isNew ? FontStyle.Bold : FontStyle.Normal;
                cd.detail.rectTransform.sizeDelta = new Vector2(textW - 4, 205); cd.detail.resizeTextMaxSize = wide ? 20 : 16; cd.detail.resizeTextMinSize = 11;
                cd.name.text = HordeText.CardTitle(content, k);
                string lineText = HordeText.CardLine(content, k);
                if (k.HasBonusStat)
                {
                    string bs = Lang.F("+{0} {1}", k.BonusStatPoints, HordeText.Stat(k.BonusStat - 1));
                    lineText = string.IsNullOrEmpty(lineText) ? bs : lineText + " · " + bs;
                }
                cd.line.text = lineText;
                cd.detail.text = DetailText(content, k);
                cd.b.SetHover(false);
                float x = (i - (n - 1) / 2f) * gap;
                ((RectTransform)cd.b.transform).anchoredPosition = new Vector2(x, CardY);
            }
            _rt.gameObject.SetActive(true);
            if (_head != null)
            {
                var f = _head.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                _rt.position = _head.transform.position + f * 0.6f + Vector3.up * -0.14f;
                UiKit.FaceHead(_rt, _head);
            }
            Debug.Log($"[Horde] choice {offer.Id} shown ({(offer.IsJob ? "job" : "cards")}, {offer.Seconds:0.#} s): {string.Join(" | ", System.Array.ConvertAll(offer.Cards, k => k.Kind + ":" + k.DefId + "@" + k.Level))}");
        }

        public void Close(int offerId)
        {
            if (_offer != null && offerId >= 0 && _offer.Id != offerId) return;
            _offer = null;
            if (_rt != null) _rt.gameObject.SetActive(false);
            JobHelp?.Hide(); // client wave 18 (h18rules), point 22
        }

        /// <summary>Takes a card (any of the ways; the smoke calls it as the laser would).</summary>
        public void Pick(int index, string how)
        {
            if (_offer == null || index < 0 || index >= _offer.Cards.Length) return;
            HowLast = how;
            var o = _offer;
            Debug.Log($"[Horde] card {index} of offer {o.Id} taken by {how}");
            Close(o.Id);
            Chosen?.Invoke(o.Id, index);
        }

        /// <summary>A pressed (the pointer hand's primary button): takes the highlighted card: the stick's, else the one under the laser or gaze.</summary>
        public bool PressPrimary()
        {
            if (!Open) return false;
            int i = Current; if (i < 0) return false;
            Pick(i, _highlight >= 0 ? "stick and A" : "gaze or laser and A"); return true;
        }

        private void Update()
        {
            if (!Open) return;
            TickJobHelp(); // client wave 18 (h18rules), point 22
            if (TimerShown)
            {
                float left = SecondsLeft;
                _timeFill.sizeDelta = new Vector2((W - 124) * Mathf.Clamp01(left / _seconds), 14);
                // co-op: the time is nearly out: the highlighted card is taken (the first when none), so the host never picks for an idle chooser
                if (left <= AutoPickBefore) { int i = Current; AutoPicks++; Pick(i >= 0 ? i : 0, "timeout"); return; }
            }
            if (_rig != null)
            {
                // the walking stick: a flick left / right moves the highlight
                var v = _rig.Off.StickValue;
                if (_stickArmed && Mathf.Abs(v.x) > 0.7f)
                {
                    _stickArmed = false;
                    int n = _offer.Cards.Length;
                    _highlight = _highlight < 0 ? (v.x > 0 ? 0 : n - 1) : Mathf.Clamp(_highlight + (v.x > 0 ? 1 : -1), 0, n - 1);
                    Haptics.Pulse(Haptics.Kind.Menu, false, 0.12f, 0.02f);
                }
                else if (Mathf.Abs(v.x) < 0.3f) _stickArmed = true;
                // the hand: a controller or tracked hand pushed through a card's face takes it
                for (int i = 0; i < _offer.Cards.Length && i < _cards.Count; i++)
                {
                    var rt = (RectTransform)_cards[i].b.transform;
                    if (Touches(rt, _rig.LeftHand) || Touches(rt, _rig.RightHand)) { Pick(i, "hand"); return; }
                }
            }
            // the frame around the card A would take (the stick's highlight, else the hovered one)
            int cur = Current;
            if (cur != _shown)
            {
                _shown = cur;
                for (int i = 0; i < _cards.Count; i++) if (_cards[i].frame.gameObject.activeSelf != (i == cur && i < _offer.Cards.Length)) _cards[i].frame.gameObject.SetActive(i == cur && i < _offer.Cards.Length);
            }
        }

        /// <summary>Client wave 18 (h18rules), point 22: the job card under a LASER (either hand's; the look never shows it), its help.</summary>
        private void TickJobHelp()
        {
            if (JobHelp == null) return;
            int card = -1;
            if (_offer != null && _offer.IsJob)
            {
                var lb = _rig != null ? _rig.LaserButton : null;
                for (int pass = 0; pass < 2 && card < 0; pass++)
                {
                    for (int i = 0; i < _offer.Cards.Length && i < _cards.Count; i++) if (lb != null && _cards[i].b == lb && _offer.Cards[i].Kind == HordeCardKind.Job) { card = i; break; }
                    lb = _rig != null ? _rig.OffLaserButton : null;
                }
            }
            JobHelp.Tick(_content, card, card >= 0 ? _offer.Cards[card].DefId : 0, card >= 0 ? (RectTransform)_cards[card].b.transform : null, _rt);
        }

        private static bool Touches(RectTransform card, Transform hand)
        {
            if (hand == null || !hand.gameObject.activeInHierarchy) return false;
            var local = card.InverseTransformPoint(hand.position); var size = card.sizeDelta;
            if (Mathf.Abs(local.x) > size.x / 2 || Mathf.Abs(local.y) > size.y / 2) return false;
            float metresPerUnit = card.lossyScale.z;
            float z = local.z * metresPerUnit; // in front of the card: negative z (the panel faces the head)
            return z > 0.005f && z < 0.06f;
        }
    }
}
