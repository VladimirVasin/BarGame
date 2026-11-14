using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal sealed class RidgeKioskCourtWorld : MonoBehaviour
    {
        internal const string LampPurpose = "RidgeKioskCourt";
        private static readonly Color DiffuserGlow = new Color(.83f, .67f, .47f);
        private readonly Transform[] canopies = new Transform[RidgeKioskCourtPlan.TableCount];
        private readonly Quaternion[] canopyRest = new Quaternion[RidgeKioskCourtPlan.TableCount];
        private readonly Transform[] tableTops = new Transform[RidgeKioskCourtPlan.TableCount];
        private Renderer concrete;
        private readonly List<Renderer> pathSurfaces = new(5);
        private readonly List<Mesh> gradedMeshes = new(5);
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
            result.BuildPaths(model, interactive);
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
                var edges = Find(model, "KioskCourtEdges").GetComponentInChildren<MeshFilter>();
                edges.gameObject.AddComponent<MeshCollider>().sharedMesh = edges.sharedMesh;
                FootstepGround.Stamp(edges.gameObject, FootstepGroundKind.Concrete);
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
                var diffuser = Find(model, $"RidgeKioskCourt_KioskCourtLamp{table}_KioskLampGlass")
                    .GetComponent<MeshRenderer>();
                diffuser.sharedMaterial = CityNightResources.EmissiveMaterial;
                diffuser.shadowCastingMode = ShadowCastingMode.Off;
                diffuser.receiveShadows = false;
                CityNightGlowRegistry.Register(diffuser, DiffuserGlow);
                AddLampSource(table, "Left", -1f);
                AddLampSource(table, "Right", 1f);

                if (!interactive) continue;
                MeshFilter canopySurface = canopy.GetComponentInChildren<MeshFilter>();
                if (!canopySurface) throw new InvalidOperationException("The imported umbrella has no rain surface.");
                var shape = canopySurface.gameObject.AddComponent<MeshCollider>();
                shape.convex = true;
                shape.isTrigger = true;
                shape.sharedMesh = canopySurface.sharedMesh;
                rainShelters.Add(shape);
            }
            return result;

            void AddLampSource(int table, string side, float outward)
            {
                Transform socket = Find(model, $"KioskCourtLight{table}{side}");
                Transform anchor = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + LampPurpose).transform;
                anchor.SetParent(host, false);
                // Both lobes tip away from the mast. Each face receives a
                // broad beam and the steel pole stays out of the bright core.
                Vector3 aim = new Vector3(outward * .32f, -1f, 0f);
                anchor.SetPositionAndRotation(socket.position, host.rotation * Quaternion.LookRotation(aim, Vector3.forward));
            }
        }

        private static Transform Find(Transform model, string name) =>
            CityPedestrianHandProps.FindSocket(model, name) ?? throw new InvalidOperationException("Missing court anchor " + name);

        private void BuildPaths(Transform model, bool interactive)
        {
            Path("Service", true);
            Path("Left", Plan.HasLeftApproach);
            Path("Right", Plan.HasRightApproach);
            Path("LeftTurn", Plan.EntrySide < 0);
            Path("RightTurn", Plan.EntrySide > 0);

            void Path(string part, bool active)
            {
                Transform group = Find(model, "KioskCourtPath" + part);
                group.gameObject.SetActive(active);
                if (!active) return;
                var filter = group.GetComponentInChildren<MeshFilter>();
                Mesh mesh = Instantiate(filter.sharedMesh);
                mesh.name = "Graded kiosk path " + part;
                gradedMeshes.Add(mesh);
                // Preserve the FBX's unit scale. Only grade the authored vertices;
                // City and the passive Home view use this same metre-space plan.
                Matrix4x4 toCourt = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Matrix4x4 toMesh = toCourt.inverse;
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = toCourt.MultiplyPoint3x4(vertices[i]);
                    p.y += Plan.PathTop(new Vector2(Plan.Origin.x + p.x, Plan.Origin.z + p.z)) - Plan.Origin.y;
                    vertices[i] = toMesh.MultiplyPoint3x4(p);
                }
                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;
                var surface = filter.GetComponent<MeshRenderer>();
                pathSurfaces.Add(surface);
                CityWetSurfaceRegistry.Register(surface, CityWetSurfaceKind.Sidewalk);
                if (interactive)
                {
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                    FootstepGround.Stamp(filter.gameObject, FootstepGroundKind.Concrete);
                }
            }
        }

        private void OnDestroy()
        {
            CityWetSurfaceRegistry.Unregister(concrete);
            foreach (Renderer surface in pathSurfaces) CityWetSurfaceRegistry.Unregister(surface);
            foreach (Mesh mesh in gradedMeshes) if (mesh) Destroy(mesh);
        }

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
