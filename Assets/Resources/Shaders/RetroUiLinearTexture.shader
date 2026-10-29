Shader "Hidden/Bar Promenade/Linear UI Texture"
{
    Properties { _MainTex("Texture", 2D) = "white" {} }
    CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _GUIClipTexture;
        float4x4 unity_GUIClipTextureMatrix;
        struct Attributes
        {
            float4 vertex : POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
        };
        struct Varyings
        {
            float4 position : SV_POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float2 clipUv : TEXCOORD1;
        };
        Varyings Vertex(Attributes input)
        {
            Varyings output;
            output.position = UnityObjectToClipPos(input.vertex);
            float3 eye = UnityObjectToViewPos(input.vertex);
            output.clipUv = mul(unity_GUIClipTextureMatrix, float4(eye.xy, 0.0, 1.0)).xy;
            output.color = float4(
                GammaToLinearSpaceExact(input.color.r),
                GammaToLinearSpaceExact(input.color.g),
                GammaToLinearSpaceExact(input.color.b), input.color.a);
            output.uv = input.uv;
            return output;
        }
        float4 Fragment(Varyings input) : SV_Target
        {
            float coverage = tex2D(_GUIClipTexture, input.clipUv).a;
            clip(coverage - .001f);
            float4 color = tex2D(_MainTex, input.uv) * input.color;
            color.a *= coverage;
            return color;
        }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Alpha"
            Blend SrcAlpha OneMinusSrcAlpha, One One
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            ENDCG
        }
        Pass
        {
            Name "Opaque"
            Blend Off
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            ENDCG
        }
    }
}
