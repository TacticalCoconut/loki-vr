// Project Loki: draws nothing. Used for RSM model parts textured with a pure-black "void" texture (see
// RoAssets.IsBlackTexture): the original client hides them against its black background; Loki has a sky, so they are
// simply not drawn. No shadow caster pass either.
Shader "Loki/NoDraw"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest Never
        }
    }
    Fallback Off
}
