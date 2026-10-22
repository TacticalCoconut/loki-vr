namespace Loki.Net
{
    /// <summary>
    /// Free movement (design note 2026-10-14: "exact sub-tile position sync across players"): the players' exact
    /// spots, outside the RO protocol, beside <see cref="IPresenceChannel"/>. The game talks to this interface only: today Loki Host's relay
    /// (<see cref="RelaySpotChannel"/> over <see cref="PresenceRelayChannel"/>; the frames: <see cref="SpotWire"/>); later a SpacetimeDB transport
    /// with no change in the game. A relay that does not forward spots (an older Loki Host) never says it can: nothing is sent, and every
    /// other player is drawn on its cell, as rAthena places it.
    /// </summary>
    public interface ISpotChannel
    {
        /// <summary>The current link forwards spots. False while not linked.</summary>
        bool CanShare { get; }
        /// <summary>Links that could carry spots so far (a new one: my spot is sent again at once).</summary>
        int Links { get; }
        /// <summary>My spot, a whole SPOT frame (<see cref="SpotWire.Encode"/>); dropped when the link cannot carry it. Main thread.</summary>
        void Send(byte[] frame);
        /// <summary>Another player's spot that arrived: its account id and the spot. Main thread, until false.</summary>
        bool TryTake(out uint aid, out SpotWire.State spot);
        int Sent { get; }
        int Received { get; }
    }

    /// <summary>The relay's channel as an <see cref="ISpotChannel"/>.</summary>
    public sealed class RelaySpotChannel : ISpotChannel
    {
        private readonly PresenceRelayChannel _r;
        public RelaySpotChannel(PresenceRelayChannel relay) { _r = relay; }
        public bool CanShare => _r.RelayForwardsSpots;
        public int Links => _r.SpotLinks;
        public void Send(byte[] frame) => _r.SendSpot(frame);
        public bool TryTake(out uint aid, out SpotWire.State spot) => _r.TryTakeSpot(out aid, out spot);
        public int Sent => _r.SpotsSent;
        public int Received => _r.SpotsReceived;
    }
}
