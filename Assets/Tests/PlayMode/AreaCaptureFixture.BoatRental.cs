using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Rental hut printed lettering, four hulls, pier route and rain at night.")]
        public IEnumerator CityBoatRentalStation()
        {
            CityGameRoot city = null;
            CitySeacoastPlan coast = null;
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12*60), Is.True);
            float previousDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f/30f;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    coast = city.World.SeacoastPlan;
                    city.Weather.enabled = false;
                    city.Rain.SetIntensity(0f);
                    city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    city.DayNight.ApplyCurrentTime(true);
                    var board = coast.Parts.Single(p => p.Kind == CitySeacoastPartKind.HutSign);
                    Renderer sign = GameObject.Find("Boat Rental Printed Sign").GetComponent<Renderer>();
                    Assert.That(Vector3.Distance(sign.bounds.center, board.Center), Is.LessThan(.04f));
                    Assert.That(sign.bounds.size.x, Is.EqualTo(board.Size.x).Within(.05f));
                    Assert.That(sign.sharedMaterial.GetTexture("_BaseMap").name, Is.EqualTo("BoatRentalAlbedo"));
                    CheckRentalPrint();
                    Vector3 pier = At(CitySeacoastPlanner.PierDeckRootId).Center;
                    Vector3 head = At(CitySeacoastPlanner.PierDeckHeadId).Center;
                    var hulls = coast.Parts.Where(p => p.Kind == CitySeacoastPartKind.BoatRest)
                        .GroupBy(p => p.BoatOrdinal).OrderBy(p => p.Key).ToArray();
                    Assert.That(hulls.Select(g => g.First().Variant).Distinct().Count(), Is.EqualTo(4));
                    foreach (var hull in hulls)
                    {
                        Assert.That(hull.Count(), Is.EqualTo(2));
                        Renderer body = GameObject.Find("Boat Rental Hull " + hull.Key).GetComponent<Renderer>();
                        Vector3 normal = hull.First().Rotation * Vector3.up;
                        float[] heights = body.GetComponent<MeshFilter>().sharedMesh.vertices
                            .Select(v => Vector3.Dot(body.transform.TransformPoint(v)-body.transform.position, normal)).ToArray();
                        Assert.That(heights.Max()-heights.Min(), Is.InRange(.62f, .82f), body.name + " depth above rests");
                        foreach (var rest in hull)
                        {
                            Vector3 foot = rest.Center - rest.Rotation*Vector3.up*rest.Size.y*.5f;
                            float sand = CitySeacoastPlanner.SampleShoreWalkTop(city.Layout, coast.Frame, foot.x, foot.z);
                            Assert.That(foot.y-sand, Is.InRange(-.045f,.025f), rest.StableId);
                        }
                    }
                    Vector3 first = hulls[0].Select(p => p.Center).Aggregate((a,b) => (a+b)*.5f);
                    Vector3 wreck = hulls.First(g => g.First().Variant == CitySeacoastBoatVariant.HolledWreck)
                        .Select(p => p.Center).Aggregate((a,b) => (a+b)*.5f);
                    var poses = new List<Shot>();
                    Add("hut-gate", board.Center + new Vector3(1.65f,-1.05f,5.3f), board.Center-Vector3.up*.68f, 54);
                    Add("hut-beach", board.Center + new Vector3(-3.6f,-1.0f,4.9f), board.Center-Vector3.up*.65f, 54);
                    Add("hulls", first+new Vector3(-2.7f,2.05f,3.4f), first+new Vector3(3.1f,.24f,0f), 58);
                    Add("wreck", wreck+new Vector3(3.4f,1.35f,-2.6f), wreck+Vector3.up*.25f, 52);
                    Add("pier", pier+new Vector3(.68f,1.60f,-1.9f), head+Vector3.up*.28f, 58);
                    Add("slip", At("seacoast-slipway-1").Center+new Vector3(2.4f,1.8f,-4.6f),
                        At("seacoast-slipway-1").Center, 55);
                    Add("night-hut", board.Center+new Vector3(.8f,-1.0f,4.9f),board.Center-Vector3.up*.65f,54,true);
                    Add("night-pier", head+new Vector3(1f,1.55f,-6.2f),head+Vector3.up*.52f,56,true);
                    return poses.ToArray();

                    void Add(string name, Vector3 eye, Vector3 target, float fov, bool night=false)
                    {
                        Shot pose = Shot.At(name,eye,target,fov);
                        int frames=0;
                        poses.Add(Shot.At("rental-"+name,eye,target,fov,readyWhen: () =>
                        {
                            if (frames++ == 0)
                            {
                                if (night && GameSessionState.GameTimeOfDayMinutes < 21*60)
                                {
                                    GameSessionState.AdvanceGameTime((float)((21*60-GameSessionState.GameTimeOfDayMinutes)/GameTimeState.GameMinutesPerRealSecond));
                                    city.DayNight.ApplyCurrentTime(true);
                                }
                                city.Rain.SetIntensity(night ? 1f : 0f);
                                CityWaterResources.SetRainIntensity(night ? 1f : 0f);
                                CityWetSurfaceRegistry.SetImmediate(night ? 1f : 0f);
                                MoveCityLightingObserver(city,pose);
                            }
                            return frames >= (night ? 35 : 8);
                        }));
                    }
                });
                yield return WalkRentalReturn(city,coast);
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu,LoadSceneMode.Single);
            }
            finally
            {
                if (city)
                {
                    city.Player.Motor.SetInputEnabled(true);
                    city.Camera.GetComponent<PlayerCameraFollow>().enabled = true;
                    city.Weather.enabled = true;
                }
                Time.captureDeltaTime=previousDelta;
                GameSessionState.BeginNewGame();
            }
            CitySeacoastPartDescriptor At(string id) => coast.Parts.Single(p => p.StableId==id);
        }

        private static void CheckRentalPrint()
        {
            var asset = Resources.Load<GameObject>("City/BoatRental");
            var sheet = asset.GetComponentsInChildren<MeshFilter>().Single(p => p.name=="Sign");
            Transform basis = sheet.transform.parent;
            Vector3 forward = (basis.Find("AxisForward").position-basis.position).normalized;
            Vector3 up = (basis.Find("AxisUp").position-basis.position).normalized;
            Vector3 viewerRight = Vector3.Cross(up,-forward);
            var mesh = sheet.sharedMesh;
            Vector2[] uv = mesh.uv;
            Vector3[] verts = mesh.vertices;
            var corners = Enumerable.Range(0,verts.Length).Where(i => uv[i].y < .25f).ToArray();
            Assert.That(corners, Has.Length.EqualTo(4));
            var coordinates = corners.OrderBy(i=>uv[i].x).ToArray();
            Vector3 delta = sheet.transform.TransformVector(verts[coordinates[3]]-verts[coordinates[0]]);
            Assert.That(Vector3.Dot(delta,viewerRight), Is.GreaterThan(.9f), "Printed U increases to reader's right");
            coordinates = corners.OrderBy(i=>uv[i].y).ToArray();
            delta = sheet.transform.TransformVector(verts[coordinates[3]]-verts[coordinates[0]]);
            Assert.That(Vector3.Dot(delta,up), Is.GreaterThan(.85f), "Printed V increases upward");
            Assert.That(Vector3.Dot(sheet.transform.TransformDirection(mesh.normals[corners[0]]),forward), Is.GreaterThan(.98f));
        }

        private static IEnumerator WalkRentalReturn(CityGameRoot city, CitySeacoastPlan coast)
        {
            var root = coast.Parts.Single(p=>p.StableId==CitySeacoastPlanner.PierDeckRootId);
            var head = coast.Parts.Single(p=>p.StableId==CitySeacoastPlanner.PierDeckHeadId);
            Vector3 sign = coast.Parts.Single(p=>p.Kind==CitySeacoastPartKind.HutSign).Center;
            Vector3 row = coast.Parts.First(p=>p.Kind==CitySeacoastPartKind.BoatRest).Center;
            float street = coast.Frame.WaterlineZ-7.65f;
            Vector3 Sand(float x,float z) => new(x,CitySeacoastPlanner.SampleShoreWalkTop(city.Layout,coast.Frame,x,z),z);
            Vector3[] stops = { Sand(sign.x,street), Sand(row.x,street), Sand(root.Center.x+.24f,street),
                root.Center+new Vector3(.24f,root.Size.y*.5f,.18f),
                head.Center+new Vector3(.24f,head.Size.y*.5f,-1.9f) };
            var motor = city.Player.Motor;
            motor.Teleport(stops[0]+Vector3.up*PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            foreach (Vector3 floor in stops.Skip(1).Concat(stops.Reverse().Skip(1)))
            {
                Vector3 goal=floor+Vector3.up*PlayerFactory.GroundedRootOffset;
                bool reached=false;
                for (int frame=0; frame<280 && !reached; ++frame)
                {
                    reached=motor.MoveTowardsApproachWaypoint(goal,.09f,.09f);
                    if (motor.InteractionPoseMoveStalled) break;
                    yield return null;
                }
                motor.CancelInteractionPoseMove();
                Assert.That(reached, Is.True, $"Rental route at {motor.transform.position} toward {goal}");
            }
        }
    }
}
