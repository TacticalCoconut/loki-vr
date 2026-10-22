using Loki.UI;
using Loki.VR;
using UnityEngine;
using UnityEngine.UI;

namespace Loki
{
    /// <summary>
    /// Client wave 24 (w24), items 9 and 17: the chat box's own buttons.
    ///   design note (2026-10-12): "The close and open button directly on the chat box" and "The chat box should not be
    ///   open by default. On the start of a new character, when they log in after the tutorial."
    ///   Close   a close button on the open chat box's top right corner (UI/ChatPanel.cs CloseButton): the chat hides exactly as the
    ///           elbow's chat button hides it (UI/LokiHud.cs ToggleChat: one switch, one stored choice).
    ///   Tab     while the chat is hidden, a small see-through button with the chat's speech bubble stands where that corner is
    ///           (it follows the chat box's own place, wherever that was put); pressed (laser, look), the chat shows where it
    ///           stands. Not shown before the world, in Horde's arena, or during the tutorial (wave 19, point 46: no chat there).
    ///           <see cref="ChatTabSize"/> is its size, <see cref="ChatTabOn"/> turns it off (FLAGGED: default what
    ///           shows while the chat is closed).
    ///   Kept    the shown / hidden choice is the account's now (UserSettings chatShown) and starts HIDDEN for a player who never
    ///           chose (item 17). The elbow's chat button, the close button and the tab all read and write that one choice. The
    ///           walking hand's stick tap and B still never show or hide the chat (wave 19, point 41: nothing here touches them).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The open tab's size, metres (the elbow's chat button is the same 4.2 cm).</summary>
        public const float ChatTabSize = 0.042f;
        /// <summary>The open tab is shown while the chat is hidden (false: only the elbow's chat button shows the chat).</summary>
        public const bool ChatTabOn = false;
        private const float ChatTabPx = 140f;
        private RectTransform _w24Tab; private UiButton _w24TabBtn;
        /// <summary>The open tab: its panel, its button, shown now; presses of the tab and of the chat's close button (smoke, log).</summary>
        public Transform W24ChatTab => _w24Tab;
        public UiButton W24ChatTabButton => _w24TabBtn;
        public bool W24ChatTabShown => _w24Tab != null && _w24Tab.gameObject.activeSelf;
        public int W24TabPresses { get; private set; }
        public int W24ClosePresses { get; private set; }

        private void BeginW24Hud(LokiBootstrap boot)
        {
            if (Hud == null || Hud.Chat == null) return;
            Hud.Chat.CloseRequested = () =>
            {
                if (!Hud.Chat.Visible) return;
                W24ClosePresses++; Hud.ToggleChat(false);
                Debug.Log($"[Chat] the chat box's close button: the chat {(Hud.Chat.Visible ? "shown" : "hidden")}");
            };
            _w24Tab = UiKit.Panel("ChatTab", boot.Rig.transform, new Vector2(ChatTabPx, ChatTabPx), ChatTabSize / ChatTabPx, false, PanelKind.Hud); // (a HUD piece like the elbow's chat button: never a menu, so the casting and the bubbles go on while it shows)
            _w24TabBtn = UiKit.Button(_w24Tab, "", Vector2.zero, new Vector2(ChatTabPx, ChatTabPx), () =>
            {
                if (Hud.Chat.Visible) return;
                W24TabPresses++; Hud.ToggleChat(false);
                Debug.Log($"[Chat] the chat's open tab: the chat {(Hud.Chat.Visible ? "shown" : "hidden")}");
            }, 76);
            _w24TabBtn.gameObject.name = "btn:chat open"; _w24TabBtn.Tip = () => Lang.T("Open chat");
            UiKit.Box(_w24Tab, "ring", Vector2.zero, new Vector2(ChatTabPx + 10, ChatTabPx + 10), HudLook.ButtonEdge(0.85f)).SetAsFirstSibling(); // (as the elbow's chat button: LokiGame.W13View.cs BuildW19ChatButton)
            // the speech bubble of the elbow's chat button: a body, its tail at the lower left, three dots
            var b = (RectTransform)_w24TabBtn.transform;
            UiKit.Box(b, "bubble", new Vector2(0f, 10f), new Vector2(84f, 54f), UiKit.Dim);
            UiKit.Box(b, "tail", new Vector2(-22f, -20f), new Vector2(20f, 20f), UiKit.Dim).localRotation = Quaternion.Euler(0f, 0f, 45f);
            for (int i = -1; i <= 1; i++) UiKit.Box(b, "dot", new Vector2(22f * i, 10f), new Vector2(11f, 11f), OverheadMenu.Unlit);
            _w24Tab.gameObject.SetActive(false);
        }

        /// <summary>The open tab may show now: the chat hidden, in the world, not in Horde's arena, not while the chat is locked (the tutorial).</summary>
        private bool W24ChatTabWanted => ChatTabOn && Hud != null && Hud.Chat != null && !Hud.Chat.Visible && W13InGame && !W8HordeBlocksWorld && !(Hud.ChatLocked != null && Hud.ChatLocked());

        /// <summary>Once a frame (LokiGame.W13Grab.cs UpdateW13Grab): the tab shown or not, at the hidden chat box's close button's spot.</summary>
        private void UpdateW24Hud()
        {
            if (_w24Tab == null) return;
            bool want = W24ChatTabWanted;
            if (_w24Tab.gameObject.activeSelf != want) _w24Tab.gameObject.SetActive(want);
            if (!want) return;
            var chat = Hud.Chat.transform;
            _w24Tab.position = chat.TransformPoint(new Vector3(ChatPanel.ClosePx.x, ChatPanel.ClosePx.y, 0f)); _w24Tab.rotation = chat.rotation;
        }
    }
}
