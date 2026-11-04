#ifndef BAR_PROMENADE_CITY_VALLEY_FOG_INCLUDED
#define BAR_PROMENADE_CITY_VALLEY_FOG_INCLUDED

// Observer elevation, mean density gain per metre of descent, lowest street,
// and elevation blend. Zero outside the production City.
float4 _CityValleyFog;

half CityValleyFogFactor(half distanceFog, float receiverY)
{
    float descent = max(0.0, _CityValleyFog.x - max(receiverY, _CityValleyFog.z));
    return distanceFog * (1.0 + descent * _CityValleyFog.y);
}

#endif
