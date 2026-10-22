using System.Diagnostics;
using LokiHost.Core;

// Loki Host's map-cache code from the command line (client wave 7, w7host). Nothing here is needed to host: Loki Host does
// it all at Start. It exists to check the C# port against the Python tools and to look at a cache.
//   LokiMapTool build  <Ragnarok Online folder> <rAthena folder (holds db/)> <out map_cache.dat>
//        = python tools/mapcache/build_import_cache.py <folder>/data.grf <rAthena folder> <out>
//   LokiMapTool check  <rAthena folder> <map_cache.dat> [rAthena source folder]
//        = python tools/mapcache/check_points.py <rAthena folder> <map_cache.dat> [source]      (the same lines; exit code 3 when not clean)
//   LokiMapTool digest <map_cache.dat>...      what the server reads from each: = python tools/mapcache/cache_digest.py
//   LokiMapTool cells  <Ragnarok Online folder> <rAthena folder>   = python tools/mapcache/cells_digest.py <folder>/data.grf <rAthena folder>
//   LokiMapTool stamp  <Ragnarok Online folder>   the install's stamp text, data stamp and patch label
//   LokiMapTool watch  <host folder> <Ragnarok Online folder> [rAthena source folder]   what Loki Host does at Start (it WRITES the
//        host folder's db/import/map_cache.dat when the maps changed, the old file kept beside it)
//   LokiMapTool tutorial <host folder> [map,x,y]   what Loki Host does at Start for the controls tutorial's private map (client wave 21,
//        w21map; TutorialInstance.Write: it WRITES the host folder's db/import/instance_db.yml block, npc/custom/loki_tutorial.txt and
//        its line in npc/scripts_custom.conf); without the third argument: Loki Host's default map; "" = off
// Output is UTF-8 (the NPC files' Latin-1 characters as their Unicode code points, as Python prints them).

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
static int Usage() { Console.Error.WriteLine("usage: LokiMapTool build|check|digest|cells|stamp|watch|tutorial … (see Program.cs)"); return 2; }
if (args.Length < 2) return Usage();
var sw = Stopwatch.StartNew();
try
{
    switch (args[0])
    {
        case "build":
        {
            if (args.Length != 4) return Usage();
            using var install = HostInstall.Open(args[1]);
            var r = MapCacheBuilder.Build(args[2], install.Read);
            File.WriteAllBytes(args[3], r.File);
            foreach (var l in r.Report) Console.WriteLine(l);
            foreach (var l in MapCacheBuilder.ServerWouldRefuse(r.Maps)) Console.WriteLine("WARNING: " + l);
            Console.Error.WriteLine($"({sw.Elapsed.TotalSeconds:0.0} s on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {r.File.Length} bytes, sha256 {MapCache.Sha256(r.File)}, content {MapCache.ContentDigest(r.File, args[3])})");
            return 0;
        }
        case "check":
        {
            if (args.Length is not (3 or 4)) return Usage();
            var r = PointsCheck.Run(args[1], File.ReadAllBytes(args[2]), args.Length == 4 ? args[3] : null);
            foreach (var l in r.Lines) Console.WriteLine(l);
            Console.Error.WriteLine($"({sw.Elapsed.TotalSeconds:0.0} s; {(r.Clean ? "clean" : $"NOT clean: {r.Bad} lost, {r.Lost} cut off")})");
            return r.Clean ? 0 : 3;
        }
        case "digest":
            foreach (var f in args.Skip(1))
            {
                var b = File.ReadAllBytes(f);
                Console.WriteLine($"{f}: {b.Length} bytes, {MapCache.Read(b, f).Count} maps, sha256 {MapCache.Sha256(b)}, content {MapCache.ContentDigest(b, f)}");
            }
            return 0;
        case "cells":
        {
            // = python tools/mapcache/cells_digest.py <folder>/data.grf <rAthena folder>: every map the caches know, with the
            // cells the install gives for it
            if (args.Length != 3) return Usage();
            using var install = HostInstall.Open(args[1]);
            var names = MapCacheBuilder.CacheOrder(args[2]).Where(File.Exists).SelectMany(p => MapCache.Read(p).Keys).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal);
            foreach (var name in names)
            {
                var g = MapCacheBuilder.GrfCells(install.Read, name);
                Console.WriteLine(g == null ? $"{name} missing" : $"{name} {g.Xs} {g.Ys} {MapCache.Sha256(g.Cells)}");
            }
            return 0;
        }
        case "stamp":
        {
            if (args.Length != 2) return Usage();
            Console.Write(HostInstall.StampText(args[1]));
            using var install = HostInstall.Open(args[1]);
            var st = install.DataStamp();
            Console.WriteLine($"data stamp {st.Stamp} ({st.Maps} maps, {st.Cells} cells, {st.Unreadable} unreadable), label \"{HostInstall.PatchLabel(args[1])}\"");
            Console.Error.WriteLine($"({sw.Elapsed.TotalSeconds:0.0} s)");
            return 0;
        }
        case "watch":
        {
            if (args.Length is not (3 or 4)) return Usage();
            var o = MapDataWatch.Check(args[1], args[2], Console.WriteLine, new MapDataWatch.Options { SourceDir = args.Length == 4 ? args[3] : null });
            Console.WriteLine(o.Summary);
            Console.Error.WriteLine($"({sw.Elapsed.TotalSeconds:0.0} s; {o.Kind})");
            return o.Kind == MapDataWatch.Kind.Failed ? 3 : 0;
        }
        case "tutorial":
        {
            if (args.Length is not (2 or 3)) return Usage();
            string setting = args.Length == 3 ? args[2] : TutorialInstance.DefaultMap;
            var said = TutorialInstance.Write(args[1], setting);
            foreach (var l in said) Console.WriteLine(l);
            if (said.Count == 0) Console.WriteLine(string.IsNullOrWhiteSpace(setting) ? "No private tutorial map (the setting is empty)." : "Nothing written: the folder has no db/instance_db.yml or no npc/scripts_custom.conf.");
            return 0;
        }
        default:
            return Usage();
    }
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"{args[0]} failed: {e.Message}");
    return 1;
}
