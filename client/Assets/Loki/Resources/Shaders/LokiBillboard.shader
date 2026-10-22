Shader "Loki/Billboard"
{
    // Client wave 4 (w4world): camera-facing quads for the weather and the sky's clouds (World/Weather.cs). Each quad's four vertices
    // share its centre (POSITION, object space = map cells) and carry their corner in TEXCOORD1 (object-space units, turned towards
    // this eye in view space, so both eyes of a headset see a flat sprite facing them). roBrowser draws these as spherical billboards
    // too (Renderer/SpriteRenderer.vs:37-40). Blend and depth test come from the material (alpha or additive; test on or off).
    // Client wave 5 (w5weather): _Align 1 turns each quad in the view plane so that its −y side points along TEXCOORD2 (the particle's
    // velocity in object space) as this eye sees it: roBrowser's rain streaks, whose SpriteRenderer.angle is atan2 of the velocity in
    // view space (Renderer/Effects/RainWeather.js:337-360, 986; SpriteRenderer.js:73-78 rotates the quad by −angle, so its +y side,
    // the texture's v = 0 row, leads; Loki's quads carry v = 0 on their −y side, hence −y here). Worked out per eye in the shader
    // because a headset has two views and no fixed camera.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        _Align ("Align to TEXCOORD2", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "IgnoreProjector" = "True" } // before the water and the UI panels (Transparent): no flake or haze over a menu
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LokiRoomMask.cginc"
            #include "LokiWorldLight.cginc"
            sampler2D _MainTex;
            float _Align;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 corner : TEXCOORD1; float3 dir : TEXCOORD2; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float3 wpos : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float scale = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20)); // the diorama's uniform scale
                float3 vc = UnityObjectToViewPos(v.vertex.xyz);
                float2 c = v.corner;
                if (_Align > 0.5)
                {
                    float3 dv = mul((float3x3)UNITY_MATRIX_V, mul((float3x3)unity_ObjectToWorld, v.dir)); // the velocity as this eye sees it
                    float l = length(dv.xy);
                    if (l > 1e-6)
                    {
                        float2 up = -dv.xy / l;             // the quad's +y side trails, its −y side (v = 0) leads
                        float2 right = float2(up.y, -up.x); // a rotation: (0, 1) → up, (1, 0) → right
                        c = v.corner.x * right + v.corner.y * up;
                    }
                }
                vc.xy += c * scale;
                o.pos = mul(UNITY_MATRIX_P, float4(vc, 1.0));
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                LokiRoomClip(i.wpos);
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                clip(c.a - 0.002);
                c.rgb = LokiFog(c.rgb, i.wpos); // roBrowser's sprite shader fogs the weather sprites too (SpriteRenderer.fs:49-75)
                return c;
            }
            ENDCG
        }
    }
}
