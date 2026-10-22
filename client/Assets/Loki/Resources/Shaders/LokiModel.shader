Shader "Loki/Model"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Alpha ("Model alpha", Range(0, 1)) = 1
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "LokiRoomMask.cginc"
            #include "LokiWorldLight.cginc"

            sampler2D _MainTex;
            float4 _LightDir;
            float4 _LightDiffuse;
            float4 _LightAmbient;
            float _Alpha;
            float _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 uv2 : TEXCOORD2; // wave 27 (wmodel): a back copy's model box (min, w = 2); other meshes have none
                float4 uv3 : TEXCOORD3; // its max
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float ndl : TEXCOORD1;
                SHADOW_COORDS(2)
                float3 wpos : TEXCOORD6;
                float4 wn : TEXCOORD7;
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
                float3 n = UnityObjectToWorldNormal(v.normal);
                o.wn = float4(n, v.uv2.w);
                if (v.uv2.w > 2.5)
                {
                    float3 refW = _LokiCutParams.x > 0.5 ? _LokiCutA.xyz : _WorldSpaceCameraPos;
                    float3 e = mul(unity_WorldToObject, float4(refW, 1.0)).xyz;
                    if (e.x < v.uv2.x - 0.5 || e.x > v.uv3.x + 0.5 || e.z < v.uv2.z - 0.5 || e.z > v.uv3.z + 0.5)
                        o.pos = float4(2.0, 2.0, 2.0, 1.0);
                }
                else if (v.uv2.w > 1.5 && _LokiCutParams.x > 0.5)
                {
                    bool overheadCeiling = n.y < -0.25 && o.wpos.y > _WorldSpaceCameraPos.y + max(_LokiCutB.w * 1.5, 0.25);
                    if (!overheadCeiling)
                    {
                        float3 e = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                        if (all(e > v.uv2.xyz - 0.25) && all(e < v.uv3.xyz + 0.25)) o.pos = float4(2.0, 2.0, 2.0, 1.0);
                    }
                }
                o.uv = v.uv;
                o.color = v.color;
                // normal.y < -0.5 in object data marks "no shading" (RSM shade type NONE) – encoded by the builder as vertex color alpha 0
                o.ndl = max(dot(n, normalize(_LightDir.xyz)), 0.0);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                LokiRoomClip(i.wpos);
                float headMargin = max(_LokiCutB.w * 1.5, 0.25);
                if (i.wn.w > 2.5)
                    clip(i.wpos.y - (_WorldSpaceCameraPos.y + headMargin));
                else if (i.wn.y < -0.35 && abs(i.wpos.y - _WorldSpaceCameraPos.y) < headMargin)
                    clip(-1.0);
                LokiCutaway(i.wpos); // forward pass only: shadows keep the full geometry
                fixed4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);
                float shade = i.color.a > 0.5 ? i.ndl : 1.0;
                float3 light = saturate(shade * _LightDiffuse.rgb + _LightAmbient.rgb + LokiPointLights(i.wpos, i.color.a > 0.5 ? i.wn.xyz : float3(0, 0, 0)));
                c.rgb *= light * i.color.rgb * LokiEnv();
                fixed sh = SHADOW_ATTENUATION(i);
                c.rgb *= lerp(0.55, 1.0, sh);
                c.rgb = LokiFog(c.rgb, i.wpos);
                c.a = _Alpha;
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
