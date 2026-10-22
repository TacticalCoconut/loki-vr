using System;
using System.Collections.Generic;
using UnityEngine;

namespace Loki.UI
{
    /// <summary>
    /// Client wave 13 (w13items), the brief's eleventh item (design note, 2026-10-07: "I open a menu such as, for example, opening the main menu and
    /// then going to the items menu. The previous menu needs to close because otherwise it starts to crowd my screen. But do Provide a
    /// little arrow button on the top left that brings me back to the menu that I was before"; and ~11:50: "Toggleable, with the default
    /// being 'close the menu outright'"). ONE navigation rule for the new menu style (Classic is left as it is: <see cref="Enabled"/>).
    /// THE PLAYER'S MENU: at most one is open at a time, <see cref="Current"/>: the ring, the board, or a window the player opened.
    /// Every menu that can be one is registered (<see cref="Register"/>: the ring and the board by UI/WristMenu.Board.cs, every
    /// ListWindow, the Guide and the World map) with how to close it (its owner's close: a window's X, which releases what the server
    /// holds open) and how to bring it back exactly as it was.
    /// OPENED FROM ANOTHER: a press of a button of the current menu (UiButton.Click: <see cref="BeforeClick"/> / <see cref="AfterClick"/>)
    /// that opens another registered menu closes the current one and puts it on the HISTORY (a stack), with what it held (place, size,
    /// page, the board's group / tab / item / lock ...) and the button that had opened it. The new one shows a BACK ARROW at its top
    /// left (<see cref="MakeArrow"/>: "←", at least as large as the board's X); the arrow (<see cref="Back"/>) closes it and brings back
    /// the menu below, step by step. The ring's pick opens a board with an arrow back to the ring. Windows that open only when the
    /// server answers (the daily check-in, the roulette, the buying store) are told apart by the button that asks for them
    /// (<see cref="OpensLater"/>): a window of those that opens within <see cref="LaterSeconds"/> s, with no other press between, counts
    /// as opened from that menu.
    /// IN PLACE: the board's own steps keep the board but change what it shows (the item's full Details, the quick-slot picker, a page
    /// of another group opened by a button of the board): the board's state is pushed as an in-place step (<see cref="PushInPlace"/>);
    /// back puts it back on the same board, which does not move.
    /// X AND B on a menu opened from another (Settings → Comfort "B and X": <see cref="BackMode"/>): "close" (the default) closes it
    /// outright and clears the history; "back" is the arrow. On a menu not opened from another they do what they did.
    /// A FRESH START: the menu button (the ring), or a window opened from a HUD panel (the minimap's World map ...), closes the current
    /// menu and clears the history. A menu that closes by any other way (its own button, the game) ends the history too
    /// (<see cref="Tick"/>).
    /// WINDOWS THE GAME OPENS BY ITSELF (an NPC dialog, a shop or storage after it, a trade or party request, the check-in at login ...):
    /// they open beside the player's menu, which stays as it is with its history; they get no arrow and are no step of the history.
    /// CLASSIC (client wave 15, w15menu, point 10; design note: "When I'm using the classic menu ... when I open any tab on it, for example, the
    /// settings menu, the menu on the left hand doesn't actually close ... when I click a menu, that would justify it being closed, it should
    /// close"): in the Classic style the classic wrist menu (<see cref="ClassicMenu"/>) follows the same rule: a press of one of its buttons
    /// that opens a registered window closes the wrist menu and puts it on the history; the window gets the back arrow (it brings the wrist
    /// menu back on the page it showed) and B / X follow "B and X". Nothing else changes in Classic (a window opened from another window
    /// opens beside it, as before).
    /// </summary>
    public static class MenuNav
    {
        public sealed class Menu
        {
            public string Name; public Transform Panel;
            public Func<bool> IsOpen;
            /// <summary>Closes it outright (its owner's close).</summary>
            public Action Close;
            /// <summary>What it holds now (place, page ...), taken when the player leaves it for another menu (null: nothing).</summary>
            public Func<object> Capture;
            /// <summary>Brings it back: the button that opened it the first time (null: none) and what <see cref="Capture"/> took.</summary>
            public Action<UiButton, object> Reopen;
            /// <summary>Shows / hides its back arrow (null: it has none: the ring).</summary>
            public Action<bool> Arrow;
            /// <summary>Client wave 23 (w23anim): the frame in which the game opened it while it handled the servers' news (-1: never;
            /// <see cref="GameOpensEnd"/>). It stays after the menu is adopted as the player's (UI/MenuAnim.cs: the server's answer to a
            /// press flies out of the pressed button).</summary>
            public int GameOpenedFrame = -1;
        }
        private sealed class Step { public Menu Menu; public UiButton Opener; public object State; public bool InPlace; public string Label; }

