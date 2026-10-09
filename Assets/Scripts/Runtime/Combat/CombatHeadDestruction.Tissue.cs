using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatHeadDestruction
    {
        public int ExposedSkullSectorCountFor(CombatActor actor)
        {
            int count = 0;
            if (actor != null && heads.TryGetValue(actor, out Head head) && head.Active)
                foreach (Sector sector in head.Sectors)
                    if (!sector.Detached && actor.BodyDamage.TissueLoss(BodyDamageRegion.Head, sector.Index % 8 / 2) >= 1f) count++;
            return count;
        }

        internal int TissuePatchFor(CombatActor actor, Vector3 point)
        {
            Head head = RequireHead(actor);
            Vector3 local = head.Bone.InverseTransformPoint(point);
            float best = float.PositiveInfinity; int patch = 0;
            foreach (Sector sector in head.Sectors)
            {
                float distance = sector.Bounds.SqrDistance(local);
                if (distance >= best) continue;
                best = distance; patch = sector.Index % 8 / 2;
            }
            return patch;
        }

        private static bool TissueVisible(Head head, Piece piece)
        {
            if (!piece.Visible) return false;
            float loss = head.Actor.BodyDamage.TissueLoss(BodyDamageRegion.Head, piece.TissuePatch);
            if (piece.Bone) return loss >= 1f;
            return piece.Interior ? loss < 1f : loss < .25f;
        }

        private static List<Piece> VisiblePieces(Head head, Sector sector)
        {
            var result = new List<Piece>();
            foreach (Piece piece in sector.Pieces) if (TissueVisible(head, piece)) result.Add(piece);
            return result;
        }

        private void PrepareSkull(Head head, SkinnedMeshRenderer source)
        {
            GameObject model = Resources.Load<GameObject>("CombatGore/Skull" + (head.Actor.IsHero ? "Hero" : "Npc"));
            if (model == null) throw new InvalidOperationException("Missing authored combat skull.");
            if (bone == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
                Texture2D texture = Resources.Load<Texture2D>("CombatGore/BoneSurface");
                if (shader == null || texture == null) throw new InvalidOperationException("Missing authored skull surface.");
                bone = new Material(shader) { name = "Combat Skull Shared" };
                bone.SetTexture("_BaseMap", texture); bone.SetColor("_BaseColor", Color.white);
                bone.SetFloat("_Smoothness", .14f); bone.SetFloat("_Cull", 2f);
            }
            foreach (SkinnedMeshRenderer template in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string label = template.name.Split(new[] { "__" }, StringSplitOptions.None)[0];
                if (!label.StartsWith("SkullSector", StringComparison.Ordinal) || !int.TryParse(label.Substring(11), out int index) || index < 0 || index > 15)
                    throw new InvalidOperationException("Invalid skull sector: " + template.name);
                var host = new GameObject("Bone " + template.name); host.transform.SetParent(source.transform, false);
                head.Hosts.Add(host);
                var skin = host.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = template.sharedMesh; skin.bones = source.bones; skin.rootBone = source.rootBone;
                skin.sharedMaterial = bone; skin.localBounds = source.localBounds; skin.updateWhenOffscreen = true;
                skin.enabled = false; Player3DHeadVisibility.RegisterDerived(source, skin);
                head.Sectors[index].Pieces.Add(new Piece { Skin = skin, Source = source, Visible = true,
                    Bone = true, Anatomical = true, Bounds = MeasureInHead(head, skin), TissuePatch = index % 8 / 2 });
            }
        }

        internal void SynchronizeTissue(CombatActor actor, bool captureImmediately = true)
        {
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            Head head = RequireHead(actor);
            bool changed = !actor.BodyDamage.IsAttached(BodyDamageRegion.Head);
            for (int p = 0; p < 4; p++) changed |= actor.BodyDamage.TissueLoss(BodyDamageRegion.Head, p) > 0f;
            TissueRequireTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stamp;
            if (!changed && !head.Active) return;
            if (!head.Active)
            {
                stamp = System.Diagnostics.Stopwatch.GetTimestamp();
                Activate(head);
                TissueActivationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stamp;
            }
            stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            long previousRelease = TissueReleaseTicks;
            var proxies = new List<BoxCollider>(); var surfaces = new List<SkinnedMeshRenderer>();
            foreach (Sector sector in head.Sectors)
            {
                if (sector.Detached) continue;
                if (!actor.BodyDamage.IsAttached(BodyDamageRegion.Head))
                {
                    sector.Detached = true; sector.Proxy.enabled = false;
                    long releaseStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                    Release(head, VisiblePieces(head, sector), head.Bone.TransformPoint(sector.Bounds.center), Vector3.down * .3f, false, sector.Index);
                    TissueReleaseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - releaseStamp;
                    continue;
                }
                sector.Proxy.enabled = true; proxies.Add(sector.Proxy);
                var tissue = new List<Piece>();
                foreach (Piece piece in sector.Pieces)
                    if (!piece.Bone && piece.Visible && !piece.TissueEmitted &&
                        actor.BodyDamage.TissueLoss(BodyDamageRegion.Head, piece.TissuePatch) >= 1f)
                    { piece.TissueEmitted = true; tissue.Add(piece); }
                if (tissue.Count > 0)
                {
                    long releaseStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                    Release(head, tissue, head.Bone.TransformPoint(sector.Bounds.center), Vector3.down * .4f, false, sector.Index);
                    TissueReleaseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - releaseStamp;
                }
                foreach (Piece piece in sector.Pieces)
                {
                    bool visible = TissueVisible(head, piece);
                    Player3DHeadVisibility.SetDerivedEnabled(piece.Source, piece.Skin, visible);
                    if (visible && piece.Anatomical) surfaces.Add(piece.Skin);
                }
            }
            bool strippedHead = true;
            for (int patch = 0; patch < CombatBodyDamageState.PatchCount; patch++)
                strippedHead &= actor.BodyDamage.TissueLoss(BodyDamageRegion.Head, patch) >= 1f;
            foreach (Piece piece in head.Brains)
            {
                if (piece.Detached) continue;
                if (strippedHead || !actor.BodyDamage.IsAttached(BodyDamageRegion.Head))
                {
                    long releaseStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                    Release(head, new List<Piece> { piece }, head.Bone.TransformPoint(piece.Bounds.center), Vector3.down * .3f, true, head.BrainCount);
                    TissueReleaseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - releaseStamp;
                    piece.Detached = true; head.BrainCount++;
                }
                else surfaces.Add(piece.Skin);
            }
            TissueVisibilityTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stamp - (TissueReleaseTicks - previousRelease);
            stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            actor.Hurtboxes.SetHeadShapes(proxies, surfaces, captureImmediately);
            TissueShapesTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stamp;
            stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            actor.Ragdoll.PhysicsController.SetCombatHeadCollisionEnabled(false);
            TissuePhysicsTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stamp;
        }
    }
}
