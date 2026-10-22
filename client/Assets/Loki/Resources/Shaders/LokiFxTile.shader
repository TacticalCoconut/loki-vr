Shader "Loki/FxTile"
{
    // Client wave 4 (w4fx): roBrowserLegacy's ground-effect tiles (Renderer/Effects/Tiles.fs, LPEffect.fs, SpiderWeb.fs, PropertyGround.fs,
    // FlatColorTile.fs): texels with any of r, g, b below _Cutoff are discarded, the alpha is forced to _FixedAlpha (< 0: the texture's),
    // times the tint; normal alpha blending, depth-tested but never written, both faces. Single-pass-instanced safe.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Discard texels darker than", Range(0, 1)) = 0
        _FixedAlpha ("Fixed alpha (< 0: texture alpha)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LokiRoomMask.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _Cutoff;
            float _FixedAlpha;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                LokiRoomClip(i.wpos);
                fixed4 t = tex2D(_MainTex, i.uv);
                if (t.a <= 0.0 || t.r < _Cutoff || t.g < _Cutoff || t.b < _Cutoff) discard;
                if (_FixedAlpha >= 0) t.a = _FixedAlpha;
                return t * _Color;
            }
            ENDCG
        }
    }
}
