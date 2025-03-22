#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
#include "Packages/com.ducktor.stylizedterrain/Assets/Shaders/URP/CustomLighting.hlsl"
float ShadowAtten(float3 WorldPos, float4 shadowMask)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(WorldPos));
#else
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
#endif
    return MainLightShadow(shadowCoord, WorldPos, shadowMask, _MainLightOcclusionProbes);
}


struct CloudNoiseSettings {
	float Scale;
	float2 Speed;
	float Coverage;
    float VerticalSpeed;
    float Brightness;
};

float2 ShadowProjection(float3 pos, float3 sunDir) {
    float mult = pos.y / -sunDir.y;
    float3 offset = mult * sunDir;
    return float2(pos.x + offset.x, pos.z + offset.z);
}

float3 mod289(float3 x)
{
    return x - floor(x / 289.0) * 289.0;
}

float4 mod289(float4 x)
{
    return x - floor(x / 289.0) * 289.0;
}

float4 taylorInvSqrt(float4 r)
{
    return (float4)1.79284291400159 - r * 0.85373472095314;
}

float3 taylorInvSqrt(float3 r)
{
    return 1.79284291400159 - 0.85373472095314 * r;
}

float4 permute(float4 x)
{
    return mod289(((x * 34.0) + 1.0) * x);
}

float3 permute(float3 x)
{
    return mod289((x * 34.0 + 1.0) * x);
}

float snoise(float3 v)
{
    const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);

    // First corner
    float3 i = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);

    // Other corners
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);

    // x1 = x0 - i1  + 1.0 * C.xxx;
    // x2 = x0 - i2  + 2.0 * C.xxx;
    // x3 = x0 - 1.0 + 3.0 * C.xxx;
    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - 0.5;

    // Permutations
    i = mod289(i); // Avoid truncation effects in permutation
    float4 p =
        permute(permute(permute(i.z + float4(0.0, i1.z, i2.z, 1.0))
            + i.y + float4(0.0, i1.y, i2.y, 1.0))
            + i.x + float4(0.0, i1.x, i2.x, 1.0));

    // Gradients: 7x7 points over a square, mapped onto an octahedron.
    // The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
    float4 j = p - 49.0 * floor(p / 49.0);  // mod(p,7*7)

    float4 x_ = floor(j / 7.0);
    float4 y_ = floor(j - 7.0 * x_);  // mod(j,N)

    float4 x = (x_ * 2.0 + 0.5) / 7.0 - 1.0;
    float4 y = (y_ * 2.0 + 0.5) / 7.0 - 1.0;

    float4 h = 1.0 - abs(x) - abs(y);

    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);

    //float4 s0 = float4(lessThan(b0, 0.0)) * 2.0 - 1.0;
    //float4 s1 = float4(lessThan(b1, 0.0)) * 2.0 - 1.0;
    float4 s0 = floor(b0) * 2.0 + 1.0;
    float4 s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, 0.0);

    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

    float3 g0 = float3(a0.xy, h.x);
    float3 g1 = float3(a0.zw, h.y);
    float3 g2 = float3(a1.xy, h.z);
    float3 g3 = float3(a1.zw, h.w);

    // Normalise gradients
    float4 norm = taylorInvSqrt(float4(dot(g0, g0), dot(g1, g1), dot(g2, g2), dot(g3, g3)));
    g0 *= norm.x;
    g1 *= norm.y;
    g2 *= norm.z;
    g3 *= norm.w;

    // Mix final noise value
    float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    m = m * m;

    float4 px = float4(dot(x0, g0), dot(x1, g1), dot(x2, g2), dot(x3, g3));
    return 42.0 * dot(m, px);
}

float toonRamp(float lighting, int shades, float brightness, float minDarkness) {
    float clampedLighting = lighting * shades;
    float plusBrightness = ceil(clampedLighting + brightness);
    float ramp = saturate(plusBrightness / shades);

    return lerp(minDarkness, 1.0, ramp);
}
float Cloud(float2 UV, float Scale, float VerticalSpeed, float2 Step, float Coverage, float Time)
{
    // FBX Calculations (Lacunarity, Octaves, Amplitude)
    float n = snoise(float3(UV * Scale, Time * VerticalSpeed));
    n += 0.5 * snoise(float3((UV * 2.0 - Step) * Scale, Time * VerticalSpeed));
    n += 0.25 * snoise(float3((UV * 4.0 - 2.0 * Step) * Scale, Time * VerticalSpeed));
    n += 0.125 * snoise(float3((UV * 8.0 - 3.0 * Step) * Scale, Time * VerticalSpeed));
    n += 0.0625 * snoise(float3((UV * 16.0 - 4.0 * Step) * Scale, Time * VerticalSpeed));
    n += 0.03125 * snoise(float3((UV * 32.0 - 5.0 * Step) * Scale, Time * VerticalSpeed));

    return Coverage + 0.5 * n;
}

float4 ToonLighting(float4 Albedo, float4 ShadowColor, float3 Normal, float DiffuseOffset, float3 WorldPos, uint PointLightBands,uint SpotLightBands, CloudNoiseSettings CloudSettings) {
    Light mainLight = GetMainLight();
    OUTPUT_LIGHTMAP_UV(lightmapUV, unity_LightmapST, lightmapUV);
    float4 Shadowmask = SAMPLE_SHADOWMASK(lightmapUV);


    float3 ambient = float3(1, 1, 1);
    AmbientSampleSH_float(Normal, ambient);

    float Clouds = Cloud(
        ShadowProjection(WorldPos, mainLight.direction) + CloudSettings.Speed * _Time.x,
        CloudSettings.Scale,
        CloudSettings.VerticalSpeed,
        float2(0, 0),
        CloudSettings.Coverage,
        _Time.y
    ) + CloudSettings.Brightness;
    float atten = ShadowAtten(WorldPos, Shadowmask);
    float nDotL = dot(mainLight.direction, Normal) + DiffuseOffset;

    float shadow = toonRamp(min(nDotL, min(Clouds,atten)), 4, 0, 0);

    float4 diffuseLighting = Albedo * float4(mainLight.color * ambient,1);

    float4 shadowColor = lerp(diffuseLighting, ShadowColor, ShadowColor.a);

    float3 additionalLightDiffuse = float3(0, 0, 0);
    float3 additionalLightSpecular = float3(0, 0, 0);

    

    AdditionalLightsToon_float(
        float3(1, 1, 1),
        0.1,
        WorldPos,
        Normal,
        GetWorldSpaceNormalizeViewDir(WorldPos),
        float4(1.0, 1.0, 1.0, 1.0),
        PointLightBands,
        SpotLightBands,
        additionalLightDiffuse,
        additionalLightSpecular
    );
    

    additionalLightDiffuse *= Albedo;


	return lerp(shadowColor, diffuseLighting, shadow) + float4(additionalLightDiffuse, 1);

}
