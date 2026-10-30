Shader "BarPromenade/WorldControlOutline"
{
    Properties { [HideInInspector] _ControlCenterOS("Control Center", Vector) = (0, 0, 0, 0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+20" "RenderType"="Opaque" }
        Pass
        {
            Name "ControlContour"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ControlCenterOS;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS));
                float4 centreCS = TransformObjectToHClip(_ControlCenterOS.xyz);
                float2 radial = output.positionCS.xy / max(output.positionCS.w, 0.0001) -
                    centreCS.xy / max(centreCS.w, 0.0001);
                float2 direction = dot(normalVS.xy, normalVS.xy) > 0.0001 ? normalVS.xy : radial;
                direction /= max(length(direction), 0.0001);
                output.positionCS.xy += direction * (4.0 / _ScaledScreenParams.xy) * output.positionCS.w;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(1.0, 0.72, 0.04, 1.0); }
            ENDHLSL
        }
    }
}
