// =====================================================================================================================
// Project Loki, client wave 4b (w4granny): a C# port, step for step, of granny-ro-js 1.5.0 (npm, MIT;
// https://github.com/stitchuuuu/granny-ro-js), the Granny3D (.gr2) decoder roBrowserLegacy uses (src/Loaders/GR2Loader.js).
// Source read: package/dist/granny-ro.esm.js (a minified bundle). Comments name the package's exported functions and, in
// brackets, the bundle's own minified names; "l. N" are the lines of that bundle reformatted by esbuild
// (client/tools/w4granny/README.md).
//
// This file: the entry points (parse [qn], parseModel [Jn], parseAnimated [Yn], parseTextured [Xn], parseAll [Zn]).
//
// MIT License
//
// Copyright (c) 2026 Stitchuuuu <stitchuuuu@icloud.com>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// =====================================================================================================================
using System.Collections.Generic;

namespace Loki.Granny
{
    /// <summary>What the parse entry points return (the fields a call did not extract stay null).</summary>
    public sealed class GrParsed
    {
        public GrFile File;
        public List<GrMember> TypeTree;
        public Dictionary<string, GrField> Root;
        public List<GrSkeleton> Skeletons;
        public List<GrMesh> Meshes;
        public List<GrTexture> Textures;
        /// <summary>By index; a slot may be null (roBrowserLegacy grafts external banks at fixed action slots).</summary>
        public List<GrAnimation> Animations;
        public List<GrModel> Models;
    }

    public static class GrannyReader
    {
        private static GrParsed Base(byte[] buffer, out GrLoaded loaded)
        {
            var n2 = GrannyFileParser.Parse(buffer);
            loaded = GrannyTypeTree.Load(n2);
            var i2 = GrannyTypeTree.ParseTypeTree(loaded, n2.Header.RootType);
            return new GrParsed
            {
                File = n2, TypeTree = i2,
                Root = GrannyTypeTree.ParseObject(loaded, i2, new GrRef((int)n2.Header.RootObject[0], (int)n2.Header.RootObject[1])),
            };
        }

        /// <summary>parseAnimated [Yn] l. 1982-1985: skeletons, meshes, animations.</summary>
        public static GrParsed ParseAnimated(byte[] buffer)
        {
            var p = Base(buffer, out var r2);
            p.Skeletons = GrannyExtract.ExtractSkeletons(r2); p.Meshes = GrannyExtract.ExtractMeshes(r2);
            p.Animations = GrannyAnimation.ExtractAnimations(r2);
            return p;
        }

        /// <summary>parseAll [Zn] l. 1990-1993: skeletons, meshes, textures, animations, models — one decompression.</summary>
        public static GrParsed ParseAll(byte[] buffer)
        {
            var p = Base(buffer, out var r2);
            p.Skeletons = GrannyExtract.ExtractSkeletons(r2); p.Meshes = GrannyExtract.ExtractMeshes(r2);
            p.Textures = GrannyTexture.ExtractTextures(r2); p.Animations = GrannyAnimation.ExtractAnimations(r2);
            p.Models = GrannyExtract.ExtractModels(r2);
            return p;
        }
    }
}
