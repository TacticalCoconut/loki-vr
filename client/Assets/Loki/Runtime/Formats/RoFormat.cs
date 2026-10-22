using System;
using System.IO;
using Loki.IO;

namespace Loki.Formats
{
    /// <summary>
    /// Client wave 7 (w7data): one way for every reader of Gravity's files to say "this file is not something I can read", so a
    /// caller can give a safe result (a map that keeps the old scene, a sprite's placeholder, a model left out) instead of
    /// breaking. Two cases, told apart by <see cref="RoFormatException.UnknownVersion"/>:
    ///  • a version newer than the newest one the reader's source describes. It is NOT read by the newest known layout: a format
    ///    without a source is not guessed (the design's rule); it is skipped and named.
    ///  • a damaged file: a wrong signature, counts that cannot fit in the file, data that ends early.
    ///
    /// The newest version each reader knows, and where that comes from (the 2026-10-04 survey of the LATAM data.grf, patch 1486 —
    /// 209,346 files of these formats — found none newer):
    ///   RSW 2.7   Formats/RswFile.cs; roBrowserLegacy Loaders/World.js:167, 195 (the install holds 1.9, 2.1 – 2.7)
    ///   GND 1.9   Formats/GndFile.cs; roBrowserLegacy Loaders/Ground.js:56-67 (the install holds 1.7, 1.8, 1.9)
    ///   GAT 1.3   Formats/GatFile.cs; roBrowserLegacy Loaders/Altitude.js reads every version alike; the install holds 1.2 and
    ///             1.3, both of this layout
    ///   RSM 2.3   Formats/RsmFile.cs (korangar ragnarok-formats/model.rs, roBrowserLegacy Loaders/Model.js:189-190)
    ///   SPR 2.1   Formats/SprFile.cs; roBrowserLegacy Loaders/Sprite.js:61-77 (the install holds 2.0 and 2.1)
    ///   ACT 2.5   Formats/ActFile.cs; roBrowserLegacy Loaders/Action.js:128-148 (the install holds 2.0, 2.1, 2.3, 2.4, 2.5)
    ///   STR 0x94  Fx/StrFile.cs; roBrowserLegacy Loaders/Str.js:117 (every STR of the install)
    ///   GRF 0x200 / 0x300  IO/GrfArchive.cs; roBrowserLegacy Loaders/GameFile.js:139-144
    ///   lub: Lua 5.1 bytecode  Data/Lub51.cs (the header check); the install holds one Lua 5.0 file, which is refused there
    /// Pure C# (no UnityEngine): Formats/GatFile.cs uses it and may be shared outside the game.
    /// </summary>
    public sealed class RoFormatException : IOException
    {
        /// <summary>"RSW", "GND", "GAT", "RSM", "SPR", "ACT", "STR".</summary>
        public readonly string Format;
        /// <summary>True: a version this reader does not know (skipped, not guessed). False: a damaged file.</summary>
        public readonly bool UnknownVersion;
        public RoFormatException(string format, string problem, bool unknownVersion, Exception inner = null)
            : base(format + ": " + problem, inner) { Format = format; UnknownVersion = unknownVersion; }
    }

    public static class RoFormat
    {
        /// <summary>Throws when <paramref name="v"/> is newer than the newest version the reader knows.</summary>
        public static void CheckVersion(string format, RoVersion v, int maxMajor, int maxMinor)
        {
            if (v.Major > maxMajor || (v.Major == maxMajor && v.Minor > maxMinor))
                throw new RoFormatException(format, $"version {v} is newer than the newest Loki knows ({maxMajor}.{maxMinor}): not read", true);
        }

        public static void Signature(string format, string got, params string[] wanted)
        {
            foreach (var w in wanted) if (got == w) return;
            throw new RoFormatException(format, "not a " + format + " file (bad signature)", false);
        }

        /// <summary>A count read from the file: it must be 0 or more and its elements must fit in what is left of the file
        /// (a damaged count would otherwise ask for gigabytes or loop for minutes).</summary>
        public static int Count(string format, RoReader r, long n, long minBytesEach, string what)
        {
            long left = r.Length - r.Position;
            if (n < 0 || n > int.MaxValue || n * minBytesEach > left)
                throw new RoFormatException(format, $"damaged: {n} {what} cannot fit in the {left} bytes left", false);
            return (int)n;
        }

        /// <summary>Runs a reader's body: anything it throws that is not already a <see cref="RoFormatException"/> (data ending
        /// early, an index out of range) becomes one, as a damaged file.</summary>
        public static T Guard<T>(string format, Func<T> parse)
        {
            try { return parse(); }
            catch (RoFormatException) { throw; }
            catch (Exception e) { throw new RoFormatException(format, "damaged (" + e.GetType().Name + ": " + e.Message.Trim() + ")", false, e); }
        }
    }
}
