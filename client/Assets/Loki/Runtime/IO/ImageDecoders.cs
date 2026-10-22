using System;
using System.IO;
using UnityEngine;

namespace Loki.IO
{
    /// <summary>Decodes BMP / TGA / (JPG, PNG via Unity) into top-down RGBA32 pixel arrays. Magenta (255,0,255) in BMPs becomes transparent, as in the client.</summary>
    public static class ImageDecoders
    {
        public struct Image { public int Width, Height; public Color32[] Pixels; /* row 0 = TOP */ }

        public static bool TryDecode(string name, byte[] data, out Image img, bool magentaIsTransparent = true)
        {
            img = default;
            if (data == null || data.Length < 4) return false;
            string ext = Path.GetExtension(name).ToLowerInvariant();
            try
            {
                if (data[0] == 'B' && data[1] == 'M') { img = DecodeBmp(data, magentaIsTransparent); return true; }
                if (ext == ".tga") { img = DecodeTga(data); return true; }
                // JPG / PNG
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(data, false)) { LokiUtil.Destroy(tex); return false; }
                img.Width = tex.width; img.Height = tex.height;
                var px = tex.GetPixels32(); // bottom-up
                img.Pixels = FlipRows(px, tex.width, tex.height);
                LokiUtil.Destroy(tex);
                if (magentaIsTransparent) KeyMagenta(img.Pixels);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ImageDecoders] {name}: {e.Message}");
                return false;
            }
        }

        /// <summary>Every opaque pixel (near) black: a void filler texture (World/RoAssets.cs IsBlackTexture; client wave 20 moved the test here
        /// so the first-run builder's room table, Data/RoomTools.cs, makes the same call off the main thread).</summary>
        public static bool IsAllBlack(in Image img)
        {
            if (img.Pixels == null || img.Pixels.Length == 0) return false;
            bool any = false;
            foreach (var p in img.Pixels)
            {
                if (p.a == 0) continue; any = true;
                if (p.r > 8 || p.g > 8 || p.b > 8) return false;
            }
            return any;
        }

        public static Texture2D ToTexture(in Image img, bool mipmaps, string name = null)
        {
            var tex = new Texture2D(img.Width, img.Height, TextureFormat.RGBA32, mipmaps, false);
            tex.name = name ?? "ro_image";
            tex.SetPixels32(FlipRows(img.Pixels, img.Width, img.Height)); // Unity wants bottom-up
            tex.Apply(mipmaps, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            if (img.Pixels != null && img.Width > 0 && img.Height > 0)
            {
                int w = img.Width, h = img.Height, lastRow = (h - 1) * w;
                bool cutV = false, cutU = false;
                for (int x = 0; x < w; x++) if (img.Pixels[x].a == 0 || img.Pixels[lastRow + x].a == 0) { cutV = true; break; }
                for (int y = 0; y < h; y++) if (img.Pixels[y * w].a == 0 || img.Pixels[y * w + w - 1].a == 0) { cutU = true; break; }
                if (cutV) tex.wrapModeV = TextureWrapMode.Clamp;
                if (cutU) tex.wrapModeU = TextureWrapMode.Clamp;
            }
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        public static Color32[] FlipRows(Color32[] src, int w, int h)
        {
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++) Array.Copy(src, y * w, dst, (h - 1 - y) * w, w);
            return dst;
        }

        private static void KeyMagenta(Color32[] px)
        {
            for (int i = 0; i < px.Length; i++)
                if (px[i].r >= 0xF8 && px[i].g <= 0x07 && px[i].b >= 0xF8) px[i] = new Color32(0, 0, 0, 0);
        }

        // ---------------- BMP ----------------
        public static Image DecodeBmp(byte[] d, bool keyMagenta)
        {
            int dataOffset = BitConverter.ToInt32(d, 10);
            int hdrSize = BitConverter.ToInt32(d, 14);
            int w, h; int bpp; int compression = 0; int colorsUsed = 0;
            if (hdrSize == 12) { w = BitConverter.ToInt16(d, 18); h = BitConverter.ToInt16(d, 20); bpp = BitConverter.ToInt16(d, 24); }
            else
            {
                w = BitConverter.ToInt32(d, 18); h = BitConverter.ToInt32(d, 22); bpp = BitConverter.ToInt16(d, 28);
                compression = BitConverter.ToInt32(d, 30); colorsUsed = BitConverter.ToInt32(d, 46);
            }
            bool topDown = h < 0; if (topDown) h = -h;
            if (compression != 0 && compression != 3) throw new NotSupportedException("BMP compression " + compression);
            var px = new Color32[w * h];
            int palOffset = 14 + hdrSize;
            int palEntry = hdrSize == 12 ? 3 : 4;
            int rowBytes = ((w * bpp + 31) / 32) * 4;
            for (int row = 0; row < h; row++)
            {
                int srcY = topDown ? row : (h - 1 - row);
                int p = dataOffset + srcY * rowBytes;
                int o = row * w;
                switch (bpp)
                {
                    case 8:
                        for (int x = 0; x < w; x++) { int i = d[p + x]; int q = palOffset + i * palEntry; px[o + x] = new Color32(d[q + 2], d[q + 1], d[q], 255); }
                        break;
                    case 4:
                        for (int x = 0; x < w; x++) { int b = d[p + (x >> 1)]; int i = (x & 1) == 0 ? (b >> 4) : (b & 15); int q = palOffset + i * palEntry; px[o + x] = new Color32(d[q + 2], d[q + 1], d[q], 255); }
                        break;
                    case 1:
                        for (int x = 0; x < w; x++) { int b = d[p + (x >> 3)]; int i = (b >> (7 - (x & 7))) & 1; int q = palOffset + i * palEntry; px[o + x] = new Color32(d[q + 2], d[q + 1], d[q], 255); }
                        break;
                    case 24:
                        for (int x = 0; x < w; x++) { int q = p + x * 3; px[o + x] = new Color32(d[q + 2], d[q + 1], d[q], 255); }
                        break;
                    case 32:
                        for (int x = 0; x < w; x++) { int q = p + x * 4; px[o + x] = new Color32(d[q + 2], d[q + 1], d[q], 255); }
                        break;
                    case 16:
                        for (int x = 0; x < w; x++) { int v = BitConverter.ToUInt16(d, p + x * 2); px[o + x] = new Color32((byte)(((v >> 10) & 31) * 255 / 31), (byte)(((v >> 5) & 31) * 255 / 31), (byte)((v & 31) * 255 / 31), 255); }
                        break;
                    default: throw new NotSupportedException("BMP bpp " + bpp);
                }
            }
            if (keyMagenta) KeyMagenta(px);
            return new Image { Width = w, Height = h, Pixels = px };
        }

        // ---------------- TGA ----------------
        public static Image DecodeTga(byte[] d)
        {
            int idLen = d[0];
            int cmapType = d[1];
            int imgType = d[2];
            int cmapStart = BitConverter.ToUInt16(d, 3);
            int cmapLen = BitConverter.ToUInt16(d, 5);
            int cmapBits = d[7];
            int w = BitConverter.ToUInt16(d, 12);
            int h = BitConverter.ToUInt16(d, 14);
            int bpp = d[16];
            int desc = d[17];
            bool topDown = (desc & 0x20) != 0;
            int p = 18 + idLen;
            Color32[] cmap = null;
            if (cmapType == 1)
            {
                cmap = new Color32[cmapLen];
                int cb = cmapBits / 8;
                for (int i = 0; i < cmapLen; i++) { cmap[i] = ReadTgaPixel(d, p, cmapBits); p += cb; }
            }
            bool rle = imgType >= 9;
            int baseType = rle ? imgType - 8 : imgType;
            var px = new Color32[w * h];
            int bytes = bpp / 8;
            int n = w * h;
            if (!rle)
            {
                for (int i = 0; i < n; i++) { px[i] = baseType == 1 ? cmap[d[p]] : ReadTgaPixel(d, p, bpp); p += bytes; }
            }
            else
            {
                int i = 0;
                while (i < n)
                {
                    int hdr = d[p++];
                    int count = (hdr & 0x7F) + 1;
                    if ((hdr & 0x80) != 0)
                    {
                        var c = baseType == 1 ? cmap[d[p]] : ReadTgaPixel(d, p, bpp); p += bytes;
                        for (int k = 0; k < count && i < n; k++) px[i++] = c;
                    }
                    else
                        for (int k = 0; k < count && i < n; k++) { px[i++] = baseType == 1 ? cmap[d[p]] : ReadTgaPixel(d, p, bpp); p += bytes; }
                }
            }
            if (!topDown) px = FlipRows(px, w, h);
            return new Image { Width = w, Height = h, Pixels = px };
        }

        private static Color32 ReadTgaPixel(byte[] d, int p, int bpp)
        {
            switch (bpp)
            {
                case 32: return new Color32(d[p + 2], d[p + 1], d[p], d[p + 3]);
                case 24: return new Color32(d[p + 2], d[p + 1], d[p], 255);
                case 16: { int v = d[p] | (d[p + 1] << 8); return new Color32((byte)(((v >> 10) & 31) * 255 / 31), (byte)(((v >> 5) & 31) * 255 / 31), (byte)((v & 31) * 255 / 31), (byte)((v & 0x8000) != 0 ? 255 : 255)); }
                case 8: return new Color32(d[p], d[p], d[p], 255);
                default: throw new NotSupportedException("TGA bpp " + bpp);
            }
        }
    }
}
