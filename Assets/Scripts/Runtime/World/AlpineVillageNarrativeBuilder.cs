using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed class VillageNarrativeInstance : MonoBehaviour
    {
        public VillageNarrativePoint Point { get; internal set; }
        public Transform Subject { get; internal set; }
        public NarrativeInteraction Interaction { get; internal set; }
        public Vector3 Approach { get; internal set; }
        public Bounds FocusBounds { get; internal set; }
    }

    public sealed class VillageHouseholdIdentity : MonoBehaviour
    {
        public string HouseId { get; internal set; }
        public string FamilyId => VillageHouseholdCatalog.FindHouse(HouseId)?.FamilyId;
        public bool Occupied => VillageHouseholdCatalog.IsOccupied(HouseId);
    }

    public static class AlpineVillageNarrativeBuilder
    {
        public const string RootName = "Village Narrative Objects";
        public static void Build(Transform parent, AlpineVillagePlan plan, AlpineVillageWalkableArea walkable)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            VillageNarrativeLibrary.LoadOrThrow();
            foreach (VillageNarrativePoint point in plan.Narrative.Points)
            {
                var host = new GameObject(point.Id);
                host.transform.SetParent(root.transform, false);
                GameObject subject;
                if (point.Existing)
                {
                    string existing = point.Number == 26 ? "Abandoned Truck Wreck" : "Discarded Wooden Chairs";
                    subject = parent.Find("Village Expansion/" + existing)?.gameObject;
                    if (subject == null) throw new InvalidOperationException("Missing " + existing);
                }
                else
                {
                    subject = VillageNarrativeLibrary.Create(point.Number, host.transform, "Narrative Model " + point.Number.ToString("00"));
                    subject.transform.SetPositionAndRotation(point.Position, point.Rotation);
                    if (point.Document || point.Number == 11) MountOnFacade(parent, subject.transform, point);
                }
                Bounds bounds = RendererBounds(subject.transform);
                // Notes focus only on the authored paper, never the enclosing house.
                if (point.Document) AddPaperText(subject.transform, point.Number);
                Physics.SyncTransforms();
                Vector3 front = point.Existing ? point.DiscoveryPosition - point.Position : point.Rotation * Vector3.forward;
                front.y = 0f;
                front.Normalize();
                Vector3 approach = FindApproach(plan, walkable, point, bounds, front);
                Vector3 face = bounds.center - approach;
                face.y = 0f;
                var definition = new NarrativeInteractionDefinition(point.Id,
                    AlpineVillageNarrativeContent.PromptKey(point.Number), AlpineVillageNarrativeContent.GetPages(point.Number));
                var staging = NarrativeStagingPlan.Standing(approach, Quaternion.LookRotation(face), bounds,
                    Vector3.Cross(Vector3.up, front), point.Document ? NarrativeCameraMode.DocumentCloseUp : NarrativeCameraMode.ObjectSide,
                    subject.transform.forward);
                var target = host.AddComponent<NarrativeInteraction>();
                target.Configure(definition, subject.transform, staging, approach + Vector3.up * .85f);
                var instance = host.AddComponent<VillageNarrativeInstance>();
                instance.Point = point;
                instance.Subject = subject.transform;
                instance.Interaction = target;
                instance.Approach = approach;
                instance.FocusBounds = bounds;

                // The large truck remains inspectable from either accessible end.
                // One definition/ID; no through-the-body interaction sphere.
                if (point.Number == 26)
                {
                    Vector3 other = FindApproach(plan, walkable, point, bounds, -front);
                    var second = new GameObject("Far Side Inspection");
                    second.transform.SetParent(host.transform, false);
                    Vector3 toward = bounds.center - other;
                    toward.y = 0f;
                    second.AddComponent<NarrativeInteraction>().Configure(definition, subject.transform,
                        NarrativeStagingPlan.Standing(other, Quaternion.LookRotation(toward), bounds,
                            Vector3.Cross(Vector3.up, -front)), other + Vector3.up * .85f);
                }
            }
            foreach (var plot in plan.Plots)
            {
                if (VillageHouseholdCatalog.FindHouse(plot.StableId) == null) continue;
                Transform house = parent.Find("Village Plot - " + plot.StableId);
                house.gameObject.AddComponent<VillageHouseholdIdentity>().HouseId = plot.StableId;
            }
            foreach (var plot in plan.Expansion.Abandonment.Plots)
            {
                if (VillageHouseholdCatalog.FindHouse(plot.Id) == null) continue;
                Transform house = parent.Find("Village Expansion/Abandoned Settlement/" + plot.Id);
                house.gameObject.AddComponent<VillageHouseholdIdentity>().HouseId = plot.Id;
            }
        }

        private static Vector3 FindApproach(AlpineVillagePlan plan, AlpineVillageWalkableArea walkable,
            VillageNarrativePoint point, Bounds bounds, Vector3 direction)
        {
            float reach = point.Existing
                ? Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z
                : point.Footprint.y * .5f;
            foreach (float extra in new[] { .8f, 1.15f, 1.5f, 1.9f, 2.4f })
            foreach (float side in new[] { 0f, -.6f, .6f, -1.2f, 1.2f })
            {
                Vector3 candidate = point.Position + direction * (reach + extra) +
                    Vector3.Cross(Vector3.up, direction) * side;
                candidate = AlpineVillageNarrativePlan.Ground(plan, candidate);
                if (!walkable.Contains(candidate, .34f)) continue;
                // The analytical slope can differ from the final triangulated
                // collider. Resolve the same rounded foot support as the hero's
                // capsule so the strict animated docking height is reachable.
                const float footRadius = .32f;
                Vector3 origin = candidate + Vector3.up * (footRadius + 1f);
                if (!Physics.SphereCast(origin, footRadius, Vector3.down, out RaycastHit support, 2f,
                    PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore) ||
                    support.normal.y < Mathf.Cos(PlayerFactory.SlopeLimitDegrees * Mathf.Deg2Rad)) continue;
                candidate.y = origin.y - support.distance - footRadius;
                if (Physics.CheckCapsule(candidate + Vector3.up * .41f, candidate + Vector3.up * 1.48f,
                    .30f, PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore)) continue;
                return candidate;
            }
            throw new InvalidOperationException("No free narrative approach for " + point.Id);
        }

        internal static Bounds RendererBounds(Transform subject)
        {
            Renderer[] renderers = subject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Narrative target has no model.");
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void MountOnFacade(Transform world, Transform subject, VillageNarrativePoint point)
        {
            bool core = point.HouseId.StartsWith("village-house-", StringComparison.Ordinal);
            Transform house = world.Find(core ? "Village Plot - " + point.HouseId :
                "Village Expansion/Abandoned Settlement/" + point.HouseId);
            Vector3 forward = point.Rotation * Vector3.forward;
            var ray = new Ray(point.Position + Vector3.up * (point.Number == 11 ? 1.18f : 1.45f) + forward * 2f, -forward);
            float nearest = float.PositiveInfinity;
            foreach (MeshFilter filter in house.GetComponentsInChildren<MeshFilter>())
            {
                string part = point.Number == 11 ? "House Walls" : core ? "Door Leaf" : "ClosedDoorLeaves";
                if (!filter.name.Contains(part) && !(point.Number == 11 && filter.name.Contains("House Plinth"))) continue;
                Vector3 origin = filter.transform.InverseTransformPoint(ray.origin);
                Vector3 direction = filter.transform.InverseTransformVector(ray.direction);
                Vector3[] vertices = filter.sharedMesh.vertices;
                int[] triangles = filter.sharedMesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], edge1 = vertices[triangles[i + 1]] - a,
                        edge2 = vertices[triangles[i + 2]] - a;
                    Vector3 cross = Vector3.Cross(direction, edge2);
                    float determinant = Vector3.Dot(edge1, cross);
                    if (Mathf.Abs(determinant) < .00000001f) continue;
                    Vector3 delta = origin - a;
                    float u = Vector3.Dot(delta, cross) / determinant;
                    if (u < 0 || u > 1) continue;
                    Vector3 q = Vector3.Cross(delta, edge1);
                    float v = Vector3.Dot(direction, q) / determinant;
                    if (v < 0 || u + v > 1) continue;
                    float distance = Vector3.Dot(edge2, q) / determinant;
                    if (distance >= 0 && distance < nearest) nearest = distance;
                }
            }
            if (float.IsPositiveInfinity(nearest)) throw new InvalidOperationException("No mounting surface for " + point.Id);
            // Contact with the real authored surface, not the roof's plot envelope.
            float back = point.Number == 11 ? -.177f : .019f;
            subject.position += forward * (Vector3.Dot(ray.GetPoint(nearest) - subject.position, forward) - back + .002f);
        }

        private static void AddPaperText(Transform subject, int number)
        {
            var host = new GameObject("Localized Paper Text");
            host.transform.SetParent(subject, false);
            host.transform.localPosition = VillageNarrativeLibrary.GetAnchor(number, "Paper") + Vector3.forward * .006f;
            host.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var text = host.AddComponent<TextMeshPro>();
            text.font = CemeteryPlaqueFont.Get();
            text.color = new Color(.11f, .10f, .085f, 1f);
            text.autoSizeTextContainer = false;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(.40f, .54f);
            text.margin = new Vector4(.008f, .008f, .008f, .008f);
            RefreshPaperText(text, number);
            text.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        internal static void RefreshPaperText(TextMeshPro text, int number)
        {
            var body = new StringBuilder();
            foreach (NarrativePage page in AlpineVillageNarrativeContent.GetPages(number))
            {
                if (page.Kind != NarrativePageKind.DocumentText) continue;
                if (body.Length > 0) body.Append("\n\n");
                body.Append(LocalizationService.Get(page.TextKey));
            }
            text.text = body.ToString();
            // A long letter must fit the physical sheet, including its last
            // paragraph. TMP's previous minimum size was still much too large.
            // Fit actual generated glyphs; never truncate the authored text.
            float small = .005f, large = 1.15f;
            for (int step = 0; step < 18; step++)
            {
                text.fontSize = (small + large) * .5f;
                text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
                if (PaperGlyphsFit(text)) small = text.fontSize;
                else large = text.fontSize;
            }
            text.fontSize = small * .99f;
            text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
            if (!PaperGlyphsFit(text)) throw new InvalidOperationException("Narrative paper text does not fit its sheet: " +
                number + ", font=" + text.fontSize + ", characters=" + text.textInfo.characterCount + ", bounds=" + text.textBounds);
        }

        internal static bool PaperGlyphsFit(TextMeshPro text)
        {
            Rect rect = text.rectTransform.rect;
            if (text.isTextTruncated || text.textInfo.characterCount == 0) return false;
            for (int i = 0; i < text.textInfo.characterCount; i++)
            {
                TMP_CharacterInfo letter = text.textInfo.characterInfo[i];
                if (!letter.isVisible) continue;
                if (letter.bottomLeft.x < rect.xMin || letter.bottomLeft.y < rect.yMin ||
                    letter.topRight.x > rect.xMax || letter.topRight.y > rect.yMax) return false;
            }
            return true;
        }
    }
}
