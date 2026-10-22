# Third-party code in Project Loki

Code written by others that is copied or ported into this repository, with its licence. (Tools the repository only
reads from outside it — rAthena's source, roBrowserLegacy, the RO client's data — are cited in the code and progress
files, not listed here.)

Loki itself is under the GNU GPL version 3 (`LICENSE`); the code below keeps its own licence.

## granny-ro-js 1.5.0 — the Granny3D (.gr2) decoder (client wave 4b, w4granny, 2026-09-30)

- What: `client/Assets/Loki/Runtime/Granny/*.cs` is a C# port, step for step, of granny-ro-js 1.5.0 (npm package
  `granny-ro-js`, https://github.com/stitchuuuu/granny-ro-js; the file read: `package/dist/granny-ro.esm.js`, sha-256 of
  the tarball `03e7ce3722cd67c704ab0eb21990f3f24fa9c690603d77c50d3a7b8726e9f80a`): Oodle0 decompression, the Granny
  type tree and sections, skeletons, meshes, materials, textures (raw and the wavelet "IGC" codec), animations and pose
  composition. roBrowserLegacy decodes the same files with this package (`src/Loaders/GR2Loader.js`).
- Every ported file carries the licence text below in its header; `GrannyLicense.cs` carries both notices as string
  constants so that they are inside every build of the client too.
- The package's own README says its authors checked edge cases against a leaked Granny SDK source ("referenced only as an
  asm-cite oracle … not used as a port source") and validated output against RAD's granny2.dll under wine. Neither is in
  the package or in this repository; Loki's port was written from the package's JavaScript only and checked against the
  package's own output (design notes).

```
MIT License

Copyright (c) 2026 Stitchuuuu <stitchuuuu@icloud.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The package's Oodle0 decompressor and GR2 header / section-table parser (ported here as `GrannyOodle0.cs` and
`GrannyFile.cs`) are in turn derived from Rasetsuu/blendergranny (https://github.com/Rasetsuu/blendergranny), whose notice
the package's LICENSE reproduces:

```
MIT License

Copyright (c) 2026 ciupix and contributors

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

(The package's LICENSE also credits magcius/noclip.website, MIT, for a test-time shim around granny2.dll; nothing of it
was ported.)
