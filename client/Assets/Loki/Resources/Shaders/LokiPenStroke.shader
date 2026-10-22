Shader "Loki/PenStroke"
{
    // Client wave 20 (w20pen), the design's point 57: the air pen's strokes (UI/PenDrawings.cs): solid tubes in the world, depth-tested and writing
    // depth like any thing on the table (a stroke behind a house is hidden by it, a stroke in front of a sprite hides it), coloured by their
    // vertex colour (the drawer's tint) times _Color, shaded by a fixed light from above so a tube reads as round. Unlit otherwise (the
    // world's own light does not change a drawing's colour). Single-pass-instanced safe.
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float light = 0.62 + 0.38 * saturate(dot(n, normalize(float3(0.3, 1.0, -0.25))));
                o.col = v.color * _Color;
                o.col.rgb *= light;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(i.col.rgb, 1);
            }
            ENDCG
        }
    }
}
