using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 5, w5fxown (design notes "w5fxown"; progress design notes): Loki's OWN skill effects — a design,
    /// not the official client's look — for the skills that draw nothing from any source (Fx/OwnSkillFx.cs, Fx/OwnFxRenderer.cs,
    /// Resources/Fx/loki_own_fx.txt). Hooks elsewhere: Fx/EffectManager.cs (the "OWN" part type, Own, the cast phase), Game/CombatFx.cs
    /// (0x01de / 0x09cb / 0x0117 call Own), LokiGame.W4Fx.cs (the table loads after the GRF's own tables), UI/ComfortWindows.cs (the
    /// Graphics page's switch), World/UserSettings.cs (ownSkillEffects). Smoke: LokiGame.W5FxOwnSmoke.cs (-lokiSmokeOnly w5fxown-host / -guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW5FxOwn(LokiBootstrap boot)
        {
            Loki.Effects.OwnSkillFx.Enabled = Loki.World.UserSettings.Prefs.ownSkillEffects;
            Debug.Log($"[OwnFx] Loki's own skill effects switch: {(Loki.Effects.OwnSkillFx.Enabled ? "on" : "off")} (Settings → Graphics; loki-user.json ownSkillEffects)");
        }
    }
}
