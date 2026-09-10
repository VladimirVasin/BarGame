using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private void CreateTruckLights()
        {
            // The two authored lenses are fixed metre coordinates in Truck.
            // Like the other city fixtures, they remain lit in the daytime.
            foreach(float x in new[]{-.88f,.88f})
            {
                var host=new GameObject("Delivery Truck Headlamp");
                host.transform.SetParent(Truck,false);
                host.transform.localPosition=new Vector3(x,1.4f,5.34f);
                host.transform.localRotation=Quaternion.Euler(8,0,0);
                Light light=host.AddComponent<Light>();
                light.type=LightType.Spot;
                light.color=new Color(1,.83f,.61f);
                light.range=16;
                light.spotAngle=48;
                light.innerSpotAngle=30;
                light.shadows=LightShadows.Hard;
                light.shadowBias=.02f;
                light.shadowNormalBias=.1f;
                CityLightHalo halo=CityLightHalo.CreateNightRegistered(host.transform,Vector3.zero,
                    .12f,.4f,new Color(1,.8f,.5f,.1f),new Color(1,.7f,.4f,.025f));
                CityNightSiteLightRegistry.Register(light,3.2f,halo);
            }
        }
    }
}