        /// <summary>The time a window that opens on the server's answer may take to count as opened from the menu that asked.</summary>
        public const float LaterSeconds = 3f;
        private static readonly List<Menu> _menus = new List<Menu>();
        private static readonly List<Step> _history = new List<Step>();
        private static Menu _cur; private static UiButton _curOpener;
        private static readonly HashSet<UiButton> _later = new HashSet<UiButton>();
        /// <summary>The new menu style is in force (WristMenu: false = Classic: nothing here acts).</summary>
        public static Func<bool> Enabled;
        /// <summary>X and B on a menu opened from another: 0 close outright (the default), 1 back (VR/Comfort.cs MenuBack).</summary>
        public static Func<int> BackMode;
        public static bool On => Enabled != null && Enabled();
        // ---- client wave 15 (w15menu), point 10: Classic (see the class)
        /// <summary>The classic wrist menu (registered by UI/WristMenu.Board.cs; its Panel is the wrist panel) and whether the Classic style
        /// is in force.</summary>
        public static Menu ClassicMenu;
        public static Func<bool> ClassicStyle;
        private static bool IsClassic => !On && ClassicStyle != null && ClassicStyle() && ClassicMenu != null && ClassicMenu.Panel != null;
        /// <summary>Classic: a history from the classic wrist menu is alive (a window opened from it, or one on its way).</summary>
        private static bool ClassicLive => IsClassic && (_history.Count > 0 || _pending.HasValue);
        /// <summary>The rule acts now: the new style, or Classic with a history from its wrist menu.</summary>
        private static bool Live => On || ClassicLive;
        private static bool _classicClick;
        /// <summary>Presses on the classic wrist menu that opened a window, which closed it (smoke, log).</summary>
        public static int ClassicCloses { get; private set; }
        public static Menu Current => _cur;
        public static string CurrentName => _cur != null ? _cur.Name : "";
        public static int Depth => _history.Count;
        public static string HistoryText { get { var l = new List<string>(); foreach (var s in _history) l.Add(s.Menu.Name + (s.InPlace ? "(" + s.Label + ")" : "")); return string.Join(" > ", l); } }
        /// <summary>Counts (smoke, log).</summary>
        public static int Pushes { get; private set; }
        public static int Backs { get; private set; }
        public static int Clears { get; private set; }
        public static int Fresh { get; private set; }
        public static int Beside { get; private set; }

        public static void Register(Menu m) { if (m != null && !_menus.Contains(m)) _menus.Add(m); }
        /// <summary>Client wave 21 (w21anim), point 63: every registered menu, in the order registered (UI/MenuAnim.cs watches the windows
        /// the wrist menu opens), and the menu a HUD panel's button opened last with the frame it did (not opened from the wrist).</summary>
        public static IReadOnlyList<Menu> All => _menus;
        public static Menu HudOpened { get; private set; }
        public static int HudOpenedFrame { get; private set; } = -1;
        /// <summary>A press of the current menu asked the server for a window (<see cref="OpensLater"/>) and it has not come yet: a window
        /// that opens now while the servers' news is handled is the answer to that press (the player's: <see cref="Tick"/> adopts it).</summary>
        public static bool Expecting => _pending.HasValue;
        /// <summary>When a button of one of the player's own menus was last pressed (the ring, a board, the classic wrist menu, a window the
        /// player opened; unscaled seconds). A page's button may hide the menu and ask the server for its window (the roulette): that window,
        /// opening within <see cref="LaterSeconds"/> of the press, was asked for by the player (UI/MenuAnim.cs: it flies out of the wrist).</summary>
        public static float PlayerPressAt { get; private set; } = float.NegativeInfinity;
        public static Menu Find(Transform panel) { foreach (var m in _menus) if (m.Panel == panel) return m; return null; }
        /// <summary>This button asks the server for a window that opens later (see the class).</summary>
        public static void OpensLater(UiButton b) { if (b != null) _later.Add(b); }

