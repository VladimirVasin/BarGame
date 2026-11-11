#ifndef BAR_PROMENADE_CITY_GRASS_WIND_INCLUDED
#define BAR_PROMENADE_CITY_GRASS_WIND_INCLUDED
#include "CityWindField.hlsl"

float4 _CityGrassWind;
float4 _CityGrassPreviousWind;
float4 _CityGrassContacts[32];
float4 _CityGrassPreviousContacts[32];
float4 _CityGrassState;
float _CityGrassFade;
float _CityGrassPreviousFade;
float4 _CityGrassContactBounds;
int _CityGrassContactCount;

float3 CityGrassTravel(float3 root, float height, float4 wind, bool previous)
{
    float3 city = CityWindCoordinates(root);
    float phase = CityWindPhase(city.xz);
    float ripple = .90 + .10 * sin(wind.w * 4.1 + dot(city.xz, float2(1.3, .93)));
    float flex = lerp(.68, .41, saturate(height));
    float strength = CityWindStrength(wind.z, city.y);
    float2 travel = wind.xy * (height * flex * strength * CityWindPulse(phase, wind.w) * ripple);
    float pressure = 0;
    bool nearBodies=all(root.xz>=_CityGrassContactBounds.xy)&&all(root.xz<=_CityGrassContactBounds.zw);
    [loop] for(int i=0;i<(nearBodies ? _CityGrassContactCount : 0);i++)
    {
        float4 body = previous ? _CityGrassPreviousContacts[i] : _CityGrassContacts[i];
        if(body.w<=.015 || abs(root.y-body.y)>.55)continue;
        float2 away=root.xz-body.xz;
        float d=length(away);
        float weight = 1 - smoothstep(body.w * .15, body.w, d);
        float2 bend = away + wind.xy * max(0, .08 - d);
        travel += bend / max(length(bend), .02) * (height * .93 * weight);
        pressure = max(pressure, weight);
    }
    float lengthXZ=length(travel);
    travel *= min(1, height * .96 / max(.001, lengthXZ));
    float sink = max(height * .60 * pressure, dot(travel,travel) / max(.10, 2 * height));
    return float3(travel.x,-sink,travel.y)*_CityGrassState.x;
}

float3 CityGrassBentPosition(float3 root, float3 position, float weight, float3 travel, float fade)
{
    float3 bend = float3(travel.x * weight, travel.y, travel.z * weight) * weight;
    return root + (position - root + bend) * fade;
}

float3 CityGrassPositionWS(float4 positionOS,float2 metadata,bool previous)
{
    float3 root=previous ? mul(UNITY_PREV_MATRIX_M,float4(0,0,0,1)).xyz : TransformObjectToWorld(float3(0,0,0));
    float3 position=previous ? mul(UNITY_PREV_MATRIX_M,positionOS).xyz : TransformObjectToWorld(positionOS.xyz);
    float weight=saturate(metadata.x);
    float height=max(.07,(position.y-root.y)/max(.01,weight));
    float3 travel=CityGrassTravel(root,height,previous ? _CityGrassPreviousWind : _CityGrassWind,previous);
    // Geometry shrinks into its own fixed roots at the outer fog band.
    float fade=previous ? _CityGrassPreviousFade : _CityGrassFade;
    return CityGrassBentPosition(root, position, weight, travel, fade);
}
void CityGrassDisplace(inout float4 positionOS,float2 metadata)
{
    positionOS.xyz=TransformWorldToObject(CityGrassPositionWS(positionOS,metadata,false));
}
void CityGrassDisplaceNormal(inout float4 positionOS,inout float3 normalOS,float2 metadata)
{
    float3 root=TransformObjectToWorld(float3(0,0,0));
    float3 position=TransformObjectToWorld(positionOS.xyz);
    float height=max(.07,(position.y-root.y)/max(.01,metadata.x));
    float3 travel=CityGrassTravel(root,height,_CityGrassWind,false);
    // Jacobian inverse transpose for the quadratic rooted bend. The same
    // normal correction is used by lighting, shadow bias and depth normals.
    float3 n=TransformObjectToWorldNormal(normalOS);
    float weight = saturate(metadata.x);
    float3 derivative = float3(travel.x * 2 * weight, travel.y, travel.z * 2 * weight) / height;
    n.y=(n.y-derivative.x*n.x-derivative.z*n.z)/max(.2,1+derivative.y);
    normalOS=TransformWorldToObjectNormal(normalize(n));
    positionOS.xyz = TransformWorldToObject(CityGrassBentPosition(root, position, weight, travel, _CityGrassFade));
}
float3 CityGrassPreviousPositionWS(float4 positionOS,float2 metadata)
{return CityGrassPositionWS(positionOS,metadata,true);}
#endif
