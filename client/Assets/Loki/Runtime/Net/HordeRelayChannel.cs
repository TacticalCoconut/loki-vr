using Loki.Horde;
using Loki.Horde.Net;
using UnityEngine;

namespace Loki.Net
{
    /// <summary>
    /// Client wave 8 (w8game): <see cref="IHordeChannel"/> over Loki Host's relay. Nothing of the wire is here: w8core's
    /// <see cref="HordeClient"/> (Horde/Core/Net: the messages, the snapshots, my own movement's prediction) is the channel, and
    /// the relay connection the headsets and voice already use (<see cref="PresenceRelayChannel"/>) is its byte transport:
    /// <c>AttachHorde</c> tells it about each link (and whether that relay's WELCOME carried the Horde capability: an older Loki
    /// Host never does, and then the mode says so and nothing is sent), <c>SendHorde</c> writes its frames on the link thread.
    /// A SpacetimeDB transport can replace the relay later with no change in the game (IHordeChannel is all the game sees).
    /// </summary>
    public static class HordeRelayChannel
    {
        /// <param name="roSex">The real character's sex as rAthena sends it (0 female, 1 male): jobs that depend on it (Bard /
        /// Dancer) follow it.</param>
        public static HordeClient Open(PresenceRelayChannel relay, HordeContent content, int roSex)
        {
            var c = new HordeClient(content, () => Time.realtimeSinceStartupAsDouble)
            {
                Sex = (byte)(roSex == 1 ? 1 : roSex == 0 ? 2 : 0),
                Log = line => Debug.Log("[Horde] " + line),
            };
            c.Send = relay.SendHorde;
            relay.AttachHorde(c);
            Debug.Log($"[Horde] channel: Loki Host's relay ({relay.Describe}); tables {c.ContentStamp}");
            return c;
        }
    }
}
