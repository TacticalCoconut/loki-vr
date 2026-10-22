using System;
using System.Collections.Generic;

namespace Loki.Game
{
    /// <summary>
    /// The official client's slash commands (client wave 4, w4social). The list and the help lines come from the client's own
    /// msgstringtable (data/msgstrings.tsv): 33-46 (/h, /w /who, /where, /ex /in /exall /inall), 107 and 136-139 (/organize, /deal,
    /// /leave, /expel), 132-135 (/sit /stand /chat /q), 211 (/memo), 313 and 757 (/savechat /report), 724 / 727 / 728 (/doridori
    /// /bingbing /bangbang), 784 (/emotion), 804 (/notrade), 852 (/pvpinfo), 918 (/hi), 923 / 924 / 952 (/blacksmith /alchemist
    /// /pk), 1043 (/loginout), 3422-3426 (/block /unblock /blockall /unblockall). Each command either sends the request rAthena
    /// parses for it (the owner registers the action and cites the parser) or is answered by the client alone; a slash line that
    /// is no command is never sent as chat (msgstringtable 95 "Invalid Command", as the official client answers).
    /// Commands whose help line is not in the table (/cl, /invite, /guildinvite, /breakguild, /taekwon) come from roBrowserLegacy
    /// (src/Controls/ProcessCommand.js — a reimplementation, not the official client); the owner says so where it registers them.
    /// </summary>
    public sealed class SlashCommands
    {
        public sealed class Command
        {
            public string[] Names;
            /// <summary>msgstringtable id of the official help line, or -1 (then <see cref="Help"/> is Loki's own line).</summary>
            public int HelpMsg = -1; public string Help = "";
            /// <summary>Runs the command with the text after the command word (trimmed; "" when none). False = "not mine after all"
            /// (e.g. "/w Name text" is a whisper, not /who), and the line goes on to the next handler.</summary>
            public Func<string, bool> Run;
        }

        private readonly List<Command> _list = new List<Command>();
        private readonly Dictionary<string, Command> _byName = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<Command> All => _list;
        /// <summary>The last command word run (smoke).</summary>
        public string Last { get; private set; } = "";

        public void Add(int helpMsg, string help, Func<string, bool> run, params string[] names)
        {
            var c = new Command { Names = names, HelpMsg = helpMsg, Help = help ?? "", Run = run };
            _list.Add(c);
            foreach (var n in names) _byName[n] = c;
        }

        /// <summary>"/who" → the command and "" ; "/deal Some Name" → the command and "Some Name"; null when the word is no command.</summary>
        public Command Find(string line, out string args)
        {
            args = "";
            if (string.IsNullOrEmpty(line) || line[0] != '/') return null;
            int sp = line.IndexOf(' ');
            string word = (sp < 0 ? line.Substring(1) : line.Substring(1, sp - 1)).Trim();
            if (!_byName.TryGetValue(word, out var c)) return null;
            args = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            return c;
        }

        /// <summary>Runs a console line if it is one of the commands; false otherwise (or when the command declines it).</summary>
        public bool TryRun(string line)
        {
            var c = Find(line, out string args);
            if (c == null) return false;
            if (!c.Run(args)) return false;
            int sp = line.IndexOf(' ');
            Last = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
            return true;
        }

        /// <summary>The help line of a command: the official msgstringtable text when there is one, else Loki's.</summary>
        public static string HelpOf(Command c) => c.HelpMsg >= 0 ? ClientText.Msg(c.HelpMsg) : Lang.T(c.Help); // w4lang: Loki's own help lines are table keys (Lang.N where they are registered)

        /// <summary>Strips the quotes the official help shows around names (/organize ''Party Name'').</summary>
        public static string Unquote(string s)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("''") && s.EndsWith("''") && s.Length >= 4) s = s.Substring(2, s.Length - 4);
            else if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s.Trim();
        }
    }
}
