using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private void CreateTruckLights()
        {
            // Read the authored lens through world space: the imported FBX
            // root owns its unit correction. Emit just outside the glass.
            // The city fixture floor keeps the dipped beams lit by day too.
            foreach(string side in new[]{"Left","Right"})
            {
                Transform lens=Require(Truck,"ANCHOR_Headlamp"+side);
                var host=new GameObject("Delivery Truck Headlamp "+side);
                host.transform.SetParent(Truck,false);
                host.transform.localPosition=Truck.InverseTransformPoint(lens.position)+Vector3.forward*.04f;
                host.transform.localRotation=Quaternion.Euler(8,0,0);
                Light light=host.AddComponent<Light>();
                light.type=LightType.Spot;
                light.color=new Color(1,.83f,.61f);
                light.range=22;
                light.spotAngle=48;
                light.innerSpotAngle=30;
                light.shadows=LightShadows.Hard;
                light.shadowBias=.02f;
                light.shadowNormalBias=.1f;
                CityLightHalo halo=CityLightHalo.CreateNightRegistered(host.transform,Vector3.zero,
                    .22f,.75f,new Color(1,.8f,.5f,.1f),new Color(1,.7f,.4f,.025f));
                CityNightSiteLightRegistry.Register(light,14f,halo);
            }
        }
    }
}
