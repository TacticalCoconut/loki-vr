// Project Loki: "only the room you are in" for indoor maps.
// RO packs every interior of a town into one map (prt_in, …) and teleports you between them. The client gives the shaders a
// per-cell mask holding each cell's ROOM (World/RoomMask.cs: the room table Data/RoomTools.cs made from the game files;
// 0 = no room) and every Loki shader discards fragments whose map cell is not in the room drawn. _LokiRoomMaskParams.x = 0
// disables the test. Client wave 20 (w20rooms), point 54: when the character comes into another room the old room dissolves
// away while the new one dissolves in (_LokiRoomFade.z from 0 to 1 over RoomMask.FadeSeconds; a fixed pattern of 1/8-cell
// grains in map space, so a grain never flickers); outside the map's cells nothing is drawn.
#ifndef LOKI_ROOM_MASK_INCLUDED
#define LOKI_ROOM_MASK_INCLUDED
sampler2D _LokiRoomMask;           // R8, one texel per GAT cell: the cell's room id / 255, point-filtered, clamped
float4x4 _LokiMapWorldToLocal;     // world → map-local (Loki units = GAT cells)
float4 _LokiRoomMaskParams;        // x: on (0/1), y: width in cells, z: height in cells
float4 _LokiRoomFade;              // x: the room drawn, y: the room fading out (= x when no fade runs), z: the fade (0 → 1)

// a stable value in [0, 1) per 1/8-cell grain of map space (Dave Hoskins' hash13, no sine)
inline float LokiRoomGrain(float3 l)
{
    float3 p = frac(floor(l * 8.0) * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

inline void LokiRoomClip(float3 wpos)
{
    if (_LokiRoomMaskParams.x > 0.5)
    {
        float3 l = mul(_LokiMapWorldToLocal, float4(wpos, 1.0)).xyz;
        float2 uv = float2(l.x / _LokiRoomMaskParams.y, l.z / _LokiRoomMaskParams.z);
        float id = floor(tex2D(_LokiRoomMask, uv).r * 255.0 + 0.5);
        float keep = (abs(id - _LokiRoomFade.x) < 0.5 ? _LokiRoomFade.z : 0.0) + (abs(id - _LokiRoomFade.y) < 0.5 ? 1.0 - _LokiRoomFade.z : 0.0);
        if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) keep = 0.0;
        clip(keep >= 0.999 ? 1.0 : keep - LokiRoomGrain(l) - 0.0001);
    }
}
// Project Loki: see-through cutaway. Geometry (ground walls and RSM models) that sits between your eyes and your
// character, above the character's feet, is discarded so buildings, arches and trees never hide you in the table and
// follow views. The volume is a cone: _LokiCutParams.y wide at the character, narrowing towards the eye. The ground's FLOOR
// tiles never take it (LokiGround.shader: its wall faces only; client wave 19, point 45).
float4 _LokiCutParams;             // x: cone on (0/1), y: radius at the character (world m), z: world y below which nothing is cut, w: ceiling world y (0 = off)
float4 _LokiCutA;                  // xyz: the character's chest (world)
float4 _LokiCutB;                  // xyz: the viewer's eye (world), w: head clipping guard radius (world m, 0 = off)

inline void LokiCutaway(float3 wpos)
{
    if (wpos.y > _LokiCutParams.z)
    {
        if (_LokiCutB.w > 0.001)
            clip(distance(wpos, _LokiCutB.xyz) - _LokiCutB.w);
        if (_LokiCutParams.w > _LokiCutParams.z && _LokiCutB.y >= _LokiCutParams.w - max(_LokiCutB.w * 1.5, 0.25))
            clip(_LokiCutParams.w - wpos.y);
        if (_LokiCutParams.x > 0.5)
        {
            float3 ab = _LokiCutB.xyz - _LokiCutA.xyz;
            float t = dot(wpos - _LokiCutA.xyz, ab) / max(dot(ab, ab), 1e-6);
            if (t > 0.0 && t < 1.0)
            {
                float r = _LokiCutParams.y * lerp(1.0, 0.3, t);
                clip(distance(wpos, _LokiCutA.xyz + ab * t) - r);
            }
        }
    }
}
#endif
