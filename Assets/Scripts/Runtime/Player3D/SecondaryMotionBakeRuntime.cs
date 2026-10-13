using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace BarPromenade
{
    internal enum SecondaryMotionBakePart { Hair, Jacket, Sling }
    internal enum SecondaryMotionBakeResult { None, Sampled, Disabled, Wind, NoContext, NoAnimation, StaleClock, RootMotion, BodyMotion, Damage, MissingAsset, TrackLayout, FinalPose, GuardLayout }

    internal readonly struct SecondaryMotionBakeSample
    {
        internal readonly float[] From, To;
        internal readonly float Alpha;
        internal readonly object TrackIdentity;
        internal readonly Vector3 WorldOffset;
        internal readonly Vector3 WorldOffsetVelocity;
        internal SecondaryMotionBakeSample(float[] from, float[] to, float alpha, object trackIdentity, Vector3 worldOffset, Vector3 worldOffsetVelocity)
        { From = from; To = to; Alpha = alpha; TrackIdentity = trackIdentity; WorldOffset = worldOffset; WorldOffsetVelocity = worldOffsetVelocity; }
    }

    /// <summary>Offline physical samples, selected by the authoritative animation clock.
    /// No sampling or simulation is performed when an asset is missing or incompatible.</summary>
    internal static partial class SecondaryMotionBakeRuntime
    {
        internal const string ResourceRoot = "Player/SecondaryMotionBakes/";
        private const int Magic = 0x42504d53, Version = 4, MaximumResidentClips = 12;
        private static readonly Dictionary<Player3DAssetRegistry, Context> contexts = new();
        private static readonly Dictionary<ClipKey, ClipData> clips = new();
        private static readonly Queue<ClipKey> residentOrder = new();
        internal static bool Enabled { get; set; } = true;
        internal static bool CaptureRejectionDetails { get; set; }

        private sealed class Context
        {
            internal AnimationClip Clip;
            internal string ClipName;
            internal double Seconds;
            internal int Frame;
            internal string Variant;
            internal bool MotionCompatible = true, HasRootHistory, FrameMotionSteady;
            internal int RootFrame = -1;
            internal Vector3 RootPosition, Velocity;
            internal Quaternion RootRotation;
            internal double RootTime;
            internal float RootDeltaTime, Acceleration, AngularSpeed;
            internal bool ValidateMotion, HasOffsetHistory, OffsetCompatible;
            internal int OffsetFrame = -1;
            internal double OffsetTime;
            internal Vector3 OffsetPosition, OffsetVelocity, FrameOffset;
            internal readonly Dictionary<SecondaryMotionBakePart, PoseGuard> Guards = new();
            internal readonly SecondaryMotionBakeResult[] LastResults = new SecondaryMotionBakeResult[3];
            internal readonly string[] Rejections = new string[3];
        }

        internal sealed class Track
        {
            internal SecondaryMotionBakePart Part;
            internal string Signature;
            // Visible deformation and final-pose guards use independent clocks:
            // coarse physical output must not coarsen anatomy validation.
            internal float[][] Values, Guards;
        }

        internal sealed class ClipData
        {
            internal float Duration;
            internal bool Loop;
            internal int WarmupSteps;
            internal Track[] Tracks;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        { contexts.Clear(); clips.Clear(); residentOrder.Clear(); ResetDiagnostics(); Enabled = true; CaptureRejectionDetails = false; }

        internal static void SetContext(Player3DAssetRegistry registry, AnimationClip clip, double seconds, string variant = null,
            bool validateRootMotion = false)
        {
            if (registry == null) return;
            if (!contexts.TryGetValue(registry, out Context context))
                contexts.Add(registry, context = new Context());
            if (context.Clip != clip) context.ClipName = clip != null ? clip.name : null;
            if (validateRootMotion)
            {
                Transform root = registry.transform;
                if (context.RootFrame != Time.frameCount)
                {
                    double now = Time.timeAsDouble;
                    float dt = (float)(now - context.RootTime);
                    Vector3 velocity = dt > 0f ? (root.position - context.RootPosition) / dt : Vector3.zero;
                    context.RootDeltaTime = dt;
                    context.Acceleration = dt > 0f ? (velocity - context.Velocity).magnitude / dt : float.PositiveInfinity;
                    context.AngularSpeed = dt > 0f ? Quaternion.Angle(root.rotation, context.RootRotation) / dt : float.PositiveInfinity;
                    context.FrameMotionSteady = context.HasRootHistory && dt > 0f && dt < .15f &&
                        (velocity - context.Velocity).sqrMagnitude < 9f * dt * dt &&
                        Quaternion.Angle(root.rotation, context.RootRotation) < 30f * dt &&
                        Vector3.Dot(root.up, Vector3.up) > .999f;
                    context.RootPosition = root.position; context.RootRotation = root.rotation;
                    context.RootTime = now; context.Velocity = velocity; context.HasRootHistory = true;
                    context.RootFrame = Time.frameCount;
                }
                Vector3 expected = root.TransformDirection(registry.Metrics.LocalForward) * AuthoredSpeed(context.ClipName ?? "");
                context.MotionCompatible = context.FrameMotionSteady && (context.Velocity - expected).sqrMagnitude < .35f * .35f;
            }
            else if (clip != null) context.MotionCompatible = true;
            context.Clip = clip; context.Seconds = seconds; context.Frame = Time.frameCount;
            context.Variant = variant;
            context.ValidateMotion = validateRootMotion;
        }

        internal static void Release(Player3DAssetRegistry registry)
        { if (!ReferenceEquals(registry, null)) contexts.Remove(registry); }
        internal static void ReloadAssets() { clips.Clear(); residentOrder.Clear(); }

        internal static float AuthoredSpeed(string clip) => clip switch
        {
            "Walk" => Player3DCharacterPresentation.FullWalkSpeed,
            "WalkBack" => -Player3DCharacterPresentation.FullWalkBackSpeed,
            "Run" => Player3DCharacterPresentation.FullRunSpeed,
            _ => 0f
        };

        internal static string EquipmentVariant(Player3DAssetRegistry registry)
        {
            CombatActor actor = registry != null ? registry.GetComponentInParent<CombatActor>() : null;
            return actor != null && actor.CarriedWeapon(CombatWeaponId.Shotgun) != null &&
                actor.CarriedWeaponLocation(CombatWeaponId.Shotgun) == CombatWeaponCarryLocation.Stowed
                ? "stored-shotgun" : "no-gear";
        }

        internal static bool TrySample(Player3DAssetRegistry registry, SecondaryMotionBakePart part,
            string signature, int valueCount, bool outside, WindSample wind,
            out SecondaryMotionBakeSample sample, string variant = "")
        {
            sample = default;
            if (registry == null || !contexts.TryGetValue(registry, out Context context))
            { RecordResult(part, SecondaryMotionBakeResult.NoContext); return false; }
            if (!Enabled) return Reject(SecondaryMotionBakeResult.Disabled);
            if (outside && wind.Strength01 > .01f) return Reject(SecondaryMotionBakeResult.Wind);
            if (context.Clip == null) return Reject(SecondaryMotionBakeResult.NoAnimation);
            if (context.Frame != Time.frameCount) return Reject(SecondaryMotionBakeResult.StaleClock);
            if (!context.MotionCompatible) return Reject(SecondaryMotionBakeResult.RootMotion);
            CombatActor actor = registry.GetComponentInParent<CombatActor>();
            if (actor != null && (actor.IsRagdollActive || actor.IsKnockedDown || actor.BodyDamage.ContactRevision != 0)) return Reject(SecondaryMotionBakeResult.Damage);
            variant = string.IsNullOrEmpty(variant) ? context.Variant ?? EquipmentVariant(registry) : variant;
            ClipData clip = GetOrLoadClip(new ClipKey(variant, context.ClipName));
            if (clip == null || Mathf.Abs(clip.Duration - context.Clip.length) > .001f) return Reject(SecondaryMotionBakeResult.MissingAsset);
            Track track = null;
            foreach (Track candidate in clip.Tracks) if (candidate.Part == part) { track = candidate; break; }
            if (track == null || track.Signature != signature || track.Values[0].Length != valueCount) return Reject(SecondaryMotionBakeResult.TrackLayout);
            double seconds = clip.Loop ? context.Seconds % clip.Duration : Math.Clamp(context.Seconds, 0d, clip.Duration);
            if (seconds < 0d) seconds += clip.Duration;
            float frame = (float)(seconds / clip.Duration * (track.Values.Length - 1));
            int from = Mathf.Clamp(Mathf.FloorToInt(frame), 0, track.Values.Length - 1);
            int to = Mathf.Min(from + 1, track.Values.Length - 1);
            float alpha = frame - from;
            float guardFrame = (float)(seconds / clip.Duration * (track.Guards.Length - 1));
            int guardFrom = Mathf.Clamp(Mathf.FloorToInt(guardFrame), 0, track.Guards.Length - 1);
            int guardTo = Mathf.Min(guardFrom + 1, track.Guards.Length - 1);
            float guardAlpha = guardFrame - guardFrom;
            // A recording's obsolete gear layout cannot be fixed by rebuilding
            // today's rig guard on every sample. Retain the current guard until
            // its actual equipment layout changes, then reject the old track.
            if (!context.Guards.TryGetValue(part, out PoseGuard guard) || !guard.IsLayoutCurrent)
            {
                context.Guards[part] = guard = new PoseGuard(registry, part);
                guardBuildCount++;
            }
            if (guard.ValueCount != track.Guards[guardFrom].Length)
                return Reject(SecondaryMotionBakeResult.GuardLayout);
            if (!guard.Matches(track.Guards[guardFrom], track.Guards[guardTo], guardAlpha,
                context.ClipName == "Idle", track.Values[from], track.Values[to]))
            {
                if (CaptureRejectionDetails && context.Rejections[(int)part] == null) context.Rejections[(int)part] = guard.Describe(track.Guards[guardFrom], track.Guards[guardTo], guardAlpha,
                    context.ClipName == "Idle", track.Values[from], track.Values[to]);
                return Reject(SecondaryMotionBakeResult.FinalPose);
            }
            if (context.ValidateMotion)
            {
                if (context.OffsetFrame != Time.frameCount)
                {
                    double now = Time.timeAsDouble;
                    float dt = (float)(now - context.OffsetTime);
                    Vector3 position = registry.transform.position + guard.WorldOffset;
                    Vector3 velocity = dt > 0f ? (position - context.OffsetPosition) / dt : Vector3.zero;
                    Vector3 expected = registry.transform.TransformDirection(registry.Metrics.LocalForward) * AuthoredSpeed(context.ClipName);
                    context.OffsetCompatible = !context.HasOffsetHistory || dt > 0f && dt < .15f &&
                        (velocity - expected).sqrMagnitude < .35f * .35f &&
                        (velocity - context.OffsetVelocity).sqrMagnitude < 9f * dt * dt;
                    context.OffsetPosition = position; context.OffsetVelocity = velocity; context.OffsetTime = now;
                    context.HasOffsetHistory = true; context.OffsetFrame = Time.frameCount; context.FrameOffset = guard.WorldOffset;
                }
                if (!context.OffsetCompatible || (guard.WorldOffset - context.FrameOffset).sqrMagnitude > .002f * .002f)
                    return Reject(SecondaryMotionBakeResult.BodyMotion);
            }
            sample = new SecondaryMotionBakeSample(track.Values[from], track.Values[to], alpha, track, guard.WorldOffset,
                context.ValidateMotion ? context.OffsetVelocity - context.Velocity : Vector3.zero);
            context.LastResults[(int)part] = SecondaryMotionBakeResult.Sampled;
            RecordResult(part, SecondaryMotionBakeResult.Sampled);
            return true;
            bool Reject(SecondaryMotionBakeResult reason)
            { context.LastResults[(int)part] = reason; RecordResult(part, reason); return false; }
        }

        internal static SecondaryMotionBakeResult LastResult(Player3DAssetRegistry registry, SecondaryMotionBakePart part) =>
            registry != null && contexts.TryGetValue(registry, out Context context) ? context.LastResults[(int)part] : SecondaryMotionBakeResult.NoContext;
        internal static string LastRejection(Player3DAssetRegistry registry, SecondaryMotionBakePart part) =>
            registry != null && contexts.TryGetValue(registry, out Context context) ? context.Rejections[(int)part] : null;

        private static long Values(ClipData clip)
        {
            long count = 0;
            if (clip != null) foreach (Track track in clip.Tracks)
                count += (long)track.Values.Length * track.Values[0].Length +
                    (long)track.Guards.Length * track.Guards[0].Length;
            return count;
        }
        private static long ResidentValues()
        { long count = 0; foreach (ClipData clip in clips.Values) count += Values(clip); return count; }

        internal static string DescribeContext(Player3DAssetRegistry registry, SecondaryMotionBakePart part)
        {
            if (!contexts.TryGetValue(registry, out Context context)) return "no animation context";
            if (context.Clip == null) return "no single animation";
            var key = new ClipKey(context.Variant ?? EquipmentVariant(registry), context.ClipName);
            string state = FormattableString.Invariant($"{key}; root motion={context.MotionCompatible}; age={Time.frameCount - context.Frame}; dt={context.RootDeltaTime:F6}; velocity={context.Velocity:F5}; acceleration={context.Acceleration:F5}; angular speed={context.AngularSpeed:F5}");
            if (!clips.TryGetValue(key, out ClipData clip) || clip == null) return state + "; no compatible asset";
            foreach (Track track in clip.Tracks)
                if (track.Part == part)
                {
                    double seconds = clip.Loop ? context.Seconds % clip.Duration : Math.Clamp(context.Seconds, 0d, clip.Duration);
                    if (seconds < 0d) seconds += clip.Duration;
                    float frame = (float)(seconds / clip.Duration * (track.Guards.Length - 1));
                    int from = Mathf.Clamp(Mathf.FloorToInt(frame), 0, track.Guards.Length - 1), to = Mathf.Min(from + 1, track.Guards.Length - 1);
                    float valueFrame = (float)(seconds / clip.Duration * (track.Values.Length - 1));
                    int valueFrom = Mathf.Clamp(Mathf.FloorToInt(valueFrame), 0, track.Values.Length - 1);
                    int valueTo = Mathf.Min(valueFrom + 1, track.Values.Length - 1);
                    return state + "; " + new PoseGuard(registry, part).Describe(track.Guards[from], track.Guards[to], frame - from,
                        context.Clip.name == "Idle", track.Values[valueFrom], track.Values[valueTo]);
                }
            return state + "; missing track";
        }

        internal static byte[] Encode(ClipData clip)
        {
            if (clip == null || !float.IsFinite(clip.Duration) || clip.Duration <= 0f ||
                clip.Tracks == null || clip.Tracks.Length < 1 || clip.Tracks.Length > 3)
                throw new InvalidDataException("Invalid secondary-motion recording header.");
            long total = 0;
            int parts = 0;
            using var output = new MemoryStream();
            using (var compressor = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var writer = new BinaryWriter(compressor))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(clip.Duration); writer.Write(clip.Loop);
                if (clip.WarmupSteps < 0 || clip.WarmupSteps > 1000000) throw new InvalidDataException("Invalid physical warm-up.");
                writer.Write(clip.WarmupSteps);
                writer.Write(clip.Tracks.Length);
                foreach (Track track in clip.Tracks)
                {
                    if (track == null || (int)track.Part < 0 || (int)track.Part > 2 ||
                        (parts & 1 << (int)track.Part) != 0 || string.IsNullOrEmpty(track.Signature) ||
                        track.Signature.Length > 4096 || track.Values == null || track.Guards == null ||
                        track.Values.Length < 2 || track.Values.Length > 4096 ||
                        track.Guards.Length < 2 || track.Guards.Length > 16384 || track.Values[0] == null || track.Guards[0] == null)
                        throw new InvalidDataException("Invalid secondary-motion track.");
                    parts |= 1 << (int)track.Part;
                    int frames = track.Values.Length, values = track.Values[0].Length;
                    int guardFrames = track.Guards.Length, guards = track.Guards[0].Length;
                    if (values < 1 || values > 250000 || guards < 1 || guards > 10000)
                        throw new InvalidDataException("Secondary-motion recording exceeds its bounded layout.");
                    total += (long)frames * values + (long)guardFrames * guards;
                    if (total > 16000000) throw new InvalidDataException("Secondary-motion recording exceeds its bounded layout.");
                    for (int frame = 0; frame < frames; frame++)
                        if (track.Values[frame] == null || track.Values[frame].Length != values)
                            throw new InvalidDataException("Secondary-motion frame layouts differ.");
                    for (int frame = 0; frame < guardFrames; frame++)
                        if (track.Guards[frame] == null || track.Guards[frame].Length != guards)
                            throw new InvalidDataException("Secondary-motion frame layouts differ.");
                    byte[] signature = System.Text.Encoding.UTF8.GetBytes(track.Signature);
                    if (signature.Length > 4096) throw new InvalidDataException("Secondary-motion signature is too long.");
                    writer.Write((int)track.Part); writer.Write((ushort)signature.Length); writer.Write(signature);
                    writer.Write(frames); writer.Write(values); writer.Write(guardFrames); writer.Write(guards);
                    WriteChannels(track.Values, values, new ushort[frames], new byte[frames * 2]);
                    WriteChannels(track.Guards, guards, new ushort[guardFrames], new byte[guardFrames * 2]);
                }

                void WriteChannels(float[][] matrix, int channels, ushort[] quantized, byte[] deltaBytes)
                {
                    const double maximumError = 1e-5;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
                        for (int frame = 0; frame < matrix.Length; frame++)
                        {
                            float value = matrix[frame][channel];
                            if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite secondary-motion sample.");
                            minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value);
                        }
                        if (minimum == maximum)
                        {
                            writer.Write((byte)0); writer.Write(minimum);
                            continue;
                        }
                        double range = (double)maximum - minimum;
                        bool fits = true;
                        for (int frame = 0; frame < matrix.Length; frame++)
                        {
                            double normalized = ((double)matrix[frame][channel] - minimum) / range;
                            ushort value = (ushort)Math.Clamp((int)Math.Round(normalized * ushort.MaxValue,
                                MidpointRounding.AwayFromZero), 0, ushort.MaxValue);
                            quantized[frame] = value;
                            float restored = (float)(minimum + range * (value / (double)ushort.MaxValue));
                            if (Math.Abs((double)restored - matrix[frame][channel]) > maximumError)
                            { fits = false; break; }
                        }
                        if (!fits)
                        {
                            writer.Write((byte)2);
                            for (int frame = 0; frame < matrix.Length; frame++) writer.Write(matrix[frame][channel]);
                            continue;
                        }
                        writer.Write((byte)1); writer.Write(minimum); writer.Write(maximum);
                        ushort previous = 0;
                        for (int frame = 0; frame < matrix.Length; frame++)
                        {
                            // Modulo ushort subtraction retains both signs while
                            // repeated/small physical changes become compressible.
                            ushort delta = unchecked((ushort)(quantized[frame] - previous));
                            deltaBytes[frame] = unchecked((byte)delta);
                            deltaBytes[matrix.Length + frame] = (byte)(delta >> 8);
                            previous = quantized[frame];
                        }
                        // Separate byte planes expose repeated high bytes to
                        // Deflate instead of interleaving them with moving lows.
                        writer.Write(deltaBytes);
                    }
                }
            }
            return output.ToArray();
        }

        private static ClipData Decode(byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length == 0 || bytes.Length > 80000000) return null;
                using var input = new MemoryStream(bytes, false);
                using var decompressor = new DeflateStream(input, CompressionMode.Decompress);
                using var reader = new BinaryReader(decompressor);
                if (reader.ReadInt32() != Magic) return null;
                int format = reader.ReadInt32();
                if (format != 2 && format != 3 && format != Version) return null;
                var clip = new ClipData { Duration = reader.ReadSingle(), Loop = reader.ReadBoolean() };
                clip.WarmupSteps = format >= 4 ? reader.ReadInt32() : 0;
                if (clip.WarmupSteps < 0 || clip.WarmupSteps > 1000000) return null;
                int count = reader.ReadInt32();
                if (!float.IsFinite(clip.Duration) || clip.Duration <= 0f || count < 1 || count > 3) return null;
                clip.Tracks = new Track[count];
                long total = 0;
                int parts = 0;
                for (int index = 0; index < count; index++)
                {
                    int part = reader.ReadInt32();
                    int signatureLength = reader.ReadUInt16();
                    if (part < 0 || part > 2 || (parts & 1 << part) != 0 || signatureLength < 1 || signatureLength > 4096) return null;
                    parts |= 1 << part;
                    byte[] signature = reader.ReadBytes(signatureLength);
                    if (signature.Length != signatureLength) return null;
                    var track = new Track { Part = (SecondaryMotionBakePart)part,
                        Signature = new System.Text.UTF8Encoding(false, true).GetString(signature) };
                    int frames = reader.ReadInt32(), values = reader.ReadInt32();
                    int guardFrames = format == 2 ? frames : reader.ReadInt32(), guards = reader.ReadInt32();
                    if (frames < 2 || frames > 4096 || guardFrames < 2 || guardFrames > 16384 ||
                        values < 1 || values > 250000 || guards < 1 || guards > 10000) return null;
                    total += (long)frames * values + (long)guardFrames * guards;
                    if (total > 16000000) return null;
                    track.Values = ReadChannels(frames, values);
                    track.Guards = ReadChannels(guardFrames, guards);
                    clip.Tracks[index] = track;
                }
                if (decompressor.ReadByte() != -1) return null;
                return clip;

                float[][] ReadChannels(int frames, int channels)
                {
                    var result = new float[frames][];
                    for (int frame = 0; frame < frames; frame++) result[frame] = new float[channels];
                    var deltaBytes = new byte[frames * 2];
                    for (int channel = 0; channel < channels; channel++)
                    {
                        byte mode = reader.ReadByte();
                        if (mode == 0)
                        {
                            float value = ReadFinite();
                            for (int frame = 0; frame < frames; frame++) result[frame][channel] = value;
                        }
                        else if (mode == 1)
                        {
                            float minimum = ReadFinite(), maximum = ReadFinite();
                            if (minimum >= maximum) throw new InvalidDataException("Invalid quantized secondary-motion range.");
                            double range = (double)maximum - minimum;
                            int read = 0;
                            while (read < deltaBytes.Length)
                            {
                                int received = reader.Read(deltaBytes, read, deltaBytes.Length - read);
                                if (received == 0) throw new EndOfStreamException();
                                read += received;
                            }
                            ushort value = 0;
                            for (int frame = 0; frame < frames; frame++)
                            {
                                int delta = deltaBytes[frame] | deltaBytes[frames + frame] << 8;
                                value = unchecked((ushort)(value + delta));
                                float restored = (float)(minimum + range * (value / (double)ushort.MaxValue));
                                if (!float.IsFinite(restored)) throw new InvalidDataException("Non-finite quantized secondary motion.");
                                result[frame][channel] = restored;
                            }
                        }
                        else if (mode == 2)
                            for (int frame = 0; frame < frames; frame++) result[frame][channel] = ReadFinite();
                        else throw new InvalidDataException("Unknown secondary-motion channel encoding.");
                    }
                    return result;
                }
                float ReadFinite()
                {
                    float value = reader.ReadSingle();
                    if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite secondary-motion sample.");
                    return value;
                }
            }
            catch (Exception failure) when (failure is IOException || failure is ArgumentException) { return null; }
        }

        /// <summary>Compare final posed anatomy, wardrobe visibility, corrective shapes and real gear.
        /// Guard data stays separate from the recorded deformation and is never rendered.</summary>
        internal sealed class PoseGuard
        {
            private readonly Player3DAssetRegistry registry;
            private readonly SecondaryMotionBakePart part;
            private readonly Transform[] bones;
            private readonly Renderer[] wardrobe;
            private readonly SkinnedMeshRenderer[] skins;
            private readonly int[][] shapeIndices;
            private readonly SecondaryMotionBakeContactBounds[] bootBounds;
            private readonly Renderer[] jacketRenderers;
            private readonly PlayerEquipmentContacts equipment;
            private readonly float[] scratch;
            private readonly int equipmentCount, prefix, shapeStart;
            private Vector3 localOffset;
            internal Vector3 WorldOffset { get; private set; }
            internal int ValueCount => scratch.Length;
            internal bool IsLayoutCurrent => registry.GetComponent<PlayerEquipmentContacts>() == equipment &&
                (equipment != null ? equipment.BoxSnapshotCount(false) : 0) == equipmentCount;

            internal PoseGuard(Player3DAssetRegistry registry, SecondaryMotionBakePart part)
            {
                this.registry = registry;
                this.part = part;
                var selected = new List<Transform>();
                foreach (Player3DAnatomicalPartBinding binding in registry.AnatomicalParts)
                    if (binding.Bone != null && !selected.Contains(binding.Bone))
                        selected.Add(binding.Bone);
                bones = selected.ToArray();
                var visible = new List<Renderer>(); var meshes = new List<SkinnedMeshRenderer>(); var shapes = new List<int[]>();
                int shapeCount = 0;
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    if (binding.Renderer == null || !(binding.MeshName.StartsWith("CLO_", StringComparison.Ordinal) || binding.Role == "hair")) continue;
                    visible.Add(binding.Renderer);
                    if (part == SecondaryMotionBakePart.Hair || !(binding.Renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null) continue;
                    var indices = new List<int>();
                    for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                    {
                        string name = skin.sharedMesh.GetBlendShapeName(i);
                        if (!name.StartsWith("JacketElbowFold.", StringComparison.Ordinal) &&
                            !name.EndsWith(".JacketElbowFold.L", StringComparison.Ordinal) &&
                            !name.EndsWith(".JacketElbowFold.R", StringComparison.Ordinal)) indices.Add(i);
                    }
                    if (indices.Count > 0) { meshes.Add(skin); shapes.Add(indices.ToArray()); shapeCount += indices.Count; }
                }
                wardrobe = visible.ToArray(); skins = meshes.ToArray(); shapeIndices = shapes.ToArray();
                bootBounds = new SecondaryMotionBakeContactBounds[skins.Length];
                for (int mesh = 0; mesh < skins.Length; mesh++)
                {
                    string name = skins[mesh].name;
                    if (!name.StartsWith("CLO_Boot.", StringComparison.Ordinal) && !name.StartsWith("CLO_BootSole.", StringComparison.Ordinal)) continue;
                    string side = name.EndsWith(".L", StringComparison.Ordinal) ? ".L" : ".R";
                    Transform shin = CityPedestrianHandProps.FindSocket(registry.Animator.transform, "shin" + side);
                    Transform foot = CityPedestrianHandProps.FindSocket(registry.Animator.transform, "foot" + side);
                    bootBounds[mesh] = new SecondaryMotionBakeContactBounds(skins[mesh], shin, foot);
                }
                var jacket = new List<Renderer>();
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                    if (binding.Renderer != null && binding.MeshName.StartsWith("CLO_Jacket", StringComparison.Ordinal)) jacket.Add(binding.Renderer);
                jacketRenderers = jacket.ToArray();
                equipment = registry.GetComponent<PlayerEquipmentContacts>();
                equipmentCount = equipment != null ? equipment.BoxSnapshotCount(false) : 0;
                shapeStart = 3 + bones.Length * 10 + wardrobe.Length;
                prefix = shapeStart + shapeCount;
                scratch = new float[prefix + equipmentCount * 25];
            }

            internal float[] Capture()
            { var result = new float[ValueCount]; Fill(result); return result; }

            private bool Fill(float[] destination)
            {
                // Graph evaluation can move carried meshes after their last
                // contact snapshot. Validate this final pose, never last frame.
                equipment?.UpdatePose();
                if ((equipment != null ? equipment.BoxSnapshotCount(false) : 0) != equipmentCount) return false;
                int index = 0; Transform frame = registry.transform;
                Vector3 pelvis = frame.InverseTransformPoint(registry.Anchors.Pelvis.position);
                Put(pelvis.x); Put(pelvis.y); Put(pelvis.z);
                foreach (Transform bone in bones)
                {
                    Vector3 position = frame.InverseTransformPoint(bone.position);
                    Quaternion rotation = Quaternion.Inverse(frame.rotation) * bone.rotation;
                    Vector3 scale = bone.lossyScale;
                    Put(position.x); Put(position.y); Put(position.z);
                    Put(rotation.x); Put(rotation.y); Put(rotation.z); Put(rotation.w);
                    Put(scale.x); Put(scale.y); Put(scale.z);
                }
                foreach (Renderer renderer in wardrobe) Put(renderer.enabled && renderer.gameObject.activeInHierarchy ? 1f : 0f);
                for (int mesh = 0; mesh < skins.Length; mesh++)
                    foreach (int shape in shapeIndices[mesh]) Put(skins[mesh].GetBlendShapeWeight(shape));
                if (equipmentCount > 0) equipment.CaptureBakeSnapshot(frame, destination, index);
                return true;
                void Put(float value) => destination[index++] = value;
            }

            internal bool Matches(float[] from, float[] to, float alpha, bool idle = false,
                float[] deformationFrom = null, float[] deformationTo = null)
            {
                if (from.Length != scratch.Length || to.Length != scratch.Length || !Fill(scratch)) return false;
                localOffset = Position(scratch, 0) - Vector3.Lerp(Position(from, 0), Position(to, 0), alpha);
                WorldOffset = registry.transform.TransformVector(localOffset);
                // Flat-ground Idle corrects the clip's small whole-body bob.
                // Retarget only a bounded vertical translation, certified by
                // every anatomical bone and every real equipment box below.
                if (!idle || Mathf.Abs(WorldOffset.y) > .02f ||
                    WorldOffset.x * WorldOffset.x + WorldOffset.z * WorldOffset.z > .00001f * .00001f)
                { localOffset = Vector3.zero; WorldOffset = Vector3.zero; }
                int index = 3;
                foreach (Transform unused in bones)
                {
                    Vector3 expectedPosition = Vector3.Lerp(Position(from, index), Position(to, index), alpha) + localOffset;
                    if ((Position(scratch, index) - expectedPosition).sqrMagnitude > .002f * .002f) return false;
                    index += 3;
                    Quaternion expected = Quaternion.Slerp(Rotation(from, index), Rotation(to, index), alpha);
                    if (Quaternion.Angle(Rotation(scratch, index), expected) > 2f) return false;
                    index += 4;
                    for (int axis = 0; axis < 3; axis++, index++)
                        if (Mathf.Abs(scratch[index] - Mathf.Lerp(from[index], to[index], alpha)) > .001f) return false;
                }
                for (; index < shapeStart; index++)
                    if (Mathf.Abs(scratch[index] - Mathf.Lerp(from[index], to[index], alpha)) > .001f) return false;
                for (int mesh = 0; mesh < skins.Length; mesh++)
                {
                    bool disjointBoot = false, checkedBoot = false;
                    foreach (int unused in shapeIndices[mesh])
                    {
                        if (Mathf.Abs(scratch[index] - Mathf.Lerp(from[index], to[index], alpha)) > .1f)
                        {
                            if (!checkedBoot)
                            {
                                checkedBoot = true;
                                disjointBoot = idle && BootCannotReachDeformation(mesh, deformationFrom, deformationTo);
                            }
                            if (!disjointBoot) return false;
                        }
                        index++;
                    }
                }
                // Imported mesh matrices may carry a scale of 100. Compare
                // physical box corners, never dimensionless basis entries.
                for (; index < scratch.Length; index += 25)
                    if (BoxError(from, to, scratch, index, alpha, localOffset) > .002f) return false;
                return true;
            }

            private bool BootCannotReachDeformation(int mesh, float[] from, float[] to)
            {
                if (bootBounds[mesh] == null || !bootBounds[mesh].TryBounds(out Bounds boot)) return false;
                // Cover the allowed 2 mm / 2 degree final-pose residual, using
                // a conservative whole-rig lever arm instead of a foot-y rule.
                float lever = boot.size.magnitude;
                foreach (Transform bone in bones) lever = Mathf.Max(lever, Vector3.Distance(bone.position, boot.center) + boot.extents.magnitude);
                boot.Expand(2f * (.002f + 2f * Mathf.Sin(Mathf.Deg2Rad) * lever));
                if (part == SecondaryMotionBakePart.Jacket)
                {
                    if (jacketRenderers.Length == 0) return false;
                    foreach (Renderer jacket in jacketRenderers)
                        if (jacket == null || boot.Intersects(jacket.bounds)) return false;
                    return true;
                }
                if (part != SecondaryMotionBakePart.Sling || from == null || to == null || from.Length < 123 || to.Length < 123) return false;
                Bounds chain = new Bounds(registry.transform.TransformPoint(Position(from, 0)) + WorldOffset, Vector3.zero);
                for (int point = 0; point < 123; point += 3)
                {
                    chain.Encapsulate(registry.transform.TransformPoint(Position(from, point)) + WorldOffset);
                    chain.Encapsulate(registry.transform.TransformPoint(Position(to, point)) + WorldOffset);
                }
                chain.Expand(.05f); // Ribbon half width, interpolation and anchor residual.
                return !boot.Intersects(chain);
            }

            private static float BoxError(float[] from, float[] to, float[] current, int offset, float alpha, Vector3 translation = default)
            {
                Matrix4x4 first = Matrix(from), second = Matrix(to), actual = Matrix(current);
                float worst = 0f;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 expected = Vector3.Lerp(first.MultiplyPoint3x4(Point(from, corner)), second.MultiplyPoint3x4(Point(to, corner)), alpha) + translation;
                    worst = Mathf.Max(worst, (actual.MultiplyPoint3x4(Point(current, corner)) - expected).sqrMagnitude);
                }
                return Mathf.Sqrt(worst);
                Matrix4x4 Matrix(float[] values)
                { Matrix4x4 matrix = default; for (int i = 0; i < 16; i++) matrix[i] = values[offset + i]; return matrix; }
                Vector3 Point(float[] values, int corner)
                {
                    Vector3 centre = Position(values, offset + 16), extent = Position(values, offset + 19) + Position(values, offset + 22);
                    return centre + new Vector3((corner & 1) == 0 ? -extent.x : extent.x,
                        (corner & 2) == 0 ? -extent.y : extent.y, (corner & 4) == 0 ? -extent.z : extent.z);
                }
            }

            internal string Describe(float[] from, float[] to, float alpha, bool idle = false,
                float[] deformationFrom = null, float[] deformationTo = null)
            {
                if (from.Length != scratch.Length || to.Length != scratch.Length || !Fill(scratch))
                    return "layout changed recorded=" + from.Length + ", current=" + scratch.Length;
                int index = 3;
                float positionError = 0f, rotationError = 0f, otherError = 0f;
                string positionBone = "", rotationBone = "";
                foreach (Transform bone in bones)
                {
                    float distance = (Position(scratch, index) - Vector3.Lerp(Position(from, index), Position(to, index), alpha)).magnitude;
                    if (distance > positionError) { positionError = distance; positionBone = bone.name; }
                    index += 3;
                    float angle = Quaternion.Angle(Rotation(scratch, index), Quaternion.Slerp(Rotation(from, index), Rotation(to, index), alpha));
                    if (angle > rotationError) { rotationError = angle; rotationBone = bone.name; }
                    index += 7;
                }
                int largest = index;
                for (; index < scratch.Length; index++)
                {
                    float error = Mathf.Abs(scratch[index] - Mathf.Lerp(from[index], to[index], alpha));
                    if (error > otherError) { otherError = error; largest = index; }
                }
                string channel;
                if (largest >= prefix) channel = equipment.BakeSnapshotName((largest - prefix) / 25) + " field " + (largest - prefix) % 25;
                else if (largest < shapeStart) channel = wardrobe[largest - 3 - bones.Length * 10].name + " visibility";
                else
                {
                    int shapeOffset = largest - shapeStart;
                    channel = "shape";
                    for (int mesh = 0; mesh < skins.Length; mesh++)
                    {
                        if (shapeOffset < shapeIndices[mesh].Length)
                        { channel = skins[mesh].name + "/" + skins[mesh].sharedMesh.GetBlendShapeName(shapeIndices[mesh][shapeOffset]); break; }
                        shapeOffset -= shapeIndices[mesh].Length;
                    }
                }
                float equipmentError = 0f;
                for (int box = prefix; box < scratch.Length; box += 25) equipmentError = Mathf.Max(equipmentError, BoxError(from, to, scratch, box, alpha));
                return FormattableString.Invariant($"position={positionError:F6}m({positionBone}); rotation={rotationError:F4}deg({rotationBone}); other={otherError:F6}({channel},actual={scratch[largest]:F6},expected={Mathf.Lerp(from[largest], to[largest], alpha):F6}); equipment corners={equipmentError:F6}m; compatible={Matches(from, to, alpha, idle, deformationFrom, deformationTo)}");
            }

            private static Vector3 Position(float[] values, int index) => new(values[index], values[index + 1], values[index + 2]);
            private static Quaternion Rotation(float[] values, int index) => new(values[index], values[index + 1], values[index + 2], values[index + 3]);
        }
    }
}
