using System.Collections.Generic;
using System.Text;
using Loki.Formats;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Horde.Client
{
    /// <summary>
    /// Client wave 8 (w8game): the frames of the sprites the arena draws by the hundred (monsters, summons, pickups), from the
    /// player's own install through the client's existing loaders: <see cref="RoAssets.Sprite"/> reads the SPR / ACT the client's
    /// sprite table names (Sprites/RoNames.cs BodyPath for a monster id, Game/GroundItems.cs ItemSpriteDir + the item table's
    /// resource name for an item), and <see cref="SpriteFrameCache.GetComposite"/> makes the same relief mesh + texture the
    /// world's <see cref="ActorSprite"/> draws. What is NOT reused is ActorSprite's per-unit component (a GameObject, a material
    /// and a string key built every frame per unit): 300 of them cost too much, so the arena keeps one cached frame per (sprite,
    /// action, direction, frame index) and draws it with Graphics.DrawMesh, one call per enemy, no GameObject per enemy.
    /// The frame choice follows ActorSprite.BuildFrame for a body-only sprite (action = group × 8 + direction, the ACT delay
    /// in 25 ms units, the layers composited in their ACT order).
    ///
    /// Client wave 9 (h9feel): the first-use hitch. Wave 8 built each frame the first time it was drawn: a new monster kind cost a
    /// 50-180 ms frame (measured, design notes). Now every monster, item and job sprite the run's content can show is
    /// queued at the lobby (<see cref="QueueKind"/>, <see cref="QueueItem"/>, <see cref="QueueUnit"/>) and built a few
    /// milliseconds a frame (<see cref="PrewarmSlice"/>) during the countdown, so the first appearance finds its frames made. A
    /// frame also carries its ACT sound event (a monster's own "poring_die.wav" on its death frames): HordeWorldDraw plays it.
    /// </summary>
    public sealed class HordeSprites
    {
        public sealed class Body
        {
            public string Path; public SprFile Spr; public ActFile Act; public bool Ok; public int Index;
            public readonly Dictionary<int, Frame> Frames = new Dictionary<int, Frame>();
        }
        public sealed class Frame
        {
            public Mesh Mesh; public Texture Texture; public Vector2 CenterPx; public float WidthPx, HeightPx;
            /// <summary>The ACT frame index within its action, and the frame's sound event (".wav" names only; null = none).</summary>
            public int Index; public string Event;
        }

        private readonly RoAssets _assets;
        private readonly Dictionary<string, Body> _bodies = new Dictionary<string, Body>();
        private readonly Dictionary<int, Body> _mobs = new Dictionary<int, Body>(), _items = new Dictionary<int, Body>();
        private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();
        private readonly StringBuilder _kb = new StringBuilder();
        private readonly Dictionary<Texture, Material> _materials = new Dictionary<Texture, Material>();
        private Shader _shader;
        public int BodiesMissing { get; private set; }
        /// <summary>Frames built on demand by <see cref="Get"/> (a hitch each when drawn), and by the pre-build.</summary>
        public int BuiltOnDemand { get; private set; }
        public int BuiltAhead { get; private set; }

        public HordeSprites(RoAssets assets) { _assets = assets; }
        public RoAssets Assets => _assets;

        public Body Mob(int mobId)
        {
            if (_mobs.TryGetValue(mobId, out var b)) return b;
            b = Load(RoNames.BodyPath(mobId, 0));
            _mobs[mobId] = b;
            return b;
        }

        public Body Item(int itemId)
        {
            if (_items.TryGetValue(itemId, out var b)) return b;
            var info = Game.GameDb.GetItem(itemId);
            b = Load(info != null && !string.IsNullOrEmpty(info.Resource) ? Game.GroundItems.ItemSpriteDir + info.Resource : null);
            _items[itemId] = b;
            return b;
        }

        private Body Load(string path)
        {
            if (string.IsNullOrEmpty(path)) { BodiesMissing++; return new Body { Path = "", Ok = false }; }
            if (_bodies.TryGetValue(path, out var b)) return b;
            b = new Body { Path = path, Index = _bodies.Count + 1 };
            try { b.Ok = _assets != null && _assets.Sprite(path, out b.Spr, out b.Act) && b.Act != null && b.Spr != null && b.Act.Actions.Length > 0; }
            catch (System.Exception e) { Debug.LogWarning($"[Horde] sprite {path}: {e.Message}"); b.Ok = false; }
            if (!b.Ok) { BodiesMissing++; Debug.LogWarning("[Horde] no sprite " + path + " in the game data: drawn as a dot"); }
            _bodies[path] = b;
            return b;
        }

        private static int ActionIndex(Body b, int group, int dir)
        {
            int n = b.Act.Actions.Length, ai = group * 8 + (dir & 7);
            return ai >= n ? Mathf.Min(dir & 7, n - 1) : ai;
        }

        private static ActFile.Action ActionOf(Body b, int group, int dir) => b.Act.Actions[ActionIndex(b, group, dir)];

        /// <summary>The frame of action group <paramref name="group"/> seen from drawn direction <paramref name="dir"/> (0..7, camera
        /// applied) at <paramref name="ms"/> into the action; looping or held on its last frame. Null when nothing can be drawn.</summary>
        public Frame Get(Body b, int group, int dir, float ms, bool loop, float speed = 1f)
        {
            if (b == null || !b.Ok) return null;
            var action = ActionOf(b, group, dir);
            int n = action.Frames.Length;
            if (n == 0) return null;
            int f = (int)(ms / (Mathf.Max(action.Delay, 1f) * 25f / Mathf.Max(speed, 0.05f)));
            f = loop ? ((f % n) + n) % n : Mathf.Clamp(f, 0, n - 1);
            return FrameAt(b, group, dir, f, false);
        }

        private Frame FrameAt(Body b, int group, int dir, int f, bool ahead)
        {
            int ai = ActionIndex(b, group, dir);
            int key = (ai << 10) | f;
            if (b.Frames.TryGetValue(key, out var fr)) return fr;
            var action = b.Act.Actions[ai];
            fr = Build(b, action.Frames[f]);
            if (fr != null)
            {
                fr.Index = f;
                var ev = b.Act.Events; int id = action.Frames[f].EventId;
                if (id >= 0 && ev != null && id < ev.Length && ev[id] != null && ev[id].EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase)) fr.Event = ev[id];
            }
            b.Frames[key] = fr; // (null kept too: an empty frame is not rebuilt every time)
            if (ahead) BuiltAhead++; else BuiltOnDemand++;
            if (ahead && fr != null && fr.Texture != null) MaterialFor(fr.Texture); // wave 27 (wfix): its material made with it, not at its first draw
            return fr;
        }

        /// <summary>How long one loop of an action lasts (ms), for a death that plays once.</summary>
        public float LengthMs(Body b, int group, int dir)
        {
            if (b == null || !b.Ok) return 0f;
            var a = ActionOf(b, group, dir); return a.Frames.Length * Mathf.Max(a.Delay, 1f) * 25f;
        }

        /// <summary>Returns the first .wav event in the monster's Dead action so fast corpse playback never skips frame-0 death sounds.</summary>
        public string DeathEvent(Body b, int dir)
        {
            if (b == null || !b.Ok) return null;
            var action = ActionOf(b, (int)ActorSprite.MonsterAction.Dead, dir);
            var ev = b.Act.Events;
            if (ev == null) return null;
            for (int i = 0; i < action.Frames.Length; i++)
            {
                int id = action.Frames[i].EventId;
                if (id >= 0 && id < ev.Length && ev[id] != null && ev[id].EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
                    return ev[id];
            }
            return null;
        }

        private Frame Build(Body b, ActFile.Frame frame)
        {
            if (!Layers(b, frame)) return null;
            var gpu = SpriteFrameCache.GetComposite(_kb.ToString(), _layers, out var center);
            if (gpu == null || gpu.Mesh == null) return null;
            return new Frame { Mesh = gpu.Mesh, Texture = gpu.Texture, CenterPx = center, WidthPx = gpu.Width, HeightPx = gpu.Height };
        }

        /// <summary>A frame's layers (_layers) and its composite's key (_kb). False when it draws nothing.</summary>
        private bool Layers(Body b, ActFile.Frame frame)
        {
            _layers.Clear(); _kb.Length = 0; _kb.Append("horde:").Append(b.Path.GetHashCode()).Append(':');
            var spr = b.Spr;
            foreach (var layer in frame.Layers)
            {
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? spr.RgbaFrames.Length : spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available) continue;
                var col = layer.Color;
                if (col.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                var g = SpriteFrameCache.Get(spr, layer.SpriteNumber, layer.SpriteType, null);
                if (g.Width == 0 || g.Height == 0) continue;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = g, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = col, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
                _kb.Append(layer.SpriteNumber).Append(layer.SpriteType == 1 ? 'r' : 'i').Append(layer.Mirror ? 'm' : 'n').Append('@').Append(layer.X).Append(',').Append(layer.Y)
                   .Append('#').Append(((uint)col.r << 24) | ((uint)col.g << 16) | ((uint)col.b << 8) | col.a)
                   .Append('s').Append(Mathf.RoundToInt(layer.Scale.x * 1000f)).Append('/').Append(Mathf.RoundToInt(layer.Scale.y * 1000f)).Append('r').Append(layer.Angle).Append('|');
            }
            return _layers.Count > 0;
        }

        // ---- client wave 18 (h18feel), the design's point 24: a dead monster's body never hides a living one. Its material writes no depth and
        // draws in CorpseQueue, before the living sprites (Loki/Sprite: AlphaTest+10): the living are drawn over it wherever they stand
        public const int CorpseQueue = 2455;
        private readonly Dictionary<Texture, Material> _corpseMaterials = new Dictionary<Texture, Material>();
        public Material CorpseMaterialFor(Texture t)
        {
            if (_corpseMaterials.TryGetValue(t, out var m) && m != null) return m;
            if (_shader == null) _shader = Shader.Find("Loki/Sprite");
            m = new Material(_shader) { mainTexture = t, name = "horde-corpse:" + t.name, enableInstancing = true, renderQueue = CorpseQueue };
            m.SetFloat("_Relief", ActorSprite.DefaultRelief); m.SetFloat("_ZWrite", 0f);
            _corpseMaterials[t] = m;
            return m;
        }

        public Material MaterialFor(Texture t)
        {
            if (_materials.TryGetValue(t, out var m) && m != null) return m;
            if (_shader == null) _shader = Shader.Find("Loki/Sprite");
            m = new Material(_shader) { mainTexture = t, name = "horde:" + t.name, enableInstancing = true };
            m.SetFloat("_Relief", ActorSprite.DefaultRelief);
            _materials[t] = m;
            return m;
        }

        /// <summary>The world matrix of a frame drawn standing at <paramref name="world"/> (feet), facing the camera around the vertical,
        /// <paramref name="cellMetres"/> metres per map cell, at <paramref name="scale"/> × the sprite's own size (ActorSprite's quad placement).</summary>
        public static Matrix4x4 Matrix(Frame f, Vector3 world, Vector3 camPos, float cellMetres, float scale)
        {
            var to = world - camPos; to.y = 0;
            var rot = to.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(to.normalized, Vector3.up) : Quaternion.identity;
            float sc = scale / ActorSprite.PixelsPerCell;
            return Matrix4x4.TRS(world, rot, Vector3.one * cellMetres) * Matrix4x4.TRS(new Vector3(f.CenterPx.x * sc, -f.CenterPx.y * sc, 0f), Quaternion.identity, Vector3.one * sc);
        }

        /// <summary>As <see cref="Matrix"/>, with a lean (degrees, about the axis towards the camera; a knockback) and a squash (x, y) of the sprite.</summary>
        public static Matrix4x4 Matrix(Frame f, Vector3 world, Vector3 camPos, float cellMetres, float scale, float leanDeg, Vector2 squash)
        {
            var to = world - camPos; to.y = 0;
            var rot = to.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(to.normalized, Vector3.up) : Quaternion.identity;
            float sc = scale / ActorSprite.PixelsPerCell;
            return Matrix4x4.TRS(world, rot * Quaternion.Euler(0f, 0f, leanDeg), Vector3.one * cellMetres)
                 * Matrix4x4.TRS(new Vector3(f.CenterPx.x * sc * squash.x, -f.CenterPx.y * sc * squash.y, 0f), Quaternion.identity, new Vector3(sc * squash.x, sc * squash.y, sc));
        }

        // ------------------------------------------------------------------ the pre-build (client wave 9, h9feel)
        /// <summary>The action groups the arena draws of a monster (HordeWorldDraw: idle, walk, attack while winding, dead).</summary>
        public static readonly int[] MonsterGroups = { (int)ActorSprite.MonsterAction.Idle, (int)ActorSprite.MonsterAction.Walk, (int)ActorSprite.MonsterAction.Attack, (int)ActorSprite.MonsterAction.Dead };
        /// <summary>The action groups the arena draws of a run character (HordePlayers: idle, walk, dead).</summary>
        public static readonly int[] PlayerGroups = { (int)ActorSprite.PlayerAction.Idle, (int)ActorSprite.PlayerAction.Walk, (int)ActorSprite.PlayerAction.Dead };

        private struct Work { public Body B; public Unit U; public int Group, Dir; public string What; }
        private readonly List<Work> _work = new List<Work>();
        private int _workAt, _workFrame;
        // client wave 18 fix (h18stutter): the frames handed to SpriteFrameCache's background threads, ahead of the one being built
        // (work index, frame, the composite's key; a null key = nothing to wait for), and the next frame to hand over
        private readonly Queue<(int At, int Frame, string Key)> _prep = new Queue<(int, int, string)>();
        private int _prepAt, _prepFrame;
        /// <summary>Frames in the background threads' hands at most (Loki's own number: their arrays wait in memory until built).</summary>
        public const int PrepareAhead = 96;
        private readonly HashSet<string> _queued = new HashSet<string>();
        public int WarmQueued => _work.Count - _workAt;
        public int WarmDone { get; private set; }
        /// <summary>Milliseconds spent building ahead, and the longest single slice (the pre-build's own cost per frame).</summary>
        public float WarmMs { get; private set; }
        public float WarmSliceMaxMs { get; private set; }
        /// <summary>The single most expensive frame built ahead (ms) and what it was: the floor of a slice (one composite is never split).</summary>
        public float WarmOneMaxMs { get; private set; }
        public string WarmOneMaxWhat { get; private set; } = "";
        public bool WarmBusy => _workAt < _work.Count;

        /// <summary>Queues every frame of a monster's drawn action groups (all 8 directions). Once per kind.</summary>
        public void QueueKind(int mobId)
        {
            if (!_queued.Add("m" + mobId)) return;
            var b = Mob(mobId); if (!b.Ok) return;
            foreach (int g in MonsterGroups) for (int d = 0; d < 8; d++) _work.Add(new Work { B = b, Group = g, Dir = d, What = "mob " + mobId });
        }

        /// <summary>Queues an item's one frame (pickups are drawn from action 0, direction 0, frame 0). Once per item.</summary>
        public void QueueItem(int itemId)
        {
            if (!_queued.Add("i" + itemId)) return;
            var b = Item(itemId); if (!b.Ok) return;
            _work.Add(new Work { B = b, Group = 0, Dir = 0, What = "item " + itemId });
        }

        /// <summary>True while no unit was queued for this job + look key (the caller makes the hidden unit only then).</summary>
        public bool NeedsUnit(string key) => !_queued.Contains("u" + key);

        /// <summary>Queues a run character's frames (its ActorSprite's layers: body, hair, hair colour) for the drawn groups, all 8 directions.
        /// The unit should be inactive (not drawn); the key makes a job + look queued once.</summary>
        public void QueueUnit(Unit u, string key)
        {
            if (u == null || u.Sprite == null || !_queued.Add("u" + key)) return;
            foreach (int g in PlayerGroups) for (int d = 0; d < 8; d++) _work.Add(new Work { U = u, Group = g, Dir = d, What = "job " + key });
        }

        private static int FramesOf(in Work w)
        {
            int n = w.U != null ? (w.U.Sprite != null ? w.U.Sprite.WarmFrameCount(w.Group, w.Dir) : 0) : w.B.Ok ? ActionOf(w.B, w.Group, w.Dir).Frames.Length : 0;
            return w.What.StartsWith("item") ? Mathf.Min(n, 1) : n;
        }

        /// <summary>Hands a monster's / item's frame to the background threads. Its composite's key, or null when there is nothing to wait for.</summary>
        private string PrepareFrame(Body b, int group, int dir, int f)
        {
            int ai = ActionIndex(b, group, dir);
            if (b.Frames.ContainsKey((ai << 10) | f) || !Layers(b, b.Act.Actions[ai].Frames[f])) return null;
            string key = _kb.ToString();
            return SpriteFrameCache.Prepare(key, _layers) ? key : null;
        }

        /// <summary>Builds queued frames for at most <paramref name="budgetMs"/> milliseconds. False when nothing is left to build.</summary>
        public bool PrewarmSlice(float budgetMs)
        {
            if (_workAt >= _work.Count) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // client wave 18 fix (h18stutter): the next frames' pixel and relief work (the bulk of a frame's cost) goes to background
            // threads first, PrepareAhead frames at most; the loop below finishes, in order, the frames whose work is done, and ends the
            // slice at the first one still being worked on (a run character's frames, ActorSprite's own layers, are built here as before)
            if (_prepAt < _workAt || (_prepAt == _workAt && _prepFrame < _workFrame)) { _prepAt = _workAt; _prepFrame = _workFrame; }
            while (_prep.Count < PrepareAhead && _prepAt < _work.Count && sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                var w = _work[_prepAt];
                if (w.U != null || _prepFrame >= FramesOf(w)) { _prepAt++; _prepFrame = 0; continue; }
                _prep.Enqueue((_prepAt, _prepFrame, PrepareFrame(w.B, w.Group, w.Dir, _prepFrame)));
                _prepFrame++;
            }
            while (_workAt < _work.Count && sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                var w = _work[_workAt];
                int n = FramesOf(w);
                if (_workFrame >= n) { _workAt++; _workFrame = 0; WarmDone++; continue; }
                while (_prep.Count > 0 && (_prep.Peek().At < _workAt || (_prep.Peek().At == _workAt && _prep.Peek().Frame < _workFrame))) _prep.Dequeue();
                if (_prep.Count > 0 && _prep.Peek().At == _workAt && _prep.Peek().Frame == _workFrame)
                {
                    if (_prep.Peek().Key != null && SpriteFrameCache.PreparePending(_prep.Peek().Key)) break;
                    _prep.Dequeue();
                }
                double t0 = sw.Elapsed.TotalMilliseconds;
                if (w.U != null) w.U.Sprite.WarmFrame(w.Group, w.Dir, _workFrame);
                else FrameAt(w.B, w.Group, w.Dir, _workFrame, true);
                float one = (float)(sw.Elapsed.TotalMilliseconds - t0);
                if (one > WarmOneMaxMs) { WarmOneMaxMs = one; WarmOneMaxWhat = $"{w.What} group {w.Group} dir {w.Dir} frame {_workFrame}"; }
                _workFrame++;
            }
            float ms = (float)sw.Elapsed.TotalMilliseconds; WarmMs += ms; if (ms > WarmSliceMaxMs) WarmSliceMaxMs = ms;
            return _workAt < _work.Count;
        }

        /// <summary>Forgets the queue (the arena is gone); what was built stays cached.</summary>
        public void ClearQueue() { _work.Clear(); _workAt = 0; _workFrame = 0; _queued.Clear(); _prep.Clear(); _prepAt = 0; _prepFrame = 0; }
    }
}
