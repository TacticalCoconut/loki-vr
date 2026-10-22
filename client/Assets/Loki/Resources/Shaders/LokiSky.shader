Shader "Loki/Sky"
{
    // Procedural sky for RenderSettings.skybox: vertical gradient (zenith / horizon / ground), a sun disc and drifting
    // value-noise clouds. Colours come from SkyController per map (theme + the client's fogparametertable colour).
    Properties
    {
        _Zenith ("Zenith", Color) = (0.25, 0.45, 0.85, 1)
        _Horizon ("Horizon", Color) = (0.75, 0.85, 0.95, 1)
        _Ground ("Below horizon", Color) = (0.35, 0.33, 0.3, 1)
        _SunDir ("Sun direction", Vector) = (0.3, 0.6, 0.4, 0)
        _SunColor ("Sun colour", Color) = (1, 0.95, 0.8, 1)
        _SunSize ("Sun size", Range(0, 0.2)) = 0.04
        _Clouds ("Cloud amount", Range(0, 1)) = 0.5
        _CloudColor ("Cloud colour", Color) = (1, 1, 1, 1)
        _Stars ("Stars", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Horizon, _Ground, _SunColor, _CloudColor;
            float4 _SunDir; float _SunSize, _Clouds, _Stars;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { float a = 0.5, s = 0; for (int k = 0; k < 4; k++) { s += a * noise(p); p *= 2.03; a *= 0.5; } return s; }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float up = d.y;
                fixed3 c = up >= 0 ? lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(up), 0.6)) : lerp(_Horizon.rgb, _Ground.rgb, saturate(-up * 4));
                // sun
                float3 sd = normalize(_SunDir.xyz);
                float sdot = dot(d, sd);
                c += _SunColor.rgb * (smoothstep(1 - _SunSize, 1 - _SunSize * 0.6, sdot) + 0.25 * pow(saturate(sdot), 64));
                // clouds on a virtual plane above
                if (up > 0.02 && _Clouds > 0.001)
                {
                    float2 uv = d.xz / (up + 0.15) * 1.6 + _Time.x * float2(0.6, 0.25);
                    float n = fbm(uv);
                    float cl = smoothstep(1 - _Clouds, 1.15 - _Clouds * 0.5, n) * saturate(up * 6);
                    c = lerp(c, _CloudColor.rgb, cl * 0.85);
                }
                if (_Stars > 0.001 && up > 0)
                {
                    float2 g = floor(d.xz / (up + 0.3) * 180);
                    float st = step(0.997, hash(g)) * _Stars * saturate(up * 3);
                    c += st;
                }
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
