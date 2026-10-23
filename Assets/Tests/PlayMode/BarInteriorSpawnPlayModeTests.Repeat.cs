using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Runtime.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class BarInteriorSpawnPlayModeTests
    {
        private sealed class BarServiceCaptureSkinScope : IDisposable
        {
            private readonly List<SkinnedMeshRenderer> skins = new();
            private readonly List<bool> previous = new();
            internal BarServiceCaptureSkinScope(BarInteriorRoot bar)
            {
                Add(bar.Bartender.transform);
                Add(bar.Player.GameObject.transform);
                foreach (BarPatron patron in bar.Patrons)
                    if (patron.Anchor.Role == BarNpcRole.CounterPatron) Add(patron.Registry.transform);
            }
            private void Add(Transform root)
            {
                foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skins.Add(skin); previous.Add(skin.forceMatrixRecalculationPerRender);
                    skin.forceMatrixRecalculationPerRender = true;
                }
            }
            public void Dispose()
            {
                for (int index = 0; index < skins.Count; index++)
                    if (skins[index] != null) skins[index].forceMatrixRecalculationPerRender = previous[index];
            }
        }

        private sealed class CounterPatronQueueProof : IDisposable
        {
            private readonly BarInteriorRoot bar;
            private readonly BarCounterPatronRefill[] requests = new BarCounterPatronRefill[2];
            private readonly BarDrinkVesselView[] vessels = new BarDrinkVesselView[2];
            private readonly Pose[] resting = new Pose[2];
            private readonly Transform[] leftHands = new Transform[2];
            private readonly Vector3[] initialHands = new Vector3[2];
            private readonly Transform[] rightHands = new Transform[2], rightElbows = new Transform[2], rightShoulders = new Transform[2];
            private readonly Transform[] leftShoulders = new Transform[2];
            private readonly Vector3[] rightSupport = new Vector3[2];
            private readonly Vector3[] previousCups = new Vector3[2];
            private readonly bool[] previousCupReturn = new bool[2];
            private readonly BarDrinkServicePhase[] previousCupPhase = new BarDrinkServicePhase[2];
            private readonly bool[] cupOfferCaptured = new bool[2], cupPullCaptured = new bool[2];
            private readonly bool[] rightSupportRestored = new bool[2];
            private readonly bool[] previouslyEnabled = new bool[2], attentionCaptured = new bool[2], refillCaptured = new bool[2];
            private readonly List<Object> owners = new();
            private readonly Transform[] acknowledgementRoots = new Transform[3];
            private readonly AcknowledgementEvidence[] acknowledgements = { new(), new(), new() };
            private readonly int initialAcknowledgementCount;
            private readonly WarehouseRefrigeratorPresentationProbe probe;
            private Object previousOwner;
            private long previousSequence;
            private int previousCompleted;
            private BarCounterPatronRefill first, second;
            private bool sawHeroQueued, sawQueuedHeroAcknowledgement;
            private readonly string[] transitions = new string[16];
            private int transitionNext, transitionCount;
            private Object tracedOwner;
            private BarDrinkServicePhase tracedPhase;
            private BarBartenderTowelState tracedTowel;
            private bool hasTrace, tracedReady, tracedBottleAttached, tracedBottleVisible;

            private sealed class AcknowledgementEvidence
            {
                internal float EarlyPitch = float.PositiveInfinity, PeakPitch = float.NegativeInfinity;
                internal float ReturnedPitch = float.PositiveInfinity, BoneMotion;
                internal Quaternion EarlyHead, EarlyNeck;
                internal bool HasEarlyPose, Looked, Captured;
            }

            internal CounterPatronQueueProof(BarInteriorRoot bar)
            {
                this.bar = bar;
                PrepareCounterDrinks(bar);
                int count = 0;
                foreach (BarPatron patron in bar.Patrons)
                {
                    if (patron.Anchor.Role != BarNpcRole.CounterPatron) continue;
                    Assert.That(count, Is.LessThan(2));
                    BarCounterPatronRefill refill = patron.Refill;
                    Assert.That(refill, Is.Not.Null);
                    requests[count] = refill; vessels[count] = refill.Vessel;
                    acknowledgementRoots[count] = refill.transform;
                    resting[count] = new Pose(refill.Vessel.transform.position, refill.Vessel.transform.rotation);
                    previousCups[count] = refill.Vessel.transform.position;
                    previouslyEnabled[count] = refill.enabled;
                    refill.enabled = false; // This scope owns their explicit setup clock.
                    foreach (NpcHandPose.HandBinding hand in patron.Drinking.HandPose.Hands)
                        if (hand.IsLeft) leftHands[count] = hand.Hand;
                        else rightHands[count] = hand.Hand;
                    Assert.That(leftHands[count], Is.Not.Null);
                    Assert.That(rightHands[count], Is.Not.Null);
                    initialHands[count] = leftHands[count].position;
                    rightSupport[count] = rightHands[count].position;
                    rightElbows[count] = FindRequiredDescendant(patron.Registry.ModelRoot, BarPatronWorldBuilder.RightForearmBoneName);
                    rightShoulders[count] = FindRequiredDescendant(patron.Registry.ModelRoot, BarPatronWorldBuilder.RightUpperArmBoneName);
                    leftShoulders[count] = FindRequiredDescendant(patron.Registry.ModelRoot, BarPatronWorldBuilder.LeftUpperArmBoneName);
                    count++;
                }
                Assert.That(count, Is.EqualTo(2));
                Assert.That(requests[0].DepletionPerSipSecond, Is.GreaterThan(0f));
                Assert.That(requests[1].DepletionPerSipSecond, Is.GreaterThan(0f));
                Assert.That(requests[0].DepletionPerSipSecond, Is.Not.EqualTo(requests[1].DepletionPerSipSecond));
                acknowledgementRoots[2] = bar.Player.GameObject.transform;
                initialAcknowledgementCount = bar.Bartender.AcknowledgementCount;
                previousCompleted = bar.DrinkShop.ServiceQueue.CompletedCount;
                probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            }

            private static void PrepareCounterDrinks(BarInteriorRoot bar)
            {
                // The visit's shuffled bag may contain no counter Pint. Give
                // this regression one Pint and one non-beer route through the
                // same production builder, without changing its random pool.
                var patrons = bar.Patrons as IList<BarPatron>;
                Assert.That(patrons, Is.Not.Null);
                var attach = typeof(BarPatronWorldBuilder).GetMethod("TryAttachCounterDrinking",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.That(attach, Is.Not.Null);
                int slot = 0;
                for (int index = 0; index < patrons.Count; index++)
                {
                    BarPatron patron = patrons[index];
                    if (patron.Anchor.Role != BarNpcRole.CounterPatron) continue;
                    BarPatronDrinkPropKind kind = slot == 0 ? BarPatronDrinkPropKind.Pint :
                        patron.Drinking.DrinkProp.Kind == BarPatronDrinkPropKind.Pint
                            ? BarPatronDrinkPropKind.WineGlass : patron.Drinking.DrinkProp.Kind;
                    if (patron.Drinking.DrinkProp.Kind != kind)
                    {
                        AnimationClip clip = patron.Drinking.AuthoredDrinkClip;
                        Transform oldProp = patron.Drinking.DrinkProp.Root;
                        Object.DestroyImmediate(patron.Refill);
                        Object.DestroyImmediate(patron.Drinking);
                        Object.DestroyImmediate(oldProp.gameObject);
                        var drinking = (BarPatronDrinkingArmPose)attach.Invoke(null, new object[] {
                            patron.Anchor, patron.Registry, patron.Presentation, clip, bar.Layout, index + 1,
                            bar.Room.Find("Counter Top").GetComponent<Renderer>(), kind });
                        Assert.That(drinking, Is.Not.Null);
                        var refill = patron.Registry.gameObject.AddComponent<BarCounterPatronRefill>();
                        refill.Configure(drinking, bar.DrinkShop, slot);
                        patrons[index] = new BarPatron(patron.Anchor, patron.Registry, patron.Presentation, patron.IsSeated, drinking);
                    }
                    slot++;
                }
            }

            internal IEnumerator StageRequests()
            {
                Assert.That(bar.DrinkShop.ServiceQueue.IsBusy, Is.False);
                foreach (BarCounterPatronRefill refill in requests)
                    for (int boundary = 0; boundary < 4 && refill.Drinking.Timeline.Phase != BarPatronDrinkPhase.Rest; boundary++)
                        refill.Drinking.Timeline.Advance(refill.Drinking.Timeline.PhaseDuration - refill.Drinking.Timeline.PhaseElapsed + .0001f);
                yield return AtFinalBarPose(probe, () =>
                {
                    for (int index = 0; index < requests.Length; index++)
                    {
                        Assert.That(requests[index].Drinking.Timeline.Phase, Is.EqualTo(BarPatronDrinkPhase.Rest));
                        rightSupport[index] = rightHands[index].position;
                        initialHands[index] = leftHands[index].position;
                        resting[index] = new Pose(vessels[index].transform.position, vessels[index].transform.rotation);
                        previousCups[index] = vessels[index].transform.position;
                    }
                });
                var emptySteps = new[] { -1, -1 };
                for (int step = 0; step < 4000 && (emptySteps[0] < 0 || emptySteps[1] < 0); step++)
                    for (int index = 0; index < 2; index++)
                        if (emptySteps[index] < 0)
                        {
                            requests[index].Advance(.25f);
                            if (requests[index].FillProgress <= 0f) emptySteps[index] = step;
                        }
                Assert.That(emptySteps[0], Is.GreaterThanOrEqualTo(0));
                Assert.That(emptySteps[1], Is.GreaterThanOrEqualTo(0));
                Assert.That(emptySteps[0], Is.Not.EqualTo(emptySteps[1]), "The two guests must empty their actual drinks at staggered times.");
                probe.Schedule(Observe);
                int startFrame = Time.frameCount;
                float startedAt = Time.realtimeSinceStartup;
                while (!(bar.DrinkShop.ServiceQueue.QueuedCount == 1 &&
                         requests[0].IsWaitingForRefill && requests[1].IsWaitingForRefill &&
                         !requests[0].IsAttentionActive && !requests[1].IsAttentionActive) &&
                       probe.Failure == null && HasBarSimulationBudget(startFrame, 480, startedAt, 30f))
                {
                    foreach (BarCounterPatronRefill refill in requests)
                        if (!refill.IsRefilling && (!refill.IsWaitingForRefill || refill.IsAttentionActive)) refill.Advance(.025f);
                    yield return null;
                }
                AssertHealthy();
                first = bar.DrinkShop.ServiceQueue.CurrentOwner as BarCounterPatronRefill;
                Assert.That(first, Is.Not.Null);
                second = ReferenceEquals(first, requests[0]) ? requests[1] : requests[0];
                Assert.That(bar.DrinkShop.ServiceQueue.QueuedCount, Is.EqualTo(1));
                Assert.That(attentionCaptured[0] && attentionCaptured[1], Is.True, "Both empty glasses need a visible attention gesture before requesting service.");
                foreach (BarCounterPatronRefill refill in requests)
                    Assert.That(refill.AttentionCount, Is.EqualTo(1), "A waiting patron must not flood the FIFO.");
                // Cancelling one queued visitor must preserve the active
                // service; the same still-empty visitor may request again.
                long activeSequence = bar.DrinkShop.ServiceQueue.CurrentSequence;
                bar.DrinkShop.CancelCounterPatron(second);
                Assert.That(bar.DrinkShop.ServiceQueue.QueuedCount, Is.Zero);
                Assert.That(bar.DrinkShop.ServiceQueue.IsOwner(first), Is.True);
                Assert.That(bar.DrinkShop.ServiceQueue.CurrentSequence, Is.EqualTo(activeSequence));
                Assert.That(bar.DrinkShop.QueuePatronRefill(second), Is.True);
                int acceptedAcknowledgements = bar.Bartender.AcknowledgementCount;
                Assert.That(bar.DrinkShop.QueuePatronRefill(second), Is.True);
                Assert.That(bar.DrinkShop.ServiceQueue.QueuedCount, Is.EqualTo(1), "A duplicate visitor request must remain one queue entry.");
                Assert.That(bar.Bartender.AcknowledgementCount, Is.EqualTo(acceptedAcknowledgements), "A duplicate request must not restart the bartender's acknowledgement.");
            }

            private void Observe()
            {
                BarDrinkShopController shop = bar.DrinkShop;
                BarBartenderServiceQueue queue = shop.ServiceQueue;
                Object owner = queue.CurrentOwner;
                RecordTransition();
                if (!ReferenceEquals(owner, previousOwner))
                {
                    if (previousOwner != null)
                        Assert.That(queue.CompletedCount, Is.GreaterThan(previousCompleted), "Another request preempted an unfinished bartender service.");
                    if (owner != null)
                    {
                        Assert.That(queue.CurrentSequence, Is.GreaterThan(previousSequence), "Dispatch must retain FIFO sequence.");
                        owners.Add(owner); previousSequence = queue.CurrentSequence;
                    }
                    previousOwner = owner; previousCompleted = queue.CompletedCount;
                }
                sawHeroQueued |= shop.IsHeroServiceQueued;
                ObserveAcknowledgement();
                for (int index = 0; index < 2; index++)
                {
                    BarCounterPatronRefill refill = requests[index];
                    Assert.That(refill.Vessel, Is.SameAs(vessels[index]), "A refill must retain the guest's existing glass.");
                    if (refill.CompletedRefills > 0 && !rightSupportRestored[index])
                    {
                        Assert.That(Vector3.Distance(rightHands[index].position, rightSupport[index]), Is.LessThan(.06f),
                            "The guest must recover the original final right-hand support before the next order.");
                        rightSupportRestored[index] = true;
                    }
                    if (refill.IsWaitingForRefill || refill.IsRefilling)
                    {
                        Vector3 support = refill.IsRefilling ? refill.Drinking.CounterRightSupportPosition : rightSupport[index];
                        float supportError = Vector3.Distance(rightHands[index].position, support);
                        float elbowBend = Vector3.Angle(rightElbows[index].position - rightShoulders[index].position,
                            rightHands[index].position - rightElbows[index].position);
                        bool shortReach = refill.IsRefilling && (refill.Drinking.CounterCupOfferOffset.sqrMagnitude > .000001f ||
                            ReferenceEquals(shop.ActivePatronRefill, refill) && (shop.IsPatronCupOfferActive || shop.IsPatronCupReturnActive));
                        Vector3 shoulderMiddle = (rightShoulders[index].position + leftShoulders[index].position) * .5f;
                        float elbowSide = Vector3.Dot(rightElbows[index].position - shoulderMiddle,
                            (rightShoulders[index].position - leftShoulders[index].position).normalized);
                        float elbowHeight = Vector3.Dot(rightElbows[index].position - rightShoulders[index].position, refill.transform.up);
                        bool invalidElbow = shortReach ? elbowSide <= 0f || elbowHeight >= 0f : elbowBend <= 20f;
                        string details = $"Counter guest {index + 1}: attention={refill.IsAttentionActive}, refilling={refill.IsRefilling}, " +
                            $"rightSupportError={supportError:F5}m, elbowBend={elbowBend:F3}deg, shortReach={shortReach}, elbowSide/height={elbowSide:F5}/{elbowHeight:F5}, " +
                            $"rest={rightSupport[index]:F5}, commanded={support:F5}, wrist={rightHands[index].position:F5}, vessel={refill.Vessel.transform.position:F5}.";
                        if (supportError >= .06f || invalidElbow)
                            CaptureBarWorkPose(bar, index == 0 ? "Test first guest right arm support failure" : "Test second guest right arm support failure", rightHands[index].position);
                        Assert.That(supportError, Is.LessThan(.06f), "The waiting right hand must follow only its commanded short offer/support pose. " + details);
                        if (shortReach)
                        {
                            Assert.That(elbowSide, Is.GreaterThan(0f), "The reaching elbow must remain on the anatomical right of the torso. " + details);
                            Assert.That(elbowHeight, Is.LessThan(0f), "The short cup reach must keep the elbow below its shoulder. " + details);
                        }
                        else Assert.That(elbowBend, Is.GreaterThan(20f), "The resting right arm must remain naturally bent. " + details);
                    }
                    ObservePatronCupOffer(index);
                    previousCups[index] = refill.Vessel.transform.position;
                    previousCupReturn[index] = ReferenceEquals(shop.ActivePatronRefill, refill) && shop.IsPatronCupReturnActive;
                    previousCupPhase[index] = shop.Phase;
                    float attentionProgress = refill.AttentionElapsedSeconds / BarRepeatGesture.EnterSeconds;
                    if (refill.IsAttentionActive && !attentionCaptured[index] &&
                        attentionProgress >= .30f && attentionProgress <= .55f &&
                        leftHands[index].position.y > initialHands[index].y + .08f &&
                        Vector3.Distance(leftHands[index].position, initialHands[index]) > .06f)
                    {
                        attentionCaptured[index] = true;
                        CaptureBarWorkPose(bar, index == 0 ? "Test first counter guest attention" : "Test second counter guest attention", leftHands[index].position);
                    }
                    if (ReferenceEquals(shop.ActivePatronRefill, refill) && !refillCaptured[index] &&
                        refill.Vessel.FillProgress > .1f && refill.Vessel.FillProgress < .9f)
                    {
                        refillCaptured[index] = true;
                        CaptureBarWorkPose(bar, index == 0 ? "Test first counter guest refill" : "Test second counter guest refill", refill.Vessel.transform.position);
                        CaptureBarWorkPose(bar, index == 0 ? "Test first guest right hand supported during refill" : "Test second guest right hand supported during refill", rightHands[index].position);
                    }
                }
                probe.Schedule(Observe);
            }

            internal IEnumerator WaitForHeroDispatch()
            {
                Assert.That(bar.DrinkShop.IsHeroServiceQueued, Is.True, "The accepted hero order must wait behind the guests already in the FIFO.");
                int startFrame = Time.frameCount;
                float startedAt = Time.realtimeSinceStartup;
                while (!bar.DrinkShop.ServiceQueue.IsOwner(bar.DrinkShop) && probe.Failure == null &&
                       HasBarSimulationBudget(startFrame, 2400, startedAt, 60f)) yield return null;
                // Include the grant's final presentation in the observer.
                yield return null;
                AssertHealthy();
                if (!bar.DrinkShop.ServiceQueue.IsOwner(bar.DrinkShop))
                {
                    RecordTransition(force: true);
                    var ordered = new string[transitionCount];
                    for (int index = 0; index < ordered.Length; index++)
                        ordered[index] = transitions[(transitionNext - transitionCount + index + transitions.Length) % transitions.Length];
                    Assert.Fail("The queued hero never received bartender ownership. Recent final-pose transitions:\n" + string.Join("\n", ordered));
                }
                Assert.That(sawHeroQueued, Is.True);
                Assert.That(bar.Bartender.AcknowledgementCount, Is.EqualTo(initialAcknowledgementCount + 4),
                    "Acknowledge both guests, the cancelled/reaccepted request and the accepted hero exactly once each.");
                Assert.That(sawQueuedHeroAcknowledgement, Is.True, "The bartender must acknowledge the hero before his queued service starts.");
                for (int index = 0; index < acknowledgements.Length; index++)
                {
                    AcknowledgementEvidence evidence = acknowledgements[index];
                    string label = index == 2 ? "hero" : "counter guest " + (index + 1);
                    Assert.That(evidence.Looked && evidence.Captured && evidence.HasEarlyPose, Is.True, "Missing real look/nod for " + label);
                    Assert.That(evidence.PeakPitch - evidence.EarlyPitch, Is.GreaterThan(3f), "The actual face did not nod down for " + label);
                    Assert.That(evidence.PeakPitch - evidence.ReturnedPitch, Is.GreaterThan(3f), "The actual face did not return from its nod for " + label);
                    Assert.That(evidence.BoneMotion, Is.GreaterThan(2f), "The visible head/neck did not move for " + label);
                }
                CollectionAssert.AreEqual(new Object[] { first, second, bar.DrinkShop }, owners);
                for (int index = 0; index < 2; index++)
                {
                    Assert.That(requests[index].CompletedRefills, Is.EqualTo(1));
                    Assert.That(requests[index].FillProgress, Is.GreaterThanOrEqualTo(.999f));
                    Assert.That(refillCaptured[index], Is.True, "Observe the actual liquid rising during each guest service.");
                    Assert.That(Vector3.Distance(vessels[index].transform.position, resting[index].position), Is.LessThan(.005f));
                    Assert.That(rightSupportRestored[index], Is.True, "Observe the restored right support after the guest's final contact solve.");
                    if (vessels[index].Kind == BarDrinkVesselKind.Pint)
                        Assert.That(cupOfferCaptured[index] && cupPullCaptured[index], Is.True, "The same Pint needs an actual offered and pulled-back pose.");
                }
            }

            private void ObservePatronCupOffer(int index)
            {
                BarDrinkShopController shop = bar.DrinkShop;
                BarCounterPatronRefill refill = requests[index];
                if (!ReferenceEquals(shop.ActivePatronRefill, refill) || vessels[index].Kind != BarDrinkVesselKind.Pint) return;
                if (!(shop.IsPatronCupOfferActive || shop.IsPatronCupReturnActive)) return;
                Vector3 position = vessels[index].transform.position;
                Assert.That(Vector3.Distance(previousCups[index], position), Is.LessThan(.03f), "The guest must slide the same Pint continuously.");
                Assert.That(Mathf.Abs(position.y - resting[index].position.y), Is.LessThan(.003f));
                AssertVesselRestsOnPhysicalCounter(bar, bar.DrinkServiceView, vessels[index]);
                Assert.That(shop.PatronCupOfferWorldOffset.magnitude, Is.EqualTo(.12f).Within(.002f));
                if (shop.IsPatronCupOfferActive)
                    Assert.That(Vector3.Distance(position - resting[index].position, refill.Drinking.CounterCupOfferOffset), Is.LessThan(.003f),
                        "The actual offered Pint must follow the commanded hand slide.");
                // The first return frame still contains the bartender's final
                // lowering step. The guest then reaches and closes before its
                // own pull; subsequent moving return frames belong to it.
                bool guestMotion = Vector3.Distance(previousCups[index], position) > .0001f &&
                    (!shop.IsPatronCupReturnActive || previousCupReturn[index]);
                if (guestMotion)
                {
                    float gripError = Vector3.Distance(refill.Drinking.HandPose.AnatomicalCylinderCentre(false), vessels[index].GripWorldPosition);
                    string details = $"frame={Time.frameCount}, phase={previousCupPhase[index]}->{shop.Phase}@{shop.Timeline.PhaseElapsedSeconds:F5}s, " +
                        $"offer/return/previousReturn={shop.IsPatronCupOfferActive}/{shop.IsPatronCupReturnActive}/{previousCupReturn[index]}, " +
                        $"offerProgress={shop.PatronCupOfferProgress:F5}, previous/current={previousCups[index]:F5}/{position:F5}, " +
                        $"original={resting[index].position:F5}, handOffset={refill.Drinking.CounterCupOfferOffset:F5}, " +
                        $"holding={refill.Drinking.IsHoldingCounterCupOffer}, gripWeight/error={refill.Drinking.HandPose.RightGripWeight:F5}/{gripError:F5}.";
                    if (!refill.Drinking.IsHoldingCounterCupOffer || refill.Drinking.HandPose.RightGripWeight <= .99f || gripError >= .015f)
                        CaptureBarWorkPose(bar, "Test counter guest moving Pint grip failure", position);
                    Assert.That(refill.Drinking.IsHoldingCounterCupOffer, Is.True, "A moving Pint needs the guest's actual closed hand. " + details);
                    Assert.That(refill.Drinking.HandPose.RightGripWeight, Is.GreaterThan(.99f), details);
                    Assert.That(gripError, Is.LessThan(.015f), details);
                }
                bool offer = shop.IsPatronCupOfferActive && shop.PatronCupOfferProgress > .3f;
                bool pull = shop.IsPatronCupReturnActive && refill.Drinking.IsHoldingCounterCupOffer &&
                    Vector3.Distance(position, resting[index].position) < .10f;
                if ((offer && !cupOfferCaptured[index]) || (pull && !cupPullCaptured[index]))
                {
                    if (offer) cupOfferCaptured[index] = true; else cupPullCaptured[index] = true;
                    CaptureBarWorkPose(bar, offer ? "Test counter guest offers same Pint" : "Test counter guest pulls filled Pint back", position);
                    AssertHandWrap(refill.Drinking.HandPose, false, vessels[index].GripWorldPosition,
                        vessels[index].OpeningDirection, "Counter guest Pint handoff");
                    BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                        refill.Drinking.HandPose, false, vessels[index].transform, "Counter guest Pint handoff");
                }
            }

            private void AssertHealthy()
            {
                if (probe.Failure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(probe.Failure).Throw();
            }

            private void RecordTransition(bool force = false)
            {
                BarDrinkShopController shop = bar.DrinkShop;
                BarDrinkServiceView service = bar.DrinkServiceView;
                BarBartenderTowelPresentation towel = bar.Bartender.TowelPresentation;
                Object owner = shop.ServiceQueue.CurrentOwner;
                bool ready = shop.IsBartenderServiceReady;
                if (!force && hasTrace && ReferenceEquals(owner, tracedOwner) && shop.Phase == tracedPhase &&
                    towel.State == tracedTowel && ready == tracedReady && shop.IsBottleGripAttached == tracedBottleAttached &&
                    service.IsCarriedBottleVisible == tracedBottleVisible) return;
                hasTrace = true; tracedOwner = owner; tracedPhase = shop.Phase; tracedTowel = towel.State;
                tracedReady = ready; tracedBottleAttached = shop.IsBottleGripAttached; tracedBottleVisible = service.IsCarriedBottleVisible;
                string OwnerName(Object value) => ReferenceEquals(value, bar.DrinkShop) ? "hero" :
                    ReferenceEquals(value, requests[0]) ? "first guest" : ReferenceEquals(value, requests[1]) ? "second guest" :
                    value != null ? value.name : "none";
                float activeFill = service.ActiveVessel != null ? service.ActiveVessel.FillProgress : -1f;
                Vector3 vesselGrip = service.ActiveVessel != null ? service.ActiveVessel.GripWorldPosition : Vector3.positiveInfinity;
                transitions[transitionNext] = $"frame={Time.frameCount}, owner={OwnerName(owner)}#{shop.ServiceQueue.CurrentSequence}, " +
                    $"queued={shop.ServiceQueue.QueuedCount}, completed={shop.ServiceQueue.CompletedCount}, " +
                    $"patron={OwnerName(shop.ActivePatronRefill)}, phase={shop.Phase}@{shop.Timeline.PhaseElapsedSeconds:F4}s/{shop.Timeline.PhaseProgress:F4}, " +
                    $"open/browsing/serving={shop.IsOpen}/{shop.IsBrowsing}/{shop.IsServing}, ready/wants={ready}/{shop.WantsBartenderService}, " +
                    $"heroQueued={shop.IsHeroServiceQueued}, actor={bar.Bartender.transform.position:F4}, " +
                    $"emptyGrip={shop.Timeline.EmptyVesselGripConfirmed}, retraction={shop.EmptyVesselRetractionError:F5}, " +
                    $"handGrip={bar.Bartender.Registry.VesselGripAnchor.position:F5}, vesselGrip={vesselGrip:F5}, " +
                    $"clip={bar.Bartender.CurrentClipKind}@{bar.Bartender.CurrentClipTimeSeconds:F4}, " +
                    $"bottleVisible/attached/grip={service.IsCarriedBottleVisible}/{shop.IsBottleGripAttached}/{shop.BottleGripError:F5}, " +
                    $"activeFill={activeFill:F4}, guestFill={requests[0].Vessel.FillProgress:F4}/{requests[1].Vessel.FillProgress:F4}, " +
                    $"guestRefilling={requests[0].IsRefilling}/{requests[1].IsRefilling}, towel={towel.State}@{towel.Progress:F4}/ready={towel.ServiceReady}.";
                transitionNext = (transitionNext + 1) % transitions.Length;
                transitionCount = Mathf.Min(transitionCount + 1, transitions.Length);
            }

            private void ObserveAcknowledgement()
            {
                BarBartenderPresentation actor = bar.Bartender;
                Transform target = actor.AcknowledgementTarget;
                if (!actor.IsAcknowledgingOrder || target == null || actor.AcknowledgementWeight < .95f) return;
                int index = -1;
                for (int candidate = 0; candidate < acknowledgementRoots.Length; candidate++)
                    if (target == acknowledgementRoots[candidate] || target.IsChildOf(acknowledgementRoots[candidate]))
                    { index = candidate; break; }
                Assert.That(index, Is.GreaterThanOrEqualTo(0), "The nod must address an actual ordering guest or hero.");
                AcknowledgementEvidence evidence = acknowledgements[index];
                Vector3 face = actor.transform.InverseTransformDirection(actor.AcknowledgementHeadForward);
                Vector3 toward = actor.transform.InverseTransformDirection(target.position - actor.Registry.Head.position).normalized;
                float targetYaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                float faceYaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
                evidence.Looked |= Mathf.Abs(targetYaw) > 20f
                    ? Mathf.Sign(faceYaw) == Mathf.Sign(targetYaw) && Mathf.Abs(faceYaw) > 10f
                    : Mathf.Abs(Mathf.DeltaAngle(faceYaw, targetYaw)) < 30f;
                // Subtract target elevation so root travel and an ordering
                // guest's head height cannot masquerade as the actual nod.
                float pitch = -Mathf.Atan2(face.y, new Vector2(face.x, face.z).magnitude) * Mathf.Rad2Deg -
                    Mathf.Clamp(-Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg, -22f, 22f);
                float progress = actor.AcknowledgementProgress;
                if (progress < .20f)
                {
                    evidence.EarlyPitch = Mathf.Min(evidence.EarlyPitch, pitch);
                    evidence.EarlyHead = actor.Registry.Head.localRotation;
                    evidence.EarlyNeck = actor.Registry.Neck.localRotation;
                    evidence.HasEarlyPose = true;
                }
                if (progress > .25f && progress < .65f)
                {
                    evidence.PeakPitch = Mathf.Max(evidence.PeakPitch, pitch);
                    if (evidence.HasEarlyPose)
                        evidence.BoneMotion = Mathf.Max(evidence.BoneMotion,
                            Quaternion.Angle(evidence.EarlyHead, actor.Registry.Head.localRotation) +
                            Quaternion.Angle(evidence.EarlyNeck, actor.Registry.Neck.localRotation));
                }
                if (progress > .72f) evidence.ReturnedPitch = Mathf.Min(evidence.ReturnedPitch, pitch);
                if (!evidence.Captured && actor.AcknowledgementNodDegrees > 6f)
                {
                    evidence.Captured = true;
                    CaptureBarWorkPose(bar, index == 2 ? "Test bartender acknowledges queued hero" :
                        index == 0 ? "Test bartender acknowledges first guest" : "Test bartender acknowledges second guest", actor.Registry.Head.position);
                }
                if (index == 2 && bar.DrinkShop.IsHeroServiceQueued) sawQueuedHeroAcknowledgement = true;
            }

            public void Dispose()
            {
                probe.CancelPending(); Object.Destroy(probe);
                for (int index = 0; index < 2; index++)
                    if (requests[index] != null) requests[index].enabled = previouslyEnabled[index];
            }
        }

        private static IEnumerator VerifyBartenderChangesWipePatch(BarInteriorRoot bar)
        {
            BarBartenderPresentation bartender = bar.Bartender;
            BarBartenderServiceChoreography work = bartender.GetComponent<BarBartenderServiceChoreography>();
            Transform foot = bartender.Registry.LeftFoot;
            Vector3 original = bartender.transform.position, previous = original;
            Vector3 firstPatch = bartender.Registry.TowelWorldBounds.center;
            Vector3 firstFoot = bartender.transform.InverseTransformPoint(foot.position);
            bool sawStow = false, sawWalk = false, sawNewPatch = false;
            float footTravel = 0f;
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            try
            {
                work.Advance(BarBartenderServiceChoreography.IdleWipePatchSeconds + .01f);
                for (int frame = 0; frame < 600 && !sawNewPatch; frame++)
                {
                    yield return AtFinalBarPose(probe, () =>
                    {
                        sawStow |= bartender.TowelPresentation.State == BarBartenderTowelState.Stowing;
                        Assert.That(Vector3.Distance(previous, bartender.transform.position), Is.LessThan(.12f),
                            "Changing a wipe patch must use continuous root travel.");
                        previous = bartender.transform.position;
                        if (bartender.CurrentClipKind == BarBartenderClipKind.Walk)
                        {
                            Assert.That(bartender.TowelPresentation.IsTowelVisible, Is.False,
                                "The towel must physically stow before walking to another patch.");
                            footTravel = Mathf.Max(footTravel, Vector3.Distance(firstFoot,
                                bartender.transform.InverseTransformPoint(foot.position)));
                            if (!sawWalk)
                                CaptureBarWorkPose(bar, "Test bartender walking to another wipe patch",
                                    bartender.Registry.Spine.position);
                            sawWalk = true;
                        }
                        if (!work.IsChangingWipePatch && bartender.TowelPresentation.WipeReady &&
                            Vector3.Distance(original, bartender.transform.position) > .15f)
                        {
                            Assert.That(bartender.Registry.TowelWipeContactError, Is.LessThan(.01f));
                            Assert.That(Vector3.Distance(firstPatch, bartender.Registry.TowelWorldBounds.center),
                                Is.GreaterThan(.15f));
                            sawNewPatch = true;
                            CaptureBarWorkPose(bar, "Test bartender wiping another counter patch",
                                bartender.Registry.TowelWorldBounds.center);
                        }
                    });
                }
                Assert.That(sawStow && sawWalk && sawNewPatch, Is.True,
                    "Idle work must stow, walk and resume wiping at another physical patch.");
                Assert.That(footTravel, Is.GreaterThan(.025f), "The root moved without a sampled Walk foot pose.");
            }
            finally { probe.CancelPending(); Object.Destroy(probe); }
        }

        private static bool HasBarSimulationBudget(int startFrame, int permittedFrames, float startedAt, float realWatchdog)
            => Time.frameCount - startFrame < permittedFrames &&
               Time.realtimeSinceStartup - startedAt < realWatchdog;

        private static string DescribeBarSimulationWait(BarInteriorRoot bar, int startFrame, float startedAt)
        {
            PlayerAnimatedInteractionController seat = bar.CounterStations[bar.CounterStations.Count - 1].Seat.Controller;
            string focused = "player";
#if UNITY_EDITOR
            focused = UnityEditor.EditorWindow.focusedWindow != null
                ? UnityEditor.EditorWindow.focusedWindow.GetType().Name : "none";
#endif
            return $"seat={seat.Phase}, clip={((Player3DCharacterPresentation)bar.Player.Visual).ActiveClipName}, " +
                $"clipProgress={seat.PhaseProgress:F5}, clipFrame={seat.FrameIndex}, menu={bar.DrinkShop.MenuState}, " +
                $"service={bar.DrinkShop.Phase}, timeScale={Time.timeScale:F5}, captureDelta={Time.captureDeltaTime:F6}, " +
                $"frames={Time.frameCount - startFrame}, realSeconds={Time.realtimeSinceStartup - startedAt:F3}, focused={focused}";
        }

        private static IEnumerator VerifyMenuDeliveryOutsideBody(BarInteriorRoot bar)
        {
            var clearance = new MenuBodyClearance(bar);
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            bool captured = false;
            Vector3? carryStart = null;
            int frame = 0;
            int startFrame = Time.frameCount;
            float startedAt = Time.realtimeSinceStartup;
            try
            {
                while (!bar.DrinkShop.IsBrowsing &&
                    HasBarSimulationBudget(startFrame, 360, startedAt, 30f))
                {
                    yield return AtFinalBarPose(probe, () =>
                    {
                        if (!bar.DrinkShop.MenuPresentation.IsVisible || frame++ % 6 != 0) return;
                        clearance.AssertClear();
                        if (bar.DrinkShop.MenuState != CounterMenuState.Delivering) return;
                        Vector3 position = bar.DrinkShop.MenuPresentation.PropRoot.position;
                        if (!carryStart.HasValue) carryStart = position;
                        if (!captured && Vector3.Distance(carryStart.Value, position) > .10f)
                        {
                            CaptureBarWorkPose(bar, "Test menu carried outside bartender body",
                                bar.DrinkShop.MenuPresentation.PropRoot.position);
                            captured = true;
                        }
                    });
                }
                Assert.That(bar.DrinkShop.IsBrowsing, Is.True,
                    "The real menu handoff did not finish. " + DescribeBarSimulationWait(bar, startFrame, startedAt));
                Assert.That(captured, Is.True, "The test did not observe a moving physical menu handoff.");
                clearance.AssertClear();
            }
            finally { probe.CancelPending(); Object.Destroy(probe); }
        }

        private static IEnumerator VerifyOpenMenuClearance(BarInteriorRoot bar)
        {
            BarBartenderPresentation bartender = bar.Bartender;
            Assert.That(bar.DrinkShop.MenuState, Is.EqualTo(CounterMenuState.Open));
            Assert.That(bar.DrinkServiceView.TryGetCounterMenuBounds(out Bounds paper), Is.True);
            Assert.That(bartender.Registry.TryGetClip(BarBartenderClipKind.Wipe, out AnimationClip clip, out _), Is.True);
            bool sawSafeWork = false;
            foreach (float phase in new[] { .12f, .28f, .44f, .70f, .94f })
            {
                if (bartender.TowelPresentation.WipeReady)
                    AdvanceWipeToPhase(bartender, clip.length, phase);
                else
                    for (int step = 0; step < 40 && !bartender.IsTowelServiceReady; step++) bartender.Advance(.05f);
                Assert.That(bar.DrinkServiceView.TryGetCounterMenuBounds(out paper), Is.True);
                Bounds padded = paper; padded.Expand(new Vector3(.03f, 0f, .03f));
                if (bartender.TowelPresentation.WipeReady)
                {
                    Bounds cloth = bartender.Registry.TowelWipeWorldBounds;
                    bool overlaps = cloth.max.x > padded.min.x && cloth.min.x < padded.max.x &&
                        cloth.max.z > padded.min.z && cloth.min.z < padded.max.z;
                    if (overlaps)
                        CaptureBarWorkPose(bar, "Test open menu wipe clearance failure", paper.center);
                    Assert.That(overlaps, Is.False,
                        $"The actual open pages need15mm cloth clearance. phase={phase:F2}, " +
                        $"paper={paper}, cloth={cloth}, envelope={bartender.Registry.WipeStrokeWorldBounds}");
                    Assert.That(bartender.Registry.TowelWipeContactError, Is.LessThan(.01f));
                    sawSafeWork = true;
                }
                else
                {
                    Assert.That(bartender.IsTowelServiceReady, Is.True, "An infeasible open-menu patch must physically stow.");
                    Assert.That(bartender.TowelPresentation.IsTowelVisible, Is.False);
                    sawSafeWork = true;
                }
                yield return null;
            }
            Assert.That(sawSafeWork, Is.True);
            CaptureBarWorkPose(bar, "Test open menu and safe counter work", paper.center);
        }

        private static IEnumerator VerifyInitialOrderGesture(BarInteriorRoot bar, DrinkId drink)
        {
            BarDrinkShopController shop = bar.DrinkShop;
            BarRepeatGesture gesture = shop.RepeatGesture;
            int cash = GameSessionState.CashBalance, consumed = GameSessionState.DrinksConsumed;
            int notices = shop.PaymentNotice.NotificationCount, price = shop.SelectedOffer.Price;
            // The hero stays in Browsing while the shared public phase may
            // report another guest's active service.
            const BarDrinkServicePhase pendingPhase = BarDrinkServicePhase.Browsing;
            var hands = new RepeatHandSurfaceAudit(bar, gesture);
            using var look = new DrinkActionLookContract(bar, drink, repeatGesture: true, gesturePhase: pendingPhase);
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            int contacts = 0, physicalContacts = 0;
            bool touching = false, sawGesture = false, waveCaptured = false, complete = false;
            float greatestLift = 0f;
            void Contact(int number, Vector3 point)
            {
                Assert.That(number, Is.EqualTo(++contacts));
            }
            gesture.TapContact += Contact;
            try
            {
                bar.CounterStations[bar.CounterStations.Count - 1].Interact(bar.Player.Interactor);
                Assert.That(shop.IsOrderRequestPending, Is.True, "Menu confirmation must begin the physical request before charging.");
                Assert.That(shop.IsServing, Is.True);
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                Assert.That(shop.PaymentNotice.NotificationCount, Is.EqualTo(notices));
                int startFrame = Time.frameCount;
                float startedAt = Time.realtimeSinceStartup;
                Action observe = null;
                observe = () =>
                {
                    Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(consumed));
                    if (shop.IsOrderRequestPending)
                    {
                        Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash), "The first order must wait for both physical taps before payment.");
                        Assert.That(shop.PaymentNotice.NotificationCount, Is.EqualTo(notices));
                    }
                    if (gesture.IsActive)
                    {
                        sawGesture = true;
                        look.Observe(pendingPhase, holdInput: !hands.WaveFramed);
                        PlayerAnimatedInteractionPhase phase = shop.CounterSeatView.Seat.Controller.NestedLoopActionPhase;
                        hands.Observe(phase);
                        if (hands.WaveFramed && !waveCaptured)
                        {
                            waveCaptured = true;
                            AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior, "Test first order left hand wave");
                        }
                        if (phase == PlayerAnimatedInteractionPhase.Looping)
                        {
                            float height = hands.IndexLowestPoint.y - bar.DrinkServiceView.CounterSurfaceWorldHeight;
                            greatestLift = Mathf.Max(greatestLift, height);
                            bool contact = height <= .0035f;
                            Assert.That(height, Is.GreaterThan(-.0025f));
                            if (contact && !touching)
                            {
                                hands.AssertContact(++physicalContacts);
                                AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior,
                                    "Test first order index tap " + (physicalContacts == 1 ? "first" : "second"));
                            }
                            if (height > .012f) touching = false;
                            else if (contact) touching = true;
                        }
                    }
                    complete = sawGesture && !gesture.IsActive && !shop.IsOrderRequestPending;
                    if (!complete) probe.Schedule(observe);
                };
                probe.Schedule(observe);
                while (!complete && probe.Failure == null && HasBarSimulationBudget(startFrame, 720, startedAt, 60f))
                    yield return null;
                probe.CancelPending();
                if (probe.Failure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(probe.Failure).Throw();
                Assert.That(complete, Is.True, "The first-order wave and taps never completed. " + gesture.LastBeginDiagnostics);
                hands.AssertComplete();
                Assert.That(contacts, Is.EqualTo(2));
                Assert.That(physicalContacts, Is.EqualTo(2));
                Assert.That(greatestLift, Is.GreaterThan(.015f));
                Assert.That(gesture.LastContactError, Is.LessThanOrEqualTo(BarRepeatGesture.ContactTolerance));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - price));
                AssertPaymentNotice(shop, notices + 1, price, cash - price);
                yield return look.VerifyRestored();
            }
            finally
            {
                gesture.TapContact -= Contact;
                probe.CancelPending(); Object.Destroy(probe);
            }
        }

        private static float MeasureCupSlideRoom(BarInteriorRoot bar, BarDrinkVesselView vessel, Vector3 direction)
        {
            Bounds local = vessel.LocalVisualBounds;
            Bounds shell = new(vessel.transform.TransformPoint(local.min), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                shell.Encapsulate(vessel.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
            Bounds wood = bar.DrinkServiceView.CounterSurfaceWorldBounds;
            bool hasMenu = bar.DrinkServiceView.TryGetRestingMenuBounds(out Bounds paper);
            if (hasMenu) paper.Expand(.02f);
            float available = 0f;
            for (int step = 1; step <= 12; step++)
            {
                Bounds moved = shell; moved.center += direction * (step * .01f);
                if (moved.min.x < wood.min.x + .01f || moved.max.x > wood.max.x - .01f ||
                    moved.min.z < wood.min.z + .01f || moved.max.z > wood.max.z - .01f || hasMenu && moved.Intersects(paper)) break;
                available = step * .01f;
            }
            return available;
        }

        private static IEnumerator VerifySameDrinkRefill(BarInteriorRoot bar, DrinkId drink)
        {
            PlayerScarfController scarf = bar.Player.GameObject.GetComponent<PlayerScarfController>();
            int mouthStartFrame = Time.frameCount;
            float mouthStartedAt = Time.realtimeSinceStartup;
            while (scarf != null && scarf.IsGestureActive &&
                   HasBarSimulationBudget(mouthStartFrame, 180, mouthStartedAt, 10f))
                yield return null;
            Assert.That(scarf == null || !scarf.IsGestureActive, Is.True,
                $"The shared scarf return did not finish before repeat input; progress={scarf?.GestureProgress:F4}.");
            BarDrinkShopController shop = bar.DrinkShop;
            BarDrinkServiceView service = bar.DrinkServiceView;
            BarDrinkVesselView vessel = service.ActiveVessel;
            Pose resting = new(vessel.transform.position, vessel.transform.rotation);
            int cash = GameSessionState.CashBalance, consumed = GameSessionState.DrinksConsumed;
            int notices = shop.PaymentNotice.NotificationCount;
            int price = shop.Offers[FindOfferIndex(shop, drink)].Price;
            bool beer = drink == DrinkId.LightBeer;
            Vector3 offerDirection = Vector3.ProjectOnPlane(bar.Player.GameObject.transform.forward, Vector3.up).normalized;
            float availableOffer = beer ? MeasureCupSlideRoom(bar, vessel, offerDirection) : 0f;
            var offerClearance = beer ? new MenuBodyClearance(bar, vessel) : null;
            using var gestureLook = new DrinkActionLookContract(bar, drink, repeatGesture: true);
            yield return gestureLook.AimAtRepeatVessel(vessel);
            shop.RefreshServedDrinkAffordance();
            Assert.That(shop.CanRepeatServedDrink && shop.IsLookingAtRepeatVessel, Is.True,
                $"Repeat availability: {shop.RepeatGesture.LastBeginDiagnostics}; " +
                $"ready={shop.RepeatGesture.CanBegin}, scarf={scarf?.IsGestureActive}, feedback={shop.FeedbackKey}.");
            Assert.That(vessel.IsInteractionHighlighted, Is.True, "The empty vessel needs the same gaze outline as a full drink.");
            BarRepeatGesture gesture = shop.RepeatGesture;
            var contacts = new List<Vector3>();
            void Contact(int number, Vector3 point)
            {
                Assert.That(number, Is.EqualTo(contacts.Count + 1));
                contacts.Add(point);
            }
            gesture.TapContact += Contact;
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            bool touching = false;
            int physicalTaps = 0;
            float greatestLift = 0f;
            bool offerStarted = false, sawOfferContact = false, sawOfferMovement = false, sawOfferRelease = false;
            bool offerPushCaptured = false;
            Vector3 previousMug = resting.position;
            float previousSlide = 0f;
            var hands = new RepeatHandSurfaceAudit(bar, gesture);
            try
            {
                bar.CounterStations[bar.CounterStations.Count - 1].Interact(bar.Player.Interactor);
                Assert.That(gesture.IsActive, Is.True,
                    "The real counter E action must begin the repeat gesture. " + gesture.LastBeginDiagnostics +
                    $"; eligible={shop.CanRepeatServedDrink}, looking={shop.IsLookingAtRepeatVessel}, feedback={shop.FeedbackKey}.");
                Assert.That(shop.BeginRepeatDrink(), Is.False, "A repeated input cannot queue or charge another drink.");
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash), "Taps precede payment.");
                Assert.That(shop.PaymentNotice.NotificationCount, Is.EqualTo(notices));
                Assert.That(vessel.IsInteractionHighlighted, Is.False);
                int waitStartFrame = Time.frameCount;
                float waitStartedAt = Time.realtimeSinceStartup;
                bool observedGestureComplete = false;
                Action observeGesture = null;
                observeGesture = () =>
                    {
                        Assert.That(service.ActiveVessel, Is.SameAs(vessel));
                        Assert.That(vessel.gameObject.activeInHierarchy, Is.True);
                        bool offering = beer && (gesture.IsOfferingVessel || gesture.CompletedVesselOffer || offerStarted);
                        if (!offering)
                            Assert.That(Vector3.Distance(vessel.transform.position, resting.position), Is.LessThan(.003f));
                        else
                        {
                            offerStarted = true;
                            Assert.That(physicalTaps, Is.EqualTo(2), "The physical mug offer must follow both index contacts.");
                            Assert.That(gesture.CompletedTapCount, Is.EqualTo(2));
                            Assert.That(gesture.PlannedOfferDistance, Is.InRange(0f, .1201f));
                            if (availableOffer >= .03f)
                                Assert.That(gesture.PlannedOfferDistance, Is.GreaterThan(.02f), "There is room for a meaningful short mug offer.");
                            Vector3 delta = vessel.transform.position - resting.position;
                            float slide = Vector3.Dot(delta, offerDirection);
                            Assert.That(Vector3.ProjectOnPlane(delta, offerDirection).magnitude, Is.LessThan(.003f), "The mug must slide on the wood without sideways or vertical drift.");
                            Assert.That(slide, Is.InRange(-.001f, gesture.PlannedOfferDistance + .003f));
                             Assert.That(slide, Is.GreaterThanOrEqualTo(previousSlide - .001f),
                                 $"The offered mug moved backwards: previous/current={previousSlide:F6}/{slide:F6}m, " +
                                 $"active/completed={gesture.IsActive}/{gesture.CompletedVesselOffer}, offerProgress={gesture.OfferProgress:F6}, " +
                                 $"reportedSlide/plan={gesture.OfferSlideDistance:F6}/{gesture.PlannedOfferDistance:F6}, " +
                                 $"gripWeight/error={gesture.OfferGripWeight:F6}/{gesture.OfferGripError:F6}, gestureProgress={gesture.Progress:F6}, " +
                                 $"completedTaps={gesture.CompletedTapCount}, nested={shop.CounterSeatView.Seat.Controller.NestedLoopActionPhase}, " +
                                 $"shopPhase={shop.Phase}, timelinePhase={shop.Timeline.Phase}, cash={GameSessionState.CashBalance}; " +
                                 gesture.LastBeginDiagnostics);
                            Assert.That(Vector3.Distance(previousMug, vessel.transform.position), Is.LessThan(.03f), "The same mug must move continuously through the short hand offer.");
                            Assert.That(Quaternion.Angle(resting.rotation, vessel.transform.rotation), Is.LessThan(.1f));
                            AssertVesselRestsOnPhysicalCounter(bar, service, vessel);
                            offerClearance.AssertClear();
                            if (gesture.IsOfferingVessel && gesture.OfferGripWeight >= .999f &&
                                gesture.OfferGripError <= BarRepeatGesture.ContactTolerance)
                            {
                                if (!sawOfferContact)
                                {
                                    AssertHandWrap(bar.Player.GameObject.GetComponentInChildren<NpcHandPose>(true), false,
                                        vessel.GripWorldPosition, vessel.OpeningDirection, "Hero short mug offer");
                                    BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                                        bar.Player.GameObject.GetComponentInChildren<NpcHandPose>(true), false, vessel.transform, "Hero short mug offer");
                                    CaptureBarWorkPose(bar, "Test hero grasps empty mug for short offer", vessel.transform.position);
                                }
                                sawOfferContact = true;
                            }
                            if (slide > previousSlide + .0001f)
                            {
                                sawOfferMovement = true;
                                Assert.That(sawOfferContact, Is.True, "The hand must physically close before the mug moves.");
                                Assert.That(gesture.OfferGripWeight, Is.GreaterThan(.99f));
                                Assert.That(gesture.OfferGripError, Is.LessThanOrEqualTo(BarRepeatGesture.ContactTolerance));
                            }
                            if (!offerPushCaptured && slide > .01f)
                            {
                                offerPushCaptured = true;
                                AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior, "Test first-person short mug offer");
                                CaptureBarWorkPose(bar, "Test hero pushes same empty mug toward bartender", vessel.transform.position);
                            }
                            if (gesture.IsOfferingVessel && gesture.OfferProgress > .8f &&
                                gesture.OfferGripWeight <= .001f && gesture.OfferGripError > .02f)
                                sawOfferRelease = true;
                            previousSlide = slide;
                        }
                        previousMug = vessel.transform.position;
                        Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(consumed));
                        if (gesture.IsActive)
                        {
                            gestureLook.Observe(BarDrinkServicePhase.EmptyOnCounter, holdInput: !hands.WaveFramed);
                            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                            Assert.That(shop.PaymentNotice.NotificationCount, Is.EqualTo(notices));
                        }
                        PlayerAnimatedInteractionController action = shop.CounterSeatView.Seat.Controller;
                        hands.Observe(action.NestedLoopActionPhase);
                        if (action.NestedLoopActionPhase == PlayerAnimatedInteractionPhase.Looping &&
                            !gesture.IsOfferingVessel && !gesture.CompletedVesselOffer)
                        {
                            float height = hands.IndexLowestPoint.y - service.CounterSurfaceWorldHeight;
                            greatestLift = Mathf.Max(greatestLift, height);
                            bool contact = height <= .0035f;
                            Assert.That(height, Is.GreaterThan(-.0025f), "The real index pad penetrated the wood.");
                            if (contact && !touching)
                            {
                                physicalTaps++;
                                hands.AssertContact(physicalTaps);
                                Assert.That(gesture.OtherFingerCurlWeight, Is.GreaterThan(.9f));
                                AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior,
                                    "Test first-person repeat index tap " + (physicalTaps == 1 ? "first" : "second"));
                                CaptureBarWorkPose(bar, "Test flat palm repeat index tap " + (physicalTaps == 1 ? "first" : "second"),
                                    gesture.TapTargetWorldPosition);
                            }
                            if (height > .012f) touching = false;
                            else if (contact) touching = true;
                        }
                        observedGestureComplete = !gesture.IsActive;
                        if (!observedGestureComplete) probe.Schedule(observeGesture);
                    };
                probe.Schedule(observeGesture);
                while (!observedGestureComplete && probe.Failure == null &&
                       HasBarSimulationBudget(waitStartFrame, 720, waitStartedAt, 60f))
                    yield return null;
                probe.CancelPending();
                if (probe.Failure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(probe.Failure).Throw();
                Assert.That(observedGestureComplete, Is.True,
                    "The final rendered repeat gesture pose was not observed.");
                Assert.That(gesture.IsActive, Is.False,
                    "The nested repeat gesture did not release. " + DescribeBarSimulationWait(bar, waitStartFrame, waitStartedAt));
                hands.AssertComplete();
                Assert.That(contacts.Count, Is.EqualTo(2));
                Assert.That(physicalTaps, Is.EqualTo(2), "The actual index surface must make two separate light contacts.");
                Assert.That(greatestLift, Is.GreaterThan(.015f), "The finger did not lift between taps.");
                Assert.That(gesture.LastContactError, Is.LessThanOrEqualTo(BarRepeatGesture.ContactTolerance));
                if (beer)
                {
                    Assert.That(gesture.CompletedVesselOffer && sawOfferContact && sawOfferRelease, Is.True, "Finish the closed-hand mug offer and physical release before payment.");
                    Assert.That(previousSlide, Is.EqualTo(gesture.PlannedOfferDistance).Within(.003f));
                    if (gesture.PlannedOfferDistance > .002f) Assert.That(sawOfferMovement, Is.True);
                }
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - price));
                AssertPaymentNotice(shop, notices + 1, price, cash - price);
                yield return gestureLook.VerifyRestored();
                yield return CapturePaymentNotice(bar, beer ? "Test repeated beer payment receipt" : "Test repeated wine payment receipt");

                bool sawPour = false, sawCarried = false, sawRepeatBottleGrip = false;
                var targetField = typeof(BarBartenderPresentation).GetField("chainTargets",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(targetField, Is.Not.Null);
                var chainTargets = (Vector3[])targetField.GetValue(bar.Bartender);
                Assert.That(chainTargets, Is.Not.Null);
                Assert.That(chainTargets.Length, Is.GreaterThan(0));
                Vector3 previous = default;
                RefillRigSnapshot previousRig = default;
                int previousFrame = -1;
                BarDrinkServicePhase previousPhase = shop.Phase;
                float previousPhaseElapsed = shop.Timeline.PhaseElapsedSeconds;
                bool observedServiceComplete = false;
                waitStartFrame = Time.frameCount;
                waitStartedAt = Time.realtimeSinceStartup;
                Action observeRefill = null;
                observeRefill = () =>
                    {
                        Assert.That(service.ActiveVessel, Is.SameAs(vessel), "A repeat must refill the same object.");
                        Assert.That(vessel.gameObject.activeInHierarchy, Is.True);
                        Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - price));
                        Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(consumed));
                        var currentRig = new RefillRigSnapshot(bar.Bartender, chainTargets[0],
                            shop.Timeline.PhaseProgress, vessel.FillProgress);
                        if (previousFrame >= 0)
                        {
                            string timing = $"frames={previousFrame}->{Time.frameCount}, " +
                                $"previousPhase={previousPhase}@{previousPhaseElapsed:F5}s, " +
                                $"deltaTime={Time.deltaTime:F6}, unscaledDeltaTime={Time.unscaledDeltaTime:F6}, " +
                                $"captureDeltaTime={Time.captureDeltaTime:F6}";
                            Assert.That(Time.frameCount - previousFrame, Is.EqualTo(1),
                                "Refill continuity must compare adjacent final rendered poses. " + timing);
                            float distance = Vector3.Distance(previous, vessel.transform.position);
                            bool discontinuous = !(distance < .10f);
                            string rigDetails = discontinuous ? $"previousRig=({previousRig}); currentRig=({currentRig}); " : string.Empty;
                            if (discontinuous)
                                CaptureBarWorkPose(bar, "Test repeat vessel continuity failure", vessel.transform.position);
                            Assert.That(distance, Is.LessThan(.10f),
                                $"A repeat vessel teleported during {shop.Phase} at {shop.Timeline.PhaseElapsedSeconds:F4}s: " +
                                $"{previous:F5} to {vessel.transform.position:F5}; {timing}; {rigDetails}" +
                                $"grip={vessel.GripWorldPosition:F5}, hand={bar.Bartender.Registry.VesselGripAnchor.position:F5}.");
                        }
                        previous = vessel.transform.position;
                        previousRig = currentRig;
                        previousFrame = Time.frameCount;
                        previousPhase = shop.Phase;
                        previousPhaseElapsed = shop.Timeline.PhaseElapsedSeconds;
                        if (!beer)
                        {
                            Assert.That(Vector3.Distance(vessel.transform.position, resting.position), Is.LessThan(.003f));
                            Assert.That(service.IsActiveVesselCarriedByBartender, Is.False);
                            Assert.That(shop.Phase, Is.Not.EqualTo(BarDrinkServicePhase.BottleCarryToGuest));
                            Assert.That(shop.Phase, Is.Not.EqualTo(BarDrinkServicePhase.BottleVesselPlacement));
                        }
                        else if (service.IsActiveVesselCarriedByBartender || service.IsBeerTapVesselCarriedByBartender)
                        {
                            sawCarried = true;
                            Assert.That(service.ResolveActiveVesselGripError(bar.Bartender.Registry.VesselGripAnchor), Is.LessThan(.02f));
                        }
                        if ((shop.Phase == BarDrinkServicePhase.Pouring || shop.Phase == BarDrinkServicePhase.BeerPouring) &&
                            vessel.FillProgress > .05f && vessel.FillProgress < .95f)
                        {
                            Assert.That(service.IsStreamVisible, Is.True);
                            Vector3 mouth = beer ? service.BeerTapSpoutWorldPosition : service.CarriedBottleMouthWorldPosition;
                            Vector3 delta = mouth - vessel.PourTargetWorldPosition;
                            Assert.That(Vector3.ProjectOnPlane(delta, Vector3.up).magnitude, Is.LessThan(.025f));
                            if (beer)
                            {
                                Assert.That(Vector3.ProjectOnPlane(vessel.transform.position - service.BeerTapVesselWorldPose.position,
                                    Vector3.up).magnitude, Is.LessThan(.025f), "The same mug must reach the physical tap.");
                                Assert.That(service.ResolveActiveVesselGripError(bar.Bartender.Registry.VesselGripAnchor),
                                    Is.LessThan(.012f), "The real palm must already hold the mug while the tap fills it.");
                            }
                            if (!sawPour)
                                CaptureBarWorkPose(bar, beer ? "Test same mug refill at beer tap" : "Test same glass refill at guest dock",
                                    vessel.transform.position);
                            if (!beer && !sawRepeatBottleGrip && vessel.FillProgress >= .4f)
                            {
                                CaptureBarServiceDetail(vessel.transform.position, "Test repeat bottle pour from player left");
                                AssertBottlePoursFromPlayerLeft(bar, vessel, "Repeat bottle pour");
                                AssertHandWrap(bar.Bartender.Registry.HandPose, false,
                                    service.CarriedBottleGripCenterWorldPosition,
                                    service.CarriedBottleRoot.up, "Repeat bartender bottle");
                                BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                                    bar.Bartender.Registry.HandPose, false, service.CarriedBottleRoot, "Repeat bartender bottle");
                                sawRepeatBottleGrip = true;
                            }
                            sawPour = true;
                        }
                        observedServiceComplete = shop.Phase == BarDrinkServicePhase.AwaitingDrink;
                        if (!observedServiceComplete) probe.Schedule(observeRefill);
                    };
                probe.Schedule(observeRefill);
                while (!observedServiceComplete && probe.Failure == null &&
                    HasBarSimulationBudget(waitStartFrame, 2400, waitStartedAt, 60f))
                    yield return null;
                probe.CancelPending();
                if (probe.Failure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(probe.Failure).Throw();
                Assert.That(observedServiceComplete, Is.True,
                    "Refill observation never reached the final rendered service pose. " +
                    DescribeBarSimulationWait(bar, waitStartFrame, waitStartedAt));
                Assert.That(shop.Phase, Is.EqualTo(BarDrinkServicePhase.AwaitingDrink),
                    DescribeBarSimulationWait(bar, waitStartFrame, waitStartedAt));
                Assert.That(sawPour && (!beer || sawCarried), Is.True);
                Assert.That(beer || sawRepeatBottleGrip, Is.True, "The repeat bottle pour needs a settled palm/finger proof.");
                Assert.That(vessel.FillProgress, Is.EqualTo(1f));
                Assert.That(Vector3.Distance(vessel.transform.position, resting.position), Is.LessThan(.003f));
                Camera.main.transform.rotation = Quaternion.LookRotation(
                    vessel.GlassRenderer.bounds.center - Camera.main.transform.position, Vector3.up);
                shop.RefreshServedDrinkAffordance();
                Assert.That(shop.BeginServedDrink(), Is.True);
                waitStartFrame = Time.frameCount;
                waitStartedAt = Time.realtimeSinceStartup;
                while (shop.IsServing && HasBarSimulationBudget(waitStartFrame, 1200, waitStartedAt, 60f)) yield return null;
                Assert.That(shop.Phase, Is.EqualTo(BarDrinkServicePhase.EmptyOnCounter),
                    DescribeBarSimulationWait(bar, waitStartFrame, waitStartedAt));
                Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(consumed + 1));
                Assert.That(GameSessionState.LastAlcoholicDrink, Is.EqualTo(drink));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - price));
                Assert.That(service.ActiveVessel, Is.SameAs(vessel));
                Assert.That(shop.PaymentNotice.NotificationCount, Is.EqualTo(notices + 1));
                Assert.That(shop.PaymentNotice.IsVisible, Is.False, "The receipt must expire while the scene remains live.");
            }
            finally
            {
                gesture.TapContact -= Contact;
                probe.CancelPending(); Object.Destroy(probe);
            }
        }

        private readonly struct RefillRigSnapshot
        {
            private readonly Pose root;
            private readonly BarBartenderClipKind clip;
            private readonly float clipTime, weight, phaseProgress, fill;
            private readonly Vector3 target, shoulder, elbow, wrist, grip;

            internal RefillRigSnapshot(BarBartenderPresentation actor, Vector3 target, float phaseProgress, float fill)
            {
                root = new Pose(actor.transform.position, actor.transform.rotation);
                clip = actor.CurrentClipKind;
                clipTime = actor.CurrentClipTimeSeconds;
                weight = actor.GetChainWeight(BarBartenderPresentation.OrdinaryVesselHandIndex);
                this.phaseProgress = phaseProgress;
                this.fill = fill;
                this.target = target;
                shoulder = actor.Registry.LeftUpperArm.position;
                elbow = actor.Registry.LeftForearm.position;
                wrist = actor.Registry.LeftHand.position;
                grip = actor.Registry.VesselGripAnchor.position;
            }

            public override string ToString()
                => $"root={root.position:F5}/{root.rotation:F5}, clip={clip}@{clipTime:F5}, " +
                   $"weight={weight:F5}, phaseProgress={phaseProgress:F5}, fill={fill:F5}, " +
                   $"target={target:F5}, shoulder={shoulder:F5}, elbow={elbow:F5}, wrist={wrist:F5}, grip={grip:F5}";
        }

        private static void AssertBottlePoursFromPlayerLeft(BarInteriorRoot bar, BarDrinkVesselView vessel, string label)
        {
            BarBartenderAssetRegistry rig = bar.Bartender.Registry;
            Vector3 bottle = bar.Room.InverseTransformPoint(bar.DrinkServiceView.CarriedBottleRoot.position);
            Vector3 glass = bar.Room.InverseTransformPoint(vessel.transform.position);
            Vector3 elbowFromShoulder = rig.RightForearm.position - rig.RightUpperArm.position;
            float elbowDown = Vector3.Dot(elbowFromShoulder, Vector3.up);
            Vector3 shoulderMidpoint = (rig.LeftUpperArm.position + rig.RightUpperArm.position) * .5f;
            float elbowOutside = Vector3.Dot(rig.RightForearm.position - shoulderMidpoint, bar.Bartender.transform.right);
            Vector3 palm = default;
            int visiblePalms = 0;
            foreach (NpcHandPose.HandBinding binding in rig.HandPose.Hands)
                if (!binding.IsLeft)
                    foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                        if (renderer.enabled && renderer.gameObject.activeInHierarchy &&
                            (renderer.name.StartsWith("GEO_Hand.") || renderer.name.Contains("HandPalm.") || renderer.name.Contains("GlovePalm.")))
                        { palm = NeutralHandMeshCentre(renderer, binding.Hand); visiblePalms++; }
            Assert.That(visiblePalms, Is.EqualTo(1), label + " needs the actual visible right palm.");
            Vector3 overhand = Vector3.ProjectOnPlane(Vector3.up, bar.DrinkServiceView.CarriedBottleRoot.up).normalized;
            Assert.That(overhand.sqrMagnitude, Is.GreaterThan(.9f), label + " needs a tilted bottle axis.");
            float palmAbove = Vector3.Dot(palm - bar.DrinkServiceView.CarriedBottleGripCenterWorldPosition, overhand);
            string details = $"{label}: bottleBase={bottle:F5}, glass={glass:F5}, " +
                $"rightElbowDown={elbowDown:F5}, rightElbowFromTorso={elbowOutside:F5}, " +
                $"palmAboveBottleAxis={palmAbove:F5}, palm={palm:F5}, " +
                $"shoulder={rig.RightUpperArm.position:F5}, elbow={rig.RightForearm.position:F5}.";
            Assert.That(bottle.x, Is.LessThan(glass.x - .05f), "The bottle must pour from the player's left. " + details);
            Assert.That(elbowDown, Is.LessThan(-.01f), "The right elbow must stay below its shoulder. " + details);
            Assert.That(elbowOutside, Is.GreaterThan(0f), "The right elbow must stay on the anatomical right of the torso. " + details);
            Assert.That(palmAbove, Is.GreaterThan(.015f), "The visible right palm must grasp the bottle neck from above. " + details);
        }

        private static void CaptureBarWorkPose(BarInteriorRoot bar, string name, Vector3 worldTarget)
        {
            Vector3 target = bar.Room.InverseTransformPoint(worldTarget);
            UnityEngine.Rendering.Volume closeUp = FindCinematicDepthOfFieldVolume();
            float previousWeight = closeUp != null ? closeUp.weight : 0f;
            try
            {
                if (closeUp != null) closeUp.weight = 0f;
                CaptureBarWipeReview(bar, name, target + new Vector3(1.1f, .65f, -1.2f), target, 58f);
            }
            finally { if (closeUp != null) closeUp.weight = previousWeight; }
        }

        private static void AssertPaymentNotice(BarDrinkShopController shop, int count, int paid, int balance)
        {
            PaymentNoticeView notice = shop.PaymentNotice;
            Assert.That(notice.NotificationCount, Is.EqualTo(count));
            Assert.That(notice.PaidAmount, Is.EqualTo(paid));
            Assert.That(notice.RemainingBalance, Is.EqualTo(balance));
            Assert.That(notice.PaidText, Is.EqualTo(string.Format(LocalizationService.Get(PaymentNoticeView.PaidKey), paid)));
            Assert.That(notice.BalanceText, Is.EqualTo(string.Format(LocalizationService.Get(PaymentNoticeView.BalanceKey), balance)));
            Assert.That(notice.ShouldRender, Is.True, "An accepted payment remains visible during the bar modal.");
            Assert.That(notice.PaymentSoundPlayed, Is.True, "The accepted transaction must play its shared payment cue.");
            Assert.That(PaymentNoticeView.ResolvePanelRect().x, Is.LessThan(20f), "The receipt belongs at the left HUD edge.");
        }

        private static IEnumerator CapturePaymentNotice(BarInteriorRoot bar, string name)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.BarInterior);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            FocusBarCaptureGameView();
            yield return null;
            yield return null;
            // Camera.Render excludes IMGUI. Queue the actual game view after
            // OnGUI, using the existing visible-Game-view capture workflow.
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path), Is.True, "The actual payment HUD frame was not written.");
            Assert.That(bar.DrinkShop.PaymentNotice.ShouldRender, Is.True);
        }

        private static void FocusBarCaptureGameView()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode) return;
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(viewType, Is.Not.Null);
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show(); view.Focus(); view.Repaint();
#endif
        }

        private sealed class DrinkActionLookContract : IDisposable
        {
            private readonly BarInteriorRoot bar;
            private readonly DrinkId drinkId;
            private readonly bool repeatGesture;
            private readonly CounterSeatView view;
            private readonly Camera camera;
            private readonly Transform head;
            private readonly Keyboard keyboard;
            private readonly Keyboard previousKeyboard;
            private readonly Mouse mouse;
            private readonly Mouse previousMouse;
            private readonly bool previousFocus;
            private readonly bool previousRunInBackground;
            private readonly InputSettings inputSettings;
            private readonly InputSettings.BackgroundBehavior previousBackgroundBehavior;
            private readonly InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
            private readonly Dictionary<BarDrinkServicePhase, LookRange> phases = new();
            private readonly BarDrinkServicePhase[] expectedPhases;
            private readonly KeyboardState positive = new(Key.RightArrow, Key.UpArrow);
            private readonly KeyboardState negative = new(Key.LeftArrow, Key.DownArrow);
            private Quaternion neutralCameraInHead;
            private float initialYaw, initialPitch, initialFov, lastYaw, lastPitch;
            private float minimumYaw, maximumYaw, minimumPitch, maximumPitch;
            private bool initialized, repeatBartenderFramed;

            private sealed class LookRange
            {
                internal bool Positive, Negative, Moved, Captured;
                internal float MinimumYaw = float.PositiveInfinity, MaximumYaw = float.NegativeInfinity;
                internal float MinimumPitch = float.PositiveInfinity, MaximumPitch = float.NegativeInfinity;
                internal string LastInput;
            }

            internal DrinkActionLookContract(BarInteriorRoot bar, DrinkId drinkId, bool repeatGesture = false,
                BarDrinkServicePhase gesturePhase = BarDrinkServicePhase.EmptyOnCounter)
            {
                this.bar = bar;
                this.drinkId = drinkId;
                this.repeatGesture = repeatGesture;
                expectedPhases = repeatGesture
                    ? new[] { gesturePhase }
                    : new[] { BarDrinkServicePhase.PlayerPickup, BarDrinkServicePhase.PlayerDrinking,
                        BarDrinkServicePhase.PlayerVesselReturn };
                view = bar.CounterStations[bar.CounterStations.Count - 1].SeatView;
                camera = Camera.main;
                head = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.Anchors.Head;
                previousKeyboard = Keyboard.current;
                previousMouse = Mouse.current;
                previousFocus = GameCursor.ApplicationFocused;
                previousRunInBackground = Application.runInBackground;
                inputSettings = InputSystem.settings;
                previousBackgroundBehavior = inputSettings.backgroundBehavior;
                previousEditorInputBehavior = inputSettings.editorInputBehaviorInPlayMode;
                try
                {
                    Application.runInBackground = true;
                    inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    inputSettings.editorInputBehaviorInPlayMode =
                        InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    GameCursor.SetApplicationFocused(true);
                    keyboard = InputSystem.AddDevice<Keyboard>("Test drink camera controls");
                    mouse = InputSystem.AddDevice<Mouse>("Test drink camera pointer");
                    InputSystem.EnableDevice(keyboard);
                    InputSystem.EnableDevice(mouse);
                    InputSystem.QueueStateEvent(mouse, new MouseState());
                    InputSystem.onAfterUpdate += SelectOwnedInputDevices;
                    SelectOwnedInputDevices();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal IEnumerator AimAtRepeatVessel(BarDrinkVesselView vessel)
            {
                Assert.That(view.IsActionLookLocked, Is.False);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                int startFrame = Time.frameCount;
                float startedAt = Time.realtimeSinceStartup;
                while (!vessel.IsLookingAt(camera) && HasBarSimulationBudget(startFrame, 120, startedAt, 10f))
                {
                    GameCursor.SetApplicationFocused(true);
                    Vector3 local = camera.transform.InverseTransformDirection(
                        vessel.GlassRenderer.bounds.center - camera.transform.position);
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    float elevation = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                    var state = new KeyboardState();
                    state.Set(Key.RightArrow, yaw > .35f);
                    state.Set(Key.LeftArrow, yaw < -.35f);
                    state.Set(Key.UpArrow, elevation > .35f);
                    state.Set(Key.DownArrow, elevation < -.35f);
                    InputSystem.QueueStateEvent(keyboard, state);
                    yield return null;
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                    "Test empty vessel gaze before repeat " + drinkId);
                Assert.That(vessel.IsLookingAt(camera), Is.True,
                    "The ordinary seated arrow controls did not reach the empty vessel before E. " +
                    $"pitch={view.ViewPitch:F4}, viewport={camera.WorldToViewportPoint(vessel.GlassRenderer.bounds.center):F5}; " +
                    DescribeLookInput());
            }

            internal void Observe(BarDrinkServicePhase phase, bool holdInput = false)
            {
                string sampledInput = DescribeLookInput();
                GameCursor.SetApplicationFocused(true);
                Assert.That(view.IsActionLimitedLookEnabled, Is.True,
                    "The active bar action must accept the shared look controls.");
                Assert.That(view.CameraFollow.OrbitInputEnabled, Is.True);
                if (!initialized)
                {
                    initialYaw = view.ViewYaw;
                    initialPitch = view.ViewPitch;
                    initialFov = camera.fieldOfView;
                    minimumYaw = repeatGesture ? -70f - initialYaw : Mathf.Max(-12f, -70f - initialYaw);
                    maximumYaw = repeatGesture ? 70f - initialYaw : Mathf.Min(12f, 70f - initialYaw);
                    minimumPitch = repeatGesture ? -25f - initialPitch : Mathf.Max(-10f, -25f - initialPitch);
                    maximumPitch = repeatGesture ? 55f - initialPitch : Mathf.Min(10f, 55f - initialPitch);
                    neutralCameraInHead = Quaternion.Inverse(head.rotation) * camera.transform.rotation *
                        Quaternion.Inverse(LookDelta(view.ActionLookYaw, view.ActionLookPitch));
                    initialized = true;
                }
                Assert.That(view.ActionLookYaw, Is.InRange(minimumYaw - .001f, maximumYaw + .001f));
                Assert.That(view.ActionLookPitch, Is.InRange(minimumPitch - .001f, maximumPitch + .001f));
                Quaternion trackedNeutral = head.rotation * neutralCameraInHead;
                Quaternion expected = trackedNeutral * LookDelta(view.ActionLookYaw, view.ActionLookPitch);
                Assert.That(Quaternion.Angle(camera.transform.rotation, expected), Is.LessThan(.15f),
                    $"The actual {phase} camera did not follow the animated head and manual gaze.");
                Assert.That(camera.fieldOfView, Is.EqualTo(initialFov).Within(.001f),
                    "Manual drink look must retain the action lens.");
                if (!phases.TryGetValue(phase, out LookRange range))
                    phases.Add(phase, range = new LookRange());
                range.MinimumYaw = Mathf.Min(range.MinimumYaw, view.ActionLookYaw);
                range.MaximumYaw = Mathf.Max(range.MaximumYaw, view.ActionLookYaw);
                range.MinimumPitch = Mathf.Min(range.MinimumPitch, view.ActionLookPitch);
                range.MaximumPitch = Mathf.Max(range.MaximumPitch, view.ActionLookPitch);
                range.LastInput = sampledInput;
                range.Positive |= view.ActionLookYaw >= maximumYaw - .1f && view.ActionLookPitch <= minimumPitch + .1f;
                range.Negative |= view.ActionLookYaw <= minimumYaw + .1f && view.ActionLookPitch >= maximumPitch - .1f;
                range.Moved |= Quaternion.Angle(trackedNeutral, camera.transform.rotation) > 3f;
                if (repeatGesture && !repeatBartenderFramed && range.Positive &&
                    view.ActionLookPitch <= minimumPitch + .1f &&
                    view.Seat.Controller.NestedLoopActionPhase == PlayerAnimatedInteractionPhase.Looping &&
                    bar.DrinkShop.RepeatGesture.IndexTapAngleDegrees >= 55f)
                {
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                        "Test raised repeat view while tapping " + drinkId);
                    float actorRange = Vector3.ProjectOnPlane(
                        bar.Bartender.Registry.Head.position - camera.transform.position, Vector3.up).magnitude;
                    AssertBartenderHeightVisible(bar.Bartender.Registry.Head.position.y, actorRange, "head");
                    AssertBartenderHeightVisible(bar.Bartender.Registry.Chest.position.y, actorRange, "chest");
                    repeatBartenderFramed = true;
                }
                if (range.Positive && !range.Captured)
                {
                    range.Captured = true;
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                        "Test limited drink view " + phase + " " + drinkId);
                }
                InputSystem.QueueStateEvent(keyboard,
                    holdInput ? new KeyboardState() : !range.Positive || repeatGesture && !repeatBartenderFramed
                        ? positive : !range.Negative ? negative : new KeyboardState());
                if (holdInput)
                {
                    Assert.That(view.ActionLookYaw, Is.Zero.Within(.001f), "Observe the wave from the original glass-directed gaze.");
                    Assert.That(view.ActionLookPitch, Is.Zero.Within(.001f), "The test must not tilt up to find the waving hand.");
                }
                lastYaw = view.ActionLookYaw;
                lastPitch = view.ActionLookPitch;
            }

            internal IEnumerator VerifyRestored()
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                foreach (BarDrinkServicePhase phase in expectedPhases)
                {
                    Assert.That(phases.TryGetValue(phase, out LookRange range), Is.True);
                    Assert.That(range.Positive && range.Negative && range.Moved, Is.True,
                        $"Real arrow input did not reach both available view limits during {phase}; " +
                        $"initial={initialYaw:F4}/{initialPitch:F4}, expectedYaw=[{minimumYaw:F4},{maximumYaw:F4}], " +
                        $"expectedPitch=[{minimumPitch:F4},{maximumPitch:F4}], " +
                        $"observedYaw=[{range.MinimumYaw:F4},{range.MaximumYaw:F4}], " +
                        $"observedPitch=[{range.MinimumPitch:F4},{range.MaximumPitch:F4}], " +
                        $"positive={range.Positive}, negative={range.Negative}, moved={range.Moved}; {range.LastInput}.");
                }
                if (repeatGesture)
                    Assert.That(repeatBartenderFramed, Is.True,
                        "The real upward input must cover bartender head/chest heights during an actual index tap.");
                Assert.That(view.IsActionLookLocked, Is.False);
                Assert.That(view.IsActionLimitedLookEnabled, Is.False);
                Assert.That(view.ActionLookYaw, Is.Zero.Within(.001f));
                Assert.That(view.ActionLookPitch, Is.Zero.Within(.001f));
                Assert.That(view.ViewYaw, Is.EqualTo(Mathf.Clamp(initialYaw + lastYaw, -70f, 70f)).Within(.01f));
                Assert.That(view.ViewPitch, Is.EqualTo(Mathf.Clamp(initialPitch + lastPitch, -25f, 55f)).Within(.01f));
                float previousYaw = view.ViewYaw;
                Quaternion previousRotation = camera.transform.rotation;
                int inputStartFrame = Time.frameCount;
                float inputStartedAt = Time.realtimeSinceStartup;
                GameCursor.SetApplicationFocused(true);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow));
                while (view.ViewYaw - previousYaw <= .5f &&
                       HasBarSimulationBudget(inputStartFrame, 30, inputStartedAt, 3f))
                {
                    GameCursor.SetApplicationFocused(true);
                    yield return null;
                }
                string heldInput = DescribeLookInput();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.That(view.ViewYaw - previousYaw, Is.GreaterThan(.5f),
                    "Ordinary seated look did not resume after the drink. " +
                    $"Held: {heldInput}; finished: {DescribeLookInput()}; " +
                    $"frames={Time.frameCount - inputStartFrame}, realSeconds={Time.realtimeSinceStartup - inputStartedAt:F4}.");
                Assert.That(Quaternion.Angle(previousRotation, camera.transform.rotation), Is.GreaterThan(.5f));
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                    "Test ordinary seated look restored " + drinkId);
            }

            private Quaternion LookDelta(float yaw, float pitch)
                => Quaternion.Inverse(Quaternion.Euler(initialPitch, initialYaw, 0f)) *
                   Quaternion.Euler(initialPitch + pitch, initialYaw + yaw, 0f);

            private void AssertBartenderHeightVisible(float height, float horizontalRange, string landmark)
            {
                // Measure vertical coverage at the actor's real range. Its
                // unrelated sideways station must not change the pitch proof.
                Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
                Assert.That(forward.sqrMagnitude, Is.GreaterThan(.001f));
                Assert.That(horizontalRange, Is.GreaterThan(camera.nearClipPlane));
                Vector3 gauge = camera.transform.position + forward.normalized * horizontalRange;
                gauge.y = height;
                Vector3 viewport = camera.WorldToViewportPoint(gauge);
                string details = $"Raised tap view excludes {landmark} height {height:F5}m at actor range {horizontalRange:F5}m: " +
                    $"heightGauge={gauge:F5}, viewport={viewport:F5}, " +
                    $"initialPitch={initialPitch:F4}, actionPitch={view.ActionLookPitch:F4}, " +
                    $"camera={camera.transform.position:F5}/{camera.transform.rotation.eulerAngles:F4}.";
                Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane), details);
                Assert.That(viewport.y, Is.InRange(0f, 1f), details);
            }

            private string DescribeLookInput()
            {
                string focusedWindow = "Player";
#if UNITY_EDITOR
                focusedWindow = UnityEditor.EditorWindow.focusedWindow?.GetType().Name ?? "None";
#endif
                return $"focus={GameCursor.ApplicationFocused}/{Application.isFocused}/{focusedWindow}, " +
                    $"firstPerson={view.IsFirstPerson}, seated={view.Seat.IsSeated}, seatPhase={view.Seat.Controller.Phase}, " +
                    $"servicePhase={bar.DrinkShop.Phase}, menuFocus={view.IsMenuFocusLocked}/{view.MenuFocusWeight:F4}, " +
                    $"actionLock={view.IsActionLookLocked}/{view.IsActionLimitedLookEnabled}, " +
                    $"orbit={view.CameraFollow.OrbitInputEnabled}, targetLock={view.CameraFollow.TargetLockActive}, " +
                    $"paused={PauseMenuController.IsAnyPaused}/{GameTimeScaleRuntime.IsPaused}, " +
                    $"keyboardCurrent={Keyboard.current?.name}, ownCurrent={Keyboard.current == keyboard}, " +
                    $"rightArrow={keyboard.rightArrowKey.isPressed}/{Keyboard.current?.rightArrowKey.isPressed}, " +
                    $"keyboardEnabled={keyboard.enabled}, mouseCurrent={Mouse.current?.name}, ownMouse={Mouse.current == mouse}, " +
                    $"background={inputSettings.backgroundBehavior}, editorInput={inputSettings.editorInputBehaviorInPlayMode}, " +
                    $"inputUpdate={InputSystem.settings.updateMode}, timeScale={Time.timeScale:F4}, " +
                    $"unscaledDelta={Time.unscaledDeltaTime:F6}, captureDelta={Time.captureDeltaTime:F6}, yaw={view.ViewYaw:F4}";
            }

            public void Dispose()
            {
                InputSystem.onAfterUpdate -= SelectOwnedInputDevices;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                inputSettings.backgroundBehavior = previousBackgroundBehavior;
                inputSettings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
                Application.runInBackground = previousRunInBackground;
                GameCursor.SetApplicationFocused(previousFocus);
            }

            private void SelectOwnedInputDevices()
            {
                if (keyboard != null && keyboard.added) keyboard.MakeCurrent();
                if (mouse != null && mouse.added) mouse.MakeCurrent();
                GameCursor.SetApplicationFocused(true);
            }
        }

        private sealed class RepeatHandSurfaceAudit
        {
            private sealed class Part
            {
                internal CpuBodySurface Surface;
                internal Transform Hand;
                internal bool Left, Index, Palm, Thumb;
                internal int FistShape;
                internal int[] Roots, Tips;
                internal Vector3[] Current, Held;
                internal float NeutralChord;
            }

            private readonly BarInteriorRoot bar;
            private readonly BarRepeatGesture gesture;
            private readonly NpcHandPose pose;
            private readonly List<Part> parts = new();
            private readonly List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid> counter = new();
            private readonly Matrix4x4 toCounter;
            private readonly float counterInset;
            private readonly Transform rightHand, leftHand, leftShoulder;
            private readonly Vector3 initialLeftPalm;
            private Part indexPart;
            private Pose heldRight;
            private Vector3 heldIndexRoot;
            private bool hasHeldPose, sawEnter, sawLoop, sawExit, waveCaptured, exitVerified;
            private bool hasWaveSample;
            private Vector3 waveSideAxis, waveForwardAxis;
            private int previousFrame = -1, waveReversals, waveDirection;
            private float previousWaveLateral, minimumWaveLateral = float.PositiveInfinity;
            private float maximumWaveLateral = float.NegativeInfinity, maximumWaveLift, maximumWaveFacing;
            internal Vector3 IndexLowestPoint { get; private set; }
            internal bool WaveFramed => waveCaptured;

            internal RepeatHandSurfaceAudit(BarInteriorRoot bar, BarRepeatGesture gesture)
            {
                this.bar = bar; this.gesture = gesture;
                pose = bar.Player.GameObject.GetComponentInChildren<NpcHandPose>(true);
                var registry = ((Player3DCharacterPresentation)bar.Player.Visual).Registry;
                Assert.That(registry.TryGetPart(Player3DAnatomicalPart.LeftUpperArm, out Player3DAnatomicalPartBinding shoulder), Is.True);
                leftShoulder = shoulder.Bone;
                foreach (NpcHandPose.HandBinding binding in pose.Hands)
                {
                    if (binding.IsLeft) leftHand = binding.Hand; else rightHand = binding.Hand;
                    foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        var part = new Part { Surface = new CpuBodySurface(renderer), Hand = binding.Hand,
                            Left = binding.IsLeft, Index = renderer.name.StartsWith("GEO_Finger0.", StringComparison.Ordinal),
                            Palm = renderer.name.StartsWith("GEO_Hand.", StringComparison.Ordinal),
                            Thumb = renderer.name.StartsWith("GEO_Thumb.", StringComparison.Ordinal),
                            FistShape = CharacterJointDeformation.FindShape(renderer.sharedMesh, "PointingFist") };
                        part.Current = part.Surface.ReadWorld();
                        if (!part.Palm)
                        {
                            Vector3 axis = binding.Hand.rotation * pose.GetAnatomicalFrame(binding.IsLeft).FingerDirectionInHand;
                            (part.Roots, part.Tips) = FindDigitCaps(part.Current, part.Surface.Triangles, axis, renderer.name);
                            part.NeutralChord = Vector3.Distance(Centre(part.Current, part.Roots), Centre(part.Current, part.Tips));
                        }
                        if (!part.Left)
                        {
                            if (!part.Palm && !part.Index)
                                Assert.That(part.FistShape, Is.GreaterThanOrEqualTo(0), renderer.name + " requires the authored pointing fist.");
                            if (part.Index) indexPart = part;
                        }
                        parts.Add(part);
                    }
                }
                Assert.That(parts.Count, Is.EqualTo(12), "Inspect the actual palm and all five digits of both hands.");
                Assert.That(indexPart, Is.Not.Null);
                initialLeftPalm = leftHand.position + leftHand.rotation * pose.GetAnatomicalFrame(true).PalmCentreInHand;
                Transform wood = bar.Room.Find("Counter Top");
                Assert.That(wood, Is.Not.Null);
                MeshFilter filter = wood.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                BarDrinkServiceWorldBuilderPlayModeTests.ReadSolidMesh(filter.sharedMesh, out Vector3[] vertices, out int[] triangles);
                BarDrinkServiceWorldBuilderPlayModeTests.AddClosedPropPieces(counter, wood.name, vertices, triangles);
                toCounter = wood.worldToLocalMatrix;
                Vector3 scale = wood.lossyScale;
                counterInset = .0001f / Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            }

            internal void Observe(PlayerAnimatedInteractionPhase phase)
            {
                if (previousFrame >= 0)
                    Assert.That(Time.frameCount - previousFrame, Is.EqualTo(1), "Inspect both hands on every final rendered gesture frame.");
                previousFrame = Time.frameCount;
                foreach (Part part in parts)
                {
                    Assert.That(part.Surface.Renderer.enabled && part.Surface.Renderer.gameObject.activeInHierarchy, Is.True);
                    part.Current = part.Surface.ReadWorld();
                    foreach (var solid in counter)
                        for (int triangle = 0; triangle < part.Surface.Triangles.Length; triangle += 3)
                            if (solid.IntersectsConvexInterior(
                                toCounter.MultiplyPoint3x4(part.Current[part.Surface.Triangles[triangle]]),
                                toCounter.MultiplyPoint3x4(part.Current[part.Surface.Triangles[triangle + 1]]),
                                toCounter.MultiplyPoint3x4(part.Current[part.Surface.Triangles[triangle + 2]]), counterInset))
                            {
                                CaptureBarWorkPose(bar, "Test repeat hand counter collision", part.Hand.position);
                                Assert.Fail($"Actual {part.Surface.Renderer.name} intersects the counter during {phase}; " +
                                    $"gesture={gesture.Progress:F5}, indexAngle={gesture.IndexTapAngleDegrees:F3}.");
                            }
                }
                IndexLowestPoint = indexPart.Current[0];
                foreach (Vector3 point in indexPart.Current) if (point.y < IndexLowestPoint.y) IndexLowestPoint = point;
                if (phase == PlayerAnimatedInteractionPhase.Entering)
                {
                    if (!sawEnter) VerifyBakedSkin("Test repeat approach hand");
                    sawEnter = true;
                    ObserveWave();
                }
                if (phase == PlayerAnimatedInteractionPhase.Looping && !gesture.IsOfferingVessel && !gesture.CompletedVesselOffer)
                {
                    sawLoop = true;
                    AssertWaveComplete();
                    Assert.That(Vector3.Angle(pose.AnatomicalPalmNormal(false), Vector3.down), Is.LessThan(1f),
                        "The tapping palm must lie horizontal over the counter.");
                    Assert.That(Mathf.Abs(Vector3.Dot(pose.AnatomicalFingerDirection(false), Vector3.up)), Is.LessThan(.02f));
                    if (!hasHeldPose)
                    {
                        hasHeldPose = true;
                        heldRight = new Pose(rightHand.position, rightHand.rotation);
                        heldIndexRoot = Centre(indexPart.Current, indexPart.Roots);
                        foreach (Part part in parts)
                            if (!part.Left && !part.Index) part.Held = (Vector3[])part.Current.Clone();
                    }
                    Assert.That(Vector3.Distance(rightHand.position, heldRight.position), Is.LessThan(.001f),
                        "The wrist moved instead of the index finger.");
                    Assert.That(Quaternion.Angle(rightHand.rotation, heldRight.rotation), Is.LessThan(.1f));
                    Assert.That(Vector3.Distance(Centre(indexPart.Current, indexPart.Roots), heldIndexRoot), Is.LessThan(.001f),
                        "The articulated index lost its embedded root.");
                    Assert.That(Vector3.Distance(Centre(indexPart.Current, indexPart.Roots), Centre(indexPart.Current, indexPart.Tips)),
                        Is.GreaterThan(indexPart.NeutralChord * .97f), "The pointing index must remain extended.");
                    foreach (Part part in parts)
                    {
                        if (part.Left || part.Index) continue;
                        if (!part.Palm)
                            Assert.That(part.Surface.Renderer.GetBlendShapeWeight(part.FistShape), Is.GreaterThan(99f));
                        float lowest = float.PositiveInfinity, motion = 0f;
                        for (int vertex = 0; vertex < part.Current.Length; vertex++)
                        {
                            lowest = Mathf.Min(lowest, part.Current[vertex].y - bar.DrinkServiceView.CounterSurfaceWorldHeight);
                            motion = Mathf.Max(motion, Vector3.Distance(part.Current[vertex], part.Held[vertex]));
                        }
                        Assert.That(lowest, Is.GreaterThanOrEqualTo(.0029f), part.Surface.Renderer.name + " must stay above the wood.");
                        Assert.That(motion, Is.LessThan(.001f), "Only the index may move: " + part.Surface.Renderer.name);
                        if (!part.Palm)
                            Assert.That(Vector3.Distance(Centre(part.Current, part.Roots), Centre(part.Current, part.Tips)),
                                Is.LessThan(part.NeutralChord * (part.Thumb ? .95f : .65f)),
                                part.Surface.Renderer.name + " must be geometrically clenched/tucked.");
                    }
                }
                if (phase == PlayerAnimatedInteractionPhase.Exiting)
                {
                    sawExit = true;
                    if (!exitVerified) { exitVerified = true; VerifyBakedSkin("Test repeat exit hand"); }
                }
            }

            private void ObserveWave()
            {
                Vector3 palm = leftHand.position + leftHand.rotation * pose.GetAnatomicalFrame(true).PalmCentreInHand;
                maximumWaveLift = Mathf.Max(maximumWaveLift, palm.y - initialLeftPalm.y);
                if (!gesture.IsLeftHandWaving) return;
                if (!hasWaveSample)
                {
                    waveForwardAxis = Vector3.ProjectOnPlane(pose.AnatomicalPalmNormal(true), Vector3.up).normalized;
                    waveSideAxis = Vector3.Cross(Vector3.up, pose.AnatomicalPalmNormal(true)).normalized;
                    Assert.That(waveSideAxis.sqrMagnitude, Is.GreaterThan(.5f), "Measure the wave in its actual palm-facing plane.");
                }
                float lateral = Vector3.Dot(palm - initialLeftPalm, waveSideAxis);
                minimumWaveLateral = Mathf.Min(minimumWaveLateral, lateral);
                maximumWaveLateral = Mathf.Max(maximumWaveLateral, lateral);
                Vector3 towardBartender = (bar.Bartender.Registry.Chest.position - palm).normalized;
                maximumWaveFacing = Mathf.Max(maximumWaveFacing, Vector3.Dot(pose.AnatomicalPalmNormal(true), towardBartender));
                float delta = lateral - previousWaveLateral;
                if (hasWaveSample && Mathf.Abs(delta) > .0002f)
                {
                    int direction = delta > 0f ? 1 : -1;
                    if (waveDirection != 0 && direction != waveDirection) waveReversals++;
                    waveDirection = direction;
                }
                previousWaveLateral = lateral;
                hasWaveSample = true;
                if (!waveCaptured && waveReversals >= 1 && maximumWaveLift > .08f)
                {
                    VerifyBakedSkin("Test left hand wave");
                    AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior, "Test first-person left hand wave");
                    CaptureBarWorkPose(bar, "Test left hand wave toward bartender", palm);
                    Assert.That(Vector3.Dot(palm - leftShoulder.position, waveForwardAxis), Is.GreaterThan(.28f),
                        "The waving palm must reach forward from the shoulder instead of rising beside the head.");
                    foreach (Part part in parts)
                        if (part.Left)
                            foreach (Vector3 point in part.Current)
                            {
                                Vector3 screen = Camera.main.WorldToViewportPoint(point);
                                string details = $"Waving {part.Surface.Renderer.name} leaves the original first-person frame: " +
                                    $"vertex={point:F5}, viewport={screen:F5}, wave={gesture.LeftWaveProgress:F5}, " +
                                    $"camera={Camera.main.transform.position:F5}/{Camera.main.transform.rotation.eulerAngles:F3}.";
                                Assert.That(screen.z, Is.InRange(Camera.main.nearClipPlane, Camera.main.farClipPlane), details);
                                Assert.That(screen.x, Is.InRange(0f, 1f), details);
                                Assert.That(screen.y, Is.InRange(0f, 1f), details);
                            }
                    waveCaptured = true;
                }
            }

            private void AssertWaveComplete()
            {
                Assert.That(gesture.LeftWaveComplete, Is.True, "The left wave must finish before tapping begins.");
                Assert.That(maximumWaveLift, Is.GreaterThan(.08f));
                Assert.That(maximumWaveLateral - minimumWaveLateral, Is.GreaterThan(.06f));
                Assert.That(waveReversals, Is.GreaterThanOrEqualTo(2));
                Assert.That(maximumWaveFacing, Is.GreaterThan(.5f), "The lifted palm must face the bartender.");
                float leftLow = float.PositiveInfinity;
                foreach (Part part in parts)
                    if (part.Left) foreach (Vector3 point in part.Current) leftLow = Mathf.Min(leftLow, point.y);
                Assert.That(leftLow - bar.DrinkServiceView.CounterSurfaceWorldHeight, Is.InRange(-.0001f, .01f),
                    "The waving left hand must return to its counter brace before the index contacts.");
            }

            internal void AssertContact(int number)
            {
                AssertWaveComplete();
                VerifyBakedSkin("Test flat palm index contact " + number);
            }

            internal void AssertComplete()
            {
                Assert.That(sawEnter && sawLoop && sawExit && waveCaptured, Is.True,
                    "Inspect enter, actual left wave, flat tapping and exit without missed phases.");
            }

            private void VerifyBakedSkin(string label)
            {
                foreach (Part part in parts)
                {
                    Vector3[] baked = BarDrinkServiceWorldBuilderPlayModeTests.ReadVerifiedHandWorldVertices(
                        part.Surface.Renderer, part.Hand, label);
                    float largest = 0f;
                    for (int vertex = 0; vertex < baked.Length; vertex++)
                        largest = Mathf.Max(largest, Vector3.Distance(baked[vertex], part.Current[vertex]));
                    Assert.That(largest, Is.LessThan(.001f), label + "/" + part.Surface.Renderer.name);
                }
            }

            private static Vector3 Centre(Vector3[] points, int[] selected)
            {
                Vector3 sum = Vector3.zero;
                foreach (int index in selected) sum += points[index];
                return sum / selected.Length;
            }

            private static (int[] roots, int[] tips) FindDigitCaps(Vector3[] points, int[] triangles,
                Vector3 anatomicalDirection, string label)
            {
                Vector3 mean = Vector3.zero;
                foreach (Vector3 point in points) mean += point;
                mean /= points.Length;
                float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
                foreach (Vector3 point in points)
                {
                    Vector3 delta = point - mean;
                    xx += delta.x * delta.x; xy += delta.x * delta.y; xz += delta.x * delta.z;
                    yy += delta.y * delta.y; yz += delta.y * delta.z; zz += delta.z * delta.z;
                }
                Vector3 axis = anatomicalDirection.normalized;
                for (int iteration = 0; iteration < 16; iteration++)
                    axis = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z,
                        xy * axis.x + yy * axis.y + yz * axis.z,
                        xz * axis.x + yz * axis.y + zz * axis.z).normalized;
                if (Vector3.Dot(axis, anatomicalDirection) < 0f) axis = -axis;
                Vector3 rootPoint = default, rootNormal = default, tipPoint = default, tipNormal = default;
                float first = float.PositiveInfinity, last = float.NegativeInfinity;
                for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                {
                    Vector3 a = points[triangles[triangle]], b = points[triangles[triangle + 1]], c = points[triangles[triangle + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (Mathf.Abs(Vector3.Dot(normal, axis)) < .95f) continue;
                    float station = Vector3.Dot((a + b + c) / 3f, axis);
                    if (station < first) { first = station; rootPoint = a; rootNormal = normal; }
                    if (station > last) { last = station; tipPoint = a; tipNormal = normal; }
                }
                Assert.That(first, Is.LessThan(last), label + " requires actual neutral end-cap planes.");
                return (Cap(rootPoint, rootNormal), Cap(tipPoint, tipNormal));

                int[] Cap(Vector3 origin, Vector3 normal)
                {
                    var unique = new Dictionary<Vector3Int, int>();
                    for (int vertex = 0; vertex < points.Length; vertex++)
                        if (Mathf.Abs(Vector3.Dot(points[vertex] - origin, normal)) < .00001f)
                        {
                            Vector3Int key = Vector3Int.RoundToInt(points[vertex] * 1000000f);
                            if (!unique.ContainsKey(key)) unique.Add(key, vertex);
                        }
                    Assert.That(unique.Count, Is.GreaterThanOrEqualTo(6), label + " must include the complete cap ring.");
                    var result = new int[unique.Count]; unique.Values.CopyTo(result, 0);
                    return result;
                }
            }
        }

        private sealed class MenuBodyClearance
        {
            private readonly BarInteriorRoot bar;
            private readonly List<(MeshFilter filter, List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid> solids)> book = new();
            private readonly List<CpuBodySurface> body = new();
            private readonly List<(MeshFilter filter, Vector3[] points, int[] triangles)> cups = new();
            internal MenuBodyClearance(BarInteriorRoot bar, BarDrinkVesselView offeredCup = null)
            {
                this.bar = bar;
                foreach (MeshFilter filter in bar.DrinkShop.MenuPresentation.PropRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.name.Contains("Highlight") || filter.GetComponentInParent<TMPro.TMP_Text>() != null) continue;
                    BarDrinkServiceWorldBuilderPlayModeTests.ReadSolidMesh(filter.sharedMesh, out Vector3[] points, out int[] triangles);
                    var solids = new List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid>();
                    BarDrinkServiceWorldBuilderPlayModeTests.AddClosedPropPieces(solids, filter.name, points, triangles);
                    book.Add((filter, solids));
                }
                Transform actor = offeredCup != null ? bar.Player.GameObject.transform : bar.Bartender.transform;
                foreach (SkinnedMeshRenderer renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string name = renderer.name;
                    bool hand = name.Contains("Hand") || name.Contains("Finger") || name.Contains("Thumb") || name.Contains("Glove");
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || (offeredCup != null ? !hand : hand)) continue;
                    body.Add(new CpuBodySurface(renderer));
                }
                if (offeredCup != null)
                    foreach (MeshFilter filter in offeredCup.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Renderer renderer = filter.GetComponent<Renderer>();
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                            renderer == offeredCup.InteractionHighlightRenderer) continue;
                        BarDrinkServiceWorldBuilderPlayModeTests.ReadSolidMesh(filter.sharedMesh, out Vector3[] points, out int[] triangles);
                        cups.Add((filter, points, triangles));
                    }
            }
            internal void AssertClear()
            {
                foreach (CpuBodySurface surface in body)
                {
                    if (!surface.Renderer.enabled || !surface.Renderer.gameObject.activeInHierarchy) continue;
                    Vector3[] points = surface.ReadWorld();
                    foreach (var part in book)
                    {
                        Renderer renderer = part.filter.GetComponent<Renderer>();
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        Matrix4x4 toBook = part.filter.transform.worldToLocalMatrix;
                        Vector3 scale = part.filter.transform.lossyScale;
                        float inset = .0001f / Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        foreach (var solid in part.solids)
                            for (int triangle = 0; triangle < surface.Triangles.Length; triangle += 3)
                                if (solid.IntersectsConvexInterior(toBook.MultiplyPoint3x4(points[surface.Triangles[triangle]]),
                                    toBook.MultiplyPoint3x4(points[surface.Triangles[triangle + 1]]),
                                    toBook.MultiplyPoint3x4(points[surface.Triangles[triangle + 2]]), inset))
                                {
                                    CaptureBarWorkPose(bar, "Test menu body clearance failure", part.filter.transform.position);
                                    Assert.Fail($"Physical menu {part.filter.name} crosses visible {surface.Renderer.name} during handoff.");
                                }
                    }
                }
                foreach (var cup in cups)
                {
                    Renderer cupRenderer = cup.filter.GetComponent<Renderer>();
                    if (cupRenderer == null || !cupRenderer.enabled || !cupRenderer.gameObject.activeInHierarchy) continue;
                    foreach (var part in book)
                    {
                        Renderer paper = part.filter.GetComponent<Renderer>();
                        if (paper == null || !paper.enabled || !paper.gameObject.activeInHierarchy) continue;
                        Matrix4x4 intoBook = part.filter.transform.worldToLocalMatrix * cup.filter.transform.localToWorldMatrix;
                        Vector3 scale = part.filter.transform.lossyScale;
                        float inset = .0001f / Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        foreach (var solid in part.solids)
                            for (int triangle = 0; triangle < cup.triangles.Length; triangle += 3)
                                if (solid.IntersectsConvexInterior(intoBook.MultiplyPoint3x4(cup.points[cup.triangles[triangle]]),
                                    intoBook.MultiplyPoint3x4(cup.points[cup.triangles[triangle + 1]]),
                                    intoBook.MultiplyPoint3x4(cup.points[cup.triangles[triangle + 2]]), inset))
                                {
                                    CaptureBarWorkPose(bar, "Test offered mug menu clearance failure", cup.filter.transform.position);
                                    Assert.Fail($"Actual offered {cup.filter.name} intersects physical menu {part.filter.name}.");
                                }
                    }
                }
            }
        }

        private sealed class CpuBodySurface
        {
            internal readonly SkinnedMeshRenderer Renderer;
            internal readonly int[] Triangles;
            private readonly Vector3[] source, result;
            private readonly BoneWeight[] weights;
            private readonly Transform[] bones;
            private readonly Matrix4x4[] bind, skin;
            private readonly Vector3[][] deltas;
            private readonly float[] frameWeights;
            internal CpuBodySurface(SkinnedMeshRenderer renderer)
            {
                Renderer = renderer;
                Mesh mesh = renderer.sharedMesh;
                BarDrinkServiceWorldBuilderPlayModeTests.ReadSolidMesh(mesh, out source, out Triangles);
                result = new Vector3[source.Length]; weights = mesh.boneWeights;
                bones = renderer.bones; bind = mesh.bindposes; skin = new Matrix4x4[bones.Length];
                deltas = new Vector3[mesh.blendShapeCount][]; frameWeights = new float[deltas.Length];
                for (int shape = 0; shape < deltas.Length; shape++)
                {
                    deltas[shape] = new Vector3[source.Length];
                    int frame = mesh.GetBlendShapeFrameCount(shape) - 1;
                    mesh.GetBlendShapeFrameVertices(shape, frame, deltas[shape], null, null);
                    frameWeights[shape] = mesh.GetBlendShapeFrameWeight(shape, frame);
                }
            }
            internal Vector3[] ReadWorld()
            {
                Array.Copy(source, result, source.Length);
                for (int shape = 0; shape < deltas.Length; shape++)
                {
                    float weight = Renderer.GetBlendShapeWeight(shape) / frameWeights[shape];
                    if (Mathf.Abs(weight) < .00001f) continue;
                    for (int vertex = 0; vertex < result.Length; vertex++) result[vertex] += deltas[shape][vertex] * weight;
                }
                for (int bone = 0; bone < bones.Length; bone++) skin[bone] = bones[bone].localToWorldMatrix * bind[bone];
                for (int vertex = 0; vertex < result.Length; vertex++)
                {
                    Vector3 point = result[vertex]; BoneWeight weight = weights[vertex];
                    result[vertex] = skin[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0 +
                        skin[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1 +
                        skin[weight.boneIndex2].MultiplyPoint3x4(point) * weight.weight2 +
                        skin[weight.boneIndex3].MultiplyPoint3x4(point) * weight.weight3;
                }
                return result;
            }
        }
    }
}
