Shader "Bar Promenade/Stairwell Distant Glow"
{
    // This is luminous air in front of the fully fogged far modules. Unlike
    // a lamp halo, applying distance fog to its colour would erase the light
    // itself. The opaque stairs still use ordinary depth fog independently.
    Properties
    {
        _GlowGain("Glow Gain", Float) = 2.4
        _DepthHazePlane("Owning staircase haze plane", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // Manual depth below retains nearby wall occlusion, while fully
            // extinguished stairs cannot cut holes in luminous air behind them.
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _GlowGain;
                float4 _DepthHazePlane;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float depth:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.depth = -TransformWorldToView(world).z;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p = input.uv * 2 - 1;
                // A broad, gently uneven envelope with no bright rim or disc.
                float r = length(p * float2(.94, 1.06));
                float envelope = pow(smoothstep(1, 0, r), 2.1);
                float air = .94 + .06 * sin(p.x * 7.3 + sin(p.y * 4.1) * 1.4);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(uv);
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float fade = saturate((depth - input.depth) / 3);
                float3 surface = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float haze = smoothstep(0, 1, saturate(dot(float4(surface, 1), _DepthHazePlane)));
                fade = lerp(fade, 1, haze);
                return half4(input.color.rgb * _GlowGain, input.color.a * envelope * air * fade);
            }
            ENDHLSL
        }
    }
}
