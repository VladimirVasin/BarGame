using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Passive metre-authored kit, shared surface materials and one owned cable mesh.</summary>
    public static class CityCoastalInfrastructureWorldBuilder
    {
        public const string ResourcePath = "City/CoastalInfrastructure/CityCoastalInfrastructure3D";
        public const string RootName = "Coastal Electrical And Service Infrastructure";
        private const float WireRadius = .015f;
        // The pinned FBX import has a 180-degree Y basis relative to these
        // Unity-authored recipes. Apply the same measured correction to whole
        // assemblies and to the cable's deformation source, exactly once.
        public static Quaternion ImportedBasisCorrection => Quaternion.Euler(0f,180f,0f);
        private static readonly Dictionary<string, Transform> Templates = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private static Vector3[] cableVertices;
        private static int[] cableTriangles;
        private static Vector2[] cableUv;

        public static GameObject Build(Transform parent, CityCoastalInfrastructurePlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return null;
            EnsureTemplates();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            for (int i = 0; i < plan.Facilities.Count; i++)
            {
                CityCoastalFacility site = plan.Facilities[i];
                Transform instance = Place(root, site.Assembly, site.StableId, site.Position, site.Rotation);
                BoxCollider collider = instance.gameObject.AddComponent<BoxCollider>();
                collider.center = Vector3.up * (site.Size.y * .5f);
                collider.size = site.Size;
            }
            for (int i = 0; i < plan.Poles.Count; i++)
            {
                Transform pole = Place(root, "PowerPole", "Coastal Power Pole " + i, plan.Poles[i], Quaternion.LookRotation(plan.PoleForwards[i],Vector3.up));
                BoxCollider collider = pole.gameObject.AddComponent<BoxCollider>();
                collider.center = Vector3.up * 4.15f;
                collider.size = new Vector3(.38f,8.3f,.38f);
            }
            for (int i = 0; i < plan.FacadeInlets.Count; i++)
                Place(root,"FacadeInlet","Coastal Facade Inlet "+i,plan.FacadeInlets[i],Quaternion.identity);
            BuildCables(root, plan.CableSpans);
            return root.gameObject;
        }

        public static GameObject BuildHomeExterior(Transform parent, HomeExteriorContextPlan context)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (context == null) throw new ArgumentNullException(nameof(context));
            CityCoastalInfrastructurePlan plan = CityCoastalInfrastructurePlan.ForLayout(context.Layout);
            if (!plan.IsEnabled) return null;
            EnsureTemplates();
            Transform root = new GameObject("Home " + RootName).transform;
            root.SetParent(parent, false);
            Vector3 Local(Vector3 world) => PlayerHomeBalconyGeometry.ToHomeLocal(context.PlayerHome, world);
            Vector3 forward = PlayerHomeBalconyGeometry.ToHomeLocalDirection(context.PlayerHome, Vector3.forward);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            for (int i = 0; i < plan.Facilities.Count; i++)
            {
                CityCoastalFacility site = plan.Facilities[i];
                Vector3 local = Local(site.Position);
                float radius = new Vector2(site.Size.x,site.Size.z).magnitude*.5f;
                if (!InsideExterior(local,radius)) continue;
                Place(root,site.Assembly,site.StableId,local,rotation * site.Rotation);
            }
            for (int i = 0; i < plan.Poles.Count; i++)
            {
                Vector3 local = Local(plan.Poles[i]);
                if (InsideExterior(local,.9f))
                    Place(root,"PowerPole","Exterior Power Pole "+i,local,rotation * Quaternion.LookRotation(plan.PoleForwards[i],Vector3.up));
            }
            for (int i = 0; i < plan.FacadeInlets.Count; i++)
            {
                Vector3 local = Local(plan.FacadeInlets[i]);
                if (InsideExterior(local,.3f))
                    Place(root,"FacadeInlet","Exterior Facade Inlet "+i,local,rotation);
            }
            var visible = new List<CityCoastalCableSpan>(plan.CableSpans.Count);
            for (int i = 0; i < plan.CableSpans.Count; i++)
            {
                CityCoastalCableSpan source = plan.CableSpans[i];
                var span = new CityCoastalCableSpan(Local(source.Start),Local(source.End),source.Sag,source.IsServiceDrop);
                if (ClipExterior(span,out CityCoastalCableSpan clipped)) visible.Add(clipped);
            }
            BuildCables(root,visible);
            return root.gameObject;
        }

        private static bool InsideExterior(Vector3 position,float radius) =>
            position.x-radius>=HomeExteriorViewBuilder.ExteriorMinimumX &&
            new Vector2(position.x,position.z).magnitude+radius<=48f;

        private static bool ClipExterior(CityCoastalCableSpan source,out CityCoastalCableSpan clipped)
        {
            clipped=default;
            Vector3 delta=source.End-source.Start;
            float start=0f,end=1f;
            float minimum=HomeExteriorViewBuilder.ExteriorMinimumX+WireRadius;
            if(Mathf.Abs(delta.x)<.0001f) { if(source.Start.x<minimum)return false; }
            else if(delta.x>0f)start=Mathf.Max(start,(minimum-source.Start.x)/delta.x);
            else end=Mathf.Min(end,(minimum-source.Start.x)/delta.x);
            Vector2 a=new Vector2(source.Start.x,source.Start.z),d=new Vector2(delta.x,delta.z);
            float aa=d.sqrMagnitude,bb=2f*Vector2.Dot(a,d),cc=a.sqrMagnitude-47.95f*47.95f;
            if(aa<.000001f) { if(cc>0f)return false; }
            else
            {
                float discriminant=bb*bb-4f*aa*cc;
                if(discriminant<0f)return false;
                float root=Mathf.Sqrt(discriminant);
                start=Mathf.Max(start,(-bb-root)/(2f*aa));
                end=Mathf.Min(end,(-bb+root)/(2f*aa));
            }
            if(end-start<.001f)return false;
            clipped=new CityCoastalCableSpan(source.Sample(start),source.Sample(end),
                source.Sag*(end-start)*(end-start),source.IsServiceDrop);
            return true;
        }

        private static void EnsureTemplates()
        {
            if (Templates.Count != 0 && Templates["PowerPole"] != null) return;
            Templates.Clear();
            GameObject source=Resources.Load<GameObject>(ResourcePath);
            if(source==null)throw new InvalidOperationException("Missing authored coastal utility kit: "+ResourcePath);
            string[] names={"Substation","MunicipalWorkshop","PortWorkshop","PowerPole","FacadeInlet","CableSpan"};
            foreach(Transform transform in source.GetComponentsInChildren<Transform>(true))
                if(Array.IndexOf(names,transform.name)>=0)Templates.Add(transform.name,transform);
            if(Templates.Count!=names.Length)throw new InvalidOperationException("Coastal utility kit has missing assembly roots.");
            MeshFilter cable=Templates["CableSpan"].GetComponentInChildren<MeshFilter>(true);
            if(cable==null || cable.sharedMesh==null)throw new InvalidOperationException("Missing authored cable topology.");
            Vector3[] imported=cable.sharedMesh.vertices;
            cableVertices=new Vector3[imported.Length];
            for(int i=0;i<imported.Length;i++)cableVertices[i]=ImportedBasisCorrection * cable.transform.TransformPoint(imported[i]);
            cableTriangles=cable.sharedMesh.triangles;
            cableUv=cable.sharedMesh.uv;
        }

        private static Transform Place(Transform parent,string assembly,string name,Vector3 position,Quaternion rotation)
        {
            Transform template=Templates[assembly];
            Transform placement=new GameObject(name).transform;
            placement.SetParent(parent,false);placement.localPosition=position;placement.localRotation=rotation;
            Transform model=Object.Instantiate(template,placement,false);
            model.localPosition=Vector3.zero;model.localRotation=ImportedBasisCorrection * template.rotation;model.localScale=template.lossyScale;
            foreach(MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                int separator=renderer.name.LastIndexOf("__",StringComparison.Ordinal);
                string role=renderer.name.Substring(separator+2);
                renderer.sharedMaterial = role == "Glass"
                    ? CityWindowAppearance.ResolveLitMaterial(CityWindowFamily.Warm)
                    : role == "TaskLightLens" ? CityNightResources.EmissiveMaterial
                    : CityPortAssetProvider.GetSurfaceMaterial(role);
                if (role == "Glass") CityWindowAppearance.ApplyLitPane(renderer, (uint)assembly.Length);
                if (role == "TaskLightLens")
                {
                    CityIndustrialPurpose purpose = assembly == "Substation" ? CityIndustrialPurpose.CompressorHouse :
                        assembly == "MunicipalWorkshop" ? CityIndustrialPurpose.RepairWorkshop : CityIndustrialPurpose.MetalWorkshop;
                    Color color = CityNightAtmosphere.GetIndustrialTaskLightColor(purpose);
                    CityNightGlowRegistry.Register(renderer, new Color(color.r * 3f, color.g * 3f, color.b * 3f, 1f));
                }
                renderer.shadowCastingMode=role == "TaskLightLens" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows=role != "TaskLightLens";
                renderer.lightProbeUsage=LightProbeUsage.BlendProbes;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            }
            // The empty shares the model's measured metre basis. Aim is a
            // logical contract in Unity space, independent of the FBX empty basis.
            foreach (Transform anchor in model.GetComponentsInChildren<Transform>(true))
                if (anchor.name.StartsWith(CityNightAtmosphere.IndustrialTaskLightPrefix, StringComparison.Ordinal))
                    anchor.rotation = placement.rotation * Quaternion.LookRotation(new Vector3(0f,-.819152f,.573576f), Vector3.up);
            return placement;
        }

        private static void BuildCables(Transform parent,IReadOnlyList<CityCoastalCableSpan> spans)
        {
            if(spans.Count==0)return;
            if(spans.Count>CityCoastalInfrastructurePlan.MaximumCableSpanCount)
                throw new InvalidOperationException("Coastal cable population exceeds its bounded plan.");
            int count=spans.Count*cableVertices.Length;
            var vertices=new Vector3[count];var uv=new Vector2[count];
            var triangles=new int[spans.Count*cableTriangles.Length];
            for(int s=0;s<spans.Count;s++)
            {
                CityCoastalCableSpan span=spans[s];
                Vector3 delta=span.End-span.Start;
                Vector3 right=new Vector3(delta.z,0f,-delta.x).normalized;
                int offset=s*cableVertices.Length;
                for(int v=0;v<cableVertices.Length;v++)
                {
                    Vector3 source=cableVertices[v];float t=Mathf.Clamp01(source.z);
                    float curve=4f*t*(1f-t);
                    vertices[offset+v]=span.Sample(t)+right*source.x+
                        Vector3.up*(source.y+.70f*curve);
                    uv[offset+v]=cableUv[v];
                }
                for(int t=0;t<cableTriangles.Length;t++)
                    triangles[s*cableTriangles.Length+t]=offset+cableTriangles[t];
            }
            var mesh=new Mesh {name="Coastal Authored Cable Network",indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
            GameObject cable=new GameObject("Coastal Electrical Conductors");cable.transform.SetParent(parent,false);
            cable.AddComponent<MeshFilter>().sharedMesh=mesh;
            MeshRenderer renderer=cable.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=CityPortAssetProvider.GetSurfaceMaterial("Rubber");
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            renderer.lightProbeUsage=LightProbeUsage.BlendProbes;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            cable.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
        }
    }
}
