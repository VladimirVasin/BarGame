Shader "Bar Promenade/City Street View Range"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 raySlope : TEXCOORD0;
            };
            Varyings Vert(uint vertexId : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexId);
                output.raySlope = (output.positionCS.xy + float2(UNITY_MATRIX_P._m02, UNITY_MATRIX_P._m12))
                    / float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11);
                return output;
            }
            half4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                // A ray reaches the street radius at a smaller eye depth in the corners.
                float eyeDepth = _ProjectionParams.z * rsqrt(1.0 + dot(input.raySlope, input.raySlope));
                depth = saturate((rcp(eyeDepth) - _ZBufferParams.w) / _ZBufferParams.z);
                return half4(unity_FogColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