        /// <summary>The registered menus shown now (smoke: "never two player-opened menus at once").</summary>
        public static List<string> OpenNames() { var l = new List<string>(); foreach (var m in _menus) if (m.Panel != null && m.IsOpen()) l.Add(m.Name); return l; }

        // ------------------------------------------------------------------ the presses
        private static int _clickDepth; private static bool[] _wasOpen; private static object _preState; private static Transform _clickPanel; private static bool _busy;
        private static (Menu from, UiButton opener, object state, float until, bool[] wasOpen)? _pending; private static bool _expectLater;
        /// <summary>Said inside a press: its window opens on the server's answer (see <see cref="OpensLater"/>).</summary>
        public static void ExpectLater() { if (InClick) _expectLater = true; }
        /// <summary>A press is being handled now (a menu opened meanwhile is decided when it ends).</summary>
        public static bool InClick => _clickDepth > 0;

        internal static void BeforeClick(UiButton b)
        {
            if (b != null) // (client wave 21, w21anim, point 63)
            {
                var panel = b.Panel; var pm = panel != null ? Find(panel) : null;
                bool player = (pm != null && !ByGame(pm)) || (ClassicMenu != null && panel != null && panel == ClassicMenu.Panel);
                if (player) PlayerPressAt = Time.unscaledTime;
                // client wave 23 (w23anim), the design's answer 53: where the press is (the button's place on its panel, before its action can close
                // that panel): a menu this press opens flies out of that button, not out of the wrist (UI/MenuAnim.cs; the outermost press only)
                if (_clickDepth == 0) MenuAnim.Pressed(b, player);
            }
            if (_clickDepth++ > 0) return; // a press inside a press: the outer one decides
            _pending = null; _expectLater = false; _classicClick = false;
            // client wave 15 (w15menu), point 10: a press on the classic wrist menu (Classic style, the menu shown): it is the player's menu now
            if (!On && !_busy && IsClassic && b != null && ClassicMenu.IsOpen() && b.transform.IsChildOf(ClassicMenu.Panel))
            {
                _classicClick = true;
                if (_cur != ClassicMenu) { if (_history.Count > 0) { _history.Clear(); Clears++; } _cur = ClassicMenu; _curOpener = null; Arrows(); }
                _clickPanel = ClassicMenu.Panel; _wasOpen = OpenStates();
                _preState = ClassicMenu.Capture != null ? ClassicMenu.Capture() : null;
                return;
            }
            if (!On || _busy) { _wasOpen = null; return; }
            _clickPanel = b != null ? b.Panel : null;
            _wasOpen = OpenStates();
            // what the current menu holds before the press changes it (a board may hide itself before it opens a window)
            _preState = _cur != null && _cur.Capture != null && _clickPanel == _cur.Panel ? _cur.Capture() : null;
        }

        internal static void AfterClick(UiButton b)
        {
            if (--_clickDepth > 0) return;
            bool classic = _classicClick; _classicClick = false;
            if (!(On || classic) || _busy || _wasOpen == null) return;
            var was = _wasOpen; _wasOpen = null;
            var opened = Newly(was);
            if (opened != null) { if (classic) ClassicCloses++; Opened(opened, b, _preState, _clickPanel); return; }
            if (b != null && (_later.Contains(b) || _expectLater) && _cur != null && _clickPanel == _cur.Panel)
                _pending = (_cur, b, _preState, Time.unscaledTime + LaterSeconds, OpenStates());
        }

        private static bool[] OpenStates() { var a = new bool[_menus.Count]; for (int i = 0; i < a.Length; i++) a[i] = _menus[i].Panel != null && _menus[i].IsOpen(); return a; }
        private static Menu Newly(bool[] was)
        {
            for (int i = 0; i < _menus.Count && i < was.Length; i++)
            {
                var m = _menus[i];
                if (!was[i] && m != _cur && m.Panel != null && m.IsOpen()) return m;
            }
            return null;
        }

