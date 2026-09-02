using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The head of one man at a courtyard backgammon board: down over the
    /// pieces, then up and round to his neighbour, then down again.
    ///
    /// A late ADDITIVE turn, copied in shape from
    /// <see cref="MountainRoadCafeConversationLook"/>, which is this project's
    /// own answer to "a seated pair at a table who look at each other". The
    /// authored sit clip is evaluated first and this post-multiplies a
    /// clamped horizontal turn onto the neck and head it produced - it never
    /// writes an absolute pose, so the breath in the clip survives underneath.
    ///
    /// EXECUTION ORDER IS THE WHOLE THING. Every clip in the pedestrian bank
    /// keys all thirty-one pose bones, so the graph rewrites both of these
    /// transforms on every evaluation and a write that lands BEFORE it
    /// produces exactly nothing - not a subtle error, simply no motion at
    /// all. `CityCourtyardResidentPresentation` declares no execution order,
    /// so it advances the graph in the default `LateUpdate` slot and `350`
    /// puts this after it.
    ///
    /// NECK AND HEAD ONLY, and that is a deliberate limit rather than a first
    /// pass. An arm reaching for a piece would hover a hand over nine
    /// counters that are baked into a batched chunk mesh and cannot move; a
    /// spine lean would fight the sit clip for ownership of the same bone.
    /// Two men alternately studying a board and turning to each other reads
    /// as a game at the three or four metres a passer-by actually has, which
    /// is the same distance the cafe pair is read at.
    /// </summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class CityCourtyardResidentLook : MonoBehaviour
    {
        /// <summary>The furthest either man turns. Under the cafe's `62`: he
        /// is turning to somebody beside him across a small table, not across
        /// a room.</summary>
        public const float MaximumYawDegrees = 54f;

        /// <summary>How the turn is split. The cafe's exact share - a neck
        /// that carries under half of it is what stops the head reading as
        /// mounted on a swivel.</summary>
        public const float NeckShare = 0.42f;

        private Transform neck;
        private Transform head;
        private Transform partnerHead;
        private Transform board;
        private int seed;
        private bool isSecondSeat;
        private float elapsedSeconds;

        public bool IsInitialized { get; private set; }

        /// <summary>What was actually applied last frame, so a test can read
        /// the turn rather than infer it from a bone.</summary>
        public float LastAppliedYawDegrees { get; private set; }

        public float LastWeight { get; private set; }

        /// <summary>True while this man is bent over the pieces.</summary>
        public bool IsAtTheBoard =>
            CityCourtyardNardiExchange.IsAtTheBoard(
                elapsedSeconds,
                seed,
                isSecondSeat);

        public void Initialize(
            Transform neckBone,
            Transform headBone,
            Transform partnerHeadBone,
            Transform boardPoint,
            int pocketSeed,
            bool secondSeat)
        {
            neck = neckBone;
            head = headBone;
            partnerHead = partnerHeadBone;
            board = boardPoint;
            seed = pocketSeed;
            isSecondSeat = secondSeat;
            IsInitialized = neck != null && head != null;
            if (!IsInitialized)
            {
                // A rig without the two bones is not an error worth throwing
                // over - the body still sits there playing its clip - but it
                // must not silently pretend to be looking at anything.
                enabled = false;
            }
        }

        /// <summary>Advances the clock by hand, for a test with no frame
        /// loop.</summary>
        internal void AdvanceForTests(float deltaSeconds)
        {
            elapsedSeconds += deltaSeconds;
            Apply();
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            elapsedSeconds += Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            if (partnerHead == null && board == null)
            {
                return;
            }

            float weight = CityCourtyardNardiExchange.Evaluate(
                elapsedSeconds,
                seed,
                isSecondSeat);
            LastWeight = weight;

            // Where he is looking WHEN NOT TURNED is the board, so the turn
            // is measured between the two targets rather than from whatever
            // the clip happens to have left the head pointing at. A clip that
            // is re-authored later then moves the man's resting gaze without
            // moving this.
            Vector3 origin = head.position;
            Vector3 toBoard = board != null
                ? board.position - origin
                : head.forward;
            Vector3 toPartner = partnerHead != null
                ? partnerHead.position - origin
                : toBoard;
            float yaw = MountainRoadCafeConversationLook.ResolveYawDegrees(
                toBoard,
                toPartner);
            yaw = Mathf.Clamp(yaw, -MaximumYawDegrees, MaximumYawDegrees) *
                  weight;
            LastAppliedYawDegrees = yaw;
            if (Mathf.Abs(yaw) < 0.01f)
            {
                return;
            }

            Quaternion neckTurn = Quaternion.AngleAxis(
                yaw * NeckShare,
                Vector3.up);
            Quaternion headTurn = Quaternion.AngleAxis(
                yaw * (1f - NeckShare),
                Vector3.up);
            neck.rotation = neckTurn * neck.rotation;
            head.rotation = headTurn * head.rotation;
        }
    }
}
