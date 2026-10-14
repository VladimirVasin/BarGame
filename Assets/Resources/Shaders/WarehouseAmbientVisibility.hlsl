#ifndef BAR_PROMENADE_WAREHOUSE_AMBIENT_VISIBILITY_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_AMBIENT_VISIBILITY_INCLUDED

// Scene globals, deliberately outside UnityPerMaterial: the single warehouse
// hero owns them. No material clones, keywords or per-renderer property blocks.
float4 _WarehouseAmbientOrigin;
float4 _WarehouseAmbientFalloff;

half4 Ps1WarehouseFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
    UNITY_BRANCH
    if (_WarehouseAmbientOrigin.w > 0.5)
    {
        float heroDistance = distance(inputData.positionWS, _WarehouseAmbientOrigin.xyz);
        float fade = saturate((heroDistance - _WarehouseAmbientFalloff.x) * _WarehouseAmbientFalloff.y);
        float visibility = 1.0 - fade * fade * (3.0 - 2.0 * fade);
        // Only indirect diffuse light fades. Stock URP still owns real light,
        // occlusion, specular response, shadows and emission, including the beam.
        inputData.bakedGI *= visibility;
    }
    return UniversalFragmentPBR(inputData, surfaceData);
}

#endif
