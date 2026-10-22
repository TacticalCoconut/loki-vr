// Client wave 13 (w13resize), the brief's second item as updated (2026-10-07 ~11:55: "any part of it that is occluded shows
// up as a ghostly projection of it"): the panels (VR/WandHolster.cs PanelsToShaders: up to 8 open panels with a background, nearest
// first) as world rectangles: centre, half the right edge, half the up edge. The panels are drawn with Loki/UIOverlay (no depth
// test, no depth write), so the depth buffer cannot say what they hide: each fragment of the wand asks here whether a panel's
// rectangle lies between the eye and it.
#ifndef LOKI_WAND_PANELS
#define LOKI_WAND_PANELS

float4 _LokiPanelC[8];
float4 _LokiPanelR[8];
float4 _LokiPanelU[8];
float _LokiPanelCount;

// 1 when the line from the eye to the world point crosses one of the panels' rectangles before reaching it
float LokiBehindPanel(float3 eye, float3 wpos)
{
    float3 d = wpos - eye;
    int n = (int)_LokiPanelCount;
    for (int i = 0; i < 8; i++)
    {
        if (i >= n) break;
        float3 c = _LokiPanelC[i].xyz, r = _LokiPanelR[i].xyz, u = _LokiPanelU[i].xyz;
        float3 nrm = cross(r, u);
        float den = dot(nrm, d);
        if (abs(den) < 1e-6) continue;
        float t = dot(nrm, c - eye) / den;
        if (t <= 0.0 || t >= 1.0) continue;
        float3 p = eye + d * t - c;
        float a = dot(p, r) / max(dot(r, r), 1e-8), b = dot(p, u) / max(dot(u, u), 1e-8);
        if (abs(a) <= 1.0 && abs(b) <= 1.0) return 1.0;
    }
    return 0.0;
}

// Client wave 18 (w18wand), point 20 (design note: "any part of the wand that is occluding the wheel be it by being in front or behind it, becomes
// a ghostly blue transparent entity"): the wand's wheel while it is drawn (UI/WandWheel.cs CoverDiscs, passed by VR/WandHolster.cs each
// frame): up to two discs in one plane (xyz the centre, w the radius; 0: none) and the plane's normal. 1 when the line from the eye
// through the world point meets one of the discs ANYWHERE ahead of the eye: the point lies over the wheel on screen, whether in front of
// it or behind it.
float4 _LokiWheelC[2];
float4 _LokiWheelN;

float LokiOverWheel(float3 eye, float3 wpos)
{
    float3 d = wpos - eye;
    float3 n = _LokiWheelN.xyz;
    float den = dot(n, d);
    if (abs(den) < 1e-6) return 0.0;
    for (int i = 0; i < 2; i++)
    {
        float r = _LokiWheelC[i].w;
        if (r <= 0.0) continue;
        float t = dot(n, _LokiWheelC[i].xyz - eye) / den;
        if (t <= 0.0) continue;
        float3 p = eye + d * t - _LokiWheelC[i].xyz;
        if (dot(p, p) <= r * r) return 1.0;
    }
    return 0.0;
}
#endif
