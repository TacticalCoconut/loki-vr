Shader "Loki/WandGhost"
{
    // Client wave 13 (w13resize), the brief's second item as updated (2026-10-07 ~11:55: "it should be visible in a way that any
    // part of it that is occluded shows up as a ghostly projection of it"): the wand's HIDDEN parts only, as a faint see-through glow,
    // one flat tint (_Color) brighter at the silhouette's edge (a fresnel rim): _Color.a at the edge, _Color.a * _Core facing the eye.
    // Drawn after the panels and the laser (VR/WandHolster.cs: sorting order 33). Two passes that never overlap:
    //   1  hidden by the world (the ground, units, buildings: they write depth): ZTest Greater
    //   2  in front of the world but behind a panel (boards, windows, the classic wrist menu, the pad: they write no depth; the
    //      fragment asks LokiWandPanels.cginc): ZTest LEqual, discarded where no panel covers it. Client wave 18 (w18wand), point 20:
    //      also wherever it lies over the wand's open wheel on screen, in front of it or behind it (LokiOverWheel)
    // Where the wand is in plain view neither pass draws: only the normal wand (Loki/WandSolid) is seen. Single-pass-instanced safe.
    Properties
    {
        _Color ("Tint (alpha: at the edge)", Color) = (0.55, 0.85, 1, 0.5)
        _Core ("Alpha facing the eye, as a share of the edge's", Range(0, 1)) = 0.35
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #include "LokiWandPanels.cginc"
    fixed4 _Color; float _Core;
    struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 eye : TEXCOORD1; float3 nrm : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
    v2f vert(appdata v)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.pos = UnityObjectToClipPos(v.vertex);
        o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
        o.eye = _WorldSpaceCameraPos;
        o.nrm = UnityObjectToWorldNormal(v.normal);
        return o;
    }
    fixed4 Ghost(v2f i)
    {
        float facing = abs(dot(normalize(i.nrm), normalize(i.eye - i.wpos)));
        float rim = 1.0 - facing;
        return fixed4(_Color.rgb, _Color.a * lerp(_Core, 1.0, rim * rim));
    }
    ENDCG
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+150" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Back
            ZWrite Off
            ZTest Greater
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            fixed4 frag(v2f i) : SV_Target { return Ghost(i); }
            ENDCG
        }
        Pass
        {
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            fixed4 frag(v2f i) : SV_Target
            {
                if (LokiBehindPanel(i.eye, i.wpos) < 0.5 && LokiOverWheel(i.eye, i.wpos) < 0.5) discard; // (client wave 18, w18wand, point 20: or over the wand's wheel)
                return Ghost(i);
            }
            ENDCG
        }
    }
}
