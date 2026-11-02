using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Finite hollow collision behind the bus's exterior traffic box. Created only
    /// for a passenger's released bottle; the traffic hull remains unchanged.
    /// </summary>
    internal sealed class CityBusCabinBottleCollision : IDisposable
    {
        private readonly CityBusActor actor;
        private readonly Door[] doors = new Door[4];
        private CityBusAssetRegistry registry;
        private Transform root;
        private Vector3 scale;
        private float driverSide;

        private readonly struct Door
        {
            internal readonly Transform Source, Collision;
            internal readonly Vector3 Centre;
            internal Door(Transform source, Transform collision, Vector3 centre)
            { Source = source; Collision = collision; Centre = centre; }
        }

        internal CityBusCabinBottleCollision(CityBusActor owner) => actor = owner;

        internal void Refresh()
        {
            CityBusAssetRegistry current = actor != null && actor.IsSpawned ? actor.Presentation.Registry : null;
            if (current == null)
            {
                if (root != null) root.gameObject.SetActive(false);
                return;
            }
            if (root == null || registry != current) Build(current);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            RefreshCabinFrame();
            foreach (Door door in doors)
                if (door.Source != null && door.Collision != null)
                    door.Collision.SetPositionAndRotation(door.Source.position + door.Source.rotation * door.Centre,
                        door.Source.rotation);
        }

        private void Build(CityBusAssetRegistry current)
        {
            Dispose();
            registry = current;
            scale = new Vector3(registry.Dimensions.Width / 2.38f, registry.Dimensions.Height / 2.95f,
                registry.Dimensions.Length / 8.25f);
            driverSide = Mathf.Sign(Vector3.Dot(registry.DriverSeatAnchor.position - actor.Position, actor.transform.right));
            if (driverSide == 0f) driverSide = -1f;
            var owner = new GameObject("Bus cabin bottle collision") { layer = CityBusCollision.LayerIndex };
            root = owner.transform;
            root.SetParent(actor.transform, false);
            FootstepGround.Stamp(owner, FootstepGroundKind.Metal);

            // Source box dimensions from build-city-bus-3d-model.py. Source axes
            // are +X driver side, -Y forward, +Z up. Only the hollow cabin shell
            // is represented: no new visible mesh, seats or passenger collision.
            Box("Floor", 0f, 0f, .62f, 2.30f, 7.98f, .10f);
            Box("Ceiling", 0f, 0f, 2.75f, 2.30f, 7.92f, .06f);
            Side("Driver side", 1f, 0f, 7.94f);
            Side("Door front end", -1f, -3.91f, .16f, false);
            Side("Between doors", -1f, -.84f, 2.74f, false);
            Side("Door rear end", -1f, 3.04f, 2f, false);
            Box("Door header", -1.145f, 0f, 2.66f, .10f, 7.90f, .34f);
            Box("Front lower", 0f, -4.045f, .92f, 2.28f, .16f, .92f);
            Box("Front glass", 0f, -4.132f, 2.025f, 2.28f, .025f, 1.29f);
            Box("Front header", 0f, -4.045f, 2.70f, 2.28f, .16f, .42f);
            Box("Rear lower", 0f, 4.045f, 1.12f, 2.28f, .16f, 1.32f);
            Box("Rear glass", 0f, 4.132f, 2.14f, 2.28f, .025f, .94f);
            Box("Rear header", 0f, 4.045f, 2.73f, 2.28f, .16f, .36f);
            DoorFrame("Front", -3.05f);
            DoorFrame("Rear", 1.34f);
            RefreshCabinFrame();
            doors[0] = BuildDoor(registry.FrontDoorForwardLeaf);
            doors[1] = BuildDoor(registry.FrontDoorRearwardLeaf);
            doors[2] = BuildDoor(registry.RearDoorForwardLeaf);
            doors[3] = BuildDoor(registry.RearDoorRearwardLeaf);
        }

        private void Side(string name, float side, float station, float length, bool header = true)
        {
            // Include the narrow belt trim between the lower panel and window.
            Box(name + " lower", side * 1.145f, station, 1.03f, .10f, length, 1.04f);
            Box(name + " glazing", side * 1.199f, station, 2.025f, .025f, length, 1.05f);
            if (header) Box(name + " header", side * 1.145f, station, 2.66f, .10f, length, .34f);
        }

        private void Box(string name, float x, float y, float z, float width, float length, float height)
        {
            var part = new GameObject(name) { layer = CityBusCollision.LayerIndex };
            part.transform.SetParent(root, false);
            part.transform.localPosition = Vector3.Scale(new Vector3(x * driverSide, z, -y), scale);
            BoxCollider shape = part.AddComponent<BoxCollider>();
            shape.size = Vector3.Scale(new Vector3(width, height, length), scale);
        }

        private void DoorFrame(string name, float station)
        {
            Box(name + " door front post", -1.205f, station - .76f, 1.68f, .085f, .07f, 1.72f);
            Box(name + " door rear post", -1.205f, station + .76f, 1.68f, .085f, .07f, 1.72f);
            Box(name + " door sill", -1.205f, station, .745f, .085f, 1.52f, .15f);
        }

        private Door BuildDoor(Transform leaf)
        {
            if (leaf == null) return default;
            // Bounds need no readable mesh/vertices. The four existing leaf
            // frames own both the measured box and its live hinge motion.
            Quaternion inverse = Quaternion.Inverse(leaf.rotation);
            Bounds bounds = default;
            bool found = false;
            foreach (MeshFilter filter in leaf.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 point = inverse * (filter.transform.TransformPoint(local.center +
                                Vector3.Scale(local.extents, new Vector3(x, y, z))) - leaf.position);
                            if (found) bounds.Encapsulate(point);
                            else { bounds = new Bounds(point, Vector3.zero); found = true; }
                        }
            }
            if (!found) throw new InvalidOperationException("Bus door has no authored collision bounds: " + leaf.name);
            var part = new GameObject(leaf.name + " bottle collision") { layer = CityBusCollision.LayerIndex };
            part.transform.SetParent(root, false);
            part.AddComponent<BoxCollider>().size = bounds.size;
            return new Door(leaf, part.transform, bounds.center);
        }

        private void RefreshCabinFrame()
        {
            Pose pose = ResolveFrame(actor, registry);
            root.SetPositionAndRotation(pose.position, pose.rotation);
        }

        internal static Pose ResolveFrame(CityBusActor actor, CityBusAssetRegistry registry)
        {
            // Three authored anchors reconstruct the sprung body's metric axes
            // independently of the FBX Body node's rotated/scaled local basis.
            Vector3 scale = new Vector3(registry.Dimensions.Width / 2.38f, registry.Dimensions.Height / 2.95f,
                registry.Dimensions.Length / 8.25f);
            float driverSide = Mathf.Sign(Vector3.Dot(registry.DriverSeatAnchor.position - actor.Position, actor.transform.right));
            if (driverSide == 0f) driverSide = -1f;
            Vector3 front = registry.FrontDoorEntryAnchor.position;
            Vector3 forward = (front - registry.RearDoorEntryAnchor.position).normalized;
            Vector3 lateral = Vector3.ProjectOnPlane(registry.DriverSeatAnchor.position - front, forward).normalized;
            float slope = Mathf.Atan2(.42f * scale.y, 1.40f * scale.x) * Mathf.Rad2Deg;
            Vector3 right = Quaternion.AngleAxis(-driverSide * slope, forward) * lateral * driverSide;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, up);
            Vector3 doorInFrame = Vector3.Scale(new Vector3(-.72f * driverSide, .66f, 3.05f), scale);
            return new Pose(front - rotation * doorInFrame, rotation);
        }

        public void Dispose()
        {
            if (root != null)
            {
                root.gameObject.SetActive(false);
                CityBusResources.DestroyObject(root.gameObject);
            }
            root = null;
            registry = null;
            Array.Clear(doors, 0, doors.Length);
        }
    }
}
