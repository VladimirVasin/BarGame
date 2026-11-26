Shader "Bar Promenade/Wet Shoe Print"
{
    Properties
    {
        _Opacity("Water", Range(0,1)) = .6
        _Mirror("Foot side", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor OneMinusSrcAlpha
            Cull Front
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Opacity;
            float _Mirror;
            CBUFFER_END
            struct Input { float4 position : POSITION; };
            struct Pixel { float4 position : SV_POSITION; };
            Pixel Vert(Input v)
            {
                Pixel o;
                o.position = TransformObjectToHClip(v.position.xyz);
                return o;
            }
            half4 Frag(Pixel i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.position);
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 local = TransformWorldToObject(world);
                clip(.5 - max(abs(local.x), max(abs(local.y), abs(local.z))));
                float3 normal = normalize(cross(ddy(world), ddx(world)));
                clip(abs(normal.y) - .8);
                float2 p = local.xy * 2;
                p.x *= _Mirror;
                float toe = length((p - float2(.04,.39)) / float2(.85,.56));
                float heel = length((p - float2(-.09,-.66)) / float2(.64,.29));
                float edge = min(toe, heel);
                float soft = max(.08, fwidth(edge));
                float outline = 1 - smoothstep(1-soft,1+soft,edge);
                // Shallow herringbone grooves interrupt the ball and heel of the print.
                float grooves = abs(frac(p.y * 4.5 - abs(p.x)*.48) - .5);
                float tread = lerp(.48, 1, smoothstep(.055,.14 + fwidth(grooves),grooves));
                return half4(0, 0, 0, _Opacity * outline * tread);
            }
            ENDHLSL
        }
    }
}
