using System.Collections;
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
        [UnityTest, Explicit("Review character finishes under the overcast sky and a working bar pendant.")]
        [PrebuildSetup(typeof(DefaultNpcDetailAssetsSetup))]
        public IEnumerator CharacterLightOnStreetAndInBar()
        {
            const string barId = "character-light";
            GameSessionState.BeginNewGame(barId);
            try
            {
                GameSessionState.TryStartGameTimeAt(720);
                GameSessionState.TrySetDebugWeather(WeatherKind.LightRain);
                yield return SceneManager.LoadSceneAsync(SceneIds.City);
                CityGameRoot city = null;
                yield return WaitFor(() => (city = Object.FindAnyObjectByType<CityGameRoot>()) && city.IsInitialized, "City");
                Vector3 street = city.Player.GameObject.transform.position;
                CompareCharacterFinishes(city.Player, street, Vector3.forward, "street");

                GameSessionState.EnterBar(barId, BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior);
                BarInteriorRoot bar = null;
                yield return WaitFor(() => (bar = Object.FindAnyObjectByType<BarInteriorRoot>()) && bar.IsInitialized, "Bar");
                bar.ArrivalPresentation?.Skip();
                var lamp = bar.Layout.LightAnchors.Single(anchor => anchor.Id == "counter-pendant-left");
                Vector3 floor = new Vector3(lamp.Position.x, .03f, lamp.Position.z + .35f);
                CompareCharacterFinishes(bar.Player, bar.Room.TransformPoint(floor), -bar.Room.forward, "bar");
            }
            finally { GameSessionState.BeginNewGame(); }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private static void CompareCharacterFinishes(PlayerRuntime player, Vector3 centre, Vector3 facing, string scene)
        {
            Camera camera = Camera.main;
            var saved = new DefaultNpcCameraState(camera);
            Transform root = player.GameObject.transform;
            Vector3 origin = root.position;
            Quaternion rotation = root.rotation;
            var host = new GameObject("Character light review");
            var hero = (Player3DCharacterPresentation)player.Visual;
            var npc = DefaultNpcFactory.CreateForCharacter(host.transform, DefaultNpcPopulation.PedestrianId(14));
            var heroSkins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var forceSkinning = heroSkins.Select(skin => skin.forceMatrixRecalculationPerRender).ToArray();
            try
            {
                foreach (var skin in heroSkins.Concat(npc.GetComponentsInChildren<SkinnedMeshRenderer>(true)))
                    skin.forceMatrixRecalculationPerRender = true;
                npc.GetComponent<NpcWardrobe>().ApplyOutfit("everyday");
                npc.Apply(VillageResidentAction.Idle, .3f);
                Quaternion npcFront = Quaternion.FromToRotation(DefaultNpcFacing(npc), facing) * npc.ModelRoot.rotation;
                var right = Vector3.Cross(Vector3.up, facing);
                npc.transform.position = centre + right * .33f;
                var face = hero.Registry.FaceAtlas.Renderer.sharedMaterial;
                var npcParts = npc.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.That(npcParts.Single(part => part.name == "GEO_FaceSurface").sharedMaterial, Is.SameAs(face));
                Assert.That(face.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), Is.False);
                Assert.That(face.GetFloat("_Smoothness"), Is.InRange(.38f, .48f));
                Assert.That(npcParts.Any(part => part.enabled && part.sharedMaterial.GetFloat("_ClearCoatMask") < 0), Is.True);
                Vector3 focus = centre + Vector3.up * 1.38f;
                Vector3 eye = focus + facing * 1.58f + Vector3.up * .13f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 37f;
                foreach (int yaw in new[] { -30, 0, 30 })
                {
                    var turn = Quaternion.AngleAxis(yaw, Vector3.up);
                    root.SetPositionAndRotation(centre - right * .33f, turn * Quaternion.LookRotation(facing));
                    hero.SetMotion(PlayerMotionSample.Stationary);
                    hero.ReapplyLatePresentationPose();
                    npc.ModelRoot.rotation = turn * npcFront;
                    CaptureCurrentCamera(camera, "CharacterLight", scene + "-" + yaw);
                }
            }
            finally
            {
                for (int i = 0; i < heroSkins.Length; i++) heroSkins[i].forceMatrixRecalculationPerRender = forceSkinning[i];
                root.SetPositionAndRotation(origin, rotation);
                saved.Restore(camera);
                NpcFootstepSources.Unregister(npc.transform);
                Object.DestroyImmediate(host);
            }
        }
    }
}
