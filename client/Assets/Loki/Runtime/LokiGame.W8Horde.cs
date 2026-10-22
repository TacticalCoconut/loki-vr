using Loki.Horde;
using Loki.Horde.Client;
using Loki.Net;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 8 (w8game): the Horde side mode's place in the game. Once the character is in the world, a
    /// <see cref="HordeGame"/> with its channel (<see cref="IHordeChannel"/>: Loki Host's relay, or on Loki Online the module itself:
    /// Net/Stdb/StdbHordeChannel.cs; the scripted stand-in only for the smoke, -lokiHorde fake), the wrist menu's Horde button, and the "veil" over the RO world while the arena is shown:
    ///   - the world keeps running untouched (packets, units, effects; chat and voice), drawn 5 km under the table (HordeArena);
    ///   - no input reaches the real character: the gaze lock and the stick's walking are off (Targeting disabled), the trigger
    ///     on the arena's ground or on nothing does nothing in the world (guards in OnFreeTrigger / OnTargetClicked), no skill
    ///     wheels, no turning in place, A does not sit / stand (it takes the highlighted card); a far walk or an attack the
    ///     character was doing is stopped when the run begins (it must not wander or fight unseen);
    ///   - world sounds that have a place are not heard (the listener is put far away), the arena's map plays its music,
    ///     the sky is the arena's, the first-person view becomes Follow for the run;
    ///   - leaving puts every one of these back (the world's map light, sky, music, room mask, night tint, view).
    /// Client wave 9 (h9flow): "Play alone" runs on the REAL engine (the host's relay; alone, the host begins at once); the
    /// stand-in is not offered any more. Settings → Horde (invitations, table follow, HUD size) are read by the HUD itself.
    /// Hooks in LokiGame.cs: BeginW8Horde, OnMapChangeW8Horde (end of EnterMap), UpdateW8Horde (Update), the smoke dispatch, and
    /// the guards named above (LokiGame.cs, LokiGame.Wheels.cs, LokiGame.Wave2Combat.cs, UI/LokiHud.cs).
    /// </summary>
    public sealed partial class LokiGame
    {
        public HordeGame Horde { get; private set; }
        /// <summary>The arena is shown: the RO world must not be acted on.</summary>
        public bool W8HordeBlocksWorld => Horde != null && Horde.InArena;
        private System.Func<Vector2?> _w8Listener; private VR.ViewController.ViewMode? _w8View;
        private IHordeChannel _w8Relay; private HordeFakeChannel _w8Fake;
        private Net.Stdb.StdbHordeChannel _w8Stdb; // port wave I (ihorde): the Loki Online module as Horde's host (the session is online)

        private void BeginW8Horde(LokiBootstrap boot)
        {
            if (Hud == null) return;
            Hud.Wrist.OpenHorde = () => { Hud.Wrist.Hide(); W8HordeOpenPanel(); };
            Hud.PrimaryFirst = () => W8HordeBlocksWorld && (Horde.Choice.PressPrimary() || true); // in the arena A never sits / stands the real character
            // client wave 15 (w15hfx), point 19: in a run a short press of the menu button opens the run's menu (a hold: the usual menu, UI/LokiHud.cs)
            Hud.RunMenuWanted = () => W8HordeBlocksWorld;
            Hud.RunMenuPress = W8HordeRunMenu;
        }

        /// <summary>Client wave 15 (w15hfx), point 19: the menu button's short press in a run: with any of the player's menus open (the run
        /// menu included) it closes them all (wave 14's rule, point 29); else the run's menu opens.</summary>
        private void W8HordeRunMenu()
        {
            if (Horde == null || !Horde.InArena) { Hud?.MenuButton(); return; }
            if (W14CloseEverything("the menu button in a Horde run")) return;
            Horde.Windows.ShowRunMenu();
            Debug.Log("[Horde] the run's menu opened (a short press of the menu button)");
        }

        public void W8HordeOpenPanel()
        {
            if (Horde == null) { Chat(Lang.T("Horde: enter the world first."), "ffb070"); return; }
            Horde.Windows.ShowPanel();
        }

        /// <summary>Hook: the end of EnterMap (every map). The first one makes the mode; later ones keep the veil on during a run.</summary>
        private void OnMapChangeW8Horde(string map)
        {
            if (Session == null || _boot.Rig == null) return;
            if (Horde == null) StartW8Horde();
            else if (Horde.InArena) W8HordeVeil(true); // the world changed map under the arena: its music / sky were just set for it
            _w8Stdb?.NotifyEnteredMap();
        }

        private void StartW8Horde()
        {
            var content = Loki.Horde.Content.HordeTables.Build();
            string name = Session.SelectedChar != null ? Session.SelectedChar.Name : "";
            int sex = Session.SelectedChar != null ? Session.SelectedChar.Sex : 1;
            // Loki Host's relay (the connection the headsets and voice use) when there is one; the scripted stand-in is the smoke's
            // (-lokiHorde fake) and the fallback of a server without any relay (the panel then says the host has no Horde)
            var fake = new HordeFakeChannel(content, Session.AccountId, name, () => Time.realtimeSinceStartupAsDouble);
            _w8Fake = fake;
            // port wave I (ihorde): on Loki Online the module itself runs Horde: the same HordeClient over the session's connection
            var relay = _presence as PresenceRelayChannel;
            if (relay == null && Session.OnlineLink && Stdb != null) _w8Stdb = Net.Stdb.StdbHordeChannel.Open(Stdb, content, sex);
            IHordeChannel ch = relay != null ? (IHordeChannel)HordeRelayChannel.Open(relay, content, sex) : _w8Stdb != null ? _w8Stdb.Client : fake;
            _w8Relay = relay != null || _w8Stdb != null ? ch : null;
            Horde = HordeGame.Create(gameObject, _boot, ch, content);
            Horde.MyAid = Session.AccountId;
            Horde.Windows.Me = Session.AccountId;
            Horde.Choice.Sex = sex == 0 ? 0 : 1; // the job cards' portraits (Bard / Dancer follow the character's sex, as the engine does)
            Horde.Veil = W8HordeVeil;
            Horde.Windows.MainMenu = () => Hud?.MenuButton(); // (w15hfx, point 19: the run menu's "Main menu")
            Horde.Windows.OtherMenusOpen = () => W14PlayerMenusOpen().Exists(n => n != "HordeRunMenu"); // client wave 18 (h18rules), point 23: the menu's pause alone holds while "Main menu" is open
            Horde.Say = (t, c) => Chat(t, c);
            Horde.MyMap = () => _boot.Map.Current != null ? _boot.Map.Current.Name : "";
            Horde.LookOf = aid => aid == Session.AccountId ? Entities.SelfLook : Entities.Get(aid)?.Look;
            Debug.Log($"[Horde] the mode is ready: channel {(ch is HordeFakeChannel ? "the scripted stand-in (no relay on this server)" : _w8Stdb != null ? "the Loki Online module (" + ch.GetType().Name + ")" : ch.GetType().Name)}; {content.Weapons.Count} weapons, {content.Jobs.Count} jobs, {content.Enemies.Count} enemies, {content.Arenas.Count} arena(s); Settings → Horde: invitations {HordeWindows.InvitesShown}, table follow {HordeHud.FollowName(HordeHud.FollowMode)}, HUD size {HordeHud.Scale:0.##}");
        }

        /// <summary>Hook: Update. A run cannot outlive the RO session's world.</summary>
        private void UpdateW8Horde()
        {
            _w8Stdb?.Poll(); // (the module's link follows the session's connection)
            if (Horde == null || !Horde.InArena) return;
            if (Session == null || Session.State != RoSession.Stage.InGame || Session.Leaving != RoSession.Leave.None)
            {
                Debug.Log("[Horde] the character is leaving the world: the run is left");
                Horde.Channel.LeaveRun(); Horde.LeaveArena();
            }
        }

        /// <summary>Hides (true) or gives back (false) the RO world around the arena; see the class summary.</summary>
        private void W8HordeVeil(bool on)
        {
            var arena = Horde != null ? Horde.Arena : null;
            if (on)
            {
                if (Target != null) { Target.ClearOnMapChange(); Target.enabled = false; }
                Disarm();
                W3WalkCancel("horde");
                if (_attackTarget != 0) StopAttackNow();
                _pendingPickup = _pendingNpc = _pendingCapture = 0;
                if (WheelsOpen) CloseWheels();
                Hud?.Wrist.Hide();
                if (Audio != null)
                {
                    if (_w8Listener == null) _w8Listener = Audio.ListenerCell;
                    Audio.ListenerCell = () => new Vector2(-1e6f, -1e6f); // every world sound with a place is out of hearing
                    string arenaMap = arena?.Loaded != null ? arena.Loaded.Name : Horde?.Run?.Map;
                    string worldMap = _boot?.Map?.Current != null ? _boot.Map.Current.Name : StripExt(Session?.MapName ?? "");
                    if (!string.IsNullOrEmpty(arenaMap)) Audio.PlayHordeBgm(arenaMap, worldMap);
                }
                var m = arena?.Loaded;
                if (Sky != null && m != null) Sky.Apply(m.Name, false, m.Rsw, m.LightDirection);
                Loki.Voice.VoiceChat.PlainOnly = true; // friends' voices plain: their characters / headsets are hidden with the world
                if (View != null && View.Mode == VR.ViewController.ViewMode.FirstPerson && !_w8View.HasValue) { _w8View = View.Mode; View.SetMode(VR.ViewController.ViewMode.Follow); }
                Debug.Log("[Horde] the RO world is veiled: no input reaches the real character, world sounds with a place are not heard");
                return;
            }
            if (Target != null) Target.enabled = true;
            Loki.Voice.VoiceChat.PlainOnly = false;
            if (Audio != null && _w8Listener != null) { Audio.ListenerCell = _w8Listener; _w8Listener = null; }
            var wm = _boot.Map.Current;
            if (wm != null)
            {
                Audio?.StopHordeBgm(wm.Name);
                if (Sky != null) Sky.Apply(wm.Name, Room.IsIndoor(wm.Name), wm.Rsw, wm.LightDirection);
                if (Room != null && Room.IsIndoor(wm.Name) && Entities.Self != null) Room.Rebuild(wm.Name, wm.Gat, _boot.Map.transform, Vector2Int.RoundToInt(Entities.Self.Cell));
            }
            else
            {
                Audio?.StopHordeBgm(StripExt(Session?.MapName ?? ""));
            }
            if (World != null) Shader.SetGlobalVector("_LokiEnv", new Vector4(World.Env.r, World.Env.g, World.Env.b, 1)); // its night tint (WorldLook writes it only on a change)
            if (View != null && _w8View.HasValue) { View.SetMode(_w8View.Value); _w8View = null; }
            FocusSelf();
            Debug.Log("[Horde] the RO world is shown again, as it was");
        }
    }
}
