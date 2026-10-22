// Client wave 2 (research/world_rendering.md §B, §C, §E): map fog, the night tint and the map's point lights, set as globals by
// World/WorldLook.cs. Every global defaults to 0 when never set, so each feature has an "on" component that must be 1.
#ifndef LOKI_WORLD_LIGHT
#define LOKI_WORLD_LIGHT

float4 _LokiFog;        // x near, y far (world metres, already scaled by the diorama), z on (1), w unused
float4 _LokiFogColor;
float4 _LokiEnv;        // rgb night multiplier, w on (1): roBrowser ScreenEffectManager.setNight, ground and models only
float4 _LokiPL[16];     // xyz world position, w range (world metres)
float4 _LokiPLColor[16];
float _LokiPLCount;

// roBrowser Ground.fs:61-65: smoothstep(near, far, depth) then mix to the fog colour (distance to this eye instead of view depth)
float3 LokiFog(float3 rgb, float3 wpos)
{
    if (_LokiFog.z < 0.5) return rgb;
    float d = distance(wpos, _WorldSpaceCameraPos.xyz);
    return lerp(rgb, _LokiFogColor.rgb, smoothstep(_LokiFog.x, _LokiFog.y, d));
}

float3 LokiEnv() { return _LokiEnv.w > 0.5 ? _LokiEnv.rgb : float3(1, 1, 1); }

// korangar forward.slang:112-117 attenuation saturate(1-(d/r)^2)^2, N·L towards the light
float3 LokiPointLights(float3 wpos, float3 n)
{
    float3 sum = 0;
    int count = (int)_LokiPLCount;
    for (int i = 0; i < 16; i++)
    {
        if (i >= count) break;
        float3 L = _LokiPL[i].xyz - wpos;
        float d = length(L);
        float r = max(_LokiPL[i].w, 1e-4);
        float a = saturate(1.0 - (d / r) * (d / r)); a *= a;
        float ndl = n.x == 0 && n.y == 0 && n.z == 0 ? 1.0 : max(dot(normalize(n), L / max(d, 1e-4)), 0.0);
        sum += _LokiPLColor[i].rgb * a * ndl;
    }
    return sum;
}

#endif
