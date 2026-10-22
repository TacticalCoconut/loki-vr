using System.Collections.Generic;
using Loki.Game;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 5, w5ai: the custom homunculus / mercenary AI (design notes, scope "w5ai"). The pieces:
    ///  • Lua/*.cs — a Lua 5.1 (lexer, parser, compiler to the Lua 5.1 instruction set, VM, the standard library, a sandboxed io), written
    ///    from the Lua 5.1 manual;
    ///  • Game/AiCore/*.cs — the AI API the official AI files call (GetV …), the default AI (w4units' port of AI.lua / AI_M.lua, completed),
    ///    the script host;
    ///  • Game/Companions.W4Ai.cs / W5Ai.cs — the companions' AI in the game: the default AI, or the player's AI\USER_AI\AI.lua / AI_M.lua
    ///    when /hoai / /merai is on, the default AI again on any error.
    /// This part wires them: the RO folder, chat notices, the owner's HP / SP, and the slash commands /hoai, /merai (the guide's update
    /// history; roBrowserLegacy Controls/ProcessCommand.js:512-541) and /traceai (the guide 5-5). The help lines are Loki's (the
    /// msgstringtable has none for these commands).
    /// Smoke: LokiGame.W5AiSmoke.cs (-lokiSmokeOnly w5ai).
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW5Ai(LokiBootstrap boot)
        {
            if (Companions == null) return;
            Companions.W5AiRoot = () => _boot.Config.clientRoot;
            Companions.W5AiNotice = (text, color) => Hud?.Chat.Add(text, color);
            Companions.W5OwnerVitals = () => (Player.Hp, Player.MaxHp, Player.Sp, Player.MaxSp);
            Companions.W5AiOtherActors = W5AiOtherActors;
            var S = W4SocialSlash;
            S.Add(-1, Lang.N("/hoai: switches your homunculus between the basic AI and your own AI\\USER_AI\\AI.lua"), a => { Companions.W5ToggleCustomAi(false); return true; }, "hoai");
            S.Add(-1, Lang.N("/merai: switches your mercenary between the basic AI and your own AI\\USER_AI\\AI_M.lua"), a => { Companions.W5ToggleCustomAi(true); return true; }, "merai");
            S.Add(-1, Lang.N("/traceai: writes the AI's TraceAI lines to TraceAI.txt (again: stops)"), a => { Companions.W5ToggleTrace(); return true; }, "traceai");
            Debug.Log($"[W5Ai] AI: homunculus {(Companions.HomCustomAi ? "custom" : "basic")}, mercenary {(Companions.MercCustomAi ? "custom" : "basic")} (RO folder '{_boot.Config.clientRoot}')");
        }

        /// <summary>GetActors' items and skills: the ground items (Game/GroundItems.cs) and the skill units drawn (CombatFx.Ground, the
        /// visible ones: a hidden trap is not in the player's view).</summary>
        private IEnumerable<(uint id, int x, int y)> W5AiOtherActors()
        {
            if (Ground != null) foreach (var g in Ground.Items.Values) if (g != null) yield return (g.Id, g.Cell.x, g.Cell.y);
            if (Combat != null) foreach (var u in Combat.Ground.Values) if (u != null && u.Visible) yield return (u.Uid, u.X, u.Y);
        }
    }
}
