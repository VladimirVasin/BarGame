using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>The seated shift boss. The port owns time and all speech; this actor owns only his pose.</summary>
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed partial class CityPortForeman : MonoBehaviour, ISpeechFaceActor
    {
        public const string ResourcePath = "City/Port/Foreman/PortForemanActor";
        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private AnimationClip seatedIdle, seatedGrumble;
        [SerializeField] private AnimationClip seatedListen, seatedTalk;
        [SerializeField] private AnimationClip[] snackActions = Array.Empty<AnimationClip>();
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private Color[] colors = Array.Empty<Color>();
        [SerializeField] private Texture2D atlas;
        private CityPortController port;
        private CityPortCrew crew;
        private CityPortConversationController conversation;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable[] playables;
        private AnimationClip[] poseClips;
        private readonly CityPortForemanSnackTimeline snackTimeline = new CityPortForemanSnackTimeline();
        private double previousSeconds = double.NaN, speechStarted, dialogueStarted;
        private int speechPartner = -1;
        private bool speaking;
        private float speechWeight, conversationWeight, lookYaw, lookPitch;
        private Collider[] solids;
        private Renderer[] carrotBites;
        private Renderer carrotStem, carrotGreens;
        private Transform[] carrotTips;
        private Transform thrownStem, throwRelease, binTarget;
        private int snackPose;
        private double snackPoseSeconds;
        private readonly SpeechFaceAtlasPresenter facePresenter = new SpeechFaceAtlasPresenter();
        private object speechFaceOwner;
        private SpeechFacePose speechFacePose;
        public bool HasSpeechFace => speechFaceOwner != null;
        public bool UsesSpriteFace => facePresenter.IsConfigured;
        public SpeechFacePose CurrentSpeechFace { get; private set; }
        public Transform ModelRoot => modelRoot;
        public Transform Head { get; private set; }
        public Transform RightHand { get; private set; }
        public Transform LeftFoot { get; private set; }
        public Transform RightFoot { get; private set; }
        public Transform Seat { get; private set; }
        public Transform CarrotTip { get; private set; }
        public Transform Mouth { get; private set; }
        public Transform BiteTip => carrotTips[Mathf.Clamp(Snack.BitesTaken, 0, 3)];
        public Transform ThrownStem => thrownStem;
        public Transform BinTarget => binTarget;
        public CityPortForemanSnackSnapshot Snack => snackTimeline.Current;
        public int VisibleCarrotBites => (carrotBites[0].enabled ? 1 : 0) +
            (carrotBites[1].enabled ? 1 : 0) + (carrotBites[2].enabled ? 1 : 0);
        public bool IsSpeaking => speaking;
        public float GestureWeight => speechWeight;
        public float EatingWeight => 1f - conversationWeight;
        public string ConversationClipName => speechPartner == -1 ? string.Empty : speechPartner == -2
            ? speaking ? "SeatedTalk" : "SeatedListen" : "SeatedGrumble";
        public CityPortForemanInteraction Interaction { get; private set; }

        public void Configure(Animator configuredAnimator, Transform model, AnimationClip idle,
            AnimationClip grumble, AnimationClip[] foodActions, Renderer[] meshes, Color[] partColors, Texture2D texture,
            AnimationClip dialogueListen, AnimationClip dialogueTalk)
        {
            animator = configuredAnimator; modelRoot = model; seatedIdle = idle; seatedGrumble = grumble;
            seatedListen = dialogueListen; seatedTalk = dialogueTalk;
            snackActions = foodActions;
            renderers = meshes; colors = partColors; atlas = texture;
        }

        public static CityPortForeman Build(Transform parent, CityPortController port,
            CityPortCrew crew, CityPortConversationController conversation)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null) throw new InvalidOperationException("The dock foreman needs his authored model and seated clips.");
            GameObject host = Instantiate(prefab, parent, false);
            host.name = "Port Foreman";
            host.transform.SetPositionAndRotation(port.Plan.ForemanSeatWorld, port.Plan.ForemanFacing);
            var foreman = host.GetComponent<CityPortForeman>();
            if (foreman == null) throw new InvalidOperationException("Missing foreman presentation bindings.");
            foreman.port = port; foreman.crew = crew; foreman.conversation = conversation;
            foreman.InitializePose();
            NpcNameplateTarget.Attach(host, "npc.name.foreman", foreman.Head, "foreman");
            crew.RegisterConversationForeman(foreman.Head);
            conversation.RegisterForeman(foreman, foreman.Head, foreman.SetSpeechPose);
            foreman.Interaction = host.GetComponent<CityPortForemanInteraction>();
            foreman.Interaction.Initialize(foreman, conversation);
            foreman.ApplyAt(crew.LifeElapsedSeconds);
            return foreman;
        }

        public void InitializePose()
        {
            if (graph.IsValid()) return;
            if (animator == null || modelRoot == null || seatedIdle == null || seatedGrumble == null ||
                seatedListen == null || seatedTalk == null || snackActions.Length != 5)
                throw new InvalidOperationException("Incomplete seated foreman rig.");
            Transform Find(string name) => CityPedestrianHandProps.FindSocket(modelRoot, name)
                ?? throw new InvalidOperationException("Missing foreman joint or contact: " + name);
            Head = Find("head"); RightHand = Find("hand.R");
            solids = GetComponents<Collider>();
            LeftFoot = Find("foot.L"); RightFoot = Find("foot.R"); Seat = Find("ANCHOR_ForemanSeat");
            CarrotTip = Find("ANCHOR_ForemanCarrotTip"); Mouth = Find("ANCHOR_ForemanBiteMouth");
            carrotTips = new[] { CarrotTip, Find("ANCHOR_ForemanCarrotTip2"),
                Find("ANCHOR_ForemanCarrotTip3"), Find("ANCHOR_ForemanCarrotStemTip") };
            carrotBites = new Renderer[3];
            for (int i = 0; i < 3; i++) carrotBites[i] = Find("FOOD_ForemanCarrotBite" + (i + 1)).GetComponent<Renderer>();
            carrotStem = Find("FOOD_ForemanCarrotStem").GetComponent<Renderer>();
            carrotGreens = Find("FOOD_ForemanCarrotGreens").GetComponent<Renderer>();
            thrownStem = Find("MOVE_ForemanThrownStem");
            throwRelease = Find("ANCHOR_ForemanThrowRelease"); binTarget = Find("ANCHOR_ForemanBinTarget");
            var properties = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                properties.SetColor("_BaseColor", colors[i]); properties.SetColor("_Color", colors[i]);
                properties.SetTexture("_BaseMap", atlas); properties.SetTexture("_MainTex", atlas);
                renderers[i].SetPropertyBlock(properties);
            }
            if (!facePresenter.Configure(Find("GEO_FaceSurface").GetComponent<Renderer>(),
                    SpeechFaceAtlasResources.ForemanPath, false))
                throw new InvalidOperationException("The foreman requires his authored dialogue face atlas.");
            InitializeChin();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("PortForeman.Seated");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            poseClips = new[] { seatedIdle, seatedGrumble, snackActions[0], snackActions[1],
                snackActions[2], snackActions[3], snackActions[4], seatedGrumble, seatedTalk, seatedListen };
            mixer = AnimationMixerPlayable.Create(graph, poseClips.Length);
            playables = new AnimationClipPlayable[poseClips.Length];
            for (int i = 0; i < poseClips.Length; i++)
            {
                if (poseClips[i] == null) throw new InvalidOperationException("Missing foreman snack action " + i);
                playables[i] = AnimationClipPlayable.Create(graph, poseClips[i]);
                playables[i].SetApplyFootIK(false); playables[i].SetApplyPlayableIK(false); playables[i].SetSpeed(0);
                graph.Connect(playables[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Seated body", animator).SetSourcePlayable(mixer);
            graph.Play();
            ApplyAt(0d);
        }

        private void SetSpeechPose(int partner, bool isSpeaking)
        {
            if (isSpeaking && !speaking) speechStarted = crew.LifeElapsedSeconds;
            if (partner == -2 && speechPartner != -2) dialogueStarted = crew.LifeElapsedSeconds;
            speechPartner = partner; speaking = isSpeaking;
        }

        private void LateUpdate()
        {
            if (crew != null) ApplyAt(crew.LifeElapsedSeconds);
        }

        public void ApplyAt(double seconds)
        {
            if (!graph.IsValid()) return;
            bool visible = port == null || port.ShorePresentationActive;
            if (modelRoot.gameObject.activeSelf != visible) modelRoot.gameObject.SetActive(visible);
            foreach (Collider solid in solids) solid.enabled = visible;
            double step = seconds - previousSeconds;
            bool seek = double.IsNaN(previousSeconds) || step < 0d || step > 2d;
            previousSeconds = seconds;
            float dt = seek ? 0f : (float)step;
            bool conversing = speechPartner != -1;
            CityPortForemanSnackSnapshot snack = snackTimeline.Advance(seconds, conversing);
            conversation?.SetForemanState(visible && isActiveAndEnabled, visible && isActiveAndEnabled && snack.CanTalk);
            if (seek || !visible)
            { speechWeight = conversationWeight = 0f; lookYaw = lookPitch = 0f; snackPose = 0; snackPoseSeconds = 0d; }
            if (!visible) return;
            speechWeight = Mathf.MoveTowards(speechWeight, speaking ? 1f : 0f, dt * 3f);
            conversationWeight = Mathf.MoveTowards(conversationWeight, conversing ? 1f : 0f, dt * 4f);
            // Freeze the outgoing snack pose while it blends to conversation.
            // The timeline preserves committed bites and restarts only an unfinished bite.
            if (!conversing)
            {
                snackPose = snack.Phase == CityPortForemanSnackPhase.Idle ? 0 : (int)snack.Phase + 1;
                snackPoseSeconds = snack.ActionSeconds;
            }
            playables[snackPose].SetTime(snackPose == 0 ? snackPoseSeconds % seatedIdle.length :
                Math.Min(snackPoseSeconds, poseClips[snackPose].length));
            playables[1].SetTime(Math.Max(0d, seconds - speechStarted) % seatedGrumble.length);
            playables[8].SetTime(Math.Max(0d, seconds - speechStarted) % seatedTalk.length);
            playables[9].SetTime(Math.Max(0d, seconds - dialogueStarted) % seatedListen.length);
            for (int i = 0; i < poseClips.Length; i++) mixer.SetInputWeight(i, 0f);
            mixer.SetInputWeight(snackPose, 1f - conversationWeight);
            mixer.SetInputWeight(speechPartner == -2 ? 8 : 1, conversationWeight * speechWeight);
            mixer.SetInputWeight(speechPartner == -2 ? 9 : 7, conversationWeight * (1f - speechWeight));
            graph.Evaluate(0f);
            PresentCarrot(snack);
            Transform target = speechPartner >= 0 ? crew?.GetConversationHead(speechPartner) :
                speechPartner == -2 ? Interaction?.Listener : null;
            float yaw = 0f, pitch = 0f;
            if (target != null && conversationWeight >= .99f)
            {
                Vector3 direction = transform.InverseTransformDirection(target.position - Head.position);
                yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -38f, 38f);
                // The hero stands while the foreman remains on his stool.
                // Only this owned dialogue adds elevation; ambient exchanges
                // retain their original horizontal head turn.
                if (speechPartner == -2)
                    pitch = Mathf.Clamp(-Mathf.Atan2(direction.y,
                        Mathf.Max(.01f, new Vector2(direction.x, direction.z).magnitude)) * Mathf.Rad2Deg, -25f, 12f);
            }
            lookYaw = Mathf.MoveTowards(lookYaw, yaw, dt * 75f);
            lookPitch = Mathf.MoveTowards(lookPitch, pitch, dt * 55f);
            Head.rotation = Quaternion.AngleAxis(lookYaw, transform.up) *
                Quaternion.AngleAxis(lookPitch, transform.right) * Head.rotation;
            PresentFace(seconds, snack, conversing);
            UpdateChin(seconds);
        }

        public bool TrySetSpeechFace(object owner, SpeechFacePose pose)
        {
            if (owner == null || !isActiveAndEnabled || !facePresenter.IsConfigured ||
                (speechFaceOwner != null && !ReferenceEquals(speechFaceOwner, owner))) return false;
            speechFaceOwner = owner; speechFacePose = pose;
            ApplyFace(pose);
            return true;
        }

        public void ReleaseSpeechFace(object owner)
        {
            if (owner == null || !ReferenceEquals(speechFaceOwner, owner)) return;
            speechFaceOwner = null;
            ApplyFace(SpeechFaceAnimation.ResolveListening(SpeechFaceProfile.Foreman,
                crew != null ? crew.LifeElapsedSeconds : 0d));
        }

        private void PresentFace(double seconds, in CityPortForemanSnackSnapshot snack, bool conversing)
        {
            if (speechFaceOwner != null) { ApplyFace(speechFacePose); return; }
            if (conversing && conversation != null && conversation.Bubbles != null &&
                conversation.Bubbles.TryGetSpeechFaceSample(this, out SpeechFaceSample sample))
            { ApplyFace(SpeechFaceAnimation.Resolve(sample, SpeechFaceProfile.Foreman, seconds)); return; }
            SpeechFacePose rest = SpeechFaceAnimation.ResolveListening(SpeechFaceProfile.Foreman, seconds);
            SpeechMouthPose mouth = SpeechMouthPose.Closed;
            if (!conversing && snack.Phase >= CityPortForemanSnackPhase.Bite1 &&
                snack.Phase <= CityPortForemanSnackPhase.Bite3)
            {
                double bite = snack.ActionSeconds;
                if (bite >= 1.3d && bite < CityPortForemanSnackTimeline.BiteCommitSeconds)
                    mouth = SpeechMouthPose.Open;
                else if (bite >= CityPortForemanSnackTimeline.BiteCommitSeconds && bite < 3.3d)
                    mouth = ((int)((bite - CityPortForemanSnackTimeline.BiteCommitSeconds) * 5.6d) & 1) == 0
                        ? SpeechMouthPose.Narrow : SpeechMouthPose.Closed;
            }
            ApplyFace(new SpeechFacePose(mouth, rest.Expression));
        }

        private void ApplyFace(SpeechFacePose pose)
        {
            if (facePresenter.Apply(pose)) CurrentSpeechFace = pose;
        }

        private void PresentCarrot(in CityPortForemanSnackSnapshot snack)
        {
            for (int i = 0; i < carrotBites.Length; i++)
                carrotBites[i].enabled = snack.HoldingCarrot && snack.BitesTaken <= i;
            carrotStem.enabled = carrotGreens.enabled = snack.HoldingCarrot;
            thrownStem.gameObject.SetActive(snack.StemInFlight);
            if (!snack.StemInFlight) return;
            float t = Mathf.Clamp01((float)snack.ThrowProgress);
            thrownStem.SetPositionAndRotation(Vector3.Lerp(throwRelease.position, binTarget.position, t) +
                Vector3.up * (.22f * 4f * t * (1f - t)),
                Quaternion.AngleAxis(t * 150f, transform.right) * throwRelease.rotation);
        }

        private void OnDisable()
        {
            ResetChin();
            speechFaceOwner = null;
            ApplyFace(new SpeechFacePose(SpeechMouthPose.Closed, SpeechFaceExpression.Rest));
            conversation?.SetForemanState(false, false);
            speaking = false; speechPartner = -1; speechWeight = conversationWeight = 0f; lookYaw = lookPitch = 0f; previousSeconds = double.NaN;
            Interaction?.Cancel();
        }

        private void OnDestroy()
        {
            facePresenter.Clear();
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
