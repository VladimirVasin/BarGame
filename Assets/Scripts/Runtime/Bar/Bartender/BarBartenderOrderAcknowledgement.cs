using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarBartenderPresentation
    {
        private BarBartenderOrderAcknowledgement orderAcknowledgement;
        public int AcknowledgementCount => orderAcknowledgement != null ? orderAcknowledgement.Count : 0;
        public bool IsAcknowledgingOrder => orderAcknowledgement != null && orderAcknowledgement.IsActive;
        public Transform AcknowledgementTarget => orderAcknowledgement != null ? orderAcknowledgement.Target : null;
        public float AcknowledgementProgress => orderAcknowledgement != null ? orderAcknowledgement.Progress : 0f;
        public float AcknowledgementWeight => orderAcknowledgement != null ? orderAcknowledgement.Weight : 0f;
        public float AcknowledgementNodDegrees => orderAcknowledgement != null ? orderAcknowledgement.NodDegrees : 0f;
        public Vector3 AcknowledgementHeadForward => orderAcknowledgement != null ? orderAcknowledgement.HeadForward : transform.forward;

        public void AcknowledgeOrder(Transform orderingPerson)
        {
            if (!isInitialized || orderingPerson == null || !isActiveAndEnabled) return;
            if (orderAcknowledgement == null)
            {
                orderAcknowledgement = gameObject.AddComponent<BarBartenderOrderAcknowledgement>();
                orderAcknowledgement.Initialize(this, registry);
            }
            orderAcknowledgement.Enqueue(orderingPerson);
        }
    }

    /// <summary>A finite order response over the final sampled head pose; hands and service time are independent.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(360)]
    internal sealed class BarBartenderOrderAcknowledgement : MonoBehaviour
    {
        public const float DurationSeconds = 1.25f;
        private readonly Transform[] pending = new Transform[BarBartenderServiceQueue.Capacity];
        private int pendingCount;
        private BarBartenderPresentation owner;
        private Transform facing, head, neck;
        private Vector3 forwardInHead;
        private float elapsed;
        private Quaternion headBase, neckBase, headWritten, neckWritten;
        private bool hasWritten;
        public int Count { get; private set; }
        public Transform Target { get; private set; }
        public bool IsActive => Target != null || pendingCount > 0;
        public float Progress => Target != null ? Mathf.Clamp01(elapsed / DurationSeconds) : 0f;
        public float Weight { get; private set; }
        public float NodDegrees { get; private set; }
        public Vector3 HeadForward => head != null ? head.rotation * forwardInHead : Vector3.forward;

        public void Initialize(BarBartenderPresentation presentation, BarBartenderAssetRegistry registry)
        {
            owner = presentation; facing = owner.transform; head = registry.Head; neck = registry.Neck;
            Vector3 forward = Vector3.ProjectOnPlane(
                (registry.FaceEyeLeft.position + registry.FaceEyeRight.position) * .5f - head.position, facing.up);
            if (forward.sqrMagnitude < .000001f) forward = facing.forward;
            forwardInHead = Quaternion.Inverse(head.rotation) * forward.normalized;
        }

        public void Enqueue(Transform person)
        {
            Transform target = CityPedestrianHandProps.FindSocket(person, "head");
            if (target == null) target = person;
            Count++;
            // A canceled service request may be accepted again before its nod finishes.
            // Count accepted requests while retaining one complete response per person.
            if (Target == target) return;
            for (int index = 0; index < pendingCount; index++)
                if (pending[index] == target) return;
            // The scene has three ordering people. Keep service independent even if
            // an unexpected caller tries to add a fourth acknowledgement target.
            if (pendingCount + (Target != null ? 1 : 0) >= pending.Length) return;
            pending[pendingCount++] = target;
            if (Target == null) BeginNext();
        }

        private void BeginNext()
        {
            Target = null; elapsed = 0f;
            while (pendingCount > 0 && Target == null)
            {
                Target = pending[0];
                pendingCount--;
                for (int index = 0; index < pendingCount; index++) pending[index] = pending[index + 1];
                pending[pendingCount] = null;
            }
        }

        private void LateUpdate()
        {
            if (owner == null || !owner.isActiveAndEnabled) { Clear(); return; }
            RestoreUntouched();
            if (Target == null) BeginNext();
            if (Target == null) { Weight = NodDegrees = 0f; return; }
            if (!GameTimeScaleRuntime.IsPaused)
                elapsed = Mathf.Min(DurationSeconds, elapsed + Mathf.Max(0f, Time.deltaTime));
            headBase = head.localRotation; neckBase = neck.localRotation;
            Quaternion headWorld = head.rotation, neckWorld = neck.rotation;
            Vector3 direction = facing.InverseTransformDirection(Target.position - head.position);
            Vector3 original = facing.InverseTransformDirection(HeadForward);
            float desiredYaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -70f, 70f);
            float desiredPitch = Mathf.Clamp(-Mathf.Atan2(direction.y,
                new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -22f, 22f);
            float originalYaw = Mathf.Atan2(original.x, original.z) * Mathf.Rad2Deg;
            float originalPitch = -Mathf.Atan2(original.y,
                new Vector2(original.x, original.z).magnitude) * Mathf.Rad2Deg;
            Weight = Mathf.Min(Mathf.SmoothStep(0f, 1f, elapsed / .18f),
                Mathf.SmoothStep(0f, 1f, (DurationSeconds - elapsed) / .25f));
            float nodPhase = Mathf.Clamp01((elapsed - .23f) / .62f);
            NodDegrees = Mathf.Sin(nodPhase * Mathf.PI) * 9f * Weight;
            float yaw = Mathf.DeltaAngle(originalYaw, desiredYaw) * Weight;
            float pitch = (desiredPitch - originalPitch) * Weight + NodDegrees;
            Quaternion turn = Quaternion.AngleAxis(yaw, facing.up);
            Vector3 right = turn * facing.right;
            neck.rotation = Quaternion.AngleAxis(pitch * .35f, right) *
                Quaternion.AngleAxis(yaw * .35f, facing.up) * neckWorld;
            head.rotation = Quaternion.AngleAxis(pitch, right) * turn * headWorld;
            headWritten = head.localRotation; neckWritten = neck.localRotation; hasWritten = true;
            if (elapsed >= DurationSeconds) { RestoreUntouched(); BeginNext(); }
        }

        private void RestoreUntouched()
        {
            if (!hasWritten) return;
            if (head != null && head.localRotation.Equals(headWritten)) head.localRotation = headBase;
            if (neck != null && neck.localRotation.Equals(neckWritten)) neck.localRotation = neckBase;
            hasWritten = false;
        }

        public void Clear()
        {
            RestoreUntouched();
            Target = null; elapsed = Weight = NodDegrees = 0f;
            for (int index = 0; index < pendingCount; index++) pending[index] = null;
            pendingCount = 0;
        }
        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
    }
}
