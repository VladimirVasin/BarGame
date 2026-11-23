using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal static class CityWaterNetworkWorld
    {
        internal const string RootName = "Municipal water and sewer";
        private sealed class Part
        {
            internal Mesh Mesh;
            internal Matrix4x4 Basis;
            internal Material Material;
        }
        private static readonly Dictionary<CityWaterKit,List<Part>> Templates = new();
        private static GameObject source;
        private static Material dischargeMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Templates.Clear();
            source = null;
            if (dischargeMaterial != null) UnityEngine.Object.Destroy(dischargeMaterial);
            dischargeMaterial = null;
        }

        private static void LoadParts()
        {
            if (source != null) return;
            source = Resources.Load<GameObject>(CityCoastalInfrastructureWorldBuilder.ResourcePath);
            Templates.Clear();
            if (dischargeMaterial == null)
                dischargeMaterial = new Material(Resources.Load<Shader>("Shaders/CitySewerDischarge"))
                    { name = "Silty beach discharge", hideFlags = HideFlags.HideAndDontSave };
            Matrix4x4 correction = Matrix4x4.Rotate(CityCoastalInfrastructureWorldBuilder.ImportedBasisCorrection);
            foreach (var group in source.GetComponentsInChildren<Transform>(true))
            {
                if (!Enum.TryParse(group.name,out CityWaterKit kind)) continue;
                var parts = new List<Part>();
                foreach (var filter in group.GetComponentsInChildren<MeshFilter>(true))
                {
                    string role = filter.name.Substring(filter.name.LastIndexOf("__",StringComparison.Ordinal)+2);
                    parts.Add(new Part { Mesh = filter.sharedMesh, Basis = correction * filter.transform.localToWorldMatrix,
                        Material = role == "Water" ? dischargeMaterial : CityPortAssetProvider.GetSurfaceMaterial(role) });
                }
                Templates.Add(kind,parts);
            }
        }

        internal static void Build(Transform parent, CityWaterNetwork plan, Predicate<Vector3> include = null,
            HomeExteriorContextPlan home = null)
        {
            if (!plan.Enabled) return;
            LoadParts();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent,false);
            Quaternion basis = home == null ? Quaternion.identity : Quaternion.LookRotation(
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(home.PlayerHome,Vector3.forward));
            var batches = new Dictionary<(int x,int z,Material material,bool solid),List<CombineInstance>>();
            foreach (var fixture in plan.Fixtures)
            {
                if (include != null && !include(fixture.Position)) continue;
                Vector3 p = home == null ? fixture.Position : PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome,fixture.Position);
                if (home != null && (p.x < HomeExteriorViewBuilder.ContextMinimumX ||
                    new Vector2(p.x-PlayerHomeBalconyGeometry.HomeFacadeX,p.z).magnitude > HomeExteriorContextPlanner.ViewRadius)) continue;
                Quaternion rotation = basis*fixture.Rotation;
                var matrix = Matrix4x4.TRS(p,rotation,fixture.Scale);
                bool head = fixture.Kind == CityWaterKit.OutfallHead;
                if (head || fixture.Kind == CityWaterKit.OutfallStream)
                {
                    var pivot = new GameObject(fixture.Kind.ToString()).transform;
                    pivot.SetParent(root,false);
                    pivot.SetLocalPositionAndRotation(p,rotation);
                    foreach (var part in Templates[fixture.Kind])
                        Batch(pivot,part.Material,new[] { new CombineInstance { mesh = part.Mesh,transform = part.Basis } },
                            solid: head && home == null, receivingBed: head ? null : plan);
                    if (!head) pivot.gameObject.AddComponent<CityWaterOutfall>().Initialize(plan);
                    continue;
                }
                foreach (var part in Templates[fixture.Kind])
                {
                    bool solid = home == null && (fixture.Kind == CityWaterKit.StreetBarrel ||
                        fixture.Kind == CityWaterKit.StreetBend || fixture.Kind == CityWaterKit.GroundShoe ||
                        fixture.Kind == CityWaterKit.LineValve);
                    var key = (Mathf.FloorToInt(p.x/45f),Mathf.FloorToInt(p.z/45f),part.Material,solid);
                    if (!batches.TryGetValue(key,out var batch)) batches.Add(key,batch = new List<CombineInstance>());
                    batch.Add(new CombineInstance { mesh = part.Mesh,transform = matrix*part.Basis });
                }
                if (fixture.Kind == CityWaterKit.VentGrille)
                {
                    Transform vent = new GameObject("Warm exhaust").transform;
                    vent.SetParent(root,false);
                    vent.localPosition = p + rotation*new Vector3(0,.39f,.14f);
                    vent.gameObject.AddComponent<CityWaterVent>().Initialize(basis);
                }
                if (home == null && fixture.Kind == CityWaterKit.PumpHeader)
                    Solid(p + Vector3.up*.42f,rotation,new Vector3(2.22f,.98f,.39f));
            }
            foreach (var pair in batches) Batch(root,pair.Key.material,pair.Value.ToArray(),solid:pair.Key.solid);

            void Solid(Vector3 center, Quaternion orientation, Vector3 size)
            {
                var body = new GameObject("Pipework solid").transform;
                body.SetParent(root,false); body.localPosition = center; body.localRotation = orientation;
                body.gameObject.AddComponent<BoxCollider>().size = size;
            }
        }

        private static void Batch(Transform root, Material material, CombineInstance[] parts,
            bool solid = false, CityWaterNetwork receivingBed = null)
        {
            var instance = new GameObject(material.name);
            instance.transform.SetParent(root,false);
            var mesh = new Mesh { name = "Municipal pipework" };
            int vertexCount = 0;
            foreach (var part in parts) vertexCount += part.mesh.vertexCount;
            if (vertexCount > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(parts);
            if (receivingBed != null)
            {
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    float z = vertices[i].z;
                    vertices[i].y += receivingBed.ReceivingBedOffset(z) * Mathf.SmoothStep(0f,1f,(z-.88f)/.4f);
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                var bounds = mesh.bounds;
                bounds.Expand(.1f);
                mesh.bounds = bounds;
            }
            if (solid) instance.AddComponent<MeshCollider>().sharedMesh = mesh;
            mesh.UploadMeshData(true);
            instance.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (receivingBed != null) renderer.shadowCastingMode = ShadowCastingMode.Off;
            instance.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
        }
    }

    internal sealed class CityWaterVent : MonoBehaviour
    {
        private ParticleSystem plume;
        private Quaternion basis;
        private float next;
        internal void Initialize(Quaternion fromCity)
        {
            basis = fromCity;
            plume = gameObject.AddComponent<ParticleSystem>();
            plume.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = plume.main;
            main.playOnAwake=false; main.maxParticles=12; main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startLifetime=1.65f; main.startSpeed=0f; main.startSize=.18f;
            var emission=plume.emission; emission.enabled=false;
            var shape=plume.shape; shape.enabled=false;
            var size=plume.sizeOverLifetime; size.enabled=true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f,.45f,1f,2.7f));
            var fade = plume.colorOverLifetime; fade.enabled=true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white,0f),new GradientColorKey(Color.white,1f) },
                new[] { new GradientAlphaKey(0f,0f),new GradientAlphaKey(.32f,.17f),new GradientAlphaKey(0f,1f) });
            fade.color=gradient;
            var renderer=plume.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial=CityNightResources.AtmosphereMaterial; renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false; plume.Play();
        }
        private void Update()
        {
            if (GameTimeScaleRuntime.IsPaused || Time.time < next) return;
            next = Time.time+.19f;
            var camera = Camera.main;
            if (camera == null || (camera.transform.position-transform.position).sqrMagnitude > 50f*50f) return;
            float phase = Time.time*.82f + transform.position.x*.12f;
            if (Mathf.Sin(phase)<.04f) return;
            WindSample wind = GameWeatherRules.EvaluateCurrentWind();
            var particle = new ParticleSystem.EmitParams {
                position=transform.position,
                velocity=Vector3.up*(.48f+.1f*Mathf.Sin(phase*4f)) + basis*wind.HorizontalDirection*(.19f+.48f*wind.Strength01),
                startColor=new Color(.76f,.78f,.75f,.56f), rotation=phase*30f
            };
            plume.Emit(particle,1);
        }
    }

    internal sealed class CityWaterOutfall : MonoBehaviour
    {
        private ParticleSystem spray;
        private float nextDistanceCheck;

        internal void Initialize(CityWaterNetwork plan)
        {
            var impact = new GameObject("Outfall splash").transform;
            impact.SetParent(transform,false);
            impact.localPosition = new Vector3(.06f,.10f+plan.ReceivingBedOffset(1.28f),1.28f);
            spray = impact.gameObject.AddComponent<ParticleSystem>();
            spray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            spray.useAutoRandomSeed = false;
            spray.randomSeed = 691;
            var main = spray.main;
            main.playOnAwake = false;
            main.maxParticles = 28;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.20f,.43f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(.045f,.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.55f,.54f,.46f,.86f),new Color(.73f,.71f,.59f,.92f));
            main.gravityModifier = .68f;
            var shape = spray.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.85f,.035f,.14f);
            var emission = spray.emission;
            emission.rateOverTime = 44f;
            var velocity = spray.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-.68f,.68f);
            velocity.y = new ParticleSystem.MinMaxCurve(.85f,1.62f);
            velocity.z = new ParticleSystem.MinMaxCurve(.21f,.84f);
            var size = spray.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f,1f,1f,.12f));
            var renderer = spray.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_SoftParticleDistance",.085f);
            properties.SetFloat("_EdgePower",.75f);
            properties.SetFloat(CityNightResources.ReceiveSkyFillId,1f);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void Update()
        {
            if (Time.time < nextDistanceCheck || GameTimeScaleRuntime.IsPaused) return;
            nextDistanceCheck = Time.time + .35f;
            var camera = Camera.main;
            bool near = camera != null && (camera.transform.position-transform.position).sqrMagnitude < 45f*45f;
            if (near && !spray.isPlaying) spray.Play();
            else if (!near && spray.isPlaying) spray.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
