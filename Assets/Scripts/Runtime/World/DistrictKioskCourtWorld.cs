using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal sealed class DistrictKioskCourtWorld : MonoBehaviour
    {
        internal const string ResidentialLamp = "ResidentialKioskCourt";
        internal const string IndustrialLamp = "IndustrialKioskCourt";
        internal DistrictKioskCourtPlan Plan { get; private set; }
        private readonly Transform[] tables = new Transform[2];
        private readonly Transform[] seats = new Transform[2];
        private readonly VodkaBottleView[] empties = new VodkaBottleView[4];
        private Transform cloth, chair, crate, spareCrate, crateStand, carryingCrate;
        private Pose chairRest;
        private int returned;
        private bool nearTableDirty, wiping, swapCommitted;

        internal Transform Table(int index) => tables[index];
        internal Transform Seat(int slot) => seats[slot];
        internal bool NeedsChore => Plan.Residential ? nearTableDirty : returned >= 2;
        internal int Returned => returned;
        internal int ChoresCompleted { get; private set; }
        internal static bool IsLampAnchor(string name) =>
            name == CityNightAtmosphere.IndustrialTaskLightPrefix + ResidentialLamp ||
            name == CityNightAtmosphere.IndustrialTaskLightPrefix + IndustrialLamp;

        internal static DistrictKioskCourtWorld Build(Transform parent, DistrictKioskCourtPlan plan,
            bool interactive, List<Collider> rainShelters)
        {
            var host = new GameObject(plan.Residential ? "Residential kiosk side court" : "Industrial kiosk rear court");
            host.transform.SetParent(parent, false);
            var template = Resources.Load<GameObject>("RidgeKiosk/" + plan.Model);
            if (!template) throw new InvalidOperationException("Missing " + plan.Model + " model.");
            Transform model = Instantiate(template, host.transform, false).transform;
            var court = host.AddComponent<DistrictKioskCourtWorld>();
            court.Plan = plan;
            court.tables[0] = Find("Table0");
            court.tables[1] = Find("Table1");

            foreach (MeshRenderer renderer in Find("Floor").GetComponentsInChildren<MeshRenderer>())
            {
                CityWetSurfaceRegistry.Register(renderer, CityWetSurfaceKind.Sidewalk);
                if (interactive)
                {
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                    FootstepGround.Stamp(renderer.gameObject, FootstepGroundKind.Concrete);
                }
            }

            if (interactive)
            {
                AddCollision(court.tables[0]);
                AddCollision(court.tables[1]);
            }
            if (plan.Residential)
            {
                court.seats[0] = Find("Seat0");
                court.seats[1] = Find("Seat1");
                court.chair = Find("LooseChair");
                court.chairRest = new Pose(court.chair.position, court.chair.rotation);
                court.cloth = BindMovable(Find("Cloth"), "Cloth grip");
                court.cloth.gameObject.SetActive(false);
                if (interactive)
                {
                    AddCollision(court.seats[0]);
                    AddCollision(court.seats[1]);
                    AddCollision(court.chair);
                    var shelter = CityTerracePilotWorld.Box(host.transform, "Residential kiosk rain shelter",
                        new Vector3(-2.23f, 2.38f, .81f), new Vector3(2.55f, .04f, .81f), Quaternion.identity);
                    shelter.isTrigger = true;
                    rainShelters.Add(shelter);
                }
            }
            else
            {
                court.crateStand = Find("CrateStand");
                court.crate = BindMovable(Find("Crate"), "Bottle crate dock");
                court.crate.position = court.ActiveCrateDock;
                if (interactive)
                {
                    AddCollision(court.crateStand);
                    court.spareCrate = Instantiate(court.crate, host.transform, true);
                    court.spareCrate.name = "Spare bottle crate";
                    court.spareCrate.position = court.InsideCrate;
                }
            }

            Transform lens = Find("Lens");
            foreach (var renderer in lens.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.name.EndsWith("_KioskLampGlass", StringComparison.Ordinal)) continue;
                renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                CityNightGlowRegistry.Register(renderer, plan.Residential
                    ? new Color(.92f, .68f, .40f) : new Color(.70f, .78f, .74f));
            }
            var source = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix +
                (plan.Residential ? ResidentialLamp : IndustrialLamp)).transform;
            source.SetParent(host.transform, false);
            source.SetPositionAndRotation(lens.position - Vector3.up * .05f,
                Quaternion.LookRotation(parent.TransformDirection(plan.Residential
                    ? new Vector3(-.30f, -1f, -.42f) : new Vector3(.70f, -1f, -.44f)), parent.forward));
            return court;

            Transform BindMovable(Transform imported, string name)
            {
                // The Blender empty has an import-axis rotation. Drive an
                // upright metre-space parent and preserve the whole mesh pose.
                var frame = new GameObject(name).transform;
                frame.SetParent(host.transform, false);
                frame.SetPositionAndRotation(imported.position, host.transform.rotation);
                imported.SetParent(frame, true);
                return frame;
            }

            Transform Find(string name) => CityPedestrianHandProps.FindSocket(model, name) ??
                throw new InvalidOperationException(plan.Model + " is missing anchor " + name);
        }

        private static void AddCollision(Transform part)
        {
            foreach (var filter in part.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }

        private Vector3 InsideCrate => transform.TransformPoint(new Vector3(-.83f, .30f, -.74f));
        private Vector3 ActiveCrateDock => crateStand.position - transform.forward * .17f;
        private Vector3 SpareCrateDock => crateStand.position + transform.forward * .18f;
        internal Vector3 ChairBack => chair.position + transform.TransformDirection(new Vector3(-.14f, .31f, 0f));
        internal void MarkDeparture(int slot)
        {
            if (!Plan.Residential || slot != 2) return;
            nearTableDirty = true;
            chair.position = chairRest.position - transform.right * .11f;
            chair.rotation = Quaternion.AngleAxis(-9f, Vector3.up) * chairRest.rotation;
        }

        internal Vector3 BottleDock => crate.TransformPoint(
            new Vector3((returned % 2 == 0 ? -.084f : .084f), .074f, returned < 2 ? -.063f : .063f));

        internal bool ReturnBottle(VodkaBottleView bottle)
        {
            if (!bottle || swapCommitted || returned >= empties.Length) return false;
            Vector3 dock = BottleDock;
            bottle.transform.SetParent(crate, true);
            bottle.transform.SetPositionAndRotation(dock, crate.rotation);
            float bottom = float.PositiveInfinity;
            foreach (Renderer glass in bottle.GlassCells)
                bottom = Mathf.Min(bottom, glass.bounds.min.y);
            if (float.IsFinite(bottom)) bottle.transform.position += Vector3.up * (dock.y - bottom);
            empties[returned++] = bottle;
            return true;
        }

        internal void BeginChore()
        {
            if (Plan.Residential) cloth.gameObject.SetActive(true);
            else
            {
                swapCommitted = false;
                carryingCrate = spareCrate;
            }
        }

        internal void CarryChore(VillageResidentPresentation seller)
        {
            Vector3 point = seller.transform.TransformPoint(new Vector3(0f, .94f, .36f));
            if (carryingCrate)
            {
                carryingCrate.SetPositionAndRotation(point, seller.transform.rotation);
                seller.ApplyHandContacts(point + seller.transform.right * .18f + Vector3.up * .14f,
                    point - seller.transform.right * .18f + Vector3.up * .14f);
            }
            if (Plan.Residential && cloth.gameObject.activeSelf)
            {
                point = seller.transform.TransformPoint(new Vector3(.20f, .98f, .20f));
                cloth.SetPositionAndRotation(point, seller.transform.rotation);
                seller.ApplyHandContacts(point, null, .9f);
            }
        }

        internal bool PresentChore(VillageResidentPresentation seller, float elapsed)
        {
            seller.ApplyIdleVariation(elapsed, false, 0f);
            if (Plan.Residential)
            {
                Vector3 top = tables[0].position;
                float wipe = Mathf.Clamp01(elapsed / 2.4f);
                Vector3 point = top + transform.right * (.25f * Mathf.Sin(wipe * Mathf.PI * 4f)) +
                    transform.forward * Mathf.Lerp(.24f, -.13f, wipe) + Vector3.up * .017f;
                wiping = true;
                cloth.SetPositionAndRotation(point, transform.rotation);
                seller.ApplyHandContacts(point, top + transform.forward * .28f - transform.right * .34f);
                return elapsed >= 2.4f;
            }

            Vector3 hands = seller.transform.TransformPoint(new Vector3(0f, .94f, .36f));
            Vector3 staging = SpareCrateDock;
            if (elapsed < .65f)
            {
                PlaceCrate(spareCrate, Vector3.Lerp(hands, staging, elapsed / .65f));
                return false;
            }
            if (!swapCommitted)
            {
                carryingCrate = crate;
                crate = spareCrate;
                spareCrate = carryingCrate;
                swapCommitted = true;
            }
            float lift = Mathf.Clamp01((elapsed - .65f) / .85f);
            PlaceCrate(carryingCrate, Vector3.Lerp(ActiveCrateDock, hands, lift));
            crate.position = Vector3.Lerp(staging, ActiveCrateDock, Mathf.InverseLerp(.30f, 1f, lift));
            return elapsed >= 1.50f;

            void PlaceCrate(Transform item, Vector3 point)
            {
                item.SetPositionAndRotation(point, transform.rotation);
                Vector3 edge = transform.forward * .13f;
                seller.ApplyHandContacts(point + edge + Vector3.up * .19f, point - edge + Vector3.up * .19f);
            }
        }

        internal bool PresentChair(VillageResidentPresentation seller, float elapsed)
        {
            float push = Mathf.SmoothStep(0f, 1f, elapsed / .75f);
            chair.position = chairRest.position - transform.right * (.11f * (1f - push));
            chair.rotation = Quaternion.AngleAxis(-9f * (1f - push), Vector3.up) * chairRest.rotation;
            seller.ApplyIdleVariation(elapsed, false, 0f);
            CarryChore(seller);
            seller.ApplyHandContacts(null, ChairBack);
            return elapsed >= .75f;
        }

        internal void FinishChore(bool completed)
        {
            if (Plan.Residential)
            {
                if (cloth) cloth.gameObject.SetActive(false);
                if (completed && wiping) { nearTableDirty = false; ChoresCompleted++; }
                wiping = false;
                return;
            }
            if (!spareCrate) return;
            if (swapCommitted)
            {
                for (int i = 0; i < returned; i++)
                {
                    if (empties[i]) Destroy(empties[i].gameObject);
                    empties[i] = null;
                }
                returned = 0;
                if (completed) ChoresCompleted++;
            }
            crate.SetPositionAndRotation(ActiveCrateDock, transform.rotation);
            spareCrate.SetPositionAndRotation(InsideCrate, transform.rotation);
            carryingCrate = null;
            swapCommitted = false;
        }
    }
}
