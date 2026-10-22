Shader "Loki/Ghost"
{
    // Client wave 22 (w22clips), point 59 (design note: "a ghostly 3D projection of the player's current controller ... as if it was those ghostly
    // instructors that VR games usually use"): a see-through glowing solid, one flat tint (_Color, given per part: a part that acts is lit
    // in another colour) that is brightest at the silhouette's edge (a fresnel rim): _Color.a at the edge, _Color.a * _Core facing the eye.
    // Both faces are drawn (a see-through shell). No depth written; _ZTest 8 (Always) on the tutorial's guide (the panels are drawn over the world with no depth test: so is the ghost
    // that stands on one), 4 (LEqual) inside the clip's own view. Single-pass-instanced safe (as Loki/WandGhost).
    Properties
    {
        _Color ("Tint (alpha: at the edge)", Color) = (0.55, 0.85, 1, 0.6)
        _Core ("Alpha facing the eye, as a share of the edge's", Range(0, 1)) = 0.3
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+160" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            float _Core;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 eye : TEXCOORD1; float3 nrm : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.eye = _WorldSpaceCameraPos;
                o.nrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float facing = abs(dot(normalize(i.nrm), normalize(i.eye - i.wpos)));
                float rim = 1.0 - facing;
                return fixed4(c.rgb, c.a * lerp(_Core, 1.0, rim * rim));
            }
            ENDCG
        }
    }
}
