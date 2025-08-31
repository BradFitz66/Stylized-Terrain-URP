Shader "Custom/TerrainHandlesInstanced"
{
    Properties
    {
        _Color ("Color", Color) = (0,1,0,1)
    }
    SubShader
    {
        Tags { 
            "RenderPipeline" = "UniversalRenderPipeline" 
            "IgnoreProjector" = "True" 
            "Queue" = "Overlay" 
            "RenderType" = "Transparent"
        }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZTest Always
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }


            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            class TerrainHandle
            {
                float4x4 TRS;
                float heightOffset;
            };
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl" 
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                StructuredBuffer<TerrainHandle> _TerrainHandles;
                float _HeightOffset;
                float4 _Color;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            }; 

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert (appdata v, uint id : SV_InstanceID)
            {
                v2f o;
                float4x4 trs = _TerrainHandles[id].TRS;
                trs[1][3] += _HeightOffset;
                v.vertex = mul(trs, v.vertex);
                o.vertex = TransformObjectToHClip(v.vertex);
                o.worldPos = TransformObjectToWorld(v.vertex);
                o.normal = v.normal;
                return o;
            }
            
            float4 frag (v2f i) : SV_Target
            {
                float4 color = _Color;
                
                return color;
            }
            ENDHLSL
        }
    }
}