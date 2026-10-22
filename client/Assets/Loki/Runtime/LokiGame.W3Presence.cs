using Loki.Net;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 3, w3presence: floating headsets. The game's side of <see cref="IPresenceChannel"/>: once the map login made the
    /// account id known, a channel to the server's pose relay (Loki Host, port 7903 by default) and the <see cref="VR.HeadsetAvatars"/>
    /// that send this player's head and hands and draw the others'. Outside the RO protocol: nothing here reads or writes an RO
    /// packet, so the same game code works on the SpacetimeDB module: a Loki Online session gets
    /// <see cref="Net.Stdb.StdbPresenceChannel"/> (port wave H: the module is the relay). Hooks in LokiGame.cs: EnterMap (every map) and the smoke dispatch; ComfortWindows.cs: two rows.
    /// </summary>
    public sealed partial class LokiGame
    {
        public VR.HeadsetAvatars Headsets { get; private set; }
        private IPresenceChannel _presence;

        /// <summary>Hook: EnterMap (first entry, warps, map-server changes). -lokiPresence 0 switches the whole feature off.</summary>
        private void OnMapChangeW3Presence(string map)
        {
            if (Session == null || _boot.Rig == null) return;
            if (Headsets == null) StartPresence();
            (_presence as Net.Stdb.StdbPresenceChannel)?.NotifyEnteredMap();
            Headsets?.OnMapChanged(map);
            StartW18Share(); // client wave 18 (w18share): casting menus friends see (LokiGame.W18Share.cs; once)
            StartFreeSpots(); // free movement (wfree): the players' exact spots (LokiGame.WFree.cs; once)
        }

        private void StartPresence()
        {
            // Loki Online (port wave H, hpresence): the module is the relay; the channel rides on the session's own connection
            if (Session.OnlineLink && Stdb != null) { StartPresenceOn(new Net.Stdb.StdbPresenceChannel(Stdb), "the session's own connection"); return; }
            string host = Session.Host;
            var (port, from) = PresenceRelayChannel.PortFor(_boot.Config?.LoadedFrom, host, Session.Port);
            // a server entry's presencePort (w3login: loki.json's servers and the ones added in game), before -lokiPresencePort
            var e = Loki.UI.LoginPanel.BuildServerList(_boot.Config).Find(x => !x.IsStdb && x.port == Session.Port && string.Equals(x.host, host, System.StringComparison.OrdinalIgnoreCase));
            if (e != null && e.presencePort > 0) { port = e.presencePort; from = "its server entry"; }
            StartPresenceOn(new PresenceRelayChannel(host, port) { AccountProof = Session.WebAuthToken }, "port from " + from); // (client wave 18, w18acct: this login's proof for the account's settings)
        }

        /// <summary>The headsets on a channel, whichever transport it is: nothing below asks.</summary>
        private void StartPresenceOn(IPresenceChannel ch, string how)
        {
            _presence = ch;
            Headsets = VR.HeadsetAvatars.Create(ch, _boot.Rig, _boot.Map, id => Entities != null ? Entities.Get(id) : null, Session.AccountId);
            Headsets.Playing = () => Session != null && Session.State == RoSession.Stage.InGame && Session.Leaving == RoSession.Leave.None;
            // -lokiPresenceSend 1: a desktop client sends its (still) camera and hands too, e.g. the playtest buddy, so a lone headset
            // player sees a floating headset to check; without it only a client with an active headset sends
            ch.Begin(Session.AccountId, Session.SelectedChar != null ? Session.SelectedChar.Name : "");
            StartW18Account(ch); // client wave 18 (w18acct): the account's settings on the same channel (LokiGame.W18Account.cs)
            Debug.Log($"[Presence] {ch.Describe} ({how}); sharing my headset {VR.PresenceSettings.ShareMine} (only while a headset is active), showing others {VR.PresenceSettings.ShowOthers}");
        }
    }
}
