using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const string MonsterTextureCaptureFolder = "monster-horror-textures";

        [UnityTest]
        [Explicit("Source-lit material review of all current monsters and the three passive/active bottle bodies.")]
        [PrebuildSetup(typeof(CombatLaboratoryAssetsSetup))]
        public IEnumerator MonsterHorrorTextures() => CaptureLaboratoryArtVisuals(true);

        private static void CaptureMonsterSurfaceViews(Camera camera, CombatActor actor,
            string name, Vector3 head, Vector3 outward)
        {
            AssertMonsterSurfaceBindings(actor.gameObject);
            Vector3 focus = head - Vector3.up * .60f;
            if (actor.IsOrdinaryZombie)
                focus = actor.transform.TransformPoint(StairwellVisibleBodyBounds(actor.gameObject, actor.transform).center);
            Vector3 front = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            if (front.sqrMagnitude < .5f) front = actor.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, front);
            CaptureMonsterTextureView(camera, name + "-body", focus + front * 3.25f + right * .85f + Vector3.up * .45f,
                focus, 48f);
            CaptureMonsterTextureView(camera, name + "-detail", head + outward * .83f + Vector3.up * .07f,
                head, 42f);
        }

        private static IEnumerator CaptureOrdinaryMonsterTextureVariants(CombatTestRoot root, Camera camera,
            WarehouseRefrigeratorPresentationProbe probe)
        {
            root.Opponent.gameObject.SetActive(false);
            var fixture = new GameObject("Monster texture variant review");
            fixture.transform.SetParent(root.transform, false);
            fixture.transform.position = root.Opponent.transform.position;
            try
            {
                for (int variant = 0; variant < 3; variant++)
                {
                    GameObject passive = CombatZombieAssetProvider.CreateOrdinaryPassive(fixture.transform, variant, false);
                    yield return AtWarehouseFridgePresentation(probe, () =>
                    {
                        AssertMonsterSurfaceBindings(passive);
                        Bounds bounds = StairwellVisibleBodyBounds(passive, fixture.transform);
                        Vector3 focus = fixture.transform.TransformPoint(bounds.center);
                        CaptureMonsterTextureView(camera, "bottle-" + variant + "-passive",
                            focus + new Vector3(2.5f, 1.2f, 2.8f), focus, 42f);
                    });
                    Object.DestroyImmediate(passive);

                    var actorRoot = new GameObject("Active bottle material " + variant);
                    actorRoot.transform.SetParent(fixture.transform, false);
                    var controller = actorRoot.AddComponent<CharacterController>();
                    controller.height = 1.75f; controller.center = Vector3.up * .875f; controller.radius = .32f;
                    VillageResidentPresentation visual = DefaultNpcFactory.CreateForCharacter(actorRoot.transform,
                        DefaultNpcPopulation.StairwellEnemyId(variant == 2 ? 11 : variant));
                    CombatZombieAssetProvider.ApplyOrdinaryAppearance(visual.gameObject, variant);
                    var actor = actorRoot.AddComponent<CombatActor>();
                    actor.InitializeOpponent(visual, controller, kind: CombatEnemyKind.Zombie);
                    actor.SetOrdinaryZombieDormant(variant);
                    Assert.That(actor.TryAwakenZombie(false), Is.True);
                    actor.Step(CombatZombieAssetProvider.OrdinaryRiseSeconds);
                    Assert.That(actor.ZombieAwake, Is.True);
                    yield return AtWarehouseFridgePresentation(probe, () =>
                    {
                        AssertMonsterSurfaceBindings(visual.gameObject);
                        Bounds bounds = StairwellVisibleBodyBounds(visual.gameObject, fixture.transform);
                        Vector3 focus = fixture.transform.TransformPoint(bounds.center);
                        CaptureMonsterTextureView(camera, "bottle-" + variant + "-active",
                            focus + new Vector3(1.7f, .55f, 2.35f), focus, 44f);
                    });
                    NpcFootstepSources.Unregister(visual.transform);
                    Object.DestroyImmediate(actorRoot);
                }
            }
            finally { Object.DestroyImmediate(fixture); }
        }

        private static void AssertMonsterSurfaceBindings(GameObject body)
        {
            bool textured = false;
            var properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
                renderer.GetPropertyBlock(properties);
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    Assert.That(material.shader != null && material.shader.isSupported, Is.True,
                        "A monster surface must retain its supported gameplay shader: " + renderer.name);
                    Texture texture = properties.GetTexture("_BaseMap") ?? properties.GetTexture("_MainTex");
                    if (texture == null && material.HasProperty("_BaseMap")) texture = material.GetTexture("_BaseMap");
                    if (texture == null && material.HasProperty("_MainTex")) texture = material.GetTexture("_MainTex");
                    if (!(texture is Texture2D atlas) || atlas.width < 256) continue;
                    textured = true;
                    Assert.That(atlas.mipmapCount, Is.GreaterThan(1), "Monster atlases need their gameplay mip chain: " + atlas.name);
                }
                properties.Clear();
            }
            Assert.That(textured, Is.True, "The actual visible monster must bind its authored surface atlas.");
        }

        private static void CaptureMonsterTextureView(Camera camera, string name, Vector3 eye, Vector3 focus, float fov)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, Vector3.up));
            camera.fieldOfView = fov;
            CaptureCurrentCamera(camera, MonsterTextureCaptureFolder, name);
        }
    }
}
