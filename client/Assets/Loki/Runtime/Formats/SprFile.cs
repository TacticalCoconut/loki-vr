using System.IO;
using Loki.IO;
using UnityEngine;

namespace Loki.Formats
{
    /// <summary>SP: sprite sheet — indexed (palette) frames, optional RGBA frames, and a 256-entry palette (1.1+).</summary>
    public sealed class SprFile
    {
        public struct Frame
        {
            public int Width, Height;
            public byte[] Indexed; // palette indices, null for RGBA frames
            public Color32[] Rgba; // straight RGBA, null for indexed frames (row 0 = top of image, RO stores it bottom-up)
        }

        public RoVersion Version;
        public Frame[] IndexedFrames;
        public Frame[] RgbaFrames;
        public Color32[] Palette; // 256; index 0 is transparent

        public int FrameCount => IndexedFrames.Length + RgbaFrames.Length;
        /// <summary>ACT sprite numbers: 0..n-1 = indexed frames, then RGBA frames. Negative sprite number = no sprite. For type 1 (RGBA) clips, ACT indexes the RGBA list directly.</summary>
        public Frame GetFrame(int spriteNumber, int spriteType)
        {
            if (spriteType == 1) return RgbaFrames[spriteNumber];
            return IndexedFrames[spriteNumber];
        }

        /// <summary>Client wave 7 (w7data): a version newer than 2.1 or a damaged file throws <see cref="RoFormatException"/>
        /// (Formats/RoFormat.cs: the known versions and their sources) — the caller gives a safe result.</summary>
        public static SprFile Parse(byte[] data) => RoFormat.Guard("SPR", () => ParseCore(data));

        /// <summary>A frame wider or taller than this is taken for a damaged size (the largest frame of the LATAM data is far below).</summary>
        private const int MaxFrameSide = 8192;

        private static SprFile ParseCore(byte[] data)
        {
            using (var r = new RoReader(data))
            {
                RoFormat.Signature("SPR", r.Signature(2), "SP");
                var s = new SprFile { Version = RoVersion.MinorFirst(r) };
                RoFormat.CheckVersion("SPR", s.Version, 2, 1);
                int indexedCount = r.U16();
                int rgbaCount = s.Version.AtLeast(1, 2) ? r.U16() : 0;
                s.IndexedFrames = new Frame[indexedCount];
                s.RgbaFrames = new Frame[rgbaCount];
                bool rle = s.Version.AtLeast(2, 1);

                for (int i = 0; i < indexedCount; i++)
                {
                    var f = new Frame { Width = r.U16(), Height = r.U16() };
                    if (f.Width > MaxFrameSide || f.Height > MaxFrameSide) throw new RoFormatException("SPR", $"damaged: frame {i} is {f.Width} x {f.Height}", false);
                    int size = f.Width * f.Height;
                    if (!rle) RoFormat.Count("SPR", r, size, 1, "pixels");
                    f.Indexed = new byte[size];
                    if (size > 0)
                    {
                        if (!rle) f.Indexed = r.Bytes(size);
                        else
                        {
                            int encoded = r.U16();
                            int next = 0;
                            while (next < size && encoded > 0)
                            {
                                byte b = r.U8(); encoded--;
                                if (b == 0)
                                {
                                    int len = r.U8(); encoded--;
                                    if (len == 0) len = 1;
                                    next += len; // zeros (transparent) — buffer already zeroed
                                }
                                else f.Indexed[next++] = b;
                                // (a run past the frame's end leaves the loop: next >= size)
                            }
                        }
                    }
                    s.IndexedFrames[i] = f;
                }
                for (int i = 0; i < rgbaCount; i++)
                {
                    var f = new Frame { Width = r.U16(), Height = r.U16() };
                    if (f.Width > MaxFrameSide || f.Height > MaxFrameSide) throw new RoFormatException("SPR", $"damaged: RGBA frame {i} is {f.Width} x {f.Height}", false);
                    int size = f.Width * f.Height;
                    RoFormat.Count("SPR", r, size, 4, "RGBA pixels");
                    f.Rgba = new Color32[size];
                    var raw = r.Bytes(size * 4);
                    // RO stores RGBA frames upside-down relative to indexed ones; flip rows so both are top-down.
                    for (int y = 0; y < f.Height; y++)
                    {
                        int srcRow = (f.Height - 1 - y) * f.Width;
                        for (int x = 0; x < f.Width; x++)
                        {
                            int si = (srcRow + x) * 4;
                            f.Rgba[y * f.Width + x] = new Color32(raw[si + 3], raw[si + 2], raw[si + 1], raw[si]); // stored ABGR
                        }
                    }
                    s.RgbaFrames[i] = f;
                }
                if (s.Version.AtLeast(1, 1))
                {
                    s.Palette = new Color32[256];
                    for (int i = 0; i < 256; i++)
                    {
                        byte cr = r.U8(), cg = r.U8(), cb = r.U8(); r.U8();
                        s.Palette[i] = new Color32(cr, cg, cb, (byte)(i == 0 ? 0 : 255));
                    }
                }
                return s;
            }
        }
    }
}