        /// <summary>A registered menu opened by a press on <paramref name="from"/>'s panel.</summary>
        private static void Opened(Menu m, UiButton opener, object fromState, Transform fromPanel)
        {
            _byGame.Remove(m); // (client wave 14, point 29: opened by a press of the player, or on the server's answer to one: the player's)
            if (_cur != null && fromPanel == _cur.Panel)
            {
                // opened FROM the current menu: that one goes on the history and closes
                var leaving = _cur;
                _history.Add(new Step { Menu = leaving, Opener = _curOpener, State = fromState, Label = leaving.Name });
                Pushes++;
                _busy = true; try { if (leaving.IsOpen()) leaving.Close(); } catch (Exception e) { Debug.LogException(e); } finally { _busy = false; }
                _cur = m; _curOpener = opener;
                Debug.Log($"[Nav] {m.Name} opened from {leaving.Name}: {leaving.Name} closed, history {HistoryText}");
            }
            else if (fromPanel != null && PanelKinds.KindOf(fromPanel) == PanelKind.Hud)
            {
                // from a HUD panel (the minimap's World map ...): a fresh start
                HudOpened = m; HudOpenedFrame = Time.frameCount; // (client wave 21, point 63: no out-of-the-wrist animation for it)
                StartFresh(m, opener, "a HUD panel's button");
            }
            else
            {
                Beside++;
                Debug.Log($"[Nav] {m.Name} opened beside the player's menu ('{CurrentName}'): not from it");
                return;
            }
            Arrows();
        }

        /// <summary>A menu opened as a new start (the menu button's ring, a board opened by the game or a command, a HUD panel's window):
        /// the current menu closes, the history is cleared.</summary>
        public static void StartFresh(Menu m, UiButton opener = null, string why = "")
        {
            if (m != null) _byGame.Remove(m); // (client wave 14, point 29)
            if (!On || m == null) return;
            if (_cur != null && _cur != m && _cur.Panel != null && _cur.IsOpen()) { _busy = true; try { _cur.Close(); } catch (Exception e) { Debug.LogException(e); } finally { _busy = false; } }
            if (_history.Count > 0) Clears++;
            _history.Clear(); _cur = m; _curOpener = opener; Fresh++;
            Debug.Log($"[Nav] {m.Name}: a fresh start{(why.Length > 0 ? " (" + why + ")" : "")}");
            Arrows();
        }

        /// <summary>The board was shown. From the ring's pick: a step back to the ring; inside a press: decided when the press ends; else a
        /// fresh start.</summary>
        public static void BoardShown(Menu board, Menu ring, bool fromRing)
        {
            if (!On || board == null) return;
            if (fromRing && ring != null)
            {
                if (_cur != ring) StartFresh(ring);
                _history.Add(new Step { Menu = ring, Label = ring.Name }); Pushes++;
                _cur = board; _curOpener = null;
                Debug.Log($"[Nav] {board.Name} opened from {ring.Name}: history {HistoryText}");
                Arrows();
                return;
            }
            if (InClick) return; // AfterClick decides
            if (_cur != board) StartFresh(board, null, "shown by the game or a command");
        }

        /// <summary>The current menu (the board) changes what it shows: its state before is an in-place step of the history.</summary>
        public static void PushInPlace(Menu m, object state, string label)
        {
            if (!On || m == null || _cur != m) return;
            _history.Add(new Step { Menu = m, Opener = _curOpener, State = state, InPlace = true, Label = label }); Pushes++;
            Debug.Log($"[Nav] {m.Name}: {label} (in place), history {HistoryText}");
            Arrows();
        }
        /// <summary>The state the current menu had before the press being handled (null: none).</summary>
        public static object PreClickState(Menu m) => InClick && _cur == m ? _preState : null;
        /// <summary>The top of the history is an in-place step of <paramref name="m"/>.</summary>
        public static bool TopInPlace(Menu m) => _history.Count > 0 && _history[_history.Count - 1].InPlace && _history[_history.Count - 1].Menu == m && _cur == m;

