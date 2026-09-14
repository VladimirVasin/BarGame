Shader "Bar Promenade/City East Distance"
{
    Properties
    {
        _HazeColor("Shared City Haze", Color) = (0.330, 0.380, 0.355, 1)
        _Tint("Surface", Color) = (0.12, 0.15, 0.14, 1)
        _Role("Land, Shoulder, Road, City, Windows, Glow", Float) = 0
        _ProjectionRadius("Presentation Radius", Float) = 44
    }
    SubShader
    {
        Tags { "Queue"="Transparent-80" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "EastDistance"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DistanceVertex
            #pragma fragment DistanceFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                half4 _Tint;
                float _Role;
                float _ProjectionRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float distance : TEXCOORD1;
                float east : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DistanceVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 ray = world - _WorldSpaceCameraPos;
                float distance = max(length(ray), 0.001);
                // Preserve the authored angular perspective and real camera
                // translation. Only depth is compressed; the city cannot
                // follow a turn of the head or swell as the hero approaches.
                float radius = min(distance, _ProjectionRadius - 6.0 +
                    6.0 * distance / (distance + 300.0));
                float3 projected = _WorldSpaceCameraPos + ray * (radius / distance);
                output.positionCS = TransformWorldToHClip(projected);
                // Projection changes angular presentation, never foreground
                // ownership. Testing at the compressed radius made distant
                // land cut through the real road/yard as the camera moved.
                #if UNITY_REVERSED_Z
                    output.positionCS.z = output.positionCS.w * 0.000001;
                #else
                    output.positionCS.z = output.positionCS.w * 0.999999;
                #endif
                output.uv = input.uv;
                output.distance = distance;
                output.east = ray.x;
                return output;
            }

            half4 DistanceFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // The skyline is only east, beyond the local boundary. It
                // never draws across the open northern sea or behind a turn.
                clip(input.east);
                if (_Role > 4.5)
                {
                    // A few warm atmospheric pools behind the skyline,
                    // with no bright strip or hard rectangular boundary.
                    half sides = pow(saturate(sin(input.uv.x * PI)), 2.0);
                    half height = saturate(input.uv.y);
                    half fade = smoothstep(0.0, 0.12, height) * pow(1.0 - height, 1.8);
                    return half4(_Tint.rgb, sides * fade * 0.28h);
                }
                float d = input.distance;
                half localVisibility = exp(-pow(d * 0.070, 2.0));
                half visibility;
                if (_Role > 3.5)
                    visibility = 0.72h;
                else if (_Role > 2.5)
                    visibility = lerp(0.10h, 0.42h, saturate(input.uv.y));
                else if (_Role > 1.5)
                    visibility = max(localVisibility, 0.22h * exp(-d / 6500.0));
                else if (_Role > 0.5)
                    visibility = max(localVisibility, 0.12h * exp(-d / 4200.0));
                else
                    visibility = max(localVisibility, 0.105h * exp(-d / 7000.0));
                // A bounded amount of the same atmospheric colour replaces
                // a second Exp2 application. The ordinary City fog is intact.
                half3 colour = lerp(_HazeColor.rgb, _Tint.rgb, visibility);
                return half4(colour, _Tint.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
