// Client wave 4b (w4granny): the Granny 3-D units (Sprites/GrannyUnit.cs). The shading is roBrowserLegacy's GR2 shader
// (src/Renderer/GR2/GR2Model.vs / GR2Model.fs, a reimplementation): a hard alpha test at 207/255, then
//   lightWeight = (1 - opacity) + max(N·L, 0) * opacity          (the map's RSW light opacity and direction)
//   colour = texel * clamp(lightWeight * diffuse + ambient, 0, 1) * clamp(env, 0, 1)
// with diffuse / ambient fixed per model (GR2ModelRenderer.js GR2_ROSTER: 'emp' 128/255 and 127/255; 'flag' 100/255 and the map
// ambient in grey), set by GrannyUnit through a MaterialPropertyBlock. The vertices arrive already skinned (on the CPU, as the
// GR2 vertex shader skins them) and in the unit's own space. Kept from Loki's world models (LokiModel.shader): fog, the indoor
// room mask, the night tint (LokiEnv = rB's uLightEnv), a shadow caster. NOT the cutaway (LokiCutaway): it clears walls and map
// models out of the view of your character; a unit is never cut, like the units drawn as sprites (LokiSprite.shader) — with it, a
// guardian standing next to you lost everything but its feet. rB draws no shadow on or under a GR2 model; the Unity shadow it
// casts here is Loki's, like its sprites'.
Shader "Loki/Granny"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha test (rB ALPHA_REF 207/255)", Range(0, 1)) = 0.81176
        _GrDiffuse ("Model diffuse (grey)", Color) = (0.50196, 0.50196, 0.50196, 1)
        _GrAmbient ("Model ambient (grey)", Color) = (0.49804, 0.49804, 0.49804, 1)
        _LightOpacity ("Map light opacity (RSW)", Range(0, 1)) = 1
        _Hover ("Pointer highlight (Loki)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LokiRoomMask.cginc"
            #include "LokiWorldLight.cginc"

            sampler2D _MainTex;
            float4 _LightDir;
            float4 _GrDiffuse;
            float4 _GrAmbient;
            float4 _Hover;
            float _LightOpacity;
            float _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float light : TEXCOORD1;
                float3 wpos : TEXCOORD2;
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
                o.uv = v.uv;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float lw = max(dot(n, normalize(_LightDir.xyz)), 0.0);   // GR2Model.vs: lightWeight
                o.light = (1.0 - _LightOpacity) + lw * _LightOpacity;    // GR2Model.vs: vLightWeighting
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                LokiRoomClip(i.wpos);
                fixed4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);                                        // GR2Model.fs: discard below uAlphaRef
                c.rgb *= saturate(i.light * _GrDiffuse.rgb + _GrAmbient.rgb); // GR2Model.fs: clamp(color, 0, 1)
                c.rgb *= saturate(LokiEnv());                               // GR2Model.fs: clamp(uLightEnv, 0, 1)
                c.rgb *= _Hover.rgb;
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
