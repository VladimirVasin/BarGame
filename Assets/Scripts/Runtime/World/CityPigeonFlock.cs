using System;
using System.Text;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Six resident domestic birds. Each flight reserves one actual free landing surface.</summary>
    [DisallowMultipleComponent]
    public sealed class CityPigeonFlock : MonoBehaviour
    {
        private const int Population = 6;
        private const int AirCapacity = 2;
        private const string Folder = "City/LoftPigeons/";

        private enum SiteKind { Shelf, Perch, Ground, Feeder, Water, Bath }

        private sealed class Site
        {
            public readonly Vector3 Feet, Out;
            public readonly SiteKind Kind;
            public readonly string Name;
            public readonly Vector3 DrinkTarget;
            public int Occupant = -1;
            public bool IsTrough => Kind == SiteKind.Feeder || Kind == SiteKind.Water;
            public bool IsGround => Kind == SiteKind.Ground || Kind == SiteKind.Bath;
            public Quaternion Facing => Quaternion.LookRotation(IsTrough || Kind == SiteKind.Bath ? -Out : Out);

            public Site(Transform anchor, SiteKind kind, Transform frame, Transform drinkTarget = null)
            {
                Vector3 horizontal = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
                Out = horizontal.sqrMagnitude > .25f ? horizontal.normalized : Vector3.forward;
                // An FBX axis-conversion root can leave a locator's local forward
                // vertical. Recover the actual open face from the authored wall and
                // the toe position, including when the whole loft is rotated.
                Vector3 radial = Vector3.ProjectOnPlane(anchor.position - frame.position, Vector3.up);
                float across = Vector3.Dot(radial, frame.right);
                float depth = Vector3.Dot(radial, frame.forward);
                if (kind == SiteKind.Shelf || (kind == SiteKind.Perch && Mathf.Abs(across) > Mathf.Abs(depth)))
                    Out = across >= 0f ? frame.right : -frame.right;
                else if (kind == SiteKind.Perch)
                    Out = depth >= 0f ? frame.forward : -frame.forward;
                if (kind == SiteKind.Feeder || kind == SiteKind.Water || kind == SiteKind.Ground)
                    Out = Vector3.Dot(radial, frame.forward) >= 0f ? frame.forward : -frame.forward;
                if (kind == SiteKind.Bath)
                {
                    DrinkTarget = drinkTarget.position;
                    Out = Vector3.ProjectOnPlane(anchor.position - DrinkTarget, Vector3.up).normalized;
                }
                Name = anchor.name;
                Kind = kind;
                // Both trough toes stand on the flat board, just inboard of the lip.
                Feet = anchor.position - (IsTrough ? Out * .034f : Vector3.zero);
            }
        }

        private sealed class Bird
        {
            public Transform Host;
            public CityLoftPigeonRig Rig;
            public int Site, Destination = -1, Ordinal, CandidateOffset;
            public float Elapsed, Duration, NextMove, NextCoo, RetryAt;
            public Quaternion DepartureRotation;
            public Collider LastObstacle;
            public Site BlockedDestination;
            public int BlockedSample;
            public bool LeavesShelf, EntersShelf;
            public float DrinkContact;
            public readonly Vector3[] Route = new Vector3[5];
            public readonly float[] SegmentEnd = new float[4];
        }

        public int BirdCount => initialized ? Population : 0;
        public int FlyingCount { get; private set; }
        public int LandingCount { get; private set; }
        public int BathVisitCount { get; private set; }
        public int BathDrinkingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < BirdCount; i++)
                    if (!IsFlying(i) && birds[i].DrinkContact >= .99f) count++;
                return count;
            }
        }
        public float BathContactError
        {
            get
            {
                float error = 0f;
                for (int i = 0; i < BirdCount; i++)
                    if (!IsFlying(i) && birds[i].DrinkContact >= .99f)
                        error = Mathf.Max(error, Vector3.Distance(birds[i].Rig.BillPosition, sites[birds[i].Site].DrinkTarget));
                return error;
            }
        }
        public int ShelteredCount
        {
            get
            {
                int covered = 0;
                for (int i = 0; i < BirdCount; i++)
                    if (!IsFlying(i) && sites[birds[i].Site].Kind == SiteKind.Shelf) covered++;
                return covered;
            }
        }
        public Transform GetBirdTransform(int index) => birds[index].Host;
        public bool IsFlying(int index) => birds[index].Destination >= 0;

        public string DescribeRoutes()
        {
            var text = new StringBuilder("loft minute=").Append(GameSessionState.GameMinuteOfDay)
                .Append(" shelter=").Append(shelter).Append(" paused=").Append(GameTimeScaleRuntime.IsPaused);
            for (int i = 0; i < BirdCount; i++)
            {
                Bird bird = birds[i];
                text.Append(" | ").Append(i).Append(' ').Append(sites[bird.Site].Name);
                if (bird.Destination >= 0) text.Append(" -> ").Append(sites[bird.Destination].Name);
                if (bird.LastObstacle && bird.BlockedDestination != null)
                    text.Append(" refused ").Append(bird.BlockedDestination.Name).Append(" at ")
                        .Append(bird.BlockedSample).Append("/16 by ").Append(bird.LastObstacle.name);
            }
            return text.ToString();
        }

        private readonly Bird[] birds = new Bird[Population];
        private readonly AudioSource[] voices = new AudioSource[2];
        private readonly int[] voiceOwners = { -1, -1 };
        private Site[] sites;
        private int feedSite, waterSite, firstShelf, cursor, routeBudget, bathSite = -1;
        private Material plumage;
        private AudioClip coo, wing;
        private Func<Transform> player;
        private Func<bool> keeperFeeding;
        private float clock, nextDecision, nextFarPose, nextCall;
        private bool initialized, audioPaused, shelter, feeding;

        public static CityPigeonFlock Create(Transform parent, Transform[] perches, Transform feeder,
            Transform water, Func<Transform> player, Func<bool> keeperFeeding,
            Transform bathPerch = null, Transform bathDrink = null)
        {
            if (!parent || !feeder || !water || perches == null || perches.Length < Population || player == null)
                throw new ArgumentException("A pigeon loft requires home, perches, feeder, water and a player provider.");
            if ((bathPerch == null) != (bathDrink == null))
                throw new ArgumentException("A courtyard bath requires both its standing stone and water contact.");
            var host = new GameObject("Domestic pigeon flock");
            host.transform.SetParent(parent, false);
            var flock = host.AddComponent<CityPigeonFlock>();
            try { flock.Initialize(perches, feeder, water, player, keeperFeeding, bathPerch, bathDrink); }
            catch
            {
                if (Application.isPlaying) Destroy(host); else DestroyImmediate(host);
                throw;
            }
            return flock;
        }

        private void Initialize(Transform[] perches, Transform feeder, Transform water,
            Func<Transform> playerProvider, Func<bool> feedProvider, Transform bathPerch, Transform bathDrink)
        {
            player = playerProvider;
            keeperFeeding = feedProvider;
            feedSite = perches.Length;
            waterSite = feedSite + 1;
            sites = new Site[perches.Length + 2 + (bathPerch ? 1 : 0)];
            int covered = 0;
            for (int i = 0; i < perches.Length; i++)
            {
                Transform p = perches[i];
                if (!p) throw new ArgumentException("Missing loft landing perch " + i);
                SiteKind kind = p.name.IndexOf("covered", StringComparison.OrdinalIgnoreCase) >= 0 ? SiteKind.Shelf :
                    p.name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0 ? SiteKind.Ground : SiteKind.Perch;
                sites[i] = new Site(p, kind, transform);
                if (kind == SiteKind.Shelf)
                {
                    if (covered == 0) firstShelf = i;
                    covered++;
                }
            }
            if (covered < Population) throw new ArgumentException("Each loft pigeon needs a separate sheltered perch.");
            sites[feedSite] = new Site(feeder, SiteKind.Feeder, transform);
            sites[waterSite] = new Site(water, SiteKind.Water, transform);
            if (bathPerch)
            {
                bathSite = waterSite + 1;
                sites[bathSite] = new Site(bathPerch, SiteKind.Bath, transform, bathDrink);
            }
            GameObject template = Required<GameObject>("LoftPigeon");
            coo = Required<AudioClip>("PigeonCoo");
            wing = Required<AudioClip>("PigeonWing");
            Shader lit = Resources.Load<Shader>("Shaders/Lit");
            if (!lit) throw new InvalidOperationException("Missing shared Lit shader for pigeon plumage.");
            plumage = new Material(lit) { hideFlags = HideFlags.HideAndDontSave, name = "Domestic pigeon plumages", enableInstancing = true };
            plumage.SetTexture("_BaseMap", Required<Texture2D>("PigeonPlumages"));
            plumage.SetFloat("_Metallic", 0f);
            plumage.SetFloat("_Smoothness", .18f);
            ReadConditions();
            for (int i = 0; i < Population; i++)
            {
                int index = InitialSite(i);
                var host = new GameObject("Domestic pigeon " + (i + 1)).transform;
                host.SetParent(transform, false);
                host.SetPositionAndRotation(sites[index].Feet, sites[index].Facing);
                var bird = new Bird { Host = host, Site = index, NextMove = 1.6f + i * 1.53f, NextCoo = 4f + i * 2.3f };
                bird.Rig = new CityLoftPigeonRig(host, template, plumage, i);
                birds[i] = bird;
                sites[index].Occupant = i;
                Present(i);
            }
            for (int i = 0; i < voices.Length; i++)
            {
                var host = new GameObject("Pigeon sound " + i);
                host.transform.SetParent(transform, false);
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1.2f;
                source.maxDistance = 14f;
                source.dopplerLevel = 0f;
                source.priority = 188;
                GameAudioMixer.Route(source, GameAudioGroup.AmbienceDetails);
                voices[i] = source;
            }
            initialized = true;
        }

        private static T Required<T>(string name) where T : UnityEngine.Object
        {
            T asset = Resources.Load<T>(Folder + name);
            return asset ? asset : throw new InvalidOperationException("Missing loft pigeon asset " + name);
        }

        private int InitialSite(int bird)
        {
            SiteKind desired = shelter || bird >= 4 ? SiteKind.Shelf : bird < 2 ? SiteKind.Ground : SiteKind.Perch;
            for (int i = 0; i < sites.Length; i++)
                if (sites[i].Kind == desired && sites[i].Occupant < 0) return i;
            for (int i = 0; i < sites.Length; i++)
                if (sites[i].Kind == SiteKind.Shelf && sites[i].Occupant < 0) return i;
            throw new InvalidOperationException("Loft has no free sleeping perch.");
        }

        private void ReadConditions()
        {
            var weather = GameWeatherRules.EvaluateCurrent().Kind;
            double minute = GameSessionState.GameMinuteOfDay;
            shelter = minute < 360 || minute >= 1320 || weather == WeatherKind.HeavyRain || weather == WeatherKind.Thunderstorm;
            feeding = !shelter && keeperFeeding != null && keeperFeeding();
        }

        private void Update() => Advance(Time.deltaTime);

        public void Advance(float deltaSeconds)
        {
            if (!initialized || !isActiveAndEnabled) return;
            bool paused = GameTimeScaleRuntime.IsPaused;
            if (paused != audioPaused)
            {
                audioPaused = paused;
                foreach (AudioSource voice in voices)
                    if (paused) voice.Pause(); else voice.UnPause();
            }
            if (paused || deltaSeconds <= 0f) return;
            Transform observer = player();
            float distanceSquared = observer ? (observer.position - sites[feedSite].Feet).sqrMagnitude : float.MaxValue;
            bool nearby = distanceSquared < 36f * 36f;
            clock += deltaSeconds;
            for (int i = 0; i < Population; i++)
            {
                Bird bird = birds[i];
                if (bird.Destination < 0) continue;
                bird.Elapsed += deltaSeconds;
                if (bird.Elapsed + .0001f < bird.Duration) continue;
                bird.Site = bird.Destination;
                bird.Destination = -1;
                if (bird.Site == bathSite) BathVisitCount++;
                bird.NextMove = clock + 5.5f + ((i * 5 + bird.Ordinal * 3) % 9);
                FlyingCount--;
                LandingCount++;
            }
            if (clock >= nextDecision)
            {
                nextDecision = clock + (nearby ? .22f : 1.1f);
                ReadConditions();
                routeBudget = 5;
                for (int n = 0; n < Population; n++)
                {
                    int i = (n + cursor) % Population;
                    Decide(i, observer, distanceSquared);
                }
                cursor = (cursor + 1) % Population;
            }
            if (nearby || clock >= nextFarPose)
            {
                nextFarPose = clock + (distanceSquared < 75f * 75f ? .12f : .75f);
                for (int i = 0; i < Population; i++) Present(i);
                for (int i = 0; i < voices.Length; i++)
                    if (voiceOwners[i] >= 0 && voices[i].isPlaying)
                        voices[i].transform.position = birds[voiceOwners[i]].Host.position + Vector3.up * .14f;
            }
        }

        private void Decide(int id, Transform observer, float distanceSquared)
        {
            Bird bird = birds[id];
            if (bird.Destination >= 0) return;
            Site current = sites[bird.Site];
            bool flee = current.IsGround && observer && (observer.position - current.Feet).sqrMagnitude < 2.25f * 2.25f;
            if (!shelter && distanceSquared < 14f * 14f && clock >= bird.NextCoo && clock >= nextCall && !flee)
            {
                Emit(id, coo, .23f);
                bird.NextCoo = clock + 12f + (id * 1.7f);
                nextCall = clock + 3.5f;
            }
            if (FlyingCount == AirCapacity || routeBudget <= 0 || clock < bird.RetryAt || (shelter && current.Kind == SiteKind.Shelf) ||
                (!flee && !shelter && clock < bird.NextMove)) return;
            if (!flee && feeding && current.Kind == SiteKind.Feeder) return;
            int count = sites.Length;
            int start = (3 * id + 5 * bird.Ordinal + bird.CandidateOffset + 1) % count;
            int attempts = 0;
            for (int step = 0; step <= count; step++)
            {
                int preferredWater = bathSite >= 0 && (id + bird.Ordinal) % 2 == 0 ? bathSite : waterSite;
                int target = step == 0 ? (feeding ? feedSite : (id + bird.Ordinal) % 3 == 0 ? preferredWater : start) : (start + step - 1) % count;
                Site next = sites[target];
                if (target == bird.Site || next.Occupant >= 0 || (shelter && next.Kind != SiteKind.Shelf) ||
                    (next.Kind == SiteKind.Feeder && !feeding) ||
                    (flee && (next.IsGround || (observer.position - next.Feet).sqrMagnitude < 2.5f * 2.5f)) ||
                    (next.IsGround && observer && (observer.position - next.Feet).sqrMagnitude < 2.25f * 2.25f)) continue;
                if ((current.Feet - next.Feet).sqrMagnitude < .55f * .55f) continue;
                routeBudget--;
                if (!PrepareRoute(bird, next, id))
                {
                    bird.CandidateOffset = (bird.CandidateOffset + 1) % count;
                    if (++attempts == 2 || routeBudget == 0) break;
                    continue;
                }
                bird.Destination = target;
                bird.DepartureRotation = bird.Host.rotation;
                bird.Elapsed = 0f;
                bird.Ordinal++;
                current.Occupant = -1;
                next.Occupant = id;
                FlyingCount++;
                if (distanceSquared < 18f * 18f) Emit(id, wing, .25f);
                return;
            }
            bird.RetryAt = clock + .8f;
        }

        private bool PrepareRoute(Bird bird, Site target, int id)
        {
            Site origin = sites[bird.Site];
            bird.LeavesShelf = origin.Kind == SiteKind.Shelf;
            bird.EntersShelf = target.Kind == SiteKind.Shelf;
            Vector3 from = StandingPosition(bird.Site, id);
            // Outside of the open nest front and the opposite feed ledge. Keep the
            // route local; a checked curved turn in this corner clears both faces.
            Vector3 a = Gateway(origin, from), b = Gateway(target, target.Feet);
            Vector3 middle = (a + b) * .5f;
            Vector3 sideOut = sites[firstShelf].Out, frontOut = sites[feedSite].Out;
            if (origin.Kind == SiteKind.Shelf || target.Kind == SiteKind.Shelf)
                middle += sideOut * Mathf.Max(0f, 1.05f - Vector3.Dot(middle - sites[firstShelf].Feet, sideOut));
            // The ground patches, maintenance perch and both trough boards all
            // approach from the front. Round the END of the deck guard on flights
            // between this front face and the covered side wall.
            if (Vector3.Dot(origin.Out, frontOut) > .6f || Vector3.Dot(target.Out, frontOut) > .6f)
                middle += frontOut * Mathf.Max(0f, 1f - Vector3.Dot(middle - sites[feedSite].Feet, frontOut));
            middle.y = Mathf.Max(a.y, b.y) + .65f + .20f * (id % 3);
            var p = bird.Route;
            p[0] = from; p[1] = a; p[2] = middle; p[3] = b; p[4] = target.Feet;
            float length = 0f;
            for (int i = 0; i < 4; i++)
            {
                length += Mathf.Max(.01f, Vector3.Distance(p[i], p[i+1]));
                bird.SegmentEnd[i] = length;
            }
            for (int i = 0; i < 4; i++) bird.SegmentEnd[i] /= length;
            bird.Duration = Mathf.Clamp(length / 3.3f + .55f, 1.45f, 5.4f);
            // Conservative body sweep on the same curve as presentation. Ignore triggers
            // (prompts/weather) and never fly a blocked fallback. Eight-perch work is bounded.
            int sampleIndex = 0;
            Vector3 previous = from + Vector3.up * .135f;
            float previousSpread = 0f;
            for (int sample = 1; sample <= 16; sample++)
            {
                sampleIndex = sample;
                float t = sample / 16f;
                Vector3 feet = FlightPoint(bird, t);
                float spread = WingSpread(bird, feet, t);
                Vector3 current = feet + Vector3.up * .135f;
                Vector3 span = current - previous;
                if (span.sqrMagnitude > .000001f)
                {
                    Vector3 direction = span.normalized;
                    float distance = span.magnitude;
                    if (Blocked(previous, .052f, direction, distance)) return false;
                    float sweep = Mathf.Max(previousSpread, spread);
                    if (sweep > .1f)
                    {
                        Vector3 across = Vector3.Cross(Vector3.up, direction).normalized * Mathf.Lerp(.04f, .216f, sweep);
                        if (Blocked(previous + across, .055f, direction, distance) ||
                            Blocked(previous - across, .055f, direction, distance)) return false;
                    }
                }
                for (int i = 0; i < Population; i++)
                {
                    Bird other = birds[i];
                    if (other.Destination < 0) continue;
                    float phase = (other.Elapsed + t * bird.Duration) / other.Duration;
                    Vector3 otherPosition = phase < 1f ? FlightPoint(other, phase) : sites[other.Destination].Feet;
                    if ((current - Vector3.up * .135f - otherPosition).sqrMagnitude < .36f * .36f) return false;
                }
                previous = current;
                previousSpread = spread;
            }
            bird.LastObstacle = null;
            bird.BlockedDestination = null;
            return true;

            bool Blocked(Vector3 start, float radius, Vector3 direction, float distance)
            {
                if (!Physics.SphereCast(start, radius, direction, out RaycastHit hit, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                bird.LastObstacle = hit.collider;
                bird.BlockedDestination = target;
                bird.BlockedSample = sampleIndex;
                return true;
            }
        }

        private static Vector3 Gateway(Site site, Vector3 start)
        {
            // A shallow lift clears the raised timber lip before the wings open outside the shelf.
            return start + site.Out * (site.Kind == SiteKind.Shelf ? .84f : .47f) +
                Vector3.up * (site.Kind == SiteKind.Shelf ? .155f : .43f);
        }

        private static Vector3 FlightPoint(Bird bird, float t)
        {
            t = Mathf.Clamp01(t);
            float along = t * t * (3f - 2f * t);
            int segment = along <= bird.SegmentEnd[0] ? 0 : along <= bird.SegmentEnd[1] ? 1 : along <= bird.SegmentEnd[2] ? 2 : 3;
            float lower = segment == 0 ? 0f : bird.SegmentEnd[segment-1];
            float u = Mathf.InverseLerp(lower, bird.SegmentEnd[segment], along);
            Vector3[] p = bird.Route;
            Vector3 a = p[segment], b = p[segment+1];
            float chord = Vector3.Distance(a,b);
            Vector3 before = segment > 0 ? p[segment-1] : a;
            Vector3 after = segment < 3 ? p[segment+2] : b;
            Vector3 leave = b - before, approach = after - a;
            // Through a nest aperture the path is normal to its face. Steering
            // diagonally past the divider with a deployed wing would clip its edge.
            if (bird.LeavesShelf && segment == 0) approach = p[1] - p[0];
            if (bird.LeavesShelf && segment == 1) leave = p[1] - p[0];
            if (bird.EntersShelf && segment == 2) approach = p[4] - p[3];
            if (bird.EntersShelf && segment == 3) leave = p[4] - p[3];
            // Level the tangent at each exterior gateway and at the apex. A steep
            // drop at the previous waypoint otherwise undershoots the sill just
            // before touchdown, causing even a clear nest opening to reject it.
            if (segment > 0) leave.y = 0f;
            if (segment < 3) approach.y = 0f;
            Vector3 c = a + leave.normalized * chord / 3f;
            Vector3 d = b - approach.normalized * chord / 3f;
            float v = 1f - u;
            return v*v*v*a + 3f*v*v*u*c + 3f*v*u*u*d + u*u*u*b;
        }

        private static float WingSpread(Bird bird, Vector3 position, float t)
        {
            float spread = Mathf.SmoothStep(0f, 1f, t / .17f) * Mathf.SmoothStep(0f, 1f, (1f - t) / .10f);
            if (bird.LeavesShelf) spread *= OpeningClearance(position, bird.Route[0], bird.Route[1]);
            if (bird.EntersShelf) spread *= OpeningClearance(position, bird.Route[4], bird.Route[3]);
            return spread;
        }

        private static float OpeningClearance(Vector3 point, Vector3 feet, Vector3 gateway)
        {
            // Fold at the lip itself; full span only beyond the outermost divider.
            Vector3 normal = Vector3.ProjectOnPlane(gateway - feet, Vector3.up).normalized;
            float outward = Vector3.Dot(point - feet, normal);
            // A point on the far side of the whole building can share the normal
            // coordinate of a shelf. Only the entrance's short gateway tube matters.
            if ((point - feet).sqrMagnitude > 1.2f) return 1f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.16f, .81f, outward));
        }

        private Vector3 StandingPosition(int siteIndex, int id)
        {
            Site s = sites[siteIndex];
            if (s.Kind != SiteKind.Ground) return s.Feet;
            // A small foraging path belongs entirely to the surveyed ground patch.
            float elapsed = Mathf.Max(0f, clock - birds[id].NextMove + 5f);
            return s.Feet + s.Out * (.14f * Mathf.Sin(elapsed * 1.1f));
        }

        private void Present(int id)
        {
            Bird bird = birds[id];
            bird.DrinkContact = 0f;
            float phaseClock = clock + id * 2.73f;
            if (bird.Destination >= 0)
            {
                float t = Mathf.Clamp01(bird.Elapsed / bird.Duration);
                Vector3 position = FlightPoint(bird, t);
                Vector3 tangent = FlightPoint(bird, Mathf.Min(1f, t+.012f)) - FlightPoint(bird, Mathf.Max(0f,t-.012f));
                tangent.y = 0f;
                Quaternion forward = tangent.sqrMagnitude > .000001f ? Quaternion.LookRotation(tangent) : bird.DepartureRotation;
                Quaternion aim = Quaternion.Slerp(bird.DepartureRotation, forward, Mathf.Clamp01(t * 9f));
                aim = Quaternion.Slerp(aim, sites[bird.Destination].Facing, Mathf.SmoothStep(0f,1f, (t-.83f)/.17f));
                bird.Host.SetPositionAndRotation(position, aim);
                float spread = WingSpread(bird, position, t);
                float legs = Mathf.SmoothStep(0f,1f,t/.16f) * Mathf.SmoothStep(0f,1f,(1f-t)/.27f);
                float flare = Mathf.SmoothStep(0f,1f,(t-.68f)/.18f) * Mathf.SmoothStep(0f,1f,(1f-t)/.12f);
                float flap = 9f + 44f * Mathf.Sin(phaseClock * (Mathf.PI*2f*6.8f)) * (1f-.6f*flare);
                bird.Rig.Pose(phaseClock, spread, flap, legs, flare, 0f,0f,0f,false,true);
                return;
            }
            Site site = sites[bird.Site];
            bird.Host.SetPositionAndRotation(StandingPosition(bird.Site,id), site.Facing);
            if (site.Kind == SiteKind.Bath)
            {
                float cycle = Mathf.Repeat(phaseClock / 3.6f, 1f);
                float dip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.15f, .53f, cycle)) *
                    (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.70f, .97f, cycle)));
                bird.DrinkContact = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.92f, 1f, dip));
                bird.Rig.Pose(phaseClock, 0f, 0f, 0f, 0f, 0f, dip, 0f, false, false,
                    site.DrinkTarget, bird.DrinkContact);
                return;
            }
            bool tucked = shelter && site.Kind == SiteKind.Shelf;
            float wave = .5f + .5f*Mathf.Sin(phaseClock * 3.8f);
            bool eating = site.Kind == SiteKind.Feeder || site.Kind == SiteKind.Water;
            float peck = eating && !tucked ? (site.Kind == SiteKind.Water ? .54f : .62f) * wave * wave :
                !tucked && site.Kind == SiteKind.Ground && Mathf.Sin(phaseClock*.67f) > .3f ? wave * wave : 0f;
            float preen = !tucked && !eating && Mathf.Sin(phaseClock*.44f) > .7f ? Mathf.SmoothStep(0f,1f,wave) : 0f;
            float step = site.Kind == SiteKind.Ground && peck < .15f && clock - bird.NextMove + 5f > 0f ? 1f : 0f;
            bird.Rig.Pose(phaseClock,0f,0f,0f,0f,step,peck,preen,tucked,false);
        }

        private void Emit(int bird, AudioClip clip, float gain)
        {
            if (audioPaused) return;
            for (int i = 0; i < voices.Length; i++)
            {
                AudioSource source = voices[i];
                if (source.isPlaying) continue;
                voiceOwners[i] = bird;
                source.transform.position = birds[bird].Host.position + Vector3.up*.14f;
                source.volume = gain;
                source.pitch = .91f + bird * .034f;
                source.clip = clip;
                source.Play();
                return;
            }
        }

        private void OnDisable()
        {
            foreach (AudioSource voice in voices) if (voice) voice.Stop();
            audioPaused = false;
        }

        private void OnDestroy()
        {
            if (!plumage) return;
            if (Application.isPlaying) Destroy(plumage); else DestroyImmediate(plumage);
        }
    }
}
