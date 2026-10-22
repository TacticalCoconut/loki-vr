using Loki.Sprites;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4b, w4granny: the Granny 3-D units (design notes, "Wave 4b" → w4granny). The pieces live in their own files:
    ///  • Runtime/Granny/*.cs — the C# port of granny-ro-js 1.5.0 (MIT, THIRD_PARTY.md): Oodle0, the type tree, skeletons, meshes,
    ///    materials, textures, animations, poses; proven equal to the package's own output on the player's 21 .gr2 files
    ///    (client/tools/w4granny, design notes);
    ///  • Sprites/GrannyUnit.cs — a unit whose job name is a roster .gr2 (the Emperium, the three castle guardians, the guild flag, the
    ///    treasure boxes) drawn in 3-D from the player's GRF, animated and placed as roBrowserLegacy does; the 2-D stand-in
    ///    (Sprites/RoNames.cs) while it loads and as the fallback;
    ///  • Resources/Shaders/LokiGranny.shader — rB's GR2 shading.
    /// This part hands GrannyUnit the map (its RSW light) and the guild emblems (Game/Guild.cs). Nothing here reads a packet: the units
    /// come and go, act and die through the usual entity packets (Sprites/EntityManager.cs), so it works unchanged on the SpacetimeDB
    /// module. Smoke: LokiGame.W4GrannySmoke.cs (-lokiSmokeOnly w4granny-host / w4granny-guest).
    /// </summary>
    public sealed partial class LokiGame
    {
        private void BeginW4Granny(LokiBootstrap boot)
        {
            GrannyUnit.Map = boot.Map;
            GrannyUnit.EmblemSource = (gid, version) => Guild != null ? Guild.EmblemOf(gid, version, false) : null;
        }
    }
}
