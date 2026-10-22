using System.IO;
using Loki.IO;
using UnityEngine;

namespace Loki.Formats
{
    /// <summary>AC: animation definitions — actions → frames (motions) → layered sprite clips, plus per-action delays and events.</summary>
    public sealed class ActFile
    {
        public struct Layer
        {
            public int X, Y;          // pixel offset of the clip centre from the frame origin
            public int SpriteNumber;  // index into SPR (indexed or rgba list by SpriteType); -1 = none
            public bool Mirror;
            public Color32 Color;
            public Vector2 Scale;
            public int Angle;         // degrees
            public int SpriteType;    // 0 = indexed, 1 = rgba
            public int Width, Height; // 2.5+ (often 0)
        }
        public struct AttachPoint { public int X, Y; public int Attribute; }
        public sealed class Frame
        {
            public Layer[] Layers;
            public int EventId = -1;
            public AttachPoint[] Anchors = new AttachPoint[0];
        }
        public sealed class Action
        {
            public Frame[] Frames;
            public float Delay = 4f; // in 25ms ticks (default 4 = 100 ms per frame)
        }

        public RoVersion Version;
        public Action[] Actions;
        public string[] Events = new string[0];

        /// <summary>Client wave 7 (w7data): a version newer than 2.5 or a damaged file throws <see cref="RoFormatException"/>
        /// (Formats/RoFormat.cs: the known versions and their sources) — the caller gives a safe result.</summary>
        public static ActFile Parse(byte[] data) => RoFormat.Guard("ACT", () => ParseCore(data));

        private static ActFile ParseCore(byte[] data)
        {
            using (var r = new RoReader(data))
            {
                RoFormat.Signature("ACT", r.Signature(2), "AC");
                var a = new ActFile { Version = RoVersion.MinorFirst(r) };
                var v = a.Version;
                RoFormat.CheckVersion("ACT", v, 2, 5);
                int actionCount = r.U16();
                r.Skip(10);
                a.Actions = new Action[actionCount];
                for (int i = 0; i < actionCount; i++)
                {
                    var act = new Action();
                    int frameCount = RoFormat.Count("ACT", r, r.S32(), 36, "frames");
                    act.Frames = new Frame[frameCount];
                    for (int f = 0; f < frameCount; f++)
                    {
                        var fr = new Frame();
                        r.Skip(32); // two unused bounding ranges
                        int layerCount = RoFormat.Count("ACT", r, r.S32(), 16, "layers");
                        fr.Layers = new Layer[layerCount];
                        for (int l = 0; l < layerCount; l++)
                        {
                            var ly = new Layer { X = r.S32(), Y = r.S32(), SpriteNumber = r.S32(), Mirror = r.U32() != 0, Color = new Color32(255, 255, 255, 255), Scale = Vector2.one };
                            if (v.AtLeast(2, 0))
                            {
                                byte cr = r.U8(), cg = r.U8(), cb = r.U8(), ca = r.U8();
                                ly.Color = new Color32(cr, cg, cb, ca);
                            }
                            if (v.Below(2, 4)) { float s = r.F32(); ly.Scale = new Vector2(s, s); }
                            else ly.Scale = r.Vec2();
                            if (v.AtLeast(2, 0))
                            {
                                ly.Angle = r.S32();
                                ly.SpriteType = r.S32();
                            }
                            if (v.AtLeast(2, 5)) { ly.Width = r.S32(); ly.Height = r.S32(); }
                            fr.Layers[l] = ly;
                        }
                        if (v.AtLeast(2, 0)) fr.EventId = r.S32();
                        if (v.AtLeast(2, 3))
                        {
                            int ac = RoFormat.Count("ACT", r, r.S32(), 16, "anchors");
                            fr.Anchors = new AttachPoint[ac];
                            for (int k = 0; k < ac; k++) { r.S32(); fr.Anchors[k] = new AttachPoint { X = r.S32(), Y = r.S32(), Attribute = r.S32() }; }
                        }
                        act.Frames[f] = fr;
                    }
                    a.Actions[i] = act;
                }
                if (v.AtLeast(2, 1))
                {
                    int ec = RoFormat.Count("ACT", r, r.S32(), 40, "events");
                    a.Events = new string[ec];
                    for (int i = 0; i < ec; i++) a.Events[i] = r.Str(40);
                }
                if (v.AtLeast(2, 2))
                    for (int i = 0; i < actionCount; i++) a.Actions[i].Delay = r.F32();
                return a;
            }
        }
    }
}
