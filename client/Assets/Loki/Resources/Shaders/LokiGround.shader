Shader "Loki/Ground"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Lightmap ("Lightmap (rgb=color, a=shadow)", 2D) = "white" {}
        _UseLightmap ("Use Lightmap", Float) = 1
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "LokiRoomMask.cginc"
            #include "LokiWorldLight.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _Lightmap;
            float4 _LightDir;
            float4 _LightDiffuse;
            float4 _LightAmbient;
            float _UseLightmap;
            float _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 color : COLOR;
                float ndl : TEXCOORD2;
                SHADOW_COORDS(3)
                float3 wpos : TEXCOORD6;
                float wall : TEXCOORD7; // client wave 19 (w19world), point 45: 1 on a wall face, 0 on a floor tile
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // (World/GroundMeshBuilder.cs: a wall face's normal is level, y = 0; a downward-facing indoor ceiling has y < -0.5; floor is > 0)
                o.wall = v.normal.y < -0.5 ? 2.0 : (abs(v.normal.y) < 0.01 ? 1.0 : 0.0);
                o.uv = v.uv;
                o.uv1 = v.uv1;
                o.color = v.color;
                float3 n = UnityObjectToWorldNormal(v.normal);
                o.ndl = v.normal.y < -0.5 ? 0.65 : max(dot(n, normalize(_LightDir.xyz)), 0.0);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                LokiRoomClip(i.wpos);
                // forward pass only: shadows keep the full geometry. Client wave 19 (w19world), point 45 (design note: "keeps repeatedly cutting
                // through the ground and making me see the skybox underneath it"): the walls only, never the floor; indoor ceilings clip when the eye is at or above them
                if (i.wall > 1.5)
                {
                    float headMargin = max(_LokiCutB.w * 1.5, 0.25);
                    clip(i.wpos.y - (_WorldSpaceCameraPos.y + headMargin));
                }
                else if (i.wall > 0.5) LokiCutaway(i.wpos);
                fixed4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);
                c *= i.color;
                float3 light = saturate(i.ndl * _LightDiffuse.rgb + _LightAmbient.rgb);
                c.rgb *= light * LokiEnv(); // night: roBrowser Ground.fs:44-57 multiplies by env before the lightmap
                if (_UseLightmap > 0.5)
                {
                    fixed4 lm = tex2D(_Lightmap, i.uv1);
                    c.rgb *= lm.a;
                    c.rgb += saturate(lm.rgb);
                }
                // Real-time shadow from the Unity directional light (units, hands, moving models).
                fixed sh = SHADOW_ATTENUATION(i);
                c.rgb *= lerp(0.55, 1.0, sh);
                c.rgb = LokiFog(c.rgb, i.wpos);
                c.a = 1;
                return c;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LokiRoomMask.cginc"
            sampler2D _MainTex; float _Cutoff;
            struct v2f { V2F_SHADOW_CASTER; float2 uv : TEXCOORD1; float3 wpos : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                o.uv = v.texcoord.xy;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                LokiRoomClip(i.wpos);
                clip(tex2D(_MainTex, i.uv).a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}
