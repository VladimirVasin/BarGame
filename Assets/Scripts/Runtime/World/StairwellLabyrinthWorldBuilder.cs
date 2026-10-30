using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Places the finite Blender-authored route; collision and foot contacts share its measured plan.</summary>
    public static class StairwellLabyrinthWorldBuilder
    {
        private const string ResourcePath = "Stairwell/Labyrinth/Labyrinth3D";
        private static GameObject template;
        private static Manifest manifest;
        [Serializable] private sealed class Manifest { public string contract_signature; public Part[] parts; }
        [Serializable] private sealed class Part { public string name, role; public int stage; public float[] bounds_min, bounds_max; }

        public static Transform Build(Transform parent, StairwellLabyrinthPlan plan, List<Collider> stairColliders)
        {
            if (template == null) template = Resources.Load<GameObject>(ResourcePath);
            if (manifest == null)
            {
                TextAsset source = Resources.Load<TextAsset>(ResourcePath);
                if (source != null) manifest = JsonUtility.FromJson<Manifest>(source.text);
            }
            if (template == null || manifest?.parts == null)
                throw new InvalidOperationException("Missing authored Stairwell labyrinth. Run its Blender generator.");
            if (manifest.contract_signature != plan.ContractSignature)
                throw new InvalidOperationException("Stairwell labyrinth model/runtime graph are out of sync. Run its Blender generator.");
            var wrapper = new GameObject("Stairwell Labyrinth");
            wrapper.transform.SetParent(parent, false);
            GameObject model = Object.Instantiate(template, wrapper.transform, false);
            var byName = new Dictionary<string, MeshRenderer>(StringComparer.Ordinal);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true)) byName.Add(renderer.name, renderer);
            foreach (Part part in manifest.parts)
            {
                if (!byName.TryGetValue(part.name, out MeshRenderer renderer))
                    throw new InvalidOperationException("Labyrinth is missing " + part.name);
                ValidateBounds(wrapper.transform, renderer, part);
                if (part.role == "Collision" || part.role == "ShellCollision" || part.role == "DecorCollision")
                {
                    renderer.enabled = false;
                    MeshCollider collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    FootstepGround.Stamp(collider, part.stage == 1 ? FootstepGroundKind.Concrete : FootstepGroundKind.Metal);
                    stairColliders.Add(collider);
                    continue;
                }
                if (part.role == "Plaster" || part.role == "Wood" || part.role == "DoorPaint" || part.role == "Wear" || (part.stage == 1 && part.role == "Concrete"))
                {
                    StairwellSurfaceKind kind = part.role == "Plaster" ? StairwellSurfaceKind.WallPaint :
                        part.role == "Wood" ? StairwellSurfaceKind.DirtyWood :
                        part.role == "DoorPaint" ? StairwellSurfaceKind.DoorPaint :
                        part.role == "Wear" ? StairwellSurfaceKind.Damage : StairwellSurfaceKind.StairConcrete;
                    Color color = part.role == "Plaster" ? new Color(.46f,.48f,.40f) :
                        part.role == "Wood" ? new Color(.27f,.20f,.12f) :
                        part.role == "Wear" ? new Color(.18f,.20f,.16f) : new Color(.38f,.40f,.33f);
                    StairwellSurfaceAppearance.Apply(renderer, kind, StairwellSurfaceProjection.BoxXZ, color);
                    // The imported UVs are already metre coordinates, unlike the old normalized parts.
                    var properties = new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
                    float scale = 1f / StairwellSurfaceAppearance.GetRecipe(kind).MetersPerTile;
                    properties.SetVector("_BaseMap_ST", new Vector4(scale,scale,0f,0f));renderer.SetPropertyBlock(properties);
                }
                else
                {
                    renderer.sharedMaterial = part.role == "Lens" ?
                        WarehouseArenaAssetProvider.Surface(part.stage == 3 ? "EmergencyRoomLamp" : "FishStationLamp", part.stage == 3 ? "SignalRed" : "Lamp") :
                        WarehouseArenaAssetProvider.GetPropSurface(part.role == "Decor" ? "WornSteel" : part.role);
                }
            }
            BuildFootProbes(wrapper.transform, plan);
            GameObject door = ApartmentEntranceDoor.Create(wrapper.transform, plan.DoorPosition, "Labyrinth Warehouse Door");
            door.transform.localRotation = Quaternion.Euler(0f,180f,0f);
            BoxCollider leaf = door.AddComponent<BoxCollider>();
            leaf.center = new Vector3(0f,1.115f,0f);
            leaf.size = new Vector3(1.12f,2.16f,.052f);
            // The new placement aligns the existing hardware with the production DoorUseLoop.
            // Its mesh and leaf remain shared with Home/the warehouse.
            float handleLift = PlayerFactory.GroundedRootOffset + 1.1148247f - 1.065f;
            foreach (string name in new[] { "Home Entry Door Handle Plates", "Home Entry Door Levers", "Home Entry Door Hardware Insets", "Home Entry Door Peephole" })
            {
                Transform part = door.transform.Find(name);
                if (part != null) part.localPosition += Vector3.up * handleLift;
            }
            return door.transform;
        }

        private static void BuildFootProbes(Transform root, StairwellLabyrinthPlan plan)
        {
            foreach (StairwellFlightPlan flight in plan.Flights)
            {
                Quaternion rotation = Quaternion.Euler(0f, Mathf.Atan2(flight.Direction.x, flight.Direction.y) * Mathf.Rad2Deg, 0f);
                for (int i=0;i<flight.StepCount;i++)
                {
                    var tread = new GameObject(flight.Id + " tread probe " + i);
                    tread.transform.SetParent(root,false);
                    Vector2 center = flight.Start + flight.Direction * ((i+.5f)*flight.StepDepth);
                    tread.transform.localPosition = new Vector3(center.x, flight.BaseElevation+(i+1)*flight.StepRise-.08f, center.y);
                    tread.transform.localRotation = rotation;
                    tread.transform.localScale = new Vector3(flight.Width,.16f,.25f);
                    FootProbeSurface.AddTreadCollider(tread);
                }
            }
        }

        private static void ValidateBounds(Transform root, Renderer renderer, Part part)
        {
            Bounds world = renderer.bounds;Bounds local=default;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 p=root.InverseTransformPoint(world.center+Vector3.Scale(world.extents,
                    new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                if(corner==0)local=new Bounds(p,Vector3.zero);else local.Encapsulate(p);
            }
            Vector3 lo=new Vector3(part.bounds_min[0],part.bounds_min[1],part.bounds_min[2]);
            Vector3 hi=new Vector3(part.bounds_max[0],part.bounds_max[1],part.bounds_max[2]);
            if(Vector3.Distance(local.min,lo)>.03f||Vector3.Distance(local.max,hi)>.03f)
                throw new InvalidOperationException("Labyrinth metre bounds mismatch: "+part.name+" "+local);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache(){template=null;manifest=null;}
    }
}
