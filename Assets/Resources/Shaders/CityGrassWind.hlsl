#ifndef BAR_PROMENADE_CITY_GRASS_WIND_INCLUDED
#define BAR_PROMENADE_CITY_GRASS_WIND_INCLUDED
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
    float phase = dot(root.xz, float2(.67,.41));
    float wave = .62 + .24*sin(wind.w*1.5 + phase) + .14*sin(wind.w*3.1+phase*1.7);
    float stiffness = lerp(.45,.20,saturate(height));
    float2 travel = wind.xy * (height*stiffness*wind.z*wave);
    bool nearBodies=all(root.xz>=_CityGrassContactBounds.xy)&&all(root.xz<=_CityGrassContactBounds.zw);
    [loop] for(int i=0;i<(nearBodies ? _CityGrassContactCount : 0);i++)
    {
        float4 body = previous ? _CityGrassPreviousContacts[i] : _CityGrassContacts[i];
        if(body.w<=.015 || abs(root.y-body.y)>.85)continue;
        float2 away=root.xz-body.xz;
        float d=length(away);
        float weight=smoothstep(body.w,0,d);
        travel += away/max(d,.12) * (height*.72*weight);
    }
    float lengthXZ=length(travel);
    travel *= min(1,height*.78/max(.001,lengthXZ));
    float sink=min(height*.42,dot(travel,travel)/max(.10,2*height));
    return float3(travel.x,-sink,travel.y)*_CityGrassState.x;
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
    position=root+(position-root)*fade;
    return position+travel*(weight*weight)*fade;
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
    float3 derivative=travel*(2*saturate(metadata.x)/height);
    n.y=(n.y-derivative.x*n.x-derivative.z*n.z)/max(.2,1+derivative.y);
    normalOS=TransformWorldToObjectNormal(normalize(n));
    CityGrassDisplace(positionOS,metadata);
}
float3 CityGrassPreviousPositionWS(float4 positionOS,float2 metadata)
{return CityGrassPositionWS(positionOS,metadata,true);}
#endif
