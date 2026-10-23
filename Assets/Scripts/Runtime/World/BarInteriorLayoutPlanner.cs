using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class BarInteriorLayoutPlanner
    {
        public const float RoomWidth = 11.4f;
        public const float RoomDepth = 7.8f;
        public const float RoomHeight = 3f;
        public const float WallThickness = 0.3f;
        public static readonly Vector3 JukeboxPosition = new Vector3(5f, 0f, -0.65f);
        public const float JukeboxYawDegrees = 180f;
        public static readonly Vector3 JukeboxColliderCenter = new Vector3(0f, 0.85f, 0f);
        public static readonly Vector3 JukeboxColliderSize = new Vector3(0.62f, 1.75f, 0.98f);
        public static readonly Vector3 JukeboxSpeakerPosition = JukeboxPosition +
            Quaternion.Euler(0f, JukeboxYawDegrees, 0f) * new Vector3(0.275f, 0.42f, 0.0025f);
        public static readonly Vector3 StagePosition = new Vector3(4.1f, 0f, -2.7f);
        public const float StageYawDegrees = -90f;
        public const float StageHeight = 0.12f;
        public static readonly Rect StageFootprint = new Rect(3.05f, -3.525f, 2.1f, 1.65f);
        public static readonly Vector3 StageMicrophonePosition = new Vector3(3.75f, 1.6f, -2.7f);
        public static readonly Vector3 StageStandingPosition = new Vector3(
            4.2f, StageHeight + PlayerFactory.GroundedRootOffset, -2.7f);
        public static readonly Vector3 StageApproachPosition = new Vector3(
            2.6f, PlayerFactory.GroundedRootOffset, -2.15f);
        public static readonly Vector3 StageEntryPosition = new Vector3(
            4.2f, StageHeight + PlayerFactory.GroundedRootOffset, -2.15f);
        public static readonly Rect BeerPongFootprint = new Rect(3.85f, -1.75f, 0.76f, 1.83f);
        public const float BoothCentreX = -4.38f;
        public const float FrontBoothCentreZ = -2.15f;
        public const float RearBoothCentreZ = 0.65f;
        public const float BoothCushionOffset = 0.95f;
        public const float BoothPelvisForwardOffset = 0.13f;

        // The exterior door leaf is recessed 0.16 m behind the street plane.
        // Room +Z points into the main range, room +X along its frontage.
        public static readonly Vector3 ExteriorDoorPosition = new Vector3(-0.16f, 0f, 0f);
        public static readonly Rect ExteriorMainRange = Rect.MinMaxRect(-8.58f, -6f, 0f, 6f);
        public const float ExteriorFirstFloorUnderside = 3.28f;

        public static Vector3 ToExteriorPosition(Vector3 roomPosition) =>
            new Vector3(-roomPosition.z - RoomDepth * 0.5f - 0.16f,
                roomPosition.y, roomPosition.x);

        private const int NpcVisualVariantCount = 6;

        public static BarInteriorLayoutPlan Generate(
            int citySeed,
            string barId,
            BarActivityKind activity)
        {
            return Generate(
                citySeed,
                barId,
                activity,
                BarDistrictIdentityCatalog.FallbackDistrict);
        }

        public static BarInteriorLayoutPlan Generate(
            int citySeed,
            string barId,
            BarActivityKind activity,
            CityDistrictKind district)
        {
            BarActivityKind normalizedActivity =
                NormalizeActivity(activity);
            uint stableSeed = ComputeStableSeed(citySeed, barId);
            var zones = CreateZones();
            Vector3 counterStation =
                new Vector3(-0.75f, 0.9f, 0.55f);
            Vector3 counterStationTriggerSize =
                new Vector3(1.1f, 1.8f, 0.85f);
            Vector3 activityStation =
                new Vector3(2.7f, 0.9f, -0.45f);
            var paths = CreatePaths();
            var furniture = CreateFurniture(normalizedActivity);
            var npcAnchors = CreateNpcAnchors(stableSeed);
            var lightAnchors = CreateLightAnchors();
            Vector3 activityTriggerSize =
                normalizedActivity == BarActivityKind.BeerPong
                    ? new Vector3(1f, 1.8f, 1f)
                    : new Vector3(1f, 1.8f, 1.2f);
            var audioAnchors = CreateAudioAnchors();

            var plan = new BarInteriorLayoutPlan(
                citySeed,
                stableSeed,
                barId,
                normalizedActivity,
                new Vector2(RoomWidth, RoomDepth),
                RoomHeight,
                WallThickness,
                new Rect(-5.4f, -3.55f, 10.8f, 7.1f),
                new Vector3(0f, 0.12f, -1.8f),
                new Vector3(0f, 0.9f, -3.55f),
                new Vector3(0f, 0.12f, -3.15f),
                new Vector3(1.45f, 1.8f, 0.7f),
                new Vector3(0.875f, 0.43f, 1.95f),
                new Vector3(5.95f, 0.86f, 1f),
                counterStation,
                counterStationTriggerSize,
                activityStation,
                activityTriggerSize,
                zones,
                paths,
                furniture,
                npcAnchors,
                lightAnchors,
                audioAnchors);
            plan.AssignDistrict(district);
            BarInteriorLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }

        public static uint ComputeStableSeed(
            int citySeed,
            string barId)
        {
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offsetBasis;
            uint seedBits = unchecked((uint)citySeed);
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (seedBits >> shift) & 0xFFu;
                hash *= prime;
            }

            string stableId = barId ?? string.Empty;
            for (int index = 0; index < stableId.Length; index++)
            {
                uint character = stableId[index];
                hash ^= character & 0xFFu;
                hash *= prime;
                hash ^= character >> 8;
                hash *= prime;
            }

            hash ^= unchecked((uint)stableId.Length);
            hash *= prime;
            return Avalanche(hash);
        }

        private static BarActivityKind NormalizeActivity(
            BarActivityKind activity)
        {
            switch (activity)
            {
                case BarActivityKind.Cocktail:
                case BarActivityKind.BeerPong:
                case BarActivityKind.SplitTheG:
                case BarActivityKind.TinctureMatch:
                    return activity;
                default:
                    return BarActivityKind.Cocktail;
            }
        }

        private static IReadOnlyList<BarInteriorZone> CreateZones()
        {
            return new[]
            {
                new BarInteriorZone(
                    "entrance",
                    BarInteriorZoneKind.Entrance,
                    new Rect(-1.2f, -3.55f, 2.4f, 1.2f)),
                new BarInteriorZone(
                    "main-hall",
                    BarInteriorZoneKind.MainHall,
                    new Rect(-3.3f, -3.45f, 6.6f, 4.9f)),
                new BarInteriorZone(
                    "booth-lounge",
                    BarInteriorZoneKind.BoothLounge,
                    new Rect(-5.4f, -3.55f, 2.05f, 5.6f)),
                new BarInteriorZone(
                    "stage",
                    BarInteriorZoneKind.Stage,
                    StageFootprint),
                new BarInteriorZone(
                    "activity-bay",
                    BarInteriorZoneKind.ActivityBay,
                    new Rect(3.6f, -1.8f, 1.8f, 2.4f)),
                new BarInteriorZone(
                    "bar-service",
                    BarInteriorZoneKind.BarService,
                    new Rect(-3f, 1.45f, 7.1f, 2.25f)),
                new BarInteriorZone(
                    "dance-floor",
                    BarInteriorZoneKind.DanceFloor,
                    new Rect(-1.2f, -1.95f, 2.4f, 1.2f))
            };
        }

        private static IReadOnlyList<BarInteriorPath> CreatePaths()
        {
            return new[]
            {
                new BarInteriorPath(
                    "main-sightline",
                    BarInteriorPathKind.Main,
                    new Rect(-1.2f, -3.45f, 2.4f, 4.05f),
                    2.4f),
                new BarInteriorPath(
                    "cross-aisle",
                    BarInteriorPathKind.CrossAisle,
                    new Rect(-3.3f, -1.95f, 6.6f, 1.2f),
                    1.2f),
                new BarInteriorPath(
                    "lounge-access",
                    BarInteriorPathKind.LoungeAccess,
                    new Rect(-3.3f, -3.45f, 1.2f, 4.9f),
                    1.2f),
                new BarInteriorPath(
                    "activity-access",
                    BarInteriorPathKind.ActivityAccess,
                    new Rect(2.1f, -1.95f, 1.2f, 2.5f),
                    1.2f)
            };
        }

        private static IReadOnlyList<BarInteriorFurnitureFootprint>
            CreateFurniture(BarActivityKind activity)
        {
            var furniture =
                new List<BarInteriorFurnitureFootprint>
                {
                    new BarInteriorFurnitureFootprint(
                        "counter",
                        BarInteriorFurnitureKind.Counter,
                        new Rect(-2.1f, 1.45f, 5.95f, 1f),
                        0.86f,
                        true),
                    new BarInteriorFurnitureFootprint(
                        "counter-return",
                        BarInteriorFurnitureKind.CounterReturn,
                        new Rect(3.05f, 0.65f, 1.04f, 1.8f),
                        0.86f,
                        true),
                    new BarInteriorFurnitureFootprint(
                        "back-bar",
                        BarInteriorFurnitureKind.BackBar,
                        new Rect(-3f, 3.25f, 6.85f, 0.45f),
                        1.3f,
                        true),
                    new BarInteriorFurnitureFootprint(
                        "jukebox",
                        BarInteriorFurnitureKind.Jukebox,
                        new Rect(JukeboxPosition.x - JukeboxColliderSize.x * 0.5f,
                            JukeboxPosition.z - JukeboxColliderSize.z * 0.5f,
                            JukeboxColliderSize.x, JukeboxColliderSize.z),
                        JukeboxColliderSize.y,
                        true),
                    new BarInteriorFurnitureFootprint(
                        "stage-platform",
                        BarInteriorFurnitureKind.Stage,
                        StageFootprint,
                        StageHeight,
                        false), // A physical floor below the hero's step height.
                    CreateBooth("booth-1", FrontBoothCentreZ),
                    CreateBooth("booth-2", RearBoothCentreZ),
                    CreatePubTable("pub-table-left-front", -1.75f, -2.8f),
                    CreatePubTable("pub-table-right-front", 1.75f, -2.8f)
                };
            if (activity == BarActivityKind.BeerPong)
                furniture.Add(CreateBeerPongFixture());
            return furniture.AsReadOnly();
        }

        private static BarInteriorFurnitureFootprint CreateBooth(
            string id,
            float centerZ)
        {
            return new BarInteriorFurnitureFootprint(
                id,
                BarInteriorFurnitureKind.Booth,
                new Rect(-5.4f, centerZ - 1.4f, 2.05f, 2.8f),
                1.55f,
                true);
        }

        private static BarInteriorFurnitureFootprint CreatePubTable(
            string id,
            float x,
            float z)
        {
            return new BarInteriorFurnitureFootprint(
                id,
                BarInteriorFurnitureKind.PubTable,
                new Rect(x - 0.325f, z - 0.325f, 0.65f, 0.65f),
                0.82f,
                true);
        }

        private static BarInteriorFurnitureFootprint CreateBeerPongFixture()
        {
            return new BarInteriorFurnitureFootprint(
                "activity-fixture",
                BarInteriorFurnitureKind.ActivityFixture,
                BeerPongFootprint,
                1f,
                true,
                BarActivityKind.BeerPong);
        }

        private static IReadOnlyList<BarNpcAnchor> CreateNpcAnchors(
            uint stableSeed)
        {
            var anchors = new List<BarNpcAnchor>(10);
            // The 3D publican stands between the hero's counter
            // station (x -1.15) and the center pendant (x 0), tight
            // to the counter's inner edge, so his lit head and
            // shoulders read across the 1.4 m top from the hall.
            AddNpc(
                anchors,
                stableSeed,
                "bartender-left",
                BarNpcRole.Bartender,
                new Vector3(-0.15f, 0f, 2.85f),
                180f,
                "bar-service",
                0f);
            // Each booth has two opposite sofas, with one guest per sofa.
            AddSeatedPair(anchors, stableSeed, 1, FrontBoothCentreZ);
            AddSeatedPair(anchors, stableSeed, 2, RearBoothCentreZ);

            AddNpc(
                anchors,
                stableSeed,
                "counter-seat-left",
                BarNpcRole.CounterPatron,
                new Vector3(BarCounterSeatPlanner.LeftmostStoolX, 0f, BarCounterSeatPlanner.StoolZ),
                0f,
                string.Empty,
                0f);
            AddNpc(
                anchors,
                stableSeed,
                "counter-seat-right",
                BarNpcRole.CounterPatron,
                new Vector3(1.05f, 0f, BarCounterSeatPlanner.StoolZ),
                0f,
                string.Empty,
                0f);
            AddNpc(
                anchors,
                stableSeed,
                "social-left-front",
                BarNpcRole.StandingPatron,
                new Vector3(-1.75f, 0f, -3.45f),
                0f,
                "pub-table-left-front",
                0f);
            AddNpc(
                anchors,
                stableSeed,
                "social-right-front",
                BarNpcRole.StandingPatron,
                new Vector3(1.75f, 0f, -3.45f),
                0f,
                "pub-table-right-front",
                0f);
            return anchors.AsReadOnly();
        }

        private static void AddSeatedPair(
            List<BarNpcAnchor> anchors,
            uint stableSeed,
            int booth,
            float boothCenterZ)
        {
            // Retain the proven pelvis depth, 0.13 m forward of the cushion
            // centre. The seat adapter separately compensates the model's
            // declared SeatBackOffset, so it is never added here twice.
            float pelvisOffset = BoothCushionOffset - BoothPelvisForwardOffset;
            AddNpc(
                anchors,
                stableSeed,
                $"booth-{booth}-near",
                BarNpcRole.SeatedPatron,
                new Vector3(BoothCentreX, 0f, boothCenterZ - pelvisOffset),
                0f,
                string.Empty,
                0f);
            AddNpc(
                anchors,
                stableSeed,
                $"booth-{booth}-far",
                BarNpcRole.SeatedPatron,
                new Vector3(BoothCentreX, 0f, boothCenterZ + pelvisOffset),
                180f,
                string.Empty,
                0f);
        }

        private static void AddNpc(
            List<BarNpcAnchor> anchors,
            uint stableSeed,
            string id,
            BarNpcRole role,
            Vector3 position,
            float yaw,
            string routeId,
            float jitter)
        {
            int index = anchors.Count;
            uint hash = Mix(stableSeed, unchecked((uint)index + 1u));
            float offsetX =
                (HashToUnitFloat(Mix(hash, 0x584A4954u)) - 0.5f) *
                jitter *
                2f;
            float offsetZ =
                (HashToUnitFloat(Mix(hash, 0x5A4A4954u)) - 0.5f) *
                jitter *
                2f;
            int variant = (int)(
                Mix(hash, 0x56415249u) %
                NpcVisualVariantCount);
            float phase = HashToUnitFloat(Mix(hash, 0x50484153u));
            float yawJitter =
                role == BarNpcRole.Walker || jitter <= 0f
                    ? 0f
                    : (HashToUnitFloat(Mix(hash, 0x59415721u)) - 0.5f) *
                      8f;
            anchors.Add(new BarNpcAnchor(
                id,
                role,
                position + new Vector3(offsetX, 0f, offsetZ),
                Mathf.Repeat(yaw + yawJitter, 360f),
                variant,
                phase,
                routeId));
        }

        private static IReadOnlyList<BarInteriorLightAnchor>
            CreateLightAnchors()
        {
            Color amber = new Color(1f, 0.62f, 0.28f);
            Color booth = new Color(1f, 0.34f, 0.20f);
            return new[]
            {
                CreateLight(
                    "counter-pendant-left",
                    BarInteriorLightKind.CounterPendant,
                    new Vector3(-2f, 2.5f, 1.95f),
                    Vector3.down,
                    amber,
                    2.6f,
                    3.8f,
                    82f),
                // The center pendant hangs over the bartender's
                // duckboard so his head and shoulders read warm
                // against the dark backbar from anywhere in the hall.
                CreateLight(
                    "counter-pendant-center",
                    BarInteriorLightKind.CounterPendant,
                    new Vector3(-0.15f, 2.5f, 2.65f),
                    Vector3.down,
                    amber,
                    2.8f,
                    4f,
                    82f),
                CreateLight(
                    "counter-pendant-right",
                    BarInteriorLightKind.CounterPendant,
                    new Vector3(2.3f, 2.5f, 1.95f),
                    Vector3.down,
                    amber,
                    2.6f,
                    3.8f,
                    82f),
                CreateLight(
                    "booth-pool",
                    BarInteriorLightKind.BoothPool,
                    new Vector3(-4.5f, 2.55f, -0.75f),
                    new Vector3(-0.2f, -1f, 0f),
                    booth,
                    2f,
                    4.2f,
                    100f),
                CreateLight(
                    "stage-key",
                    BarInteriorLightKind.StageKey,
                    new Vector3(4.5f, 2.5f, -2.825f),
                    new Vector3(-0.1f, -1f, 0f),
                    new Color(0.28f, 0.72f, 1f),
                    2.4f,
                    3.4f,
                    64f),
                CreateLight(
                    "entry-fill",
                    BarInteriorLightKind.EntryFill,
                    new Vector3(0f, 2.55f, -2.7f),
                    new Vector3(0f, -1f, 0.25f),
                    new Color(0.42f, 0.65f, 1f),
                    1.8f,
                    3.5f,
                    86f),
                CreateLight(
                    "washroom-ceiling",
                    BarInteriorLightKind.Washroom,
                    BarWashroomLayout.LightPosition,
                    Vector3.down,
                    new Color(1f, .86f, .65f),
                    3.1f,
                    3.3f,
                    100f),
                CreateLight(
                    "washroom-sign",
                    BarInteriorLightKind.WashroomSign,
                    BarWashroomLayout.SignLightPosition,
                    new Vector3(0f, -1f, .45f),
                    new Color(1f, .86f, .65f),
                    2.6f,
                    3.8f,
                    140f)
            };
        }

        private static BarInteriorLightAnchor CreateLight(
            string id,
            BarInteriorLightKind kind,
            Vector3 position,
            Vector3 direction,
            Color color,
            float intensity,
            float range,
            float spotAngle)
        {
            return new BarInteriorLightAnchor(
                id,
                kind,
                position,
                direction.normalized,
                color,
                intensity,
                range,
                spotAngle);
        }

        private static IReadOnlyList<BarInteriorAudioAnchor>
            CreateAudioAnchors()
        {
            return new[]
            {
                new BarInteriorAudioAnchor(
                    "crowd-booths",
                    BarInteriorAudioKind.CrowdBed,
                    new Vector3(-4.3f, 1.35f, -0.75f),
                    8f,
                    0.82f),
                new BarInteriorAudioAnchor(
                    "crowd-tables",
                    BarInteriorAudioKind.CrowdBed,
                    new Vector3(1.8f, 1.35f, -1.5f),
                    8f,
                    0.78f),
                new BarInteriorAudioAnchor(
                    "bar-service",
                    BarInteriorAudioKind.BarService,
                    new Vector3(0.425f, 1.2f, 2.75f),
                    6f,
                    0.72f),
                new BarInteriorAudioAnchor(
                    "jukebox-speaker",
                    BarInteriorAudioKind.Music,
                    JukeboxSpeakerPosition,
                    BarMusicPlayer.DefaultMaximumDistance,
                    1f),
            };
        }

        private static float HashToUnitFloat(uint hash)
        {
            return (hash >> 8) * (1f / 16777216f);
        }

        private static uint Mix(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
            return Avalanche(hash);
        }

        private static uint Avalanche(uint hash)
        {
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }
    }
}
