using Loki.Game;
using Loki.Net;
using Loki.Sprites;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>Client wave 2, modes: memorial dungeons, battlegrounds and duels (Modes.cs) — the wrist menu's Modes page, the lobby
    /// prompt, team colours and who can be hit on a battlefield, and the duel commands.</summary>
    public sealed partial class LokiGame
    {
        public Modes Modes { get; private set; }

        private void BeginWave2Modes(LokiBootstrap boot)
        {
            Modes = new Modes(Session, Entities, () => Session.AccountId, (t, c) => Chat(t, c))
            {
                Toast = m => Hud.Wrist.Toast(m), Say = line => Session.Chat(Session.SelectedChar.Name, line), SelfName = () => Session.SelectedChar?.Name ?? "",
            };
            Modes.Changed += () => Hud.Wrist.MarkDirty();
            // client wave 4 (w4social): the duel state comes from 0x099b and the rest through Net/IDuelChannel (LokiGame.W4Social.cs wires
            // both); the old peek at the server's texts is gone (the re-audit's §3 leak)
            Hud.Wrist.SetHub(WristMenu.Tab.Modes, () => Lang.T("Dungeons, battlegrounds, duels"), () => Modes.Summary(),
                new WristMenu.HubButton(Lang.T("Close the dungeon"), () => Modes.DestroyDungeon(), () => Modes.HasShownDungeon && Party != null && Party.IsLeader),
                new WristMenu.HubButton(Lang.T("Accept the battle"), () => Modes.AnswerLobby(true), () => Modes.LobbyPending),
                new WristMenu.HubButton(Lang.T("Decline"), () => Modes.AnswerLobby(false), () => Modes.LobbyPending),
                new WristMenu.HubButton(Lang.T("Battlegrounds…"), () => BgWindow(), () => Modes.Queued.Length == 0),
                new WristMenu.HubButton(Lang.T("Leave the queue"), () => Modes.CancelQueue(), () => Modes.Queued.Length > 0 && !Modes.LobbyPending),
                new WristMenu.HubButton(Lang.T("Team chat…"), () => Hud.Wrist.PrefillChat("/bt "), () => Modes.OnBattlefield),
                new WristMenu.HubButton(() => { var g = Hud.Wrist.GazedPlayerName?.Invoke(); return g != null ? Lang.F("Duel {0}", g) : Lang.T("Duel the player I'm looking at"); }, () => DuelGazed(), () => Modes.DuelInviter.Length == 0), // w4lang: two whole labels
                new WristMenu.HubButton(Lang.T("Accept the duel"), () => Modes.DuelAccept(), () => Modes.DuelInviter.Length > 0),
                new WristMenu.HubButton(Lang.T("Refuse the duel"), () => Modes.DuelReject(), () => Modes.DuelInviter.Length > 0),
                new WristMenu.HubButton(Lang.T("Leave the duel"), () => Modes.DuelLeave(), () => Modes.InDuel));
        }

        /// <summary>A duel with the gaze-locked player: a new two-player duel with them, or an invitation into ours (Net/IDuelChannel.cs).</summary>
        public void DuelGazed()
        {
            var n = Hud.Wrist.GazedPlayerName?.Invoke();
            if (n == null) { Hud.Wrist.Toast(Lang.T("Look at a player first.")); return; }
            Modes.DuelChallenge(n);
        }

        private ListWindow _bgWin;
        /// <summary>The battleground list (names from data/account/battlegrounds.json): solo or party application.</summary>
        public void BgWindow()
        {
            if (_bgWin == null) { _bgWin = ListWindow.Create(_boot.Rig.transform, "BattlegroundWindow"); _bgWin.OnCloseClicked = () => _bgWin.Hide(); }
            var rows = new System.Collections.Generic.List<ListWindow.Row>();
            foreach (var bg in Modes.Battlegrounds)
            {
                var b2 = bg;
                string lv = bg.MaxLevel > 0 ? Lang.F("level {0}–{1}", bg.MinLevel, bg.MaxLevel) : Lang.F("level {0}+", bg.MinLevel);
                rows.Add(new ListWindow.Row { Text = $"{bg.Name}\n<size=19><color=#9aa3b5>{lv} · " + Lang.F("{0} per side", bg.MinPlayers) + "</color></size>", A = "Solo", OnA = () => { Modes.Apply(b2.Name, 1); _bgWin.Hide(); }, B = Lang.T("Party"), OnB = () => { Modes.Apply(b2.Name, 2); _bgWin.Hide(); }, BEnabled = Party != null && Party.InParty && Party.IsLeader });
            }
            _bgWin.SetTitle(Lang.T("Battlegrounds")); _bgWin.SetTabs(null, 0, null); _bgWin.SetInput(false);
            _bgWin.SetInfo(rows.Count == 0 ? Lang.T("No battleground list (data/account/battlegrounds.json).") : Lang.T("Apply alone or with your party; you'll be asked to accept when both sides are full."));
            _bgWin.SetFooter((Lang.T("Close"), () => _bgWin.Hide()));
            _bgWin.SetRows(rows, true);
            _bgWin.Open(_boot.Rig.Head);
        }
        public ListWindow BgListWindow => _bgWin;

        private bool HandleWave2Modes(ushort id, byte[] b, int o, int n) => Modes != null && Modes.Handle(id, b, o, n);

        private void OnMapChangeWave2Modes(string map) => Modes?.OnMapChange();

        /// <summary>"/bt text" on the console: battleground team chat.</summary>
        private bool TryWave2Command(string line)
        {
            if (line != null && line.StartsWith("/bt ", System.StringComparison.OrdinalIgnoreCase) && Modes != null) { if (Modes.OnBattlefield) Modes.TeamSay(line.Substring(4)); else Chat(Lang.T("You aren't on a battlefield."), "ffb070"); return true; }
            return false;
        }
    }
}
