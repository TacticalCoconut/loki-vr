using System;
using System.Collections.Generic;
using System.IO;

namespace Loki.VrMaps
{
    /// <summary>
    /// The numbers of Loki's three views and of its drawing that live in MonoBehaviours (which this tool cannot compile), restated
    /// with their source. <see cref="Check"/> re-reads those source lines: a changed number fails `architect constants` (no silent
    /// drift). The thresholds (<see cref="Thresholds"/>) are this tool's own (design notes section 2, FLAGGED).
    /// </summary>
    public static class Views
    {
        // ---- the client's numbers (client/Assets/Loki/Runtime/...)
        /// <summary>VR/ViewController.cs:24</summary>
        public const float FirstPersonMetresPerCell = 0.7f, FollowMetresPerCell = 0.06f, EyeHeightMetres = 1.5f;
        /// <summary>VR/ViewController.cs:27 (the extra pitch) and its comment :35-36 (the default spot: 0.99 m from eyes 1.60 m up)</summary>
        public const float FollowExtraPitch = 7f, FollowMinDown = 12f, FollowMaxDown = 80f;
        /// <summary>LokiBootstrap.cs:20, :58 (metres per cell at table zoom 1)</summary>
        public const float TableMetresPerCell = 0.02f;
        /// <summary>VR/DioramaRig.cs:30-32</summary>
        public const float TableHeight = 0.95f, TableDistance = 0.7f, TableMinScale = 0.15f, TableMaxScale = 8f;
        /// <summary>VR/DioramaRig.cs:149</summary>
        public const float NearClipMetres = 0.02f;
        /// <summary>World/Cutaway.cs:17</summary>
        public const float CutRadiusCells = 3f, CutFeetMarginCells = 0.5f, CutChestCells = 1f;
        /// <summary>Fx/OwnFxRenderer.cs:148: a player's height in cells</summary>
        public const float PlayerHeightCells = 2.4f;
        /// <summary>World/WorldLook.cs:99 (16 nearest lights), :117 (map effects within 25 cells), :90 (fog x 240)</summary>
        public const int MaxPointLights = 16; public const float MapEffectCells = 25f, FogCellsPerUnit = 240f;
        /// <summary>rA conf/battle/client.conf:39 (area_size)</summary>
        public const int ServerAreaSize = 14;

        // ---- derived
        public static float EyeCells => EyeHeightMetres / FirstPersonMetresPerCell;      // 2.142857
        public static float FpCells(float metres) => metres / FirstPersonMetresPerCell;
        /// <summary>The Follow eye relative to the character's feet at the default pitch: the spot 0.75 m ahead at table height seen
        /// from eyes 1.60 m up (ViewController.cs:139-162, comment :35-36), turned 7 deg further down about the eyes.</summary>
        public static void FollowEye(out float backCells, out float upCells)
        {
            const float headY = 1.60f; float dy = headY - TableHeight, level = 0.75f;
            double dist = Math.Sqrt(level * level + dy * dy);
            double down = Math.Atan2(dy, level) + FollowExtraPitch * Math.PI / 180.0;
            backCells = (float)(dist * Math.Cos(down) / FollowMetresPerCell);
            upCells = (float)(dist * Math.Sin(down) / FollowMetresPerCell);
        }

        /// <summary>The tool's thresholds (plan section 2; all FLAGGED: configurable). Cells unless named.</summary>
        public static class Thresholds
        {
            public static readonly float HeadInside = FpCells(0.10f), HeadFace = FpCells(0.25f), HeadNear = FpCells(0.50f);
            public static float CeilingHigh => EyeCells + FpCells(0.10f);
            /// <summary>A ceiling is a surface over the head: from 1.5 cells (1.05 m) up; lower ones are furniture in the body's way (C2).</summary>
            public const float CeilingFrom = 1.5f;
            public const float CeilingSprite = PlayerHeightCells, CeilingCramped = 3.0f;
            public const float FloorGapMedium = 0.15f, FloorGapHigh = 0.5f, FloorSearch = 3f;
            public const float WadeCells = 0.5f;
            public const float FlatStep = 0.3f, ClearFrom = 0.15f, InvisibleWallCapsule = 0.35f, WallVisible = 0.15f;
            /// <summary>The body from 0.6 cells (42 cm: above flower beds and petal cards, which RO lays at knee height) to 2.0.</summary>
            public const float BodyFrom = 0.6f, BodyTo = 2.0f, BodyRadius = 0.25f;
            /// <summary>A blocked flat cell with a drawn obstacle within this many cells is only padding before it (low).</summary>
            public const float PaddingCells = 1.0f;
            public const float WallOnPathMedium = 0.3f, WallOnPathHigh = 1f;
            public const int HoleHigh = 2, HoleMedium = 10, HoleLow = 30;
            public const float LightReach = 30f;
            public const float TableReachMetres = 1.2f, StandingHeadMetres = 1.6f;
            public const float RoomCeilingSearch = 10f, OpenCeilingShare = 0.5f, LowWallMargin = 0.3f, LowWallShare = 0.25f;
            public const float SoundHeight = 3f;
        }

