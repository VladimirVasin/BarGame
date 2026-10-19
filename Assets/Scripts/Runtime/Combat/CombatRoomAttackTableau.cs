using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>A prepared visual room. It never contains a second gameplay participant.</summary>
    internal sealed class CombatRoomAttackTableau : IDisposable
    {
        private readonly GameObject root;
        private readonly Player3DAssetRegistry hero;
        private readonly AnimationClip seated;
        private readonly Transform pelvis, forward, camera, lookAt, entryCamera;
        // This channel belongs only to the prepared image. Warehouse lights
        // must not fill in its projected dark shapes; the real hero is untouched.
        private const uint ShadowLayer = 1u << 5;
        private readonly Light light, shadowLight;
        private readonly Texture2D[] shadowFrames;
        private readonly AudioSource memorySound;
        private bool memoryStarted, memoryPaused;
        private readonly Renderer[] shadowReceivers;
        private readonly uint[] receiverLayers;
        private readonly Vector3 sourceOrigin, sourceRight, sourceUp, sourceForward;
        private readonly Dictionary<CombatActor, SourceFrame> sourceFrames = new Dictionary<CombatActor, SourceFrame>();
        private readonly FootContact[] footContacts;
        private float lightIntensity;
        internal bool Visible => root != null && root.activeSelf;
        internal Player3DAssetRegistry Hero => hero;
        internal Pose CameraPose => Aim(camera.position);
        internal Pose EntryPose => Aim(entryCamera.position);
        internal Vector3 Focus => lookAt.position;
        internal Vector3 FaceFocus => hero.Anchors.Head.position + root.transform.up * (.08f * OpticalScale);
        internal Pose FaceCameraPose
        {
            get
            {
                Vector3 focus = FaceFocus;
                Vector3 position = focus + (hero.transform.forward * .58f + hero.transform.right * .13f + root.transform.up * .015f) * OpticalScale;
                return new Pose(position, Quaternion.LookRotation(focus - position, root.transform.up));
            }
        }
        internal float OpticalScale { get; private set; } = 1f;
        internal Vector3 Inward => root.transform.forward;
        internal Light ShadowLight => shadowLight;
        internal AudioSource MemorySound => memorySound;

        internal CombatRoomAttackTableau(Transform parent, Player3DAssetRegistry source)
        {
            root = new GameObject("Room Inside attack tableau");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.down * 200f;
            try
            {
                GameObject room = UnityEngine.Object.Instantiate(CombatRoomAttackAssetProvider.LoadTableau(), root.transform, false);
                pelvis = CombatAssetProvider.FindAnchor(room, "ANCHOR_HeroPelvis");
                forward = CombatAssetProvider.FindAnchor(room, "ANCHOR_HeroForward");
                camera = CombatAssetProvider.FindAnchor(room, "ANCHOR_Camera");
                lookAt = CombatAssetProvider.FindAnchor(room, "ANCHOR_LookAt");
                entryCamera = CombatAssetProvider.FindAnchor(room, "ANCHOR_EntryCamera");
                sourceOrigin = LocalAnchor("SourceOrigin"); sourceRight = LocalAnchor("SourceRight");
                sourceUp = LocalAnchor("SourceUp"); sourceForward = LocalAnchor("SourceForward");
                CombatRoomAttackAssetProvider.ApplyTableauMaterials(room);
                // The NPC's actual skin supplies every corridor wall, outside
                // and inside. Only this prepared hero, chair and lamp are added.
                foreach (MeshRenderer surface in room.GetComponentsInChildren<MeshRenderer>(true))
                    if (surface.name == "GEO_RoomAttackOriginalCorridor") surface.enabled = false;
                hero = Player3DResources.Instantiate(root.transform);
                hero.gameObject.name = "Room Inside seated hero image";
                if (!hero.TryGetAnimation(CityBenchSitInteraction.LoopClipName, out Player3DAnimationBinding binding))
                    throw new InvalidOperationException("Room Inside needs the production seated hero clip.");
                seated = binding.Clip;
                DisableGameplay(hero);
                CopyAppearance(source, hero);
                shadowReceivers = hero.GetComponentsInChildren<Renderer>(true);
                receiverLayers = new uint[shadowReceivers.Length];
                for (int index = 0; index < shadowReceivers.Length; index++)
                    receiverLayers[index] = shadowReceivers[index].renderingLayerMask;
                Transform lightAnchor = CombatAssetProvider.FindAnchor(room, "ANCHOR_Light");
                var lamp = new GameObject("Room Inside ceiling light");
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.position = lightAnchor.position;
                light = lamp.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(1f, .78f, .56f);
                light.intensity = 12f; light.range = 8f; light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                var projector = new GameObject("Room Inside memory light");
                projector.transform.SetParent(root.transform, false);
                projector.transform.position = lightAnchor.position;
                shadowLight = projector.AddComponent<Light>();
                shadowLight.type = LightType.Spot; shadowLight.color = light.color;
                shadowLight.intensity = 0f; shadowLight.range = 8f;
                shadowLight.spotAngle = 44f; shadowLight.innerSpotAngle = 42f;
                shadowLight.shadows = LightShadows.None; shadowLight.bounceIntensity = 0f;
                shadowLight.renderMode = LightRenderMode.ForcePixel;
                shadowFrames = new Texture2D[CombatRoomAttackAssetProvider.ShadowFrameCount];
                for (int frame = 0; frame < shadowFrames.Length; frame++)
                    shadowFrames[frame] = CombatRoomAttackAssetProvider.LoadShadowCookie(frame);
                shadowLight.cookie = shadowFrames[0];
                shadowLight.GetUniversalAdditionalLightData().renderingLayers = ShadowLayer;
                var sound = new GameObject("Room Inside cloth and wood");
                sound.transform.SetParent(root.transform, false);
                sound.transform.position = pelvis.position;
                memorySound = sound.AddComponent<AudioSource>();
                memorySound.playOnAwake = memorySound.loop = false;
                CityWorkAudio.Configure(memorySound, true, 2400f, 8f, false);
                memorySound.clip = CombatRoomAttackAssetProvider.LoadMemorySound();
                memorySound.volume = .7f; memorySound.priority = 96;
                memorySound.ignoreListenerPause = false;
                Sample(0f);
                SampleShadow(0f);
                footContacts = new[] { new FootContact(hero, true), new FootContact(hero, false) };
                root.SetActive(false);

                Vector3 LocalAnchor(string name) => root.transform.InverseTransformPoint(
                    CombatAssetProvider.FindAnchor(room, "ANCHOR_" + name).position);
            }
            catch { UnityEngine.Object.Destroy(root); throw; }
        }

        private Pose Aim(Vector3 position) => new Pose(position,
            Quaternion.LookRotation(lookAt.position - position, root.transform.up));

        internal void PrepareActor(CombatActor actor)
        {
            if (sourceFrames.ContainsKey(actor)) return;
            Vector3 offset = root.transform.InverseTransformPoint(entryCamera.position) - sourceOrigin;
            float unit = Vector3.Distance(sourceRight, sourceOrigin);
            Vector3 reference = new Vector3(Vector3.Dot(offset, (sourceRight - sourceOrigin).normalized),
                Vector3.Dot(offset, (sourceUp - sourceOrigin).normalized),
                Vector3.Dot(offset, (sourceForward - sourceOrigin).normalized)) / unit;
            sourceFrames.Add(actor, new SourceFrame(actor, reference));
        }

        internal void BindTo(CombatActor actor)
        {
            if (footContacts != null)
                foreach (FootContact contact in footContacts) contact.Reset();
            SourceFrame source = sourceFrames[actor];
            Matrix4x4 frame = source.Capture();
            Vector3 worldRight = frame.MultiplyVector(Vector3.right);
            Quaternion rotation = Quaternion.LookRotation(frame.MultiplyVector(Vector3.forward), frame.MultiplyVector(Vector3.up));
            OpticalScale = worldRight.magnitude / Vector3.Distance(sourceRight, sourceOrigin);
            Transform stage = root.transform;
            stage.rotation = rotation;
            Vector3 parentScale = stage.parent != null ? stage.parent.lossyScale : Vector3.one;
            stage.localScale = new Vector3(OpticalScale / parentScale.x, OpticalScale / parentScale.y, OpticalScale / parentScale.z);
            // Authored rulers include the FBX unit factor and the corridor's
            // fourteen-fold enlargement; no native mesh-unit assumption remains.
            Quaternion authoredAxes = Quaternion.LookRotation(sourceForward - sourceOrigin, sourceUp - sourceOrigin);
            stage.rotation = rotation * Quaternion.Inverse(authoredAxes);
            stage.position = frame.MultiplyPoint3x4(Vector3.zero) - stage.TransformVector(sourceOrigin);
            light.range = 8f * OpticalScale;
            shadowLight.range = 8f * OpticalScale;
            memorySound.minDistance = .8f * OpticalScale;
            memorySound.maxDistance = 8f * OpticalScale;
            lightIntensity = 12f * OpticalScale * OpticalScale;
            SetLightAmount(0f);
            SampleShadow(0f);
            Sample(0f);
            source.CaptureFloor(stage);
            float pelvisHeight = stage.InverseTransformPoint(hero.Anchors.Pelvis.position).y;
            foreach (FootContact contact in footContacts) contact.Prepare(stage, source, pelvisHeight, hero.transform.forward);
            Sample(0f);
        }

        internal void SetLightAmount(float amount)
        {
            light.intensity = lightIntensity * Mathf.Clamp01(amount);
            shadowLight.intensity = light.intensity;
        }

        internal void SetVisible(bool visible)
        {
            if (root == null) return;
            for (int index = 0; index < shadowReceivers.Length; index++)
                if (shadowReceivers[index] != null)
                    shadowReceivers[index].renderingLayerMask = visible ? ShadowLayer : receiverLayers[index];
            if (!visible)
            {
                SetLightAmount(0f);
                memorySound.Stop(); memoryStarted = memoryPaused = false;
            }
            root.SetActive(visible);
        }

        /// <summary>A continuous abstract light interruption, sampled only by the owned pause-aware shot.</summary>
        internal void SampleShadow(float seconds)
        {
            int frame = Mathf.Min(shadowFrames.Length - 1, (int)(Mathf.Repeat(seconds,
                CombatRoomAttackAssetProvider.ShadowCycleSeconds) / CombatRoomAttackAssetProvider.ShadowCycleSeconds * shadowFrames.Length));
            if (shadowLight.cookie != shadowFrames[frame]) shadowLight.cookie = shadowFrames[frame];
            float sway = Mathf.Sin(seconds * (Mathf.PI * 1.4f));
            float drift = Mathf.Sin(seconds * 1.1f);
            Vector3 aim = root.transform.InverseTransformPoint(pelvis.position) +
                new Vector3(.025f * sway, .49f + .012f * drift, 0f);
            Vector3 toward = aim - shadowLight.transform.localPosition;
            shadowLight.transform.localRotation = Quaternion.LookRotation(toward, Vector3.up) *
                Quaternion.AngleAxis(-5f + sway, Vector3.forward);
        }

        internal void SampleSound(float seconds)
        {
            if (!Visible || seconds >= memorySound.clip.length) return;
            if (!memoryStarted) { memorySound.Play(); memoryStarted = true; }
            // Normal frames stream the prepared phrase. A hitch/manual tick
            // seeks only after real drift; there is no per-frame DSP restart.
            if (Mathf.Abs(memorySound.time - seconds) > .12f)
                memorySound.time = Mathf.Clamp(seconds, 0f, memorySound.clip.length - .02f);
        }

        internal void SetSoundPaused(bool paused)
        {
            if (!memoryStarted || memoryPaused == paused) return;
            memoryPaused = paused;
            if (paused) memorySound.Pause(); else memorySound.UnPause();
        }

        internal void Sample(float seconds)
        {
            hero.transform.localPosition = Vector3.zero;
            Vector3 up = root.transform.up;
            Vector3 facing = Vector3.ProjectOnPlane(forward.position - pelvis.position, up);
            hero.transform.rotation = Quaternion.LookRotation(facing, up);
            seated.SampleAnimation(hero.Animator.gameObject, Mathf.Repeat(seconds, seated.length));
            hero.transform.position += pelvis.position - hero.Anchors.Pelvis.position;
            if (footContacts != null)
                foreach (FootContact contact in footContacts) contact.Apply(root.transform);
        }

        /// <summary>Only limb pose adapts to the retained room's floor and raised thresholds.</summary>
        private sealed class FootContact
        {
            private readonly Transform thigh, shin, foot;
            private readonly Vector3[] bootPoints;
            private Vector3 target, hint;
            private Quaternion rotation;
            private bool prepared;

            internal FootContact(Player3DAssetRegistry registry, bool left)
            {
                foot = left ? registry.Anchors.LeftFoot : registry.Anchors.RightFoot;
                shin = foot != null ? foot.parent : null;
                thigh = shin != null ? shin.parent : null;
                if (thigh == null || shin == null)
                    throw new InvalidOperationException("Room seated hero needs its production leg chain.");
                var points = new List<Vector3>();
                var measured = new HashSet<Renderer>();
                var scratch = new Mesh { name = "Room seated boot contact measurement", hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    string suffix = left ? ".L" : ".R";
                    foreach (Player3DMeshBinding binding in registry.MeshBindings)
                    {
                        Renderer renderer = binding.Renderer;
                        string name = binding.MeshName;
                        if (renderer == null || !renderer.enabled || !name.EndsWith(suffix, StringComparison.Ordinal) ||
                            name.IndexOf("Boot", StringComparison.Ordinal) < 0 && name.IndexOf("Foot", StringComparison.Ordinal) < 0 ||
                            !measured.Add(renderer)) continue;
                        Vector3[] vertices;
                        if (renderer is SkinnedMeshRenderer skin)
                        {
                            skin.BakeMesh(scratch, true);
                            vertices = scratch.vertices;
                        }
                        else
                        {
                            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                            if (mesh == null) continue;
                            vertices = mesh.vertices;
                        }
                        Matrix4x4 toFoot = foot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        foreach (Vector3 point in vertices) points.Add(toFoot.MultiplyPoint3x4(point));
                    }
                }
                finally { UnityEngine.Object.Destroy(scratch); }
                if (points.Count == 0) throw new InvalidOperationException("Room seated hero needs visible production boot geometry.");
                bootPoints = points.ToArray();
            }

            internal void Reset() => prepared = false;

            internal void Prepare(Transform stage, SourceFrame source, float pelvisHeight, Vector3 forward)
            {
                Matrix4x4 toStage = stage.worldToLocalMatrix * foot.localToWorldMatrix;
                float lift = 0f;
                foreach (Vector3 vertex in bootPoints)
                {
                    Vector3 point = toStage.MultiplyPoint3x4(vertex);
                    if (source.TryGetFloorHeight(point, pelvisHeight, out float floor))
                        lift = Mathf.Max(lift, floor + .004f - point.y);
                }
                target = stage.InverseTransformPoint(foot.position) + Vector3.up * lift;
                rotation = Quaternion.Inverse(stage.rotation) * foot.rotation;
                hint = stage.InverseTransformPoint(shin.position + forward * (.15f * stage.lossyScale.x));
                prepared = true;
            }

            internal void Apply(Transform stage)
            {
                if (!prepared) return;
                LimbTwoBoneIk.Solve(thigh, shin, foot, stage.TransformPoint(target), stage.rotation * rotation,
                    stage.TransformPoint(hint), 1f, .999f, true);
            }
        }

        private static void DisableGameplay(Player3DAssetRegistry registry)
        {
            foreach (MonoBehaviour behaviour in registry.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != registry) behaviour.enabled = false;
            foreach (Animator animator in registry.GetComponentsInChildren<Animator>(true))
            { animator.applyRootMotion = false; animator.runtimeAnimatorController = null; animator.enabled = false; }
            foreach (Collider collider in registry.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in registry.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            foreach (Light light in registry.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (AudioSource audio in registry.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        }

        private static void CopyAppearance(Player3DAssetRegistry source, Player3DAssetRegistry target)
        {
            var renderers = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
                renderers.TryAdd(Path(renderer.transform, source.transform), renderer);
            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                if (renderers.TryGetValue(Path(renderer.transform, target.transform), out Renderer original))
                {
                    renderer.enabled = original.enabled;
                    renderer.sharedMaterials = original.sharedMaterials;
                    original.GetPropertyBlock(block); renderer.SetPropertyBlock(block);
                }
        }

        private static string Path(Transform current, Transform root) => current == root ? string.Empty :
            current.parent == root ? current.name : Path(current.parent, root) + "/" + current.name;

        public void Dispose() { if (root != null) { SetVisible(false); UnityEngine.Object.Destroy(root); } }

        /// <summary>Prepared skin-frame sample at the mouth, not the actor's root.</summary>
        private sealed class SourceFrame
        {
            private readonly Transform[] bones;
            private readonly Matrix4x4[] bind;
            private readonly float[] weights;
            private readonly Matrix4x4 sourceToMesh;
            private readonly Matrix4x4[] posedBones;
            private readonly Vector3[] vertices, floorVertices;
            private readonly BoneWeight[] vertexWeights;
            private readonly int[] plasterTriangles;

            internal SourceFrame(CombatActor actor, Vector3 reference)
            {
                SkinnedMeshRenderer live = null;
                foreach (SkinnedMeshRenderer skin in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.name == "GEO_Torso") { live = skin; break; }
                if (live == null) throw new InvalidOperationException("Room camera needs its retained corridor skin.");
                SkinnedMeshRenderer authored = CombatRoomAssetProvider.LoadBodyRenderer("GEO_Torso");
                Mesh mesh = authored.sharedMesh;
                bones = live.bones; bind = mesh.bindposes; weights = new float[bones.Length];
                posedBones = new Matrix4x4[bones.Length];
                sourceToMesh = authored.transform.worldToLocalMatrix;
                Vector3 point = sourceToMesh.MultiplyPoint3x4(reference);
                vertices = mesh.vertices; vertexWeights = mesh.boneWeights;
                floorVertices = new Vector3[vertices.Length];
                int plasterSlot = -1;
                foreach (CombatAuthoredBodyBank.BodyMesh surface in CombatRoomAssetProvider.LoadBodyManifest().meshes)
                    if (surface.name == "GEO_Torso") plasterSlot = Array.IndexOf(surface.surfaces, "plaster");
                if (plasterSlot < 0 || plasterSlot >= mesh.subMeshCount)
                    throw new InvalidOperationException("Room seated feet need the authored plaster surface.");
                plasterTriangles = mesh.GetTriangles(plasterSlot);
                int[] triangles = mesh.triangles;
                float best = float.PositiveInfinity; int a = 0, b = 0, c = 0; Vector3 barycentric = Vector3.right;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int x = triangles[i], y = triangles[i + 1], z = triangles[i + 2];
                    Vector3 bary = CombatDamageMarks.ClosestTriangle(vertices[x], vertices[y], vertices[z], point);
                    float distance = (vertices[x] * bary.x + vertices[y] * bary.y + vertices[z] * bary.z - point).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance; a = x; b = y; c = z; barycentric = bary;
                }
                Add(vertexWeights[a], barycentric.x); Add(vertexWeights[b], barycentric.y); Add(vertexWeights[c], barycentric.z);

                void Add(BoneWeight value, float amount)
                {
                    weights[value.boneIndex0] += value.weight0 * amount;
                    weights[value.boneIndex1] += value.weight1 * amount;
                    weights[value.boneIndex2] += value.weight2 * amount;
                    weights[value.boneIndex3] += value.weight3 * amount;
                }
            }

            internal Matrix4x4 Capture()
            {
                Matrix4x4 matrix = Matrix4x4.zero;
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    Matrix4x4 posed = posedBones[bone] = bones[bone].localToWorldMatrix * bind[bone];
                    if (weights[bone] <= 0f) continue;
                    for (int cell = 0; cell < 16; cell++) matrix[cell] += posed[cell] * weights[bone];
                }
                return matrix * sourceToMesh;
            }

            internal void CaptureFloor(Transform stage)
            {
                Matrix4x4 toStage = stage.worldToLocalMatrix;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    BoneWeight weight = vertexWeights[vertex];
                    Vector3 point = vertices[vertex];
                    Vector3 world = posedBones[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0 +
                        posedBones[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1 +
                        posedBones[weight.boneIndex2].MultiplyPoint3x4(point) * weight.weight2 +
                        posedBones[weight.boneIndex3].MultiplyPoint3x4(point) * weight.weight3;
                    floorVertices[vertex] = toStage.MultiplyPoint3x4(world);
                }
            }

            internal bool TryGetFloorHeight(Vector3 point, float maximumHeight, out float height)
            {
                height = float.NegativeInfinity;
                for (int triangle = 0; triangle < plasterTriangles.Length; triangle += 3)
                {
                    Vector3 a = floorVertices[plasterTriangles[triangle]],
                        b = floorVertices[plasterTriangles[triangle + 1]], c = floorVertices[plasterTriangles[triangle + 2]];
                    Vector3 ab = b - a, ac = c - a;
                    Vector3 normal = Vector3.Cross(ab, ac);
                    if (normal.y <= normal.magnitude * .5f) continue;
                    // Vertical projection in the normalized stage, including
                    // the raised tops of the original architrave thresholds.
                    float determinant = ab.x * ac.z - ab.z * ac.x;
                    if (Mathf.Abs(determinant) < .000001f) continue;
                    float x = point.x - a.x, z = point.z - a.z;
                    float u = (x * ac.z - z * ac.x) / determinant;
                    float v = (ab.x * z - ab.z * x) / determinant;
                    if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                    float candidate = a.y + ab.y * u + ac.y * v;
                    if (candidate < maximumHeight && candidate > height) height = candidate;
                }
                return !float.IsNegativeInfinity(height);
            }
        }
    }
}
