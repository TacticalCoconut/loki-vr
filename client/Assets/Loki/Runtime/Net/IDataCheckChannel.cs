using System;

namespace Loki.Net
{
    /// <summary>
    /// The game-data check, outside the RO protocol (client wave 7, w7host): this client tells the host a short stamp of its
    /// own Ragnarok Online data (IO/MapDataStamp.cs: a hash of its maps' cells) and learns whether it is the host's. The game
    /// talks to this interface only, beside <see cref="IPresenceChannel"/> and <see cref="IVoiceChannel"/>: today
    /// <see cref="PresenceRelayChannel"/> (Loki Host's relay compares the stamp with the one of the install its server's map
    /// cache was made from); a SpacetimeDB transport can replace it later (a reducer taking the stamp, the module knowing its
    /// own) with no change in the game. A host that does not compare (an older Loki Host, a server without one) never answers:
    /// no check, nothing else changes.
    /// </summary>
    public interface IDataCheckChannel
    {
        /// <summary>My stamp and a label for people (the install's patch number), once they are known: sent once per link to a
        /// host that compares. Any thread.</summary>
        void SetDataStamp(string stamp, string label);
        /// <summary>The host's answer: (my data differs from the host's, the host's stamp, the host's label). Main thread
        /// (raised from <see cref="IPresenceChannel.Poll"/>).</summary>
        event Action<bool, string, string> DataChecked;
    }
}
