Shader "Loki/TreeGhost"
{
    // Client wave 22 (w22tree), the design's point 64: the 3D skill tree's preview (UI/SkillDiorama.cs) draws the game's own sprite frames (the
    // player's character, a Poring: the meshes and textures a hidden ActorSprite makes from the player's game data) as pale see-through
    // ghosts over the tree: the frame's picture times _Tint, no light of the map, no room mask, no depth test (as the tree and every
    // panel). Only POSITION and TEXCOORD0 are read: whatever else the sprite's mesh carries is not used. Single-pass-instanced safe.
    Properties
    {
        _MainTex ("Frame", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+102" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint; float _Cutoff;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);
                // a ghost: its own colours, paler, tinted
                c.rgb = lerp(c.rgb, float3(1.0, 1.0, 1.0), 0.18) * _Tint.rgb;
                c.a *= _Tint.a;
                return c;
            }
            ENDCG
        }
    }
}
