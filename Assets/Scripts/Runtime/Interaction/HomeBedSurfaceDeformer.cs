using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Dents the bed's grid surfaces under the hero's weight and lets them
    /// slowly refill after he gets up — the mattress behaves like thick
    /// cloth instead of a rigid box. Runs after every writer of the
    /// player's pose (bones final past order 300; the model-root pelvis
    /// pin lands in the interaction controller's LateUpdate at 220), so it
    /// reads the frame's finished body and follows its actual underside.
    /// </summary>
    [DefaultExecutionOrder(400)]
    [DisallowMultipleComponent]
    public sealed class HomeBedSurfaceDeformer : MonoBehaviour
    {
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private HomeBedInteraction bed;
        private HomeBedDeformableSurface mattress;
        private HomeBedDeformableSurface pillow;
        private HomeBedSurfaceDepressionModel mattressModel;
        private HomeBedSurfaceDepressionModel pillowModel;
        private Vector3[] mattressVertices;
        private Vector3[] pillowVertices;
        private readonly HomeBedDepressionSource[] mattressSources =
            new HomeBedDepressionSource[
                HomeBedSurfaceDepressionModel.MaximumSources];
        private readonly HomeBedDepressionSource[] pillowSources =
            new HomeBedDepressionSource[
                HomeBedSurfaceDepressionModel.MaximumSources];
        private PlayerAnimatedInteractionPhase previousPhase =
            PlayerAnimatedInteractionPhase.Idle;
        private bool snapPending;

        public HomeBedSurfaceDepressionModel MattressModel =>
            mattressModel;
        public HomeBedSurfaceDepressionModel PillowModel =>
            pillowModel;

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController
                interactionController,
            HomeBedInteraction bedInteraction,
            HomeBedDeformableSurface mattressSurface,
            HomeBedDeformableSurface pillowSurface)
        {
            if (playerRuntime.GameObject == null)
            {
                throw new ArgumentException(
                    "The bed surface deformer requires a player.",
                    nameof(playerRuntime));
            }

            if (interactionController == null)
            {
                throw new ArgumentNullException(
                    nameof(interactionController));
            }

            if (bedInteraction == null)
            {
                throw new ArgumentNullException(
                    nameof(bedInteraction));
            }

            if (mattressSurface == null || pillowSurface == null)
            {
                throw new ArgumentNullException(
                    mattressSurface == null
                        ? nameof(mattressSurface)
                        : nameof(pillowSurface));
            }

            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }

            player = playerRuntime;
            controller = interactionController;
            bed = bedInteraction;
            mattress = mattressSurface;
            pillow = pillowSurface;
            mattressModel = CreateModel(
                mattress,
                HomeBedSurfaceDepressionSettings.Default);
            pillowModel = CreateModel(
                pillow,
                HomeBedSurfaceDepressionSettings.Compact);

            Vector2 pillowCenter =
                mattress.WorldToLocalPlanar(
                    pillow.transform.position);
            if (pillow.HasRestProfile)
                mattressModel.SetSupportProfile(
                    pillowCenter.x - pillow.SizeX * 0.5f, pillowCenter.y - pillow.SizeZ * 0.5f,
                    pillowCenter.x + pillow.SizeX * 0.5f, pillowCenter.y + pillow.SizeZ * 0.5f,
                    pillow.Columns, pillow.Rows, pillow.BottomHeights,
                    mattress.RestTopWorldY - pillow.transform.position.y);
            else
                mattressModel.SetShadowRect(
                    pillowCenter.x - pillow.SizeX * 0.5f, pillowCenter.y - pillow.SizeZ * 0.5f,
                    pillowCenter.x + pillow.SizeX * 0.5f, pillowCenter.y + pillow.SizeZ * 0.5f,
                    Mathf.Max(0f, mattress.RestTopWorldY - pillow.RestBottomSupportWorldY - 0.01f));
            mattressVertices = new Vector3[mattress.VertexCount];
            pillowVertices = new Vector3[pillow.VertexCount];
            mattress.CopyBaseVertices(mattressVertices);
            pillow.CopyBaseVertices(pillowVertices);
            previousPhase = controller.Phase;
            snapPending =
                previousPhase ==
                PlayerAnimatedInteractionPhase.Looping;
            controller.PhaseChanged += HandlePhaseChanged;
        }

        /// <summary>
        /// The dented surface height at a world point: the pillow where it
        /// covers the bed, the mattress elsewhere. This is what honest
        /// "he rests on the surface" assertions compare against.
        /// </summary>
        public float GetSurfaceHeight(Vector3 worldPosition)
        {
            float mattressHeight = HomeInteriorWorldBuilder.BedMattressSurfaceHeight;
            if (mattress != null && mattressModel != null)
            {
                Vector2 mattressLocal = mattress.WorldToLocalPlanar(worldPosition);
                mattressHeight = mattress.RestTopWorldY - mattressModel.SampleDepth(
                    mattressLocal.x, mattressLocal.y);
            }
            if (pillow != null &&
                pillowModel != null &&
                pillow.ContainsPlanar(worldPosition))
            {
                Vector2 local =
                    pillow.WorldToLocalPlanar(worldPosition);
                return Mathf.Max(mattressHeight,
                    pillow.SampleRestWorldHeight(worldPosition) - pillowModel.SampleDepth(local.x, local.y));
            }
            return mattressHeight;
        }

        private void LateUpdate()
        {
            if (mattressModel == null || controller == null)
            {
                return;
            }

            float weight = ResolveBodyWeight();
            mattressModel.SetBodyWeight(weight);
            pillowModel.SetBodyWeight(weight);
            GatherSources();

            if (snapPending)
            {
                snapPending = false;
                mattressModel.SnapToTarget();
                pillowModel.SnapToTarget();
                WriteSurface(
                    mattress, mattressModel, mattressVertices);
                WriteSurface(pillow, pillowModel, pillowVertices);
                return;
            }

            if (mattressModel.Advance(Time.deltaTime))
            {
                WriteSurface(
                    mattress, mattressModel, mattressVertices);
            }

            if (pillowModel.Advance(Time.deltaTime))
            {
                WriteSurface(pillow, pillowModel, pillowVertices);
            }
        }

        private void OnDisable()
        {
            RestoreRest();
        }

        private void OnDestroy()
        {
            RestoreRest();
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }
        }

        private void HandlePhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            // The opening begins its sleep directly in the loop. Its
            // close-up must find the dent already made, so a loop that was
            // not reached through the lie-down snaps to equilibrium.
            // Deliberately no ownership check here: the bed raises its
            // ownership flag only after BeginLooping returns, so this
            // event always fires before it. LateUpdate re-resolves the
            // weight, and snapping someone else's loop lands on rest —
            // a harmless no-op.
            if (phase == PlayerAnimatedInteractionPhase.Looping &&
                previousPhase !=
                PlayerAnimatedInteractionPhase.Entering)
            {
                snapPending = true;
            }

            previousPhase = phase;
        }

        private static HomeBedSurfaceDepressionModel CreateModel(
            HomeBedDeformableSurface surface,
            HomeBedSurfaceDepressionSettings settings)
        {
            return new HomeBedSurfaceDepressionModel(
                settings,
                surface.SizeX,
                surface.SizeZ,
                surface.Columns,
                surface.Rows,
                surface.MaxDepth,
                surface.RestHeights,
                surface.BottomHeights);
        }

        private float ResolveBodyWeight()
        {
            if (bed == null || !bed.OwnsActiveInteraction)
            {
                return 0f;
            }

            return ResolveBodyWeight(
                controller.Phase,
                controller.FrameIndex,
                bed.Definition);
        }

        /// <summary>
        /// How much of the hero's weight rests on the bed at a given point
        /// of the interaction: nothing while he stands or sits on the edge
        /// (the seat is boot-pinned and deliberately takes no dent), and a
        /// smooth ramp across the lie-down and the sit-up. Static and pure
        /// so EditMode tests can pin it to the seat windows directly.
        /// </summary>
        public static float ResolveBodyWeight(
            PlayerAnimatedInteractionPhase phase,
            int frameIndex,
            PlayerAnimatedInteractionDefinition definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Looping:
                    return 1f;
                case PlayerAnimatedInteractionPhase.Entering:
                {
                    float progress = Mathf.Clamp01(
                        (frameIndex -
                         definition.EnterStartFrame) /
                        (float)definition.EnterFrameCount);
                    return Smooth01(
                        Mathf.InverseLerp(
                            HomeBedInteractionPlan
                                .EnterSeatDepartureProgress,
                            1f,
                            progress));
                }

                case PlayerAnimatedInteractionPhase.Exiting:
                    // Full weight through the whole wake: the sources
                    // vanish naturally as body parts rise off the rest
                    // plane, and the slow spring refills the vacated
                    // hollow behind him — the one moment the dent is not
                    // hidden under the body that made it. An early ramp
                    // here erased the dent before it could ever be seen.
                    return 1f;
                default:
                    return 0f;
            }
        }

        private void GatherSources()
        {
            int mattressCount = 0;
            int pillowCount = 0;
            Player3DCharacterPresentation presentation =
                player.Visual as Player3DCharacterPresentation;
            if (presentation != null && presentation.Registry != null)
            {
                mattressCount = AppendPart(
                    presentation, Player3DAnatomicalPart.Pelvis,
                    mattress, mattressSources, mattressCount);
                mattressCount = AppendPart(
                    presentation, Player3DAnatomicalPart.Torso,
                    mattress, mattressSources, mattressCount);
                mattressCount = AppendPart(
                    presentation, Player3DAnatomicalPart.LeftThigh,
                    mattress, mattressSources, mattressCount);
                mattressCount = AppendPart(
                    presentation, Player3DAnatomicalPart.RightThigh,
                    mattress, mattressSources, mattressCount);
                mattressCount = AppendPart(
                    presentation, Player3DAnatomicalPart.LeftFoot,
                    mattress, mattressSources, mattressCount);
                mattressCount = AppendPart(
                    presentation, Player3DAnatomicalPart.RightFoot,
                    mattress, mattressSources, mattressCount);
                pillowCount = AppendPart(
                    presentation, Player3DAnatomicalPart.Head,
                    pillow, pillowSources, pillowCount);
                // The measured supine head support is the back of the
                // hair shell, slightly below GEO_Head on the production rig.
                foreach (Player3DMeshBinding binding in presentation.Registry.MeshBindings)
                    if (binding.MeshName == "GEO_HairBack" && binding.Renderer != null)
                    {
                        pillowCount = AppendBounds(binding.Renderer.bounds, pillow, pillowSources, pillowCount);
                        break;
                    }
            }

            mattressModel.SetSources(mattressSources, mattressCount);
            pillowModel.SetSources(pillowSources, pillowCount);
        }

        private static int AppendPart(
            Player3DCharacterPresentation presentation,
            Player3DAnatomicalPart part,
            HomeBedDeformableSurface surface,
            HomeBedDepressionSource[] target,
            int count)
        {
            if (count >= target.Length ||
                !presentation.Registry.TryGetPart(
                    part,
                    out Player3DAnatomicalPartBinding binding) ||
                binding?.Renderer == null)
            {
                return count;
            }

            return AppendBounds(binding.Renderer.bounds, surface, target, count);
        }

        private static int AppendBounds(Bounds bounds, HomeBedDeformableSurface surface,
            HomeBedDepressionSource[] target, int count)
        {
            if (count >= target.Length) return count;
            float penetration = Mathf.Clamp(
                surface.RestTopWorldY - bounds.min.y,
                0f,
                surface.MaxDepth);
            if (penetration <= 0f)
            {
                return count;
            }

            Vector2 local =
                surface.WorldToLocalPlanar(bounds.center);
            target[count] = new HomeBedDepressionSource(
                local.x,
                local.y,
                bounds.extents.x,
                bounds.extents.z,
                penetration);
            return count + 1;
        }

        private static void WriteSurface(
            HomeBedDeformableSurface surface,
            HomeBedSurfaceDepressionModel model,
            Vector3[] buffer)
        {
            if (surface == null)
            {
                return;
            }

            surface.ApplyDepths(model, buffer);
        }

        private void RestoreRest()
        {
            mattressModel?.ResetToRest();
            pillowModel?.ResetToRest();
            if (mattress != null)
            {
                mattress.RestoreRestState();
            }

            if (pillow != null)
            {
                pillow.RestoreRestState();
            }
        }

        private static float Smooth01(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * (3f - (2f * clamped));
        }
    }
}
