using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class NameplateCatalog { public NameplateEntry[] entries; }
        [Serializable]
        private sealed class NameplateEntry { public string key; public string value; }

        [UnityTest]
        [Explicit("Focused nameplate depth/lifecycle contract and real frames of the five affected roots.")]
        public IEnumerator NpcNameplates()
        {
            string[][] roles =
            {
                new[] { "foreman", "watchman", "fisherman", "ferryman", "chess_player", "checkers_player" },
                new[] { "bartender" }, new[] { "cashier" }, new[] { "cat" },
                new[] { "ferryman", "cafe_attendant" }
            };
            string[] scenes = { SceneIds.City, SceneIds.BarInterior, SceneIds.SupermarketInterior,
                SceneIds.StairwellInterior, SceneIds.MountainRoad };
            string[] allKeys = roles.SelectMany(value => value).Distinct()
                .Select(value => "npc.name." + value).ToArray();
            NameplateEntry[] russian = null;
            Font font = RetroUiTheme.InterfaceFont;
            Assert.That(font, Is.Not.Null);
            foreach (string language in new[] { "ru", "en" })
            {
                TextAsset asset = Resources.Load<TextAsset>("Localization/" + language);
                Assert.That(asset, Is.Not.Null);
                NameplateEntry[] entries = JsonUtility.FromJson<NameplateCatalog>(asset.text).entries
                    .Where(entry => entry.key.StartsWith("npc.name.", StringComparison.Ordinal)).ToArray();
                CollectionAssert.AreEquivalent(allKeys, entries.Select(entry => entry.key),
                    "Only the approved ten roles are named; Mother and village residents remain deferred.");
                font.RequestCharactersInTexture(string.Join("", entries.Select(entry => entry.value)), NpcNameplatePolicy.FontSize);
                foreach (NameplateEntry entry in entries)
                {
                    Assert.That(entry.value, Is.Not.Null.And.Not.Empty, language + ": " + entry.key);
                    float width = 0f;
                    foreach (char letter in entry.value)
                    {
                        Assert.That(letter == '\n' || letter == '\r', Is.False, "A role occupies one line.");
                        Assert.That(font.GetCharacterInfo(letter, out CharacterInfo glyph, NpcNameplatePolicy.FontSize),
                            Is.True, language + ": missing glyph " + letter);
                        width += glyph.advance;
                    }
                    Assert.That(width, Is.InRange(1f, RetroUiTheme.LogicalWidth / 3f), entry.key + " stays compact in " + language);
                }
                if (language == "ru") russian = entries;
            }

            using (var language = new NameplateLanguageScope(russian))
            {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
            NpcNameplateTarget[] previous = Array.Empty<NpcNameplateTarget>();
            for (int area = 0; area < scenes.Length; area++)
            {
                if (area == 1)
                {
                    // Existing distant cannery teardown restores its hidden
                    // lights too late, during destruction. Restore presentation
                    // while the City is alive; that unrelated defect must not
                    // prevent inspection of the interior nameplates.
                    foreach (CityCanneryController cannery in Object.FindObjectsByType<CityCanneryController>())
                    {
                        cannery.ForcePresentation = true;
                        cannery.RefreshPresentation();
                    }
                }
                if (area == 1) GameSessionState.EnterBar("nameplate-capture");
                if (area == 2) GameSessionState.EnterSupermarket();
                yield return SceneManager.LoadSceneAsync(scenes[area], LoadSceneMode.Single);
                PlayerRuntime player = default;
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (player.GameObject == null)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), scenes[area] + " initialization");
                    player = NameplatePlayer(scenes[area]);
                    yield return null;
                }
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                foreach (NpcNameplateTarget old in previous)
                    Assert.That(old == null, Is.True, "The previous scene must release its actor identities.");
                NpcNameplateTarget[] current = NpcNameplateTarget.ActiveTargets.ToArray();
                CollectionAssert.AreEquivalent(roles[area], current.Select(value => value.StableId), scenes[area]);
                Assert.That(current.Select(value => value.ActorRoot).Distinct().Count(), Is.EqualTo(current.Length),
                    "Multiple interaction components on one NPC still produce one identity.");
                foreach (NpcNameplateTarget actor in current)
                {
                    Assert.That(actor.Head, Is.Not.Null);
                    Assert.That(actor.LocalizationKey, Is.EqualTo("npc.name." + actor.StableId));
                }
                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                Assert.That(camera.GetComponent<NpcNameplateContext>(), Is.Not.Null,
                    "The actual gameplay camera must own nameplate presentation.");
                if (area == 0) ValidateNameplateDepthAndLifecycle(player);
                using (var frame = new NameplateFrame(camera, player))
                {
                    foreach (NpcNameplateTarget actor in current)
                        yield return CaptureNameplateActor(frame, player, actor, scenes[area]);
                }
                previous = current;
            }
            }
            Debug.Log("NPC NAMEPLATES: ten localized identities; five real roots; near/far, shared speech, opaque depth without colliders, panel depth and scene teardown.");
        }

        private static PlayerRuntime NameplatePlayer(string scene)
        {
            if (scene == SceneIds.City)
            { var root = Object.FindAnyObjectByType<CityGameRoot>(); return root != null && root.IsInitialized ? root.Player : default; }
            if (scene == SceneIds.BarInterior)
            { var root = Object.FindAnyObjectByType<BarInteriorRoot>(); return root != null && root.IsInitialized ? root.Player : default; }
            if (scene == SceneIds.SupermarketInterior)
            { var root = Object.FindAnyObjectByType<SupermarketInteriorRoot>(); return root != null && root.IsInitialized ? root.Player : default; }
            if (scene == SceneIds.StairwellInterior)
            { var root = Object.FindAnyObjectByType<StairwellInteriorRoot>(); return root != null && root.IsInitialized ? root.Player : default; }
            var road = Object.FindAnyObjectByType<MountainRoadRoot>();
            return road != null && road.IsInitialized ? road.Player : default;
        }

        private static IEnumerator CaptureNameplateActor(NameplateFrame frame, PlayerRuntime player,
            NpcNameplateTarget actor, string scene)
        {
            // Start on the NPC's customer-facing side. The shorter alternatives
            // keep a camera inside a narrow landing or behind an interior counter.
            Vector3[] offsets =
            {
                actor.transform.forward * 2.8f + actor.transform.right * .3f,
                actor.transform.forward * 1.5f,
                actor.transform.right * 1.7f,
                -actor.transform.right * 1.7f
            };
            bool shown = false;
            foreach (Vector3 offset in offsets)
            {
                player.Motor.Teleport(actor.transform.position + offset);
                Vector3 eye = actor.Head.position + offset;
                if (actor.StableId == "cat") eye += Vector3.up * .65f;
                frame.Camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation(actor.Head.position - Vector3.up * .15f - eye));
                frame.Camera.fieldOfView = 58f;
                // Distant presentation owners legitimately sleep their visual
                // roots. Let ordinary Update/LateUpdate see the nearby observer
                // before asking the renderer to photograph this actor.
                for (int settle = 0; settle < 2; settle++) yield return null;
                for (int settle = 0; !actor.IsPresent && settle < 6; settle++) yield return null;
                Assert.That(actor.IsPresent, Is.True, actor.StableId + " must wake its real visual on approach.");
                eye = actor.Head.position + offset;
                if (actor.StableId == "cat") eye += Vector3.up * .65f;
                frame.Camera.transform.SetPositionAndRotation(eye,
                    Quaternion.LookRotation(actor.Head.position - Vector3.up * .15f - eye));
                foreach (NpcSpeechBubbleView speech in Object.FindObjectsByType<NpcSpeechBubbleView>(FindObjectsSortMode.None))
                    speech.DismissAll();
                Color32[] without = frame.RenderWithout(actor);
                Color32[] with = frame.Render();
                if (!frame.Feature.DebugTryGetNameplate(actor, out Rect panel, out float opacity)) continue;
                if (NameplateChangedPixels(without, with, panel) < 30) continue;
                Assert.That(opacity, Is.EqualTo(1f).Within(.01f), actor.StableId + " is fully readable nearby.");
                frame.Save(scene, "nameplate-" + actor.StableId);
                shown = true;
                break;
            }
            Assert.That(shown, Is.True, scene + "/" + actor.StableId + " must draw actual nameplate pixels above the real rig.");
        }

        private static void ValidateNameplateDepthAndLifecycle(PlayerRuntime player)
        {
            const int layer = 30;
            int originalCount = NpcNameplateTarget.ActiveTargets.Count;
            var stage = new GameObject("Nameplate depth contract probes");
            stage.transform.position = new Vector3(3000f, 100f, 3000f);
            var cameraObject = new GameObject("Nameplate depth probe camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.13f, .16f, .19f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 20f;
            camera.fieldOfView = 58f;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            cameraObject.AddComponent<NpcNameplateContext>().Initialize(player.Interactor);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            material.SetColor("_BaseColor", new Color(.22f, .32f, .38f));
            GameObject actor = new GameObject("Nameplate actor probe");
            actor.layer = layer;
            actor.transform.SetParent(stage.transform, false);
            NameplateProbeBox(actor.transform, "body", new Vector3(0f, .75f, 0f), new Vector3(.55f, 1.5f, .4f), material, layer);
            Transform head = NameplateProbeBox(actor.transform, "head", new Vector3(0f, 1.7f, 0f), new Vector3(.24f, .24f, .24f), material, layer).transform;
            NpcNameplateTarget target = NpcNameplateTarget.Attach(actor, "npc.name.foreman", head, "nameplate-probe");
            GameObject blocker = NameplateProbeBox(stage.transform, "opaque blocker without collider",
                new Vector3(0f, 1.2f, -2f), new Vector3(3f, 4f, .04f), material, layer);
            blocker.SetActive(false);
            Assert.That(blocker.GetComponent<Collider>(), Is.Null);
            camera.transform.SetPositionAndRotation(stage.transform.position + new Vector3(0f, 1.7f, -4f), Quaternion.identity);
            NpcSpeechBubbleView speech = stage.AddComponent<NpcSpeechBubbleView>();
            speech.Initialize(camera, player.GameObject.transform);
            speech.UseManualClock = true;
            bool fog = RenderSettings.fog;
            bool vertexJitter = GraphicsEffectsSettings.VertexJitterEnabled;
            RenderSettings.fog = false;
            GraphicsEffectsSettings.VertexJitterEnabled = false;
            try
            {
                using (var frame = new NameplateFrame(camera, player))
                {
                    Assert.That(NpcNameplateTarget.Attach(actor, "npc.name.foreman", head, "nameplate-probe"), Is.SameAs(target));
                    Assert.That(NpcNameplateTarget.ActiveTargets.Count, Is.EqualTo(originalCount + 1));
                    foreach (float distance in new[] { .5f, 4f, 5f, 6f, 6.1f })
                    {
                        player.Motor.Teleport(actor.transform.position - Vector3.forward * distance);
                        Color32[] without = frame.RenderWithout(target);
                        Color32[] with = frame.Render();
                        bool candidate = frame.Feature.DebugTryGetNameplate(target, out Rect panel, out float alpha);
                        if (distance >= 6f)
                        {
                            Assert.That(candidate, Is.False, "Six metres is the outer visibility boundary.");
                            Assert.That(NameplateChangedPixels(without, with, new Rect(0, 0, Width, Height)), Is.Zero);
                        }
                        else
                        {
                            Assert.That(candidate, Is.True);
                            Assert.That(alpha, Is.EqualTo(distance == 5f ? .5f : 1f).Within(.01f));
                            Assert.That(NameplateChangedPixels(without, with, panel), Is.GreaterThan(30));
                        }
                    }
                    player.Motor.Teleport(actor.transform.position - Vector3.forward * 3f);
                    AssertVisible("nameplate-probe-open");
                    Assert.That(frame.Feature.DebugTryGetNameplate(target, out Rect unblockedPanel, out _), Is.True);

                    blocker.SetActive(true);
                    AssertHidden("An opaque renderer without a collider hides the complete nameplate.", "nameplate-probe-wall");
                    blocker.SetActive(false);
                    AssertVisible("nameplate-probe-open-again");

                    // The wall hides the whole body and head, but its top stays
                    // below the panel's sightline. A label cannot float over it.
                    blocker.transform.localPosition = new Vector3(0f, .85f, -2f);
                    blocker.transform.localScale = new Vector3(3f, 1.86f, .04f);
                    blocker.SetActive(true);
                    AssertHidden("The actor must be visible even when the label's own ray clears the wall.", "nameplate-probe-hidden-actor");

                    // Conversely, visible actor pixels cannot make text draw
                    // through a narrow shelf crossing only the label rectangle.
                    blocker.SetActive(false);
                    frame.Render();
                    Vector3 lower = camera.ScreenToWorldPoint(new Vector3(unblockedPanel.xMin, unblockedPanel.yMin, 2f));
                    Vector3 upper = camera.ScreenToWorldPoint(new Vector3(unblockedPanel.xMax, unblockedPanel.yMax, 2f));
                    blocker.transform.position = (lower + upper) * .5f;
                    blocker.transform.localScale = new Vector3((upper.x - lower.x) * 1.15f, (upper.y - lower.y) * 1.15f, .04f);
                    blocker.SetActive(true);
                    AssertHidden("Visible NPCs still cannot draw their label through an overhead obstacle.", "nameplate-probe-hidden-panel");
                    blocker.SetActive(false);

                    Assert.That(speech.DeclareSpeaker(target, head, NpcVoiceCatalog.WatchmanDesignId,
                        NpcEarshotProfile.Conversation), Is.True);
                    Assert.That(speech.ShowAt(target, LocalizationService.Get(CityPortConversationController.ForemanOfferKey), 0f, 3f), Is.True);
                    Assert.That(target.IsSpeaking, Is.True);
                    frame.Render();
                    Assert.That(frame.Feature.DebugTryGetNameplate(target, out _, out _), Is.False, "The shared speech owner suppresses its own nameplate.");
                    speech.DismissAll();
                    Assert.That(target.IsSpeaking, Is.False);
                    AssertVisible("nameplate-probe-after-speech");

                    actor.SetActive(false);
                    frame.Render();
                    Assert.That(NpcNameplateTarget.ActiveTargets.Count, Is.EqualTo(originalCount));
                    Assert.That(frame.Feature.DebugTryGetNameplate(target, out _, out _), Is.False);
                    actor.SetActive(true);
                    AssertVisible("nameplate-probe-reenabled");
                    Object.DestroyImmediate(target);
                    Assert.That(NpcNameplateTarget.ActiveTargets.Count, Is.EqualTo(originalCount));
                    target = NpcNameplateTarget.Attach(actor, "npc.name.foreman", head, "nameplate-probe");
                    AssertVisible("nameplate-probe-recreated");

                    void AssertVisible(string name)
                    {
                        Color32[] without = frame.RenderWithout(target), with = frame.Render();
                        frame.Save("Nameplates", name);
                        Assert.That(frame.Feature.DebugTryGetNameplate(target, out Rect panel, out _), Is.True, name);
                        Assert.That(NameplateChangedPixels(without, with, panel), Is.GreaterThan(30), name);
                    }
                    void AssertHidden(string reason, string name)
                    {
                        Color32[] without = frame.RenderWithout(target);
                        frame.Save("Nameplates", name + "-without-label");
                        Color32[] with = frame.Render();
                        frame.Save("Nameplates", name);
                        Assert.That(frame.Feature.DebugTryGetNameplate(target, out _, out _), Is.True,
                            "GPU depth is being exercised; distance and screen culling still admit the actor.");
                        Assert.That(NameplateChangedPixels(without, with, unblockedPanel), Is.Zero, reason);
                    }
                }
            }
            finally
            {
                RenderSettings.fog = fog;
                GraphicsEffectsSettings.VertexJitterEnabled = vertexJitter;
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(material);
            }
            Assert.That(NpcNameplateTarget.ActiveTargets.Count, Is.EqualTo(originalCount));
        }

        private static GameObject NameplateProbeBox(Transform parent, string name, Vector3 position,
            Vector3 size, Material material, int layer)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.layer = layer;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        private static int NameplateChangedPixels(Color32[] before, Color32[] after, Rect region)
        {
            int count = 0;
            int left = Mathf.Clamp(Mathf.FloorToInt(region.xMin), 0, Width);
            int right = Mathf.Clamp(Mathf.CeilToInt(region.xMax), 0, Width);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(region.yMin), 0, Height);
            int top = Mathf.Clamp(Mathf.CeilToInt(region.yMax), 0, Height);
            for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                {
                    Color32 a = before[y * Width + x], b = after[y * Width + x];
                    if (Mathf.Abs(a.r - b.r) > 2 || Mathf.Abs(a.g - b.g) > 2 || Mathf.Abs(a.b - b.b) > 2) count++;
                }
            return count;
        }

        private sealed class NameplateFrame : IDisposable
        {
            public Camera Camera { get; }
            public Ps1CompositeRendererFeature Feature { get; }
            private readonly RenderTexture target;
            private readonly Texture2D pixels;
            private readonly RenderTexture previousTarget;
            private readonly Pose previousCamera;
            private readonly float previousField;
            private readonly PlayerRuntime player;
            private readonly Vector3 previousHero;
            private readonly bool previousMotorEnabled;
            private readonly bool previousInput;
            private readonly PlayerCameraFollow follow;
            private readonly bool previousFollow;
            private readonly List<Renderer> hidden = new List<Renderer>();

            public NameplateFrame(Camera camera, PlayerRuntime player)
            {
                Camera = camera;
                this.player = player;
                previousTarget = camera.targetTexture;
                previousCamera = new Pose(camera.transform.position, camera.transform.rotation);
                previousField = camera.fieldOfView;
                previousHero = player.GameObject.transform.position;
                previousMotorEnabled = player.Motor.enabled;
                previousInput = player.Interactor.InputEnabled;
                follow = camera.GetComponent<PlayerCameraFollow>();
                previousFollow = follow != null && follow.enabled;
                if (follow != null) follow.enabled = false;
                player.Motor.enabled = false;
                player.Interactor.SetInputEnabled(true);
                foreach (Renderer renderer in player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                target.Create();
                pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                camera.targetTexture = target;
                Feature = Resources.FindObjectsOfTypeAll<Ps1CompositeRendererFeature>().FirstOrDefault(value => value.isActive);
                Assert.That(Feature, Is.Not.Null);
            }

            public Color32[] Render()
            {
                Camera.Render();
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    pixels.Apply();
                    return pixels.GetPixels32();
                }
                finally { RenderTexture.active = previous; }
            }

            public Color32[] RenderWithout(NpcNameplateTarget actor)
            {
                actor.enabled = false;
                try { return Render(); }
                finally { actor.enabled = true; }
            }

            public void Save(string scene, string name)
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", scene);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }

            public void Dispose()
            {
                Camera.targetTexture = previousTarget;
                Camera.transform.SetPositionAndRotation(previousCamera.position, previousCamera.rotation);
                Camera.fieldOfView = previousField;
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
                player.Motor.Teleport(previousHero);
                player.Motor.enabled = previousMotorEnabled;
                player.Interactor.SetInputEnabled(previousInput);
                if (follow != null) follow.enabled = previousFollow;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
            }
        }

        private sealed class NameplateLanguageScope : IDisposable
        {
            private readonly Dictionary<string, string> catalog;
            private readonly Dictionary<string, string> previous = new Dictionary<string, string>();

            public NameplateLanguageScope(NameplateEntry[] entries)
            {
                // Force resource loading before borrowing only these ten keys.
                // Every screenshot is then readable in the player's language,
                // independently of the build machine's operating-system locale.
                LocalizationService.Get(entries[0].key);
                catalog = (Dictionary<string, string>)typeof(LocalizationService).GetField("Values",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                foreach (NameplateEntry entry in entries)
                {
                    previous[entry.key] = catalog[entry.key];
                    catalog[entry.key] = entry.value;
                }
            }

            public void Dispose()
            {
                foreach (var entry in previous) catalog[entry.Key] = entry.Value;
            }
        }
    }
}
