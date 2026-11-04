#ifndef BAR_PROMENADE_CITY_SKY_FILL_INCLUDED
#define BAR_PROMENADE_CITY_SKY_FILL_INCLUDED

// Set only while the playable City owns the sky. Stylized, unlit receivers
// follow its diffuse fill separately from windows, lenses and actual lamps.
half _CitySkyFillLoss;

half CitySkyFill()
{
    return 1.0h - _CitySkyFillLoss;
}

#endif
