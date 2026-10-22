Shader "Loki/WandSolid"
{
    // Client wave 13 (w13resize): the wand as it is (an unlit colour, as wave 12's Sprites/Default drew it), depth-tested against the
    // world, drawn AFTER the panels (VR/WandHolster.cs: sorting order 11, the panels 10) so that a wand in front of a board is seen
    // whole; where a panel lies between the eye and it (LokiWandPanels.cginc) it is not drawn: the panel covers it there and the
    // ghost (Loki/WandGhost) shows that part. Single-pass-instanced safe.
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
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
            #include "LokiWandPanels.cginc"

            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 eye : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.eye = _WorldSpaceCameraPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (LokiBehindPanel(i.eye, i.wpos) > 0.5) discard;
                if (LokiOverWheel(i.eye, i.wpos) > 0.5) discard; // client wave 18 (w18wand), point 20: over the wand's wheel the ghost shows it
                return _Color;
            }
            ENDCG
        }
    }
}
