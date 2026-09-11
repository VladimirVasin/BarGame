Shader "Bar Promenade/NPC Nameplate"
{
    Properties
    {
        [NoScaleOffset] _NpcSceneDepth("Scene Depth", 2D) = "black" {}
        [NoScaleOffset] _NpcVisibleActors("Visible Actors", 2D) = "black" {}
        [NoScaleOffset] _NpcFontAtlas("Shared Interface Font", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Ps1VertexJitter.hlsl"
            TEXTURE2D_X_FLOAT(_NpcSceneDepth);
            TEXTURE2D(_NpcVisibleActors);
            TEXTURE2D(_NpcFontAtlas);
            float4 _NpcTargetSize;
            float _NpcActorId;
            float4 _NpcProbes[32];
            float4 _NpcExtraProbes[32];
            float4 _NpcPlanes[32];
            float4 _NpcOverlapMasks[32];

            float EyeDepth(float rawDepth)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        rawDepth = 1.0 - rawDepth;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            float SceneDepth(float2 uv)
            {
                return EyeDepth(SAMPLE_TEXTURE2D_X_LOD(_NpcSceneDepth, sampler_PointClamp, uv, 0).r);
            }

            struct MaskAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct MaskVaryings { float4 positionCS : SV_POSITION; };
            MaskVaryings MaskVertex(MaskAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                MaskVaryings output;
                output.positionCS = Ps1SnapClipPosition(TransformObjectToHClip(input.positionOS.xyz));
                return output;
            }
            half4 MaskFragment(MaskVaryings input) : SV_Target
            {
                // Compare the SAME surface, not a bone inside a skull. Two mm
                // absorbs depth storage roundoff, never a head-radius allowance.
                float depth = SceneDepth(input.positionCS.xy * _NpcTargetSize.zw);
                clip(0.002 - abs(depth - EyeDepth(input.positionCS.z)));
                return half4(_NpcActorId, 0, 0, 1);
            }

            struct CaptionAttributes
            {
                float3 positionOS : POSITION;
                float4 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct CaptionVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            CaptionVaryings CaptionVertex(CaptionAttributes input)
            {
                CaptionVaryings output;
                output.positionCS = float4(input.positionOS.xy, 0, 1);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            float OwnSurface(float2 uv, float actorId)
            {
                if (any(uv < 0.0) || any(uv > 1.0)) return 0.0;
                float sampleId = SAMPLE_TEXTURE2D_LOD(_NpcVisibleActors, sampler_PointClamp, uv, 0).r;
                return abs(sampleId - actorId) < (0.4 / 255.0) ? 1.0 : 0.0;
            }
            bool VisibleCaption(int index)
            {
                float actorId = (index + 1.0) / 255.0;
                float4 headBody = _NpcProbes[index];
                float4 sides = _NpcExtraProbes[index];
                float visible = OwnSurface(headBody.xy, actorId) + OwnSurface(headBody.zw, actorId) +
                    OwnSurface(sides.xy, actorId) + OwnSurface(sides.zw, actorId);
                return visible > 0.5 && SceneDepth(_NpcPlanes[index].zw) >= _NpcPlanes[index].x;
            }
            uint OverlapMask(int index)
            {
                return (uint)_NpcOverlapMasks[index].x | ((uint)_NpcOverlapMasks[index].y << 16);
            }
            half4 CaptionFragment(CaptionVaryings input) : SV_Target
            {
                int index = (int)(input.uv.w + 0.5);
                if (!VisibleCaption(index)) discard;
                // Resolve priority against THIS frame's visible surfaces.
                // A nearer actor behind a wall cannot hide a visible caption.
                uint accepted = 0;
                [loop] for (int previous = 0; previous < index; previous++)
                {
                    if ((OverlapMask(previous) & accepted) == 0 && VisibleCaption(previous))
                        accepted |= 1u << previous;
                }
                if ((OverlapMask(index) & accepted) != 0) discard;
                // The caption itself must not paint over an opaque object,
                // even when a visible part of its speaker peeks around it.
                clip(SceneDepth(input.positionCS.xy * _NpcTargetSize.zw) - _NpcPlanes[index].x);
                half coverage = input.uv.z > 0.5 ? 1.0h :
                    SAMPLE_TEXTURE2D(_NpcFontAtlas, sampler_PointClamp, input.uv.xy).a;
                return half4(input.color.rgb, input.color.a * coverage * _NpcPlanes[index].y);
            }
        ENDHLSL
        Pass
        {
            Name "VisibleActorSurface"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "CrispRoleCaption"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex CaptionVertex
            #pragma fragment CaptionFragment
            ENDHLSL
        }
    }
}
