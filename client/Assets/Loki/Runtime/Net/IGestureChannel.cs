namespace Loki.Net
{
    /// <summary>
    /// Client wave 18 (w18share), the design's point 33: the casting menus friends see, outside the RO protocol, beside
    /// <see cref="IPresenceChannel"/> (whose poses say where the headsets and hands are). The game talks to this interface only: today
    /// Loki Host's relay (<see cref="RelayGestureChannel"/> over <see cref="PresenceRelayChannel"/>; the frames: <see cref="GestureWire"/>);
    /// later a SpacetimeDB transport (a table of open menus the module filters by the same relations) with no change in the game. A relay
    /// that does not forward menus (an older Loki Host) never says it can: nothing is sent, and no friend's menu ever arrives.
    /// </summary>
    public interface IGestureChannel
    {
        /// <summary>The current link forwards menus (and filters them by each sender's choice). False while not linked.</summary>
        bool CanShare { get; }
        /// <summary>Links that could carry menus so far (a new one: every open menu is sent again).</summary>
        int Links { get; }
        /// <summary>One whole GESTURE frame (<see cref="GestureWire.Encode"/>), sent on the current link; dropped when it cannot carry
        /// menus. Main thread.</summary>
        void Send(byte[] frame);
        /// <summary>A friend's menu that arrived: the sender's account id and the body (from its format byte). Main thread, until false.</summary>
        bool TryTake(out uint aid, out byte[] body);
        int Sent { get; }
        int Received { get; }
    }

    /// <summary>The relay's channel as an <see cref="IGestureChannel"/>.</summary>
    public sealed class RelayGestureChannel : IGestureChannel
    {
        private readonly PresenceRelayChannel _r;
        public RelayGestureChannel(PresenceRelayChannel relay) { _r = relay; }
        public bool CanShare => _r.RelayForwardsMenus;
        public int Links => _r.GestureLinks;
        public void Send(byte[] frame) => _r.SendGesture(frame);
        public bool TryTake(out uint aid, out byte[] body) => _r.TryTakeGesture(out aid, out body);
        public int Sent => _r.GesturesSent;
        public int Received => _r.GesturesReceived;
    }
}
