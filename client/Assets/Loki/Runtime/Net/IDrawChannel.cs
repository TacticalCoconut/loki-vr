namespace Loki.Net
{
    /// <summary>
    /// Client wave 20 (w20pen), the design's point 57: the air pen's drawings other players see, outside the RO protocol, beside
    /// <see cref="IPresenceChannel"/> (whose poses tell the relay which map a player is on; its GONE says a player left it). The game talks to
    /// this interface only: today Loki Host's relay (<see cref="RelayDrawChannel"/> over <see cref="PresenceRelayChannel"/>; the frames:
    /// <see cref="DrawWire"/>); later a SpacetimeDB transport (a table of strokes the module filters by the same relations) with no change in the
    /// game. A relay that does not forward drawings (an older Loki Host) never says it can: nothing is sent, and no drawing ever arrives (the
    /// player's own drawings still show on their own table).
    /// </summary>
    public interface IDrawChannel
    {
        /// <summary>The current link forwards drawings (and filters them by each sender's "Drawings seen by"). False while not linked.</summary>
        bool CanShare { get; }
        /// <summary>Links that could carry drawings so far (a new one: every drawing of mine is sent again).</summary>
        int Links { get; }
        /// <summary>One whole DRAW frame (<see cref="DrawWire"/>), sent on the current link; dropped when it cannot carry drawings. Main thread.</summary>
        void Send(byte[] frame);
        /// <summary>Another player's drawing frame that arrived: the sender's account id, what the sender is to me (the relay's relation bits:
        /// friends 2, family 4, guild 8; 0 none or not known) and the body (from its format byte). Main thread, until false.</summary>
        bool TryTake(out uint aid, out byte relation, out byte[] body);
        int Sent { get; }
        int Received { get; }
    }

    /// <summary>The relay's channel as an <see cref="IDrawChannel"/>.</summary>
    public sealed class RelayDrawChannel : IDrawChannel
    {
        private readonly PresenceRelayChannel _r;
        public RelayDrawChannel(PresenceRelayChannel relay) { _r = relay; }
        public bool CanShare => _r.RelayForwardsDrawings;
        public int Links => _r.DrawLinks;
        public void Send(byte[] frame) => _r.SendDraw(frame);
        public bool TryTake(out uint aid, out byte relation, out byte[] body) => _r.TryTakeDraw(out aid, out relation, out body);
        public int Sent => _r.DrawsSent;
        public int Received => _r.DrawsReceived;
    }
}
