Shader "Loki/Sprite"
{
    Properties
    {
        _MainTex ("Frame", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.05
        _Relief ("Relief strength (0 = flat)", Range(0, 1)) = 0
        // client wave 18 (h18feel), Horde point 24: a material may draw without writing depth (a dead monster in a Horde run, drawn
        // before the living ones so it never hides them); On (1) for everything else, as before
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest+10" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            ZWrite [_ZWrite]
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "LokiRoomMask.cginc"
            #include "LokiWorldLight.cginc"

            sampler2D _MainTex; float4 _MainTex_TexelSize;
            float4 _LightDir; float4 _LightDiffuse; float4 _LightAmbient;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)
            float _Cutoff; float _Relief;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wnormal : TEXCOORD1; float3 wpos : TEXCOORD2;
                SHADOW_COORDS(3)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                LokiRoomClip(i.wpos);
                fixed4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);
                c *= UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                // Relief shading: the mesh normal carries the inflated-sprite shape; blend towards flat (fully lit) by _Relief.
                float ndl = max(dot(normalize(i.wnormal), normalize(_LightDir.xyz)), 0.0);
                float shade = lerp(1.0, ndl, _Relief);
                // map point lights: the relief normal, or flat when the relief is off (client wave 2); roBrowser leaves sprites out of the night tint
                float3 light = saturate(shade * _LightDiffuse.rgb + _LightAmbient.rgb + LokiPointLights(i.wpos, _Relief > 0.01 ? i.wnormal : float3(0, 0, 0)));
                c.rgb *= light;
                fixed sh = SHADOW_ATTENUATION(i);
                c.rgb *= lerp(0.6, 1.0, sh);
                c.rgb = LokiFog(c.rgb, i.wpos);
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
                clip(tex2D(_MainTex, i.uv).a - max(_Cutoff, 0.3));
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}
