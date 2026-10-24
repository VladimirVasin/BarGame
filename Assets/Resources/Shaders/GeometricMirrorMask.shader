Shader "Hidden/Bar Promenade/Geometric Mirror Mask"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Geometry-10"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        ColorMask 0
        ZWrite Off
        ZTest LEqual
        Cull Off

        Stencil
        {
            Ref 2
            WriteMask 2
            Comp Always
            Pass Replace
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 MirrorMaskVertex(float4 positionOS : POSITION) : SV_POSITION
        {
            return TransformObjectToHClip(positionOS.xyz);
        }

        half4 MirrorMaskFragment() : SV_Target
        {
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex MirrorMaskVertex
            #pragma fragment MirrorMaskFragment
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            HLSLPROGRAM
            #pragma vertex MirrorMaskVertex
            #pragma fragment MirrorMaskFragment
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex MirrorMaskVertex
            #pragma fragment MirrorMaskFragment
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            HLSLPROGRAM
            #pragma vertex MirrorMaskVertex
            #pragma fragment MirrorMaskFragment
            ENDHLSL
        }
    }

    Fallback Off
}
