using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>A visible, contact-corrected copy of an authored skinned scarf part.</summary>
    internal sealed class PlayerScarfContactSurface : IDisposable
    {
        private readonly Transform actor;
        private readonly Mesh scratch;
        private readonly List<Vector3> baked = new List<Vector3>();
        private readonly Vector3[] current;
        private readonly Vector3[] previous;
        private readonly Vector3[] local;
        private readonly int[] triangles;
        private bool hasPrevious;

        public SkinnedMeshRenderer Source { get; }
        public SkinnedMeshRenderer Renderer { get; }
        public Mesh Mesh { get; }
        public Vector3[] WorldVertices => current;
        public Vector3[] PreviousWorldVertices => previous;
        public bool HasPrevious => hasPrevious;

        public PlayerScarfContactSurface(SkinnedMeshRenderer source, Transform owner, bool mirror)
        {
            Source = source;
            actor = owner;
            source.forceRenderingOff = true;
            source.enabled = true;
            scratch = new Mesh { name = source.name + " Source Bake", hideFlags = HideFlags.HideAndDontSave };
            Mesh = UnityEngine.Object.Instantiate(source.sharedMesh);
            Mesh.name = source.name + " Contact Surface";
            Mesh.hideFlags = HideFlags.HideAndDontSave;
            Mesh.MarkDynamic();
            Mesh.ClearBlendShapes();
            current = new Vector3[Mesh.vertexCount];
            previous = new Vector3[Mesh.vertexCount];
            local = new Vector3[Mesh.vertexCount];
            triangles = Mesh.triangles;
            var weights = new BoneWeight[Mesh.vertexCount];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            Mesh.boneWeights = weights;
            Mesh.bindposes = new[] { Matrix4x4.identity };
            var host = new GameObject(source.name);
            host.transform.SetParent(actor, false);
            host.layer = actor.gameObject.layer;
            Renderer = host.AddComponent<SkinnedMeshRenderer>();
            Renderer.sharedMesh = Mesh;
            Renderer.sharedMaterial = PlayerScarfResources.SharedMaterial;
            Renderer.bones = new[] { host.transform };
            Renderer.rootBone = host.transform;
            Renderer.updateWhenOffscreen = true;
            Renderer.shadowCastingMode = mirror ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
            Renderer.enabled = false;
            PreparePose();
            WriteMesh();
        }

        public void PreparePose()
        {
            // useScale compensates the renderer transform scale, producing
            // local vertices for the full matrix below, including FBX units.
            Source.BakeMesh(scratch, true);
            scratch.GetVertices(baked);
            if (baked.Count != current.Length)
                throw new InvalidOperationException("Scarf contact surface topology changed.");
            Matrix4x4 toWorld = Source.transform.localToWorldMatrix;
            for (int i = 0; i < current.Length; i++) current[i] = toWorld.MultiplyPoint3x4(baked[i]);
        }

        public void Resolve(PlayerScarfCollisionWorld world, bool reset)
        {
            if (reset || !hasPrevious) Array.Copy(current, previous, current.Length);
            PlayerScarfContactSolver.Resolve(previous, current, triangles, world, .003f);
            WriteMesh();
            Array.Copy(current, previous, current.Length);
            hasPrevious = true;
        }

        public void ResolveBody(PlayerScarfBodyContacts body)
        {
            body.Resolve(current);
            WriteMesh();
        }

        public void ResetHistory() => hasPrevious = false;

        private void WriteMesh()
        {
            Matrix4x4 toActor = actor.worldToLocalMatrix;
            for (int i = 0; i < current.Length; i++) local[i] = toActor.MultiplyPoint3x4(current[i]);
            Mesh.vertices = local;
            Mesh.RecalculateNormals();
            Mesh.RecalculateBounds();
            Renderer.localBounds = Mesh.bounds;
        }

        public void CopyTo(PlayerScarfContactSurface target)
        {
            if (target == null || target.local.Length != local.Length) return;
            // Both renderers are identity children of their respective actor;
            // the mirror actor already owns the geometric reflection.
            Array.Copy(local, target.local, local.Length);
            target.Mesh.vertices = target.local;
            target.Mesh.RecalculateNormals();
            target.Mesh.RecalculateBounds();
            target.Renderer.localBounds = target.Mesh.bounds;
        }

        public void Dispose()
        {
            PlayerScarfResources.DestroyOwned(Renderer != null ? Renderer.gameObject : null);
            PlayerScarfResources.DestroyOwned(Mesh);
            PlayerScarfResources.DestroyOwned(scratch);
        }
    }
}
