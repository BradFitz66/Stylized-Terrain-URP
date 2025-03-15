Shader "STE/Terrain" {
    Properties {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _RockColor("Rock Color", Color) = (1,1,1,1)
        _Diffuse ("Diffuse", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0,0,0,0)
        _DiffuseOffset ("Diffuse Offset", Float) = 0.5

        //Texture layers
        [HideInInspector]_Ground1("Texture 1", 2D) = "white" {}
        [HideInInspector]_Ground2("Texture 2", 2D) = "white" {}
        [HideInInspector]_Ground3("Texture 3", 2D) = "white" {}
        [HideInInspector]_Ground4("Texture 4", 2D) = "white" {}

        _RockNoise("Rock Noise", 3D) = "white" {}


    }
    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline" }
        Pass {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma target 4.5


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.ducktor.stylizedterrain/Assets/Shaders/URP/CustomLighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                sampler2D _MainTex;

                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;

                float4 _Diffuse;
                float _DiffuseOffset;

                float4 _RockColor;
                float4 _ShadowColor;

                sampler2D _Ground1;
                sampler2D _Ground2;
                sampler2D _Ground3;
                sampler2D _Ground4;

                sampler3D _RockNoise;
                float4 _RockNoise_ST;
            CBUFFER_END

            struct Attributes
            {
                // The positionOS variable contains the vertex positions in object
                // space.
                float4 positionOS   : POSITION;          
                float3 normalOS     : NORMAL;
                float2 uv_MainTex   : TEXCOORD0;
                float4 COLOR 	    : COLOR;
            };

            struct Varyings
            {
                // The positions in this struct must have the SV_POSITION semantic.
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 worldViewDir : TEXCOORD2;
                float2 uv_MainTex   : TEXCOORD3;
                float4 color        : COLOR;
            };            

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS.xyz);

                OUT.positionHCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.worldViewDir = GetWorldSpaceViewDir(positionInputs.positionWS);

                OUT.uv_MainTex = IN.uv_MainTex;
                OUT.color = IN.COLOR;


                return OUT;
            }

            float ShadowAtten(float3 worldPosition)
            {
                return MainLightRealtimeShadow(TransformWorldToShadowCoord(worldPosition));
            }

            float4 triplanar(float3 position, float3 normal, sampler2D t, float blend, float tile)
            {
	            float3 Node_UV = position * tile;
	            float3 Node_Blend = pow(abs(normal), blend);
	            Node_Blend /= dot(Node_Blend, 1.0);
	            float4 Node_X = tex2D(t, Node_UV.yz);
	            float4 Node_Y = tex2D(t, Node_UV.xz);
	            float4 Node_Z = tex2D(t, Node_UV.xy);
	            return Node_X * Node_Blend.x + Node_Y * Node_Blend.y + Node_Z * Node_Blend.z;
            }


            float4 TerrainLayer(
	            float3 position,
	            float3 normal,
	            float4 l,
	            sampler2D t,
	            float control,
	            float tiling
            )
            {
	            float4 color = triplanar(position,normal,t,1.0,1.0);

	            return lerp(l, color, control);
            }

            void GetLedgeValue(float2 UV, float ledgeTopThickness, float ledgeBottomThickness, float thicknessMult, out float ledgeTop, out float ledgeBottom){
                float topLedgeValue = UV.y - 1.0 + ledgeTopThickness * thicknessMult;
                bool topLedge = topLedgeValue > 0.01;
                float bottomLedgeValue = UV.x - 1.0 + ledgeBottomThickness * thicknessMult;
                bool bottomLedge = bottomLedgeValue > 0.01;

                ledgeTop = (float)topLedge;
                ledgeBottom = (float)bottomLedge;
            }

            inline float IsWall(float2 uv){
                float y = uv.y;

                float a = step(0.99,y);
                float b = step(0.99,y);

                return a * b;
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
                float rockNoise = tex3D(_RockNoise,IN.positionWS.xyz).r;
                float4 rockColor = _RockColor * rockNoise;
                float4 vertColor = step(rockNoise, IN.color);

                float bottomLedge = 0.0;
                float topLedge = 0.0;

                GetLedgeValue(IN.uv_MainTex, 0.5, 0.5, rockNoise, topLedge, bottomLedge);

                float sumEdge = topLedge + bottomLedge;

                float mask = clamp(sumEdge + IsWall(IN.uv_MainTex),0,1.0);



                float4 layer0 = float4(0.5,0.5,0.5,0.5);
                float4 layer1 = TerrainLayer(IN.positionWS.xyz,IN.normalWS.xyz,layer0,_Ground1,vertColor.r,1);
                float4 layer2 = TerrainLayer(IN.positionWS.xyz,IN.normalWS.xyz,layer1,_Ground2,vertColor.g,1);
                float4 layer3 = TerrainLayer(IN.positionWS.xyz,IN.normalWS.xyz,layer2,_Ground3,vertColor.b,1);
                float4 layer4 = TerrainLayer(IN.positionWS.xyz,IN.normalWS.xyz,layer3,_Ground4,vertColor.a,1);


                OUTPUT_LIGHTMAP_UV(lightmapUV, unity_LightmapST, lightmapUV);
                float4 Shadowmask = SAMPLE_SHADOWMASK(lightmapUV);

                float Cookie = SampleMainLightCookie(IN.positionWS);
                float atten = ShadowAtten(IN.positionWS,Shadowmask);
                float nDotL = dot(mainLight.direction, IN.normalWS) + _DiffuseOffset;

                float4 color = lerp(layer4,rockColor,mask) * _Diffuse;

                float shadow = toonRamp(Cookie * min(nDotL,atten),4,0.25,0);

                float4 shadowColor = lerp(color,_ShadowColor,_ShadowColor.a);


                float3 additionalLightDiffuse = float3(0,0,0);
                float3 additionalLightSpecular = float3(0,0,0);


                AdditionalLightsToon_float(
                    float3(1,1,1),
                    0.1,
                    IN.positionWS,
                    IN.normalWS,
                    IN.worldViewDir,
                    float4(1.0,1.0,1.0,1.0),
                    4,
                    4,
                    additionalLightDiffuse,
                    additionalLightSpecular
                );
                float3 ambient = float3(1,1,1);
                AmbientSampleSH_float(IN.normalWS, ambient);
                float4 final = lerp(shadowColor,color,shadow) * float4(mainLight.color.rgb + additionalLightDiffuse,1.0);
                final *= float4(ambient,1.0);


                return final;
            }

            ENDHLSL
        }
        Pass {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
 
            ZWrite On
            ColorMask 0
 
            HLSLPROGRAM
            // Required to compile gles 2.0 with standard srp library
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x gles
            //#pragma target 4.5
 
            // Material Keywords
            #pragma shader_feature _ALPHATEST_ON
            #pragma shader_feature _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
 
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
             
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
             
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
 
            // Again, using this means we also need _BaseMap, _BaseColor and _Cutoff shader properties
            // Also including them in cbuffer, except _BaseMap as it's a texture.
 
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
 
            ZWrite On
            ZTest LEqual
 
            HLSLPROGRAM
            // Required to compile gles 2.0 with standard srp library
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x gles
            //#pragma target 4.5
 
            // Material Keywords
            #pragma shader_feature _ALPHATEST_ON
            #pragma shader_feature _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
 
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
             
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
     
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
 
            ENDHLSL
        }
    }

}