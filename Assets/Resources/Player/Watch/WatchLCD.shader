Shader "BarPromenade/Watch LCD"
{
    Properties
    {
        _Segments ("Segment Masks", Vector) = (63, 63, 63, 63)
        _ColonVisible ("Colon Visible", Float) = 1
        _Panel ("LCD", Color) = (0.48, 0.52, 0.43, 1)
        _Ink ("Segments", Color) = (0.065, 0.085, 0.059, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "LCD"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Segments;
                float _ColonVisible;
                half4 _Panel;
                half4 _Ink;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Segment(float2 p, float2 centre, float2 extent, float aa)
            {
                float2 toEdge = abs(p - centre) - extent;
                return 1.0 - smoothstep(-aa, aa, max(toEdge.x, toEdge.y));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * float2(5.7, 1.8);
                float aa = max(fwidth(p.x), fwidth(p.y)) * 0.55;
                float pixel = 0;
                if (abs(p.x) < .40)
                {
                    pixel = _ColonVisible * (Segment(p, float2(0, .26), .09, aa) +
                                             Segment(p, float2(0, -.26), .09, aa));
                }
                else
                {
                    bool outer = abs(p.x) >= 1.50;
                    uint column = p.x < 0 ? (outer ? 0 : 1) : (outer ? 3 : 2);
                    p.x += (p.x < 0 ? 1 : -1) * (outer ? 2.04 : .96);
                    uint mask = (uint)_Segments[column];
                    const float2 centres[7] = {
                        float2(0, .61), float2(.33, .31), float2(.33, -.31),
                        float2(0, -.61), float2(-.33, -.31), float2(-.33, .31), float2(0, 0)
                    };
                    [unroll] for (uint i = 0; i < 7; ++i)
                    {
                        bool horizontal = i == 0 || i == 3 || i == 6;
                        float2 size = horizontal ? float2(.25, .065) : float2(.065, .23);
                        pixel += ((mask >> i) & 1u) * Segment(p, centres[i], size, aa);
                    }
                }
                return half4(MixFog(lerp(_Panel.rgb, _Ink.rgb, saturate(pixel)), input.fog), 1);
            }
            ENDHLSL
        }
    }
}
