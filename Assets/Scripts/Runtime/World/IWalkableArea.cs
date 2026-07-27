using UnityEngine;

namespace BarPromenade
{
    public interface IWalkableArea
    {
        bool Contains(Vector3 position, float radius = 0f);

        Vector3 Constrain(
            Vector3 currentPosition,
            Vector3 desiredPosition,
            float radius = 0f);
    }
}
