using UnityEngine;

namespace BarPromenade
{
    internal sealed class PawnshopWatchHand : MonoBehaviour
    {
        private Transform secondHand;
        private Quaternion rest;
        private int lastTick = -1;

        private void Awake()
        {
            secondHand = PawnshopFindCatalog.FindAnchor(transform, "SecondHand");
            rest = secondHand.localRotation;
        }

        private void LateUpdate()
        {
            if (PauseMenuController.IsAnyPaused) return;
            int tick = Mathf.FloorToInt(Time.unscaledTime * 5f);
            if (tick == lastTick) return;
            lastTick = tick;
            secondHand.rotation = Quaternion.AngleAxis((tick % 300) * 1.2f, transform.up) *
                secondHand.parent.rotation * rest;
        }
    }
}
