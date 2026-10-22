using Loki.Game;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4b (w4lang): the game's side of the language setting (Lang/Lang.cs). A change of language in the Settings window
    /// (Lang.Set) relabels every fixed text at once (LangText); here the panels whose texts are drawn from state redraw too. Whole
    /// sentences that LokiGame.cs used to build from parts live here as one text per case.
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW4Lang(LokiBootstrap boot)
        {
            Lang.Changed += () =>
            {
                Achievements.ReloadForLanguage(); // the GRF's achievement names of the new language (Game/Achievements.cs)
                Hud?.Wrist.MarkDirty();
                if (Tutorial != null && Tutorial.Visible) Tutorial.Refresh();
                Hud?.Wrist.RefreshW4World();
                if (Hud != null && Hud.Chat != null) Hud.Chat.SetInput(Hud.Chat.InputText); // redraws the console line (its hint is in the language)
                Debug.Log($"[Lang] the game follows the new language {Lang.Code}");
            };
        }

        /// <summary>The line under the laser while a skill waits for its target: one sentence per kind of aim.</summary>
        private static string W4LangArmedText(Progression.SkillEntry k, int lv) =>
            k.Ground ? Lang.F("{0} Lv {1}: look at a spot, pull the trigger.  B cancels", k.Info.Name, lv)
            : k.Support ? Lang.F("{0} Lv {1}: look at a target (or nothing: yourself), pull the trigger.  B cancels", k.Info.Name, lv)
            : Lang.F("{0} Lv {1}: look at a target, pull the trigger.  B cancels", k.Info.Name, lv);

        /// <summary>ZC_NOTIFY_EXP2's chat line ("+1,234 Base EXP (1.20%) (quest)"): base / job × with / without the share of the level
        /// × quest or not, each a whole text.</summary>
        private static string W4LangExpLine(string sign, long amount, bool job, double? pct, bool quest)
        {
            if (job)
                return pct.HasValue
                    ? (quest ? Lang.F("{0}{1:N0} Job EXP ({2:0.00}%) (quest)", sign, amount, pct.Value) : Lang.F("{0}{1:N0} Job EXP ({2:0.00}%)", sign, amount, pct.Value))
                    : (quest ? Lang.F("{0}{1:N0} Job EXP (quest)", sign, amount) : Lang.F("{0}{1:N0} Job EXP", sign, amount));
            return pct.HasValue
                ? (quest ? Lang.F("{0}{1:N0} Base EXP ({2:0.00}%) (quest)", sign, amount, pct.Value) : Lang.F("{0}{1:N0} Base EXP ({2:0.00}%)", sign, amount, pct.Value))
                : (quest ? Lang.F("{0}{1:N0} Base EXP (quest)", sign, amount) : Lang.F("{0}{1:N0} Base EXP", sign, amount));
        }
    }
}
