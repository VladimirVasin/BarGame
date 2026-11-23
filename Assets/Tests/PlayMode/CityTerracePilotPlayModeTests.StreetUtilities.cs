using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_StreetUtilitiesWalkOpenCallAndLeave()
        {
            bool focused = GameCursor.ApplicationFocused;
            var input = new InputTestFixture();
            input.Setup();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                GameCursor.SetApplicationFocused(true);
                GameSessionState.TryStartGameTimeAt(12*60);
                yield return LoadProductionCity();
                var motor = city.Player.Motor;
                var sound = RetroAudioService.EnsureInstalled();
                var voices = sound.GetComponentsInChildren<AudioSource>();
                var heard = new System.Collections.Generic.HashSet<AudioClip>();
                var inputFailures = new System.Collections.Generic.List<string>();
                void Listen() { foreach (var voice in voices) if (voice.clip) heard.Add(voice.clip); }
                IEnumerator Press(Key key, CityStreetUtilitySession session)
                {
                    Assert.That(GameInput.CanRead(GameInputContext.Contextual), Is.True, "Context must own the exit key");
                    keyboard.MakeCurrent();
                    input.Press(keyboard[key],queueEventOnly:true);
                    for (int i=0;i<4 && (session.Phase==CityStreetUtilitySession.Step.Inspect ||
                        session.Phase==CityStreetUtilitySession.Step.Call);i++) yield return null;
                    var expected = key==Key.Q ? CityStreetUtilitySession.Step.HangingUp : CityStreetUtilitySession.Step.Leaving;
                    if (session.Phase != expected || !keyboard[key].isPressed || PauseMenuController.IsAnyPaused)
                    {
                        inputFailures.Add($"{key}: expected {expected}, got {session.Phase}, key down={keyboard[key].isPressed}, " +
                            $"current={Keyboard.current==keyboard}, contextual={GameInput.IsHeld(GameInputAction.LeavePhoneBooth)}, " +
                            $"paused={PauseMenuController.IsAnyPaused}");
                        // Finish the remaining independent contact, audio and exit checks before reporting.
                        session.RequestBoothExit();
                    }
                    else Debug.Log($"Booth {key}: {session.Phase}, pause menu closed");
                    input.Release(keyboard[key],queueEventOnly:true);
                    yield return null;
                    Assert.That(PauseMenuController.IsAnyPaused,Is.False,"Booth exit opened the pause menu");
                }
                var available = Object.FindObjectsByType<CityStreetUtilityInteraction>();
                var ridge = CityHillsideFurniturePlan.ForLayout(city.Layout);
                Assert.That(ridge.UtilityDocks.Any(d => d.Kind == CityStreetUtilityKind.Dumpster), Is.True, "Household bins on Ridge");
                Debug.Log("Municipal survey: " + string.Join(", ",available.GroupBy(d=>d.Dock.Kind).Select(s=>s.Key+" "+s.Count())) +
                    "; Ridge sites " + ridge.UtilityDocks.Count);
                bool capture = System.Environment.GetEnvironmentVariable("BARGAME_UTILITY_FRAMES")=="1";
                foreach (var kind in new[] {CityStreetUtilityKind.PhoneBooth,CityStreetUtilityKind.Dumpster})
                {
                    var utility = available.Where(u => u.Dock.Kind==kind).OrderBy(u=>u.Dock.Id.Contains("-homeyard-"))
                        .ThenBy(u=>u.Dock.Id).First();
                    var dock = utility.Dock;
                    var orientation = Quaternion.LookRotation(dock.Facing);
                    Vector3 start = dock.Approach + Vector3.up*PlayerFactory.GroundedRootOffset;
                    motor.Teleport(start);
                    Physics.SyncTransforms();
                    for(int i=0;i<5;i++) yield return null;
                    Assert.That(utility.Model, Is.Not.Null);
                    Assert.That(city.World.WalkableArea.Contains(dock.StandPosition,.32f), Is.True, dock.Id);
                    bool reached = false;
                    for(int i=0;i<95 && !reached;i++)
                    {
                        reached = motor.MoveTowardsSupportedInteractionPose(dock.StandPosition+Vector3.up*PlayerFactory.GroundedRootOffset,orientation,Time.deltaTime);
                        yield return null;
                    }
                    Assert.That(reached,Is.True,$"{dock.Id}: {start} to {dock.StandPosition}, stopped at {motor.transform.position}");
                    Assert.That(utility.CanInteract(city.Player.Interactor),Is.True,dock.Id);
                    if (capture) AreaCaptureFixture.CaptureCurrentCamera(city.Camera,"StreetUtilities",kind+"-street",960,540);
                    utility.Interact(city.Player.Interactor);
                    var session = motor.GetComponent<CityStreetUtilitySession>();
                    Assert.That(session, Is.Not.Null);
                    yield return Until(CityStreetUtilitySession.Step.Inspect);
                    Assert.That(utility.Model.OpenAmount,Is.EqualTo(1f));
                    Assert.That(city.Camera.nearClipPlane,Is.LessThan(.05f));
                    if (capture)
                        AreaCaptureFixture.CaptureCurrentCamera(city.Camera,"StreetUtilities",kind.ToString(),960,540);
                    if (kind==CityStreetUtilityKind.PhoneBooth)
                    {
                        Assert.That(utility.Model.transform.InverseTransformPoint(motor.transform.position).z,Is.LessThan(.3f));
                        Assert.That(session.Use(),Is.True);
                        var touched = new bool[3];
                        var tipErrors = new float[3];
                        var bends = new float[3];
                        var probe = motor.gameObject.AddComponent<StreetUtilityContactProbe>();
                        probe.Sample = () =>
                        {
                            int k = session.DepressedKey;
                            if (k<0 || touched[k] || Vector3.Distance(utility.Model.DialKey(k),utility.Model.KeyFace(k))<.0025f) return;
                            tipErrors[k] = Vector3.Distance(session.IndexTip,utility.Model.KeyFace(k));
                            bends[k] = session.WristBend;
                            touched[k] = true;
                            Debug.Log($"Payphone key {k}: tip {tipErrors[k]:F4} m, wrist {bends[k]:F1} degrees");
                            if (capture) AreaCaptureFixture.CaptureCurrentCamera(city.Camera,"StreetUtilities","Keypress-"+k,960,540);
                        };
                        for (int frame=0; frame<210 && !touched.All(t=>t); frame++) { Listen(); yield return null; }
                        probe.enabled = false;
                        Assert.That(touched,Is.All.True);
                        yield return Press(Key.Q,session);
                        Assert.That(tipErrors.Max(),Is.LessThan(.018f),"Index pad on each moving key");
                        Assert.That(bends.Max(),Is.LessThan(43f),"Wrist aligned with forearm");
                    }
                    else Assert.That(session.Use(),Is.True);
                    yield return Until(CityStreetUtilitySession.Step.Idle);
                    Assert.That(utility.Model.OpenAmount,Is.Zero);
                    Assert.That(motor.InputEnabled && city.Player.Interactor.InputEnabled,Is.True);
                    if (kind == CityStreetUtilityKind.PhoneBooth)
                    {
                        Assert.That(Vector3.Distance(motor.transform.position,nearDoor()),Is.LessThan(.15f));
                        utility.Interact(city.Player.Interactor);
                        yield return Until(CityStreetUtilitySession.Step.Inspect);
                        yield return Press(Key.Escape,session);
                        yield return Until(CityStreetUtilitySession.Step.Idle);
                        Assert.That(Vector3.Distance(motor.transform.position,nearDoor()),Is.LessThan(.15f));
                    }
                    Vector3 nearDoor() => dock.StandPosition+Vector3.up*PlayerFactory.GroundedRootOffset;
                    IEnumerator Until(CityStreetUtilitySession.Step phase)
                    {
                        for(int i=0;i<195 && session.Phase!=phase;i++) { Listen(); yield return null; }
                        Assert.That(session.Phase,Is.EqualTo(phase),dock.Id+" at "+motor.transform.position);
                        yield return null;
                    }
                }
                foreach (var cue in new[] { RetroSfxId.BinLidLift, RetroSfxId.BinLidShut, RetroSfxId.BoothUnlatch,
                    RetroSfxId.BoothLatch, RetroSfxId.HandsetLift, RetroSfxId.HandsetSeat, RetroSfxId.PayphoneKey })
                {
                    Assert.That(Enumerable.Range(0,RetroSfxLibrary.GetDefinition(cue).VariantCount)
                        .Any(i=>heard.Contains(sound.GetClip(cue,i))), Is.True, cue.ToString());
                    var clip = sound.GetClip(cue);
                    var samples = new float[clip.samples];
                    Assert.That(clip.GetData(samples,0) && samples.Any(s=>Mathf.Abs(s)>.02f), Is.True, cue+" audible signal");
                }
                Assert.That(inputFailures,Is.Empty,string.Join("\n",inputFailures));
                // Every published path is tested with the same controller used by the action.
                var blocked = new System.Collections.Generic.List<string>();
                foreach (var utility in available)
                {
                    var dock = utility.Dock;
                    motor.Teleport(dock.Approach + Vector3.up*PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    yield return null;
                    bool reached=false;
                    for (int i=0; i<100 && !reached; i++)
                    {
                        reached = motor.MoveTowardsSupportedInteractionPose(dock.StandPosition + Vector3.up*PlayerFactory.GroundedRootOffset,
                            Quaternion.LookRotation(dock.Facing),Time.deltaTime);
                        if (motor.InteractionPoseMoveStalled) break;
                        yield return null;
                    }
                    if (!reached) blocked.Add($"{dock.Id} from {dock.Approach} to {dock.StandPosition}, reached {motor.transform.position}");
                }
                Assert.That(blocked,Is.Empty,string.Join("\n",blocked));
            }
            finally { input.TearDown(); GameCursor.SetApplicationFocused(focused); }
        }
    }

    // A test coroutine resumes before the hero's final IK pass. Observe the
    // same pose that goes to the camera, after animation and contact owners.
    [DefaultExecutionOrder(32000)]
    internal sealed class StreetUtilityContactProbe : MonoBehaviour
    {
        internal System.Action Sample;
        private void LateUpdate() => Sample?.Invoke();
    }
}
