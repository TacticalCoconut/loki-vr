using System.Collections.Generic;
using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 1 (2026-09-27): the player menu (gaze / laser on a player), NPC progress bars, the NPC text prompt's VR keyboard,
    /// skill menus that answer the server (Auto Spell, Auto Shadow Spell, Repair Weapon), Warp Portal memo, stop-attack, ammunition
    /// messages, and the return to the login screen when the connection dies in game. The smokes of each item are at the bottom
    /// (-lokiSmokeOnly w1host / w1guest / w1chars / w1online; the missing-data one lives in LokiBootstrap).
    /// </summary>
    public sealed partial class LokiGame
    {
        public PlayerMenu PlayerMenu { get; private set; }
        public ProgressBars Progress { get; private set; }
        public SkillMenus SkillMenus { get; private set; }
        /// <summary>This scene reached the map once: a failure after that is a lost connection, not a failed login.</summary>
        private bool _wasInGame, _goingToLogin, _npcKeyboard;
        /// <summary>Why the previous scene went back to the login screen (shown there once).</summary>
        private static string _bootNotice;
        /// <summary>ZC_ACTION_FAILURE: the last type and its text (smoke test).</summary>
        public (int type, string text) LastActionFailure = (-1, "");

        private void BeginWave1(LokiBootstrap boot)
        {
            System.Func<Camera> head = () => boot.Rig.Head;
            PlayerMenu = new PlayerMenu(Session, ListWindow.Create(boot.Rig.transform, "PlayerMenu"), head) { Trade = Trade, Party = Party, Social = Social, Guild = Guild };
            PlayerMenu.PrefillChat = t => Hud.Wrist.PrefillChat(t);
            PlayerMenu.WalkTo = u =>
            {
                if (u == null || Entities.Self == null || Session.State != RoSession.Stage.InGame) return;
                var w = NearestWalkable(Vector2Int.RoundToInt(u.Cell), Vector2Int.RoundToInt(Entities.Self.Cell));
                _pendingPickup = _pendingNpc = 0; _attackTarget = 0; W3WalkStart(w, "player"); // far: in legs (w3walk)
            };
            PlayerMenu.Toast = m => { Hud.Wrist.Toast(m); Chat(m, "c8cbd6"); };
            Progress = new ProgressBars(Session, () => Entities.Self, id => Entities.Get(id));
            Progress.Message += m => Hud.Wrist.Toast(m);
            SkillMenus = new SkillMenus(Session, ListWindow.Create(boot.Rig.transform, "SkillMenuWindow"), head, (t, c) => Chat(t, c));
            Target.IsOwnCompanion = id => Companions != null && Companions.Owns(id);
            Hud.StopAttacking = StopAttackNow;
            Hud.Wrist.StopAttack = () => { if (!StopAttackNow()) Hud.Wrist.Toast(Lang.T("You aren't attacking anything.")); };
            Hud.Wrist.Attacking = () => _attackTarget != 0;
            Hud.Wrist.Memo = () => { Session.RememberWarpPoint(); Debug.Log("[Skill] memo request (CZ_REMEMBER_WARPPOINT)"); };
            // NPC text / number prompts type through the VR keyboard (the dialog's ⌨ button; a text prompt opens it by itself)
            Hud.Dialog.RequestKeyboard = () =>
            {
                Keyboard.OnChar = c => Dialog.TypeChar(c); Keyboard.OnBackspace = () => Dialog.Backspace(); Keyboard.OnEnter = () => Dialog.SubmitInput(); Keyboard.OnCaret = d => Dialog.MoveCaret(d); // ◄ ► (w3login)
                Keyboard.OnClose = null; _npcKeyboard = true; Keyboard.Show();
                Debug.Log("[Dialog] VR keyboard on the NPC prompt");
            };
            Dialog.Changed += () => { if (_npcKeyboard && !Dialog.WantsKeyboard) { _npcKeyboard = false; Keyboard.Hide(); } };
            Hud.Chat.KeyboardRequested += () => _npcKeyboard = false; // the chat console took the keyboard back
            // Loki Online: the SpacetimeDB connection closed under us (a server close, a network failure)
            Stdb.ConnectionLost += why => { if (Session.OnlineLink && _wasInGame) ConnectionLost(why); };
            ClientUpdater.InGameContext = false;
            ClientUpdater.UpdateStarting -= OnClientUpdateStarting;
            ClientUpdater.UpdateStarting += OnClientUpdateStarting;
            if (_bootNotice != null && Login != null) { Login.Status(_bootNotice); Debug.Log("[Game] back at the login screen: " + _bootNotice); _bootNotice = null; }
        }

        private void OnClientUpdateStarting(uint version, string msg)
        {
            if (this == null) return;
            ClientUpdater.InGameContext = _wasInGame;
            _goingToLogin = true;
            LoginPanel.PendingReconnect = null;
            try { Stdb?.Disconnect(); } catch { }
            try { Session?.Dispose(); } catch { }
            if (_wasInGame)
            {
                Chat(msg, "ffd86b");
                ShowNotice(Lang.T("Update Available"), msg);
            }
            else if (Login != null)
            {
                Login.Show(LoginPanel.Screen.Login);
                Login.Status(msg);
            }
        }

        /// <summary>Right B / the wrist button: CZ_CANCEL_LOCKON 0x0118 ends a continuous attack (clif_parse_StopAttack). True when one ran.</summary>
        public bool StopAttackNow()
        {
            if (_attackTarget == 0 || Session == null || Session.State != RoSession.Stage.InGame) return false;
            Session.StopAttack(); _attackTarget = 0;
            Debug.Log("[Game] stop attacking (CZ_CANCEL_LOCKON)");
            Hud?.Wrist.Toast(Lang.T("Stopped attacking."));
            return true;
        }

        private bool HandleWave1(ushort id, byte[] b, int o, int n)
        {
            if (Progress != null && Progress.Handle(id, b, o, n)) { if (id == 0x02f0 || id == 0x02f2) Dialog?.DismissLocal(); return true; }
            if (SkillMenus != null && SkillMenus.Handle(id, b, o, n)) return true;
            if (PlayerMenu != null && PlayerMenu.Handle(id, b, o, n)) return true;
            if (id == 0x013b)
            {
                // ZC_ACTION_FAILURE type.W (clif_arrow_fail): 0 no ammunition, 1 / 2 overweight, 3 ammunition equipped — for 3 the client
                // picks the line by job: assassins "throwing daggers", gunslingers "bullets", ninjas nothing, others "arrow" (clif.cpp:4219-4228)
                var r = new PacketReader(b, o, n); r.Skip(2); int type = r.U16();
                int job = Entities.Self != null ? Entities.Self.Job : Session.SelectedChar?.Job ?? 0;
                int msg = type == 0 ? 242 : type == 1 ? 243 : type == 2 ? 244 : job == 12 || job == 4013 || job == 4035 ? 1040 : job == 24 ? 1175 : job == 25 ? -1 : 245;
                string text = msg >= 0 ? ClientText.Msg(msg) : "";
                LastActionFailure = (type, text);
                Debug.Log($"[Game] action failure {type}: {text}");
                if (text.Length > 0) { Chat(text, type == 3 ? "a8e6a3" : "ff7a6a"); if (type != 3) Hud?.Notify(text); } // client wave 11: in front of the player
                return true;
            }
            return false;
        }

        /// <summary>The connection died in game (map server closed, SpacetimeDB closed or failed): say why, then a fresh scene on the login screen.</summary>
        private void ConnectionLost(string why)
        {
            if (ClientUpdater.IsUpdating) return;
            if (W4AccountConnectionLost(why)) return; // client wave 4: a lost map server logs in again (LokiGame.W4Account.cs)
            if (_goingToLogin) return;
            _goingToLogin = true;
            Debug.LogWarning("[Game] connection lost in game: " + why);
            Chat(why, "ff7a6a");
            ShowNotice(Lang.T("Disconnected"), why + "\n" + Lang.T("Back to the login screen in a moment…")); // w4lang: the reason (server / system text), then one line of ours
            _bootNotice = why; _showLoginOnBoot = true;
            StartCoroutine(ReloadAfter(4f));
        }
        private System.Collections.IEnumerator ReloadAfter(float seconds) { yield return new WaitForSeconds(seconds); LokiBootstrap.Reload(); }

        private RectTransform _notice;
        /// <summary>A plain message panel in front of the head (no buttons) — the connection-lost notice.</summary>
        private void ShowNotice(string title, string text)
        {
            if (_boot.Rig == null) return;
            if (_notice == null)
            {
                _notice = UiKit.Panel("Notice", _boot.Rig.transform, new Vector2(820, 300), 0.0007f, true, PanelKind.ByName, PanelLook.Card); // client wave 21 (w21kit): a notice is a parchment card of the theme
                UiKit.Label(_notice, title, new Vector2(0, 100), new Vector2(760, 60), 36, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.5f));
                UiKit.Label(_notice, "", new Vector2(0, -30), new Vector2(760, 180), 26, TextAnchor.MiddleCenter).name = "body";
            }
            foreach (var t in _notice.GetComponentsInChildren<UnityEngine.UI.Text>(true)) if (t.name == "body") t.text = text;
            var hd = _boot.Rig.Head; var f = hd.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            _notice.position = hd.transform.position + f * 0.6f; UiKit.FaceHead(_notice, hd);
            _notice.gameObject.SetActive(true);
        }

    }
}
