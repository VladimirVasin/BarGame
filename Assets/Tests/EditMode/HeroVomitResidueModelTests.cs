using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The marks the hero's vomit leaves: puddles that merge on one plane
    /// and stay apart across planes, a radius that only grows and never
    /// passes the cap, a hard limit on puddles and lumps, and the ragged fan
    /// each puddle is drawn as — the same fan for the same seed.
    /// </summary>
    public sealed class HeroVomitResidueModelTests
    {
        private const int Seed = 0x564D;
        private const float RodVolume = 0.6f * 0.04f * 0.04f;

        [Test]
        public void Impact_WithinMergeRadiusOnTheSamePlaneGrowsTheOnePuddle()
        {
            var model = new HeroVomitResidueModel(Seed);

            Assert.That(
                model.AddImpact(Vector3.zero, Vector3.up, RodVolume),
                Is.EqualTo(HeroVomitResidueChange.Created));
            Assert.That(model.PatchCount, Is.EqualTo(1));
            Assert.That(model.Dirty, Is.True);
            model.ClearDirty();

            Assert.That(
                model.AddImpact(new Vector3(0.2f, 0f, 0.1f), Vector3.up, RodVolume),
                Is.EqualTo(HeroVomitResidueChange.Grown));
            Assert.That(model.PatchCount, Is.EqualTo(1));
            Assert.That(model.Dirty, Is.True, "Growth is a change the view must draw.");

            HeroVomitPatch patch = model.Patches[0];
            Assert.That(patch.Normal, Is.EqualTo(Vector3.up));
            Assert.That(patch.Center.y, Is.EqualTo(0f).Within(1e-6f), "The centre stays in the plane.");
            Assert.That(patch.Center.x, Is.GreaterThan(0f), "The centre drifts toward where the liquid lands.");
            Assert.That(
                model.AddImpact(Vector3.zero, Vector3.up, 0f),
                Is.EqualTo(HeroVomitResidueChange.Ignored));
        }

        [Test]
        public void Impact_OnAWallBesideTheFloorPuddleStartsASecondPuddle()
        {
            var model = new HeroVomitResidueModel(Seed);
            model.AddImpact(Vector3.zero, Vector3.up, RodVolume);

            HeroVomitResidueChange change = model.AddImpact(
                new Vector3(0.1f, 0.05f, 0f),
                Vector3.right,
                RodVolume);

            Assert.That(change, Is.EqualTo(HeroVomitResidueChange.Created));
            Assert.That(model.PatchCount, Is.EqualTo(2));
            Assert.That(model.Patches[1].Normal, Is.EqualTo(Vector3.right));
            Assert.That(
                Vector3.Dot(model.Patches[1].Tangent, Vector3.right),
                Is.EqualTo(0f).Within(1e-6f),
                "The tangent lies in the wall.");
        }

        [Test]
        public void Impact_AStepAboveTheFloorIsAnotherPlaneEvenWithTheSameNormal()
        {
            var model = new HeroVomitResidueModel(Seed);
            model.AddImpact(Vector3.zero, Vector3.up, RodVolume);

            HeroVomitResidueChange change = model.AddImpact(
                new Vector3(0.1f, 0.15f, 0f),
                Vector3.up,
                RodVolume);

            Assert.That(change, Is.EqualTo(HeroVomitResidueChange.Created));
            Assert.That(model.PatchCount, Is.EqualTo(2));
        }

        [Test]
        public void Radius_IsMonotonicAndNeverPassesTheCap()
        {
            var model = new HeroVomitResidueModel(Seed);
            float previous = 0f;
            for (int index = 0; index < 2000; index++)
            {
                model.AddImpact(
                    new Vector3((index % 7) * 0.02f, 0f, (index % 5) * 0.02f),
                    Vector3.up,
                    RodVolume);
                Assert.That(model.PatchCount, Is.EqualTo(1));
                float radius = model.Patches[0].Radius;
                Assert.That(radius, Is.GreaterThanOrEqualTo(previous));
                Assert.That(radius, Is.GreaterThanOrEqualTo(HeroVomitResidueModel.MinRadius));
                Assert.That(radius, Is.LessThanOrEqualTo(HeroVomitResidueModel.MaxRadius));
                previous = radius;
            }

            Assert.That(
                previous,
                Is.EqualTo(HeroVomitResidueModel.MaxRadius).Within(1e-6f),
                "Two thousand rods are more than enough to fill the cap.");
            Assert.That(
                model.Patches[0].Area,
                Is.GreaterThan(Mathf.PI * HeroVomitResidueModel.MaxRadius * HeroVomitResidueModel.MaxRadius),
                "The area keeps the whole volume even after the radius is capped.");
        }

        [Test]
        public void Patches_TheThirteenthEvictsTheOldest()
        {
            var model = new HeroVomitResidueModel(Seed);
            for (int index = 0; index < HeroVomitResidueModel.MaxPatches; index++)
            {
                Assert.That(
                    model.AddImpact(new Vector3(index * 0.5f, 0f, 0f), Vector3.up, RodVolume),
                    Is.EqualTo(HeroVomitResidueChange.Created));
            }

            Assert.That(model.PatchCount, Is.EqualTo(HeroVomitResidueModel.MaxPatches));
            int firstOrdinal = model.Patches[0].Ordinal;

            Assert.That(
                model.AddImpact(
                    new Vector3(HeroVomitResidueModel.MaxPatches * 0.5f, 0f, 0f),
                    Vector3.up,
                    RodVolume),
                Is.EqualTo(HeroVomitResidueChange.Created));

            Assert.That(model.PatchCount, Is.EqualTo(HeroVomitResidueModel.MaxPatches));
            for (int index = 0; index < model.PatchCount; index++)
            {
                Assert.That(model.Patches[index].Ordinal, Is.Not.EqualTo(firstOrdinal));
                Assert.That(model.Patches[index].Center.x, Is.GreaterThan(0.25f), "The puddle at the origin is gone.");
            }

            Assert.That(model.Patches[model.PatchCount - 1].Center.x, Is.EqualTo(6f).Within(1e-6f));
        }

        [Test]
        public void Chunks_StopAtFortyEight()
        {
            var model = new HeroVomitResidueModel(Seed);
            for (int index = 0; index < HeroVomitResidueModel.MaxChunks; index++)
            {
                Assert.That(
                    model.AddChunk(new Vector3(index * 0.01f, 0f, 0f), Vector3.up),
                    Is.True,
                    $"chunk {index}");
            }

            Assert.That(model.ChunkCount, Is.EqualTo(HeroVomitResidueModel.MaxChunks));
            Assert.That(model.AddChunk(Vector3.zero, Vector3.up), Is.False);
            Assert.That(model.ChunkCount, Is.EqualTo(HeroVomitResidueModel.MaxChunks));

            int pale = 0;
            for (int index = 0; index < model.ChunkCount; index++)
            {
                HeroVomitChunk chunk = model.Chunks[index];
                Assert.That(
                    chunk.Size,
                    Is.InRange(HeroVomitResidueModel.ChunkSizeMinimum, HeroVomitResidueModel.ChunkSizeMaximum));
                Assert.That(chunk.YawDegrees, Is.InRange(0f, 360f));
                if (chunk.Pale)
                {
                    pale++;
                }
            }

            Assert.That(pale, Is.GreaterThan(0), "Some of the lumps are the pale kind.");
            Assert.That(pale, Is.LessThan(model.ChunkCount / 2), "Most are dark.");
        }

        [Test]
        public void Mesh_IsAFanOfTenRimVerticesInTheLiftedTangentPlane()
        {
            foreach (Vector3 normal in new[]
                     {
                         Vector3.up,
                         Vector3.right,
                         new Vector3(0.3f, 0.9f, -0.2f).normalized
                     })
            {
                var model = new HeroVomitResidueModel(Seed);
                Vector3 point = new Vector3(3f, 1f, -2f);
                for (int index = 0; index < 200; index++)
                {
                    model.AddImpact(point, normal, RodVolume);
                }

                HeroVomitPatch patch = model.Patches[0];
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                HeroVomitResidueModel.BuildPatchMesh(in patch, vertices, normals, uvs, triangles);

                Assert.That(vertices.Count, Is.EqualTo(HeroVomitResidueModel.RimVertexCount + 1), normal.ToString());
                Assert.That(normals.Count, Is.EqualTo(vertices.Count));
                Assert.That(uvs.Count, Is.EqualTo(vertices.Count));
                Assert.That(triangles.Count, Is.EqualTo(HeroVomitResidueModel.RimVertexCount * 3));

                Vector3 origin = patch.Center + patch.Normal * HeroVomitResidueModel.LiftMetres;
                Assert.That((vertices[0] - origin).magnitude, Is.LessThan(1e-5f), "The hub is the lifted centre.");
                for (int index = 1; index < vertices.Count; index++)
                {
                    Vector3 offset = vertices[index] - origin;
                    Assert.That(
                        Mathf.Abs(Vector3.Dot(offset, patch.Normal)),
                        Is.LessThan(1e-5f),
                        $"rim {index} lies in the lifted plane ({normal})");
                    Assert.That(
                        offset.magnitude,
                        Is.InRange(
                            HeroVomitResidueModel.RimRadiusMinimum * patch.Radius - 1e-5f,
                            patch.Radius + 1e-5f),
                        $"rim {index} radius ({normal})");
                    Assert.That(normals[index], Is.EqualTo(patch.Normal));
                    Assert.That(
                        uvs[index].magnitude,
                        Is.EqualTo(offset.magnitude / HeroVomitResidueModel.TextureMetresPerTile).Within(1e-4f),
                        "UVs are tangent-plane metres over the tile size.");
                }

                for (int index = 0; index < triangles.Count; index += 3)
                {
                    Vector3 a = vertices[triangles[index]];
                    Vector3 b = vertices[triangles[index + 1]];
                    Vector3 c = vertices[triangles[index + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    Assert.That(
                        Vector3.Dot(face, patch.Normal),
                        Is.GreaterThan(0f),
                        $"triangle {index / 3} faces the surface normal ({normal})");
                    Assert.That(triangles[index], Is.EqualTo(0), "Every triangle fans from the hub.");
                }
            }
        }

        [Test]
        public void Mesh_AppendsAfterExistingVerticesWithOffsetIndices()
        {
            var model = new HeroVomitResidueModel(Seed);
            model.AddImpact(Vector3.zero, Vector3.up, RodVolume);
            model.AddImpact(new Vector3(2f, 0f, 0f), Vector3.up, RodVolume);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            HeroVomitResidueModel.BuildPatchMesh(model.Patches[0], vertices, normals, uvs, triangles);
            HeroVomitResidueModel.BuildPatchMesh(model.Patches[1], vertices, normals, uvs, triangles);

            int perPatch = HeroVomitResidueModel.RimVertexCount + 1;
            Assert.That(vertices.Count, Is.EqualTo(perPatch * 2));
            Assert.That(triangles.Count, Is.EqualTo(HeroVomitResidueModel.RimVertexCount * 6));
            for (int index = HeroVomitResidueModel.RimVertexCount * 3; index < triangles.Count; index++)
            {
                Assert.That(triangles[index], Is.InRange(perPatch, perPatch * 2 - 1));
            }
        }

        [Test]
        public void Mesh_SameSeedSameImpactsSameFan()
        {
            List<Vector3> first = BuildFan(Seed);
            List<Vector3> second = BuildFan(Seed);
            List<Vector3> other = BuildFan(Seed + 1);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first), "Another seed is another contour.");
        }

        private static List<Vector3> BuildFan(int seed)
        {
            var model = new HeroVomitResidueModel(seed);
            for (int index = 0; index < 50; index++)
            {
                model.AddImpact(
                    new Vector3((index % 3) * 0.03f, 0f, (index % 4) * 0.02f),
                    Vector3.up,
                    RodVolume);
            }

            var vertices = new List<Vector3>();
            HeroVomitResidueModel.BuildPatchMesh(
                model.Patches[0],
                vertices,
                new List<Vector3>(),
                new List<Vector2>(),
                new List<int>());
            return vertices;
        }
    }
}
