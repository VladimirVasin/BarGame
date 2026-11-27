using System;
using UnityEngine;

namespace BarPromenade
{
    // Calendar time places each visitor, including a first visit halfway through an evening.
    internal sealed partial class RidgeMotelCourtLife : MonoBehaviour
    {
        private static readonly int[] ArrivalMinutes = { 17 * 60 + 35, 17 * 60 + 46, 19 * 60 + 4, 19 * 60 + 23 };
        private static readonly int[] LeavingMinutes = { 22 * 60 + 47, 22 * 60 + 28, 22 * 60 + 8, 21 * 60 + 42 };
        private const float ApproachSeconds = 15f, ClearChairSeconds = 2f;
        private const float SeatTop = .463f, TableTop = .773f;
        private readonly Guest[] guests = new Guest[4];
        private readonly Vector3[] table = new Vector3[2];
        private readonly AudioSource[] glass = new AudioSource[2];
        private Transform observer, bag, opener;
        private Vector3 bagRest, openerRest;
        private Quaternion bagRotation, openerRotation;
        private double previous = double.NaN, nextFarCheck;
        private bool visible;
        private int lastDay = -1;

        internal sealed class Guest
        {
            internal Transform Chair, Seat;
            internal Vector3 HomeChair, BaseOut, Out, Feet, Dock, Top;
            internal Quaternion ChairRotation;
            internal CityPedestrianPresentation Body;
            internal VillageResidentPresentation Character;
            internal CityPedestrianSeatedRide Ride;
            internal RidgeKioskVisitorHands Hands;
            internal BarPatronDrinkProp Beer;
            internal Renderer Cap;
            internal KioskVisitorFace Face;
            internal BarPatronDrinkingArmPose Pose;
            internal int Table, Sips;
        }

        internal Guest At(int i) => guests[i];
        internal static int PartySize(int day) => day % 3 == 1 ? 2 : 4;
        private static bool ShareTable(int day) => day % 3 == 2;
        private static float RealSeconds(double minutes) => (float)(minutes / GameTimeState.GameMinutesPerRealSecond);

        internal void Initialize(Transform model)
        {
            for (int i = 0; i < table.Length; i++)
            {
                var top = model.Find("PatioTable" + i);
                table[i] = top.position + Vector3.up * TableTop;
                glass[i] = top.gameObject.AddComponent<AudioSource>();
                glass[i].playOnAwake = false;
                glass[i].volume = .22f;
                CityWorkAudio.Configure(glass[i], false, 5600f, 12f, false);
            }
            for (int i = 0; i < guests.Length; i++)
            {
                Transform mesh = model.Find("PatioChair" + i);
                Vector3 outwards = Vector3.ProjectOnPlane(mesh.position - table[i / 2], Vector3.up).normalized;
                Transform chair = new GameObject("Motel chair " + i).transform;
                chair.SetParent(transform, false);
                chair.SetPositionAndRotation(mesh.position, Quaternion.LookRotation(-outwards));
                mesh.SetParent(chair, true);
                var solid = chair.gameObject.AddComponent<BoxCollider>();
                solid.size = new Vector3(.43f, SeatTop, .40f);
                solid.center = Vector3.up * SeatTop * .5f;
                var back = chair.gameObject.AddComponent<BoxCollider>();
                back.size = new Vector3(.45f, .39f, .045f);
                back.center = new Vector3(0, .62f, -.195f);
                Transform seat = new GameObject("Seat top").transform;
                seat.SetParent(chair, false);
                seat.localPosition = Vector3.up * SeatTop;
                guests[i] = new Guest { Chair = chair, Seat = seat, HomeChair = chair.position,
                    BaseOut = outwards, ChairRotation = chair.rotation };
            }
            bag = model.Find("BottleBag"); bagRotation = bag.rotation;
            bagRest = table[0] + new Vector3(.03f, -TableTop, -.59f);
            bag.gameObject.SetActive(false);
            opener = model.Find("Opener"); openerRest = opener.position; openerRotation = opener.rotation;
            InitializeLinen(model);
        }

        internal void Bind(Transform hero, Camera camera)
        {
            observer = hero;
            if (!speech)
            {
                speech = gameObject.AddComponent<NpcSpeechBubbleView>();
                speech.UseManualClock = true;
                deck = new BarSpeechVariantDeck(Topics.Length, GameSessionState.CitySeed ^ 0x4652);
            }
            speech.Initialize(camera, hero);
            nextFarCheck = 0;
            Sample(true);
        }

