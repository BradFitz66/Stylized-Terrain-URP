Shader "Instanced/Grass" {
    Properties {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Diffuse ("Diffuse", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0,0,0,0)
        _DiffuseOffset ("Diffuse Offset", Float) = 0.5
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct DetailBuffer {
                float4x4 TRS;
                float3 normal;
                float normalOffset; //Unused  
            };

            CBUFFER_START(UnityPerMaterial)
                StructuredBuffer<DetailBuffer> _TerrainDetail;

                sampler2D _MainTex;

                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;

                float4 _Diffuse;
                float _DiffuseOffset;

                float4 _ShadowColor;
            CBUFFER_END

            struct Attributes
            {
                // The positionOS variable contains the vertex positions in object
                // space.
                float4 positionOS   : POSITION;          
                float3 normalOS     : NORMAL;
                float2 uv_MainTex   : TEXCOORD0;
            };

            struct Varyings
            {
                // The positions in this struct must have the SV_POSITION semantic.
                float4 positionHCS  : SV_POSITION;
                float4 positionWorld: TEXCOORD0;
                float3 normal       : TEXCOORD1;
                float2 uv_MainTex   : TEXCOORD2;
            };            

            Varyings vert (Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                
                float4x4 detailToWorld = _TerrainDetail[instanceID].TRS;
                float3 normal = _TerrainDetail[instanceID].normal;

                float3 scale = float3(length(detailToWorld[0].xyz), length(detailToWorld[1].xyz), length(detailToWorld[2].xyz));
                float4 worldPos = mul(detailToWorld, float4(0.0,0.0,0.0,1.0));
                float3 viewPos = TransformWorldToView(worldPos) + IN.positionOS.xyz * float3(scale.x,scale.y,1.0);
                float4 clipPos = TransformWViewToHClip(viewPos);

                OUT.positionHCS = clipPos;
                OUT.uv_MainTex = IN.uv_MainTex;
                OUT.positionWorld = worldPos;
                OUT.normal = normal;

                return OUT;
            }

            float ShadowAtten(float3 worldPosition)
            {
                    return MainLightRealtimeShadow(TransformWorldToShadowCoord(worldPosition));
            }

            float4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                float4 color = tex2D(_MainTex, IN.uv_MainTex) * _Diffuse;

                float nDotL = dot(mainLight.direction, IN.normal) + _DiffuseOffset;

                float Cookie = SampleMainLightCookie(IN.positionWorld);
                float atten = ShadowAtten(IN.positionWorld.xyz);

                float shadow = Cookie * min(nDotL, atten);
                float4 albedo = lerp(color,_ShadowColor,_ShadowColor.a);
                float4 diffuse = lerp(albedo,_Diffuse,shadow);

                float4 finalColor = diffuse * float4(mainLight.color.xyz,1.0);




                if (color.a < 0.1)
                {
                    discard;
                }

                color.rgb *= atten;

                return finalColor;
            }

            ENDHLSL
        }
    }
}