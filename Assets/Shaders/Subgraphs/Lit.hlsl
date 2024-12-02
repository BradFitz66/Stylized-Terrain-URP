#ifndef FUR_SHELL_LIT_HLSL
#define FUR_SHELL_LIT_HLSL

#include "Packages/com.deltation.toon-rp/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 tangentWS : TEXCOORD2;
    float2 uv : TEXCOORD4;
    float4 fogFactorAndVertexLight : TEXCOORD6; // x: fogFactor, yzw: vertex light
    float  layer : TEXCOORD7;
};

Attributes vert(Attributes input)
{
    return input;
}

void AppendShellVertex(inout TriangleStream<Varyings> stream, Attributes input, int index)
{
    Varyings output = (Varyings)0;


    stream.Append(output);
}

[maxvertexcount(42)]
void geom(triangle Attributes input[3], inout TriangleStream<Varyings> stream)
{
    [loop] for (float i = 0; i < 4; ++i)
    {
        [unroll] for (float j = 0; j < 3; ++j)
        {
            AppendShellVertex(stream, input[j], i);
        }
        stream.RestartStrip();
    }
}

inline float3 TransformHClipToWorld(float4 positionCS)
{
    return mul(UNITY_MATRIX_I_VP, positionCS).xyz;
}

float4 frag(Varyings input) : SV_Target
{

    return float4(1, 0, 0, 1);
}

#endif
