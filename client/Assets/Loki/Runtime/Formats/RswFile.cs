using System.Collections.Generic;
using System.IO;
using Loki.IO;
using UnityEngine;

namespace Loki.Formats
{
    /// <summary>GRSW: world description — sub-file names, water, global light, and placed objects (models, lights, sounds, effects).</summary>
    public sealed class RswFile
    {
        public sealed class ModelObject
        {
            public string Name, FileName, NodeName;
            public int AnimType, BlockType;
            public float AnimSpeed;
            public Vector3 Position, Rotation, Scale; // raw RO units / degrees
        }
        public sealed class LightObject { public string Name; public Vector3 Position; public Color Color; public float Range; }
        public sealed class SoundObject { public string Name, File; public Vector3 Position; public float Volume, Range, Cycle; public int Width, Height; }
        public sealed class EffectObject { public string Name; public Vector3 Position; public int Id; public float Delay; public float[] Param = new float[4]; }

        public RoVersion Version;
        public int BuildNumber;
        public string IniFile, GndFile, GatFile, SrcFile;

        public bool HasWater;
        public float WaterLevel, WaveHeight = 1f, WaveSpeed = 2f, WavePitch = 50f;
        public int WaterType, WaterAnimSpeed = 3;

        public int LightLongitude = 45, LightLatitude = 45;
        public Color LightDiffuse = Color.white, LightAmbient = new Color(0.3f, 0.3f, 0.3f);
        public float LightOpacity = 1f;

        public int GroundTop = -500, GroundBottom = 500, GroundLeft = -500, GroundRight = 500;

        public List<ModelObject> Models = new List<ModelObject>();
        public List<LightObject> Lights = new List<LightObject>();
        public List<SoundObject> Sounds = new List<SoundObject>();
        public List<EffectObject> Effects = new List<EffectObject>();

        /// <summary>Client wave 7 (w7data): a version newer than 2.7 or a damaged file throws <see cref="RoFormatException"/>
        /// (Formats/RoFormat.cs: the known versions and their sources) — the caller gives a safe result.</summary>
        public static RswFile Parse(byte[] data) => RoFormat.Guard("RSW", () => ParseCore(data));

        private static RswFile ParseCore(byte[] data)
        {
            using (var r = new RoReader(data))
            {
                RoFormat.Signature("RSW", r.Signature(4), "GRSW");
                var w = new RswFile { Version = RoVersion.MajorFirst(r) };
                var v = w.Version;
                RoFormat.CheckVersion("RSW", v, 2, 7);
                if (v.AtLeast(2, 5)) w.BuildNumber = r.S32();
                if (v.AtLeast(2, 2)) r.U8();
                w.IniFile = r.Str(40);
                w.GndFile = r.Str(40);
                w.GatFile = r.Str(40);
                if (v.AtLeast(1, 4)) w.SrcFile = r.Str(40);

                if (v.Below(2, 6))
                {
                    w.HasWater = true;
                    if (v.AtLeast(1, 3)) w.WaterLevel = r.F32();
                    if (v.AtLeast(1, 8)) { w.WaterType = r.S32(); w.WaveHeight = r.F32(); w.WaveSpeed = r.F32(); w.WavePitch = r.F32(); }
                    if (v.AtLeast(1, 9)) w.WaterAnimSpeed = r.S32();
                }
                if (v.AtLeast(1, 5))
                {
                    w.LightLongitude = r.S32();
                    w.LightLatitude = r.S32();
                    w.LightDiffuse = new Color(r.F32(), r.F32(), r.F32());
                    w.LightAmbient = new Color(r.F32(), r.F32(), r.F32());
                    if (v.AtLeast(1, 7)) w.LightOpacity = r.F32();
                }
                if (v.AtLeast(1, 6))
                {
                    w.GroundTop = r.S32(); w.GroundBottom = r.S32(); w.GroundLeft = r.S32(); w.GroundRight = r.S32();
                }
                if (v.AtLeast(2, 7)) { int n = RoFormat.Count("RSW", r, r.S32(), 4, "values"); r.Skip(4 * n); }

                int count = RoFormat.Count("RSW", r, r.S32(), 4, "objects");
                for (int i = 0; i < count; i++)
                {
                    int type = r.S32();
                    switch (type)
                    {
                        case 1:
                        {
                            var m = new ModelObject();
                            if (v.AtLeast(1, 3)) { m.Name = r.Str(40); m.AnimType = r.S32(); m.AnimSpeed = r.F32(); m.BlockType = r.S32(); }
                            if (v.AtLeast(2, 6) && w.BuildNumber >= 186) r.U8();
                            if (v.AtLeast(2, 7)) r.S32();
                            m.FileName = r.Str(80);
                            m.NodeName = r.Str(80);
                            m.Position = r.Vec3();
                            m.Rotation = r.Vec3();
                            m.Scale = r.Vec3();
                            w.Models.Add(m);
                            break;
                        }
                        case 2:
                        {
                            var l = new LightObject { Name = r.Str(80), Position = r.Vec3() };
                            l.Color = new Color(Mathf.Clamp01(r.F32()), Mathf.Clamp01(r.F32()), Mathf.Clamp01(r.F32()));
                            l.Range = r.F32();
                            w.Lights.Add(l);
                            break;
                        }
                        case 3:
                        {
                            var s = new SoundObject { Name = r.Str(80), File = r.Str(80), Position = r.Vec3(), Volume = r.F32(), Width = r.S32(), Height = r.S32(), Range = r.F32() };
                            s.Cycle = v.AtLeast(2, 0) ? r.F32() : 4f;
                            w.Sounds.Add(s);
                            break;
                        }
                        case 4:
                        {
                            var e = new EffectObject { Name = r.Str(80), Position = r.Vec3(), Id = r.S32(), Delay = r.F32() };
                            for (int k = 0; k < 4; k++) e.Param[k] = r.F32();
                            w.Effects.Add(e);
                            break;
                        }
                        default:
                            throw new RoFormatException("RSW", $"damaged: unknown object type {type} at object {i} of {count}", false);
                    }
                }
                // quadtree follows (2.1+); not needed.
                return w;
            }
        }
    }
}
