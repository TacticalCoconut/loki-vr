using System;
using System.Collections.Generic;
using System.IO;
using Loki.Formats;
using Loki.Net;
using Loki.Sprites;
using Loki.World;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// Emotes (research/qol_packets.md §2): 0x00c0 ZC_EMOTION gid.L type.B → bubble over the unit; 0x00bf CZ_REQ_EMOTION type.B
    /// to send one. rAthena e985006 (clif_parse_Emotion, clif.cpp:11625-11670) takes the types 0 … ET_MAX-1 = 87 (clif.hpp:263-354),
    /// refuses ET_CHAT_PROHIBIT (34, the muted characters' emote) and everything before Basic Skill level 2 (or Summoner's Basic
    /// Skill 1), both with a skill-fail 0x0110 for NV_BASIC, and throttles by whole seconds: a request is refused (and restarts the
    /// wait) while <c>emotionlasttime + 1 &gt;= time(nullptr)</c>, so two emotes pass only when the server's clock has passed two
    /// second boundaries between them — a gap of 2 s or more always does. That leaves 87 emotes a player can send: every one is in
    /// the palette (<see cref="All"/>). data/emotions.tsv (from the client's emotionlist.lub) maps the id to the action of
    /// data\sprite\이팩트\emotion.act (1000 = no sprite) and gives the chat commands ("/!", "/lv" …) the official client accepts.
    /// </summary>
    public sealed class Emotes
    {
        public sealed class Entry
        {
            public int Id, Act; public string Command = ""; public string[] Aliases = new string[0];
            /// <summary>The command, or for the emotes emotionlist.lub gives no command the name rAthena's enum gives the id (flags, dice
            /// faces, the yut set: <see cref="Unnamed"/>), else "#id".</summary>
            public string Label => Command.Length > 0 ? Command : Unnamed.TryGetValue(Id, out var n) ? Lang.T(n) : "#" + Id; // w4lang: only shown (the wrist menu's palette)
        }
        /// <summary>The emote ids without a chat command, named after rAthena's e_emotion_type (clif.hpp:277, 299, 312-315, 322-331, 345-351):
        /// ET_FLAG, ET_INDONESIA_FLAG, ET_PH_FLAG, ET_MY_FLAG, ET_SI_FLAG, ET_BR_FLAG, ET_DICE1-6, ET_INDIA_FLAG, ET_FLAG8, ET_FLAG9, ET_YUT1-7.</summary>
        private static readonly Dictionary<int, string> Unnamed = new Dictionary<int, string>
        {
            { 13, Lang.N("~emote~Flag") }, { 35, Lang.N("Indonesia flag") }, { 48, Lang.N("PH flag") }, { 49, Lang.N("MY flag") }, { 50, Lang.N("SI flag") }, { 51, Lang.N("BR flag") },
            { 58, Lang.N("Dice 1") }, { 59, Lang.N("Dice 2") }, { 60, Lang.N("Dice 3") }, { 61, Lang.N("Dice 4") }, { 62, Lang.N("Dice 5") }, { 63, Lang.N("Dice 6") },
            { 64, Lang.N("India flag") }, { 66, Lang.N("Flag 8") }, { 67, Lang.N("Flag 9") },
            { 81, Lang.N("Yut 1") }, { 82, Lang.N("Yut 2") }, { 83, Lang.N("Yut 3") }, { 84, Lang.N("Yut 4") }, { 85, Lang.N("Yut 5") }, { 86, Lang.N("Yut 6") }, { 87, Lang.N("Yut 7") },
        };
        private static Dictionary<int, Entry> _table;
        private static List<Entry> _all;
        private readonly RoSession _s; private readonly EntityManager _ents; private readonly RoAssets _assets; private readonly Action<string> _toast;
        private SprFile _spr; private ActFile _act; private bool _loaded; private float _lastSent = -10f;
        /// <summary>ET_CHAT_PROHIBIT (clif.hpp:298): refused by the server for everyone (clif.cpp:11639-11642).</summary>
        public const int ChatProhibit = 34;
        /// <summary>ET_MAX (clif.hpp:353): types at or above it are dropped without an answer (clif.cpp:11632-11634).</summary>
        public const int Max = 88;
        /// <summary>The client's spacing between two requests: the server's whole-second rule (see the class notes) plus a margin for
        /// the network. A request sent sooner would be refused and would restart the server's wait.</summary>
        public const float MinGap = 2.1f;

        public Emotes(RoSession s, EntityManager ents, RoAssets assets, Action<string> toast) { _s = s; _ents = ents; _assets = assets; _toast = toast; }

        private static void LoadTable()
        {
            if (_table != null) return;
            _table = new Dictionary<int, Entry>(); _all = new List<Entry>();
            try
            {
                foreach (var line in File.ReadLines(Loki.Data.ClientData.File("emotions.tsv")))
                {
                    var c = line.Split('\t');
                    if (c.Length < 2 || !int.TryParse(c[0], out int id) || !int.TryParse(c[1], out int act)) continue;
                    var e = new Entry { Id = id, Act = act, Command = c.Length > 2 ? c[2].Trim() : "" };
                    if (c.Length > 3) e.Aliases = c[3].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    _table[id] = e;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[Emotes] table: " + ex.Message); }
            for (int id = 0; id < Max; id++)
                if (id != ChatProhibit) _all.Add(_table.TryGetValue(id, out var e) ? e : new Entry { Id = id, Act = -1 });
        }

        /// <summary>Every emote a player can send on this server, in id order (87: 0 … 87 without 34).</summary>
        public static IReadOnlyList<Entry> All { get { LoadTable(); return _all; } }

        /// <summary>ACT action for a packet emote id (-1 when the client has no sprite for it).</summary>
        public static int ActionOf(int id) { LoadTable(); return _table.TryGetValue(id, out var e) && e.Act >= 0 && e.Act < 1000 ? e.Act : -1; }

        /// <summary>The emote a chat line names ("/lv", "/heart", "/!" … — emotionlist.lub's commands and aliases), or -1. The whole
        /// line must be the command, so "/w name : text" and the other slash commands are never taken for an emote.</summary>
        public static int ByCommand(string line)
        {
            LoadTable();
            if (string.IsNullOrEmpty(line) || line[0] != '/') return -1;
            string t = line.Trim();
            foreach (var e in _all)
            {
                if (e.Command.Length > 0 && string.Equals(e.Command, t, StringComparison.OrdinalIgnoreCase)) return e.Id;
                foreach (var a in e.Aliases) if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase)) return e.Id;
            }
            return -1;
        }

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x00c0 || n < 7) return false;
            var r = new PacketReader(b, o, n); r.Skip(2); uint gid = r.U32(); int type = r.U8();
            ShowOn(gid, type);
            return true;
        }
        /// <summary>Draws emotion `type` (ET_*) over a unit, as ZC_EMOTION does — also used for a pet's 0x01aa emotions.</summary>
        public void ShowOn(uint gid, int type)
        {
            var u = _ents.Get(gid);
            int act = ActionOf(type);
            Debug.Log($"[Emotes] {gid} emote {type} → action {act}{(u == null ? " (unit not here)" : "")}");
            if (u == null || act < 0) return;
            EnsureSprite();
            EmoteSprite.Show(u.transform, _spr, _act, act);
            LastShown = (gid, type);
        }
        /// <summary>For the smoke test: the last (unit, emote) drawn.</summary>
        public (uint gid, int type) LastShown = (0, -1);

        private void EnsureSprite()
        {
            if (_loaded) return;
            _loaded = true; _assets.Sprite($"data\\sprite\\{RoNames.Effect}\\emotion", out _spr, out _act);
            if (_act != null) Debug.Log($"[Emotes] emotion.act: {_act.Actions.Length} actions");
        }

        /// <summary>Seconds until the next emote may be sent (0 = now).</summary>
        public float Wait => Mathf.Max(0f, _lastSent + MinGap - Time.time);

        /// <summary>Sends an emote, honouring the server's rules client-side so the request is never refused.</summary>
        public bool Send(int id)
        {
            if (id < 0 || id >= Max || id == ChatProhibit) { _toast?.Invoke(Lang.T("That emote can't be sent.")); return false; }
            if (Wait > 0f) { _toast?.Invoke(Lang.F("One emote every 2 seconds — {0:0.0} s left.", Wait)); return false; }
            _lastSent = Time.time;
            _s.Emotion(id);
            Debug.Log($"[Emotes] sent {id}");
            return true;
        }

        // ---- palette icons: one frame of each emote's action, composited like the bubble (SpriteFrameCache) into a flat texture
        private readonly Dictionary<int, Texture2D> _icons = new Dictionary<int, Texture2D>();
        private readonly List<SpriteFrameCache.CompositeLayer> _layers = new List<SpriteFrameCache.CompositeLayer>();

        /// <summary>The palette picture of emote `id`: the frame of its action with the most sprite layers, then the most drawn pixels,
        /// the last of equals — an emote's bubble grows in empty and its content is drawn over it as further layers, so that frame shows
        /// what it says once it has settled. Null when the client has no sprite for it.</summary>
        public Texture2D IconOf(int id)
        {
            if (_icons.TryGetValue(id, out var t)) return t;
            t = null;
            int action = ActionOf(id);
            EnsureSprite();
            if (action >= 0 && _act != null && _spr != null && action < _act.Actions.Length)
            {
                var frames = _act.Actions[action].Frames;
                Color32[] px = null; int w = 0, h = 0, bestLayers = 0, bestCount = 0;
                for (int f = 0; f < frames.Length; f++)
                {
                    Collect(frames[f]);
                    if (_layers.Count < bestLayers || !SpriteFrameCache.CompositeBounds(_layers, out int fx, out int fy, out int fw, out int fh)) continue;
                    var fp = SpriteFrameCache.ComposePixels(_layers, fx, fy, fw, fh); // top-down rows
                    int count = 0; foreach (var c in fp) if (c.a > 32) count++;
                    if (_layers.Count > bestLayers || count >= bestCount) { bestLayers = _layers.Count; bestCount = count; px = fp; w = fw; h = fh; }
                }
                if (px != null && bestCount > 0)
                {
                    var flipped = new Color32[px.Length];
                    for (int y = 0; y < h; y++) Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
                    t = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "emote" + id };
                    t.SetPixels32(flipped); t.Apply(false, true);
                }
            }
            _icons[id] = t;
            return t;
        }

        private void Collect(ActFile.Frame frame)
        {
            _layers.Clear();
            foreach (var layer in frame.Layers)
            {
                if (layer.SpriteNumber < 0) continue;
                int available = layer.SpriteType == 1 ? _spr.RgbaFrames.Length : _spr.IndexedFrames.Length;
                if (layer.SpriteNumber >= available || layer.Color.a == 0 || layer.Scale.x == 0f || layer.Scale.y == 0f) continue;
                var gpu = SpriteFrameCache.Get(_spr, layer.SpriteNumber, layer.SpriteType);
                if (gpu.Width == 0 || gpu.Height == 0) continue;
                _layers.Add(new SpriteFrameCache.CompositeLayer { Frame = gpu, X = layer.X, Y = layer.Y, Mirror = layer.Mirror, Color = layer.Color, ScaleX = layer.Scale.x, ScaleY = layer.Scale.y, Angle = layer.Angle });
            }
        }
    }
}
