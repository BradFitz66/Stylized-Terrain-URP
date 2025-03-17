Shader "Instanced/Grass" {
    Properties {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        [MainColor]_Diffuse ("Diffuse", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0,0,0,0)
        _DiffuseOffset ("Diffuse Offset", Float) = 0.5
        _WaveGradient ("Wave Gradient", 2D) = "white" {}
        [Toggle]_UseAtlas ("Use Atlas", Float) = 0
        _AtlasTiles ("Atlas Tiles", Integer) = 1
        _AtlasIndex ("Atlas Index", Integer) = 0
        _NoiseScale ("Noise Scale", Float) = 1
    }
    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline" }
        Cull Off
        ZWrite On
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

                bool _UseAtlas;
                float _AtlasTiles;
                float _AtlasIndex;

                float _NoiseScale;
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
                float  normalOffset : TEXCOORD2;
                float2 uv_MainTex   : TEXCOORD3;
                float  noise        : TEXCOORD4;
            };            

            float Unity_RandomRange_float(float2 Seed, float Min, float Max)
            {
                float randomno =  frac(sin(dot(Seed, float2(12.9898, 78.233)))*43758.5453);
                return lerp(Min, Max, randomno);
            }

            //Noise functions
            float2 hash22(float2 p)
            {
                p = frac(p * float2(5.3983, 5.4427));
                p += dot(p.yx, p.xy + float2(21.5351, 14.3137));
                return frac((p.x + p.y) * p.x * p.y);
            }
         
            inline float unity_noise_randomValue (float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453);
            }

            inline float unity_noise_interpolate (float a, float b, float t)
            {
                return (1.0-t)*a + (t*b);
            }

            inline float unity_valueNoise (float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);

                uv = abs(frac(uv) - 0.5);
                float2 c0 = i + float2(0.0, 0.0);
                float2 c1 = i + float2(1.0, 0.0);
                float2 c2 = i + float2(0.0, 1.0);
                float2 c3 = i + float2(1.0, 1.0);
                float r0 = unity_noise_randomValue(c0);
                float r1 = unity_noise_randomValue(c1);
                float r2 = unity_noise_randomValue(c2);
                float r3 = unity_noise_randomValue(c3);

                float bottomOfGrid = unity_noise_interpolate(r0, r1, f.x);
                float topOfGrid = unity_noise_interpolate(r2, r3, f.x);
                float t = unity_noise_interpolate(bottomOfGrid, topOfGrid, f.y);
                return t;
            }

            float Unity_SimpleNoise_float(float2 UV, float Scale)
            {
                float t = 0.0;

                float freq = pow(2.0, float(0));
                float amp = pow(0.5, float(3-0));
                t += unity_valueNoise(float2(UV.x*Scale/freq, UV.y*Scale/freq))*amp;

                freq = pow(2.0, float(1));
                amp = pow(0.5, float(3-1));
                t += unity_valueNoise(float2(UV.x*Scale/freq, UV.y*Scale/freq))*amp;

                freq = pow(2.0, float(2));
                amp = pow(0.5, float(3-2));
                t += unity_valueNoise(float2(UV.x*Scale/freq, UV.y*Scale/freq))*amp;

                return t;
            }

            float remap(float2 value, float low1, float high1, float low2, float high2)
            {
                return low2 + (value - low1) * (high2 - low2) / (high1 - low1);
            }

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


                float2 uv = IN.uv_MainTex;
                if (_UseAtlas)
                {
                    float2 randomValue = Unity_SimpleNoise_float(float2(worldPos.x,worldPos.z), _NoiseScale);
                    //Remap
                    randomValue = remap(randomValue, .5, 1, 0, _AtlasTiles);

                    //Clamp randomValue to 0-_AtlasTiles
                    randomValue = clamp(round(randomValue), 0, _AtlasTiles);
                    
                    OUT.noise = randomValue;

				    float2 size = float2(1.0f / _AtlasTiles, 1.0f / 1);
				    uint totalFrames = _AtlasTiles * 1;

				    uint index = randomValue;

				    uint indexX = index % _AtlasTiles;
				    uint indexY = 1;

				    float2 offset = float2(size.x*indexX, 0);

				    uv = uv*size;
                    uv += offset;
                }


                OUT.positionHCS = clipPos;
                OUT.uv_MainTex = uv;
                OUT.positionWS = worldPos;
                OUT.normal = _TerrainDetail[instanceID].normal;
                OUT.normalOffset = _TerrainDetail[instanceID].normalOffset;


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

                float4 color =  tex2D(_MainTex, IN.uv_MainTex) * _Diffuse;
                

                float shadow = toonRamp(Cookie * min(nDotL,atten),4,0.25,0);

                float4 shadowColor = lerp(color,_ShadowColor,_ShadowColor.a);

                float3 additionalLightDiffuse = float3(0,0,0);
                float3 additionalLightSpecular = float3(0,0,0);


                
                AdditionalLightsToon_float(
                    float3(1,1,1),
                    0.1,
                    worldPos,
                    IN.normal,
                    GetWorldSpaceNormalizeViewDir(worldPos),
                    float4(1.0,1.0,1.0,1.0),
                    4,
                    4,
                    additionalLightDiffuse,
                    additionalLightSpecular
                );

                half4 final = lerp(shadowColor,color,shadow) * float4(mainLight.color.rgb + additionalLightDiffuse,1.0);
                final *= float4(ambient,1.0);
                

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