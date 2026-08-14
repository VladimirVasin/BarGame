using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Staged wheelchair-specific bindings layered over the shared pedestrian
    /// presentation contract. This component is asset metadata only: it does
    /// not add the NPC to the ambient catalog or drive any runtime behaviour.
    /// Pipeback's current bespoke loops remain exact 31-bone skeletal clips;
    /// these mechanism pivots are reserved for a future procedural driver.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityWheelchairNpcAssetRegistry : MonoBehaviour
    {
        [SerializeField] private CityPedestrianAssetRegistry pedestrianRegistry;
        [SerializeField] private Transform leftWheelPivot;
        [SerializeField] private Transform rightWheelPivot;
        [SerializeField] private Transform leftCasterPivot;
        [SerializeField] private Transform rightCasterPivot;
        [SerializeField] private Transform bellowsPivot;
        [SerializeField] private Transform pipeBankPivot;

        public CityPedestrianAssetRegistry PedestrianRegistry =>
            pedestrianRegistry;
        public Transform LeftWheelPivot => leftWheelPivot;
        public Transform RightWheelPivot => rightWheelPivot;
        public Transform LeftCasterPivot => leftCasterPivot;
        public Transform RightCasterPivot => rightCasterPivot;
        public Transform BellowsPivot => bellowsPivot;
        public Transform PipeBankPivot => pipeBankPivot;

        public void Configure(
            CityPedestrianAssetRegistry configuredPedestrianRegistry,
            Transform configuredLeftWheelPivot,
            Transform configuredRightWheelPivot,
            Transform configuredLeftCasterPivot,
            Transform configuredRightCasterPivot,
            Transform configuredBellowsPivot,
            Transform configuredPipeBankPivot)
        {
            pedestrianRegistry = configuredPedestrianRegistry;
            leftWheelPivot = configuredLeftWheelPivot;
            rightWheelPivot = configuredRightWheelPivot;
            leftCasterPivot = configuredLeftCasterPivot;
            rightCasterPivot = configuredRightCasterPivot;
            bellowsPivot = configuredBellowsPivot;
            pipeBankPivot = configuredPipeBankPivot;
        }
    }
}
