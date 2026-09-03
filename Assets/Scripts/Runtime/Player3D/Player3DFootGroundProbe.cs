using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Left or right, as the rig names them.</summary>
    public enum FootSide
    {
        Left = 0,
        Right = 1
    }

    /// <summary>What one foot's probes found this frame.</summary>
    public readonly struct FootGroundSample
    {
        public FootGroundSample(
            bool hasSurface,
            float heelY,
            float toeY,
            Vector3 normal,
            FootSurfaceKind kind)
        {
            HasSurface = hasSurface;
            HeelY = heelY;
            ToeY = toeY;
            Normal = normal;
            Kind = kind;
        }

        public static FootGroundSample None =>
            new FootGroundSample(
                false,
                0f,
                0f,
                Vector3.up,
                FootSurfaceKind.None);

        /// <summary>Whether the heel ray found ground the foot may use.</summary>
        public bool HasSurface { get; }

        /// <summary>Surface height under the ankle.</summary>
        public float HeelY { get; }

        /// <summary>Surface height under the toe (the heel when unknown).</summary>
        public float ToeY { get; }

        /// <summary>Surface normal under the ankle.</summary>
        public Vector3 Normal { get; }

        public FootSurfaceKind Kind { get; }
    }

    /// <summary>
    /// Per-foot ground knowledge for a skinned rig: the lowest vertex of
    /// each boot's own renderer (baked from the current pose, so it follows
    /// the clip), and two rays under each boot — one at the ankle, one a
    /// boot-length forward — against every surface a foot may stand on,
    /// tread colliders included.
    ///
    /// World space throughout. The bone hierarchy carries a <c>100x</c>
    /// unit factor and the toe is not a registered anatomical part, so the
    /// toe ray starts from a captured foot-forward direction rather than
    /// from a bone.
    /// </summary>
    internal sealed class Player3DFootGroundProbe : IDisposable
    {
        public const float ProbeStartHeight = 0.45f;
        public const float ProbeDistance = 1.4f;

        /// <summary>
        /// A hit this far above the actor's ground is a table edge, not a
        /// step: the controller cannot climb it either (its step limit is
        /// <c>0.28 m</c>).
        /// </summary>
        public const float MaximumRise = 0.32f;

        /// <summary>Ankle to toe, along the foot's captured forward.</summary>
        public const float ToeDistance = 0.14f;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private readonly SkinnedMeshRenderer[][] soleRenderers;
        private readonly Transform ignoredRoot;
        private readonly Mesh bakedSoleMesh;
        private readonly List<Vector3> bakedSoleVertices =
            new List<Vector3>(64);

        private Player3DFootGroundProbe(
            SkinnedMeshRenderer[] leftRenderers,
            SkinnedMeshRenderer[] rightRenderers,
            Transform rootToIgnore)
        {
            soleRenderers = new[] { leftRenderers, rightRenderers };
            ignoredRoot = rootToIgnore;
            bakedSoleMesh = new Mesh
            {
                name = "Player3D Foot Ground Probe",
                hideFlags = HideFlags.HideAndDontSave
            };
            bakedSoleMesh.MarkDynamic();
        }

        /// <summary>Whether either boot has a renderer to bake.</summary>
        public bool HasSoleRenderers =>
            soleRenderers[0].Length > 0 || soleRenderers[1].Length > 0;

        /// <summary>
        /// The hero variant: boot renderers are the mesh bindings whose
        /// bone is <c>foot.L</c> / <c>foot.R</c>.
        /// </summary>
        public static Player3DFootGroundProbe CreateForHero(
            Player3DAssetRegistry registry,
            Transform rootToIgnore)
        {
            if (registry == null)
            {
                return null;
            }

            var left = new List<SkinnedMeshRenderer>();
            var right = new List<SkinnedMeshRenderer>();
            IReadOnlyList<Player3DMeshBinding> bindings =
                registry.MeshBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding == null ||
                    !(binding.Renderer is SkinnedMeshRenderer renderer) ||
                    binding.Bone == null)
                {
                    continue;
                }

                if (binding.BoneName == "foot.L" && !left.Contains(renderer))
                {
                    left.Add(renderer);
                }
                else if (binding.BoneName == "foot.R" &&
                         !right.Contains(renderer))
                {
                    right.Add(renderer);
                }
            }

            return new Player3DFootGroundProbe(
                left.ToArray(),
                right.ToArray(),
                rootToIgnore);
        }

        /// <summary>
        /// Any rig: explicit boot renderers per side (a pedestrian's
        /// <c>*BootSole*</c> parts, say).
        /// </summary>
        public static Player3DFootGroundProbe Create(
            IReadOnlyList<SkinnedMeshRenderer> leftRenderers,
            IReadOnlyList<SkinnedMeshRenderer> rightRenderers,
            Transform rootToIgnore)
        {
            return new Player3DFootGroundProbe(
                CopyRenderers(leftRenderers),
                CopyRenderers(rightRenderers),
                rootToIgnore);
        }

        /// <summary>
        /// The lowest vertex of this boot in the CURRENT pose, or false
        /// when the side has nothing to bake.
        /// </summary>
        public bool TryGetSoleHeight(FootSide side, out float soleY)
        {
            soleY = float.PositiveInfinity;
            SkinnedMeshRenderer[] renderers = soleRenderers[(int)side];
            for (int index = 0; index < renderers.Length; index++)
            {
                SkinnedMeshRenderer renderer = renderers[index];
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                bakedSoleMesh.Clear(false);
                renderer.BakeMesh(bakedSoleMesh, true);
                bakedSoleVertices.Clear();
                bakedSoleMesh.GetVertices(bakedSoleVertices);
                Transform rendererTransform = renderer.transform;
                for (int vertexIndex = 0;
                     vertexIndex < bakedSoleVertices.Count;
                     vertexIndex++)
                {
                    Vector3 worldVertex = rendererTransform.TransformPoint(
                        bakedSoleVertices[vertexIndex]);
                    if (!float.IsNaN(worldVertex.y) &&
                        !float.IsInfinity(worldVertex.y))
                    {
                        soleY = Mathf.Min(soleY, worldVertex.y);
                    }
                }
            }

            return !float.IsPositiveInfinity(soleY);
        }

        /// <summary>
        /// The lowest sole of both boots — the scalar the legacy grounding
        /// pinned to the actor's ground.
        /// </summary>
        public bool TryGetLowestSoleHeight(out float soleY)
        {
            bool left = TryGetSoleHeight(FootSide.Left, out float leftY);
            bool right = TryGetSoleHeight(FootSide.Right, out float rightY);
            soleY = Mathf.Min(
                left ? leftY : float.PositiveInfinity,
                right ? rightY : float.PositiveInfinity);
            return left || right;
        }

        /// <summary>
        /// Casts the heel and toe rays under a boot. Hits above the ground
        /// the actor stands on by more than <see cref="MaximumRise"/> are
        /// discarded as furniture, and the actor's own colliders are never
        /// a floor.
        /// </summary>
        public FootGroundSample Probe(
            Vector3 anklePosition,
            Vector3 footForward,
            float actorGroundY)
        {
            bool hasHeel = TryCast(
                anklePosition,
                actorGroundY,
                out float heelY,
                out Vector3 heelNormal);
            if (!hasHeel)
            {
                return FootGroundSample.None;
            }

            Vector3 planarForward = footForward;
            planarForward.y = 0f;
            float toeY = heelY;
            if (planarForward.sqrMagnitude > 0.0001f)
            {
                Vector3 toeOrigin = anklePosition +
                                    planarForward.normalized * ToeDistance;
                if (!TryCast(toeOrigin, actorGroundY, out toeY, out _))
                {
                    toeY = heelY;
                }
            }

            FootSurfaceKind kind = PlayerFootPlacementRules.Classify(
                heelY,
                toeY,
                ToeDistance,
                PlayerFootPlacementRules.DefaultRampLimitDegrees);
            return new FootGroundSample(
                true,
                heelY,
                toeY,
                heelNormal,
                kind);
        }

        public void Dispose()
        {
            if (bakedSoleMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(bakedSoleMesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(bakedSoleMesh);
            }
        }

        private bool TryCast(
            Vector3 planarPosition,
            float actorGroundY,
            out float surfaceY,
            out Vector3 normal)
        {
            surfaceY = 0f;
            normal = Vector3.up;
            Vector3 origin = new Vector3(
                planarPosition.x,
                actorGroundY + ProbeStartHeight,
                planarPosition.z);
            // Triggers are asked for so tread colliders (triggers on the
            // FootProbe layer, invisible to every obstacle sweep) count as
            // ground; any other trigger — a door volume, the cat's reach —
            // is not a floor and is skipped.
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                Hits,
                ProbeDistance,
                FootProbeSurface.ProbeMask,
                QueryTriggerInteraction.Collide);
            float closestDistance = float.PositiveInfinity;
            bool found = false;
            float ceiling = actorGroundY + MaximumRise;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = Hits[index];
                if (hit.collider == null ||
                    (hit.collider.isTrigger &&
                     hit.collider.gameObject.layer !=
                     FootProbeSurface.LayerIndex) ||
                    (ignoredRoot != null &&
                     hit.collider.transform.IsChildOf(ignoredRoot)) ||
                    hit.normal.y <= 0.001f ||
                    hit.point.y > ceiling ||
                    hit.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = hit.distance;
                surfaceY = hit.point.y;
                normal = hit.normal.normalized;
                found = true;
            }

            return found;
        }

        private static SkinnedMeshRenderer[] CopyRenderers(
            IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            if (renderers == null || renderers.Count == 0)
            {
                return Array.Empty<SkinnedMeshRenderer>();
            }

            var copy = new List<SkinnedMeshRenderer>(renderers.Count);
            for (int index = 0; index < renderers.Count; index++)
            {
                if (renderers[index] != null &&
                    !copy.Contains(renderers[index]))
                {
                    copy.Add(renderers[index]);
                }
            }

            return copy.ToArray();
        }
    }
}
