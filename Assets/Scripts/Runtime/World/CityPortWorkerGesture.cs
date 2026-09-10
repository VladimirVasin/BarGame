using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Small overlays on a freshly sampled worker pose. The port owns time,
    /// eligibility and contacts; this helper never moves a worker's root.</summary>
    public sealed class CityPortWorkerGesture
    {
        public const float SmokeDurationSeconds = 9f;
        public const float SmokeDrawStartSeconds = 2.1f;
        public const float SmokeDrawEndSeconds = 3.6f;
        public const float SmokeExhaleSeconds = 4.7f;
        public const float SmokeExhaleEndSeconds = 6.2f;
        public const float WaveDurationSeconds = 3.1f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private readonly VillageResidentPresentation actor;
        private readonly int role;
        private readonly Transform spine, rightUpper, rightForearm, leftUpper, leftForearm;
        private readonly Transform rightHand, leftHand, mouth, cigaretteSocket;
        private readonly CityPedestrianHandPropRegistry cigarette;
        private readonly Renderer ember;
        private readonly Transform paper;
        private readonly Vector3 paperButt, paperTip, cigaretteAxisInHand;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private double smokeStart = -1d, waveStart = -1d, pendingGreeting = -1d, nextSmoke = double.NaN;
        private float headYaw, headPitch, conversationWeight;
        private bool greeted, exhaled;

        public bool IsSmoking { get; private set; }
        public bool IsWaving { get; private set; }
        public bool HasPendingWave => pendingGreeting >= 0d;
        public bool MouthBusy { get; private set; }
        public float WaveWeight { get; private set; }
        public float SmokeElapsedSeconds { get; private set; } = -1f;
        public CityPedestrianHandPropRegistry HeldCigarette => cigarette;
        public HomeBalconySmokingExhaleEffect SmokeEffect { get; }
        public Vector3 CigaretteButtPosition => paper.TransformPoint(paperButt);
        public Vector3 MouthPosition => mouth.position;

        public CityPortWorkerGesture(VillageResidentPresentation actor, int role)
        {
            this.actor = actor != null ? actor : throw new ArgumentNullException(nameof(actor));
            if (role < 0 || role > 4) throw new ArgumentOutOfRangeException(nameof(role));
            this.role = role;
            actor.Initialize();
            spine = Require("spine"); rightHand = Require("hand.R"); leftHand = Require("hand.L");
            rightUpper = Require("upper_arm.R"); rightForearm = Require("forearm.R");
            leftUpper = Require("upper_arm.L"); leftForearm = Require("forearm.L");
            mouth = Require(CityPedestrianHandProps.MouthSocketName);
            cigaretteSocket = Require(CityPedestrianHandProps.CigaretteRightSocketName);
            // StationWorker is the same NpcHumanV2 skeleton, including its FBX
            // unit scale. Attach retains the prop's measured inverse-scale Mount.
            cigarette = CityPedestrianHandProps.Attach(cigaretteSocket,
                CityPedestrianHandPropId.Cigarette, null, role % 4);
            Renderer paperRenderer = cigarette.FindRenderer("ACC_Cigarette");
            ember = cigarette.FindRenderer("ACC_CigaretteEmber");
            if (paperRenderer == null || ember == null)
                throw new InvalidOperationException("Port smoking requires the authored cigarette and ember.");
            MeshFilter filter = paperRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Port cigarette has no measured paper mesh.");
            paper = filter.transform;
            Bounds bounds = filter.sharedMesh.bounds;
            int axis = bounds.size.x > bounds.size.y ? 0 : 1;
            if (bounds.size.z > bounds.size[axis]) axis = 2;
            Vector3 end = Vector3.zero; end[axis] = bounds.extents[axis];
            Vector3 a = bounds.center - end, b = bounds.center + end;
            Vector3 aw = paper.TransformPoint(a), bw = paper.TransformPoint(b);
            float length = Vector3.Distance(aw, bw);
            Vector3 scale = cigarette.Mount.lossyScale, bodyScale = actor.transform.lossyScale;
            if (length < .06f || length > .095f ||
                Mathf.Abs(Mathf.Abs(scale.x / bodyScale.x) - 1f) > .03f ||
                Mathf.Abs(Mathf.Abs(scale.y / bodyScale.y) - 1f) > .03f ||
                Mathf.Abs(Mathf.Abs(scale.z / bodyScale.z) - 1f) > .03f)
                throw new InvalidOperationException("Port cigarette socket does not preserve the authored metre scale.");
            bool aNear = (aw - cigaretteSocket.position).sqrMagnitude < (bw - cigaretteSocket.position).sqrMagnitude;
            paperButt = aNear ? a : b; paperTip = aNear ? b : a;
            cigaretteAxisInHand = rightHand.InverseTransformDirection(
                paper.TransformPoint(paperTip) - paper.TransformPoint(paperButt)).normalized;
            ember.sharedMaterial = CityNightResources.EmissiveMaterial;
            ember.shadowCastingMode = ShadowCastingMode.Off;
            ember.receiveShadows = false;
            var smokeHost = new GameObject("Port Worker Exhale");
            smokeHost.transform.SetParent(actor.transform, false);
            SmokeEffect = smokeHost.AddComponent<HomeBalconySmokingExhaleEffect>();
            SmokeEffect.Initialize(mouth, CityBalconySmokerPresentation.CreateAnimationDefinition());
            if (!SmokeEffect.EnableManualBurstMode())
                throw new InvalidOperationException("Port worker could not initialize manual smoke.");
            Reset();
        }

        public void Apply(double lifeSeconds, float deltaSeconds, bool handsFree, bool resting,
            bool speaking, bool greeting, Vector3? lookTarget, bool seek, bool conversing = false)
        {
            float dt = Mathf.Max(0f, deltaSeconds);
            if (seek) Reset();
            if (double.IsNaN(nextSmoke)) nextSmoke = lifeSeconds + 5d + role * 1.5d;
            // Even the plume advances only on the caller's clock. Pausing its
            // ParticleSystem after a manual step prevents a second Unity tick.
            if (!seek && dt > 0f && SmokeEffect.Particles.particleCount > 0)
                SmokeEffect.Particles.Simulate(dt, true, false, false);
            SmokeEffect.Particles.Pause(true);

            ApplyHead(lifeSeconds, dt, handsFree, lookTarget, seek);
            if (handsFree)
            {
                float t = (float)(lifeSeconds % 60d) + role * 1.73f;
                spine.rotation = Quaternion.AngleAxis(Mathf.Sin(t * 1.17f) * .75f, actor.transform.forward) *
                    Quaternion.AngleAxis(Mathf.Sin(t * .83f) * .65f, actor.transform.right) * spine.rotation;
            }

            if (!handsFree)
            {
                StopSmoking(); waveStart = -1d; conversationWeight = 0f;
            }
            if (!seek && greeting && !greeted) pendingGreeting = lifeSeconds;
            greeted = greeting;
            if (pendingGreeting >= 0d && lifeSeconds - pendingGreeting > 25d) pendingGreeting = -1d;
            // A new exchange may reserve the mouth while the cigarette is
            // merely being held. Do not let its following draw/exhale cross
            // that reservation; no already active breath phase is prolonged.
            if (smokeStart >= 0d && (conversing || speaking) && !MouthBusy)
                smokeStart += dt;
            if (smokeStart >= 0d && lifeSeconds - smokeStart >= SmokeDurationSeconds) StopSmoking();
            // A working captain can acknowledge the arrival as soon as the
            // helm is released. A puff finishes before the same hand waves.
            if (handsFree && pendingGreeting >= 0d && smokeStart < 0d && !seek)
            { waveStart = lifeSeconds; pendingGreeting = -1d; }
            float waveTime = waveStart >= 0d ? (float)(lifeSeconds - waveStart) : -1f;
            IsWaving = handsFree && waveTime >= 0f && waveTime < WaveDurationSeconds;
            if (!IsWaving) waveStart = -1d;
            WaveWeight = IsWaving ? Smooth(waveTime / .55f) * Smooth((WaveDurationSeconds - waveTime) / .55f) : 0f;

            if (handsFree && resting && role >= 2 && !speaking && !conversing && !greeting && !IsWaving && pendingGreeting < 0d &&
                smokeStart < 0d && lifeSeconds >= nextSmoke && !seek)
            {
                smokeStart = lifeSeconds; exhaled = false;
                nextSmoke = lifeSeconds + 31d + role * 2.7d + Jitter((long)(lifeSeconds / 17d), role) * 7d;
            }
            float smokeTime = smokeStart >= 0d ? (float)(lifeSeconds - smokeStart) : -1f;
            if (smokeTime >= SmokeDurationSeconds) { StopSmoking(); smokeTime = -1f; }
            IsSmoking = handsFree && smokeTime >= 0f;
            SmokeElapsedSeconds = IsSmoking ? smokeTime : -1f;
            MouthBusy = IsSmoking && ((smokeTime >= SmokeDrawStartSeconds && smokeTime <= SmokeDrawEndSeconds) ||
                (smokeTime >= SmokeExhaleSeconds && smokeTime < SmokeExhaleEndSeconds));

            conversationWeight = handsFree && !IsWaving && !IsSmoking
                ? Mathf.MoveTowards(conversationWeight, speaking ? 1f : 0f, dt * 3f) : 0f;
            if (IsWaving) ApplyWave(waveTime);
            else if (IsSmoking) ApplySmoke(smokeTime, !seek);
            else if (conversationWeight > 0f) ApplyConversation(lifeSeconds, conversationWeight);
            else if (handsFree && resting) ApplyCoatAdjustment(lifeSeconds);
        }

        public void Reset()
        {
            StopSmoking();
            waveStart = pendingGreeting = -1d; nextSmoke = double.NaN;
            headYaw = headPitch = conversationWeight = 0f;
            greeted = false; IsWaving = false; WaveWeight = 0f;
            SmokeEffect?.StopAndClear();
        }

        private void ApplyHead(double time, float dt, bool handsFree, Vector3? target, bool seek)
        {
            float yaw = 0f, pitch = 0f;
            if (target.HasValue)
            {
                Vector3 direction = target.Value - actor.Head.position;
                Vector3 local = new Vector3(Vector3.Dot(direction, BodyRight),
                    Vector3.Dot(direction, actor.transform.up), Vector3.Dot(direction, BodyForward));
                yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -42f, 42f);
                pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -12f, 15f);
            }
            else
            {
                double period = 8.1d + role * .73d;
                double sample = time + role * 2.31d;
                float phase = (float)(sample % period);
                float envelope = Smooth(phase / .9f) * (1f - Smooth((phase - 2.4f) / 1.1f));
                yaw = (Jitter((long)(sample / period), role) * 2f - 1f) * (handsFree ? 24f : 5f) * envelope;
                pitch = Mathf.Sin((float)(sample % 80d) * .63f) * 3f * envelope;
            }
            headYaw = seek ? yaw : Mathf.MoveTowards(headYaw, yaw, dt * 38f);
            headPitch = seek ? pitch : Mathf.MoveTowards(headPitch, pitch, dt * 22f);
            actor.Head.rotation = Quaternion.AngleAxis(headYaw, actor.transform.up) *
                Quaternion.AngleAxis(headPitch, BodyRight) * actor.Head.rotation;
        }

        private void ApplyWave(float seconds)
        {
            float weight = WaveWeight;
            bool left = (role & 1) != 0;
            Transform hand = left ? leftHand : rightHand;
            Transform grip = left ? actor.LeftGrip : actor.RightGrip;
            float sign = left ? -1f : 1f;
            float swing = Mathf.Sin(Mathf.Max(0f, seconds - .45f) * 8f + role * .37f);
            // The wave stays visibly outside and above the hat. The palm's
            // normal comes from the real grip socket, fingers from hand.up.
            Vector3 target = BodyPoint(sign * (.46f + .065f * swing), 1.94f, .18f);
            Quaternion palmFrame = Quaternion.LookRotation(hand.InverseTransformDirection(grip.up), Vector3.up);
            Quaternion raised = Quaternion.AngleAxis(swing * 16f, BodyForward) *
                Quaternion.LookRotation(BodyForward, actor.transform.up) * Quaternion.Inverse(palmFrame);
            hand.rotation = Quaternion.Slerp(hand.rotation, raised, weight);
            SolveGestureArm(left, target, weight, BodyRight * sign - actor.transform.up * .45f + BodyForward * .15f);
        }

        private void ApplyConversation(double time, float weight)
        {
            float t = (float)(time % 60d) + role * 1.37f;
            bool left = (role & 1) != 0;
            float sign = left ? -1f : 1f;
            Vector3 point = BodyPoint(sign * (.28f + .07f * Mathf.Sin(t * 2.1f)),
                1.1f + .055f * Mathf.Sin(t * 2.8f), .28f + .065f * Mathf.Sin(t * 1.8f));
            SolveGestureArm(left, point, weight * .85f, BodyRight * sign - actor.transform.up * .6f);
            actor.Head.rotation = Quaternion.AngleAxis(Mathf.Sin(t * 3.3f) * 1.8f * weight,
                BodyRight) * actor.Head.rotation;
        }

        private void ApplyCoatAdjustment(double time)
        {
            float phase = (float)((time + role * 5.7d) % (19d + role * 1.3d));
            if (phase >= 2.9f) return;
            float weight = Smooth(phase / .7f) * Smooth((2.9f - phase) / .8f);
            Vector3 point = BodyPoint(.07f, 1.05f + .035f * Mathf.Sin(phase * 4f), .20f);
            SolveGestureArm(true, point, weight * .8f, -BodyRight - actor.transform.up * .7f);
        }

        private void ApplySmoke(float seconds, bool emit)
        {
            bool visible = seconds >= .65f && seconds < 8.3f;
            cigarette.SetVisible(visible);
            float lift = Smooth((seconds - .85f) / (SmokeDrawStartSeconds - .85f));
            float lower = 1f - Smooth((seconds - SmokeDrawEndSeconds) / 1.05f);
            float atLips = lift * lower;
            Vector3 outward = mouth.up.normalized;
            // Matching only the cigarette axis leaves its wrist twist free.
            // Pin the finger direction up/inward as well, so the wrist sits
            // outside the cheek and the elbow hangs below the shoulder.
            Vector3 fingers = Vector3.ProjectOnPlane(actor.transform.up * .72f - BodyRight * .69f, outward).normalized;
            Quaternion aligned = Quaternion.LookRotation(outward, fingers) *
                Quaternion.Inverse(Quaternion.LookRotation(cigaretteAxisInHand, Vector3.up));
            rightHand.rotation = Quaternion.Slerp(rightHand.rotation, aligned, atLips);
            Vector3 buttOffset = paper.TransformPoint(paperButt) - actor.RightGrip.position;
            Vector3 lipGrip = mouth.position + outward * .003f - buttOffset;
            Vector3 lowGrip = BodyPoint(.25f, .90f, .10f);
            float entry = Smooth(seconds / .65f) * Smooth((SmokeDurationSeconds - seconds) / .7f);
            SolveGestureArm(false, Vector3.Lerp(lowGrip, lipGrip, atLips), entry,
                BodyRight * 1.1f + BodyForward * .5f - actor.transform.up);
            float draw = Smooth((seconds - SmokeDrawStartSeconds) / .3f) *
                (1f - Smooth((seconds - SmokeDrawEndSeconds) / .35f));
            ember.GetPropertyBlock(properties);
            Color color = Color.Lerp(new Color(.35f, .10f, .025f), new Color(2.9f, .80f, .14f), draw);
            properties.SetColor(BaseColor, color); properties.SetColor(LegacyColor, color);
            ember.SetPropertyBlock(properties); properties.Clear();
            if (!exhaled && seconds >= SmokeExhaleSeconds)
            {
                exhaled = true;
                if (emit && seconds < SmokeExhaleEndSeconds) SmokeEffect.EmitManualBurst();
                SmokeEffect.Particles.Pause(true);
            }
        }

        private void StopSmoking()
        {
            smokeStart = -1d; exhaled = false;
            IsSmoking = MouthBusy = false;
            SmokeElapsedSeconds = -1f;
            cigarette?.SetVisible(false);
        }

        // NpcHumanV2's R joints are on source -X and its face is source -Y.
        // A Unity placement wrapper is not an anatomical frame. Measure that
        // frame from the imported shoulders instead of assuming +X is right.
        private Vector3 BodyRight => Vector3.ProjectOnPlane(rightUpper.position - leftUpper.position,
            actor.transform.up).normalized;
        private Vector3 BodyForward => Vector3.Cross(BodyRight, actor.transform.up).normalized;
        private Vector3 BodyPoint(float right, float height, float forward)
        {
            Vector3 scale = actor.transform.lossyScale;
            return actor.transform.position + BodyRight * (right * Mathf.Abs(scale.x)) +
                actor.transform.up * (height * Mathf.Abs(scale.y)) + BodyForward * (forward * Mathf.Abs(scale.z));
        }

        private void SolveGestureArm(bool left, Vector3 target, float weight, Vector3 elbowPole)
        {
            Transform upper = left ? leftUpper : rightUpper;
            Transform forearm = left ? leftForearm : rightForearm;
            Transform hand = left ? leftHand : rightHand;
            Transform grip = left ? actor.LeftGrip : actor.RightGrip;
            target = Vector3.Lerp(grip.position, target, Mathf.Clamp01(weight));
            Quaternion handRotation = hand.rotation;
            Vector3 offset = Quaternion.Inverse(handRotation) * (grip.position - hand.position);
            Vector3 shoulder = upper.position;
            Vector3 delta = target - handRotation * offset - shoulder;
            if (delta.sqrMagnitude < .000001f) return;
            float upperLength = Vector3.Distance(shoulder, forearm.position);
            float lowerLength = Vector3.Distance(forearm.position, hand.position);
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upperLength - lowerLength) + .001f,
                upperLength + lowerLength - .001f);
            Vector3 axis = delta.normalized;
            Vector3 wrist = shoulder + axis * distance;
            Vector3 bend = Vector3.ProjectOnPlane(elbowPole, axis).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(BodyForward, axis).normalized;
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            Vector3 elbow = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f,
                upperLength * upperLength - along * along));
            upper.rotation = Quaternion.FromToRotation(forearm.position - shoulder, elbow - shoulder) * upper.rotation;
            forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, wrist - forearm.position) * forearm.rotation;
            hand.rotation = handRotation;
        }

        private Transform Require(string name) => CityPedestrianHandProps.FindSocket(actor.ModelRoot, name)
            ?? throw new InvalidOperationException("Port worker is missing its ordinary rig socket " + name);

        private static float Smooth(float value)
        { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }

        private static float Jitter(long cycle, int role)
        {
            unchecked
            {
                uint value = (uint)cycle * 747796405u + (uint)(role + 1) * 2891336453u;
                value = ((value >> (int)((value >> 28) + 4)) ^ value) * 277803737u;
                return ((value >> 22) ^ value) / (float)uint.MaxValue;
            }
        }
    }
}
