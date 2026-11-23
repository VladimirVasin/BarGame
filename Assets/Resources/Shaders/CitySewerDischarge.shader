Shader "Bar Promenade/City Sewer Discharge"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-85" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float3 position : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 clip : SV_POSITION;
                float3 local : TEXCOORD0;
                half3 normal : TEXCOORD1;
                float bank : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            float hash21(float2 p)
            {
                float3 q = frac(p.xyx * .1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float noise2(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3.-2.*f);
                return lerp(lerp(hash21(cell), hash21(cell+float2(1,0)), f.x),
                            lerp(hash21(cell+float2(0,1)), hash21(cell+1.), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = input.position;
                float loose = smoothstep(-.05, .5, p.z);
                float t = _Time.y;
                p.y += loose*(.012*sin(12.*p.z-9.*t+4.*p.x) + .007*sin(18.*p.x-8.*p.z+7.*t));
                p.x += loose*.013*sin(7.*p.z-5.*t+input.uv.x*3.);
                o.local = p;
                o.clip = TransformObjectToHClip(p);
                o.normal = TransformObjectToWorldNormal(input.normal);
                o.bank = input.uv.x;
                o.fog = ComputeFogFactor(o.clip.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float z = i.local.z, x = i.local.x, t = _Time.y;
                float mixing = smoothstep(1.08, 1.63, z);
                // Transport slows and the current widens past the plunge.
                float along = z + max(z-1.22, 0.)*.62 - t*1.55;
                float2 uv = float2(4.6*x, 1.55*along);
                float twist = noise2(uv*.72);
                uv += float2(.85*twist, .23*sin(4.*x + along));
                float sediment = noise2(uv)*.73 + noise2(uv*2.8+.4)*.27;
                float eddy = noise2(float2(x*8.,along*5.) + twist*2.);
                float impact = exp(-2.7*abs(z-1.28));
                float wrinkles = noise2(float2(x*24., along*8.) + 2.2*float2(twist,eddy));
                float grain = noise2(float2(x*39.,along*29.) + eddy*2.);
                // The bars divide the current into narrow wakes. The ridges wander
                // and break into patches as the sheet accelerates over the lip.
                float wake = (.5+.5*cos(37.85*x + 1.15*sin(9.*along+8.*x) + 3.4*(twist-.5)));
                float streak = pow(wake,4.) * smoothstep(.24,.72,wrinkles) *
                    (1.-smoothstep(.9,1.8,z));
                float ripple = smoothstep(.62,.85,wrinkles)*(.38+.62*grain);
                float lace = 1.-smoothstep(.025,.14,abs(eddy+.20*(grain-.5)-.53));
                float froth = saturate(streak*.56 + impact*(.20+.73*lace+.27*ripple)
                    + mixing*(.18*lace+.16*ripple));
                half3 mud = lerp(half3(.043,.044,.032),half3(.18,.132,.073),sediment);
                half3 water = lerp(mud,half3(.44,.417,.344),froth);

                Light sun = GetMainLight();
                half3 normal = normalize(i.normal + half3(.38*(wrinkles-.5)+.15*(grain-.5),
                    .02, .30*(eddy-.5)+.12*(grain-.5)));
                half3 sky = max(SampleSH(half3(0,1,0)), half3(.05,.05,.05));
                half3 illumination = max(SampleSH(normal), .5h*sky) +
                    sun.color * (.20h + .64h*saturate(dot(normal,sun.direction)));
                half3 view = GetWorldSpaceNormalizeViewDir(TransformObjectToWorld(i.local));
                half fresnel = .065h+.47h*pow(1.h-saturate(dot(normal,view)),3.h);
                half3 reflection = (.76h*sky + .14h*sun.color)*half3(.72,.84,.86);
                half glint = pow(saturate(dot(normal,SafeNormalize(view+sun.direction))),30.h);
                water = lerp(water*illumination, reflection, fresnel) + sun.color*.21h*glint;
                float shoreEdge = 1.-smoothstep(.62,1.,abs(i.bank) + .085*(eddy-.5));
                float spread = (.69+.17*sediment)*shoreEdge*(1.-smoothstep(3.05,5.03,z));
                return half4(MixFog(water,i.fog), lerp(.95-.08*abs(i.bank),spread,mixing));
            }
            ENDHLSL
        }
    }
}
