using System;
using System.Collections.Generic;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 11 (w11menu), design specification: "Provide a button on the menu that lists out important
    /// commands for the console. Such as changing class, adding items, one that lists out the IDs for all the items in the game, and
    /// any other important command like warp and stuff." A list window (UI/ListWindow.cs) of rAthena's @commands a game master uses
    /// most, each with its syntax and one line saying what it does. "To chat" puts the command into the chat line and opens the VR
    /// keyboard, so the player completes its parameters; a command that needs none also has "Send", which sends it at once.
    /// The syntax, the aliases and what each does are rAthena's own (e985006, C:\rAthena): conf/atcommands.yml (each
    /// command's aliases and help; @go's town numbers :474-496) and doc/atcommands.txt, checked against src/map/atcommand.cpp
    /// (mapmove :595-640: map then optional x y; jump :729: no parameter = a random cell; heal :1371: none = full; killmonster :2440:
    /// no map = this map; item :1462: the amount is optional; jobchange :1203: an ID or a name; speed :1002, zeny :2922, baselevelup
    /// :1664, where :652 need their parameter). The commands are sent as chat lines, which is how rAthena takes them (and the
    /// Loki Online module); nothing here is specific to one server. The server refuses them for an account without the rights.
    /// @itemreset has no "Send" on purpose: it removes every item at once.
    /// </summary>
    public sealed class GmCommandList
    {
        public sealed class Cmd
        {
            /// <summary>The syntax as shown, what goes into the chat line, and the help line's key (English: Lang translates it).</summary>
            public string Syntax, Fill, Help;
            /// <summary>The command works without parameters: it gets the "Send" button.</summary>
            public bool Sendable;
            /// <summary>A heading (no command).</summary>
            public bool Heading;
        }

        private static Cmd H(string title) => new Cmd { Syntax = title, Heading = true };
        private static Cmd C(string syntax, string help, bool sendable = false)
        {
            int sp = syntax.IndexOf(' ');
            string name = sp < 0 ? syntax : syntax.Substring(0, sp);
            return new Cmd { Syntax = syntax, Fill = sendable && sp < 0 ? name : name + " ", Help = help, Sendable = sendable };
        }

        /// <summary>The list, in the window's order.</summary>
        public static readonly Cmd[] All =
        {
            H(Lang.N("Class, levels, skills")),
            C("@job <job name or ID>", Lang.N("Changes your class (also @jobchange). 0 Novice, 1 Swordsman, 2 Mage, 3 Archer, 4 Acolyte, 5 Merchant, 6 Thief; the whole list: @help job")),
            C("@blvl <+/- levels>", Lang.N("Raises your base level, or lowers it with a minus (also @lvup, @baselvl).")),
            C("@jlvl <+/- levels>", Lang.N("Raises or lowers your job level (also @joblvl).")),
            C("@allskill", Lang.N("Gives you every skill of your class (also @skillall)."), true),
            C("@questskill <skill ID>", Lang.N("Gives you a quest skill: 142 First Aid, 143 Play Dead; the whole list: @help questskill")),
            C("@stpoint <+/- points>", Lang.N("Gives or takes status points.")),
            C("@skpoint <+/- points>", Lang.N("Gives or takes skill points.")),
            C("@allstats [<+/- amount>]", Lang.N("Raises every stat by the amount; with no amount, to the maximum."), true),
            C("@resetstat", Lang.N("Gives your status points back (also @streset)."), true),
            C("@resetskill", Lang.N("Gives your skill points back (also @skreset)."), true),
            H(Lang.N("Items and money")),
            C("@item <item name or ID> [<amount>]", Lang.N("Gives you the item (1 without an amount).")),
            C("@item2 <item> <amount> <identified 0/1> <refine> <broken 0/1> <card1> <card2> <card3> <card4>", Lang.N("An item made to order: refine level and cards.")),
            C("@idsearch <part of an item's name>", Lang.N("Lists the items whose name holds that text, with their IDs.")),
            C("@iteminfo <item name or ID>", Lang.N("An item's type, price and weight (also @ii).")),
            C("@delitem <item name or ID> <amount>", Lang.N("Removes that many of the item from your bag.")),
            C("@itemreset", Lang.N("Removes EVERY item you carry (also @clearinventory). Only through the chat line: there is no way back.")),
            C("@zeny <+/- amount>", Lang.N("Gives or takes zeny.")),
            C("@storage", Lang.N("Opens your Kafra storage where you stand."), true),
            H(Lang.N("Travel")),
            C("@go <town name or number>", Lang.N("Warps you to a town. With no town it lists them.")),
            new Cmd { Syntax = "0 prontera · 1 morocc · 2 geffen · 3 payon · 4 alberta · 5 izlude · 6 aldebaran · 7 xmas (lutie) · 8 comodo · 9 yuno · 10 amatsu · 11 gonryun · 12 umbala", Heading = false, Fill = "@go " },
            new Cmd { Syntax = "13 niflheim · 14 louyang · 15 start point · 16 prison · 17 jawaii · 18 ayothaya · 19 einbroch · 20 lighthalzen · 21 einbech · 22 hugel · 23 rachel · 24 veins", Heading = false, Fill = "@go " },
            new Cmd { Syntax = "25 moscovia · 26 midgard camp · 27 manuk · 28 splendide · 29 brasilis · 30 el dicastes · 31 mora · 32 dewata · 33 malangdo · 34 malaya port · 35 eclage · 36 lasagna", Heading = false, Fill = "@go " },
            C("@warp <map> [<x> <y>]", Lang.N("Warps you to a map, to that cell or a random one (also @rura, @mapmove). Example: @warp prt_fild08 170 375")),
            C("@jump [<x> <y>]", Lang.N("Moves you to a cell of this map; with no cell, to a random one."), true),
            C("@save", Lang.N("Makes this spot your save point."), true),
            C("@load", Lang.N("Warps you to your save point (also @return)."), true),
            C("@memo [<0-2>]", Lang.N("Stores this spot as a Warp Portal memo; with no number it lists them."), true),
            C("@where <character name>", Lang.N("Says which map and cell a player is on.")),
            C("@who [<name>]", Lang.N("Lists the players online (also @whois)."), true),
            H(Lang.N("Monsters")),
            C("@monster <monster name or ID> [<amount>]", Lang.N("Summons monsters beside you (also @spawn). Example: @monster 1002 5")),
            C("@mobinfo <monster name or ID>", Lang.N("A monster's stats, experience and drops (also @mi).")),
            C("@killmonster [<map>]", Lang.N("Kills every monster of this map (or of the map named); they drop their items."), true),
            C("@killmonster2", Lang.N("The same without drops."), true),
            H(Lang.N("Yourself")),
            C("@heal [<HP> <SP>]", Lang.N("Refills HP and SP, or adds the amounts given."), true),
            C("@alive", Lang.N("Brings you back to life."), true),
            C("@speed <1-1000>", Lang.N("Your walking speed: 150 is normal, lower is faster.")),
            C("@hide", Lang.N("Makes you invisible to players and monsters; again to show yourself."), true),
            H(Lang.N("Help")),
            C("@commands", Lang.N("Lists every command your account may use."), true),
            C("@help <command>", Lang.N("Explains a command and its parameters (also @h).")),
        };

        private readonly ListWindow _win; private readonly Func<Camera> _head;
        /// <summary>Puts a text into the chat line and opens the keyboard; sends a chat line at once.</summary>
        public Action<string> ToChat, Send;
        /// <summary>Opens a picker (0 items, 1 jobs, 2 places, 3 monsters: Game/GmPickers.cs).</summary>
        public Action<int> OpenPicker;
        /// <summary>Commands put into the chat line and sent from the list (smoke, log), and the last of each.</summary>
        public int Filled { get; private set; }
        public int Sent { get; private set; }
        public string LastFilled { get; private set; } = "";
        public string LastSent { get; private set; } = "";

        public GmCommandList(ListWindow win, Func<Camera> head) { _win = win; _head = head; _win.OnCloseClicked = Hide; }
        /// <summary>Client wave 14 (w14kit), point 24: the "Presets" tab (Game/CharPresets.cs), shown for a GM only (PresetsShown: the GM
        /// check of LokiGame.W14Kit.cs); Opened runs at every Show (the check starts there); FillPresets draws the tab into this window.</summary>
        public Func<bool> PresetsShown;
        public Action Opened;
        public Action<ListWindow> FillPresets;
        /// <summary>The tab shown: 0 the commands, 1 the presets.</summary>
        public int Tab { get; private set; }
        public void ShowTab(int t) { Tab = t; Refresh(); }
        public bool Visible => _win.Visible;
        public int RowCount => _win.RowCount;
        public string RowText(int i) => _win.RowText(i);
        public void Press(int i, bool a = true) => _win.Press(i, a);
        public void Hide() => _win.Hide();

        public void Show()
        {
            _win.SetTabs(null, 0, null); _win.SetInput(false);
            _win.Open(_head());
            Opened?.Invoke();
            Refresh();
        }

        /// <summary>'&lt;' as text: uGUI reads "&lt;b&gt;", "&lt;i&gt;", "&lt;color&gt;" and "&lt;size&gt;" as tags; a zero-width space after it keeps a
        /// parameter's angle bracket a bracket (UiKit.RoColors does the same for item descriptions).</summary>
        private static string Plain(string s) => s.Replace("<", "<\u200B");

        /// <summary>The row index of a command by its name ("@heal"); -1: not in the list.</summary>
        public static int IndexOf(string name)
        {
            for (int i = 0; i < All.Length; i++) if (!All[i].Heading && All[i].Help != null && (All[i].Syntax == name || All[i].Syntax.StartsWith(name + " "))) return i;
            return -1;
        }

        public void Refresh()
        {
            if (!_win.Visible) return;
            // client wave 14 (w14kit), point 24: the tabs "Commands" / "Presets" (a GM only)
            bool presets = PresetsShown != null && PresetsShown() && FillPresets != null;
            if (!presets) Tab = 0;
            _win.SetTabs(presets ? new[] { Lang.T("Commands"), Lang.T("Presets") } : null, Tab, t => ShowTab(t));
            if (presets && Tab == 1) { FillPresets(_win); return; }
            _win.SetInput(false);
            _win.SetTitle(Lang.T("Console commands"));
            _win.SetInfo(Lang.T("The server only takes these from an account with the rights (Loki Host: Accounts, \"Make GM\")."));
            var rows = new List<ListWindow.Row>();
            foreach (var c in All)
            {
                if (c.Heading) { rows.Add(new ListWindow.Row { Text = "<b>" + Lang.T(c.Syntax) + "</b>", Highlight = true }); continue; }
                var cmd = c;
                if (c.Help == null) { rows.Add(new ListWindow.Row { Text = "<color=#9aa3b5>" + c.Syntax + "</color>", A = Lang.T("To chat"), OnA = () => Fill(cmd) }); continue; } // @go's town numbers
                rows.Add(new ListWindow.Row
                {
                    Text = "<color=#ffd060>" + Plain(c.Syntax) + "</color>\n<color=#c8cbd6>" + Lang.T(c.Help) + "</color>",
                    A = Lang.T("To chat"), OnA = () => Fill(cmd),
                    B = c.Sendable ? Lang.T("Send") : null, OnB = c.Sendable ? () => SendNow(cmd) : (Action)null,
                });
            }
            _win.SetRows(rows, false);
            // the pickers that fill a command's parameters without typing (Game/GmPickers.cs): every item with its ID, the jobs, the places
            _win.SetFooter((Lang.T("Close"), Hide), (Lang.T("Places…"), () => OpenPicker?.Invoke(2)), (Lang.T("Jobs…"), () => OpenPicker?.Invoke(1)), (Lang.T("Item browser…"), () => OpenPicker?.Invoke(0)));
        }

        private void Fill(Cmd c)
        {
            Filled++; LastFilled = c.Fill;
            Debug.Log($"[Commands] to the chat line: '{c.Fill}'");
            ToChat?.Invoke(c.Fill);
        }

        private void SendNow(Cmd c)
        {
            string line = c.Fill.Trim();
            Sent++; LastSent = line;
            Debug.Log($"[Commands] sent: '{line}'");
            Send?.Invoke(line);
        }
    }
}
