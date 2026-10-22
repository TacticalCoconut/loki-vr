Shader "Loki/OwnFx"
{
    // Client wave 5 (w5fxown): Loki's OWN skill effects (a design, not the official client's look; Fx/OwnFxRenderer.cs).
    // Texture × tint × vertex colour, additive (SrcAlpha One) or alpha-blended (SrcAlpha OneMinusSrcAlpha) per material, depth-tested,
    // never written. VR comfort: whatever comes closer than _LokiOwnNearFade.y metres to the eye fades out, gone at _LokiOwnNearFade.x (an
    // aura on your own character in first person never fills your view). Fx/OwnFxRenderer.cs sets that global each frame from the map's
    // scale (0.35 / 0.9 cells: in first person 0.25 / 0.63 m, at table scale a few millimetres); unset (0, 0) it fades nothing.
    // _MaxAlpha caps how bright one layer can get. Single-pass-instanced safe.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        _MaxAlpha ("Max alpha", Float) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+22" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LokiRoomMask.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color; float _MaxAlpha;
            float4 _LokiOwnNearFade; // global (Fx/OwnFxRenderer.cs): x gone, y full, world metres

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float3 wpos : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // the eye's own position (per eye with single-pass instancing): near the eye the effect fades out
                float d = distance(_WorldSpaceCameraPos, o.wpos);
                float near = saturate((d - _LokiOwnNearFade.x) / max(_LokiOwnNearFade.y - _LokiOwnNearFade.x, 1e-4));
                o.color = v.color * _Color;
                o.color.a *= near;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                LokiRoomClip(i.wpos);
                fixed4 t = tex2D(_MainTex, i.uv) * i.color;
                t.a = min(t.a, _MaxAlpha);
                if (t.a <= 0.003) discard;
                return t;
            }
            ENDCG
        }
    }
}
