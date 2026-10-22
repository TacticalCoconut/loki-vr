using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Loki.Game.AiCore;
using Loki.UI;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Client wave 5 (w5ai): the custom homunculus / mercenary AI. The official client runs the RO folder's AI\USER_AI\AI.lua (homunculus)
    /// or AI\USER_AI\AI_M.lua (mercenary) instead of its basic AI when the player types /hoai or /merai (the guide's update history,
    /// 2005-07-19 item 3: "Command to toggle between basic and custom AI: /hoai"; /merai: roBrowserLegacy Controls/ProcessCommand.js
    /// 527-541 and msgstringtable 1273 / 1274). Loki runs that file with its own Lua 5.1 (Lua/*.cs) through the same client functions the
    /// default AI uses (Game/AiCore); with no file, or after any error, the default AI (Game/AiCore/DefaultAi.cs) runs — the official
    /// client halts with a message instead (the guide, section 2). The messages: msgstringtable 1023 "Homunculus has been customized." /
    /// 1024 "… activated with the basic AI.", 1273 / 1274 for the mercenary (roBrowserLegacy shows 1024 when switching to custom — the
    /// table's texts say the opposite; Loki shows them as they read), 2913 "[%s] file is not existing. Activating default AI".
    /// TraceAI writes to TraceAI.txt while /traceai is on (the guide 5-5) — in the per-user folder, not the RO install.
    /// </summary>
    public sealed partial class Companions
    {
        public bool HomCustomAi => World.UserSettings.Prefs.aiHomCustom;
        public bool MercCustomAi => World.UserSettings.Prefs.aiMercCustom;
        /// <summary>The RO folder (it holds AI\), set by the game from loki.json's clientRoot.</summary>
        public Func<string> W5AiRoot;
        /// <summary>A line in the chat (text, colour).</summary>
        public Action<string, string> W5AiNotice;
        /// <summary>/traceai: TraceAI lines go to TraceAI.txt.</summary>
        public bool W5TraceOn { get; private set; }
        /// <summary>The custom files (the official client's names; a smoke may point elsewhere).</summary>
        public string W5HomFile = "AI/USER_AI/AI.lua", W5MercFile = "AI/USER_AI/AI_M.lua";
        public static string W5TracePath => Path.Combine(World.UserSettings.Dir, "TraceAI.txt");
        /// <summary>Where scripts write their own files (the io library): the per-user folder, never the RO install.</summary>
        public static string W5WriteRoot => Path.Combine(World.UserSettings.Dir, "AI files");

        private AiFiles W5Files() => new AiFiles(W5AiRoot?.Invoke() ?? "", W5WriteRoot);

        /// <summary>/hoai or /merai: switch, save, start the AI again (roBrowserLegacy resetAI), the client's message.</summary>
        public void W5ToggleCustomAi(bool merc)
        {
            var p = World.UserSettings.Prefs;
            bool on;
            if (merc) on = p.aiMercCustom = !p.aiMercCustom; else on = p.aiHomCustom = !p.aiHomCustom;
            World.UserSettings.Save();
            if (merc) _mercAi = null; else _homAi = null; // the next tick starts it again with the chosen AI
            W5AiNotice?.Invoke(ClientText.Msg(merc ? (on ? 1273 : 1274) : (on ? 1023 : 1024)), "9ad1ff");
            Debug.Log($"[W5Ai] {(merc ? "mercenary" : "homunculus")} AI: {(on ? "custom" : "basic")}");
            Refresh();
        }

        /// <summary>/traceai: TraceAI.txt on / off (the guide 5-5).</summary>
        public void W5ToggleTrace()
        {
            W5TraceOn = !W5TraceOn;
            W5AiNotice?.Invoke(W5TraceOn ? Lang.F("TraceAI on: the AI writes its trace to {0}", W5TracePath) : Lang.T("TraceAI off."), "9ad1ff");
        }

        /// <summary>A new brain: its custom script when the player chose it.</summary>
        private void W5AiStart(W4Brain b)
        {
            if (!(b.Merc ? MercCustomAi : HomCustomAi)) return;
            string file = b.Merc ? W5MercFile : W5HomFile;
            var s = AiScript.Start(W5Files(), file, b.Merc, out string err, t => W5Trace(b, t), o => W5Print(b, o));
            if (s == null)
            {
                string shown = file.Replace('/', '\\');
                if (err == "missing")
                {
                    b.ScriptProblem = Lang.F("{0} is missing", shown);
                    W5AiNotice?.Invoke(ClientText.Fill(ClientText.Msg(2913), shown), "ffb070");
                }
                else
                {
                    b.ScriptProblem = err;
                    W5AiNotice?.Invoke(Lang.F("The custom AI ({0}) could not start: {1}. The basic AI takes over.", shown, err), "ff8a7a");
                }
                Debug.Log($"[W5Ai] {(b.Merc ? "mercenary" : "homunculus")} {b.MyId}: custom AI {file} not started: {err}");
                return;
            }
            b.Script = s;
            b.ScriptProblem = null;
            Debug.Log($"[W5Ai] {(b.Merc ? "mercenary" : "homunculus")} {b.MyId}: custom AI {file} started");
            W5ShadowStart(b);
        }

        /// <summary>AI(id) of the custom script; on an error the default AI takes over (until the companion appears again or the
        /// player switches).</summary>
        private void W5AiRunScript(W4Brain b, double[] msg, double[] rmsg)
        {
            W5ShadowBefore();
            if (b.Script.Tick(b.MyId, msg, rmsg, _world, _actions)) { W5ShadowAfter(b, msg, rmsg); return; }
            string shown = b.Script.FileName.Replace('/', '\\');
            b.ScriptProblem = b.Script.Error;
            Debug.Log($"[W5Ai] {(b.Merc ? "mercenary" : "homunculus")} {b.MyId}: custom AI stopped after {b.Script.Ticks} tick(s): {b.Script.Error}{(b.Script.ErrorTraceback != null ? "\n" + b.Script.ErrorTraceback : "")}");
            W5AiNotice?.Invoke(Lang.F("The custom AI ({0}) stopped: {1}. The basic AI takes over.", shown, b.Script.Error), "ff8a7a");
            b.Script = null;
            Refresh();
        }

        /// <summary>print() / io.write of a script: the official client is a window program with no console, so its scripts' print goes
        /// nowhere; Loki writes it to TraceAI.txt while /traceai is on (for script authors), and to the log in development builds.</summary>
        private void W5Print(W4Brain b, string text)
        {
            W5Trace(b, "print: " + text);
        }

        private void W5Trace(W4Brain b, string text)
        {
            if (!W5TraceOn) return;
            try { File.AppendAllText(W5TracePath, text + "\r\n", new UTF8Encoding(false)); } // plain text, no byte-order mark
            catch (IOException e) { Debug.LogWarning("[W5Ai] TraceAI.txt: " + e.Message); W5TraceOn = false; }
            catch (UnauthorizedAccessException e) { Debug.LogWarning("[W5Ai] TraceAI.txt: " + e.Message); W5TraceOn = false; }
        }

        /// <summary>The companion window's AI row: which AI runs, a button to switch (the official client has only /hoai and /merai; a
        /// button is Loki's, for a player without a keyboard in VR).</summary>
        private ListWindow.Row W5AiRow(bool merc)
        {
            var b = merc ? _mercAi : _homAi;
            bool on = merc ? MercCustomAi : HomCustomAi;
            string file = (merc ? W5MercFile : W5HomFile).Replace('/', '\\');
            string text;
            if (!on) text = Lang.T("AI: basic");
            else if (b != null && b.Script != null) text = Lang.F("AI: custom ({0})", file);
            else if (b != null && b.ScriptProblem != null) text = Lang.F("AI: custom ({0}) — not running: {1}", file, b.ScriptProblem);
            else text = Lang.F("AI: custom ({0})", file);
            return new ListWindow.Row
            {
                Text = $"<size=19>{text}</size>",
                A = on ? Lang.T("Basic AI") : Lang.T("Custom AI"),
                OnA = () => W5ToggleCustomAi(merc),
            };
        }

        private void W5ShadowStart(W4Brain b) { }
        private void W5ShadowBefore() { }
        private void W5AiRecord(string req) { }
        private void W5ShadowAfter(W4Brain b, double[] msg, double[] rmsg) { }
    }
}
