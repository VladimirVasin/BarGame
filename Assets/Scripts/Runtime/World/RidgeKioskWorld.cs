using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class RidgeKioskWorld
    {
        private static Material signBoard, signLetters;
        // Scene cleanup does not retain textures referenced only through an MPB.
        private static Texture2D beerPoster, vodkaPoster, piePoster;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPosters() => beerPoster = vodkaPoster = piePoster = null;

        public static Transform Build(Transform parent, CityKioskSitePlan site, bool interactive) =>
            Build(parent, site, interactive, out _);

        internal static Transform Build(Transform parent, CityKioskSitePlan site, bool interactive, out Collider[] rainShelters)
        {
            rainShelters = Array.Empty<Collider>();
            var root = new GameObject(site.IsRidge ? "Ridge Kiosk" : "Kiosk " + site.StableId).transform;
            root.SetParent(parent, false);
            root.localPosition = site.Origin;
            root.localRotation = site.Rotation;
            var template = Resources.Load<GameObject>("RidgeKiosk/RidgeKiosk");
            if (template == null) throw new InvalidOperationException("Ridge kiosk model is missing.");
            Transform model = UnityEngine.Object.Instantiate(template, root, false).transform;
            model.localPosition = Vector3.zero;
            root.gameObject.AddComponent<RidgeKioskStreetLightReceivers>().Configure(model);
            var anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var part in model.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("Kiosk", StringComparison.Ordinal)) anchors[part.name] = part;

            if (!signBoard) signBoard = Resources.Load<Material>("Pawnshop/Materials/PawnshopMatteSign");
            if (!signLetters) signLetters = Resources.Load<Material>("Pawnshop/Materials/PawnshopMatteLetters");
            if (!signBoard || !signLetters) throw new InvalidOperationException("Kiosk enamel sign materials are missing.");
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                switch (renderer.name)
                {
                    case "RidgeKiosk_KioskLampGlass":
                        renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        CityNightGlowRegistry.Register(renderer, new Color(3.8f, 2.3f, .9f));
                        break;
                    case "RidgeKiosk_KioskSignBoard": renderer.sharedMaterial = signBoard; break;
                    case "RidgeKiosk_KioskSignLetters": renderer.sharedMaterial = signLetters; break;
                    case "RidgeKiosk_KioskAdvertisement":
                        BindPoster(renderer, site.StableId);
                        break;
                }
            }
            root.gameObject.AddComponent<KioskHoursNeon>().Initialize(model);

            Transform signLamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "KioskSign").transform;
            signLamp.SetParent(root, false);
            signLamp.SetPositionAndRotation(anchors["KioskSignSource"].position,
                Quaternion.LookRotation(anchors["KioskSignTarget"].position - anchors["KioskSignSource"].position, root.up));

            Transform approach = anchors["KioskApproach"];
            var offers = RidgeKioskOffers.Build(root, anchors["KioskBeer"], anchors["KioskVodka"], anchors["KioskPie"], approach,
                interactive, site.StableId);
            // Direction is owned by this metre-space frame, not by the imported
            // Empty's local axes. Both City and the passive balcony share its lens.
            var lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "RidgeKiosk").transform;
            lamp.SetParent(root, false);
            // Put both lobes just below the real diffuser. The outward axis
            // clears the sill before meeting the pavement; the old steep axis
            // spent its energy on the counter. Walls/hood retain full shadows.
            lamp.position = anchors["KioskLamp"].position - root.up * .04f;
            lamp.rotation = root.rotation * Quaternion.LookRotation(new Vector3(0f, -.55f, 1.6f), Vector3.forward);
            var interior = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "RidgeKioskInterior").transform;
            interior.SetParent(root, false);
            interior.position = lamp.position;
            interior.rotation = root.rotation * Quaternion.LookRotation(Vector3.down, Vector3.forward);

            if (interactive)
            {
                rainShelters = BuildCollision(root);
                var vendor = RidgeKioskVendor.Build(root, anchors["KioskVendor"], anchors["KioskTalk"], approach,
                    site.CharacterId, site.HasDialogue);
                root.gameObject.AddComponent<RidgeKioskStation>().Initialize(vendor, offers, anchors["KioskTalk"], approach);
            }
            return root;
        }

        private static void BindPoster(Renderer renderer, string siteId)
        {
            Texture2D texture = siteId switch
            {
                "ridge-kiosk" => Poster("KioskBeer", ref beerPoster),
                "industrial-kiosk" => Poster("KioskVodka", ref vodkaPoster),
                "residential-kiosk" => Poster("KioskPies", ref piePoster),
                _ => throw new ArgumentOutOfRangeException(nameof(siteId), siteId, "Missing kiosk advertisement.")
            };
            renderer.sharedMaterial = signLetters;
            var print = new MaterialPropertyBlock();
            print.SetTexture("_BaseMap", texture);
            print.SetColor("_BaseColor", Color.white);
            renderer.SetPropertyBlock(print);
        }

        private static Texture2D Poster(string name, ref Texture2D cached)
        {
            if (!cached) cached = Resources.Load<Texture2D>("RidgeKiosk/Posters/" + name);
            return cached ? cached : throw new InvalidOperationException("Missing kiosk poster " + name);
        }

        private static Collider[] BuildCollision(Transform root)
        {
            BoxCollider Box(string part, Vector3 centre, Vector3 size) =>
                CityTerracePilotWorld.Box(root, part, centre, size, Quaternion.identity);
            Box("Rear wall", new Vector3(0f, 1.16f, -1.20f), new Vector3(3f, 2.75f, .10f));
            foreach (float sign in new[] { -1f, 1f })
            {
                Box("Side wall", new Vector3(sign * 1.46f, 1.16f, 0f), new Vector3(.08f, 2.75f, 2.5f));
                Box("Window jamb", new Vector3(sign * 1.31f, 1.58f, 1.20f), new Vector3(.38f, 1.08f, .10f));
            }
            Box("Counter base", new Vector3(0f, .275f, 1.20f), new Vector3(3f, 1.45f, .10f));
            Box("Counter", new Vector3(0f, 1.02f, 1.21f), new Vector3(2.48f, .04f, .594f));
            Box("Window head", new Vector3(0f, 2.32f, 1.20f), new Vector3(3f, .40f, .10f));
            var roof = Box("Roof", new Vector3(0f, 2.55f, 0f), new Vector3(3.14f, .10f, 2.68f));
            var hood = CityTerracePilotWorld.Box(root, "Window hood", new Vector3(0f, 2.255f, 1.475f),
                new Vector3(2.74f, .051f, .60f), Quaternion.Euler(8f, 0f, 0f));
            Box("Service floor", new Vector3(0f, -.185f, 0f), new Vector3(3f, .53f, 2.5f));
            float front = RidgeKioskSitePlan.ApronFront, back = RidgeKioskSitePlan.ApronBack;
            var apron = Box("Visitor apron", new Vector3(0f, -.25f, (front + back) * .5f),
                new Vector3(3f, .5f, front - back));
            FootstepGround.Stamp(apron.gameObject, FootstepGroundKind.Concrete);
            return new Collider[] { roof, hood };
        }
    }
}
