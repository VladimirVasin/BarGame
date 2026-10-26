using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The existing toilet bank on one visitor's existing graph, with its own planted feet.</summary>
    internal sealed class BarNpcToiletPresentation
    {
        private static AnimationClip[] clips;
        private static Manifest manifest;
        private readonly BarPatron patron;
        private readonly ToiletFixtureAnchors fixture;
        private readonly Transform actor, pelvis, head, spine, chest, upper, forearm, hand, grip;
        private readonly Transform[] thighs = new Transform[2], shins = new Transform[2], feet = new Transform[2];
        private readonly Vector3[] footPositions = new Vector3[2];
        private readonly Quaternion[] footRotations = new Quaternion[2];
        private readonly Vector3[] seatedFootPositions = new Vector3[2];
        private readonly Quaternion[] seatedFootRotations = new Quaternion[2];
        private readonly AnatomicalHandFrame anatomy;
        private readonly HomeToiletSeatedAppearance seatedAppearance;
        private readonly BarNpcToiletHands seatedHands;
        private readonly Transform flushHandle;
        private readonly Vector3 flushTopLocal, headForwardLocal;
        private Vector3 flushRest;
        private bool ownsFlushHandle;
        private Mesh clearanceMesh;
        private readonly Vector3 modelRestPosition;
        private readonly Quaternion modelRestRotation;
        private Vector3 neutralPelvis;
        private bool captured;
        public Vector3 PeeOutlet
        {
            get
            {
                Vector3 waist = pelvis.position - actor.up * .015f + actor.forward * .24f;
                // Keep the clothed waist above the real annular seat, also for
                // the shorter members of the shared visitor population.
                waist.y = Mathf.Max(waist.y, fixture.Seat.position.y + .20f);
                return waist;
            }
        }
        public float FootContactError { get; private set; }
        public float LidContactError { get; private set; }
        public bool IsSeatedAppearanceActive => seatedAppearance.IsActive;
        public float TrousersDown => seatedAppearance.TrousersDown;
        public Transform BowelOutlet => seatedAppearance.Outlet;
        public Vector3 LeftSeatedPalm => seatedHands.LeftPalm;
        public Vector3 RightSeatedPalm => seatedHands.RightPalm;
        public float SeatedHandContactError => seatedHands.ContactError;
        public bool PrepareSeatedAppearance() => seatedAppearance.Prepare();
        public bool BeginSeatedAppearance() => seatedAppearance.Begin();
        public void EndSeatedAppearance() => seatedAppearance.End();
        public void PresentClothing(float amount) { if (seatedAppearance.IsActive) seatedAppearance.Present(amount); }
        public float OuterwearLiftWeight => seatedAppearance.OuterwearLiftWeight;
        public HomeToiletActorPhase Phase { get; private set; } = HomeToiletActorPhase.None;
        public float NormalizedTime { get; private set; }
        public bool IsFlushContact => Phase == HomeToiletActorPhase.Flush &&
            NormalizedTime * HomeToiletActorPresentation.FlushDurationSeconds >= HomeToiletActorPresentation.FlushContactStartSeconds &&
            NormalizedTime * HomeToiletActorPresentation.FlushDurationSeconds <= HomeToiletActorPresentation.FlushContactEndSeconds;
        public float FlushContactError => IsFlushContact && flushHandle != null
            ? Vector3.Distance(grip.position, flushHandle.TransformPoint(flushTopLocal)) : 0f;
        public float GazeAlignment => Vector3.Dot(head.TransformDirection(headForwardLocal).normalized,
            (fixture.BowlLookTarget - head.position).normalized);
        public string SeatBlocker { get; private set; } = string.Empty;
        public bool SeatClear
        {
            get
            {
                SeatBlocker = string.Empty;
                Bounds column = new Bounds(fixture.Frame.TransformPoint(new Vector3(4.05f, 1.15f, 1.40f)),
                    WorldSize(new Vector3(.34f, 1.15f, .30f)));
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        renderer.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    Bounds bounds = PresentedBounds(renderer);
                    if (!bounds.Intersects(column)) continue;
                    SeatBlocker = renderer.name + " bounds=" + bounds.ToString("F4") + " phase=" + Phase;
                    return false;
                }
                return true;
            }
        }

        public static bool BankAvailable()
        {
            if (manifest != null && clips != null) return true;
            TextAsset source = Resources.Load<TextAsset>(HomeToiletActorPresentation.ResourcePath);
            Manifest loaded = source != null ? JsonUtility.FromJson<Manifest>(source.text) : null;
            AnimationClip[] bank = Resources.LoadAll<AnimationClip>(HomeToiletActorPresentation.ResourcePath);
            if (loaded?.clips == null || loaded.clips.Length != HomeToiletActorPresentation.ClipNames.Length) return false;
            var selected = new AnimationClip[loaded.clips.Length];
            var selectedData = new ClipData[loaded.clips.Length];
            for (int index = 0; index < selected.Length; index++)
            {
                string name = HomeToiletActorPresentation.ClipNames[index];
                foreach (AnimationClip candidate in bank)
                    if (candidate != null && candidate.name == name) { selected[index] = candidate; break; }
                foreach (ClipData data in loaded.clips)
                    if (data != null && data.name == name) { selectedData[index] = data; break; }
                if (selected[index] == null || selected[index].events.Length != 0 ||
                    Mathf.Abs(selected[index].length - HomeToiletActorPresentation.Duration((HomeToiletActorPhase)index)) > .003f ||
                    selectedData[index]?.samples == null || selectedData[index].samples.Length < 2)
                    return false;
                foreach (PoseSample sample in selectedData[index].samples)
                    if (sample?.source_pelvis == null || sample.source_pelvis.Length != 3) return false;
            }
            manifest = new Manifest { clips = selectedData }; clips = selected;
            return true;
        }

        public BarNpcToiletPresentation(BarPatron guest, ToiletFixtureAnchors toilet)
        {
            patron = guest;
            fixture = toilet;
            actor = guest.Registry.transform;
            pelvis = guest.Registry.PelvisAnchor;
            head = guest.Registry.HeadAnchor;
            Transform model = guest.Registry.ModelRoot;
            modelRestPosition = model.localPosition;
            modelRestRotation = model.localRotation;
            spine = CityPedestrianHandProps.FindSocket(model, "spine");
            chest = CityPedestrianHandProps.FindSocket(model, "chest");
            upper = CityPedestrianHandProps.FindSocket(model, "upper_arm.R");
            forearm = CityPedestrianHandProps.FindSocket(model, "forearm.R");
            hand = CityPedestrianHandProps.FindSocket(model, "hand.R");
            grip = CityPedestrianHandProps.FindSocket(model, "SOCKET_Grip.R");
            headForwardLocal = head.InverseTransformDirection(actor.forward);
            flushHandle = fixture.Flush;
            if (flushHandle != null && flushHandle.TryGetComponent(out MeshFilter flushMesh) && flushMesh.sharedMesh != null)
            {
                Bounds bounds = flushMesh.sharedMesh.bounds;
                flushTopLocal = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            }
            NpcHandPose fingers = guest.Registry.GetComponentInChildren<NpcHandPose>();
            if (fingers == null) throw new InvalidOperationException("A visitor's toilet action needs its registered hand anatomy.");
            anatomy = fingers.GetAnatomicalFrame(false);
            seatedAppearance = guest.Registry.GetComponent<HomeToiletSeatedAppearance>();
            if (seatedAppearance == null) seatedAppearance = guest.Registry.gameObject.AddComponent<HomeToiletSeatedAppearance>();
            seatedAppearance.Initialize(guest.Registry, actor);
            seatedHands = new BarNpcToiletHands(guest);
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? ".L" : ".R";
                thighs[side] = CityPedestrianHandProps.FindSocket(model, "thigh" + suffix);
                shins[side] = CityPedestrianHandProps.FindSocket(model, "shin" + suffix);
                feet[side] = side == 0 ? guest.Registry.LeftFootAnchor : guest.Registry.RightFootAnchor;
            }
            if (pelvis == null || head == null || spine == null || chest == null || upper == null || forearm == null || hand == null ||
                grip == null || thighs[0] == null || thighs[1] == null || shins[0] == null || shins[1] == null ||
                feet[0] == null || feet[1] == null)
                throw new InvalidOperationException("A visitor's toilet action needs the registered production human rig.");
        }

        public void CaptureNeutral(bool keepSeatedAppearance = false)
        {
            Clear(keepSeatedAppearance);
            neutralPelvis = actor.InverseTransformPoint(pelvis.position);
            for (int side = 0; side < feet.Length; side++)
            { footPositions[side] = feet[side].position; footRotations[side] = feet[side].rotation; }
            // Derive the seated contacts from this actual visitor and the same
            // authored bank, rather than forcing feet to remain a metre ahead
            // of the seat. Sit/rise transfer them with two short planted steps.
            patron.Presentation.ApplyAuthoredAction(clips[(int)HomeToiletActorPhase.Seated], 0f, 1f);
            patron.Registry.ModelRoot.position += fixture.SeatPelvis - pelvis.position;
            for (int side = 0; side < feet.Length; side++)
            {
                seatedFootPositions[side] = feet[side].position;
                seatedFootPositions[side].y = footPositions[side].y;
                seatedFootRotations[side] = feet[side].rotation;
            }
            Clear(keepSeatedAppearance);
            captured = true;
        }

        public void Sample(HomeToiletActorPhase phase, float progress)
        {
            if (!captured || !BankAvailable()) return;
            int index = (int)phase;
            float normalized = Mathf.Clamp01(progress);
            Phase = phase; NormalizedTime = normalized;
            if (phase == HomeToiletActorPhase.Flush && flushHandle != null)
            {
                if (!ownsFlushHandle) { flushRest = flushHandle.localPosition; ownsFlushHandle = true; }
                flushHandle.localPosition = flushRest + Vector3.down * (HomeToiletActorPresentation.FlushPressDepth *
                    HomeToiletActorPresentation.FlushPressProgress(normalized));
            }
            else RestoreFlushHandle();
            patron.Presentation.ApplyAuthoredAction(clips[index], normalized, 1f);
            Vector3 source = SamplePelvis(manifest.clips[index], normalized);
            Vector3 local = new Vector3(-source.x, source.z, -source.y);
            float seated = phase == HomeToiletActorPhase.Sit ? Mathf.SmoothStep(0f, 1f, normalized)
                : phase == HomeToiletActorPhase.Rise ? 1f - Mathf.SmoothStep(0f, 1f, normalized)
                : phase == HomeToiletActorPhase.Seated ? 1f : 0f;
            Vector3 target = actor.TransformPoint(local + (neutralPelvis - new Vector3(0f, .835f, -.008f)) * (1f - seated));
            Vector3 authoredSeat = actor.TransformPoint(new Vector3(0f, .665f, -.730f));
            target += (fixture.SeatPelvis - authoredSeat) * seated;
            patron.Registry.ModelRoot.position += target - pelvis.position;
            PinFeet(seated);
            if (phase == HomeToiletActorPhase.Sit || phase == HomeToiletActorPhase.Seated || phase == HomeToiletActorPhase.Rise)
                seatedHands.Apply(normalized, seated);
            if (phase == HomeToiletActorPhase.Prepare || phase == HomeToiletActorPhase.Sit ||
                phase == HomeToiletActorPhase.Seated || phase == HomeToiletActorPhase.Rise || phase == HomeToiletActorPhase.Dress ||
                phase == HomeToiletActorPhase.Inspect || phase == HomeToiletActorPhase.Flush)
            {
                if (!seatedAppearance.Begin()) return;
                float trousers = phase == HomeToiletActorPhase.Prepare ? HomeToiletActorPresentation.ClothingProgress(normalized)
                    : phase == HomeToiletActorPhase.Dress ? HomeToiletActorPresentation.ClothingProgress(1f - normalized) : 1f;
                seatedAppearance.Present(trousers);
            }
            if (phase == HomeToiletActorPhase.Inspect)
            {
                float look = Mathf.SmoothStep(0f, 1f, normalized / .3f);
                Quaternion correction = Quaternion.FromToRotation(head.TransformDirection(headForwardLocal),
                    fixture.BowlLookTarget - head.position);
                head.rotation = Quaternion.Slerp(head.rotation, correction * head.rotation, look);
            }
            if (phase == HomeToiletActorPhase.Flush && flushHandle != null) ApplyFlushContact(normalized);
            LidContactError = 0f;
            if (phase == HomeToiletActorPhase.OpenLid || phase == HomeToiletActorPhase.CloseLid)
            {
                float sample = phase == HomeToiletActorPhase.OpenLid ? normalized : 1f - normalized;
                float reach = Mathf.SmoothStep(0f, 1f, sample / HomeToiletActorPresentation.ReachEnd) *
                    (1f - Mathf.SmoothStep(0f, 1f, (sample - HomeToiletActorPresentation.ReleaseStart) /
                        (1f - HomeToiletActorPresentation.ReleaseStart)));
                Vector3 socketInHand = hand.InverseTransformPoint(grip.position);
                Quaternion rotation = hand.rotation;
                LimbTwoBoneIk.Solve(upper, forearm, hand, fixture.Lid.Grip.position - rotation * socketInHand,
                    rotation, upper.position + actor.right * .35f - actor.up * .25f, reach, .995f, true);
                if (reach > .95f) LidContactError = Vector3.Distance(grip.position, fixture.Lid.Grip.position);
            }
        }

        public void SamplePee(float amount)
        {
            patron.Presentation.ClearAuthoredAction();
            patron.Presentation.Advance(0f, false);
            float weight = Mathf.Clamp01(amount);
            spine.rotation = Quaternion.AngleAxis(4f * weight, actor.right) * spine.rotation;
            chest.rotation = Quaternion.AngleAxis(3f * weight, actor.right) * chest.rotation;
            head.rotation = Quaternion.AngleAxis(8f * weight, actor.right) * head.rotation;
            Vector3 fingers = (-actor.up - actor.right * 1.25f).normalized;
            Quaternion rotation = Quaternion.LookRotation(fingers, actor.forward) *
                Quaternion.Inverse(Quaternion.LookRotation(anatomy.FingerDirectionInHand, anatomy.PalmNormalInHand));
            Vector3 palm = PeeOutlet - actor.forward * .09f - actor.up * .02f;
            LimbTwoBoneIk.Solve(upper, forearm, hand, palm - rotation * anatomy.PalmCentreInHand,
                rotation, upper.position + actor.right * .38f - actor.forward * .04f - actor.up * .32f,
                weight, .995f, true);
            PinFeet();
        }

        private void PinFeet(float seated = 0f)
        {
            FootContactError = 0f;
            if (!captured) return;
            for (int side = 0; side < feet.Length; side++)
            {
                float step = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(side == 0 ? .02f : .20f,
                    side == 0 ? .64f : .96f, seated));
                Vector3 target = Vector3.Lerp(footPositions[side], seatedFootPositions[side], step) +
                    actor.up * (.035f * Mathf.Sin(step * Mathf.PI));
                Quaternion rotation = Quaternion.Slerp(footRotations[side], seatedFootRotations[side], step);
                LimbTwoBoneIk.Solve(thighs[side], shins[side], feet[side], target, rotation,
                    thighs[side].position + actor.forward * .7f - actor.up * .2f + actor.right * (side == 0 ? -.08f : .08f),
                    1f, .995f, true);
                FootContactError = Mathf.Max(FootContactError, Vector3.Distance(feet[side].position, target));
            }
        }

        private void ApplyFlushContact(float normalized)
        {
            float seconds = normalized * HomeToiletActorPresentation.FlushDurationSeconds;
            float reach = Mathf.SmoothStep(0f, 1f, seconds / HomeToiletActorPresentation.FlushContactStartSeconds) *
                (1f - Mathf.SmoothStep(0f, 1f, (seconds - HomeToiletActorPresentation.FlushContactEndSeconds) / .55f));
            Vector3 socketInHand = hand.InverseTransformPoint(grip.position);
            Quaternion rotation = hand.rotation;
            LimbTwoBoneIk.Solve(upper, forearm, hand, flushHandle.TransformPoint(flushTopLocal) - rotation * socketInHand,
                rotation, upper.position + actor.right * .30f + actor.forward * .10f - actor.up * .10f,
                reach, .995f, true);
        }

        private void RestoreFlushHandle()
        {
            if (ownsFlushHandle && flushHandle != null) flushHandle.localPosition = flushRest;
            ownsFlushHandle = false;
        }

        public void Clear(bool keepSeatedAppearance = false)
        {
            RestoreFlushHandle();
            if (!keepSeatedAppearance) seatedAppearance.End();
            seatedHands.Clear();
            patron.Presentation.ClearAuthoredAction();
            patron.Registry.ModelRoot.SetLocalPositionAndRotation(modelRestPosition, modelRestRotation);
            patron.Presentation.Advance(0f, false);
            if (keepSeatedAppearance && seatedAppearance.IsActive) seatedAppearance.Present(seatedAppearance.TrousersDown);
            captured = false;
            Phase = HomeToiletActorPhase.None; NormalizedTime = 0f;
        }

        public void Dispose()
        {
            Clear();
            seatedAppearance.ReleasePrepared();
            if (clearanceMesh != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(clearanceMesh);
                else UnityEngine.Object.DestroyImmediate(clearanceMesh);
                clearanceMesh = null;
            }
        }

        private Vector3 WorldSize(Vector3 size)
        {
            Vector3 x = fixture.Frame.TransformVector(new Vector3(size.x, 0f, 0f));
            Vector3 y = fixture.Frame.TransformVector(new Vector3(0f, size.y, 0f));
            Vector3 z = fixture.Frame.TransformVector(new Vector3(0f, 0f, size.z));
            return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        private Bounds PresentedBounds(Renderer renderer)
        {
            if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null) return renderer.bounds;
            if (clearanceMesh == null) clearanceMesh = new Mesh { name = "NPC toilet actor clearance", hideFlags = HideFlags.HideAndDontSave };
            clearanceMesh.Clear(false);
            skin.BakeMesh(clearanceMesh, true);
            clearanceMesh.RecalculateBounds();
            Bounds local = clearanceMesh.bounds;
            Vector3 x = skin.transform.TransformVector(new Vector3(local.extents.x, 0f, 0f));
            Vector3 y = skin.transform.TransformVector(new Vector3(0f, local.extents.y, 0f));
            Vector3 z = skin.transform.TransformVector(new Vector3(0f, 0f, local.extents.z));
            Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(skin.transform.TransformPoint(local.center), extents * 2f);
        }

        private static Vector3 SamplePelvis(ClipData data, float normalized)
        {
            float cursor = normalized * (data.samples.Length - 1);
            int first = Mathf.Min(Mathf.FloorToInt(cursor), data.samples.Length - 1);
            int second = Mathf.Min(first + 1, data.samples.Length - 1);
            float[] a = data.samples[first].source_pelvis, b = data.samples[second].source_pelvis;
            return Vector3.Lerp(new Vector3(a[0], a[1], a[2]), new Vector3(b[0], b[1], b[2]), cursor - first);
        }
        [Serializable] private sealed class Manifest { public ClipData[] clips; }
        [Serializable] private sealed class ClipData { public string name; public PoseSample[] samples; }
        [Serializable] private sealed class PoseSample { public float[] source_pelvis; }
    }
}
