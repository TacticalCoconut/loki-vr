using Loki.Game;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 11 (w11menu): what the design's 2026-10-06 playtest asked for, wired into the game.
    ///   The gaze dot: shown while Settings → Comfort is open (the gaze calibration's sliders are on that page) or always
    ///   (Comfort.GazeDot); VR/DioramaRig.Ui.cs draws it where the one calibrated gaze ray lands.
    ///   The console commands list (Game/GmCommandList.cs): "Commands" on the strip under the wrist menu. "To chat" fills the chat
    ///   line and opens the VR keyboard (the wrist menu's PrefillChat), "Send" goes the chat line's own way (ChatPanel.Submit: the
    ///   same path a typed line takes, local commands included).
    /// Hook in LokiGame.cs: InitW11Menu, once, where the gaze choosing is set up.
    /// </summary>
    public sealed partial class LokiGame
    {
        public GmCommandList Commands { get; private set; }
        /// <summary>The item browser and the job / place / monster pickers of the console commands (Game/GmPickers.cs).</summary>
        public GmPickers Pickers { get; private set; }

        private void InitW11Menu(LokiBootstrap boot)
        {
            if (boot.Rig == null || Hud == null) return;
            boot.Rig.ShowGazeDot = () => VR.Comfort.GazeDot || (ComfortUi != null && ComfortUi.Visible && ComfortUi.Current == ComfortWindow.Page.Comfort && ComfortUi.Section == 1); // (client wave 14, w14menu: the Look select section, where the look rows are)
            Commands = new GmCommandList(ListWindow.Create(boot.Rig.transform, "CommandsWindow"), () => boot.Rig.Head);
            Commands.ToChat = t => Hud.Wrist.PrefillChat?.Invoke(t);
            Commands.Send = line => Hud.Chat.Submit?.Invoke(line);
            Hud.Wrist.OpenCommands = () => Commands.Show();
            Pickers = new GmPickers(ListWindow.Create(boot.Rig.transform, "PickerWindow"), () => boot.Rig.Head);
            Pickers.Send = line => Hud.Chat.Submit?.Invoke(line);
            // client wave 14 (w14play), point 30: the Jobs tab asks the character's sex and the GRF's bodies (Game/GmPickers.cs JobRows)
            Pickers.Sex = () => Session != null && Session.SelectedChar != null ? Session.SelectedChar.Sex : 1;
            Pickers.HasBody = (job, sex) => Sprites.RoNames.PcName(job) != null && boot.Assets != null && boot.Assets.SpriteExists(Sprites.RoNames.BodyPath(job, sex));
            Commands.OpenPicker = m => { Commands.Hide(); Pickers.Show((GmPickers.Mode)m); };
        }
    }
}
