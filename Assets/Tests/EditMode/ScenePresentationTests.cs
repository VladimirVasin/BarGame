using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ScenePresentationTests
    {
        [Test]
        public void Resources_ContainNativeCompositeMaterial()
        {
            Material material = Resources.Load<Material>("Materials/SceneComposite");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Hidden/BarPromenade/SceneComposite"));
            Assert.That(material.passCount, Is.EqualTo(2));
        }

        [Test]
        public void PcRenderer_ContainsConfiguredSceneCompositeFeature()
        {
            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Settings/PC_Renderer.asset");
            Assert.That(renderer, Is.Not.Null);
            var feature = renderer.rendererFeatures.Find(item => item is SceneCompositeRendererFeature)
                as SceneCompositeRendererFeature;
            Assert.That(feature, Is.Not.Null);
            Assert.That(feature.CompositeMaterial, Is.Not.Null);
            Assert.That(feature.isActive, Is.True);
        }

        [Test]
        public void CityLighting_RegisteredMetadataKeepsPooledAndMovingZonesLiveWithoutAllocations()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            Vector3 residential = Position(CityDistrictKind.Residential);
            Vector3 industrial = Position(CityDistrictKind.Industrial);
            var fixtures = new Dictionary<Vector3, CityLightingPresentation.Zone>
            {
                [residential] = CityLightingPresentation.Zone.River,
                [industrial] = CityLightingPresentation.Zone.Park
            };
            var host = new GameObject("Test registered light metadata");
            try
            {
                Light light = host.AddComponent<Light>();
                light.name = "Port Vessel Searchlight";
                var fixedSource = new CityLightingPresentation.SourceZoneBinding(light.name);
                light.name = "Pooled Street Light 0";
                var pooledSource = new CityLightingPresentation.SourceZoneBinding(light.name);
                light.name = "Bus Headlight 0";
                var districtSource = new CityLightingPresentation.SourceZoneBinding(light.name);
                // Names are immutable registration metadata. Only position and
                // the live pooled fixture assignment participate after capture.
                light.name = "Cannery Service Work Light";
                Assert.That(Resolve(fixedSource, residential), Is.EqualTo(CityLightingPresentation.Zone.CoastPort));
                Assert.That(Resolve(fixedSource, industrial), Is.EqualTo(CityLightingPresentation.Zone.CoastPort));
                Assert.That(Resolve(pooledSource, residential), Is.EqualTo(CityLightingPresentation.Zone.River));
                Assert.That(Resolve(pooledSource, industrial), Is.EqualTo(CityLightingPresentation.Zone.Park));
                Assert.That(Resolve(districtSource, residential), Is.EqualTo(CityLightingPresentation.Zone.Residential));
                Assert.That(Resolve(districtSource, industrial), Is.EqualTo(CityLightingPresentation.Zone.Industrial));
                fixtures.Remove(industrial);
                Assert.That(Resolve(pooledSource, industrial), Is.EqualTo(CityLightingPresentation.Zone.Industrial));
                fixtures[industrial] = CityLightingPresentation.Zone.ChurchCemetery;
                Assert.That(Resolve(pooledSource, industrial), Is.EqualTo(CityLightingPresentation.Zone.ChurchCemetery));

                int checksum = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int sample = 0; sample < 64; sample++)
                {
                    checksum += (int)Resolve(fixedSource, residential);
                    checksum += (int)Resolve(pooledSource, industrial);
                    checksum += (int)Resolve(districtSource, industrial);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(checksum, Is.GreaterThan(0));
                Assert.That(allocated, Is.Zero, "Per-camera zone resolution must reuse captured metadata without name reads or interface enumerators.");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }

            CityLightingPresentation.Zone Resolve(CityLightingPresentation.SourceZoneBinding binding, Vector3 position) =>
                CityLightingPresentation.ResolveSourceZone(binding, position, fixtures, layout);

            Vector3 Position(CityDistrictKind kind)
            {
                foreach (CityDistrictDescriptor district in layout.Districts)
                    if (district.Kind == kind)
                    {
                        Rect cell = layout.GetCellWorldBounds(district.Cells[0]);
                        return new Vector3(cell.center.x, 0f, cell.center.y);
                    }
                throw new InvalidOperationException("The default city has no " + kind + " district.");
            }
        }
    }
}
