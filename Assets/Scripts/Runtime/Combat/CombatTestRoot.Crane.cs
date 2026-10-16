using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        // Initial patrol stop. The travelling bridge covers the central void
        // below the fan; later stops are independent of the hero's storey.
        private static readonly Vector3 CraneGround = new Vector3(0f, 0f, -2.4f);
    }
}
