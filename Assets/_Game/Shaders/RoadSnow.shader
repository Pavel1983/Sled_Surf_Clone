Shader "SledSurf/RoadSnow"
{
    Properties
    {
        [MainTexture] _BaseMap ("Road", 2D) = "white" {}
        [MainColor] _BaseColor ("Road Tint", Color) = (1, 1, 1, 1)
        _SnowMap ("Snow Mask", 2D) = "white" {}
        _SnowColor ("Snow", Color) = (0.96, 0.97, 1, 1)
        _SnowDepth ("Snow Depth", Range(0, 2)) = 1
        _NoiseScale ("Noise Scale", Range(0.05, 8)) = 1.6
        _NoisePower ("Noise Power", Range(0.2, 4)) = 1.46
        _NoiseStrength ("Noise Strength", Range(0, 1.5)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Assets/_Game/Shaders/RoadSnow.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 parametric : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 snowUV : TEXCOORD1;
                float fogCoord : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float3 normalWS : TEXCOORD5;
                half shape : TEXCOORD6;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalWS;
                float noise;
                float3 positionWS = DisplaceSnow(input.positionOS.xyz, input.normalOS, input.parametric, normalWS, noise);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.snowUV = input.parametric;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.shape = saturate(noise * 1.15);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 ice = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                // The mask texture is updated while sliding. Baked vertex color lags that trail.
                half snow = SAMPLE_TEXTURE2D(_SnowMap, sampler_SnowMap, input.snowUV).r;

                float3 geometricNormal = normalize(input.normalWS);
                float shape = input.shape;
                float3 snowNormal = geometricNormal;

                float3 lightDir = _MainLightPosition.xyz;
                if (dot(lightDir, lightDir) < 1e-4)
                    lightDir = float3(0.35, 0.86, 0.28);
                lightDir = normalize(lightDir);
                float3 lightColor = float3(1.0, 0.98, 0.94);

                float wrap = saturate(dot(snowNormal, lightDir) * 0.55 + 0.45);
                float hemi = saturate(snowNormal.y * 0.5 + 0.5);
                float3 ambient = lerp(float3(0.48, 0.54, 0.62), float3(0.9, 0.94, 1.0), hemi);
                float3 snowLit = _SnowColor.rgb * lerp(0.78, 1.0, shape);
                snowLit *= ambient * 0.62 + lightColor * wrap * 0.5;

                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfDir = normalize(lightDir + viewDir);
                float spec = pow(saturate(dot(snowNormal, halfDir)), 28.0);
                snowLit += lightColor * spec * 0.22 * snow;

                float edge = saturate(length(float2(ddx(snow), ddy(snow))) * 6.0);
                half3 color = lerp(ice, snowLit, snow);
                color *= lerp(1.0, 0.84, edge * (1.0 - snow));
                color = MixFog(color, input.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/_Game/Shaders/RoadSnow.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 parametric : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                float3 normalWS;
                float noise;
                float3 positionWS = DisplaceSnow(input.positionOS.xyz, input.normalOS, input.parametric, normalWS, noise);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Assets/_Game/Shaders/RoadSnow.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 parametric : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                float3 normalWS;
                float noise;
                float3 positionWS = DisplaceSnow(input.positionOS.xyz, input.normalOS, input.parametric, normalWS, noise);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                Varyings output;
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
