using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_StreetUtilitiesWalkOpenCallAndLeave()
        {
            bool focused = GameCursor.ApplicationFocused;
            try
            {
                GameCursor.SetApplicationFocused(true);
                GameSessionState.TryStartGameTimeAt(12*60);
                yield return LoadProductionCity();
                var motor = city.Player.Motor;
                var available = Object.FindObjectsByType<CityStreetUtilityInteraction>(FindObjectsSortMode.None);
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
                        for (int frame=0; frame<18; frame++) yield return null;
                        if (capture) AreaCaptureFixture.CaptureCurrentCamera(city.Camera,"StreetUtilities","Phone-in-hand",960,540);
                        yield return Until(CityStreetUtilitySession.Step.Inspect);
                        session.Look(new Vector2(160f,0f));
                    }
                    Assert.That(session.Use(),Is.True);
                    yield return Until(CityStreetUtilitySession.Step.Idle);
                    Assert.That(utility.Model.OpenAmount,Is.Zero);
                    Assert.That(motor.InputEnabled && city.Player.Interactor.InputEnabled,Is.True);
                    IEnumerator Until(CityStreetUtilitySession.Step phase)
                    {
                        for(int i=0;i<195 && session.Phase!=phase;i++) yield return null;
                        Assert.That(session.Phase,Is.EqualTo(phase),dock.Id+" at "+motor.transform.position);
                        yield return null;
                    }
                }
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
            finally { GameCursor.SetApplicationFocused(focused); }
        }
    }
}
