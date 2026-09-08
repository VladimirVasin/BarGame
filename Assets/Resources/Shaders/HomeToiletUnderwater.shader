Shader "Bar Promenade/Home Toilet Underwater"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "SubmergedOptics"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _BowlSubmersion;
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float amount = _BowlSubmersion.x;
                float clock = _BowlSubmersion.y;
                float2 uv = input.texcoord;
                float2 centred = uv - .5;
                float radius = length(centred);
                float flow = _BowlSubmersion.z * amount;
                // Camera rotation carries the whirl. This bounded tangential
                // refraction adds moving water without pulling black frame borders.
                float curl = sin(radius * 31 - _BowlSubmersion.w * 1.3) * .012 * flow;
                float2 tangent = float2(-centred.y, centred.x) / max(.05, radius);
                float2 ripple = float2(sin(uv.y * 21.0 + clock * 1.7),
                    cos(uv.x * 17.0 - clock * 1.3)) * (.0018 * amount);
                float2 edge = smoothstep(0, .035, uv) * smoothstep(0, .035, 1.0 - uv);
                uv += (ripple + tangent * curl) * edge.x * edge.y;
                float2 blur = _BlitTexture_TexelSize.xy * (1.7 * amount);
                half4 colour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * .6;
                colour += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + blur) * .2;
                colour += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - blur) * .2;
                float luminance = dot(colour.rgb, float3(.2126,.7152,.0722));
                colour.rgb = lerp(colour.rgb, lerp(colour.rgb, luminance.xxx, .12) *
                    float3(.78,.87,.81), amount);
                return colour;
            }
            ENDHLSL
        }
    }
}
