Shader "Instanced/Grass" {
    Properties {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Diffuse ("Diffuse", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0,0,0,0)
        _DiffuseOffset ("Diffuse Offset", Float) = 0.5
        _WaveGradient ("Wave Gradient", 2D) = "white" {}
    }
    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline" }
        Cull Off
        Pass {
            HLSLPROGRAM
            //Turn off backface culling

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.ducktor.stylizedterrain/Assets/Shaders/URP/CustomLighting.hlsl"

            struct DetailBuffer {
                float4x4 TRS;
                float3 normal;
                float normalOffset;
            };

            CBUFFER_START(UnityPerMaterial)
                StructuredBuffer<DetailBuffer> _TerrainDetail;

                sampler2D _MainTex;

                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;

                float4 _Diffuse;
                float _DiffuseOffset;

                float4 _ShadowColor;

                sampler2D _WaveGradient;
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
                float4 positionWS   : TEXCOORD0;
                float3 normal       : TEXCOORD1;
                float3 viewDir 	    : TEXCOORD2;
                float  normalOffset : TEXCOORD3;
                float2 uv_MainTex   : TEXCOORD4;
                float2 sample       : TEXCOORD5;
            };            

            Varyings vert (Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                
                float4x4 detailToWorld = _TerrainDetail[instanceID].TRS;

                //Vertex normal inputs
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);


                float3 scale = float3(length(detailToWorld[0].xyz), length(detailToWorld[1].xyz), length(detailToWorld[2].xyz));
                float4 worldPos = mul(detailToWorld, float4(0.0,0.0,0.0,1.0));

                float2 samplePos = worldPos.xz/scale.xz;
                samplePos.x += (_Time.x * 10);
                float windSample = tex2Dlod(_WaveGradient, float4(samplePos, 0, 0));
                float heightFactor = IN.positionOS.y > 0.25;

                float3 viewPos = TransformWorldToView(worldPos) + (IN.positionOS.xyz + sin(1.0*windSample)*heightFactor) * float3(scale.x,scale.y,1.0);
                float4 clipPos = TransformWViewToHClip(viewPos);


                //Wave the top vertices of the grass

                OUT.positionHCS = clipPos;
                OUT.uv_MainTex = IN.uv_MainTex;
                OUT.positionWS = worldPos;
                OUT.normal = _TerrainDetail[instanceID].normal;
                OUT.normalOffset = _TerrainDetail[instanceID].normalOffset;
                OUT.viewDir = GetWorldSpaceNormalizeViewDir(worldPos);


                return OUT;
            }

            float ShadowAtten(float3 WorldPos, float4 shadowMask)
            {
		        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
		        float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(WorldPos));
		        #else
		        float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
		        #endif
		        return MainLightShadow(shadowCoord, WorldPos, shadowMask, _MainLightOcclusionProbes);
            }

            float toonRamp(float lighting, int shades, float brightness, float minDarkness){
                float clampedLighting = lighting * shades;
                float plusBrightness = ceil(clampedLighting + brightness);
                float ramp = saturate( plusBrightness / shades);

                return lerp(minDarkness, 1.0, ramp);    
            }

            float4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                OUTPUT_LIGHTMAP_UV(lightmapUV, unity_LightmapST, lightmapUV);
                float4 Shadowmask = SAMPLE_SHADOWMASK(lightmapUV);

                float3 worldPos = IN.positionWS + float3(0,-IN.normalOffset,0);

                float Cookie = SampleMainLightCookie(worldPos);
                float atten = ShadowAtten(worldPos,Shadowmask);
                float nDotL = dot(mainLight.direction, IN.normal) + _DiffuseOffset;

                float3 ambient = float3(1,1,1);
                AmbientSampleSH_float(IN.normal, ambient);

                float4 color =  tex2D(_MainTex, IN.uv_MainTex) * (_Diffuse );
                

                float shadow = toonRamp(Cookie * min(nDotL,atten),4,0.25,0);

                float4 shadowColor = lerp(color,_ShadowColor,_ShadowColor.a);

                float3 additionalLightDiffuse = float3(0,0,0);
                float3 additionalLightSpecular = float3(0,0,0);


                
                AdditionalLightsToon_float(
                    float3(1,1,1),
                    0.1,
                    worldPos,
                    IN.normal,
                    IN.viewDir,
                    float4(1.0,1.0,1.0,1.0),
                    4,
                    4,
                    additionalLightDiffuse,
                    additionalLightSpecular
                );

                half4 final = lerp(shadowColor,color,shadow) * float4(mainLight.color.rgb + additionalLightDiffuse + ambient,1.0);
                



                if (color.a < 0.1)
                {
                    discard;
                }

                return final;
            }

            ENDHLSL
        }
    }
}