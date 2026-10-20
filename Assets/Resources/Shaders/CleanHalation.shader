Shader "Hidden/BarPromenade/CleanHalation"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        TEXTURE2D_X_FLOAT(_CleanSceneDepth);
        TEXTURE2D_X(_CleanHalationTexture);
        float4 _CleanLightSources[24];
        float _CleanLightSourceStrengths[24];
        int _CleanLightSourceCount;
        float4 _CleanBlurDirection;
        ENDHLSL
        Pass
        {
            Name "VisibleSourceMask"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment SourceMask
            half4 SourceMask(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SAMPLE_TEXTURE2D_X_LOD(_CleanSceneDepth, sampler_PointClamp, uv, 0).r;
                float depth = unity_OrthoParams.w > .5 ? LinearDepthToEyeDepth(raw)
                    : LinearEyeDepth(raw, _ZBufferParams);
                float coverage = 0;
                [loop] for (int i = 0; i < _CleanLightSourceCount; i++)
                {
                    float4 source = _CleanLightSources[i];
                    // CPU snapshots GPU-correct centre, eye depth and squared
                    // radius once per camera; no world matrices in this loop.
                    float2 delta = uv - source.xy;
                    delta.x *= _ScreenParams.x / _ScreenParams.y;
                    float shape = saturate(1 - dot(delta, delta) / source.w);
                    coverage = max(coverage, shape * step(source.z - .22, depth) * _CleanLightSourceStrengths[i]);
                }
                half3 colour = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                half peak = max(colour.r, max(colour.g, colour.b));
                half knee = smoothstep(.2h, .6h, peak);
                return half4(colour * knee * coverage, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Diffusion"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Diffuse
            half4 Diffuse(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 d = _CleanBlurDirection.xy;
                half3 result = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0).rgb * .227027h;
                result += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord + d * 1.384615, 0).rgb * .316216h;
                result += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord - d * 1.384615, 0).rgb * .316216h;
                result += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord + d * 3.230769, 0).rgb * .070270h;
                result += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord - d * 3.230769, 0).rgb * .070270h;
                return half4(result, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "OpticalEdge"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Composite
            half4 Composite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 image = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                half3 glow = SAMPLE_TEXTURE2D_X_LOD(_CleanHalationTexture, sampler_LinearClamp, input.texcoord, 0).rgb;
                // Headroom limits the diffusion at the bright core and retains black elsewhere.
                image.rgb += min(glow * .45h, .16h) * saturate(1 - image.rgb);
                return image;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
