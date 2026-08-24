Shader "Bar Promenade/City Bus Glass Rain"
{
    Properties
    {
        _RainIntensity("Rain Intensity", Range(0, 1)) = 0
        _DropColor("Drop Color", Color) =
            (0.66, 0.74, 0.82, 0.55)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CityBusGlassRain"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RainVertex
            #pragma fragment RainFragment
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _RainIntensity;
                half4 _DropColor;
                // Wiper state, pushed per frame by the presentation in
                // THIS pane's own coordinates: xy = pivot in pane
                // metres, z = blade angle (radians, measured where the
                // visible blade tip actually is), w = sweep direction
                // sign. _WiperMask: x = inner radius, y = outer radius,
                // z = regrow angle, w = wipers running (0/1).
                float4 _WiperA;
                float4 _WiperB;
                float4 _WiperMask;
                float4 _BusForwardWS;
            CBUFFER_END

            float WipedFactor(
                float2 pane,
                float4 wiper,
                float innerRadius,
                float outerRadius,
                float regrowAngle)
            {
                float2 offset = pane - wiper.xy;
                float radius = length(offset);
                if (radius < innerRadius || radius > outerRadius)
                {
                    return 1.0;
                }

                float delta = atan2(offset.y, offset.x) - wiper.z;
                delta = delta - 6.2831853 *
                    floor((delta + 3.14159265) / 6.2831853);
                float behind = -delta * wiper.w;
                // Ahead of the blade the drops still wait for it;
                // behind it the glass starts clean and refills toward
                // the regrow angle.
                return behind <= 0.0
                    ? 1.0
                    : saturate(behind / regrowAngle);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // Pane coordinates in METRES, measured from the pane
                // object's own origin so the droplets ride along with
                // the moving bus: x runs across the pane, y climbs it.
                // Neither the imported object basis nor its unit scale
                // can be trusted (object Y runs along the bus and the
                // vertices are not in metres), so everything is derived
                // in world space and re-anchored to the object origin.
                float2 pane : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            };

            float Hash(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453);
            }

            Varyings RainVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;

                float3 originWS = float3(
                    UNITY_MATRIX_M._m03,
                    UNITY_MATRIX_M._m13,
                    UNITY_MATRIX_M._m23);
                float3 relativeWS =
                    positionInputs.positionWS - originWS;
                float3 normalWS = TransformObjectToWorldNormal(
                    input.normalOS);
                float3 acrossWS = cross(
                    normalWS,
                    float3(0.0, -1.0, 0.0));
                float acrossLength = length(acrossWS);
                acrossWS = acrossLength > 0.001
                    ? acrossWS / acrossLength
                    : float3(1.0, 0.0, 0.0);
                output.pane.x = dot(relativeWS, acrossWS);
                output.pane.y = relativeWS.y;
                output.normalWS = normalWS;

                float viewZ =
                    -TransformWorldToView(positionInputs.positionWS).z;
                output.fogFactor = ComputeFogFactorZ0ToFar(
                    max(viewZ - _ProjectionParams.y, 0));
                return output;
            }

            half4 RainFragment(Varyings input) : SV_Target
            {
                float u = input.pane.x;
                float v = input.pane.y;

                // Runners: sparse columns of drops sliding down the
                // glass. Each column owns a hashed phase, speed and
                // wobble; heavier rain wakes more columns. Sizes are
                // deliberately chunky — a PS1 pane reads a 4-8 pixel
                // trickle, not a hairline.
                const float ColumnWidth = 0.16;
                float column = floor(u / ColumnWidth);
                float seed = Hash(column);
                float active = step(
                    1.0 - (0.25 + _RainIntensity * 0.65),
                    Hash(column + 53.0));
                float speed = lerp(0.4, 1.1, Hash(column + 17.0));
                float w = frac(
                    v * 1.3 + seed * 9.0 + _Time.y * speed);
                float head = smoothstep(0.16, 0.0, w);
                float trail = pow(saturate(1.0 - w), 5.0) * 0.45;
                float wobble =
                    sin(v * 9.0 + seed * 21.0) * 0.012;
                float uCenter =
                    (column + 0.5) * ColumnWidth +
                    (Hash(column + 31.0) - 0.5) * 0.05 + wobble;
                float across = smoothstep(
                    0.045,
                    0.015,
                    abs(u - uCenter));
                float runner = across * active *
                    saturate(head + trail);

                // The windshield behind a running blade is freshly
                // squeegeed: suppress the drops in the wiped sector and
                // let them regrow toward the return stroke. Only panes
                // facing the bus's own forward carry wipers.
                half pattern = saturate(runner);
                if (_WiperMask.w > 0.5 &&
                    dot(normalize(input.normalWS),
                        _BusForwardWS.xyz) > 0.6)
                {
                    float wiped = min(
                        WipedFactor(
                            input.pane,
                            _WiperA,
                            _WiperMask.x,
                            _WiperMask.y,
                            _WiperMask.z),
                        WipedFactor(
                            input.pane,
                            _WiperB,
                            _WiperMask.x,
                            _WiperMask.y,
                            _WiperMask.z));
                    pattern *= wiped;
                }

                half alpha = pattern * _RainIntensity * _DropColor.a;
                half3 color = _DropColor.rgb * (1.0h + head * 0.5h);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
