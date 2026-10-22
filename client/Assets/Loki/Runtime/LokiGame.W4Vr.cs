using System.Linq;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4, w4vr (design notes "the headset, hands and settings"): wiring of the parts that live in VR/ and UI/ —
    /// the OpenXR interaction profiles this build has (logged on start), the button strip over the walking wrist for hands and
    /// controllers without A / B, the low-HP heartbeat / job level rumble and the menu button that opens Settings (VR/VrFeedback.cs),
    /// and the quick bar's rows per character (Game/Hotkeys.cs). One hook in LokiGame.cs (after the rumble lines of the boot code);
    /// the smoke is LokiGame.W4VrSmoke.cs.
    /// </summary>
    public sealed partial class LokiGame
    {
        public ButtonStrip W4VrStrip { get; private set; }
        public VR.VrFeedback W4VrFeedback { get; private set; }

        private void BeginW4Vr(LokiBootstrap boot)
        {
            W4VrLogProfiles();
            var rig = boot.Rig;
            // the quick bar: the character decides which hotkey tab is shown (user settings); the wrist Quick tab reuses the bar's cooldowns
            Session.OnEnteredMap += s => { if (Hotkeys != null) Hotkeys.Character = s.SelectedChar?.Name ?? ""; };
            if (Hud != null) Hud.Wrist.QuickBarRef = Quick;
            // Menu, Map, Back, Next, Centre, View over the walking wrist (hands, Vive, the simple controller; or always / never). Client wave 13
            // (w13view): Menu is the menu button (Y / B since then), Map the see-through minimap's switch (as the overhead menu's)
            W4VrStrip = ButtonStrip.Create(rig.transform, rig.OffHand, rig.Head, W4VrStripWanted,
                (Lang.T("Menu"), () => Hud?.MenuPress()), (Lang.T("Map"), () => W13SetMinimap(!(MiniMap != null && MiniMap.Visible), "the wrist strip")), (Lang.T("Back"), () => Hud?.MainSecondary()),
                (Lang.T("Next"), () => Hud?.MainPrimary()), (Lang.T("Centre"), () => rig.RecentreDiorama()), (Lang.T("View"), () => rig.RequestViewCycle()));
            rig.HandsSwapped += () => W4VrStrip?.SetHand(rig.OffHand);
            W4VrFeedback = gameObject.AddComponent<VR.VrFeedback>();
            W4VrFeedback.Init(Player, () => Session != null && Session.State == Net.RoSession.Stage.InGame, rig, () => ComfortUi?.Toggle());
        }

        /// <summary>The strip shows in the world when Settings says always, or (automatic) when a hand holds a controller without
        /// A / B or is a tracked hand (VR/ControllerProfiles.cs LacksFaceButtons).</summary>
        private bool W4VrStripWanted()
        {
            if (Session == null || Session.State != Net.RoSession.Stage.InGame || Entities.Self == null || VR.Comfort.ButtonStrip == 2) return false;
            if (VR.Comfort.ButtonStrip == 1 || W4VrForceStrip) return true;
            var input = _boot.Rig != null ? _boot.Rig.Input : null;
            return input != null && (VR.ControllerProfiles.LacksFaceButtons(input.Left.Profile) || VR.ControllerProfiles.LacksFaceButtons(input.Right.Profile));
        }
        /// <summary>Smoke only.</summary>
        private bool W4VrForceStrip;

        /// <summary>The build's OpenXR settings (the interaction features and whether each is on), and the Input System layouts the
        /// plugin registered for them (OpenXRLoader.InitializeInternal → OpenXRInput.RegisterLayouts runs before the runtime is asked
        /// for a headset, OpenXRLoader.cs:200-213, so a desktop run has them too).</summary>
        private void W4VrLogProfiles()
        {
            try
            {
                var s = UnityEngine.XR.OpenXR.OpenXRSettings.Instance;
                if (s == null) { Debug.Log("[W4Vr] OpenXR settings: none in this build"); return; }
                var feats = s.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRInteractionFeature>();
                string list = string.Join(", ", feats.Select(f => $"{f.GetType().Name} {(f.enabled ? "on" : "off")}"));
                // layout names are case-insensitive: the Input System's WindowsMR "HololensHand" and the plugin's "HoloLensHand" are one name
                var layouts = new System.Collections.Generic.HashSet<string>(UnityEngine.InputSystem.InputSystem.ListLayouts(), System.StringComparer.OrdinalIgnoreCase);
                string reg = string.Join(", ", VR.ControllerProfiles.All.Select(i => $"{i.Layout} {(layouts.Contains(i.Layout) ? "yes" : "no")}"));
                Debug.Log($"[W4Vr] OpenXR interaction features in this build ({feats.Length}): {list}");
                Debug.Log($"[W4Vr] Input System layouts registered: {reg}");
            }
            catch (System.Exception e) { Debug.LogWarning("[W4Vr] OpenXR settings: " + e.Message); }
        }
    }
}
