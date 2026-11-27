Shader "Bar Promenade/Ravine mirror glass"
{
    Properties { _BridgeView ("Window view", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BridgeView);
            SAMPLER(sampler_BridgeView);
            CBUFFER_START(UnityPerMaterial)
                float4 _ViewRight, _ViewUp, _ViewForward, _PaneNormal;
            CBUFFER_END
            struct Input { float3 pos : POSITION; };
            struct Varyings { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };
            Varyings Vert(Input v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.pos);
                o.pos = TransformWorldToHClip(o.world);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 ray = reflect(normalize(i.world - GetCameraPositionWS()), normalize(_PaneNormal.xyz));
                float depth = dot(ray,_ViewForward.xyz);
                float2 projected = float2(dot(ray,_ViewRight.xyz),dot(ray,_ViewUp.xyz)) / max(.001,depth);
                half inView = saturate((1-max(abs(projected.x),abs(projected.y)))*15) * step(0,depth);
                half3 colour = SAMPLE_TEXTURE2D(_BridgeView,sampler_BridgeView,saturate(.5+.5*projected)).rgb;
                return half4(lerp(half3(.093,.108,.101),colour*half3(.86,.91,.87)+.009,inView),1);
            }
            ENDHLSL
        }
    }
}
