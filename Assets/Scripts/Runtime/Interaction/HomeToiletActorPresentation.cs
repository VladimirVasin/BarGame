using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeToiletActorPhase { None = -1, OpenLid, Prepare, Sit, Seated, Rise, Dress, CloseLid, Inspect, Flush }

    /// <summary>Authored toilet clips on the real hero, sampled by the bathroom's sole owner.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletActorPresentation : MonoBehaviour
    {
        public const string ResourcePath = "Player/HomeToiletSeatedActions";
        public const float ReachEnd = .25f;
        public const float ReleaseStart = .75f;
        public const float FlushCueSeconds = 1.5f;
        public const float FlushContactStartSeconds = .8f;
        public const float FlushContactEndSeconds = 1.65f;
        public const float FlushDurationSeconds = 2.5f;
        public const float FlushPressDepth = .03f;
        public const float SeatPelvisHeight = HomeToiletSeatedAppearance.SeatPelvisHeight;
        public static readonly Vector3 SeatedPelvisLocal = new Vector3(4.05f, SeatPelvisHeight, 1.40f);
        public static readonly string[] ClipNames = { "ToiletOpenLid", "ToiletPrepare", "ToiletSit",
            "ToiletSeated", "ToiletRise", "ToiletDress", "ToiletCloseLid", "ToiletInspect", "ToiletFlush" };

        private HomeInteriorRoot home;
        private Player3DCharacterPresentation visual;
        private Player3DAssetRegistry registry;
        private HomeToiletLid lid;
        private Manifest manifest;
        private ClipData clipData;
        private string ownedClip;
        private bool previousHandoff;
        private Mesh clearanceMesh;
        private Transform flushHandle;
        private Transform leftEye, rightEye;
        private Vector3 flushRest, flushTopLocal, headForwardLocal;
        private bool ownsFlushHandle;
        public bool IsInitialized { get; private set; }
        public bool IsActive { get; private set; }
        public HomeToiletActorPhase Phase { get; private set; } = HomeToiletActorPhase.None;
        public float NormalizedTime { get; private set; }
        public Vector3 SeatPelvis => home.transform.TransformPoint(SeatedPelvisLocal);
        public bool IsLidContact => IsActive && (Phase == HomeToiletActorPhase.OpenLid || Phase == HomeToiletActorPhase.CloseLid) &&
            NormalizedTime >= ReachEnd && NormalizedTime <= ReleaseStart;
        public float ContactError => IsLidContact && lid.Grip != null
            ? Vector3.Distance(registry.Anchors.RightGrip.position, lid.Grip.position) : 0f;
        public float SeatPelvisError => registry != null ? Vector3.Distance(registry.Anchors.Pelvis.position, SeatPelvis) : 0f;
        public bool IsFlushContact => IsActive && Phase == HomeToiletActorPhase.Flush &&
            NormalizedTime * FlushDurationSeconds >= FlushContactStartSeconds &&
            NormalizedTime * FlushDurationSeconds <= FlushContactEndSeconds;
        public float FlushContactError => IsFlushContact ? Vector3.Distance(registry.Anchors.RightGrip.position,
            flushHandle.TransformPoint(flushTopLocal)) : 0f;
        public Vector3 BowlLookTarget => home.transform.TransformPoint(new Vector3(4.05f, .4373f, 1.40f));
        public float GazeAlignment => registry != null ? Vector3.Dot(
            registry.Anchors.Head.TransformDirection(headForwardLocal).normalized,
            (BowlLookTarget - (leftEye != null && rightEye != null ? (leftEye.position + rightEye.position) * .5f
                : registry.Anchors.Head.position)).normalized) : 0f;
        public string SeatBlocker { get; private set; } = string.Empty;
        public float ClothingAmount => Phase == HomeToiletActorPhase.Prepare ? ClothingProgress(NormalizedTime)
            : Phase == HomeToiletActorPhase.Dress ? ClothingProgress(1f - NormalizedTime)
            : Phase == HomeToiletActorPhase.Sit || Phase == HomeToiletActorPhase.Seated || Phase == HomeToiletActorPhase.Rise ||
                Phase == HomeToiletActorPhase.Inspect || Phase == HomeToiletActorPhase.Flush ? 1f : 0f;

        /// <summary>The actual rendered body has left the camera's vertical exit column.</summary>
        public bool SeatClear
        {
            get
            {
                SeatBlocker = string.Empty;
                if (home == null) return false;
                Bounds column = new Bounds(home.transform.TransformPoint(new Vector3(4.05f, 1.15f, 1.40f)),
                    new Vector3(.34f, 1.15f, .30f));
                foreach (Renderer renderer in home.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        renderer.name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    Bounds bounds = PresentedBounds(renderer);
                    if (!bounds.Intersects(column)) continue;
                    SeatBlocker = renderer.name + " bounds=" + bounds.ToString("F4") +
                        " scale=" + renderer.transform.lossyScale.ToString("F4") +
                        " phase=" + Phase + "@" + NormalizedTime.ToString("F4");
                    return false;
                }
                return true;
            }
        }

        public bool Initialize(HomeInteriorRoot homeRoot)
        {
            End();
            home = homeRoot != null ? homeRoot : throw new ArgumentNullException(nameof(homeRoot));
            visual = home.Player.Visual as Player3DCharacterPresentation;
            registry = visual != null ? visual.Registry : null;
            lid = home.Room.GetComponentInChildren<HomeToiletLid>(true);
            flushHandle = home.Room.Find("Home Bathroom Toilet Flush");
            if (flushHandle != null)
            {
                Bounds bounds = flushHandle.GetComponent<MeshFilter>().sharedMesh.bounds;
                flushTopLocal = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            }
            if (registry != null && registry.Anchors.Head != null)
            {
                headForwardLocal = registry.Anchors.Head.InverseTransformDirection(home.Player.GameObject.transform.forward);
                foreach (Transform bone in registry.Anchors.Head.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "face.eye.L") leftEye = bone;
                    else if (bone.name == "face.eye.R") rightEye = bone;
                }
            }
            TextAsset text = Resources.Load<TextAsset>(ResourcePath);
            manifest = text != null ? JsonUtility.FromJson<Manifest>(text.text) : null;
            IsInitialized = registry != null && lid != null && manifest != null;
            return Prepare();
        }

        public bool Prepare(HomeInteriorRoot homeRoot) => Initialize(homeRoot);
        public bool Prepare() => IsInitialized && lid.Grip != null && flushHandle != null && registry.Anchors.Pelvis != null &&
            registry.Anchors.RightGrip != null && manifest.clips != null &&
            manifest.clips.Length == ClipNames.Length && TryAttachClips(registry);

        public bool Begin()
        {
            if (IsActive) return true;
            if (!Prepare()) return false;
            previousHandoff = visual.InteractionHandoffLocked;
            visual.SetInteractionHandoffLocked(true);
            IsActive = true;
            Phase = HomeToiletActorPhase.None;
            return true;
        }

        public void ApplyPhase(HomeToiletActorPhase phase, float normalized)
        {
            if (!IsActive || phase == HomeToiletActorPhase.None) return;
            if (Phase != phase)
            {
                ReleaseClip();
                string clip = ClipNames[(int)phase];
                if (!visual.TryBeginClip(clip)) throw new InvalidOperationException("Missing toilet action: " + clip);
                ownedClip = clip;
                clipData = manifest.clips.First(value => value.name == clip);
                Phase = phase;
            }
            NormalizedTime = Mathf.Clamp01(normalized);
            if (phase == HomeToiletActorPhase.OpenLid || phase == HomeToiletActorPhase.CloseLid)
            {
                if (!lid.TryAcquireAngleControl(this)) throw new InvalidOperationException("The toilet lid already has a hand owner.");
                float clipTime = phase == HomeToiletActorPhase.OpenLid ? NormalizedTime : 1f - NormalizedTime;
                lid.SetOwnedAngle(this, HomeToiletLid.OpenDegrees * LidProgress(clipTime));
            }
            if (phase == HomeToiletActorPhase.Flush)
            {
                if (!ownsFlushHandle) { flushRest = flushHandle.localPosition; ownsFlushHandle = true; }
                flushHandle.localPosition = flushRest + Vector3.down * (FlushPressDepth * FlushPressProgress(NormalizedTime));
            }
            visual.SampleActiveClip(NormalizedTime);
            Vector3 source = SamplePelvis(clipData, NormalizedTime);
            Vector3 target = home.Player.GameObject.transform.TransformPoint(new Vector3(-source.x, source.z, -source.y));
            float seated = phase == HomeToiletActorPhase.Sit ? Smooth(NormalizedTime)
                : phase == HomeToiletActorPhase.Rise ? 1f - Smooth(NormalizedTime)
                : phase == HomeToiletActorPhase.Seated ? 1f : 0f;
            if (seated > 0f)
            {
                Vector3 authoredSeat = home.Player.GameObject.transform.TransformPoint(new Vector3(0f, .665f, -.730f));
                target += (SeatPelvis - authoredSeat) * seated;
            }
            visual.AlignActiveClipAnchor(target);
        }

        public void Present(HomeToiletActorPhase phase, float normalized) => ApplyPhase(phase, normalized);

        /// <summary>Allow a normal grounded turn between neutral clips, retaining the lid angle.</summary>
        public void PausePose() => End();

        public void End()
        {
            ReleaseClip();
            if (IsActive && visual != null) visual.SetInteractionHandoffLocked(previousHandoff);
            IsActive = false;
            Phase = HomeToiletActorPhase.None;
            NormalizedTime = 0f;
        }

        private void ReleaseClip()
        {
            if (visual != null && ownedClip != null && visual.ActiveClipName == ownedClip) visual.EndClip();
            ownedClip = null;
            clipData = null;
            lid?.ReleaseAngleControl(this);
            if (ownsFlushHandle && flushHandle != null) flushHandle.localPosition = flushRest;
            ownsFlushHandle = false;
        }

        public static float Duration(HomeToiletActorPhase phase) => phase == HomeToiletActorPhase.Seated ? 3f
            : phase == HomeToiletActorPhase.Flush ? FlushDurationSeconds : 2f;
        public static float FlushPressProgress(float normalized)
        {
            float seconds = Mathf.Clamp01(normalized) * FlushDurationSeconds;
            return seconds <= FlushCueSeconds ? Smooth((seconds - 1.25f) / .25f)
                : 1f - Smooth((seconds - FlushCueSeconds) / .15f);
        }
        public static float LidProgress(float normalized) => Smooth((normalized - ReachEnd) / (ReleaseStart - ReachEnd));
        public static float ClothingProgress(float normalized) => Smooth((normalized - .2f) / .6f);
        private static float Smooth(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));

        private static Vector3 SamplePelvis(ClipData clip, float normalized)
        {
            float cursor = normalized * (clip.samples.Length - 1);
            int first = Mathf.Min(Mathf.FloorToInt(cursor), clip.samples.Length - 1);
            int second = Mathf.Min(first + 1, clip.samples.Length - 1);
            float[] a = clip.samples[first].source_pelvis;
            float[] b = clip.samples[second].source_pelvis;
            return Vector3.Lerp(new Vector3(a[0], a[1], a[2]), new Vector3(b[0], b[1], b[2]), cursor - first);
        }

        private Bounds PresentedBounds(Renderer renderer)
        {
            if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null) return renderer.bounds;
            // Imported culling bounds deliberately include other action poses.
            // The exit gate needs the currently deformed body, including clothes.
            if (clearanceMesh == null) clearanceMesh = new Mesh { name = "Toilet actor clearance", hideFlags = HideFlags.HideAndDontSave };
            clearanceMesh.Clear(false);
            // Match the production foot/contact probes: FBX renderer scale must
            // be compensated before transforming the baked vertices to world.
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

        public static bool TryAttachClips(Player3DAssetRegistry target)
        {
            if (target == null) return false;
            var bindings = new List<Player3DAnimationBinding>(target.Animations);
            AnimationClip[] assets = Resources.LoadAll<AnimationClip>(ResourcePath);
            bool changed = false;
            for (int index = 0; index < ClipNames.Length; index++)
            {
                string name = ClipNames[index];
                if (target.TryGetAnimation(name, out Player3DAnimationBinding existing) && existing?.Clip != null) continue;
                AnimationClip clip = assets.FirstOrDefault(value => value.name == name);
                if (clip == null || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - Duration((HomeToiletActorPhase)index)) > .003f ||
                    clip.isLooping != (index == (int)HomeToiletActorPhase.Seated)) return false;
                bindings.Add(new Player3DAnimationBinding(name, "home_toilet", clip, clip.length, clip.isLooping));
                changed = true;
            }
            if (changed)
                target.Configure(target.Animator, target.ModelRoot, target.Renderers.ToArray(), target.MeshBindings.ToArray(),
                    target.AnatomicalParts.ToArray(), bindings.ToArray(), target.Anchors, target.Metrics,
                    target.SourceGeneratorVersion, target.SourcePose, target.SourceTriangleCount, target.BuildSignature, target.FaceAtlas);
            return true;
        }

        [Serializable] private sealed class Manifest { public ClipData[] clips; }
        [Serializable] private sealed class ClipData { public string name; public PoseSample[] samples; }
        [Serializable] private sealed class PoseSample { public float[] source_pelvis; }
        private void OnDisable() => End();
        private void OnDestroy()
        {
            End();
            if (clearanceMesh != null) Destroy(clearanceMesh);
        }
    }
}
