using System.Collections.Generic;
using Loki.UI;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 5, w5friends: the servers the login screen offers, in the player's log (Player.log), once at start. The friends'
    /// guide (client/release/) asks a friend to send that log to the host when something goes wrong: this line says which servers
    /// (names, addresses, kind; the ones added in game marked) the friend's Loki had, i.e. whether the host's address arrived. No
    /// account or password is written. In every build (not test code).
    /// </summary>
    public sealed partial class LokiGame
    {
        private static void W5FriendsLogServers(List<LoginPanel.ServerEntry> servers)
        {
            var parts = new List<string>();
            foreach (var e in servers) parts.Add($"{e.name} {e.Address} ({e.KindName}{(e.userAdded ? ", added in game" : "")})");
            Debug.Log($"[Login] servers on the login screen ({servers.Count}): {string.Join("; ", parts)}");
        }
    }
}
