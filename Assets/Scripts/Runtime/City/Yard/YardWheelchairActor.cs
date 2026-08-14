using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Drives the staged Pipeback Roller around the yard circuit. The actor
    /// owns the motion; the prefab stays the passive presentation it was
    /// authored as.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(330)]
    public sealed class YardWheelchairActor : MonoBehaviour
    {
        private YardWheelchairPresentation presentation;
        private YardWheelchairPlan plan;
        private float distance;

        public YardWheelchairPlan Plan => plan;
        public float Distance => distance;
        public bool IsInitialized { get; private set; }

        public void Initialize(
            YardWheelchairPlan circuit,
            YardWheelchairPresentation wheelchairPresentation)
        {
            plan = circuit;
            presentation = wheelchairPresentation;
            distance = 0f;
            IsInitialized = plan.IsPresent && presentation != null;
            if (IsInitialized)
            {
                ApplyPose(0f, true);
            }
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            distance = YardWheelchairMotion.Advance(
                plan,
                distance,
                deltaTime);
            ApplyPose(deltaTime, false);
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void ApplyPose(float deltaTime, bool immediate)
        {
            YardWheelchairPose pose = YardWheelchairMotion.Sample(
                plan,
                distance);
            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            presentation.Advance(
                immediate ? 0f : deltaTime,
                pose,
                distance,
                true);
        }
    }
}
