using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        public Light ServiceWorkLight { get; private set; }

        private void CreateFactoryServiceLight()
        {
            Transform anchor = Require(equipment, "ANCHOR_ServiceLight");
            Vector3 target = Require(equipment, "ANCHOR_SeamerRestWorker").position + Vector3.up * 1.1f;
            var host = new GameObject("Cannery Service Work Light");
            host.transform.SetParent(factory, false);
            host.transform.SetPositionAndRotation(anchor.position, Quaternion.LookRotation(target - anchor.position));
            ServiceWorkLight = host.AddComponent<Light>();
            ServiceWorkLight.type = LightType.Spot;
            ServiceWorkLight.color = new Color(1f, .84f, .63f);
            ServiceWorkLight.range = 4.8f;
            ServiceWorkLight.spotAngle = 88f;
            ServiceWorkLight.innerSpotAngle = 54f;
            ServiceWorkLight.shadows = LightShadows.Hard;
            ServiceWorkLight.shadowBias = .018f;
            ServiceWorkLight.shadowNormalBias = .08f;
            CityNightSiteLightRegistry.Register(ServiceWorkLight, 5.5f, 5.5f * 2f / 3f, null);
        }

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
