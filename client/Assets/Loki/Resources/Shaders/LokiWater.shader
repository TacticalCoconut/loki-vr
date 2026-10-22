Shader "Loki/Water"
{
    // Client wave 4 (w4world): the RSW / GND water animated as the reference clients do (World/MapLoader.cs WaterAnimator sets the
    // uniforms). Vertex wave, roBrowser Renderer/Map/Water.vs:13-21 (korangar shaders/passes/forward/wave.slang:37-50 is the same wave):
    //   x = mod(pos.x, 2), y = mod(pos.z, 2), diff = x < 1 ? (y < 1 ? 1 : -1) : 0
    //   height = sin(radians(_WaveOffset + 0.5 * _WavePitch * (pos.x + pos.z + diff))) * waveHeight
    // pos is in cells (a GND tile is 2 cells: the mesh is map-local, 1 unit = 1 cell, as roBrowser's world units); roBrowser's y
    // points down, so its "+ height" is Loki's "- height". _WaveHeight is already in cells (RO units / 5, Loaders/World.js:130).
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Opacity ("Opacity", Range(0, 1)) = 0.64
        _WaveHeight ("Wave height (cells)", Float) = 0
        _WavePitch ("Wave pitch (degrees)", Float) = 50
        _WaveOffset ("Wave offset (degrees)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "LokiRoomMask.cginc"
            #include "LokiWorldLight.cginc"
            sampler2D _MainTex; float _Opacity; float _WaveHeight, _WavePitch, _WaveOffset;
            float4 _LightDiffuse; float4 _LightAmbient;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 p = v.vertex;
                float mx = fmod(p.x, 2.0), mz = fmod(p.z, 2.0);
                float diff = mx < 1.0 ? (mz < 1.0 ? 1.0 : -1.0) : 0.0;
                float h = sin(radians(_WaveOffset + 0.5 * _WavePitch * (p.x + p.z + diff))) * _WaveHeight;
                p.y -= h; // roBrowser adds it in its y-down space
                o.pos = UnityObjectToClipPos(p);
                o.wpos = mul(unity_ObjectToWorld, p).xyz;
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                LokiRoomClip(i.wpos);
                fixed4 c = tex2D(_MainTex, i.uv);
                c.rgb *= saturate(_LightDiffuse.rgb + _LightAmbient.rgb);
                c.rgb = LokiFog(c.rgb, i.wpos); // roBrowser Water.fs fogs the water too
                c.a = _Opacity;
                return c;
            }
            ENDCG
        }
    }
}
