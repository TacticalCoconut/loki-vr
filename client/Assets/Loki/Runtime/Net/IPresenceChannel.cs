using System;
using UnityEngine;

namespace Loki.Net
{
    /// <summary>
    /// One player's headset and controllers, IN THE MAP'S OWN FRAME: Loki units (1 = one GAT cell, y up, World/RoSpace.cs) relative
    /// to the map root (MapLoader's transform). Every player scales, pans and turns the diorama as they like; poses in this frame
    /// mean the same place on every table. <see cref="MetresPerCell"/> is the sender's own table scale (to draw its headset at the
    /// right size relative to its hands).
    /// </summary>
    public sealed class PresencePose
    {
        public string Map = "";
        public float MetresPerCell;
        public Vector3 HeadPos, LeftPos, RightPos;
        public Quaternion HeadRot = Quaternion.identity, LeftRot = Quaternion.identity, RightRot = Quaternion.identity;
        public bool LeftTracked, RightTracked;

        public void CopyFrom(PresencePose o)
        {
            Map = o.Map; MetresPerCell = o.MetresPerCell; HeadPos = o.HeadPos; LeftPos = o.LeftPos; RightPos = o.RightPos;
            HeadRot = o.HeadRot; LeftRot = o.LeftRot; RightRot = o.RightRot; LeftTracked = o.LeftTracked; RightTracked = o.RightTracked;
        }
    }

    public enum PresenceLink { Off, Connecting, Connected, Waiting }

    /// <summary>
    /// The presence channel: where players' headsets go, outside the RO protocol (client wave 3, w3presence). The game talks to
    /// this interface only, beside <see cref="IAccountSession"/>: today <see cref="PresenceRelayChannel"/> (Loki Host's pose
    /// relay, a TCP side channel next to rAthena); later a SpacetimeDB implementation (a table the module owns, one row per
    /// player, subscribed per map) can replace it without a gameplay change. Players are identified by their RO account id
    /// (AID): rAthena gives a player's unit the account id as its block id (map/pc.cpp:1444 pc_setnewpc `sd->id = account_id`),
    /// so the AID pairs a headset with the character the RO server shows.
    /// </summary>
    public interface IPresenceChannel : IDisposable
    {
        /// <summary>For logs: "relay 127.0.0.1:7903" …</summary>
        string Describe { get; }
        PresenceLink Link { get; }
        /// <summary>Who we are, once per game (after the map login: the AID is known then).</summary>
        void Begin(uint aid, string characterName);
        /// <summary>Our pose (10-20 per second while on a map and sharing); the channel keeps only the latest if the link is slow.</summary>
        void Send(PresencePose pose);
        /// <summary>We are not on a map any more, or stopped sharing: the others stop seeing our headset at once.</summary>
        void Leave();
        /// <summary>Main thread, once per frame: raises the events for what arrived since the last call.</summary>
        void Poll();
        /// <summary>(AID, character name, pose) of another player on our map.</summary>
        event Action<uint, string, PresencePose> PoseReceived;
        /// <summary>That player left our map or the channel.</summary>
        event Action<uint> Gone;
        /// <summary>The link dropped: every pose seen so far is stale.</summary>
        event Action Lost;
    }
}
