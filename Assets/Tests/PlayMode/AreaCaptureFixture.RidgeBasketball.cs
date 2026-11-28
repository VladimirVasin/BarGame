using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(240000)]
        [Explicit("Review the imported wall hoop and both children in the live City.")]
        public IEnumerator RidgeBasketball_AlternatesAndOnlyScorerJumps()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12*60), Is.True);
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            var city = Object.FindAnyObjectByType<CityGameRoot>();
            Assert.That(city.IsInitialized, Is.True);
            Vector3 foot = new(154.5f,36f,-339.1f);
            city.Player.Motor.Teleport(foot);
            city.Player.Motor.SetInputEnabled(false);
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            follow.enabled = false;
            var court = city.World.Basketball;
            Assert.That(court, Is.Not.Null);
            Bounds board = CityGlassReturnAssetProvider.FindPart(court.transform,"BoardTimber").GetComponent<Renderer>().bounds;
            Bounds steel = CityGlassReturnAssetProvider.FindPart(court.transform,"HoopSteel").GetComponent<Renderer>().bounds;
            Bounds net = CityGlassReturnAssetProvider.FindPart(court.transform,"CordKnots").GetComponent<Renderer>().bounds;
            Assert.That(board.size.z, Is.LessThan(.11f), "The backboard stands against the wall.");
            Assert.That(board.size.y, Is.InRange(.70f,.76f));
            Assert.That(steel.size.y, Is.LessThan(.043f), "The imported steel ring lies horizontally.");
            Assert.That(steel.size.x, Is.InRange(.51f,.535f));
            Assert.That(steel.size.z, Is.InRange(.51f,.535f));
            Assert.That(Vector3.Distance(steel.center, court.Rim), Is.LessThan(.012f));
            Assert.That(board.center.y, Is.LessThan(steel.center.y-.1f), "The turned board extends below the rim.");
            Assert.That(board.min.z - steel.max.z, Is.GreaterThan(.30f), "Visible daylight between the rim and wall board.");
            Assert.That(net.max.y, Is.EqualTo(court.Rim.y).Within(.008f));
            Assert.That(net.min.y, Is.EqualTo(court.Rim.y-.324f).Within(.012f));
            // Eye-level inspection from the reported coordinates puts the lens inside the hero.
            foreach (var renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            Vector3 eye = foot + Vector3.up*1.54f;
            SetWestInfillCamera(city, eye, court.Rim + new Vector3(0,-.87f,-1.2f));
            // Put the opening and its wall brackets in profile, then resume the player's view.
            Vector3 bracketEye = court.Rim + new Vector3(1.75f,.94f,-2.2f);
            city.Camera.transform.SetPositionAndRotation(bracketEye,Quaternion.LookRotation(court.Rim-bracketEye));
            city.Camera.fieldOfView = 45f;
            yield return null;
            CaptureCurrentCamera(city.Camera,"RidgeBasketball","wall-brackets",1024,576);
            SetWestInfillCamera(city, eye, court.Rim + new Vector3(0,-.87f,-1.2f));
            var ambient = city.AmbientPedestrians;
            ambient.enabled = false; // Step the game once per sample, including the captured frames.
            bool hit = false, miss = false, jump = false;
            int last = -1, lastShot = 0;
            for (int i = 0; i < 6000 && !(hit && miss && jump && lastShot >= 2); i++)
            {
                ambient.Advance(1f/24f);
                if (court.Attempts > lastShot)
                {
                    Assert.That(court.Shooter, Is.Not.EqualTo(last));
                    last = court.Shooter;
                    lastShot = court.Attempts;
                }
                if (court.Current != RidgeBasketballCourt.Phase.Rebound) continue;
                var shooter = court.Player(last);
                var other = court.Player(1-last);
                float feet = Mathf.Min(shooter.Registry.LeftFoot.position.y,shooter.Registry.RightFoot.position.y)-shooter.transform.position.y;
                float waitingFeet = Mathf.Min(other.Registry.LeftFoot.position.y,other.Registry.RightFoot.position.y)-other.transform.position.y;
                Assert.That(waitingFeet, Is.LessThan(.15f), "Waiting child stays grounded.");
                if (!court.Made)
                {
                    Assert.That(feet, Is.LessThan(.15f), "A miss has no celebration.");
                    if (!miss)
                    {
                        yield return null; // Flush skinned renderers after advancing the rig.
                        CaptureCurrentCamera(city.Camera,"RidgeBasketball","miss",1024,576);
                    }
                    miss = true;
                }
                else
                {
                    if (Vector3.Distance(court.Ball.position,court.Rim) < .025f)
                    {
                        if (!hit)
                        {
                            yield return null;
                            CaptureCurrentCamera(city.Camera,"RidgeBasketball","rim",1024,576);
                        }
                        hit = true;
                    }
                    if (feet > .29f && !jump)
                    {
                        var hands = shooter.Registry.Animator.transform;
                        foreach (string grip in new[] { "SOCKET_Grip.L", "SOCKET_Grip.R" })
                            Assert.That(FindPart(hands,grip).position.y, Is.GreaterThan(shooter.Registry.Head.position.y));
                        yield return null;
                        var palm = FindPart(shooter.Registry.ModelRoot,"GEO_Hand.R").GetComponent<SkinnedMeshRenderer>();
                        var baked = new Mesh();
                        try
                        {
                            palm.BakeMesh(baked);
                            Assert.That(palm.transform.TransformPoint(baked.bounds.center).y,
                                Is.GreaterThan(shooter.Registry.Head.position.y), "Visible hand mesh rises above the head.");
                        }
                        finally { Object.DestroyImmediate(baked); }
                        CaptureCurrentCamera(city.Camera,"RidgeBasketball","jump",1024,576);
                        jump = true;
                    }
                }
            }
            string state = $"phase={court.Current} attempts={court.Attempts} court={court.transform.position:F2} " +
                $"first={court.Player(0)?.transform.position:F2} second={court.Player(1)?.transform.position:F2}";
            Assert.That(hit && miss && jump && lastShot >= 2, Is.True, state);
            for (int i = 0; i < 1400 && court.FreeSeat != 0; i++) court.Advance(1f/24f,false);
            Assert.That(court.Player(0), Is.Null, "First child rejoins the sidewalk at dusk.");
            Assert.That(court.Player(1), Is.Null, "Second child rejoins the sidewalk at dusk.");
        }
    }
}
