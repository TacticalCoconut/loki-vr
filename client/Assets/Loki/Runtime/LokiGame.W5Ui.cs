using Loki.Game;
using Loki.Net;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 5 (w5ui): wiring of the windows-and-data work — the skill window's character sex (the Portuguese job names' female
    /// forms) and Frost Joke / Scream's joke line (Game/JokeLines.cs) on our own ZC_USESKILL_ACK 0x0b1a. The rest lives in its own files:
    /// Game/SkillPlan.cs + UI/WristMenu.Skills.cs (planned points), UI/CharRowPreview.cs + UI/LoginPanel.W5Ui.cs (the character list's
    /// pictures), UI/RouletteWheel.cs (the roulette wheel), UI/SpritePortrait.cs + Game/Cart.cs (cart pictures), the first-run builder's
    /// Portuguese columns (Data/GrfTools.cs, Game/Progression.cs SkillDb, Game/SkillTree.cs). Hook: LokiGame.cs Begin (one line).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The last joke line this client said (for the log and the smoke); "" = none yet.</summary>
        public string W5LastJoke { get; private set; } = "";
        public int W5JokesSaid { get; private set; }
        public int W5JokesSkipped { get; private set; }

        private void BeginW5Ui(LokiBootstrap boot)
        {
            if (Hud != null && Hud.Wrist != null) Hud.Wrist.SkillSex = () => Session?.SelectedChar != null ? Session.SelectedChar.Sex : 1;
            Session.OnMapPacket += W5UiListen;
        }

        private void W5UiListen(RoSession s, ushort id, byte[] b, int o, int n)
        {
            // ZC_USESKILL_ACK 0x0b1a srcId.L dstId.L x.W y.W skillId.W element.L delayTime.L disposable.B attackMT.L (packets_struct.hpp:3952-3964)
            if (id != 0x0b1a || n < 16 || s.SelectedChar == null) return;
            var r = new PacketReader(b, o, n); r.Skip(2);
            uint src = r.U32(); r.U32(); r.U16(); r.U16(); int skill = r.U16();
            if (src != s.AccountId || !JokeLines.IsJokeSkill(skill)) return;
            if (!JokeLines.On) { W5JokesSkipped++; Debug.Log($"[Jokes] skill {skill}: the joke line is off (Settings → Language)"); return; }
            string line = JokeLines.Pick(skill);
            if (string.IsNullOrEmpty(line)) { Debug.Log($"[Jokes] skill {skill}: no joke list in the game data"); return; }
            W5LastJoke = line; W5JokesSaid++;
            Debug.Log($"[Jokes] skill {skill}: says '{line}'");
            s.Chat(s.SelectedChar.Name, line); // CZ_REQUEST_CHAT "name : text", the chat console's own path (Net/RoSession.cs Chat)
        }
    }
}
