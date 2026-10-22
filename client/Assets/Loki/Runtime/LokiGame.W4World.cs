using System.Collections.Generic;
using System.Linq;
using Loki.Game;
using Loki.Net;
using Loki.UI;
using Loki.World;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 4, w4world — the world and the view (brief design notes, progress design notes):
    ///   • the weather rAthena's map flags send (0x01f3 with EF_SNOW, EF_SAKURA, EF_MAPLE, EF_CLOUD…, EF_POKJUK; World/Weather.cs), taken
    ///     from the packet stream before the effect player (Combat.Handle) sees them, and 0x0284 num ≤ 0 stopping them;
    ///   • the quest icons over NPCs and their minimap marks (0x0446 via QuestLog.Marker → World/QuestIcons.cs);
    ///   • navigation from the client's own data with the Guide window (Game/Navigation.cs, UI/GuideWindow.cs) and /navi;
    ///   • screenshots of the headset view (World/Screenshots.cs) and the desktop window's spectator view (World/SpectatorView.cs), both on
    ///     the wrist strip (UI/WristMenu.W4World.cs);
    ///   • the per-map sky (World/SkyController.cs + World/MapSky.cs), the water (World/MapLoader.cs) and RSM2 texture animation
    ///     (World/RsmMeshBuilder.cs) need no hook here.
    /// Every LokiGame member of this wave starts with W4World. Smoke: LokiGame.W4WorldSmoke.cs (-lokiSmokeOnly w4world…).
    /// </summary>
    public sealed partial class LokiGame
    {
        public Weather W4WorldWeather { get; private set; }
        public QuestIcons W4WorldQuestIcons { get; private set; }
        public GuideWindow W4WorldGuide { get; private set; }
        public SpectatorView W4WorldSpectator { get; private set; }
        /// <summary>The last screenshot written (smoke).</summary>
        public string W4WorldLastShot { get; private set; }
        /// <summary>Smoke: the weather ids taken from the packet stream, in order (id, stop).</summary>
        private readonly List<(int ef, bool stop)> _w4worldWeatherSeen = new List<(int, bool)>();

        private void BeginW4World(LokiBootstrap boot)
        {
            // weather: under the map loader (it follows the diorama), around our character
            W4WorldWeather = boot.Map.gameObject.AddComponent<Weather>();
            W4WorldWeather.Init(boot.Assets, () => Entities?.Self != null ? Entities.Self.transform.localPosition : W25LoginCloudCentre, () => boot.Map.Current?.Gat); // (client wave 25, w25misc, item 1: before the world, the login backdrop's middle: LokiGame.W25Misc.cs)
            if (Sky != null) Sky.Weather = W4WorldWeather;
            // quest icons + their minimap marks, after wave 2's viewpoint marks
            W4WorldQuestIcons = new QuestIcons(Entities, boot.Assets);
            if (MiniMap != null)
            {
                var marks = MiniMap.MarkCells;
                MiniMap.MarkCells = () => (marks != null ? marks() : Enumerable.Empty<(Vector2, Color)>()).Concat(W4WorldQuestIcons.Marks());
            }
            // the Guide window
            W4WorldGuide = GuideWindow.Create(boot.Rig.transform, boot.Rig.Head, Nav, () => _boot.Map.Current?.Name);
            W4WorldGuide.RequestKeyboard = f => { Keyboard.Attach(f); Keyboard.Show(); };
            W4WorldGuide.WalkTo = c => W3WalkStart(c, "guide");
            Nav.WindowRequested += () => W4WorldGuide.Open();
            Nav.Near = (pt, portal) =>
            {
                Sprites.Unit best = null; float bd = portal ? 5f : 4f;
                foreach (var u in Entities.Units.Values)
                {
                    if (u == null || u.IsSelf) continue;
                    if (portal ? u.Kind != Sprites.Unit.UnitKind.Warp : u.Kind != Sprites.Unit.UnitKind.Npc) continue;
                    float d = Vector2.Distance(u.Cell, pt); if (d <= bd) { bd = d; best = u; }
                }
                return best != null ? (Vector2?)best.Cell : null;
            };
            // the desktop window's spectator view
            W4WorldSpectator = boot.Rig.gameObject.AddComponent<SpectatorView>();
            W4WorldSpectator.Init(boot.Rig.Head, () => Entities?.Self != null ? Entities.Self.transform : null, () => Entities?.Self != null ? Entities.Self.Sprite.Direction : 0, () => boot.Map.transform);
            W4WorldSpectator.Changed += _ => Hud?.Wrist?.RefreshW4World();
            // the wrist strip
            Hud.Wrist.TakeScreenshot = W4WorldScreenshot;
            Hud.Wrist.CycleDesktopView = () => W4WorldSpectator.Cycle();
            Hud.Wrist.DesktopViewLabel = () => SpectatorView.Label(W4WorldSpectator.Current);
            Hud.Wrist.OpenGuide = () => W4WorldGuide.Toggle();
            Hud.Wrist.RefreshW4World();
        }

        /// <summary>0x01f3 ZC_NOTIFY_EFFECT2 aid.L effect.L / 0x0284 ZC_NOTIFY_EFFECT3 aid.L effect.L num.L carrying a weather id: the weather
        /// draws it (the owner does not matter: roBrowser ignores it, and clif_weather_check sends it on ourselves). Anything else passes on.</summary>
        private bool HandleW4World(ushort id, byte[] b, int o, int n)
        {
            if ((id != 0x01f3 || n < 10) && (id != 0x0284 || n < 14)) return false;
            var r = new PacketReader(b, o, n); r.Skip(2); r.U32(); int ef = r.S32(); int num = id == 0x0284 ? r.S32() : 1;
            if (!Weather.IsWeather(ef) || W4WorldWeather == null) return false;
            bool stop = id == 0x0284 && num <= 0;
            _w4worldWeatherSeen.Add((ef, stop));
            Debug.Log($"[W4World] 0x{id:x4} weather {Weather.Name(ef)} ({ef}){(stop ? " stop" : "")}");
            if (stop) W4WorldWeather.Stop(ef); else W4WorldWeather.Play(ef);
            return true;
        }

        /// <summary>QuestLog.Marker (0x0446): the icon over the NPC and the minimap mark. QuestLog does not keep x / y: they are read again here.</summary>
        private void W4WorldQuestMarker(uint npc, int effect, int color)
        {
            var u = Entities.Get(npc);
            var cell = _w4worldLastQuestCell.TryGetValue(npc, out var c) ? c : (u != null ? Vector2Int.RoundToInt(u.Cell) : Vector2Int.zero);
            W4WorldQuestIcons?.Set(npc, cell.x, cell.y, effect, color);
        }
        private readonly Dictionary<uint, Vector2Int> _w4worldLastQuestCell = new Dictionary<uint, Vector2Int>();
        /// <summary>Peeks at 0x0446's x / y (QuestLog's Marker event does not carry them) — called before the handlers consume it.</summary>
        private void W4WorldPeek(ushort id, byte[] b, int o, int n)
        {
            if (id != 0x0446 || n < 14) return;
            var r = new PacketReader(b, o, n); r.Skip(2); uint npc = r.U32(); int x = r.U16(), y = r.U16();
            _w4worldLastQuestCell[npc] = new Vector2Int(x, y);
        }

        private void OnMapChangeW4World(string map)
        {
            W4WorldQuestIcons?.Clear(); _w4worldLastQuestCell.Clear();
            W4WorldGuide?.Refresh();
        }

        /// <summary>/navi (the Guide window), /navi &lt;map&gt; [x y] (roBrowser Controls/ProcessCommand.js:798-816: "/navi map x y").</summary>
        private bool TryW4WorldCommand(string line)
        {
            if (line == null) return false;
            string t = line.Trim();
            if (!(t.Equals("/navi", System.StringComparison.OrdinalIgnoreCase) || t.StartsWith("/navi ", System.StringComparison.OrdinalIgnoreCase) || t.Equals("/navigation", System.StringComparison.OrdinalIgnoreCase))) return false;
            var parts = t.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) { W4WorldGuide?.Toggle(); return true; }
            int x = 0, y = 0;
            bool xy = parts.Length >= 4 && int.TryParse(parts[2], out x) && int.TryParse(parts[3], out y);
            Debug.Log($"[W4World] /navi {parts[1]} {(xy ? $"{x} {y}" : "")}");
            Nav.Go(xy ? 0 : 1, parts[1], x, y, 0, W4WorldGuide != null ? W4WorldGuide.Flag : 101, true, true);
            return true;
        }

        private void W4WorldScreenshot()
        {
            var hide = new List<GameObject>(); if (Hud?.Wrist != null) hide.Add(Hud.Wrist.gameObject);
            var path = Screenshots.Take(_boot.Rig.Head, hide, out string err);
            W4WorldLastShot = path;
            Loki.Audio.LokiAudio.Instance?.PlayUi();
            if (path != null) { Hud?.Wrist?.Toast(Lang.F("Saved {0}", System.IO.Path.GetFileName(path))); Chat(Lang.F("Screenshot saved: {0}", path), "9ad1ff"); }
            else Chat(Lang.F("The screenshot failed: {0}", err), "ff7a6a");
        }
    }
}
