#nullable disable
using System;
using Loki.Horde;

namespace Loki.Net
{
    /// <summary>Whether the mode can be played over this channel now, and why not.</summary>
    public enum HordeLinkState
    {
        /// <summary>No link (yet): connecting, or the server has no relay at all.</summary>
        NoLink,
        /// <summary>Linked to a host from before Horde existed (an older Loki Host): everything else works, the mode does not.</summary>
        OldHost,
        /// <summary>Linked, and the host has the mode switched off.</summary>
        SwitchedOffByHost,
        /// <summary>Linked, but the host's Horde tables are another version than this client's (one of the two must update Loki).</summary>
        OtherVersion,
        Ready,
        /// <summary>Linked, but the host runs another version of Horde's protocol (client wave 9: HordeWire.Version; an older or a
        /// newer Loki Host): headsets, voice and the data check work, the mode does not, until one of the two updates Loki.</summary>
        OtherProtocol,
    }

    /// <summary>
    /// The Horde channel (client wave 8): the ONE way the co-op side mode talks to its server, outside the RO protocol,
    /// beside <see cref="IPresenceChannel"/> and <see cref="IVoiceChannel"/>. Gameplay code never asks which server it is on.
    /// Today: Loki Host's relay (the simulation runs there, host-authoritative; wire Horde/Core/Net/HordeWire.cs, client
    /// state machine Horde/Core/Net/HordeClient.cs, which implements this interface over any byte transport); later a
    /// SpacetimeDB module can carry it (reducers for the requests, subscribed rows / events for the rest) with no change in
    /// the game. No UnityEngine type appears here: Loki Host's headless bots compile this very file.
    ///
    /// Players are identified by their RO account id, as for the headsets (map/pc.cpp:1444: a player's unit id is its
    /// account id). The real character is never touched by a run; the only thing that reaches it is the EXP reward the HOST
    /// decides at the end (<see cref="Ended"/> says the share, <see cref="Rewarded"/> the EXP once rAthena's side granted it).
    ///
    /// Threading: every member is for the main thread; every event is raised from <see cref="Poll"/>.
    /// </summary>
    public interface IHordeChannel
    {
        HordeLinkState LinkState { get; }
        /// <summary>The mode's state on this server (never null): what the panel shows. <see cref="StatusChanged"/> says when it changed.</summary>
        HordeStatus Status { get; }
        /// <summary>I am a player of the run being played.</summary>
        bool InRun { get; }
        /// <summary>The run I am in (null when not in one).</summary>
        HordeRunInfo Run { get; }
        /// <summary>The run's state as last heard (never null; meaningful while <see cref="InRun"/>).</summary>
        HordeWorldView World { get; }
        /// <summary>The channel's clock in seconds: the one <see cref="HordeWorldView.Alpha"/> and DrawTick take.</summary>
        double Now { get; }

        // ---- requests (each is ignored when it makes no sense now; the host decides)
        /// <summary>"Start a run": the host asks every connected player whether they join; I am in.</summary>
        void RequestStart();
        /// <summary>My answer to an invitation, or (join = true while a run is played) joining it late from the panel.</summary>
        void Answer(uint runId, bool join);
        /// <summary>My choice among an offer's cards (0-based).</summary>
        void Choose(int offerId, int index);
        /// <summary>I leave the run: my result stands as it is now.</summary>
        void LeaveRun();
        /// <summary>Client wave 14 (w14kit), point 15 of the design's headset list: END the run for everyone (a player who was in it, left it or
        /// still plays it); every player gets the EXP earned so far (the host's results and rewards, as at a run's end).</summary>
        void EndRun(uint runId);
        /// <summary>Client wave 15 (w15hfx), point 19 of the design's wave 14 list: PAUSE (true) the run I am in for everyone, or resume it
        /// (false: any player of the run may). The holder is the player whose flags carry HordePlayerFlags.Held.</summary>
        void Pause(bool pause);
        /// <summary>Casts an owned active laser-targeted weapon at the given map cell coordinates (cellX, cellY).</summary>
        void CastActive(int weaponId, float cellX, float cellY);

        // ---- my own movement
        /// <summary>My stick: a direction with a length of 0..1 (x east, y north in the map's cells), as often as it
        /// changes. The channel sends it at the simulation's rate and predicts my own movement from it.</summary>
        void SetMove(float x, float y);
        /// <summary>Where to draw MY character now: predicted from my own stick, corrected by the host's snapshots, in map cells.</summary>
        float MyX { get; }
        float MyY { get; }

        /// <summary>Once per frame: raises the events for what arrived, runs my movement.</summary>
        void Poll();

        event Action StatusChanged;
        /// <summary>The prompt. Answer with <see cref="Answer"/>; unanswered, it lapses with the countdown.</summary>
        event Action<HordeInvite> Invited;
        /// <summary>I am in a run (at its start, or joined late): draw the arena. World is filled before this is raised.</summary>
        event Action<HordeRunInfo> RunStarted;
        /// <summary>A new snapshot was put into World (about 10 a second).</summary>
        event Action SnapshotArrived;
        /// <summary>Something happened that is worth drawing once (hits, deaths, level-ups, falls …).</summary>
        event Action<HordeEvent> Happened;
        /// <summary>A lasting effect began (it is in World.Fx until it ends) / ended (hit = true: it ended on a hit, not on its time).</summary>
        event Action<HordeFxView> FxBegan;
        event Action<HordeFxView, bool> FxEnded;
        /// <summary>A choice for me opened / is over (answered, timed out or the run ended): the offer's id.</summary>
        event Action<HordeOffer> Offered;
        event Action<int> OfferClosed;
        /// <summary>The run is over for me (it ended, or I left): the results, with each player's reward share.</summary>
        event Action<HordeResults> Ended;
        /// <summary>The real EXP my character was given (seconds after the results, or when it is next online).</summary>
        event Action<HordeRewardNote> Rewarded;
        /// <summary>The link dropped while I was in a run (the channel joins again by itself when the link is back and the run still on).</summary>
        event Action Lost;
    }
}
