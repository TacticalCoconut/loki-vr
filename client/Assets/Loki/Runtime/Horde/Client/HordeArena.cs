using System.Collections.Generic;
using Loki.World;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game): the Horde arena on the table, and the "veil" over the RO world while a run is played.
    ///
    /// The arena is a second <see cref="MapLoader"/> ("horde-arena") beside the world's own ("diorama"), under the same diorama
    /// pivot (the bootstrap object the rig scales, pans and turns): the client draws the arena's map exactly as it draws any map,
    /// and every view mode, recentring and two-hand scaling work on it unchanged. Each frame the arena is shifted so that the
    /// player's run character sits at the pivot ("the table follows it", as the Follow view does for the world).
    ///
    /// The RO world is NOT unloaded, paused or touched: the session keeps reading packets, units keep walking, effects keep
    /// playing. It is only moved out of sight: the world's map object goes under a holder that sits 5 km under the table for
    /// the run (beyond every camera's far plane, out of every laser's 50 m reach), its sun light is switched off, and the
    /// global shader state the world sets every frame (indoor room mask, cut-away cone, night tint, fog, point lights, the
    /// map light) is set for the arena after the world's own components wrote it (LateUpdate, execution order 1100, after
    /// RoomMask / Cutaway at 1000). Leaving puts every one of them back.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public sealed class HordeArena : MonoBehaviour
    {
        public MapLoader Map { get; private set; }
        public MapLoader.LoadedMap Loaded => Map != null ? Map.Current : null;
        public Transform Root => Map != null ? Map.transform : null;
        public bool Active { get; private set; }
        public string MapName { get; private set; } = "";
        /// <summary>Where the run character is drawn (map cells, the arena's own frame): the table follows it (<see cref="Follow"/>).</summary>
        public Vector2 Focus;
        /// <summary>The run character's walking velocity (cells per second): the follow leads a little in that direction. Optional.</summary>
        public Vector2 FocusVel;
        /// <summary>Client wave 9 (h9move): how the table follows the character (a dead zone, a critically damped glide, a lead;
        /// modes Smooth / Tight / Fixed from the Settings row, set by HordeGame; never a snap). The follow code is
        /// Horde/Core/HordeMath.cs's HordeTableFollow (h9move's); this file only calls it.</summary>
        public readonly HordeTableFollow Follow = new HordeTableFollow();
        /// <summary>The run character's feet (world space), for the cut-away cone; null: no cone.</summary>
        public System.Func<Vector3?> FocusFeet;
        public string Describe { get; private set; } = "";

        private LokiBootstrap _boot;
        private Transform _holder;
        private readonly List<Light> _worldLights = new List<Light>();
        private GameObject _lightsOf;
        private static readonly int RoomParams = Shader.PropertyToID("_LokiRoomMaskParams"), CutParams = Shader.PropertyToID("_LokiCutParams"),
            CutA = Shader.PropertyToID("_LokiCutA"), CutB = Shader.PropertyToID("_LokiCutB");
        /// <summary>How far under the table the world waits during a run, in metres (beyond the far planes: 200 / 300 / 1500 m).</summary>
        public const float HideMetres = 5000f;

        public static HordeArena Create(LokiBootstrap boot)
        {
            var go = new GameObject("horde-arena-host");
            go.transform.SetParent(boot.transform, false);
            var a = go.AddComponent<HordeArena>();
            a._boot = boot;
            return a;
        }

        /// <summary>Loads the arena's map and hides the world. False (with the reason in <see cref="Describe"/>) when the map cannot be read:
        /// the world stays as it is.</summary>
        public bool Enter(string mapName)
        {
            if (Active) Leave();
            var go = new GameObject("horde-arena");
            go.transform.SetParent(_boot.transform, false);
            go.transform.localScale = Vector3.one * _boot.DioramaScale;
            Map = go.AddComponent<MapLoader>();
            Map.Init(_boot.Assets);
            try { Map.Load(mapName); }
            catch (System.Exception e)
            {
                Describe = $"the arena's map '{mapName}' could not be read ({e.GetType().Name}: {e.Message})";
                Debug.LogWarning("[Horde] " + Describe);
                Destroy(go); Map = null;
                RestoreWorldLight();
                return false;
            }
            // the laser stops on the arena's ground and does nothing there: no click reaches the RO world (DioramaRig: UiBlocker)
            if (Map.Current.Ground != null) Map.Current.Ground.AddComponent<UI.UiBlocker>();
            // the world under its holder (identity while no run is played: nothing changes for it)
            var world = _boot.Map.transform;
            if (_holder == null) { var h = new GameObject("world-holder"); h.transform.SetParent(_boot.transform, false); _holder = h.transform; }
            _holder.localPosition = Vector3.zero; _holder.localRotation = Quaternion.identity; _holder.localScale = Vector3.one;
            world.SetParent(_holder, false);
            Active = true;
            MapName = mapName ?? "";
            var g = Map.Current.Gat;
            Describe = $"{mapName} ({g.Width}x{g.Height} cells)";
            Debug.Log($"[Horde] arena {Describe} drawn on the table; the RO world is kept running, out of sight");
            Follow.Reset(Focus.x, Focus.y); // the run's start: the table is placed on the character at once, then only glides
            LateUpdate();
            return true;
        }

        /// <summary>Takes the arena down and puts the world back exactly as it was drawn.</summary>
        public void Leave()
        {
            if (!Active && Map == null) return;
            Active = false;
            MapName = "";
            if (Map != null) { Map.Unload(); Destroy(Map.gameObject); Map = null; }
            if (_holder != null)
            {
                _holder.localPosition = Vector3.zero;
                if (_boot.Map != null && _boot.Map.transform.parent == _holder) _boot.Map.transform.SetParent(_boot.transform, false);
            }
            foreach (var l in _worldLights) if (l != null) l.enabled = true;
            _worldLights.Clear(); _lightsOf = null;
            RestoreWorldLight();
            Shader.SetGlobalVector(CutParams, Vector4.zero); // the world's Cutaway writes its own next frame
            Debug.Log("[Horde] arena gone; the RO world is drawn again");
        }

        private void RestoreWorldLight()
        {
            var m = _boot != null && _boot.Map != null ? _boot.Map.Current : null;
            if (m == null || m.Rsw == null) return;
            Shader.SetGlobalVector("_LightDir", m.LightDirection);
            Shader.SetGlobalColor("_LightDiffuse", m.Rsw.LightDiffuse);
            Shader.SetGlobalColor("_LightAmbient", m.Rsw.LightAmbient);
        }

        private void LateUpdate()
        {
            if (!Active || Map == null || Map.Current == null) return;
            // keep the world out of sight whatever the table's scale (metres → the pivot's local units)
            float s = Mathf.Max(Mathf.Abs(_boot.transform.lossyScale.y), 1e-4f);
            if (_holder != null) _holder.localPosition = Vector3.down * (HideMetres / s);
            if (_boot.Map != null && _boot.Map.transform.parent != _holder && _holder != null) _boot.Map.transform.SetParent(_holder, false);
            // the world's sun (a new one after each world map change)
            var wm = _boot.Map != null ? _boot.Map.Current : null;
            if (wm != null && wm.Root != _lightsOf)
            {
                _lightsOf = wm.Root; _worldLights.Clear();
                foreach (var l in wm.Root.GetComponentsInChildren<Light>(true)) if (l.enabled) { l.enabled = false; _worldLights.Add(l); }
            }
            // the run character near the pivot: the table follows it (h9move's HordeTableFollow: dead zone, damped glide, lead)
            Follow.Step(Focus.x, Focus.y, FocusVel.x, FocusVel.y, Time.unscaledDeltaTime);
            var gat = Map.Current.Gat;
            float fx = Follow.X, fy = Follow.Y;
            int cx = Mathf.Clamp(Mathf.FloorToInt(fx), 0, gat.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(fy), 0, gat.Height - 1);
            float gy = -gat.Get(cx, cy).AverageHeight * RoSpace.Scale;
            Map.transform.localPosition = -new Vector3(fx, gy, fy) * _boot.DioramaScale;
            // the arena's own look (globals the world's components write every frame or on its map changes)
            var am = Map.Current;
            Shader.SetGlobalVector("_LightDir", am.LightDirection);
            Shader.SetGlobalColor("_LightDiffuse", am.Rsw.LightDiffuse);
            Shader.SetGlobalColor("_LightAmbient", am.Rsw.LightAmbient);
            Shader.SetGlobalVector(RoomParams, Vector4.zero); // an indoor room mask of the world must not cut the arena
            Shader.SetGlobalVector("_LokiEnv", new Vector4(1, 1, 1, 1));
            Shader.SetGlobalVector("_LokiFog", Vector4.zero);
            Shader.SetGlobalFloat("_LokiPLCount", 0);
            // the cut-away cone, for the run character (World/Cutaway.cs's formula and numbers)
            var feet = FocusFeet?.Invoke(); var cam = Camera.main;
            if (feet.HasValue && cam != null)
            {
                float cell = Mathf.Abs(Map.transform.lossyScale.x);
                Shader.SetGlobalVector(CutA, feet.Value + Vector3.up * cell);
                Shader.SetGlobalVector(CutB, cam.transform.position);
                Shader.SetGlobalVector(CutParams, new Vector4(1f, 3f * cell, feet.Value.y + 0.5f * cell, 0f));
            }
            else Shader.SetGlobalVector(CutParams, Vector4.zero);
        }

        /// <summary>Map-local height (y up, cells) of a place.</summary>
        public float Height(float x, float y)
        {
            var gat = Map != null && Map.Current != null ? Map.Current.Gat : null;
            if (gat == null) return 0f;
            int cx = Mathf.Clamp(Mathf.FloorToInt(x), 0, gat.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(y), 0, gat.Height - 1);
            return -gat.Get(cx, cy).AverageHeight * RoSpace.Scale;
        }

        /// <summary>Client wave 18 (h18feel), point 25: the ground's map-local height (cells) under a place as the ground is drawn: between its
        /// cell's four corners (GatFile.Cell H0 SW, H1 SE, H2 NW, H3 NE), not the cell's average (<see cref="Height"/>, where the sprites stand).
        /// A slice laid over the ground with it follows a slope between the cells' steps.</summary>
        public float GroundHeight(float x, float y)
        {
            var gat = Map != null && Map.Current != null ? Map.Current.Gat : null;
            if (gat == null) return 0f;
            int cx = Mathf.Clamp(Mathf.FloorToInt(x), 0, gat.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(y), 0, gat.Height - 1);
            var c = gat.Get(cx, cy); float fx = Mathf.Clamp01(x - cx), fy = Mathf.Clamp01(y - cy);
            return -Mathf.Lerp(Mathf.Lerp(c.H0, c.H1, fx), Mathf.Lerp(c.H2, c.H3, fx), fy) * RoSpace.Scale;
        }

        /// <summary>Map-local position of a place on the ground (the wire's cell coordinates: 150.5 is cell 150's centre).</summary>
        public Vector3 Local(float x, float y) => new Vector3(x, Height(x, y), y);

        /// <summary>Intersects a world-space ray with the arena's ground collider (or focus plane) and returns the cell coordinates.</summary>
        public bool GroundCell(Ray ray, out Vector2 cell)
        {
            cell = Focus;
            if (!Active || Map == null || Map.Current == null) return false;
            var col = Map.Current.Ground != null ? Map.Current.Ground.GetComponent<Collider>() : null;
            if (col != null && col.Raycast(ray, out var hit, 50f))
            {
                var loc = Map.transform.InverseTransformPoint(hit.point);
                cell = new Vector2(loc.x, loc.z);
                return true;
            }
            var footWorld = Map.transform.TransformPoint(Local(Focus.x, Focus.y));
            var plane = new Plane(Map.transform.up, footWorld);
            if (plane.Raycast(ray, out float t) && t > 0f && t <= 50f)
            {
                var loc = Map.transform.InverseTransformPoint(ray.GetPoint(t));
                cell = new Vector2(loc.x, loc.z);
                return true;
            }
            return false;
        }

        private void OnDestroy() { if (Active) Leave(); }
    }
}
