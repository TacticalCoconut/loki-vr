Shader "Loki/Tree"
{
    // Client wave 22 (w22tree), the design's point 64: the 3D skill tree's own shader (UI/SkillTree3D.cs, UI/TreeMesh.cs). Every part of the
    // tree is a mesh made in code whose vertices carry their own material:
    //   COLOR      the surface colour and its alpha
    //   TEXCOORD0  xy = the picture's place (only the icon sheet's material uses a picture: LOKITREE_TEX), z = how much of the slow
    //              breath it takes, w = 1: a bud (small while the tree is not alive, its full size when it is) + 2: a part of the
    //              TRUNK object (the family's rise moves it as one thing: _TrunkShift / _TrunkScale / _TrunkTurn about _TrunkPivot)
    //   TEXCOORD1  x = gloss (0 cloth / wood ... 1 polished metal), y = glow (0 lit like a thing, 1 its own light: lines, crystals),
    //              z = how much of the tree's LIVELINESS it takes (under 0: its OWN colour brightens instead of turning to
    //              _LifeColor), w = from which liveliness on (0: all the way)
    //   TEXCOORD2  xyz = the point the vertex grows out of, w = when (0-1 of the opening)
    // GROWING: _Grow runs 0 -> 1 while the tree opens; a vertex sits on its pivot until _Grow reaches its time and then moves out to its
    // place over _GrowSoft, so a branch pushes out of the trunk, a medallion pops out of its branch (no CPU work per frame).
    // LIGHT: one fixed light in the VIEW's frame (up, left, towards the eyes) plus a soft highlight and a rim, so steel and gold read as
    // metal from every side without any light of the world (the map's light never changes the menu's colours).
    // ORDER: no depth test (as every panel: Loki/UIOverlay) and no depth written: the tree is never cut by the map's houses. Its parts are
    // drawn back to front: the triangles in each mesh in the order they were built, the meshes by their materials' queues. Back faces are
    // not drawn (a tube's far side would paint over its near side).
    // THE RISE (UI/SkillTree3D.Intro.cs): each job family's tree arrives in its own way; the trunk's vertices are moved together by
    // _TrunkShift (mm), _TrunkScale and _TrunkTurn.z (a roll in the tree's plane, radians) about _TrunkPivot, and with _TrunkWhole 1
    // they do not wait for _Grow (a sword that drops in whole). After the rise: shift 0, scale 1, turn 0: the tree stands still.
    // LIVELINESS (_Life 0-1, the job level in the tree's own job): a STATIC look: the parts that take it turn to _LifeColor and to their
    // own steady light, buds reach their size; nothing moves. The one thing that changes by itself is the BREATH of the families that
    // have one (_Breath: its share of brightness, _BreathPeriod seconds: slow and small, e.g. a crystal 6 % over 7 s; 0: still).
    // The preview beside a medallion (UI/SkillDiorama.cs) draws the game's own sprite frames with it too: LOKITREE_TEX, _Unlit, _Tint.
    Properties
    {
        _MainTex ("Icon sheet", 2D) = "white" {}
        _Grow ("Grown (0-1)", Range(0, 1.2)) = 1
        _GrowSoft ("Grow softness", Range(0.01, 0.5)) = 0.12
        _Fade ("Alpha", Range(0, 1)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Life ("Liveliness (0-1)", Range(0, 1)) = 0
        _LifeColor ("Lively colour (a: how far)", Color) = (1, 1, 1, 0.6)
        _Breath ("Breath (share of brightness)", Range(0, 0.2)) = 0
        _BreathPeriod ("Breath period (s)", Float) = 7
        _Unlit ("Unlit (the preview's ghosts)", Range(0, 1)) = 0
        _TrunkShift ("Trunk shift (mm)", Vector) = (0, 0, 0, 0)
        _TrunkScale ("Trunk scale", Vector) = (1, 1, 1, 0)
        _TrunkTurn ("Trunk turn (z: roll, radians)", Vector) = (0, 0, 0, 0)
        _TrunkPivot ("Trunk pivot (mm)", Vector) = (0, 0, 0, 0)
        _TrunkWhole ("Trunk whole (0: it grows, 1: it is there)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle(LOKITREE_TEX)] _UseTex ("Uses the picture", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+90" "IgnoreProjector" = "True" }
        Pass
        {
            Cull [_Cull]
            ZWrite Off
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ LOKITREE_TEX // (multi_compile: a material made in code keeps its variant in a build)
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Grow, _GrowSoft, _Fade, _Unlit, _Life, _Breath, _BreathPeriod, _TrunkWhole;
            fixed4 _Tint, _LifeColor;
            float4 _TrunkShift, _TrunkScale, _TrunkTurn, _TrunkPivot;

            struct appdata
            {
                float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR;
                float4 uv : TEXCOORD0; float4 mat : TEXCOORD1; float4 grow : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION; fixed4 col : COLOR; float2 uv : TEXCOORD0;
                float3 nv : TEXCOORD1; float3 vv : TEXCOORD2; float4 mat : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // out of its pivot once the opening reaches its time (a little past its place first, then settled: a soft spring)
                float trunk = step(1.5, v.uv.w), bud = step(0.5, fmod(v.uv.w, 2.0));
                float g = saturate((_Grow - v.grow.w) / max(_GrowSoft, 0.001));
                g = lerp(g, 1.0, trunk * _TrunkWhole);
                float s = g * g * (3.0 - 2.0 * g);
                s += sin(g * 3.14159265) * 0.12 * (1.0 - g);
                // how alive this part is: all the way with the tree's liveliness, or lit once the tree has reached its own mark
                float alive = v.mat.w > 0.001 ? saturate((_Life - v.mat.w) * 8.0) : _Life;
                float lw = abs(v.mat.z), la = alive * lw;
                float budEase = alive * alive * (3.0 - 2.0 * alive) + (v.mat.w > 0.001 ? sin(alive * 3.14159265) * 0.22 * (1.0 - alive) : 0.0);
                float3 full = bud > 0.5 ? lerp(v.grow.xyz, v.vertex.xyz, lerp(0.28, 1.0, budEase)) : v.vertex.xyz; // a bud: small until alive, springs out with elastic bounce
                float3 p = lerp(v.grow.xyz, full, s);
                float3 nrm = v.normal;
                if (trunk > 0.5)
                {
                    // the trunk object as one thing: scaled and rolled about its pivot, then shifted (all zero / one once it has risen)
                    float3 q = (p - _TrunkPivot.xyz) * _TrunkScale.xyz;
                    float sn, cs; sincos(_TrunkTurn.z, sn, cs);
                    q.xy = float2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
                    nrm.xy = float2(nrm.x * cs - nrm.y * sn, nrm.x * sn + nrm.y * cs);
                    p = _TrunkPivot.xyz + q + _TrunkShift.xyz;
                }
                float4 world = mul(unity_ObjectToWorld, float4(p, 1.0));
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.nv = mul((float3x3)UNITY_MATRIX_V, UnityObjectToWorldNormal(nrm));
                o.vv = mul(UNITY_MATRIX_V, world).xyz;
                // liveliness is a colour: towards the lively colour, or the part's own colour brighter (a steady look), plus a shimmer flash when a bud pops in
                fixed4 col = v.color;
                float pop = bud > 0.5 && v.mat.w > 0.001 ? sin(alive * 3.14159265) * 0.35 * lw : 0.0;
                col.rgb = v.mat.z < 0.0 ? saturate(col.rgb * (1.0 + 0.62 * la) + _LifeColor.rgb * pop) : saturate(lerp(col.rgb, _LifeColor.rgb, la * _LifeColor.a) + _LifeColor.rgb * pop);
                o.col = col * _Tint;
                o.col.a *= _Fade * step(0.001, g);
                if (lw > 0.001 && v.color.a < 0.99) o.col.a *= lerp(0.35, 1.0, alive); // a see-through lively part (smoke, a halo) thickens
                o.uv = v.uv.xy; o.mat = float4(v.mat.x, saturate(v.mat.y + pop), la, v.uv.z);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = i.col;
            #ifdef LOKITREE_TEX
                c *= tex2D(_MainTex, i.uv);
            #endif
                float3 n = normalize(i.nv);
                float3 view = normalize(-i.vv);
                // the view frame looks down -z: the light comes from the upper left, in front of the thing
                float3 l = normalize(float3(-0.45, 0.70, 0.55));
                float ndl = dot(n, l);
                float diff = 0.42 + 0.58 * saturate(ndl * 0.75 + 0.25);
                float3 h = normalize(l + view);
                // liveliness: glossier, more its own light (its colour: the vertex stage)
                float life = i.mat.z;
                float gloss = lerp(i.mat.x, 1.0, life * 0.5);
                float spec = pow(saturate(dot(n, h)), lerp(8.0, 48.0, gloss)) * gloss;
                float rim = pow(1.0 - saturate(dot(n, view)), 3.0) * (0.10 + 0.25 * gloss);
                float3 lit = c.rgb * diff + (spec * 0.85 + rim) * lerp(float3(1.0, 1.0, 1.0), c.rgb, 0.45);
                c.rgb = lerp(lit, c.rgb, saturate(i.mat.y + life * 0.7 + _Unlit));
                // the family's slow breath (if it has one): a small, slow change of brightness of the parts that take it
                c.rgb *= 1.0 + _Breath * i.mat.w * sin(_Time.y * 6.2831853 / max(_BreathPeriod, 1.0));
                return c;
            }
            ENDCG
        }
    }
}
