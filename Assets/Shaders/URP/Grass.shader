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

        _SpotLightBands ("Spot Light Bands", Integer) = 4
        _PointLightBands ("Point Light Bands", Integer) = 4

        _QueueOffset ("Queue Offset", Float) = 0

        // [HideInInspector]_CloudScale ("Cloud Scale", Float) = 1.0
        // [HideInInspector]_CloudSpeedX ("Cloud Speed X", Float) = 1.0
        // [HideInInspector]_CloudSpeedY ("Cloud Speed Y", Float) = 1.0
        // [HideInInspector]_CloudDensity ("Cloud Density", Float) = 1.0
        // [HideInInspector]_CloudBrightness ("Cloud Gradient", Float) = 1.0
        // [HideInInspector]_CloudVerticalSpeed ("Cloud Gradient", Float) = 1.0


    }
    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline"}
        Cull Off
        Pass {
            HLSLPROGRAM

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
            #include "Packages/com.ducktor.stylizedterrain/Assets/Shaders/URP/Lighting.hlsl"

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

                int _SpotLightBands;
                int _PointLightBands;

                float _CloudScale;
                float _CloudSpeedX;
                float _CloudSpeedY;
                float _CloudDensity;
                float _CloudBrightness;
                float _CloudVerticalSpeed;
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

            float4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                OUTPUT_LIGHTMAP_UV(lightmapUV, unity_LightmapST, lightmapUV);
                float4 Shadowmask = SAMPLE_SHADOWMASK(lightmapUV);

                float3 worldPos = IN.positionWS + float3(0,-IN.normalOffset,0);
                float4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Diffuse;

                CloudNoiseSettings cloudSettings;
                cloudSettings.Scale = _CloudScale;
                cloudSettings.Speed = float2(_CloudSpeedX, _CloudSpeedY);
                cloudSettings.Coverage = _CloudDensity;
                cloudSettings.Brightness = _CloudBrightness;
                cloudSettings.VerticalSpeed = _CloudVerticalSpeed;

                float4 lighting = ToonLighting(
                  albedo,
                  _ShadowColor,
                  IN.normal,
                  _DiffuseOffset,
                  worldPos,
                  _PointLightBands,
                  _SpotLightBands,
                  cloudSettings
                );
                
                if (albedo.a < 0.1)
                {
                    discard;
                }

                return lighting;
            }
            ENDHLSL
        }
    }
}