using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A finite scene-owned camera deck. Real cast anchors supply close-ups;
    /// the shared director retains the camera, collision, optics and DOF loan.
    /// </summary>
    public sealed class BarKaraokeCameraSequence : IDisposable
    {
        public const int SingerVariantCount = 6;
        public const int VariantsPerListener = 2;
        public const float FirstWashroomMinimumSeconds = 60f;
        public const float MinimumWashroomCooldownSeconds = 90f;
        public const float MaximumWashroomCooldownSeconds = 150f;
        private const int IntroCount = 3;
        private const int Capacity = IntroCount + SingerVariantCount +
            VariantsPerListener * (BarBusinessHours.MaximumGuests + 1);
        private const int ClearanceVariants = 6;
        private const int PathSamples = 5;
        private const float ValidationSeconds = .1f;
        private const float MaximumAdvanceSeconds = 8f;
        private const float OrdinaryClearance = .16f;
        private readonly BarInteriorRoot bar;
        private readonly BarKaraokePlan plan;
        private readonly ContextualCameraDirector director;
        private readonly Entry[] entries = new Entry[Capacity];
        private readonly int[] singerDeck = new int[SingerVariantCount];
        private readonly int[] listenerDeck = new int[VariantsPerListener * (BarBusinessHours.MaximumGuests + 1)];
        private readonly Transform heroRoot, heroHead, heroPelvis;
        private readonly Transform bowlFrame;
        private readonly BoxCollider bowlPedestalProxy;
        private int entryCount, listenerCount, singerCursor, listenerCursor;
        private uint random;
        private bool performing, disposed, deckDirty = true, suspended, introFinished, moveBlocked;
        private float elapsed, validationClock;
        private double activeSeconds, nextWashroomSeconds;
        private Transform lastListener, focusAnchor;
        private Vector3 headAtCut;
        private Pose lastSafePose;
        private Vector3 lastSafeFocus;
        private float lastSafeFov;
        private BarKaraokeCameraShot currentShot;

        private struct Entry
        {
            public Transform Root, Head;
            public BarPatron Patron;
            public BarKaraokeShotKind Kind;
            public int Variant;
            public string Id;
        }

        public int CurrentShotIndex { get; private set; }
        public float Progress => currentShot.Seconds > 0f ? Mathf.Clamp01(elapsed / currentShot.Seconds) : 0f;
        public int CutCount { get; private set; }
        public int ShotCount => entryCount + 4;
        public Transform CurrentSubject => currentShot.Subject;
        public BarKaraokeShotKind CurrentKind => currentShot.Kind;
        public BarKaraokeCameraShot CurrentShot => currentShot;
        public BarKaraokeWashroomShotKind CurrentWashroomKind { get; private set; }
        public float SecondsUntilWashroomShot => (float)Math.Max(0d, nextWashroomSeconds - activeSeconds);
        public int WashroomCutCount { get; private set; }
        public bool HasSafeShot { get; private set; } = true;

        public BarKaraokeCameraSequence(BarInteriorRoot owner, BarKaraokePlan cameraPlan,
            ContextualCameraDirector cameraDirector)
        {
            bar = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            if (bar.Crowd == null) throw new ArgumentException("Compose the finite bar cast before its camera sequence.", nameof(owner));
            plan = cameraPlan ?? throw new ArgumentNullException(nameof(cameraPlan));
            director = cameraDirector ?? throw new ArgumentNullException(nameof(cameraDirector));
            var presentation = bar.Player.Visual as Player3DCharacterPresentation;
            if (presentation == null) throw new ArgumentException("Karaoke cameras require the actual production hero.", nameof(owner));
            heroRoot = bar.Player.GameObject.transform;
            heroHead = presentation.Registry.Anchors.Head;
            heroPelvis = presentation.Registry.Anchors.Pelvis;
            bowlFrame = bar.Washroom != null ? bar.Washroom.FixtureFrame : null;
            Transform pedestal = bowlFrame != null ? bowlFrame.Find("Bar Washroom ToiletPedestal") : null;
            bowlPedestalProxy = pedestal != null ? pedestal.GetComponent<BoxCollider>() : null;
            random = bar.Layout.StableSeed ^ 0x5A17C9E3u ^ unchecked((uint)GameSessionState.GameDayIndex * 397u) ^
                unchecked((uint)GameSessionState.GameMinuteOfDay * 127u);
            var present = bar.Crowd.Patrons;
            for (int i = 0; i < present.Count; i++)
            {
                string signature = present[i].Registry != null ? present[i].Registry.BuildSignature : null;
                if (signature == null) continue;
                for (int letter = 0; letter < signature.Length; letter++)
                    random = unchecked((random ^ signature[letter]) * 16777619u);
            }
            if (random == 0) random = 0x6D2B79F5u;
            nextWashroomSeconds = FirstWashroomMinimumSeconds + 30d * NextUnit();
            currentShot = IntroShot(0);
            lastSafePose = currentShot.Pose;
            lastSafeFocus = currentShot.Focus;
            lastSafeFov = currentShot.FieldOfView;
            RefreshDeck();
            if (bar.Crowd != null) bar.Crowd.PresenceChanged += MarkDeckDirty;
        }

        public void BeginPerformance()
        {
            if (disposed || performing) return;
            if (suspended) ReturnToStand();
            performing = true;
            suspended = false;
            elapsed = validationClock = 0f;
            // The first three established compositions remain the opening of a performance.
            introFinished = false;
            if (CurrentShotIndex != 0 || CurrentKind != BarKaraokeShotKind.Singer)
                TryCut(IntroShot(0), 0, null, BarKaraokeWashroomShotKind.None);
        }

        /// <summary>Stops performance ownership and bowl effects without moving a paused lens.</summary>
        public void Suspend()
        {
            performing = false;
            suspended = true;
            EndWashroomEffects();
        }

        public void ReturnToStand()
        {
            performing = false;
            suspended = false;
            EndWashroomEffects();
            elapsed = validationClock = 0f;
            if (disposed || director.IsFinished || director.IsReturning) return;
            if (TryCut(IntroShot(0), 0, null, BarKaraokeWashroomShotKind.None)) return;
            // A visiting body can temporarily occupy the usual lens. Prefer another
            // clear singer view; never force the camera through that body.
            for (int variant = 0; variant < SingerVariantCount; variant++)
                if (TryEntry(IntroCount + variant)) return;
            HasSafeShot = false;
        }

        /// <summary>Advances only active performance time; the bounded step also supports coarse manual clocks.</summary>
        public void Advance(float realSeconds)
        {
            if (disposed || !performing || !float.IsFinite(realSeconds) || realSeconds <= 0f ||
                !GameCursor.ApplicationFocused || GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
                director.IsFinished || director.IsReturning) return;
            float seconds = Mathf.Min(realSeconds, MaximumAdvanceSeconds);
            activeSeconds += seconds;
            if (deckDirty) RefreshDeck();
            if (!CurrentSubjectValid())
            {
                EndWashroomEffects();
                if (!SelectNext()) ReturnToStand();
                return;
            }
            // Presentation never catches up by flashing several missed cuts in one
            // frame. A coarse clock selects one new shot and gives it its full hold.
            elapsed = Mathf.Min(currentShot.Seconds, elapsed + seconds);
            validationClock += seconds;
            ApplyMovingShot(validationClock >= ValidationSeconds);
            if (validationClock >= ValidationSeconds) validationClock %= ValidationSeconds;
            if (elapsed + .0001f >= currentShot.Seconds && !SelectNext()) elapsed = currentShot.Seconds;
        }

        private bool SelectNext()
        {
            if (deckDirty) RefreshDeck();
            if (TryWashroomShot()) return true;
            if (!introFinished && CurrentShotIndex < IntroCount - 1)
            {
                int next = CurrentShotIndex + 1;
                if (TryCut(IntroShot(next), next, null, BarKaraokeWashroomShotKind.None)) return true;
            }
            introFinished = true;
            bool wantListener = CurrentKind == BarKaraokeShotKind.Singer;
            if (wantListener && TryDeck(listenerDeck, listenerCount, ref listenerCursor, true)) return true;
            if (TryDeck(singerDeck, SingerVariantCount, ref singerCursor, false)) return true;
            if (!wantListener && TryDeck(listenerDeck, listenerCount, ref listenerCursor, true)) return true;
            return TryCut(IntroShot(0), 0, null, BarKaraokeWashroomShotKind.None);
        }

        private bool TryDeck(int[] deck, int count, ref int cursor, bool listeners)
        {
            if (count == 0) return false;
            for (int attempt = 0; attempt < count; attempt++)
            {
                if (cursor >= count) { Shuffle(deck, count); cursor = 0; }
                int index = deck[cursor++];
                Entry entry = entries[index];
                if (index == CurrentShotIndex || listeners && entry.Root == lastListener && HasOtherListener(entry.Root)) continue;
                if (!TryEntry(index)) continue;
                if (listeners) lastListener = entry.Root;
                return true;
            }
            return false;
        }

        private bool TryEntry(int index)
        {
            if (index < IntroCount || index >= entryCount) return false;
            Entry entry = entries[index];
            if (!EntryValid(entry)) return false;
            Vector3 facing = entry.Patron?.Presentation != null && entry.Patron.Presentation.SeatAnchor != null
                ? entry.Patron.Presentation.SeatAnchor.forward : entry.Root.forward;
            float duration = 5f + 3f * NextUnit();
            for (int variant = 0; variant < ClearanceVariants; variant++)
            {
                BarKaraokeCameraShot shot = BuildHumanShot(entry.Root, entry.Head, facing, entry.Variant,
                    variant, entry.Kind, entry.Id, duration);
                if (TryCut(shot, index, entry.Head, BarKaraokeWashroomShotKind.None)) return true;
            }
            return false;
        }

        private bool TryWashroomShot()
        {
            if (activeSeconds < nextWashroomSeconds || bar.Crowd == null ||
                !bar.Crowd.TryGetKaraokeWashroomSubject(out BarPatron patron, out BarKaraokeWashroomShotKind actual) ||
                patron?.Registry == null || patron.Registry.Head == null) return false;
            bool pee = actual == BarKaraokeWashroomShotKind.PeeFace || actual == BarKaraokeWashroomShotKind.PeeBowl;
            if (pee && bar.Crowd.WashroomActionRemainingSeconds < 5.5f) return false;
            bool bowlFirst = NextUnit() < .35f;
            for (int option = 0; option < 2; option++)
            {
                bool bowl = option == 0 ? bowlFirst : !bowlFirst;
                BarKaraokeWashroomShotKind kind = pee
                    ? bowl ? BarKaraokeWashroomShotKind.PeeBowl : BarKaraokeWashroomShotKind.PeeFace
                    : bowl ? BarKaraokeWashroomShotKind.PoopBowl : BarKaraokeWashroomShotKind.PoopFace;
                int index = entryCount + (pee ? 0 : 1) + (bowl ? 2 : 0);
                if (bowl)
                {
                    if (!bar.Crowd.CanBeginWashroomBowlShot) continue;
                    if (!bar.Crowd.TryGetKaraokeBowlCameraPose(out Pose pose, out Vector3 focus)) continue;
                    var shot = new BarKaraokeCameraShot(pose, 65f, focus, pose, 65f, focus, 5.5f,
                        patron.Registry.transform, BarKaraokeShotKind.WashroomBowl, patron.Anchor.Id,
                        HomeToiletPlungeInteraction.SubmergedNearClip, .08f);
                    if (!TryCut(shot, index, null, kind)) continue;
                }
                else
                {
                    bool accepted = false;
                    for (int fallback = 0; fallback < ClearanceVariants && !accepted; fallback++)
                    {
                        BarKaraokeCameraShot shot = BuildHumanShot(patron.Registry.transform, patron.Registry.Head,
                            patron.Registry.transform.forward, pee ? 0 : 1, fallback,
                            BarKaraokeShotKind.WashroomFace, patron.Anchor.Id, 5.5f);
                        accepted = TryCut(shot, index, patron.Registry.Head, kind);
                    }
                    if (!accepted) continue;
                }
                WashroomCutCount++;
                nextWashroomSeconds = activeSeconds + MinimumWashroomCooldownSeconds +
                    (MaximumWashroomCooldownSeconds - MinimumWashroomCooldownSeconds) * NextUnit();
                return true;
            }
            return false;
        }

        private bool TryCut(BarKaraokeCameraShot shot, int index, Transform head,
            BarKaraokeWashroomShotKind washroomKind)
        {
            if (!ShotPathClear(shot)) return false;
            Collider coarseProxy = null;
            if (shot.Kind == BarKaraokeShotKind.WashroomBowl &&
                !TryMeasuredBowlProxy(shot.Pose, shot.Focus, out coarseProxy)) return false;
            float near = float.IsFinite(shot.NearClipPlane) ? shot.NearClipPlane : director.OriginalNearClipPlane;
            // Restore the shared underwater mix before a cut can reveal the hall.
            EndWashroomEffects();
            if (!director.TrySetAuthoredShot(shot.Pose, shot.FieldOfView, shot.Focus, true,
                    shot.Subject, near, shot.OpticalScale, ClearanceFor(shot), coarseProxy)) return false;
            if (CurrentShotIndex != index || CurrentSubject != shot.Subject || CurrentKind != shot.Kind) CutCount++;
            currentShot = shot;
            CurrentShotIndex = index;
            CurrentWashroomKind = washroomKind;
            focusAnchor = head;
            headAtCut = head != null ? head.position : Vector3.zero;
            elapsed = validationClock = 0f;
            lastSafePose = shot.Pose; lastSafeFocus = shot.Focus; lastSafeFov = shot.FieldOfView;
            moveBlocked = false;
            HasSafeShot = true;
            if (shot.Kind == BarKaraokeShotKind.WashroomBowl) bar.Crowd.SetKaraokeBowlShot(true);
            return true;
        }

        private void ApplyMovingShot(bool validate)
        {
            if (validate && !PoseClear(lastSafePose, lastSafeFocus, currentShot))
            {
                // Holding a path is appropriate only while the held lens is
                // still clear. A newly walking body instead motivates a cut.
                if (!SelectNext()) { HasSafeShot = false; EndWashroomEffects(); }
                return;
            }
            currentShot.Sample(Progress, out Pose pose, out float fov, out Vector3 focus);
            if (focusAnchor != null)
            {
                Vector3 headMotion = focusAnchor.position - headAtCut;
                focus += headMotion;
                if (CurrentKind == BarKaraokeShotKind.WashroomFace) pose.position += headMotion;
            }
            if (validate && !MovingSegmentClear(lastSafePose, lastSafeFocus, pose, focus, currentShot))
            {
                // Retain the last admitted pose. The next scheduled check may
                // recover; an obstructed move never skips across the blocker.
                return;
            }
            if (!validate && moveBlocked) return;
            float near = float.IsFinite(currentShot.NearClipPlane)
                ? currentShot.NearClipPlane : director.OriginalNearClipPlane;
            director.SetAuthoredShot(pose, fov, focus, false, near, currentShot.OpticalScale);
            lastSafePose = pose; lastSafeFocus = focus; lastSafeFov = fov;
            moveBlocked = false;
            HasSafeShot = true;
        }

        private bool ShotPathClear(BarKaraokeCameraShot shot)
        {
            if (director.IsFinished || director.IsReturning) return false;
            for (int sample = 0; sample < PathSamples; sample++)
            {
                shot.Sample(sample / (float)(PathSamples - 1), out Pose pose, out _, out Vector3 focus);
                if (!PoseClear(pose, focus, shot)) return false;
            }
            return true;
        }

        private bool MovingSegmentClear(Pose start, Vector3 startFocus, Pose end, Vector3 endFocus,
            BarKaraokeCameraShot shot)
        {
            for (int sample = 0; sample < 3; sample++)
            {
                float t = sample * .5f;
                var pose = new Pose(Vector3.Lerp(start.position, end.position, t),
                    Quaternion.Slerp(start.rotation, end.rotation, t));
                if (!PoseClear(pose, Vector3.Lerp(startFocus, endFocus, t), shot))
                { moveBlocked = true; return false; }
            }
            return true;
        }

        private bool PoseClear(Pose pose, Vector3 focus, BarKaraokeCameraShot shot)
        {
            Collider coarseProxy = null;
            if (shot.Kind == BarKaraokeShotKind.WashroomBowl &&
                !TryMeasuredBowlProxy(pose, focus, out coarseProxy)) return false;
            return director.IsAuthoredShotClear(pose, focus, shot.Subject, ClearanceFor(shot), coarseProxy) &&
                LensClearOfBodies(pose.position) &&
                (shot.Kind == BarKaraokeShotKind.Hall || shot.Kind == BarKaraokeShotKind.WashroomBowl ||
                    BodiesLeaveSightClear(pose.position, focus, shot.Subject));
        }

        private bool TryMeasuredBowlProxy(Pose pose, Vector3 focus, out Collider coarseProxy)
        {
            coarseProxy = null;
            if (bowlFrame == null || bowlPedestalProxy == null || bar.Washroom == null) return false;
            Vector3 position = bowlFrame.InverseTransformPoint(pose.position);
            // HomeToiletAction's measured cavity: inner floor .28, camera .315,
            // water .4373; generator rays prove a .035 m-radius open vertical
            // corridor. The fixed lens is admitted only at that imported anchor,
            // with its .015 m clearance sphere strictly inside the corridor.
            Vector3 anchor = new Vector3(4.05f, .315f, 1.40f);
            Vector3 offset = position - anchor;
            if (Mathf.Abs(offset.y) > .001f || offset.x * offset.x + offset.z * offset.z > .001f * .001f ||
                (pose.position - bar.Washroom.Plunge.SubmergedPosition).sqrMagnitude > .001f * .001f) return false;
            Vector3 direction = bowlFrame.InverseTransformVector(focus - pose.position);
            if (direction.y < .5f) return false;
            // Keep the sight centre inside the measured well through the top
            // of this exact coarse pedestal proxy (Y .48); no side-wall bypass.
            float travelToProxyTop = (.48f - position.y) / direction.y;
            Vector2 topOffset = new Vector2(offset.x + direction.x * travelToProxyTop,
                offset.z + direction.z * travelToProxyTop);
            if (topOffset.sqrMagnitude > .02f * .02f) return false;
            coarseProxy = bowlPedestalProxy;
            return true;
        }

        private static float ClearanceFor(BarKaraokeCameraShot shot) =>
            shot.Kind == BarKaraokeShotKind.WashroomBowl ? .015f : OrdinaryClearance;

        private bool LensClearOfBodies(Vector3 position)
        {
            // These model-only NPCs deliberately have no physics colliders.
            // Head/pelvis capsules keep a lens out of their actual animated torso.
            if (!BodyLensClear(position, heroPelvis, heroHead)) return false;
            if (bar.Bartender != null && !BodyLensClear(position,
                    bar.Bartender.Registry.Pelvis, bar.Bartender.Registry.Head)) return false;
            var patrons = bar.Crowd.Patrons;
            for (int i = 0; i < patrons.Count; i++)
            {
                CityPedestrianAssetRegistry actor = patrons[i].Registry;
                if (actor != null && actor.gameObject.activeInHierarchy &&
                    !BodyLensClear(position, actor.Pelvis, actor.Head)) return false;
            }
            CityPedestrianAssetRegistry transit = bar.Crowd.TransitPatron?.Registry;
            if (transit != null && transit.gameObject.activeInHierarchy &&
                !BodyLensClear(position, transit.Pelvis, transit.Head)) return false;
            return true;
        }

        private static bool BodyLensClear(Vector3 lens, Transform pelvis, Transform head)
        {
            if (pelvis == null || head == null) return true;
            Vector3 start = pelvis.position + Vector3.up * .12f, end = head.position + Vector3.up * .04f;
            return DistanceToSegmentSquared(lens, start, end) > .38f * .38f;
        }

        private bool BodiesLeaveSightClear(Vector3 camera, Vector3 focus, Transform subject)
        {
            if (subject != heroRoot && !BodySightClear(camera, focus, heroPelvis, heroHead)) return false;
            if (bar.Bartender != null && subject != bar.Bartender.transform &&
                !BodySightClear(camera, focus, bar.Bartender.Registry.Pelvis, bar.Bartender.Registry.Head)) return false;
            var patrons = bar.Crowd.Patrons;
            for (int i = 0; i < patrons.Count; i++)
            {
                CityPedestrianAssetRegistry actor = patrons[i].Registry;
                if (actor != null && actor.transform != subject && actor.gameObject.activeInHierarchy &&
                    !BodySightClear(camera, focus, actor.Pelvis, actor.Head)) return false;
            }
            CityPedestrianAssetRegistry transit = bar.Crowd.TransitPatron?.Registry;
            if (transit != null && transit.transform != subject && transit.gameObject.activeInHierarchy &&
                !BodySightClear(camera, focus, transit.Pelvis, transit.Head)) return false;
            return true;
        }

        private static bool BodySightClear(Vector3 camera, Vector3 focus, Transform pelvis, Transform head)
        {
            if (pelvis == null || head == null) return true;
            return DistanceToSegmentSquared(head.position, camera, focus) > .20f * .20f &&
                DistanceToSegmentSquared(Vector3.Lerp(pelvis.position, head.position, .45f), camera, focus) > .25f * .25f;
        }

        private static float DistanceToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float amount = segment.sqrMagnitude > .00001f
                ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude) : 0f;
            return (point - start - segment * amount).sqrMagnitude;
        }

        private bool CurrentSubjectValid()
        {
            if (CurrentKind == BarKaraokeShotKind.Hall || CurrentKind == BarKaraokeShotKind.Singer) return heroHead != null;
            if (CurrentSubject == null || !CurrentSubject.gameObject.activeInHierarchy) return false;
            if (CurrentKind == BarKaraokeShotKind.Bartender) return BartenderAvailable;
            if (CurrentKind == BarKaraokeShotKind.Patron)
            {
                var patrons = bar.Crowd.Patrons;
                for (int i = 0; i < patrons.Count; i++)
                    if (patrons[i].Registry != null && patrons[i].Registry.transform == CurrentSubject)
                        return !patrons[i].IsTemporarilyAway && patrons[i].Presentation != null && !patrons[i].Presentation.IsMoving;
                return false;
            }
            if (!bar.Crowd.TryGetKaraokeWashroomSubject(out BarPatron patron, out BarKaraokeWashroomShotKind actual) ||
                patron?.Registry == null || patron.Registry.transform != CurrentSubject) return false;
            bool wasPee = CurrentWashroomKind == BarKaraokeWashroomShotKind.PeeFace ||
                CurrentWashroomKind == BarKaraokeWashroomShotKind.PeeBowl;
            bool isPee = actual == BarKaraokeWashroomShotKind.PeeFace || actual == BarKaraokeWashroomShotKind.PeeBowl;
            return wasPee == isPee;
        }

        private bool BartenderAvailable => bar.Bartender != null &&
            bar.Bartender.CurrentClipKind == BarBartenderClipKind.Wipe && !bar.Bartender.IsAcknowledgingOrder;

        private bool EntryValid(Entry entry) => entry.Root != null && entry.Head != null &&
            entry.Root.gameObject.activeInHierarchy && (entry.Kind != BarKaraokeShotKind.Bartender || BartenderAvailable) &&
            (entry.Patron == null ||
                !entry.Patron.IsTemporarilyAway && entry.Patron.Presentation != null && !entry.Patron.Presentation.IsMoving);

        private void RefreshDeck()
        {
            Array.Clear(entries, 0, entries.Length);
            entryCount = IntroCount;
            for (int variant = 0; variant < SingerVariantCount; variant++)
            {
                singerDeck[variant] = entryCount;
                entries[entryCount++] = new Entry { Root = heroRoot, Head = heroHead,
                    Variant = variant, Kind = BarKaraokeShotKind.Singer, Id = "hero" };
            }
            listenerCount = 0;
            if (bar.Bartender != null)
                for (int variant = 0; variant < VariantsPerListener; variant++)
                {
                    listenerDeck[listenerCount++] = entryCount;
                    entries[entryCount++] = new Entry { Root = bar.Bartender.transform, Head = bar.Bartender.Registry.Head,
                        Variant = variant, Kind = BarKaraokeShotKind.Bartender, Id = "bartender" };
                }
            var patrons = bar.Crowd.Patrons;
            for (int i = 0; i < patrons.Count && entryCount + VariantsPerListener <= Capacity; i++)
            {
                BarPatron patron = patrons[i];
                if (patron.Registry == null || patron.IsTemporarilyAway) continue;
                for (int variant = 0; variant < VariantsPerListener; variant++)
                {
                    listenerDeck[listenerCount++] = entryCount;
                    entries[entryCount++] = new Entry { Root = patron.Registry.transform, Head = patron.Registry.Head,
                        Patron = patron, Variant = variant, Kind = BarKaraokeShotKind.Patron, Id = patron.Anchor.Id };
                }
            }
            Shuffle(singerDeck, SingerVariantCount);
            Shuffle(listenerDeck, listenerCount);
            singerCursor = listenerCursor = 0;
            deckDirty = false;
        }

        private bool HasOtherListener(Transform previous)
        {
            for (int i = 0; i < listenerCount; i++)
                if (entries[listenerDeck[i]].Root != previous && EntryValid(entries[listenerDeck[i]])) return true;
            return false;
        }

        private BarKaraokeCameraShot IntroShot(int index)
        {
            BarKaraokeCameraShot shot = plan.CameraShots[index];
            return new BarKaraokeCameraShot(shot.Pose, shot.FieldOfView, shot.Focus, shot.EndPose,
                shot.EndFieldOfView, shot.EndFocus, shot.Seconds, index < 2 ? heroRoot : null,
                index < 2 ? BarKaraokeShotKind.Singer : BarKaraokeShotKind.Hall, index < 2 ? "hero" : "hall");
        }

        private BarKaraokeCameraShot BuildHumanShot(Transform subject, Transform head, Vector3 bodyFacing,
            int variant, int clearanceVariant, BarKaraokeShotKind kind, string id, float seconds)
        {
            Vector3 up = bar.Room.up;
            Vector3 front = Vector3.ProjectOnPlane(bodyFacing, up).normalized;
            if (front.sqrMagnitude < .01f) front = bar.Room.forward;
            if (kind == BarKaraokeShotKind.Patron || kind == BarKaraokeShotKind.Bartender)
            {
                Vector3 singer = Vector3.ProjectOnPlane(heroHead.position - head.position, up);
                // The audience's combined torso/head pose actually faces the singer,
                // including counter guests looking over a shoulder. Stage from that gaze.
                if (singer.sqrMagnitude > .01f) front = singer.normalized;
            }
            bool singerShot = kind == BarKaraokeShotKind.Singer;
            float sign = (variant & 1) == 0 ? -1f : 1f;
            float angle = singerShot ? variant switch { 0 => -28f, 1 => 28f, 2 => -8f, 3 => 48f, 4 => -42f, _ => 12f } : sign * 24f;
            float distance = singerShot ? variant switch { 2 => 1.45f, 3 => .88f, 4 => 1.25f, 5 => 1.15f, _ => 1.08f } : 1.05f;
            float height = singerShot ? variant switch { 2 => .22f, 4 => .30f, 5 => -.18f, _ => .06f } : .06f;
            switch (clearanceVariant)
            {
                case 1: angle = -angle; break;
                case 2: angle = sign * 55f; distance = .92f; height += .10f; break;
                case 3: angle = -sign * 55f; distance = .92f; height += .10f; break;
                case 4: angle = -sign * 75f; distance = .82f; height += .14f; break;
                case 5: angle = 0f; distance = 1.20f; height += .24f; break;
            }
            Vector3 focus = head.position - up * (singerShot && variant == 2 ? .27f : .10f);
            Vector3 start = head.position + Quaternion.AngleAxis(angle, up) * front * distance + up * height;
            Vector3 end = head.position + Quaternion.AngleAxis(angle + sign * 7f, up) * front * (distance - .10f) + up * (height + .035f);
            Vector3 endFocus = focus + up * .025f;
            float fov = singerShot ? variant switch { 2 => 53f, 3 => 36f, 5 => 48f, _ => 43f } : variant == 0 ? 40f : 43f;
            float endFov = Mathf.Max(32f, fov - 4f);
            return new BarKaraokeCameraShot(new Pose(start,
                    Quaternion.LookRotation(focus - start, up) * Quaternion.Euler(0f, 0f, sign * 1.5f)), fov, focus,
                new Pose(end, Quaternion.LookRotation(endFocus - end, up) * Quaternion.Euler(0f, 0f, -sign)),
                endFov, endFocus, seconds, subject, kind, id);
        }

        private void EndWashroomEffects()
        {
            if (bar.Crowd != null && CurrentKind == BarKaraokeShotKind.WashroomBowl)
                bar.Crowd.SetKaraokeBowlShot(false);
            CurrentWashroomKind = BarKaraokeWashroomShotKind.None;
        }

        private void MarkDeckDirty() => deckDirty = true;

        private float NextUnit()
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return (random & 0x00FFFFFFu) / 16777216f;
        }

        private void Shuffle(int[] deck, int count)
        {
            for (int i = count - 1; i > 0; i--)
            {
                int other = Mathf.Min(i, (int)(NextUnit() * (i + 1)));
                int value = deck[i]; deck[i] = deck[other]; deck[other] = value;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            performing = false;
            EndWashroomEffects();
            if (!director.IsFinished && !director.IsReturning)
                director.SetAuthoredShot(lastSafePose, lastSafeFov, lastSafeFocus, false,
                    director.OriginalNearClipPlane, 1f);
            if (bar.Crowd != null) bar.Crowd.PresenceChanged -= MarkDeckDirty;
            disposed = true;
            Array.Clear(entries, 0, entries.Length);
            focusAnchor = lastListener = null;
        }
    }
}
