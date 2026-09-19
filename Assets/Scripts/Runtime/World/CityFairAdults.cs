using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Quiet adult browsers, with population-owned appearances and local placement.</summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class CityFairAdults : MonoBehaviour
    {
        public const float BodyRadius = .24f;
        private readonly List<Player3DProceduralLocomotionLayer> legs = new List<Player3DProceduralLocomotionLayer>();
        private bool initialized;
        public VillageResidentPresentation[] Actors { get; private set; } = Array.Empty<VillageResidentPresentation>();
        public int ActorCount => Actors.Length;
        public float ElapsedSeconds { get; private set; }

        public static CityFairAdults Build(Transform parent, CityFairPlan fair)
        {
            if (fair == null) throw new ArgumentNullException(nameof(fair));
            if (!fair.IsEnabled) return null;
            var host = new GameObject("Fair Adult Visitors");
            host.transform.SetParent(parent, false);
            var result = host.AddComponent<CityFairAdults>();
            result.Actors = new VillageResidentPresentation[fair.AdultPositions.Length];
            for (int i = 0; i < result.Actors.Length; i++)
            {
                var actor = DefaultNpcFactory.CreateForCharacter(host.transform, DefaultNpcPopulation.FairVisitorId(i));
                actor.name = "Fair Adult Visitor " + i;
                actor.transform.SetPositionAndRotation(fair.AdultPositions[i], Quaternion.LookRotation(fair.AdultFacings[i]));
                CityPortCrew.AlignWorkerModelWithPlacement(actor);
                var body = actor.gameObject.AddComponent<CapsuleCollider>();
                body.center = Vector3.up * .94f;
                body.height = 1.62f;
                body.radius = BodyRadius;
                result.Actors[i] = actor;
                result.legs.Add(BindFeet(actor));
            }
            result.initialized = true;
            result.ApplyAt(0f);
            return result;
        }

        private static Player3DProceduralLocomotionLayer BindFeet(VillageResidentPresentation actor)
        {
            // Use the same equipped-sole probes and planted leg layer as street NPCs.
            var left = new List<SkinnedMeshRenderer>();
            var right = new List<SkinnedMeshRenderer>();
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name.EndsWith("Sole.L", StringComparison.Ordinal)) left.Add(renderer);
                if (renderer.name.EndsWith("Sole.R", StringComparison.Ordinal)) right.Add(renderer);
            }
            Transform Bone(string name) => CityPedestrianHandProps.FindSocket(actor.ModelRoot, name)
                ?? throw new InvalidOperationException("A fair visitor has no " + name + " joint.");
            var layer = new Player3DProceduralLocomotionLayer();
            layer.Bind(null, actor.transform, Bone("pelvis"), null, null, null,
                Bone("thigh.L"), Bone("shin.L"), Bone("foot.L"),
                Bone("thigh.R"), Bone("shin.R"), Bone("foot.R"),
                Player3DFootGroundProbe.Create(left, right, actor.transform));
            layer.Calibrate();
            return layer;
        }

        private void LateUpdate()
        {
            if (initialized && !GameTimeScaleRuntime.IsPaused && Time.deltaTime > 0f)
                ApplyAt(ElapsedSeconds + Time.deltaTime);
        }

        public void ApplyAt(float seconds)
        {
            if (!initialized) return;
            float next = Mathf.Max(0f, seconds);
            float delta = Mathf.Max(0f, next - ElapsedSeconds);
            ElapsedSeconds = next;
            for (int i = 0; i < Actors.Length; i++)
            {
                legs[i].Restore();
                Actors[i].Apply(VillageResidentAction.Idle, next * (.88f + .06f * i) + i * 2.73f);
                legs[i].Apply(new Player3DProceduralLayerInput(
                    true, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 0f, false, false), delta);
            }
        }

        private void OnEnable()
        {
            if (!initialized) return;
            foreach (var actor in Actors)
            {
                actor.gameObject.SetActive(true);
                NpcFootstepSources.Register(actor.transform);
            }
            ApplyAt(ElapsedSeconds);
        }

        private void OnDisable()
        {
            if (!initialized) return;
            for (int i = 0; i < Actors.Length; i++)
            {
                legs[i].Restore();
                if (Actors[i] == null) continue;
                NpcFootstepSources.Unregister(Actors[i].transform);
                Actors[i].gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            foreach (var layer in legs) layer.Dispose();
        }
    }
}
