using System;
using System.Collections;
using System.Collections.Generic;
using Loki.World;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 21 (w21anim), the design's point 63 (2026-10-09): "add individual animations for when each of the menus that can be opened from
    /// the wrist menu are opened, where they are projected from the wrist into existence (these animations respect the same 5s cooldown from
    /// the inventory, and they can't be longer than 0.5s, though the Inventory can be up to 0.8s. All these animations can be toggled on or
    /// off)"; "The animation to open the skills tab can be up to 1.5s long"; "One switch for each menu ... a single button that opens up
    /// into its own sub-settings panel".
    ///
    /// THE REGISTRY (one place for every menu's opening animation, whoever plays it):
    ///   <see cref="Register"/>(key, displayName, maxSeconds)  a menu: its switch appears in Settings → Controls → Menus → "Menu animations…"
    ///                                                         (default On, kept with the account: World/UserSettings.cs menuAnimOff)
    ///   <see cref="ShouldPlay"/>(key)   ask ONCE when the menu opens: true = play. False: its switch is off, or it was closed less than
    ///                                   <see cref="ReopenSeconds"/> s ago (<see cref="Closed"/>'s clock), or a test run wants every menu
    ///                                   finished at once (development builds, -lokiSmoke 1: see TestInstant)
    ///   <see cref="Closed"/>(key)       the menu closed now (the 5 s clock starts)
    ///   <see cref="Own"/>(key)          its owner plays its own animation (the new inventory's unroll, the 3D skill tree's growth): the
    ///                                   registry never plays its out-of-the-wrist one for that key
    ///   <see cref="Play"/>(key, root, style)  the shared "out of the wrist" animation on any panel root (asks ShouldPlay itself)
    ///   <see cref="Track"/>(root, isOpen, key)  a panel that opens and closes by itself: the registry watches it, stamps Closed and plays
    ///                                   the key's animation when it opens (the boards, the classic wrist menu and the windows the wrist
    ///                                   menu opens are tracked this way: nothing at their many open / close places changed)
    ///
    /// OUT OF THE WRIST, AND NEVER IN THE WAY OF INPUT. The animation is a picture only: from the first frame the panel IS at its own
    /// place and size for everything that reads or uses it (the laser, the look, the trigger, the grip, the panels placed around it, the
    /// one-menu-at-a-time rule, every test). Each frame, after every script's LateUpdate (<see cref="MenuAnimDriver"/>, execution order
    /// 32000), the root's true local pose is kept and the animated one written; when the frame has been drawn (WaitForEndOfFrame) the true
    /// pose is put back. Physics never sees the animated pose (Project Settings: transforms are synced to the colliders at the physics
    /// step only, which runs before the scripts' Update of the next frame, after the pose was put back), so a button takes the laser, the
    /// look and the trigger at its own place while its picture still flies there. Nothing of the panel's layout, colliders, colours or
    /// sprites is touched: the root's position, rotation and scale for the drawing, and a CanvasGroup's alpha (the fade in).
    /// The picture: the panel's <see cref="Style.Anchor"/> point travels from the wrist (the hand that carries the menu) to its own place,
    /// eased out, while the panel grows from <see cref="Style.StartScale"/> of its size, unfolds from <see cref="Style.Fold"/>, bows by
    /// <see cref="Style.Arc"/> and, for an <see cref="Unroll"/>, opens one side before the other. Each menu has its own numbers
    /// (<see cref="StyleOf"/>; the table: design notes).
    ///
    /// OUT OF THE PANEL THAT ASKED (client wave 23, w23anim; design note, 2026-10-11: "when a menu is already open and I open another one through
    /// it, for example, if I on the wrist menu open up the world tab and then from that opened panel, I open up the horde one it shouldn't
    /// open from my wrist it should show up from the panel that spawned it"). ONE rule for every menu played here: a menu asked for by a
    /// press of a button ON AN OPEN PANEL (a board's tab or its page's button, a list window's row, the classic wrist menu's page, the
    /// inventory's foot line, a card, a back arrow) starts at THAT button's place, facing as that panel does there, and flies to its own
    /// place; asked for from the ring (the wrist's own menu), by a controller button, from a HUD piece or a casting tool's own picture, or
    /// with no press at all: out of the wrist, as before (<see cref="Pressed"/>, <see cref="Begin"/>). The time, the start size, the
    /// fold, the bow, the fade, the switches, the 5 s rule and the panel's own pose (input is never delayed) are untouched.
    /// </summary>
    public static class MenuAnim
    {
        /// <summary>A menu reopened within this many seconds of closing opens with no animation (design note: "the same 5s cooldown").</summary>
        public const float ReopenSeconds = 5f;
        /// <summary>The longest an opening animation may take: every menu, the inventory, the Skills tab (the design's numbers).</summary>
        public const float DefaultMax = 0.5f, InventoryMax = 0.8f, SkillsMax = 1.5f;
        /// <summary>The keys of the two menus whose owners play their own animation (the Items board / the new inventory; the Skills board /
        /// the 3D skill tree) and of the classic wrist menu.</summary>
        public const string Inventory = "inventory", Skills = "skills", Classic = "classic";
        /// <summary>The panel fades in over this share of its animation.</summary>
        public const float FadeShare = 0.35f;
        /// <summary>A long first frame (a board built its page) takes at most this much of the animation.</summary>
        public const float MaxFirstStep = 1f / 30f;
        /// <summary>The picture is at its place this long before the menu's limit at the latest (one frame at 50 a second), so the first
        /// frame drawn at its place still comes inside the limit.</summary>
        public const float EndMargin = 0.02f;

        public sealed class Menu
        {
            public string Key { get; internal set; }
            /// <summary>Its name in the switches' list (English: a text table key).</summary>
            public string Name { get; internal set; }
            public float MaxSeconds { get; internal set; }
            /// <summary>Its owner plays its own animation (<see cref="Own"/>).</summary>
            public bool Owned { get; internal set; }
            /// <summary>How often <see cref="ShouldPlay"/> said yes / no, and the last reason for a no (log, tests).</summary>
            public int Plays { get; internal set; }
            public int Skips { get; internal set; }
            public string LastSkip { get; internal set; } = "";
            /// <summary>The last out-of-the-wrist animation played for it: from the frame the menu opened to the frame it was done, in s.</summary>
            public float LastSeconds { get; internal set; }
            /// <summary>The same animation: the last frame drawn away from its place, in s after the frame the menu opened.</summary>
            public float LastMoving { get; internal set; }
            /// <summary>Client wave 23 (w23anim): where the last animation played here for it started: on the pressed button of an open
            /// panel (true; <see cref="LastFrom"/> names the panel and the button: "MenuBoard / Horde  ›") or at the wrist ("the wrist").</summary>
            public bool LastFromPanel { get; internal set; }
            public string LastFrom { get; internal set; } = "";
            internal float ClosedAt = float.NegativeInfinity;
            internal bool SkipNext;
        }

        public enum Unroll { None, WidthFirst, HeightFirst }

        /// <summary>One menu's own way out of the wrist.</summary>
        public struct Style
        {
            /// <summary>How long (cut to the menu's limit), and the share of its size it starts at.</summary>
            public float Seconds, StartScale;
            /// <summary>The panel's point that rides from the wrist to its own place: (0, 0) the middle, (-1, 0) the left edge's middle,
            /// (0, 1) the top edge's ...; the panel grows and unfolds about it.</summary>
            public Vector2 Anchor;
            /// <summary>It starts turned this much about the anchor (degrees about its own right / up / forward) and ends flat.</summary>
            public Vector3 Fold;
            /// <summary>The path bows by this much at its middle (metres along the panel's own right / up / forward).</summary>
            public Vector3 Arc;
            /// <summary>One side opens before the other (the width, then the height; or the height first).</summary>
            public Unroll Unroll;
            /// <summary>It overshoots its size and place a little and settles (about 5 %).</summary>
            public bool Spring;
            public bool NoFade;
        }

        private static readonly Dictionary<string, Menu> ByKey = new Dictionary<string, Menu>();
        private static readonly List<Menu> Ordered = new List<Menu>();
        private static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>();
        /// <summary>The windows the wrist menu opens (a board's side tab, a page's button, the classic strip), by their panel's name → key.
        /// Every other window (an NPC's shop, the storage, a trade, a skill's list ...) is not opened from the wrist: no animation.</summary>
        private static readonly Dictionary<string, string> WindowKeys = new Dictionary<string, string>();
        /// <summary>The switches changed (the list redraws).</summary>
        public static event Action Changed;
        /// <summary>The hand that carries the menu: the animations start at its place.</summary>
        public static Func<Transform> Wrist;

        static MenuAnim()
        {
            // the ring's eight boards, in the ring's order (the names are the ring's own)
            Add("character", Lang.N("Character"), DefaultMax, new Style { Seconds = 0.45f, StartScale = 0.05f, Anchor = new Vector2(0, -1), Fold = new Vector3(75, 0, 0) });
            Add(Inventory, Lang.N("Items"), InventoryMax, new Style { Seconds = 0.50f, StartScale = 0.05f, Anchor = new Vector2(-1, 0), Unroll = Unroll.HeightFirst });
            Add(Skills, Lang.N("Skills"), SkillsMax, new Style { Seconds = 0.50f, StartScale = 0.05f, Anchor = new Vector2(0, -1), Unroll = Unroll.WidthFirst });
            Add("quests", Lang.N("Quests"), DefaultMax, new Style { Seconds = 0.40f, StartScale = 0.06f, Anchor = new Vector2(0, 1), Unroll = Unroll.WidthFirst });
            Add("companions", Lang.N("Companions"), DefaultMax, new Style { Seconds = 0.35f, StartScale = 0.05f, Arc = new Vector3(0, 0.10f, 0), Spring = true });
            Add("social", Lang.N("Social"), DefaultMax, new Style { Seconds = 0.40f, StartScale = 0.06f, Anchor = new Vector2(-1, 0), Fold = new Vector3(0, 80, 0) });
            Add("world", Lang.N("World"), DefaultMax, new Style { Seconds = 0.45f, StartScale = 0.04f, Fold = new Vector3(0, 0, 90), Arc = new Vector3(0.08f, 0, 0) });
            Add("settings", Lang.N("Settings"), DefaultMax, new Style { Seconds = 0.30f, StartScale = 0.08f });
            Add(Classic, Lang.N("Classic menu"), DefaultMax, new Style { Seconds = 0.30f, StartScale = 0.05f, Anchor = new Vector2(0, -1), Unroll = Unroll.WidthFirst });
            // the windows a board's tab or a page's button opens
            Window("settingswin", Lang.N("Settings…"), new Style { Seconds = 0.40f, StartScale = 0.06f, Anchor = new Vector2(0, 1), Fold = new Vector3(-70, 0, 0) }, "ComfortWindow");
            Window("tutorial", Lang.N("Controls tutorial"), new Style { Seconds = 0.35f, StartScale = 0.06f, Anchor = new Vector2(1, 1), Fold = new Vector3(0, 0, 20) }, "TutorialWindow");
            Window("commands", Lang.N("Commands"), new Style { Seconds = 0.30f, StartScale = 0.08f, Anchor = new Vector2(-1, 1), Fold = new Vector3(0, 0, -25) }, "CommandsWindow", "PickerWindow");
            Window("leave", Lang.N("Leave the game…"), new Style { Seconds = 0.35f, StartScale = 0.05f, Fold = new Vector3(0, -100, 0) }, "LeaveWindow");
            Window("guide", Lang.N("Guide"), new Style { Seconds = 0.45f, StartScale = 0.05f, Anchor = new Vector2(1, 0), Fold = new Vector3(0, -110, 0) }, "GuideWindow");
            Window("worldmap", Lang.N("World map"), new Style { Seconds = 0.45f, StartScale = 0.04f, Unroll = Unroll.WidthFirst }, "WorldMapWindow");
            Window("horde", Lang.N("Horde"), new Style { Seconds = 0.40f, StartScale = 0.05f, Fold = new Vector3(-40, 0, 0), Arc = new Vector3(0, 0, -0.10f), Spring = true }, "HordePanel");
            Window("cart", Lang.N("Cart"), new Style { Seconds = 0.40f, StartScale = 0.05f, Anchor = new Vector2(0, -1), Fold = new Vector3(0, 0, 30), Arc = new Vector3(0.12f, 0, 0) }, "CartWindow");
            Window("market", Lang.N("~board~Market"), new Style { Seconds = 0.40f, StartScale = 0.05f, Anchor = new Vector2(0, -1), Fold = new Vector3(0, 0, -30), Arc = new Vector3(-0.12f, 0, 0) }, "MarketWindow");
            Window("bank", Lang.N("Bank"), new Style { Seconds = 0.45f, StartScale = 0.05f, Unroll = Unroll.HeightFirst }, "BankWindow");
            Window("cashshop", Lang.N("Cash shop"), new Style { Seconds = 0.35f, StartScale = 0.05f, Fold = new Vector3(0, 0, 15), Spring = true }, "CashShopWindow");
            Window("mail", Lang.N("Mail"), new Style { Seconds = 0.40f, StartScale = 0.05f, Anchor = new Vector2(0, 1), Fold = new Vector3(80, 0, 0) }, "MailWindow");
            Window("achievements", Lang.N("Achievements"), new Style { Seconds = 0.45f, StartScale = 0.05f, Anchor = new Vector2(0, -1), Arc = new Vector3(0, 0.15f, 0), Spring = true }, "AchievementWindow");
            Window("checkin", Lang.N("Daily check-in"), new Style { Seconds = 0.35f, StartScale = 0.06f, Anchor = new Vector2(-1, 1), Unroll = Unroll.WidthFirst }, "CheckInWindow");
            Window("guild", Lang.N("Guild"), new Style { Seconds = 0.45f, StartScale = 0.05f, Anchor = new Vector2(0, 1), Unroll = Unroll.HeightFirst }, "GuildWindow");
            Window("companionwin", Lang.N("Companion window"), new Style { Seconds = 0.35f, StartScale = 0.06f, Fold = new Vector3(0, 0, -20), Arc = new Vector3(0, 0.08f, 0), Spring = true }, "CompanionWindow");
            Window("swap", Lang.N("Swap set"), new Style { Seconds = 0.30f, StartScale = 0.08f, Anchor = new Vector2(1, 0), Fold = new Vector3(0, -60, 0) }, "SwapWindow");
            Window("roulette", Lang.N("Roulette"), new Style { Seconds = 0.45f, StartScale = 0.04f, Fold = new Vector3(0, 0, 360) }, "RouletteWindow");
            Window("chat", Lang.N("Chat rooms"), new Style { Seconds = 0.35f, StartScale = 0.06f, Anchor = new Vector2(-1, -1), Fold = new Vector3(0, 40, 0) }, "ChatRoomWindow", "WhisperWindow", "ChannelWindow");
            Window("booking", Lang.N("Booking"), new Style { Seconds = 0.35f, StartScale = 0.06f, Anchor = new Vector2(1, -1), Fold = new Vector3(0, -40, 0) }, "BookingWindow");
            Window("battlegrounds", Lang.N("Battlegrounds"), new Style { Seconds = 0.40f, StartScale = 0.05f, Fold = new Vector3(60, 0, 0), Arc = new Vector3(0, 0, -0.08f) }, "BattlegroundWindow");
            Window("reputation", Lang.N("Reputation"), new Style { Seconds = 0.30f, StartScale = 0.07f, Anchor = new Vector2(0, 1), Arc = new Vector3(0, 0.06f, 0) }, "ReputationWindow");
        }

        private static void Add(string key, string name, float max, Style style) { Register(key, name, max); Styles[key] = style; }
        private static void Window(string key, string name, Style style, params string[] panels) { Add(key, name, DefaultMax, style); foreach (var p in panels) WindowKeys[p] = key; }

        // ------------------------------------------------------------------ the registry
        /// <summary>A menu with an opening animation: its key, its name in the switches' list (English, a text table key: say it through
        /// Lang.N where you register) and the longest its animation may take. Registered again: the name and the limit are replaced.</summary>
        public static Menu Register(string key, string displayName, float maxSeconds = DefaultMax)
        {
            if (string.IsNullOrEmpty(key)) key = "?";
            if (!ByKey.TryGetValue(key, out var m)) { m = new Menu { Key = key }; ByKey[key] = m; Ordered.Add(m); }
            m.Name = string.IsNullOrEmpty(displayName) ? key : displayName; m.MaxSeconds = Mathf.Max(0.05f, maxSeconds);
            return m;
        }
        public static Menu Get(string key) => key != null && ByKey.TryGetValue(key, out var m) ? m : Register(key, key);
        /// <summary>Every menu registered, in the order of the switches' list.</summary>
        public static IReadOnlyList<Menu> Menus => Ordered;
        public static float MaxSeconds(string key) => Get(key).MaxSeconds;
        /// <summary>The key's owner plays its own animation: the registry's out-of-the-wrist one is never played for it (its switch, its
        /// limit and its 5 s clock stay here: the owner asks <see cref="ShouldPlay"/> and says <see cref="Closed"/>).</summary>
        public static void Own(string key) { Get(key).Owned = true; }
        /// <summary>Client wave 21 (w21inv): an owner's own opening has ended: how long it took (the registry's record of the last
        /// opening, as for the menus it plays itself).</summary>
        public static void Played(string key, float seconds) { var m = Get(key); m.LastSeconds = seconds; m.LastMoving = seconds; }
        /// <summary>The key's next opening on a tracked panel plays nothing (it is not opened from the wrist: the bag pulled out of the backpack).</summary>
        public static void SkipNext(string key) { Get(key).SkipNext = true; }

        /// <summary>The menu's switch (default On; kept with the account).</summary>
        public static bool IsOn(string key) { var off = UserSettings.Prefs.menuAnimOff; return off == null || Array.IndexOf(off, key) < 0; }
        public static void SetOn(string key, bool on)
        {
            if (IsOn(key) == on) return;
            var l = new List<string>(UserSettings.Prefs.menuAnimOff ?? new string[0]);
            if (on) l.RemoveAll(k => k == key); else l.Add(key);
            UserSettings.Prefs.menuAnimOff = l.ToArray(); UserSettings.Prefs.saved = true;
            UserSettings.Save();
            Debug.Log($"[Anim] {key}: switched {(on ? "on" : "off")}");
            Changed?.Invoke();
        }
        /// <summary>Every registered menu's switch at once.</summary>
        public static void SetAll(bool on)
        {
            var l = new List<string>(); if (!on) foreach (var m in Ordered) l.Add(m.Key);
            UserSettings.Prefs.menuAnimOff = l.ToArray(); UserSettings.Prefs.saved = true;
            UserSettings.Save();
            Debug.Log($"[Anim] every menu: switched {(on ? "on" : "off")}");
            Changed?.Invoke();
        }


        /// <summary>Ask ONCE when the menu opens: true = play its opening animation now. False: switched off, closed less than
        /// <see cref="ReopenSeconds"/> s ago, or a test run that wants every menu finished at once.</summary>
        public static bool ShouldPlay(string key)
        {
            var m = Get(key); string why = null;
            float since = Time.unscaledTime - m.ClosedAt;
            if (why == null && !IsOn(key)) why = "switched off";
            if (why == null && since < ReopenSeconds) why = $"reopened {since:0.0} s after closing";
            if (why != null) { Skip(m, why); return false; }
            m.Plays++;
            return true;
        }
        private static void Skip(Menu m, string why) { m.Skips++; m.LastSkip = why; Debug.Log($"[Anim] {m.Key}: no animation ({why})"); }

        /// <summary>The menu closed now: its 5 s clock starts.</summary>
        public static void Closed(string key) { if (key != null) Get(key).ClosedAt = Time.unscaledTime; }
        /// <summary>Seconds since the menu closed (a large number: never).</summary>
        public static float SinceClosed(string key) => Time.unscaledTime - Get(key).ClosedAt;

        // ------------------------------------------------------------------ out of the wrist
        private sealed class Run
        {
            public Transform Root; public Menu Menu; public Style Style;
            public float Opened, T0, End, Seconds, Progress, Moving; public int Frames;
            public bool Applied; public Vector3 LocalPos, LocalScale; public Quaternion LocalRot;
            public CanvasGroup Group; public float Alpha1;
            // client wave 23 (w23anim): it starts on the pressed button of the panel that asked (not at the wrist): the button's place
            // and facing in Space (the space that panel stood in; none: the world)
            public bool FromPanel, HasSpace; public Transform Space; public Vector3 From; public Quaternion FromRot;
        }
        private static readonly List<Run> Runs = new List<Run>();
        private static MenuAnimDriver _driver;

        /// <summary>The key's own style (a key without one: a plain flight).</summary>
        public static Style StyleOf(string key) => key != null && Styles.TryGetValue(key, out var s) ? s : new Style { Seconds = 0.35f, StartScale = 0.06f };
        /// <summary>Gives a key its style (a menu registered later).</summary>
        public static void SetStyle(string key, Style style) { Get(key); Styles[key] = style; }

        /// <summary>Plays the key's opening on <paramref name="root"/> (a panel's root: a world-space canvas) if <see cref="ShouldPlay"/>
        /// says so: out of the wrist to its own place and size, in the key's style (or <paramref name="style"/>), never longer than the
        /// key's limit. The root's own pose is not changed for anything but the drawing (see the class). True: it plays.</summary>
        public static bool Play(string key, Transform root, Style? style = null)
        {
            if (root == null || !ShouldPlay(key)) return false;
            Begin(Get(key), root, style ?? StyleOf(key));
            return true;
        }

        /// <summary><paramref name="answered"/>: the game opened it now while it handled the servers' news (the answer to a press, if any).</summary>
        private static void Begin(Menu m, Transform root, Style style, bool answered = false)
        {
            Finish(root);
            var r = new Run { Root = root, Menu = m, Style = style, Opened = Time.unscaledTime, Seconds = Mathf.Clamp(style.Seconds, 0.05f, m.MaxSeconds) };
            if (!style.NoFade)
            {
                r.Group = root.GetComponent<CanvasGroup>();
                if (r.Group == null) r.Group = root.gameObject.AddComponent<CanvasGroup>();
                r.Alpha1 = r.Group.alpha;
            }
            // client wave 23 (w23anim): asked for by a press on an open panel: it starts on that button (see "the press that asked")
            var p = Last;
            if (AskedOnPanel(root, answered)) { r.FromPanel = true; r.HasSpace = p.HasSpace; r.Space = p.Space; r.From = p.Point; r.FromRot = p.Facing; }
            m.LastFromPanel = r.FromPanel; m.LastFrom = r.FromPanel ? p.Name : "the wrist";
            Runs.Add(r); EnsureDriver();
            Debug.Log(r.FromPanel ? $"[Anim] {m.Key}: out of the panel that asked ({p.Name}), {r.Seconds:0.00} s ('{root.name}')" : $"[Anim] {m.Key}: out of the wrist, {r.Seconds:0.00} s ('{root.name}')");
        }

        // ------------------------------------------------------------------ client wave 23 (w23anim): the press that asked
        // The last press of a button (UI/MenuNav.cs BeforeClick, before the button's action runs: the action may close the panel it is on,
        // and the menu that REPLACES that panel still starts where the button was). Its place is kept in the space the pressed panel
        // stands in (the rig's: it stays right when the player is moved or turned meanwhile). It is THE press that asked for a menu when
        //   the menu opens while that press is handled, or is seen open by the next look at the watched panels (the same frame): Fresh; or
        //   the game opens the menu as the server's answer (the roulette, the daily check-in, the buying store) and the press was one of
        //   the player's own menus' less than MenuNav.LaterSeconds ago (wave 13's time for such an answer; wave 21's rule for "asked").
        // A press counts as "on an open panel" unless its panel is the ring (the wrist's own menu), a HUD piece or a casting tool's own
        // picture (UI/UiKit.cs PanelKind.Hud / Own: never an open menu). Every other opening (the ring's pick by the stick or a trigger,
        // a controller button, the backpack, a command, B's step back) has no press of its own: the wrist.
        private sealed class Press
        {
            public bool Fresh, Origin, Player, HasSpace; public float At = float.NegativeInfinity;
            public Transform Panel, Space; public Vector3 Point; public Quaternion Facing = Quaternion.identity; public string Name = "";
        }
        private static readonly Press Last = new Press();
        /// <summary>The panels that are the wrist itself (by their name): a press on one asks from the wrist.</summary>
        private static readonly HashSet<string> WristPanels = new HashSet<string> { "MenuRing" };

        /// <summary>A button is being pressed (the outermost press only); <paramref name="player"/>: on one of the player's own menus
        /// (UI/MenuNav.cs PlayerPressAt's rule).</summary>
        internal static void Pressed(UiButton b, bool player)
        {
            if (b == null) return;
            var panel = b.Panel; var p = Last;
            p.Fresh = true; p.Player = player; p.At = Time.unscaledTime; p.Panel = panel;
            var kind = panel != null ? PanelKinds.KindOf(panel) : PanelKind.Hud;
            p.Origin = panel != null && kind != PanelKind.Hud && kind != PanelKind.Own && !WristPanels.Contains(panel.name);
            p.Space = panel != null ? panel.parent : null; p.HasSpace = p.Space != null;
            var rt = b.transform as RectTransform;
            Vector3 at = rt != null ? rt.TransformPoint(rt.rect.center) : b.transform.position; Quaternion facing = b.transform.rotation; // (the button's own facing: a wing or a bent strip is turned against its panel)
            p.Point = p.HasSpace ? p.Space.InverseTransformPoint(at) : at;
            p.Facing = p.HasSpace ? Quaternion.Inverse(p.Space.rotation) * facing : facing;
            string label = b.Text != null ? b.Text.text : null;
            p.Name = (panel != null ? panel.name : "?") + " / " + (string.IsNullOrEmpty(label) ? b.name : label.Replace('\n', ' '));
        }

        /// <summary>The menu opening on <paramref name="root"/> now was asked for by the last press, and that press was on an open panel.</summary>
        private static bool AskedOnPanel(Transform root, bool answered)
        {
            var p = Last;
            if (!p.Origin || p.Panel == root || (p.HasSpace && p.Space == null)) return false; // (the ring, a HUD piece, no press yet; its own button; that space is gone)
            return p.Fresh || (answered && p.Player && Time.unscaledTime - p.At < MenuNav.LaterSeconds);
        }

        /// <summary>An opening animation is playing on this root now; how far it is (0-1; 1: none).</summary>
        public static bool Playing(Transform root) { foreach (var r in Runs) if (r.Root == root) return true; return false; }
        public static float Progress(Transform root) { foreach (var r in Runs) if (r.Root == root) return r.Progress; return 1f; }
        /// <summary>Ends the root's animation now (the picture is at its place).</summary>
        public static void Finish(Transform root) { for (int i = Runs.Count - 1; i >= 0; i--) if (Runs[i].Root == root) EndRun(i, false); }

        private static void EndRun(int i, bool done)
        {
            var r = Runs[i]; Runs.RemoveAt(i);
            if (r.Root != null)
            {
                if (r.Applied) PutBack(r);
                if (r.Group != null) r.Group.alpha = r.Alpha1;
            }
            if (done) { r.Menu.LastSeconds = Time.unscaledTime - r.Opened; r.Menu.LastMoving = r.Moving; Debug.Log($"[Anim] {r.Menu.Key}: at its place after {r.Menu.LastSeconds:0.000} s (last frame away from it at {r.Moving:0.000} s, {r.Frames} frames)"); }
        }

        private static void PutBack(Run r)
        {
            r.Applied = false;
            r.Root.localPosition = r.LocalPos; r.Root.localRotation = r.LocalRot; r.Root.localScale = r.LocalScale;
        }

        /// <summary>The true poses back (when the frame has been drawn; again before anything of the next frame runs).</summary>
        internal static void Restore() { for (int i = 0; i < Runs.Count; i++) { var r = Runs[i]; if (r.Applied) { if (r.Root != null) PutBack(r); else r.Applied = false; } } }

        /// <summary>After every script's LateUpdate: the tracked panels are looked at, then every playing animation's pose is written
        /// for this frame's drawing.</summary>
        internal static void Tick()
        {
            Restore();
            Watch();
            float now = Time.unscaledTime;
            for (int i = Runs.Count - 1; i >= 0; i--)
            {
                var r = Runs[i];
                if (r.Root == null || !r.Root.gameObject.activeInHierarchy) { EndRun(i, false); continue; }
                r.Frames++;
                if (r.Frames == 2)
                {
                    // the clock starts with the second frame (the first drew the panel at the wrist, see-through): a long first frame takes at
                    // most MaxFirstStep of the animation; the whole stays inside the menu's limit, counted from the frame it opened
                    r.T0 = Mathf.Max(r.Opened, now - MaxFirstStep);
                    r.End = Mathf.Min(r.T0 + r.Seconds, r.Opened + r.Menu.MaxSeconds - EndMargin);
                }
                float t = r.Frames < 2 ? 0f : r.End - r.T0 < 0.01f ? 1f : Mathf.Clamp01((now - r.T0) / (r.End - r.T0));
                if (t >= 1f) { EndRun(i, true); continue; }
                Apply(r, t);
            }
            Last.Fresh = false; // (client wave 23: a menu that opens from now on was not opened by that press)
        }

        private static void Apply(Run r, float t)
        {
            var root = r.Root;
            r.LocalPos = root.localPosition; r.LocalRot = root.localRotation; r.LocalScale = root.localScale; r.Applied = true; r.Progress = t;
            r.Moving = Time.unscaledTime - r.Opened;
            Vector3 pos = root.position; Quaternion rot = root.rotation; Vector2 half = HalfSize(root);
            // client wave 23 (w23anim): from the pressed button of the panel that asked (its place and facing in the space that panel
            // stood in), else from the wrist with the panel's own facing, as before
            if (r.FromPanel && r.HasSpace && r.Space == null) r.FromPanel = false; // (that space is gone: the wrist)
            Vector3 from = !r.FromPanel ? WristPoint(pos, rot, half) : r.HasSpace ? r.Space.TransformPoint(r.From) : r.From;
            Quaternion fromRot = !r.FromPanel ? rot : r.HasSpace ? r.Space.rotation * r.FromRot : r.FromRot;
            Pose(r.Style, t, from, fromRot, pos, rot, half, out var p, out var q, out var mul, out float alpha);
            root.SetPositionAndRotation(p, q);
            root.localScale = Vector3.Scale(r.LocalScale, mul);
            if (r.Group != null) r.Group.alpha = r.Alpha1 * alpha;
        }

        /// <summary>Half the panel's size in metres.</summary>
        public static Vector2 HalfSize(Transform root)
        {
            if (!(root is RectTransform rt)) return Vector2.zero;
            var s = rt.lossyScale; var rc = rt.rect;
            return new Vector2(Mathf.Abs(rc.width * s.x), Mathf.Abs(rc.height * s.y)) * 0.5f;
        }

        /// <summary>The wrist's place now (no hand known: 25 cm under the panel).</summary>
        public static Vector3 WristPoint(Vector3 panelPos, Quaternion panelRot, Vector2 half)
        {
            Transform w = null; try { if (Wrist != null) w = Wrist(); } catch (Exception) { }
            return w != null ? w.position : panelPos + panelRot * new Vector3(0f, -half.y - 0.25f, 0f);
        }

        private static float OutCubic(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }
        private static float OutBack(float x) { const float c1 = 1.2f, c3 = c1 + 1f; x = Mathf.Clamp01(x) - 1f; return 1f + c3 * x * x * x + c1 * x * x; }

        /// <summary>The picture's pose at the share <paramref name="t"/> (0-1) of a style's time: where the panel is drawn, how it is turned,
        /// its size against its own (per axis) and its alpha. At 1: the panel's own pose, size 1, alpha 1.</summary>
        public static void Pose(Style s, float t, Vector3 wrist, Vector3 pos, Quaternion rot, Vector2 half, out Vector3 outPos, out Quaternion outRot, out Vector3 scale, out float alpha)
            => Pose(s, t, wrist, rot, pos, rot, half, out outPos, out outRot, out scale, out alpha);

        /// <summary>Client wave 23 (w23anim): the same picture starting at <paramref name="from"/> facing <paramref name="fromRot"/> (the
        /// pressed button of the panel that asked: its place and that panel's facing there; out of the wrist: the wrist and the panel's
        /// own facing, which gives the pose above): the facing turns to the panel's own along the way.</summary>
        public static void Pose(Style s, float t, Vector3 from, Quaternion fromRot, Vector3 pos, Quaternion rot, Vector2 half, out Vector3 outPos, out Quaternion outRot, out Vector3 scale, out float alpha)
        {
            Vector3 wrist = from;
            t = Mathf.Clamp01(t);
            float E(float x) => s.Spring ? OutBack(x) : OutCubic(x);
            float p = E(t), first = E(t / 0.55f), second = E((t - 0.25f) / 0.75f);
            float s0 = Mathf.Clamp(s.StartScale, 0.01f, 1f);
            float sx = Mathf.LerpUnclamped(s0, 1f, s.Unroll == Unroll.WidthFirst ? first : s.Unroll == Unroll.HeightFirst ? second : p);
            float sy = Mathf.LerpUnclamped(s0, 1f, s.Unroll == Unroll.HeightFirst ? first : s.Unroll == Unroll.WidthFirst ? second : p);
            scale = new Vector3(sx, sy, Mathf.LerpUnclamped(s0, 1f, p));
            var face = fromRot == rot ? rot : Quaternion.Slerp(fromRot, rot, Mathf.Clamp01(p)); // (client wave 23: out of the wrist: its own facing throughout, as before)
            outRot = face * Quaternion.Euler(s.Fold * (1f - p));
            var anchorEnd = pos + rot * new Vector3(s.Anchor.x * half.x, s.Anchor.y * half.y, 0f);
            var onPath = Vector3.LerpUnclamped(wrist, anchorEnd, p) + rot * (s.Arc * Mathf.Sin(Mathf.PI * t));
            outPos = onPath - outRot * new Vector3(s.Anchor.x * half.x * sx, s.Anchor.y * half.y * sy, 0f);
            alpha = s.NoFade ? 1f : Mathf.SmoothStep(0f, 1f, t / FadeShare);
        }

        // ------------------------------------------------------------------ the panels that open and close by themselves
        private sealed class Tracked
        {
            public Transform Root; public Func<bool> IsOpen; public Func<string> Key; public MenuNav.Menu Nav;
            public bool Seen, Open; public string OpenKey;
        }
        private static readonly List<Tracked> Watched = new List<Tracked>();
        private static int _navSeen;

        /// <summary>Watches a panel: when <paramref name="isOpen"/> turns true the key's animation plays on <paramref name="root"/> (if
        /// <see cref="ShouldPlay"/> says so and the key is not <see cref="Own"/>ed); when it turns false the key's 5 s clock starts.
        /// <paramref name="key"/> is asked while it is open (a board shows another menu in place: the old one counts as closed, no animation).</summary>
        public static void Track(Transform root, Func<bool> isOpen, Func<string> key)
        {
            if (root == null || isOpen == null || key == null) return;
            Watched.Add(new Tracked { Root = root, IsOpen = isOpen, Key = key });
            EnsureDriver();
        }

        private static void Watch()
        {
            // the windows registered with the one-menu-at-a-time rule (every ListWindow, the Guide, the World map) whose names the table knows
            var all = MenuNav.All;
            for (; _navSeen < all.Count; _navSeen++)
            {
                var m = all[_navSeen];
                if (m == null || m.Panel == null || m.Name == null || !WindowKeys.TryGetValue(m.Name, out var key)) continue;
                Watched.Add(new Tracked { Root = m.Panel, IsOpen = m.IsOpen, Key = () => key, Nav = m, Seen = true }); // (a window made when it first opens, the battlegrounds' list: open at the first look = opened now)
            }
            for (int i = Watched.Count - 1; i >= 0; i--)
            {
                var w = Watched[i];
                if (w.Root == null) { Watched.RemoveAt(i); continue; }
                bool open; string key = null;
                try { open = w.IsOpen(); if (open) key = w.Key(); } catch (Exception e) { Debug.LogException(e); open = false; }
                if (!w.Seen) { w.Seen = true; w.Open = open; w.OpenKey = key; continue; } // (as it is when first looked at: no animation)
                if (open && !w.Open) { w.Open = true; w.OpenKey = key; Opened(w, key); }
                else if (!open && w.Open) { w.Open = false; Closed(w.OpenKey); w.OpenKey = null; }
                else if (open && key != w.OpenKey) { Closed(w.OpenKey); w.OpenKey = key; }
            }
        }

        private static void Opened(Tracked w, string key)
        {
            if (key == null) return;
            var m = Get(key);
            if (m.Owned) return; // (its owner asks ShouldPlay and plays its own)
            if (m.SkipNext) { m.SkipNext = false; Skip(m, "not opened from the wrist"); return; }
            // (a window that opens on the server's answer to a press of one of the player's menus, the roulette, the daily check-in, the buying
            // store, was asked for by the player: UI/MenuNav.cs expects it, or a button of the player's menus was pressed less than
            // MenuNav.LaterSeconds ago: the roulette's button hides the menu before the answer comes)
            bool asked = MenuNav.Expecting || Time.unscaledTime - MenuNav.PlayerPressAt < MenuNav.LaterSeconds;
            if (w.Nav != null && MenuNav.ByGame(w.Nav) && !asked) { Skip(m, "opened by the game"); return; }
            if (w.Nav != null && MenuNav.HudOpened == w.Nav && MenuNav.HudOpenedFrame == Time.frameCount) { Skip(m, "opened from a HUD panel"); return; }
            if (!ShouldPlay(key)) return;
            Begin(m, w.Root, StyleOf(key), w.Nav != null && w.Nav.GameOpenedFrame == Time.frameCount); // (client wave 23: the game opened it in this frame: the server's answer)
        }

        /// <summary>The key of a board's group (the ring's name for it, English).</summary>
        public static string KeyOfGroup(string groupName)
        {
            switch (groupName)
            {
                case "Character": return "character";
                case "Items": return Inventory;
                case "Skills": return Skills;
                case "Quests": return "quests";
                case "Companions": return "companions";
                case "Social": return "social";
                case "World": return "world";
                case "Settings": return "settings";
                default: return groupName != null ? groupName.ToLowerInvariant() : null;
            }
        }
        /// <summary>The key of a window by its panel's name (null: not one the wrist menu opens).</summary>
        public static string KeyOfWindow(string panelName) => panelName != null && WindowKeys.TryGetValue(panelName, out var k) ? k : null;

        private static void EnsureDriver()
        {
            if (_driver != null) return;
            var go = new GameObject("MenuAnim");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<MenuAnimDriver>();
            go.AddComponent<MenuAnimEarly>();
        }
    }

    /// <summary>Client wave 21 (w21anim): after every other script's LateUpdate (the classic wrist menu follows its hand there, the view
    /// controller moves the world at 500, the casting's pictures up to 1100) the opening animations' poses are written for the drawing;
    /// when the frame has been drawn the panels' own poses are put back (UI/MenuAnim.cs).</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class MenuAnimDriver : MonoBehaviour
    {
        private void OnEnable() { StartCoroutine(AfterDrawing()); }
        private void LateUpdate() { MenuAnim.Tick(); }
        private IEnumerator AfterDrawing() { var eof = new WaitForEndOfFrame(); while (true) { yield return eof; MenuAnim.Restore(); } }
        private void OnDisable() { MenuAnim.Restore(); }
    }

    /// <summary>Client wave 21 (w21anim): before every other script (and before the physics step): a pose the end of the last frame did
    /// not put back (no drawing that frame) is put back now.</summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class MenuAnimEarly : MonoBehaviour
    {
        private void FixedUpdate() { MenuAnim.Restore(); }
        private void Update() { MenuAnim.Restore(); }
    }
}
