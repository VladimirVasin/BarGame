namespace BarPromenade
{
    public static class SupermarketEntranceGeometry
    {
        public const float ExteriorWidth = 15.50f;
        public const float ExteriorDepth = 15.50f;
        public const float ExteriorHeight = 6.40f;
        public const float ExteriorWallInset = 0.08f;
        public const float FoundationInset = 0.14f;
        public const float MinimumOpaqueClearance = 0.03f;
        public const float WalkwayWidth = 4.80f;
        public const float FenceOpeningWidth = 5.60f;
        public const float StorefrontWidth = 8.40f;
        public const float CanopyWidth = 9.20f;
        public const float InteractionTriggerRadius = 1.05f;

        // The larger shop prompt reaches the hero while he is still on the
        // carriageway, including beside the door on a graded Street. Cover
        // that complete physical reach, one kerb and the ordinary settling
        // tolerance so every visible prompt can begin the walked approach.
        public const float DoorApproachVerticalTolerance =
            CityStreetSurfacePlanner.SidewalkTop -
            CityStreetSurfacePlanner.RoadTop +
            (PlayerInteractor.InteractionRadius +
             InteractionTriggerRadius) *
            (CityElevationPlan.MaximumBusGradePercent / 100f) +
            PlayerMotor.InteractionVerticalTolerance;
    }
}
