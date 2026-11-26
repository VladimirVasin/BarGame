Shader "Bar Promenade/City Rain Contact"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+12" "RenderType" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RainVertex
            #pragma fragment RainFragment
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            #define BAR_PROMENADE_LOCAL_LIGHT_SHADOWS 1
            #include "AlpineVillageLocalLighting.hlsl"
            #include "CitySkyFill.hlsl"
            #include "CityValleyFog.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                half2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half2 uv : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
            };

            Varyings RainVertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                output.fog = ComputeFogFactorZ0ToFar(max(-TransformWorldToView(output.positionWS).z - _ProjectionParams.y, 0));
                return output;
            }

            half4 RainFragment(Varyings input) : SV_Target
            {
                half2 uv = input.uv;
                half3 tint = input.color.rgb;
                half alpha;
                if (uv.y > 2.0h)
                {
                    // Pool triangles encode local radius around the grate in the
                    // same batch as drops and jets. The highlight travels inward.
                    uv.y -= 4.0h;
                    half radius = length(uv);
                    half rim = .88h + .08h * sin(uv.x * 9.0h + uv.y * 4.0h) * cos(uv.y * 12.0h);
                    half coverage = 1.0h - smoothstep(rim * .64h, rim, radius);
                    half warp = sin(uv.x * 5.0h - uv.y * 3.0h) * .8h;
                    half wave = sin(radius * 23.0h + _Time.y * 4.5h + warp);
                    half crest = smoothstep(.53h, .97h, wave) * smoothstep(.17h, .78h, radius);
                    tint = lerp(tint, half3(.34h, .42h, .43h), crest * .46h);
                    alpha = coverage * input.color.a * (.78h + .15h * crest);
                }
                else
                {
                    half radius = dot(uv, uv);
                    alpha = (1.0h - smoothstep(.12h, 1.0h, radius)) * input.color.a;
                }
                half3 lighting = CitySkyFill() + AlpineLocalParticleLighting(input.positionWS, true);
                return half4(MixFog(tint * lighting,
                    CityValleyFogFactor(input.fog, input.positionWS.y)), alpha);
            }
            ENDHLSL
        }
    }
}
