using System;
using System.Collections.Generic;
using System.Text;

namespace Loki.Effects
{
    /// <summary>
    /// Gravity's STR effect file (research/skill_effects.md §2.1; roBrowserLegacy Loaders/Str.js). Little-endian:
    ///   "STRM" version.L(0x94) fps.L maxKey.L layerCount.L reserved[16]
    ///   per layer: texCount.L, texCount × name[128], keyCount.L, keyCount × 124-byte key:
    ///     frame.L type.L(0 key / 1 per-frame delta) pos[2].f uv[8].f xy[8].f aniframe.f anitype.L delay.f angle.f(/(1024/360)) color[4].f(/255) srcalpha.L destalpha.L mtpreset.L
    /// Texture names are raw CP949 bytes kept as Latin-1 chars (GRF paths); they resolve under data\texture\effect\.
    /// </summary>
    public sealed class StrFile
    {
        public sealed class Key
        {
            public int Frame, Type; public float PosX, PosY; public float[] Uv = new float[8], Xy = new float[8];
            public float AniFrame; public int AniType; public float Delay; public float Angle; public float[] Color = new float[4]; public int SrcAlpha, DestAlpha, MtPreset;
        }
        public sealed class Layer { public string[] Textures; public Key[] Keys; public int LastFrame, LastSource; }
        public int Fps, MaxKey; public Layer[] Layers;

        public static StrFile Parse(byte[] b)
        {
            if (b == null || b.Length < 36 || b[0] != 'S' || b[1] != 'T' || b[2] != 'R' || b[3] != 'M') throw new FormatException("not an STR file");
            int p = 4;
            uint version = U32(b, ref p);
            if (version != 0x94) throw new FormatException($"STR version 0x{version:x} (only 0x94 is known; 0x95 is the .ezv form)");
            var f = new StrFile { Fps = (int)U32(b, ref p), MaxKey = (int)U32(b, ref p) };
            int layers = (int)U32(b, ref p); p += 16;
            f.Layers = new Layer[layers];
            for (int i = 0; i < layers; i++)
            {
                var l = new Layer();
                int tex = (int)U32(b, ref p);
                l.Textures = new string[tex];
                for (int t = 0; t < tex; t++) { l.Textures[t] = CStr(b, p, 128); p += 128; }
                int keys = (int)U32(b, ref p);
                l.Keys = new Key[keys];
                for (int k = 0; k < keys; k++)
                {
                    var key = new Key { Frame = (int)U32(b, ref p), Type = (int)U32(b, ref p) };
                    key.PosX = F32(b, ref p); key.PosY = F32(b, ref p);
                    for (int j = 0; j < 8; j++) key.Uv[j] = F32(b, ref p);
                    for (int j = 0; j < 8; j++) key.Xy[j] = F32(b, ref p);
                    key.AniFrame = F32(b, ref p); key.AniType = (int)U32(b, ref p); key.Delay = F32(b, ref p);
                    key.Angle = F32(b, ref p) / (1024f / 360f);
                    for (int j = 0; j < 4; j++) key.Color[j] = F32(b, ref p) / 255f;
                    key.SrcAlpha = (int)U32(b, ref p); key.DestAlpha = (int)U32(b, ref p); key.MtPreset = (int)U32(b, ref p);
                    l.Keys[k] = key;
                    if (key.Frame > l.LastFrame) l.LastFrame = key.Frame;
                    if (key.Type == 0 && key.Frame > l.LastSource) l.LastSource = key.Frame;
                }
                f.Layers[i] = l;
            }
            return f;
        }

        private static uint U32(byte[] b, ref int p) { uint v = (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24)); p += 4; return v; }
        private static float F32(byte[] b, ref int p) { float v = BitConverter.ToSingle(b, p); p += 4; return v; }
        private static string CStr(byte[] b, int p, int n) { int e = 0; while (e < n && b[p + e] != 0) e++; return Encoding.GetEncoding(28591).GetString(b, p, e); }

        /// <summary>The state of one layer at a fractional frame index, or null when the layer is hidden (Str renderer rules, research §2.3).</summary>
        public sealed class Frame
        {
            public float PosX, PosY, Angle; public float[] Uv = new float[8], Xy = new float[8], Color = new float[4]; public int TexIndex, SrcAlpha, DestAlpha;
        }

        public static bool Evaluate(Layer l, float keyIndex, Frame o)
        {
            int fromId = -1, toId = -1;
            var keys = l.Keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                if (k.Frame <= keyIndex) { if (k.Type == 0) fromId = i; else toId = i; }
            }
            if (fromId < 0) return false;
            if (toId < 0 && keyIndex > l.LastFrame) return false;
            var from = keys[fromId];
            o.SrcAlpha = from.SrcAlpha; o.DestAlpha = from.DestAlpha;
            bool morph = toId == fromId + 1 && keys[toId].Frame == from.Frame;
            if (!morph)
            {
                if (toId >= 0 && l.LastSource <= from.Frame) return false; // a trailing key with earlier interpolation = the layer's end
                o.Angle = from.Angle; o.TexIndex = (int)from.AniFrame; o.PosX = from.PosX; o.PosY = from.PosY;
                Array.Copy(from.Color, o.Color, 4); Array.Copy(from.Uv, o.Uv, 8); Array.Copy(from.Xy, o.Xy, 8);
                return true;
            }
            var to = keys[toId];
            float d = keyIndex - from.Frame;
            for (int i = 0; i < 4; i++) o.Color[i] = from.Color[i] + to.Color[i] * d;
            for (int i = 0; i < 8; i++) { o.Uv[i] = from.Uv[i] + to.Uv[i] * d; o.Xy[i] = from.Xy[i] + to.Xy[i] * d; }
            o.Angle = from.Angle + to.Angle * d; o.PosX = from.PosX + to.PosX * d; o.PosY = from.PosY + to.PosY * d;
            int tex = l.Textures.Length;
            float ani;
            switch (to.AniType)
            {
                case 1: ani = from.AniFrame + to.AniFrame * d; break;
                case 2: ani = Math.Min(from.AniFrame + to.Delay * d, tex - 1); break;
                case 3: ani = tex > 0 ? (from.AniFrame + to.Delay * d) % tex : 0; break;
                case 4: ani = tex > 0 ? (from.AniFrame - to.Delay * d) % tex : 0; break;
                default: ani = 0; break;
            }
            o.TexIndex = (int)ani;
            if (o.TexIndex < 0 || o.TexIndex >= tex) return false;
            return true;
        }
    }
}
