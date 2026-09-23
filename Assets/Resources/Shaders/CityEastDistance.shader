Shader "Bar Promenade/City East Distance"
{
    Properties
    {
        _HazeColor("Area Haze", Color) = (0.330, 0.380, 0.355, 1)
        _Tint("Surface", Color) = (0.12, 0.15, 0.14, 1)
        _Role("Panorama Surface Role", Float) = 0
        _DepthBandMeters("Far Depth Band (Metres)", Float) = 0.20
        _DepthWrite("Opaque Panorama Depth", Float) = 1
        _ViewDirection("View Hemisphere", Vector) = (1, 0, 0, 0)
        _Visibility("Area Visibility", Range(0, 1)) = 1
        _RockMap("Shared Weathered Stone", 2D) = "gray" {}
        _RoadMap("Shared Asphalt", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent-80" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "EastDistance"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_DepthWrite]
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DistanceVertex
            #pragma fragment DistanceFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_RockMap); SAMPLER(sampler_RockMap);
            TEXTURE2D(_RoadMap); SAMPLER(sampler_RoadMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                half4 _Tint;
                float _Role;
                float _DepthBandMeters;
                float _DepthWrite;
                float4 _ViewDirection;
                float _Visibility;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 normal : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DistanceVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float4 clipPosition = TransformWorldToHClip(world);
                // Keep true perspective W. A radial vertex warp also distorted
                // interpolation across broad terrain faces.
                // Only the final 20 cm of camera depth is used by the panorama.
                // Source depth orders its surfaces; nearer world depth wins.
                float4 band = mul(UNITY_MATRIX_P,
                    float4(0, 0, -(_ProjectionParams.z - _DepthBandMeters), 1));
                float sourceDepth = max(0.01, -TransformWorldToView(world).z);
                float fraction = 300.0 / (300.0 + sourceDepth);
                #if UNITY_REVERSED_Z
                    clipPosition.z = clipPosition.w * (band.z / band.w) * fraction;
                #else
                    clipPosition.z = clipPosition.w * (1.0 -
                        (1.0 - band.z / band.w) * fraction);
                #endif
                output.positionCS = clipPosition;
                output.uv = input.uv;
                output.world = world;
                output.normal = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DistanceFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 ray = input.world - _WorldSpaceCameraPos;
                clip(dot(ray, _ViewDirection.xyz));
                float d = length(ray);
                half3 normal = normalize(input.normal);
                if (_Role > 13.5)
                {
                    // Both the lens haze and the pool use source-space depth,
                    // just like their mast and road. Ordinary particle halos
                    // would be clipped at 48 m or appear over nearer slopes.
                    float2 centered = input.uv * 2.0 - 1.0;
                    float radius = dot(centered, centered);
                    float soft = exp(-radius * 4.5) * (1.0 - smoothstep(0.55, 1.0, radius));
                    float reach = exp(-d / (_Role > 14.5 ? 1100.0 : 2400.0));
                    float alpha = soft * reach * (_Role > 14.5 ? 0.23 : 0.12);
                    return half4(_Tint.rgb, alpha * _Visibility);
                }
                if (_Role > 4.5 && _Role < 5.5)
                {
                    // Broad light pollution in the existing haze: overlapping
                    // district pools, with no bright strip, pulsing or new fog.
                    // Density is authored-space, so it cannot swim with a turn.
                    float x = input.uv.x * 2.0 - 1.0;
                    float height = saturate(input.uv.y);
                    float centre = exp(-pow((x + 0.07) / 0.42, 2.0));
                    float west = exp(-pow((x + 0.53) / 0.24, 2.0));
                    float east = exp(-pow((x - 0.46) / 0.31, 2.0));
                    float pools = centre + west * 0.34 + east * 0.47;
                    float sides = smoothstep(0.0, 0.13, input.uv.x) *
                        (1.0 - smoothstep(0.87, 1.0, input.uv.x));
                    float rise = smoothstep(0.0, 0.15, height);
                    float ceiling = pow(1.0 - height, 2.2);
                    float grain = 0.90 + 0.065 * sin(x * 18.0 + height * 9.0) +
                        0.035 * sin(x * 31.0 - height * 16.0);
                    float density = pools * sides * rise * ceiling * grain;
                    return half4(_Tint.rgb, saturate(density * 0.42) * _Visibility);
                }

                half3 surface = _Tint.rgb;
                half visibility;
                if (_Role > 12.5)
                {
                    visibility = 0.28h * exp(-d / 2400.0);
                }
                else if (_Role > 11.5)
                {
                    visibility = exp(-pow(d * 0.070, 2.0)) +
                        0.22h * smoothstep(35.0, 130.0, d) * exp(-d / 850.0);
                }
                else if (_Role > 9.5)
                {
                    // Emissive lenses face along the car. No realtime Light.
                    half facing = saturate(dot(normal, normalize(-ray)));
                    visibility = (0.22h + 0.60h * facing) * exp(-d / 2600.0);
                }
                else if (_Role > 7.5)
                {
                    visibility = 0.48h * exp(-d / 3300.0) + 0.06h;
                    surface *= 0.82h + 0.18h * saturate(normal.y);
                }
                else if (_Role > 5.5 || _Role < 0.5)
                {
                    half slopeLight = 0.62h + 0.62h * saturate(dot(normal,
                        normalize(half3(-0.35h, 0.8h, -0.48h))));
                    float2 rockUv = abs(normal.y) > 0.60h ? input.world.xz / 18.0 :
                        (abs(normal.x) > abs(normal.z) ? input.world.zy : input.world.xy) / 18.0;
                    half grain = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, rockUv).r;
                    half grainStrength = 0.28h * exp(-d / 700.0);
                    surface *= slopeLight * lerp(1.0h, 0.65h + grain, grainStrength);
                    visibility = 0.28h * exp(-d / 2300.0) + 0.035h;
                }
                else if (_Role > 3.5)
                {
                    // Constant warm rooms, softened into the same shared haze.
                    visibility = 0.32h;
                }
                else if (_Role > 2.5)
                {
                    half height = smoothstep(0.02h, 0.95h, saturate(input.uv.y));
                    visibility = lerp(0.055h, 0.245h, height);
                    surface *= 0.9h + 0.1h * saturate(normal.y);
                }
                else
                {
                    // The first decorative metres meet the real asphalt in
                    // its existing fog. Material detail and paint dissolve
                    // continuously into the broad distant road ribbon.
                    half nearVisibility = exp(-pow(d * 0.070, 2.0));
                    half distantVisibility = (_Role > 1.5 ? 0.50h : 0.24h) * exp(-d / 3000.0) + 0.045h;
                    visibility = max(nearVisibility,
                        distantVisibility * (_Role > 1.5 ? smoothstep(32.0, 100.0, d) : smoothstep(35.0, 160.0, d)));
                    if (_Role > 1.5)
                    {
                        half grain = SAMPLE_TEXTURE2D(_RoadMap, sampler_RoadMap,
                            float2(input.uv.y, input.uv.x * 6.0) / 12.0).r;
                        surface *= lerp(1.0h, 0.65h + grain, exp(-d / 180.0));
                        float u = input.uv.x;
                        float aa = max(fwidth(u), 0.001);
                        float centre = 1.0 - smoothstep(0.012, 0.012 + aa, abs(u - 0.5));
                        float edges = 1.0 - smoothstep(0.012, 0.012 + aa,
                            min(abs(u - 0.055), abs(u - 0.945)));
                        float dash = 1.0 - smoothstep(0.42, 0.46, frac(input.uv.y / 6.0));
                        float paint = max(edges, centre * dash) * exp(-d / 450.0);
                        surface = lerp(surface, half3(0.55, 0.54, 0.41), paint);
                    }
                }
                return half4(lerp(_HazeColor.rgb, surface, visibility * _Visibility), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
