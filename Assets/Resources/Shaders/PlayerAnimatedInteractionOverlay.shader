Shader "Bar Promenade/Player Animated Interaction Overlay"
{
    Properties
    {
        _MainTex("Sprite Texture", 2D) = "white" {}

        [HideInInspector] _Color("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] PixelSnap("Pixel Snap", Float) = 0
        [HideInInspector] _RendererColor(
            "Renderer Color",
            Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaTex(
            "External Alpha",
            2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha(
            "Enable External Alpha",
            Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "InteractionOverlay"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma target 3.5
            #pragma vertex OverlayVertex
            #pragma fragment OverlayFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings OverlayVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(
                    input.positionOS,
                    unity_SpriteProps.xy);

                Varyings output =
                    CommonUnlitVertex(input);
                output.color =
                    input.color *
                    _Color *
                    unity_SpriteColor;
                return output;
            }

            half4 OverlayFragment(
                Varyings input) : SV_Target
            {
                return CommonUnlitFragment(
                    input,
                    input.color);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
