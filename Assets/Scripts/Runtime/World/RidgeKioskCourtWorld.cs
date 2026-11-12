using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal sealed class RidgeKioskCourtWorld : MonoBehaviour
    {
        private readonly Transform[] canopies = new Transform[RidgeKioskCourtPlan.TableCount];
        private readonly Quaternion[] canopyRest = new Quaternion[RidgeKioskCourtPlan.TableCount];
        private readonly Transform[] tableTops = new Transform[RidgeKioskCourtPlan.TableCount];
        private Renderer concrete;
        private Camera observer;
        private float nextWindSample;
        internal RidgeKioskCourtPlan Plan { get; private set; }
        internal NpcHeroUpperBodyPose HeroActions { get; private set; }
        internal Transform TableTop(int table) => tableTops[table];

        internal static RidgeKioskCourtWorld Build(Transform kiosk, RidgeKioskCourtPlan plan, bool interactive,
            List<Collider> rainShelters)
        {
            var host = new GameObject("Ridge Kiosk Rear Court").transform;
            host.SetParent(kiosk, false);
            GameObject prefab = Resources.Load<GameObject>("RidgeKiosk/RidgeKioskCourt");
            if (!prefab) throw new InvalidOperationException("The authored Ridge kiosk court is missing.");
            Transform model = Instantiate(prefab, host, false).transform;
            var result = host.gameObject.AddComponent<RidgeKioskCourtWorld>();
            result.Plan = plan;
            result.concrete = Find(model, "KioskCourtSlab").GetComponentInChildren<MeshRenderer>();
            CityWetSurfaceRegistry.Register(result.concrete, CityWetSurfaceKind.Sidewalk);
            if (interactive)
            {
                result.HeroActions = new NpcHeroUpperBodyPose(host);
                float depth = RidgeKioskCourtPlan.SlabFront - RidgeKioskCourtPlan.SlabBack;
                var floor = CityTerracePilotWorld.Box(host, "Concrete court support",
                    new Vector3(0f, -.10f, (RidgeKioskCourtPlan.SlabBack + RidgeKioskCourtPlan.SlabFront) * .5f),
                    new Vector3(2f * RidgeKioskCourtPlan.SlabHalfWidth, .20f, depth), Quaternion.identity);
                FootstepGround.Stamp(floor.gameObject, FootstepGroundKind.Concrete);
                var furniture = Find(model, "RidgeKioskCourt_RidgeKioskSurface").GetComponent<MeshFilter>();
                var furnitureCollision = furniture.gameObject.AddComponent<MeshCollider>();
                furnitureCollision.sharedMesh = furniture.sharedMesh;
                FootstepGround.Stamp(furniture.gameObject, FootstepGroundKind.Wood);
            }
            for (int table = 0; table < RidgeKioskCourtPlan.TableCount; table++)
            {
                Vector3 local = plan.Table(table) - plan.Origin;
                Transform top = Find(model, "KioskCourtTable" + table);
                if (Vector3.Distance(top.position, host.TransformPoint(local + Vector3.up * RidgeKioskCourtPlan.TableHeight)) > .015f)
                    throw new InvalidOperationException("The kiosk table contact must match the imported tabletop.");
                result.tableTops[table] = top;
                Transform canopy = Find(model, "KioskCanopy" + table);
                result.canopies[table] = canopy;
                result.canopyRest[table] = canopy.localRotation;
                MeshFilter canopySurface = null;
                foreach (var renderer in canopy.GetComponentsInChildren<MeshRenderer>())
                {
                    if (renderer.name.EndsWith("_KioskLampGlass", StringComparison.Ordinal))
                    {
                        renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        CityNightGlowRegistry.Register(renderer, new Color(1.48f, .99f, .49f));
                    }
                    else canopySurface = renderer.GetComponent<MeshFilter>();
                }
                Transform light = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "RidgeKioskCourt").transform;
                light.SetParent(host, false);
                light.SetPositionAndRotation(Find(model, "KioskCourtLight" + table).position,
                    host.rotation * Quaternion.LookRotation(Vector3.down, Vector3.forward));
                if (!interactive) continue;
                if (!canopySurface) throw new InvalidOperationException("The imported umbrella has no rain surface.");
                var shape = canopySurface.gameObject.AddComponent<MeshCollider>();
                shape.convex = true;
                shape.isTrigger = true;
                shape.sharedMesh = canopySurface.sharedMesh;
                rainShelters.Add(shape);
            }
            return result;
        }

        private static Transform Find(Transform model, string name) =>
            CityPedestrianHandProps.FindSocket(model, name) ?? throw new InvalidOperationException("Missing court anchor " + name);

        private void OnDestroy() => CityWetSurfaceRegistry.Unregister(concrete);

        private void LateUpdate()
        {
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || Time.time < nextWindSample) return;
            if (!observer) observer = Camera.main;
            bool close = observer && (observer.transform.position - transform.position).sqrMagnitude < 28f * 28f;
            nextWindSample = Time.time + (close ? 0f : .125f);
            WindSample wind = GameWeatherRules.EvaluateCurrentWind();
            Vector3 local = transform.InverseTransformDirection(wind.HorizontalDirection);
            Vector3 axis = new(local.z, 0f, -local.x);
            for (int i = 0; i < canopies.Length; i++)
            {
                float degrees = (.28f + .18f * Mathf.Sin(Time.time * 2.1f + i * 1.7f)) * wind.Strength01;
                // Pivot about the clamp at the mast. Ribs, hem and shade move together.
                canopies[i].localRotation = canopyRest[i] * Quaternion.AngleAxis(degrees, axis);
            }
        }
    }
}
