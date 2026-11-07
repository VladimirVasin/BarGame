using UnityEngine;

namespace BarPromenade
{
    /// <summary>One solid courtyard leaf shared by the keeper and the hero's door action.</summary>
    [DisallowMultipleComponent]
    public sealed class CityPigeonCourtyardGate : MonoBehaviour
    {
        private Transform pivot, outsideHandle, insideHandle;
        private BoxCollider leaf;
        private Quaternion closed;
        private InteriorHingedDoorInteraction interaction;
        private Vector3 outsideDock, insideDock;
        private bool live, bound, initialPending;
        private float initialGoal = 1f;
        private double lastPassiveMinute = double.NaN;
        private float openness = 1f;
        private CityHillsideGroundPlan ground;

        public float Openness => bound ? interaction.OpenFraction : openness;
        public bool IsBusy => bound && interaction.OwnsActiveInteraction;
        public Vector3 GripPosition => pivot.TransformPoint(new Vector3(.20f, .958f, 0f));
        public Transform Pivot => pivot;
        public InteriorHingedDoorInteraction Interaction => interaction;

        internal void Initialize(Transform hinge, CityPigeonCourtyardPlan plan, bool live, CityHillsideGroundPlan ground)
        {
            pivot = hinge;
            closed = hinge.localRotation;
            this.live = live;
            this.ground = ground;
            var solid = new GameObject("Gate solid leaf");
            solid.transform.SetParent(pivot, false);
            leaf = solid.AddComponent<BoxCollider>();
            leaf.center = new Vector3(.775f, .71f, 0f);
            leaf.size = new Vector3(1.55f, 1.30f, .075f);
            leaf.enabled = live;
            outsideHandle = Handle("Outside latch", new Vector3(1.50f, .80f, -.055f));
            insideHandle = Handle("Inside latch", new Vector3(1.50f, .80f, .055f));
            outsideDock = Grounded(pivot.TransformPoint(new Vector3(1.10f, 0f, -.55f)));
            insideDock = Grounded(pivot.TransformPoint(new Vector3(1.92f, 0f, .48f)));
            pivot.localRotation = closed * Quaternion.Euler(0f, -90f, 0f);
            initialGoal = IsNight ? 0f : 1f;
            initialPending = live && initialGoal < 1f;
            if (!live) ApplyPassive();

            Transform Handle(string name, Vector3 point)
            {
                var handle = new GameObject(name).transform;
                handle.SetParent(pivot, false);
                handle.localPosition = point;
                return handle;
            }
        }

        public void BindPlayer(PlayerRuntime player)
        {
            if (!live || bound) return;
            interaction = gameObject.AddComponent<InteriorHingedDoorInteraction>();
            interaction.Initialize(player, player.GameObject.GetComponent<PlayerAnimatedInteractionController>(),
                pivot, leaf, outsideHandle, insideHandle, outsideDock, insideDock, -90f, openness, Grounded);
            interaction.ConfigurePromptKeys("interaction.open_pigeon_gate", "interaction.close_pigeon_gate");
            interaction.InteractionFinished += OnHeroFinished;
            bound = true;
            if (initialPending && TrySetOpenness(initialGoal)) initialPending = false;
        }

        public bool TrySetOpenness(float fraction)
        {
            if (GameTimeScaleRuntime.IsPaused || IsBusy || float.IsNaN(fraction) || float.IsInfinity(fraction)) return false;
            fraction = Mathf.Clamp01(fraction);
            if (!bound)
            {
                initialGoal = fraction;
                initialPending = true;
                return false;
            }
            bool applied = interaction.TrySetOpenFraction(fraction);
            if (applied) initialPending = false;
            return applied;
        }

        private static bool IsNight => GameSessionState.GameMinuteOfDay < 360 || GameSessionState.GameMinuteOfDay >= 1260;

        private Vector3 Grounded(Vector3 point)
        {
            if (ground.TrySampleTop(new Vector2(point.x, point.z), out float floor))
                point.y = floor + PlayerFactory.GroundedRootOffset;
            return point;
        }

        private void Update()
        {
            if (GameTimeScaleRuntime.IsPaused) return;
            if (!live) ApplyPassive();
            else if (bound && initialPending && TrySetOpenness(initialGoal)) initialPending = false;
        }

        private void ApplyPassive()
        {
            double minute = GameSessionState.GameMinuteOfDay;
            if (minute == lastPassiveMinute) return;
            lastPassiveMinute = minute;
            openness = IsNight ? 0f : 1f;
            pivot.localRotation = closed * Quaternion.Euler(0f, -90f * openness, 0f);
        }

        private void OnHeroFinished(bool completed) => initialPending = false;

        private void OnDestroy()
        {
            if (interaction != null) interaction.InteractionFinished -= OnHeroFinished;
        }
    }
}
