Shader "Loki/RainPuddle"
{
    // Client wave 5 (w5weather): the rain's puddles, roBrowser's own procedural water (a reimplementation: no official asset or code;
    // Renderer/Effects/RainWeather.vs / RainWeather.fs, drawn by RainWeather.js:1030-1106). One flat quad per puddle on the map's ground
    // plane (World/Weather.cs builds them in map cells: x, height, y), the 64 × 64 deformed disc texture of RainWeather.js:219-269,
    // the puddle's alpha in the vertex colour (uColor = 1, 1, 1, alpha :1093). Depth test on, no depth write, alpha blending, before
    // the units' sprites (rB renders puddles before the entities: ScreenEffectManager.js:196-198, runWithDepth(true, false, …) :1033).
    // The fragment is RainWeather.fs:62-107 line for line, with two notes:
    //  • its Fresnel term reads Camera.position, which in roBrowser is the negated target, not the eye (Renderer/Camera.js:280-282,
    //    504-510): normalize(uCameraPos − vWorldPos).y is negative wherever the player's map y exceeds the ground height — everywhere
    //    but a map's southern edge — so max(0, …) is 0 and the term is pow(1 − 0, 3.5) = 1. Loki draws what roBrowser draws: 1.
    //  • pow(combinedWave, …) of a negative wave (the quad's corners, dist > 0.5) is undefined in GLSL and HLSL alike; the base is
    //    clamped at 0 there (those texels are almost all transparent and discarded anyway).
    // The fog is Loki's map fog (LokiWorldLight.cginc: rB passes MapRenderer.fog, the same fogparametertable values).
    Properties
    {
        _MainTex ("Puddle", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-2" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LokiRoomMask.cginc"
            #include "LokiWorldLight.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float3 wpos : TEXCOORD1; float mapX : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex.xyz);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.mapX = v.vertex.x; // rB's vWorldPos.x: the map x in cells, the cell's centre at + 0.5 (RainWeather.vs:35-36)
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                LokiRoomClip(i.wpos);
                float uTime = _Time.y; // rB: tick · 0.001 (RainWeather.js:1056), seconds
                float2 uv = i.uv;
                float2 center = float2(0.5, 0.5);
                float dist = distance(uv, center);
                // multi-layered procedural ripples (RainWeather.fs:67-70)
                float wave1 = sin(dist * 20.0 - uTime * 2.2) * 0.5 + 0.5;
                float wave2 = sin(dist * 12.0 + uTime * 1.4 + i.mapX * 0.5) * 0.5 + 0.5;
                float combinedWave = (wave1 * 0.7 + wave2 * 0.3) * (1.0 - dist * 2.0);
                // refraction-like distortion (:73-78)
                float2 distortedUV = uv + (uv - center) * combinedWave * 0.035;
                float4 texColor = tex2D(_MainTex, distortedUV);
                if (texColor.a < 0.01) discard;
                float fresnel = 1.0; // see the note above (:80-82 with roBrowser's camera vector)
                float w = max(combinedWave, 0.0);
                float3 skyReflect = float3(0.88, 0.92, 1.0);                      // :85
                float reflectFactor = (pow(w, 2.5) * 0.45 + fresnel * 0.5) * 0.8; // :86
                texColor.rgb *= 0.98;                                             // :89
                texColor.rgb = lerp(texColor.rgb, skyReflect, reflectFactor);     // :90
                float shine = pow(w, 8.0) * 0.55 + fresnel * 0.1;                 // :93
                texColor.rgb += float3(shine, shine, shine);                      // :94
                float4 finalColor = texColor * i.color;                           // :97
                finalColor.rgb = LokiFog(finalColor.rgb, i.wpos);                 // :100-104
                return finalColor;
            }
            ENDCG
        }
    }
}
