#ifndef ROAD_SNOW_INCLUDED
#define ROAD_SNOW_INCLUDED

// Snow trail from https://steveimm.id/posts/snow-trail/
// Simple noise builds a height pile. The snow mask is white on untouched snow and
// black where the sled has passed, so the pile multiplies back down to the road.
// Same role as the tutorial render texture, stored in spline space instead of a
// top-down camera (the track is a long ribbon, not a flat plane).

TEXTURE2D(_SnowMap);
SAMPLER(sampler_SnowMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _SnowColor;
    float _SnowDepth;
    float _NoiseScale;
    float _NoisePower;
    float _NoiseStrength;
CBUFFER_END

float SnowHash(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

float SnowValueNoise(float2 uv)
{
    float2 cell = floor(uv);
    float2 blend = frac(uv);
    blend = blend * blend * (3.0 - 2.0 * blend);

    float r0 = SnowHash(cell);
    float r1 = SnowHash(cell + float2(1.0, 0.0));
    float r2 = SnowHash(cell + float2(0.0, 1.0));
    float r3 = SnowHash(cell + float2(1.0, 1.0));

    float bottom = lerp(r0, r1, blend.x);
    float top = lerp(r2, r3, blend.x);
    return lerp(bottom, top, blend.y);
}

// Wide lumps only. The finest octave is smaller than the road mesh, so it costs
// a vertex texture-worth of hash work and never shows up as extra shape.
float SnowSimpleNoise(float2 uv, float scale)
{
    float sum = 0.0;
    float frequency = 2.0;
    float amplitude = 0.25;
    [unroll]
    for (int octave = 0; octave < 2; octave++)
    {
        sum += SnowValueNoise(uv * (scale / frequency)) * amplitude;
        frequency *= 2.0;
        amplitude *= 2.0;
    }

    // Match the old three-octave peak so drift height stays put.
    return sum * (0.875 / 0.75);
}

// Mesh vertex color is a coarse copy baked at rebuild. It lags the trail and fades
// across the ice-to-snow edge, so the mask texture is the only sample.
// Mip 0 of this mask is thousands of texels tall. Every vertex of a long view would
// fetch a unique texel and the GPU stalls once the side hills come into frame.
// The color of the trail is the fragment sample; displacement only needs the coarse mip.
float SnowMaskAt(float2 snowUV)
{
    return SAMPLE_TEXTURE2D_LOD(_SnowMap, sampler_SnowMap, snowUV, 3).r;
}

// Meters to push the vertex along the road normal. Zero on the trail and at the walls.
float SnowPile(float3 positionWS, float2 parametric, float snow, out float noise)
{
    noise = SnowSimpleNoise(positionWS.xz, _NoiseScale);
    float shaped = pow(saturate(noise), max(_NoisePower, 0.001));
    // parametric.x is 0 at the left wall and 1 at the right. Keep the lip on the wall.
    float edge = smoothstep(0.0, 0.045, min(parametric.x, 1.0 - parametric.x));
    return shaped * _NoiseStrength * _SnowDepth * snow * edge;
}

float3 DisplaceSnow(float3 positionOS, float3 normalOS, float2 snowUV, out float3 normalWS, out float noise)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    normalWS = TransformObjectToWorldNormal(normalOS);
    float lengthSq = dot(normalWS, normalWS);
    normalWS = lengthSq > 1e-8 ? normalWS * rsqrt(lengthSq) : float3(0.0, 1.0, 0.0);

    float height = SnowPile(positionWS, snowUV, SnowMaskAt(snowUV), noise);
    return positionWS + normalWS * height;
}

#endif
