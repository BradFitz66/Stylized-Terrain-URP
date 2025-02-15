Shader "Instanced/Grass" {
    Properties {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Diffuse ("Diffuse", Color) = (1,1,1,1)
    }
    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline" }
        Pass {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma target 4.5


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"            


            struct DetailBuffer {
                float4x4 TRS;
            };

            CBUFFER_START(UnityPerMaterial)
                StructuredBuffer<DetailBuffer> _TerrainDetail;

                sampler2D _MainTex;

                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;

                float4 _Diffuse;
            CBUFFER_END

            struct Attributes
            {
                // The positionOS variable contains the vertex positions in object
                // space.
                float4 positionOS   : POSITION;          
                float2 uv_MainTex   : TEXCOORD0;
            };

            struct Varyings
            {
                // The positions in this struct must have the SV_POSITION semantic.
                float4 positionHCS  : SV_POSITION;
                float2 uv_MainTex   : TEXCOORD0;
            };            


            Varyings vert (Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                float3 grassWS = _TerrainDetail[instanceID].TRS._m03_m13_m23;

                OUT.positionHCS = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_MV, float4(grassWS, 1.0)) + float4(IN.positionOS.x, IN.positionOS.y, 0.0, 0.0));

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                return tex2D(_MainTex, IN.uv_MainTex) * _Diffuse;
            }

            ENDHLSL
        }
    }
}