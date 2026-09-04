using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class BarInteriorLayoutPlanner
    {
        public const float RoomWidth = 22f;
        public const float RoomDepth = 16f;
        public const float RoomHeight = 4.8f;
        public const float WallThickness = 0.3f;

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
                new Vector3(-1.15f, 0.9f, 4.75f);
            Vector3 counterStationTriggerSize =
                new Vector3(1.1f, 1.8f, 0.85f);
            Vector3 activityStation =
                new Vector3(5.1f, 0.9f, -1.55f);
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
                new Rect(-10.25f, -7.25f, 20.5f, 14.5f),
                new Vector3(0f, 0.12f, -5.35f),
                new Vector3(0f, 0.9f, -7.25f),
                new Vector3(0f, 0.12f, -6.9f),
                new Vector3(2f, 1.8f, 1.35f),
                new Vector3(0f, 0.43f, 5.75f),
                new Vector3(11.2f, 0.86f, 1f),
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
                    new Rect(-3.4f, -7.25f, 6.8f, 2.15f)),
                new BarInteriorZone(
                    "main-hall",
                    BarInteriorZoneKind.MainHall,
                    new Rect(-5.2f, -5.1f, 10.4f, 10.2f)),
                new BarInteriorZone(
                    "booth-lounge",
                    BarInteriorZoneKind.BoothLounge,
                    new Rect(-10.1f, -5.1f, 3.1f, 8.65f)),
                new BarInteriorZone(
                    "stage",
                    BarInteriorZoneKind.Stage,
                    new Rect(-10.1f, 3.65f, 4.85f, 3.5f)),
                new BarInteriorZone(
                    "activity-bay",
                    BarInteriorZoneKind.ActivityBay,
                    new Rect(5.15f, -2.75f, 4.8f, 6.45f)),
                new BarInteriorZone(
                    "bar-service",
                    BarInteriorZoneKind.BarService,
                    new Rect(-5.9f, 5.15f, 11.8f, 2.3f)),
                new BarInteriorZone(
                    "dance-floor",
                    BarInteriorZoneKind.DanceFloor,
                    new Rect(-4.5f, 1.35f, 9f, 3.55f))
            };
        }

        private static IReadOnlyList<BarInteriorPath> CreatePaths()
        {
            return new[]
            {
                new BarInteriorPath(
                    "main-sightline",
                    BarInteriorPathKind.Main,
                    new Rect(-1.3f, -6.95f, 2.6f, 12.05f),
                    2.6f),
                new BarInteriorPath(
                    "cross-aisle",
                    BarInteriorPathKind.CrossAisle,
                    new Rect(-5.9f, -1.2f, 10.8f, 2.4f),
                    2.4f),
                new BarInteriorPath(
                    "lounge-access",
                    BarInteriorPathKind.LoungeAccess,
                    new Rect(-7.1f, -5.2f, 1.4f, 8.7f),
                    1.4f),
                new BarInteriorPath(
                    "activity-access",
                    BarInteriorPathKind.ActivityAccess,
                    new Rect(4.5f, -2.5f, 1.2f, 1.55f),
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
                        new Rect(-5.6f, 5.25f, 11.2f, 1f),
                        0.86f,
                        true),
                    new BarInteriorFurnitureFootprint(
                        "counter-return",
                        BarInteriorFurnitureKind.CounterReturn,
                        new Rect(4.56f, 2.495f, 1.04f, 3.25f),
                        0.86f,
                        true),
                    new BarInteriorFurnitureFootprint(
                        "back-bar",
                        BarInteriorFurnitureKind.BackBar,
                        new Rect(-5.55f, 7.125f, 11.1f, 0.55f),
                        1.44f,
                        true),
                    new BarInteriorFurnitureFootprint(
                        "stage-platform",
                        BarInteriorFurnitureKind.Stage,
                        new Rect(-10f, 4.75f, 3.8f, 2.35f),
                        0.32f,
                        true),
                    CreateBooth("booth-1", -3.9f),
                    CreateBooth("booth-2", -0.35f),
                    CreateBooth("booth-3", 3.15f),
                    CreatePubTable("pub-table-left-front", -3.5f, -3.65f),
                    CreatePubTable("pub-table-right-front", 3.5f, -3.65f),
                    CreatePubTable("pub-table-left-rear", -3.5f, 2.5f),
                    CreatePubTable("pub-table-right-rear", 3.25f, 2.55f),
                    new BarInteriorFurnitureFootprint(
                        "coat-rack",
                        BarInteriorFurnitureKind.CoatRack,
                        new Rect(-9.05f, -6.65f, 0.4f, 0.4f),
                        1.84f,
                        true),
                    CreateActivityFixture(activity)
                };
            return furniture.AsReadOnly();
        }

        private static BarInteriorFurnitureFootprint CreateBooth(
            string id,
            float centerZ)
        {
            return new BarInteriorFurnitureFootprint(
                id,
                BarInteriorFurnitureKind.Booth,
                new Rect(-10.66f, centerZ - 1.15f, 3.07f, 2.3f),
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
                new Rect(x - 0.5f, z - 0.5f, 1f, 1f),
                0.82f,
                true);
        }

        private static BarInteriorFurnitureFootprint CreateActivityFixture(
            BarActivityKind activity)
        {
            Rect bounds;
            float height;
            switch (activity)
            {
                case BarActivityKind.BeerPong:
                    bounds = new Rect(5.825f, -1.545f, 2.35f, 4.25f);
                    height = 1f;
                    break;
                case BarActivityKind.SplitTheG:
                    bounds = new Rect(5.825f, -0.075f, 2.65f, 1.05f);
                    height = 1.35f;
                    break;
                case BarActivityKind.TinctureMatch:
                    bounds = new Rect(5.825f, -0.075f, 2.65f, 1.05f);
                    height = 1.55f;
                    break;
                default:
                    bounds = new Rect(5.825f, -0.075f, 2.65f, 1.05f);
                    height = 1.3f;
                    break;
            }

            return new BarInteriorFurnitureFootprint(
                "activity-fixture",
                BarInteriorFurnitureKind.ActivityFixture,
                bounds,
                height,
                true,
                activity);
        }

        private static IReadOnlyList<BarNpcAnchor> CreateNpcAnchors(
            uint stableSeed)
        {
            var anchors = new List<BarNpcAnchor>(12);
            // The 3D publican stands between the hero's counter
            // station (x -1.15) and the center pendant (x 0), tight
            // to the counter's inner edge, so his lit head and
            // shoulders read across the 1.4 m top from the hall.
            AddNpc(
                anchors,
                stableSeed,
                "bartender-left",
                BarNpcRole.Bartender,
                new Vector3(-0.55f, 0f, 6.50f),
                180f,
                "bar-service",
                0.12f);
            // Pairs share each booth's single wall bench, aligned to
            // the authored booth centers and the bench geometry the
            // interior actually builds.
            AddSeatedPair(anchors, stableSeed, 1, -3.9f);
            AddSeatedPair(anchors, stableSeed, 2, -0.35f);
            AddSeatedPair(anchors, stableSeed, 3, 3.15f);

            AddNpc(
                anchors,
                stableSeed,
                "counter-seat-left",
                BarNpcRole.CounterPatron,
                new Vector3(-4.25f, 0f, 4.53f),
                0f,
                string.Empty,
                0f);
            AddNpc(
                anchors,
                stableSeed,
                "counter-seat-right",
                BarNpcRole.CounterPatron,
                new Vector3(2.55f, 0f, 4.53f),
                0f,
                string.Empty,
                0f);
            AddNpc(
                anchors,
                stableSeed,
                "social-left-front",
                BarNpcRole.StandingPatron,
                new Vector3(-4.2f, 0f, -3.65f),
                90f,
                "pub-table-left-front",
                0f);
            AddNpc(
                anchors,
                stableSeed,
                "social-right-front",
                BarNpcRole.StandingPatron,
                new Vector3(4.2f, 0f, -3.65f),
                270f,
                "pub-table-right-front",
                0f);
            AddNpc(
                anchors,
                stableSeed,
                "social-left-rear",
                BarNpcRole.StandingPatron,
                new Vector3(-4.2f, 0f, 2.5f),
                90f,
                "pub-table-left-rear",
                0f);
            return anchors.AsReadOnly();
        }

        private static void AddSeatedPair(
            List<BarNpcAnchor> anchors,
            uint stableSeed,
            int booth,
            float boothCenterZ)
        {
            // The seat target is 0.20 m forward of its former position. The
            // seated presentation then applies each design's measured back
            // offset, leaving both pelvises forward on the cushion and the
            // guests facing east toward the table.
            AddNpc(
                anchors,
                stableSeed,
                $"booth-{booth}-near",
                BarNpcRole.SeatedPatron,
                new Vector3(-9.5f, 0f, boothCenterZ - 0.55f),
                90f,
                string.Empty,
                0f);
            AddNpc(
                anchors,
                stableSeed,
                $"booth-{booth}-far",
                BarNpcRole.SeatedPatron,
                new Vector3(-9.5f, 0f, boothCenterZ + 0.55f),
                90f,
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
                    new Vector3(-3.8f, 3.75f, 5f),
                    Vector3.down,
                    amber,
                    5.2f,
                    5.5f,
                    82f),
                // The center pendant hangs over the bartender's
                // duckboard so his head and shoulders read warm
                // against the dark backbar from anywhere in the hall.
                CreateLight(
                    "counter-pendant-center",
                    BarInteriorLightKind.CounterPendant,
                    new Vector3(-0.55f, 3.75f, 6.05f),
                    Vector3.down,
                    amber,
                    5.5f,
                    5.8f,
                    82f),
                CreateLight(
                    "counter-pendant-right",
                    BarInteriorLightKind.CounterPendant,
                    new Vector3(3.8f, 3.75f, 5f),
                    Vector3.down,
                    amber,
                    5.2f,
                    5.5f,
                    82f),
                CreateLight(
                    "booth-pool",
                    BarInteriorLightKind.BoothPool,
                    new Vector3(-7.6f, 3.1f, -1f),
                    new Vector3(-0.2f, -1f, 0f),
                    booth,
                    3.8f,
                    6.4f,
                    88f),
                CreateLight(
                    "stage-key",
                    BarInteriorLightKind.StageKey,
                    new Vector3(-4.75f, 4.05f, 3.25f),
                    new Vector3(-0.65f, -0.7f, 0.35f),
                    new Color(0.28f, 0.72f, 1f),
                    4.4f,
                    7f,
                    64f),
                CreateLight(
                    "entry-fill",
                    BarInteriorLightKind.EntryFill,
                    new Vector3(0f, 3.6f, -6.7f),
                    new Vector3(0f, -1f, 0.25f),
                    new Color(0.42f, 0.65f, 1f),
                    2.8f,
                    5.2f,
                    86f)
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
                    new Vector3(-6.8f, 1.35f, 0.5f),
                    16f,
                    0.82f),
                new BarInteriorAudioAnchor(
                    "crowd-tables",
                    BarInteriorAudioKind.CrowdBed,
                    new Vector3(3.4f, 1.35f, -1.2f),
                    16f,
                    0.78f),
                new BarInteriorAudioAnchor(
                    "bar-service",
                    BarInteriorAudioKind.BarService,
                    new Vector3(0f, 1.2f, 6.35f),
                    11f,
                    0.72f),
                new BarInteriorAudioAnchor(
                    "jukebox-speaker",
                    BarInteriorAudioKind.Music,
                    new Vector3(6.4f, 0.5f, -6.5f),
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
