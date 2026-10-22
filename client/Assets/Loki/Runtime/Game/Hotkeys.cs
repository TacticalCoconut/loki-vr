using System;
using Loki.Net;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Server-saved shortcut bar (research/qol_packets.md §5; HOTKEY_SAVING, common/mmo.hpp:21-40). At PACKETVER 20211103 (RE)
    /// rAthena uses the 2019+ packets (map/packets_struct.hpp:1582-1656, clif_packetdb.hpp:1958-1959):
    ///   0x0b20 ZC_SHORTCUT_KEY_LIST rotate.B tab.W {isSkill.B id.L count.W}×38 — sent twice on connect, tab 0 and tab 1
    ///          (clif.cpp:10908-10910, clif_hotkeys_send clif.cpp:11217-11245; rotate = the tab's hotkey_rowshift / hotkey_rowshift2);
    ///   0x0b21 CZ_SHORTCUT_KEY_CHANGE2 tab.W index.W isSkill.B id.L count.W — sets one slot (no ack; clif_parse_Hotkey
    ///          clif.cpp:11266-11278 stores it in hotkeys[index + tab*38]); isSkill 0 id 0 clears it;
    ///   0x0b22 CZ_SHORTCUTKEYBAR_ROTATE2 tab.W rowshift.B — the tab's row shift (clif_parse_HotkeyRowShift clif.cpp:11248-11256).
    /// The character server saves both (char.cpp:298-328 hotkey_rowshift / hotkey_rowshift2), so the same bar comes back on the next
    /// login and is shared with the official client's F-key bar. MAX_HOTKEYS 38 per tab = 4 rows of 9 + 2 quick slots
    /// (mmo.hpp:31-33: "9 skills x 4 bars &amp; 2 Quickslots"); two tabs (MAX_HOTKEYS_DB = 76, mmo.hpp:36-37).
    /// Client wave 4 (w4vr): Loki's quick bar shows one row of 9 of the 8 rows (tab 0 rows 1-4, tab 1 rows 1-4); the row switcher
    /// sends 0x0b22 with the row inside its tab. rAthena only stores that byte (it never reads it: clif.cpp:11252-11256); Loki reads
    /// it back as "the row shown" (0-3). The official client's own reading of the byte is not in rAthena or roBrowserLegacy (rB does
    /// not implement the rotation): the only risk is that the official client starts on another row. Which TAB is shown is not saved
    /// by rAthena at all: Loki keeps it per character in the user settings (World/UserSettings.cs). The two extra quick slots
    /// (36, 37) are kept as they are.
    /// </summary>
    public sealed class Hotkeys
    {
        public struct Slot { public bool IsSkill; public int Id, Count; public bool Empty => Id == 0; }
        public const int PerTab = 38, QuickSlots = 9, RowsPerTab = 4, Rows = RowsPerTab * 2;
        public readonly Slot[] Slots = new Slot[PerTab * 2];
        public int Rotate0, Rotate1;
        public bool Loaded { get; private set; }
        public event Action Changed;
        /// <summary>The row the quick bar shows: 0-3 tab 0, 4-7 tab 1.</summary>
        public int CurrentRow { get; private set; }
        public int CurrentTab => CurrentRow / RowsPerTab;
        public int CurrentRowInTab => CurrentRow % RowsPerTab;
        /// <summary>The character whose bar this is (the per-character shown tab in the user settings).</summary>
        public string Character = "";
        /// <summary>Tests: the 0x0b21 / 0x0b22 requests sent, as text.</summary>
        public readonly System.Collections.Generic.List<string> Sent = new System.Collections.Generic.List<string>();
        private readonly RoSession _s;
        private bool _tab0, _tab1;

        public Hotkeys(RoSession s) { _s = s; }

        public void Reset() { Array.Clear(Slots, 0, Slots.Length); Loaded = false; _tab0 = _tab1 = false; }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x0b20 || n < 5 + PerTab * 7) return false;
            var r = new PacketReader(b, o, n); r.Skip(2); int rotate = r.U8(); int tab = r.U16();
            if (tab < 0 || tab > 1) return true;
            if (tab == 0) { Rotate0 = rotate; _tab0 = true; } else { Rotate1 = rotate; _tab1 = true; }
            for (int i = 0; i < PerTab; i++)
            {
                var s = new Slot { IsSkill = r.U8() != 0, Id = r.S32(), Count = r.U16() };
                Slots[tab * PerTab + i] = s;
            }
            Loaded = true;
            int used = 0; for (int i = 0; i < PerTab; i++) if (!Slots[tab * PerTab + i].Empty) used++;
            // the shown row (both lists come once, on the first map entry: clif.cpp:10904-10911): the character's shown tab (user
            // settings), at that tab's row shift (0-3; anything else is logged and shown as the tab's row 1)
            int showTab = World.UserSettings.QuickTabOf(Character);
            if (showTab == 1 && !_tab1) showTab = 0; // tab 1's list follows right after tab 0's
            CurrentRow = RowOf(showTab);
            Debug.Log($"[Hotkeys] tab {tab}: {used} slots set, rotate {rotate}{(rotate > 3 ? " (not a row 0-3: row 1 shown)" : "")}; showing bar {CurrentTab + 1} row {CurrentRowInTab + 1}");
            Changed?.Invoke();
            return true;
        }

        private int RowOf(int tab) { int shift = tab == 0 ? Rotate0 : Rotate1; return tab * RowsPerTab + (shift >= 0 && shift < RowsPerTab ? shift : 0); }
        /// <summary>Client wave 18 (w18acct): shows a tab at its row (the account's settings name another shown tab than this PC's).</summary>
        public void ShowTab(int tab) { if (tab == 1 && !_tab1) tab = 0; if (Loaded && tab != CurrentTab) ShowRow(RowOf(tab)); }

        /// <summary>The index in <see cref="Slots"/> of slot i (0-8) of row <paramref name="row"/> (0-7).</summary>
        public static int SlotIndex(int row, int i) => (row / RowsPerTab) * PerTab + (row % RowsPerTab) * QuickSlots + i;

        /// <summary>Slot i (0-8) of the shown row.</summary>
        public Slot Quick(int i) => Slots[SlotIndex(CurrentRow, Mathf.Clamp(i, 0, QuickSlots - 1))];
        public Slot At(int row, int i) => Slots[SlotIndex(Mathf.Clamp(row, 0, Rows - 1), Mathf.Clamp(i, 0, QuickSlots - 1))];

        /// <summary>Sets slot i (0-8) of the shown row and tells the server.</summary>
        public void Set(int i, bool isSkill, int id, int count)
        {
            if (i < 0 || i >= QuickSlots) return;
            SetAt(CurrentRow, i, isSkill, id, count);
        }
        /// <summary>Sets slot i of row <paramref name="row"/>: CZ_SHORTCUT_KEY_CHANGE2 with the tab and the index inside it.</summary>
        public void SetAt(int row, int i, bool isSkill, int id, int count)
        {
            if (row < 0 || row >= Rows || i < 0 || i >= QuickSlots) return;
            int tab = row / RowsPerTab, index = (row % RowsPerTab) * QuickSlots + i;
            Slots[tab * PerTab + index] = new Slot { IsSkill = isSkill, Id = id, Count = count };
            _s.SetHotkey(tab, index, isSkill, id, count);
            Sent.Add($"0x0b21 tab {tab} index {index} {(isSkill ? "skill" : "item")} {id} x{count}");
            Debug.Log($"[Hotkeys] bar {tab + 1} row {row % RowsPerTab + 1} slot {i + 1} = {(isSkill ? "skill" : "item")} {id} x{count} (0x0b21 tab {tab} index {index})");
            Changed?.Invoke();
        }
        public void Clear(int i) => Set(i, false, 0, 0);

        /// <summary>Puts an item/skill in the first free slot of the shown row (or its last slot when the row is full); -1 if it is already there.</summary>
        public int Pin(bool isSkill, int id, int count)
        {
            for (int i = 0; i < QuickSlots; i++) if (Quick(i).IsSkill == isSkill && Quick(i).Id == id) return -1;
            int free = QuickSlots - 1;
            for (int i = 0; i < QuickSlots; i++) if (Quick(i).Empty) { free = i; break; }
            Set(free, isSkill, id, count);
            return free;
        }

        /// <summary>The bar switcher: the next (+1) or previous (-1) of the 8 rows. Moving inside a tab sends that tab's row shift
        /// (0x0b22); moving to the other tab remembers the tab for this character (the server keeps no "shown tab").</summary>
        public void SwitchRow(int delta) => ShowRow(((CurrentRow + delta) % Rows + Rows) % Rows);

        public void ShowRow(int row)
        {
            row = Mathf.Clamp(row, 0, Rows - 1);
            int oldTab = CurrentTab;
            CurrentRow = row;
            int tab = CurrentTab, shift = CurrentRowInTab;
            int saved = tab == 0 ? Rotate0 : Rotate1;
            if (saved != shift)
            {
                if (tab == 0) Rotate0 = shift; else Rotate1 = shift;
                _s.HotkeyRowShift(tab, shift);
                Sent.Add($"0x0b22 tab {tab} rowshift {shift}");
            }
            if (tab != oldTab || World.UserSettings.QuickTabOf(Character) != tab) { World.UserSettings.SetQuickTab(Character, tab); World.UserSettings.Save(); }
            Debug.Log($"[Hotkeys] showing bar {tab + 1} row {shift + 1} (row {row + 1} of {Rows}){(saved != shift ? $", 0x0b22 tab {tab} rowshift {shift}" : "")}");
            Changed?.Invoke();
        }

        // ---- client wave 18 (w18share), the design's point 33: a FRIEND's bar as their casting menu showed it (UI/FriendGestures.cs keeps one
        // copy per friend's menu, made with no session: its Slots are written from the menu's state). The row it shows is set here with no
        // packet and no saved setting, and the pictures drawing from it are told (Changed).
        public void ShowMirrored(int row) { CurrentRow = Mathf.Clamp(row, 0, Rows - 1); Loaded = true; Changed?.Invoke(); }
    }
}
