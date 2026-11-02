Shader "Bar Promenade/Vodka Bottle Liquid"
{
    Properties
    {
        _BaseColor("Liquid", Color) = (.62,.68,.57,.48)
        _FillPlaneLocal("Local gravity plane", Vector) = (0,1,0,.28)
        _IsSurface("Free surface", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "BottleLiquid"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _FillPlaneLocal;
                float4x4 _BottleFromObject;
                float4 _BottleProfile[7];
                float _IsSurface;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 bottle:TEXCOORD0; float3 normal:TEXCOORD1; half fog:TEXCOORD2; };
            V vert(A v)
            {
                V o; VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS=p.positionCS; o.bottle=mul(_BottleFromObject,v.positionOS).xyz;
                o.normal=TransformObjectToWorldNormal(v.normalOS);
                o.fog=ComputeFogFactor(p.positionCS.z); return o;
            }
            float radius(float y)
            {
                [unroll] for(int segment=0;segment<6;segment++)
                {
                    if(y<=_BottleProfile[segment+1].x)
                        return lerp(_BottleProfile[segment].y,_BottleProfile[segment+1].y,
                            saturate((y-_BottleProfile[segment].x)/(_BottleProfile[segment+1].x-_BottleProfile[segment].x)));
                }
                return _BottleProfile[6].y;
            }

            half4 frag(V i):SV_Target
            {
                if(_IsSurface<.5) clip(_FillPlaneLocal.w-dot(i.bottle,_FillPlaneLocal.xyz));
                else
                {
                    clip(min(i.bottle.y-_BottleProfile[0].x,_BottleProfile[6].x-i.bottle.y));
                    // Match the sixteen-sided authored cavity, including its narrow neck.
                    float angle=atan2(i.bottle.z,i.bottle.x);
                    float sector=3.14159265/8.;
                    float edge=cos((floor(angle/sector)+.5)*sector-angle);
                    clip(radius(i.bottle.y)*cos(sector*.5)-length(i.bottle.xz)*edge);
                }
                half3 n=normalize(i.normal); Light light=GetMainLight();
                half3 lit=SampleSH(n)+light.color*(.25h+.55h*saturate(dot(n,light.direction)));
                half3 color=_BaseColor.rgb*max(lit,half3(.22,.24,.22));
                if(_IsSurface>.5) color+=half3(.10,.12,.10);
                return half4(MixFog(color,i.fog),_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