        private void Update()
        {
            if (!observer || GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning) return;
            if (!visible && Time.unscaledTimeAsDouble < nextFarCheck) return;
            Sample(false);
        }

        internal void Sample(bool seek)
        {
            if (!observer) return;
            int day = GameSessionState.GameDayIndex;
            double clock = GameSessionState.GameTimeOfDayMinutes;
            double now = (day * 1440d + clock) / GameTimeState.GameMinutesPerRealSecond;
            double elapsed = now - previous;
            bool near = (observer.position - transform.position).sqrMagnitude < (visible ? 76f * 76f : 66f * 66f);
            seek |= near != visible || day != lastDay || double.IsNaN(elapsed) || elapsed < 0d || elapsed > 2d;
            previous = now; lastDay = day; visible = near;
            nextFarCheck = Time.unscaledTimeAsDouble + .4d;
            if (seek) EndExchange();
            float delta = seek ? 0f : (float)elapsed;
            int size = PartySize(day);
            for (int i = 0; i < guests.Length; i++)
            {
                SetChair(i, day, clock);
            }
            bool traveller = Traveller(clock, delta, near, seek);
            for (int i = 0; i < guests.Length; i++)
            {
                if (i == 1 && traveller) continue;
                if (i < size && near && clock >= ArrivalMinutes[i] && clock < LeavingMinutes[i] + (i == 0 ? 12 : 5))
                    Visit(i, day, clock, delta, seek);
                else if (i == 0 && near && clock >= 8 * 60 && clock < 17 * 60 + 21)
                    Clerk(clock, delta);
                else Retire(guests[i]);
            }
            SampleDay(clock, near);
            PresentBottles(clock, size, near);
            if (near) Talk(delta, seek);
        }