        // ---- the drift check
        private static readonly (string file, string text, string what)[] Sources =
        {
            ("VR/ViewController.cs", "public float FollowMetresPerCell = 0.06f, FirstPersonMetresPerCell = 0.7f, EyeHeight = 1.5f;", "view scales and eye height"),
            ("VR/ViewController.cs", "public float FollowExtraPitch = 7f;", "Follow extra pitch"),
            ("VR/ViewController.cs", "FollowMinDown = 12f, FollowMaxDown = 80f", "Follow pitch range"),
            ("VR/ViewController.cs", "d.position = new Vector3(hp.x, hp.y - EyeHeight, hp.z);", "First person: the eyes straight above the feet"),
            ("LokiBootstrap.cs", "public float RigTableScale = 0.02f;", "table metres per cell"),
            ("LokiBootstrap.cs", "DioramaScale = RigTableScale;", "the table scale in play"),
            ("VR/DioramaRig.cs", "public float TableHeight = 0.95f;", "table height"),
            ("VR/DioramaRig.cs", "public float TableDistance = 0.7f;", "table distance"),
            ("VR/DioramaRig.cs", "public float MinScale = 0.15f, MaxScale = 8f;", "table zoom range"),
            ("VR/DioramaRig.cs", "Head.nearClipPlane = 0.02f;", "near clip"),
            ("World/Cutaway.cs", "public float RadiusCells = 3f, FeetMarginCells = 0.5f, ChestCells = 1f;", "cutaway"),
            ("Fx/OwnFxRenderer.cs", "u.IsPlayerType ? 2.4f", "player height"),
            ("World/WorldLook.cs", "for (int i = 0; i < _lights.Count && n < 16; i++)", "16 point lights"),
            ("World/WorldLook.cs", "bool near = d.magnitude < 25f;", "map effects within 25 cells"),
            ("World/WorldLook.cs", "new Vector4(_fogNear * 240f * scale, _fogFar * 240f * scale, 1, 0)", "fog x 240"),
            ("World/WorldLook.cs", "_hasFog = _sky != null && _sky.FogOf(map, out _fogNear, out _fogFar, out _fogColor) && !indoor;", "no fog indoors"),
            ("World/MapLoader.cs", "var centerOffset = new Vector3(gnd.Width, 0, gnd.Height);", "model centre offset"),
            ("World/MapLoader.cs", "var g = GroundMeshBuilder.Build(gnd, true, ti => ti >= 0 && ti < blackTex.Length && blackTex[ti]);", "ground: black textures culled"),
            ("World/MapLoader.cs", "if (_assets.IsBlackTexture(tn))", "models: black textures not drawn"),
            ("World/MapLoader.cs", "if (!(t.H0 > level - waveHeight || t.H1 > level - waveHeight || t.H2 > level - waveHeight || t.H3 > level - waveHeight)) continue;", "water tiles"),
            ("World/MapLoader.cs", "if (gnd.Water != null) { level = gnd.Water.Level;", "water from the GND first"),
            ("World/RoAssets.cs", "if (IsAllBlack(img)) _black.Add(key);", "the black-texture test"),
            ("../Resources/Shaders/LokiGround.shader", "Cull Back", "ground culls back faces"),
            ("../Resources/Shaders/LokiModel.shader", "_Cull (\"Cull\", Float) = 2", "models cull back faces"),
            // wave 27 (wmodel): the fix kind M restates these (Models.cs): the model shadow pass draws both sides; the back copies cast none
            ("../Resources/Shaders/LokiModel.shader", "ZWrite On ZTest LEqual Cull Off", "model shadows drawn from both sides"),
            ("World/MapLoader.cs", "br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;", "the fix's model back copies cast no shadow"),
            ("../Resources/Shaders/LokiModel.shader", "if (all(e > v.uv2.xyz - 0.25) && all(e < v.uv3.xyz + 0.25))", "back copies left out in the table / follow views inside their model's box (Models.cs InBox)"),
            ("../Resources/Shaders/LokiWater.shader", "Cull Off", "water drawn from both sides"),
            ("Audio/LokiAudio.cs", "Range = Mathf.Max(1f, s.Range * RoSpace.Scale)", "sound range in cells"),
            ("Sprites/Unit.cs", "h = -_gat.Get(cx, cy).AverageHeight * RoSpace.Scale;", "units stand on the GAT"),
        };

        /// <summary>Checks every restated number against the client's source under <paramref name="runtimeDir"/>
        /// (client/Assets/Loki/Runtime). Returns the failures (empty: all still true).</summary>
        public static List<string> Check(string runtimeDir)
        {
            var bad = new List<string>();
            var cache = new Dictionary<string, string>();
            foreach (var (file, text, what) in Sources)
            {
                string p = Path.GetFullPath(Path.Combine(runtimeDir, file.Replace('/', Path.DirectorySeparatorChar)));
                if (!cache.TryGetValue(p, out var src)) { src = File.Exists(p) ? File.ReadAllText(p) : null; cache[p] = src; }
                if (src == null) bad.Add($"{file}: missing ({what})");
                else if (src.IndexOf(text, StringComparison.Ordinal) < 0) bad.Add($"{file}: no longer says `{text}` ({what})");
            }
            return bad;
        }
        public static int SourceCount => Sources.Length;
    }
}
