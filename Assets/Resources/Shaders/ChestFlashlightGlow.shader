Shader "Bar Promenade/Chest Flashlight Glow"
{
    Properties
    {
        [HDR] _GlowColor("Source Color", Color) = (3.2, 2.9, 2.3, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SourceVertex
            #pragma fragment SourceFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Ps1VertexJitter.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 centerOS : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                float4 centerScreen : TEXCOORD2;
                float centerEyeDepth : TEXCOORD3;
            };
            Varyings SourceVertex(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 center = TransformObjectToWorld(input.centerOS);
                output.positionCS = Ps1SnapClipPosition(TransformWorldToHClip(world));
                output.centerScreen = ComputeScreenPos(TransformWorldToHClip(center));
                output.eyeDepth = -TransformWorldToView(world).z;
                output.centerEyeDepth = -TransformWorldToView(center).z;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 SourceFragment(Varyings input) : SV_Target
            {
                half radius = length(input.uv * 2.0h - 1.0h);
                half envelope = saturate(1.0h - radius);
                envelope = envelope * envelope * (3.0h - 2.0h * envelope);
                envelope *= envelope;
                clip(envelope * input.color.a - .0001h);

                // The whole source vanishes when a hand or a wall covers its
                // centre; ordinary per-pixel depth alone leaves a false rim.
                float2 centerUv = input.centerScreen.xy / input.centerScreen.w;
                float sourceDepth = LinearEyeDepth(SampleSceneDepth(centerUv), _ZBufferParams);
                clip(sourceDepth - input.centerEyeDepth + .002);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(
                    GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
                half depthFade = saturate((sceneDepth - input.eyeDepth + .001) / .006);
                half alpha = input.color.a * envelope * depthFade * _GlowColor.a;
                return half4(_GlowColor.rgb * input.color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