        private void SetChair(int i, int day, double minute)
        {
            Guest g = guests[i];
            g.Table = i / 2;
            float arrival = RealSeconds(minute - ArrivalMinutes[i]);
            float pulled = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ApproachSeconds - 1.3f, ApproachSeconds, arrival));
            if (i == 0 && minute > LeavingMinutes[0])
                pulled *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(RealSeconds(minute - LeavingMinutes[0]) - .8f));
            if (i >= PartySize(day)) pulled = 0;
            Vector3 centre = g.HomeChair, outward = g.BaseOut;
            if (i >= 2 && ShareTable(day) && arrival > 9f)
            {
                Vector3 along = Vector3.Cross(Vector3.up, guests[0].BaseOut) * (i == 2 ? 1f : -1f);
                float travel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, ApproachSeconds, arrival));
                centre = Vector3.Lerp(centre, table[0] - Vector3.up * TableTop + along * .79f, travel);
                outward = Quaternion.Slerp(g.ChairRotation, Quaternion.LookRotation(-along), travel) * Vector3.back;
                g.Table = 0;
            }
            g.Out = outward; g.Top = table[g.Table];
            g.Feet = centre + outward * (.13f * pulled);
            Quaternion facing = Quaternion.LookRotation(-outward);
            if (g.Chair.position != g.Feet || g.Chair.rotation != facing) g.Chair.SetPositionAndRotation(g.Feet, facing);
            g.Dock = BottleBase(g);
        }

        private static Vector3 BottleBase(Guest g) =>
            g.Top + g.Out * .31f + Vector3.Cross(Vector3.up, -g.Out) * .12f;

        private void PrepareGuest(int i)
        {
            Guest g = guests[i];
            if (g.Body) return;
            var registry = CityPedestrianDefaultNpcBody.Create(transform, DefaultNpcPopulation.MotelGuestId(i),
                CityPedestrianResources.LoadStreetClipDonor());
            g.Character = registry.GetComponent<CityPedestrianDefaultNpcBody>().Character;
            g.Body = registry.gameObject.AddComponent<CityPedestrianPresentation>();
            g.Body.Initialize(registry);
            g.Body.ConfigureCycle(1f, i * .23f);
            if (!CityPedestrianResources.TryGetArchetype(registry.DesignId, out var archetype) || archetype.SeatedRide == null)
                throw new InvalidOperationException("Motel visitor lacks a seated clip.");
            g.Ride = archetype.SeatedRide;
            g.Face = new KioskVisitorFace(g.Character);
            g.Hands = new RidgeKioskVisitorHands(g.Body, g.Body.transform);
        }

        private void PrepareBeer(int i)
        {
            Guest g = guests[i];
            if (g.Beer != null) return;
            g.Beer = BarPatronDrinkProp.Create(transform, BarPatronDrinkPropKind.Bottle,
                BarDrinkPresentationCatalog.Get(i == 2 ? DrinkId.DarkBeer : DrinkId.LightBeer));
            g.Beer.Root.GetComponentInChildren<BarServicePropInstance>().TryGetRenderer("service_bottle_closure", out g.Cap);
        }

        private void Visit(int i, int day, double minute, float dt, bool seek)
        {
            Guest g = guests[i];
            PrepareGuest(i);
            PrepareBeer(i);
            g.Body.gameObject.SetActive(true);
            float arrival = RealSeconds(minute - ArrivalMinutes[i]);
            float leaving = RealSeconds(minute - LeavingMinutes[i]);
            float collectedAt = ClearChairSeconds + 5f * PartySize(day) + 4f;
            bool seated = arrival >= ApproachSeconds && leaving < 0;
            if (seated)
            {
                if (!g.Body.IsSeated || seek)
                {
                    ReleasePose(g);
                    g.Body.transform.SetPositionAndRotation(g.Feet, Quaternion.LookRotation(-g.Out));
                    if (!seek) g.Body.BeginGroundSeatTransition(.85f);
                    if (!g.Body.TrySeat(g.Seat, g.Ride, Vector3.up, g.Feet, -g.Out))
                        throw new InvalidOperationException("Motel visitor could not sit on his chair.");
                }
                bool bottlesOpen = i >= 2 || RealSeconds(minute - ArrivalMinutes[1]) >= ApproachSeconds + 6f;
                if (bottlesOpen && !g.Pose)
                {
                    g.Beer.Root.gameObject.SetActive(true);
                    var cadence = new BarPatronDrinkTimeline(unchecked(GameSessionState.CitySeed ^ (day * 197 + i * 919)));
                    g.Pose = g.Hands.BindTable(g.Beer, g.Top, g.Out, .31f, cadence, false, true);
                    g.Pose.SipCompleted = () => g.Sips++;
                    g.Sips = 0;
                }
                if (g.Pose)
                {
                    g.Pose.SampleContacts = (observer.position - g.Feet).sqrMagnitude < 32f * 32f;
                    g.Pose.HoldNewSips = leaving > -6f;
                }
                var previousPhase = g.Pose ? g.Pose.Timeline.Phase : BarPatronDrinkPhase.Rest;
                g.Body.Advance(dt, false);
                if (previousPhase == BarPatronDrinkPhase.Lower && g.Pose.Timeline.Phase == BarPatronDrinkPhase.Rest)
                    Clink(g.Table);
                return;
            }

            if (g.Body.IsSeated && !seek) g.Body.BeginGroundSeatTransition(.8f);
            ReleasePose(g);
            Vector3 gate = transform.TransformPoint(new Vector3(17f + i, 0, 17.1f));
            Vector3 aisle = new Vector3(g.HomeChair.x, gate.y, transform.position.z + 15.15f);
            Vector3 side = Vector3.Cross(Vector3.up, g.Out);
            if (side.z < 0f) side = -side;
            Vector3 next = g.Feet + side * .52f;
            Vector3 position, facing;
            if (leaving < 0f && i >= 2 && ShareTable(day) && arrival >= 9f)
            {
                position = g.Feet + side * Mathf.Lerp(.52f, 0f, Mathf.InverseLerp(13.9f, 15f, arrival));
                facing = Quaternion.Slerp(Quaternion.LookRotation(-side), Quaternion.LookRotation(-g.Out),
                    Mathf.InverseLerp(13.9f, 15f, arrival)) * Vector3.forward;
            }
            else if (i == 0 && leaving >= ClearChairSeconds && leaving < collectedAt)
            {
                CollectionRoute(leaving, out position, out facing);
            }
            else
            {
                float progress = leaving < 0f ? Mathf.Clamp01(arrival / (ShareTable(day) && i >= 2 ? 9f : ApproachSeconds))
                    : 1f - Mathf.Clamp01((leaving - (i == 0 ? collectedAt : ClearChairSeconds)) / ApproachSeconds);
                Route(gate, aisle, next, g.Feet, progress, out position, out facing);
                if (leaving >= 0f) facing = -facing;
                if (progress > .90f)
                    facing = Vector3.Slerp(facing, -g.Out, Mathf.InverseLerp(.90f, 1f, progress));
            }
            bool busy = leaving >= ClearChairSeconds && leaving < collectedAt && i == 0;
            if (leaving >= (i == 0 ? collectedAt : ClearChairSeconds) + ApproachSeconds) { Retire(g); return; }
            Vector3 displacement = position - g.Body.transform.position;
            g.Body.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
            g.Body.AdvanceTravelled(dt, !seek && displacement.sqrMagnitude > .000001f, seek ? Vector3.zero : displacement);
            if (leaving < 0f && i != 1)
            {
                g.Beer.Root.gameObject.SetActive(true);
                g.Hands.Carry(g.Beer);
            }
            if (leaving < 0f && arrival > (i >= 2 && ShareTable(day) ? 9f : ApproachSeconds - 1.4f))
                g.Hands.ReachFor(g.Chair.TransformPoint(new Vector3(.15f,.76f,-.20f)), .85f, left: true);
            if (busy) ReachForEmpties(g, leaving);
        }

        private static void Route(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float progress,
            out Vector3 position, out Vector3 facing)
        {
            float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c), cd = Vector3.Distance(c, d);
            float metres = progress * (ab + bc + cd);
            if (metres < ab) { facing = (b - a).normalized; position = a + facing * metres; }
            else if (metres < ab + bc) { facing = (c - b).normalized; position = b + facing * (metres - ab); }
            else { facing = (d - c).normalized; position = Vector3.Lerp(c, d, (metres - ab - bc) / Mathf.Max(cd, .001f)); }
        }

        private void CollectionRoute(float leaving, out Vector3 position, out Vector3 facing)
        {
            float time = leaving - ClearChairSeconds;
            int size = PartySize(lastDay);
            int index = Mathf.Min(size - 1, (int)(time / 5f));
            Guest previousGuest = guests[Mathf.Min(Mathf.Max(index - 1, 0), size - 1)];
            Guest target = guests[Mathf.Min(index, size - 1)];
            Vector3 from = index == 0 ? guests[0].Feet : CollectionDock(previousGuest);
            Vector3 to = CollectionDock(target);
            Vector3 aisle = new(from.x, from.y, transform.position.z + 14.78f);
            Vector3 aisleEnd = new(to.x, to.y, aisle.z);
            float t = Mathf.Clamp01((time - index * 5f) / 3.8f);
            if (t >= 1f) { position = to; facing = Vector3.ProjectOnPlane(target.Dock - to, Vector3.up).normalized; }
            else if (index == 0) { position = Vector3.Lerp(from,to,t); facing = -target.Out; }
            else Route(from, aisle, aisleEnd, to, t, out position, out facing);
            if (time < size * 5f) return;
            float back = Mathf.InverseLerp(size * 5f, size * 5f + 4f, time);
            var home = guests[0];
            Route(to, aisleEnd, new Vector3(home.Feet.x, home.Feet.y, aisle.z), home.Feet, back, out position, out facing);
            if (facing.sqrMagnitude < .1f) facing = -home.Out;
        }

        private static Vector3 CollectionDock(Guest g)
        {
            Vector3 side = Vector3.Cross(Vector3.up, g.Out);
            if (side.z < 0) side = -side;
            return g.Top - Vector3.up * TableTop + g.Out * .54f + side * .60f;
        }

        private void ReachForEmpties(Guest collector, float leaving)
        {
            float t = leaving - ClearChairSeconds;
            int slot = Mathf.FloorToInt(t / 5f);
            if (slot >= PartySize(lastDay) || t % 5f < 3.8f) return;
            Guest target = guests[slot];
            if (target.Beer == null) return;
            float reach = Mathf.InverseLerp(3.8f, 4.32f, t % 5f);
            float lift = Mathf.InverseLerp(4.32f, 4.94f, t % 5f);
            collector.Hands.ReceiveGift(target.Beer, target.Dock, reach, lift, lift > 0f);
        }

        private void PresentBottles(double minute, int size, bool near)
        {
            float arrival = RealSeconds(minute - ArrivalMinutes[0]);
            float leaving = RealSeconds(minute - LeavingMinutes[0]);
            float opening = RealSeconds(minute - ArrivalMinutes[1]) - ApproachSeconds;
            float collectedAt = ClearChairSeconds + size * 5f + 4f;
            bool party = near && arrival >= 0 && leaving < collectedAt + ApproachSeconds;
            bag.gameObject.SetActive(party);
            if (party)
            {
                Guest first = guests[0];
                bool carryingBag = leaving >= ClearChairSeconds || arrival < ApproachSeconds;
                bag.SetPositionAndRotation(carryingBag && first.Body.gameObject.activeSelf
                    ? first.Character.LeftGrip.position - Vector3.up * .42f : bagRest, bagRotation);
            }
            for (int i = 0; i < guests.Length; i++)
            {
                Guest g = guests[i];
                bool brought = party && i < size && (i == 1 || minute >= ArrivalMinutes[i]);
                bool collected = leaving - ClearChairSeconds >= (i + 1) * 5f;
                if (!brought || collected)
                {
                    if (g.Beer != null) g.Beer.Root.gameObject.SetActive(false);
                    continue;
                }
                PrepareBeer(i);
                g.Beer.Root.gameObject.SetActive(true);
                if (g.Cap) g.Cap.enabled = i < 2 ? opening < (i == 0 ? 1.9f : 3.6f)
                    : RealSeconds(minute - ArrivalMinutes[i]) < ApproachSeconds;
                if (i < 2 && arrival >= ApproachSeconds && opening < 6f)
                {
                    Vector3 offered = guests[1].Top + guests[1].Out * .24f + Vector3.Cross(Vector3.up, guests[1].Out) * .15f;
                    SetBottleBase(g, i == 1 ? g.Dock : Vector3.Lerp(offered, g.Dock,
                        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4.25f, 5.85f, opening))));
                    continue;
                }
                if (g.Pose || (i != 1 && minute < LeavingMinutes[i] && g.Body && g.Body.gameObject.activeSelf)) continue;
                float lift = leaving - ClearChairSeconds - i * 5f - 4.32f;
                if (lift > 0f && lift < .68f && guests[0].Body)
                {
                    Vector3 mouthOfBag = bag.position + Vector3.up * .30f;
                    SetBottleBase(g, Vector3.Lerp(g.Dock, mouthOfBag, lift / .68f));
                }
                else if (i == 1 && arrival < ApproachSeconds)
                    SetBottleBase(g, bag.position + Vector3.up * .11f);
                else SetBottleBase(g, g.Dock);
            }
            if (party && opening >= 0 && opening < 5.9f) OpenPair(opening);
            else if (opener.position != openerRest) opener.SetPositionAndRotation(openerRest, openerRotation);
        }

        private void OpenPair(float t)
        {
            Guest mate = guests[1];
            Vector3 rest = openerRest;
            Vector3 first = guests[0].Beer.Mouth.position, second = mate.Beer.Mouth.position;
            Vector3 target = Vector3.Lerp(rest, first, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, 1.8f, t)));
            target = Vector3.Lerp(target, second, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.2f, 3.5f, t)));
            target = Vector3.Lerp(target, rest, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.8f, 4.5f, t)));
            float grip = Mathf.InverseLerp(0f, .5f, t) * (1f - Mathf.InverseLerp(4.5f, 5f, t));
            Vector3 contact = mate.Hands.ReachFor(target, grip);
            opener.SetPositionAndRotation(Vector3.Lerp(rest, contact, grip), openerRotation);
            if (t > 4.45f)
                guests[0].Hands.ReachFor(guests[0].Beer.Root.position, Mathf.Sin(Mathf.PI * Mathf.InverseLerp(4.45f, 5.9f, t)));
        }

        private void Clink(int index)
        {
            var source = glass[index];
            if ((observer.position - table[index]).sqrMagnitude >= source.maxDistance * source.maxDistance) return;
            if (!source.clip) source.clip = RetroAudioService.EnsureInstalled().GetClip(RetroSfxId.Clink);
            source.Play();
        }

        private void SetBottleBase(Guest g, Vector3 point)
        {
            if (g.Beer.Root.parent != transform) g.Beer.Root.SetParent(transform, true);
            Quaternion upright = Quaternion.LookRotation(-g.Out);
            g.Beer.Root.SetPositionAndRotation(point - upright * g.Beer.BaseLocalPosition, upright);
        }

        private void ReleasePose(Guest g)
        {
            if (!g.Pose)
            {
                if (g.Body && g.Body.IsSeated) g.Body.ClearSeat();
                return;
            }
            g.Pose.ReleaseTableBinding();
            g.Pose = null;
            g.Beer.Root.SetParent(transform, true);
            g.Body.ClearSeat();
            g.Hands.RestoreContactCulling();
        }

        private void Retire(Guest g)
        {
            if (!g.Body || !g.Body.gameObject.activeSelf) return;
            ReleasePose(g);
            g.Hands.RestoreContactCulling();
            g.Beer?.Root.SetParent(transform, true);
            g.Body.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            EndExchange();
            // Streaming deactivates the whole hierarchy. The next sample rebinds at calendar time.
            previous = double.NaN; visible = false; nextFarCheck = 0;
        }
    }
}