        // ------------------------------------------------------------------ back, X, B
        /// <summary>The back arrow: the current menu closes and the one below on the history comes back as it was (true: there was one).</summary>
        public static bool Back()
        {
            if (_history.Count == 0) return false;
            var s = _history[_history.Count - 1]; _history.RemoveAt(_history.Count - 1);
            var leaving = _cur;
            _busy = true;
            try
            {
                if (!s.InPlace && leaving != null && leaving != s.Menu && leaving.Panel != null && leaving.IsOpen()) leaving.Close();
                s.Menu.Reopen?.Invoke(s.Opener, s.State);
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { _busy = false; }
            _cur = s.Menu; _curOpener = s.Opener; Backs++;
            Debug.Log($"[Nav] back from {(leaving != null ? leaving.Name : "-")} to {s.Menu.Name}{(s.InPlace ? " (" + s.Label + " undone)" : "")}, history {HistoryText}");
            Arrows();
            return true;
        }

        /// <summary>A menu's X was pressed (true: taken here: "back"); with "close" the history is cleared and its owner closes it.</summary>
        public static bool CloseButton(Transform panel)
        {
            if (!Live || _cur == null || _cur.Panel != panel) return false; // (client wave 15, point 10: Classic too, with a history from its wrist menu)
            if (_history.Count > 0 && BackMode != null && BackMode() == 1) return Back();
            if (_history.Count > 0) { _history.Clear(); Clears++; Debug.Log($"[Nav] X on {_cur.Name}: closed outright, the history cleared"); Arrows(); }
            return false;
        }

        /// <summary>B (true: taken here). On a menu opened from another: back, or closed outright with the history cleared ("close", the
        /// default). On any other: false (as before).</summary>
        public static bool B()
        {
            if (!Live || _cur == null || _cur.Panel == null || !_cur.IsOpen() || _history.Count == 0) return false; // (client wave 15, point 10: Live)
            if (BackMode != null && BackMode() == 1) return Back();
            var m = _cur; _history.Clear(); Clears++; _cur = null; _curOpener = null;
            _busy = true; try { m.Close(); } catch (Exception e) { Debug.LogException(e); } finally { _busy = false; }
            Debug.Log($"[Nav] B on {m.Name}: closed outright, the history cleared");
            Arrows();
            return true;
        }

        /// <summary>Forgets everything (the style changed): nothing is closed.</summary>
        public static void Reset()
        {
            if (_cur == null && _history.Count == 0) return;
            _history.Clear(); _cur = null; _curOpener = null; _pending = null; Arrows();
        }

        /// <summary>Every frame (UI/LokiHud.cs): a current menu that closed by any other way ends the history; a window the server opened
        /// on a menu's request is taken as opened from it.</summary>
        public static void Tick()
        {
            ForgetClosed(); // client wave 14 (w14ui), point 29: in both styles
            if (!Live) { Reset(); return; } // (client wave 15, point 10: Classic keeps a history from its wrist menu)
            if (_busy || InClick) return;
            if (_pending.HasValue)
            {
                var p = _pending.Value;
                if (Time.unscaledTime > p.until || _cur != p.from) _pending = null;
                else
                {
                    var m = Newly(p.wasOpen);
                    if (m != null) { _pending = null; Opened(m, p.opener, p.state, p.from.Panel); }
                }
            }
            if (_cur != null && (_cur.Panel == null || !_cur.IsOpen()))
            {
                Debug.Log($"[Nav] {_cur.Name} closed{(_history.Count > 0 ? ": the history cleared" : "")}");
                if (_history.Count > 0) Clears++;
                _history.Clear(); _cur = null; _curOpener = null; Arrows();
            }
        }

        private static void Arrows()
        {
            foreach (var m in _menus) { if (m.Arrow == null || m.Panel == null) continue; try { m.Arrow(m == _cur && _history.Count > 0); } catch (Exception e) { Debug.LogException(e); } }
        }

        // ------------------------------------------------------------------ client wave 14 (w14ui), point 29: the menu button closes everything
        // design note (2026-10-07): "Pressing the controller menu button while a panel is open immediately closes
        // everything, including the wrist menu"; and on the orchestrator's question "NPC dialogs, trade windows and HUD panels stay?": "Yes".
        // A registered menu (every ListWindow, the Guide, the World map, the ring, the board) is THE GAME'S when it opened while the game
        // handled what the servers sent (the RO session, Loki Online, Horde's relay) or by itself (the voice notice): an NPC's shop or
        // storage, a trade, a party / guild invitation, the check-in at login, Horde's invitation and results ... (LokiGame.W14Ui.cs wraps
        // those updates in <see cref="GameOpensBegin"/> / <see cref="GameOpensEnd"/>). Every other one is THE PLAYER'S (opened by a press,
        // the laser on a unit, a controller button, the backpack ...), and so is one the game opened on the server's answer to a press of
        // the player (<see cref="OpensLater"/>: adopted as the player's menu by <see cref="Opened"/>). The NPC dialog is not registered: it stays.
        private static readonly HashSet<Menu> _byGame = new HashSet<Menu>();
        private static bool[] _gameWas = new bool[0]; private static int _gameWasN = -1;
        /// <summary>The game is about to handle what the servers sent (once a frame: no allocation).</summary>
        public static void GameOpensBegin()
        {
            if (_gameWas.Length < _menus.Count) _gameWas = new bool[_menus.Count + 16];
            for (int i = 0; i < _menus.Count; i++) _gameWas[i] = _menus[i].Panel != null && _menus[i].IsOpen();
            _gameWasN = _menus.Count;
        }
        /// <summary>Every registered menu that opened since <see cref="GameOpensBegin"/> is the game's.</summary>
        public static void GameOpensEnd()
        {
            int n = _gameWasN; _gameWasN = -1;
            if (n < 0) return;
            for (int i = 0; i < _menus.Count; i++)
            {
                var m = _menus[i];
                if (!((i >= n || !_gameWas[i]) && m.Panel != null && m.IsOpen())) continue;
                m.GameOpenedFrame = Time.frameCount; // (client wave 23, w23anim)
                if (_byGame.Add(m)) Debug.Log($"[Nav] {m.Name} opened by the game: the menu button leaves it open");
            }
        }
        /// <summary>Windows only the game opens, from what it does by itself outside the servers' updates (Horde's relay runs in its own
        /// update: its invitation and its results; the first-time voice notice): always the game's.</summary>
        public static readonly HashSet<string> GameWindows = new HashSet<string> { "HordePrompt", "HordeResults", "VoiceNoticeWindow" };
        /// <summary>This menu was opened by the game (it stays when the menu button closes everything).</summary>
        public static bool ByGame(Menu m) => m != null && (_byGame.Contains(m) || GameWindows.Contains(m.Name));
        private static readonly List<Menu> _gone = new List<Menu>();
        private static void ForgetClosed()
        {
            if (_byGame.Count == 0) return;
            _gone.Clear(); foreach (var m in _byGame) if (m.Panel == null || !m.IsOpen()) _gone.Add(m);
            foreach (var m in _gone) _byGame.Remove(m);
        }
        /// <summary>The player's menus open now (registered ones only: the classic wrist menu and the keyboard are the caller's).</summary>
        public static List<string> PlayerOpenNames()
        {
            var l = new List<string>();
            foreach (var m in _menus) if (m.Panel != null && m.IsOpen() && !ByGame(m)) l.Add(m.Name);
            return l;
        }
        /// <summary>Closes every player's menu that is open (its owner's close: a window's X, which releases what the server holds open),
        /// and clears the history (back arrows gone). The game's stay. Returns their names.</summary>
        public static List<string> ClosePlayerMenus()
        {
            var closed = new List<string>();
            _busy = true;
            try
            {
                foreach (var m in _menus.ToArray())
                {
                    if (m.Panel == null || !m.IsOpen() || ByGame(m)) continue;
                    try { m.Close(); closed.Add(m.Name); } catch (Exception e) { Debug.LogException(e); }
                }
            }
            finally { _busy = false; }
            if (_history.Count > 0) Clears++;
            _history.Clear(); _cur = null; _curOpener = null; _pending = null; Arrows();
            return closed;
        }

        // ------------------------------------------------------------------ the arrow
        public const string ArrowGlyph = "←";
        public static readonly Color ArrowText = new Color(1f, 0.85f, 0.45f);
        /// <summary>The back arrow, the same everywhere: "←" in gold on a button, just outside its window's top left corner (its top level
        /// with the window's top), <paramref name="size"/> px; it calls <see cref="Back"/>.</summary>
        public static UiButton MakeArrow(RectTransform window, Vector2 windowSize, Vector2 size)
        {
            // client wave 21 (w21kit): a round gold button of the theme, clear of the window's wood frame (UiKit.OutsetOf)
            var b = UiKit.RoundButton(window, ArrowGlyph, new Vector2(-windowSize.x / 2 - 8 - UiKit.OutsetOf(window) - size.x / 2, windowSize.y / 2 - size.y / 2), size, () => Back(), Mathf.RoundToInt(size.y * 0.62f));
            b.name = "btn:back";
            b.Tip = () => Lang.T("~tip~Back"); // client wave 14 (w14menu), point 7
            if (!b.IsThemed) b.Text.color = ArrowText;
            b.gameObject.SetActive(false);
            return b;
        }

        /// <summary>A window's place and its page, to put back (ListWindow, Guide, World map).</summary>
        public sealed class Pose { public Vector3 Position; public Quaternion Rotation; public int Page; }
    }
}
