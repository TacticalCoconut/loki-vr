using Loki.Net;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 3, w3login: the VR keyboard's caret on the chat console and the login fields (the physical arrows, Home,
    /// End and Delete too), and what happens when loki.json's auto-login fails (the login screen with the reason; the
    /// offline demo only runs under -lokiSmoke). The smokes are in LokiGame.W3LoginSmoke.cs.
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW3Login(LokiBootstrap boot)
        {
            if (!RoText.SelfTest(out string why)) Debug.LogError("[Text] cp1252 codec self-test FAILED: " + why);
            if (Hud == null || Keyboard == null) return;
            // the chat console: characters, backspace, enter and ◄ ► (ChatPanel routes them on to a focused login field first)
            Hud.Chat.KeyboardRequested = () => { Keyboard.Target(c => Hud.Chat.TypeChar(c), () => Hud.Chat.Backspace(), () => Hud.Chat.Enter(), d => Hud.Chat.MoveCaret(d)); Keyboard.Show(); };
            Hud.Chat.KeyboardRequested += () => _npcKeyboard = false; // as client wave 1 did: the chat console took the keyboard back
            if (Login != null) { Hud.Chat.LoginCaret = d => Login.KeyCaret(d); Hud.Chat.LoginDelete = () => Login.KeyDelete(); }
            Hud.Chat.NpcCaret = d => Dialog.MoveCaret(d);
        }
    }
}
