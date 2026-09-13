using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private Renderer[][] workerClothes;
        private Color[][] workerClothingColors;
        private Vector4[][] workerFabricUv;
        private readonly bool[] workerAppearanceDirty = new bool[5];
        private MaterialPropertyBlock workerClothingBlock;
        private Texture workerFabricTexture;
        private static readonly Color[] WorkerCoatColors =
        {
            new Color(.27f, .31f, .29f), new Color(.58f, .62f, .57f),
            new Color(.22f, .29f, .34f), new Color(.45f, .40f, .29f),
            new Color(.31f, .28f, .25f)
        };
        private static readonly Color[] WorkerKnitColors =
        {
            new Color(.24f, .25f, .23f), new Color(.43f, .46f, .41f),
            new Color(.27f, .30f, .30f), new Color(.34f, .34f, .28f),
            new Color(.23f, .24f, .26f)
        };
        private static readonly Color[] WorkerGloveColors =
        {
            new Color(.34f, .29f, .22f), new Color(.60f, .63f, .53f),
            new Color(.39f, .37f, .30f), new Color(.54f, .50f, .40f),
            new Color(.23f, .23f, .21f)
        };

        private void CreateCrewAppearance()
        {
            workerClothingBlock = new MaterialPropertyBlock();
            workerClothes = new Renderer[workers.Length][];
            workerClothingColors = new Color[workers.Length][];
            workerFabricUv = new Vector4[workers.Length][];
            Material fabric = CityPortAssetProvider.GetSurfaceMaterial("Fabric");
            workerFabricTexture = fabric.GetTexture("_BaseMap");
            for (int i = 0; i < workers.Length; i++)
            {
                // Her authored coloured atlas survives enable/disable untouched.
                if (i == CanneryWomanPresentation.WorkerSlot)
                {
                    workerClothes[i] = Array.Empty<Renderer>();
                    workerClothingColors[i] = Array.Empty<Color>();
                    workerFabricUv[i] = Array.Empty<Vector4>();
                    continue;
                }
                workerClothes[i] = Array.FindAll(workers[i].GetComponentsInChildren<Renderer>(true),
                    renderer => renderer.name.StartsWith("CLO_", StringComparison.Ordinal) || IsCrewGlove(renderer.name));
                workerClothingColors[i] = new Color[workerClothes[i].Length];
                workerFabricUv[i] = new Vector4[workerClothes[i].Length];
                for (int j = 0; j < workerClothes[i].Length; j++)
                {
                    Renderer renderer = workerClothes[i][j];
                    string part = renderer.name;
                    renderer.GetPropertyBlock(workerClothingBlock);
                    Color source = workerClothingBlock.GetColor("_BaseColor");
                    workerClothingBlock.Clear();
                    bool knit = part.Contains("Cap") || part.Contains("Scarf") ||
                        part.Contains("Collar") || part.Contains("Cuff");
                    Color color = IsCrewGlove(part) ? WorkerGloveColors[i] : knit ? WorkerKnitColors[i] :
                        part.Contains("Thigh") || part.Contains("Shin") ? source * new Color(.83f, .86f, .86f) :
                        WorkerCoatColors[i] * (part.Contains("Pocket") ? .78f : 1f);
                    color.a = 1f;
                    workerClothingColors[i][j] = color;
                    // Indoors the loose scarf and winter ear flaps are put
                    // away. Caps, fitted cuffs and the same complete rig stay.
                    if (i >= 1 && i <= 3 && (part.Contains("Scarf") || part.Contains("EarFlap")))
                        renderer.enabled = false;
                    if (!IsCrewFabric(part)) continue;
                    workerFabricUv[i][j] = CrewFabricUvTransform(renderer);
                    renderer.sharedMaterial = fabric;
                }
                workerAppearanceDirty[i] = true;
            }
            CreateWorkerApron(1, new Color(.59f, .63f, .57f));
            CreateWorkerApron(3, new Color(.52f, .46f, .34f));
        }

        private void CreateWorkerApron(int index, Color color)
        {
            VillageResidentPresentation actor = workers[index];
            GameObject workwear = CityCanneryAssetProvider.Create("Workwear", actor.transform);
            // Start from the same neutral authored body used when the library
            // initialized this resident, then retain each imported world scale
            // while attaching the two cloth sections to the existing joints.
            DockApronPart(Require(workwear.transform, "ApronBib"), Require(actor.ModelRoot, "chest"),
                new Vector3(0f, 1.20f, .176f));
            DockApronPart(Require(workwear.transform, "ApronSkirt"), Require(actor.ModelRoot, "pelvis"),
                new Vector3(0f, .79f, .18f));
            // The complete garment now belongs to the worker rig; destroying
            // the worker also owns its meshes. No material or mesh is cloned.
            if (Application.isPlaying) Destroy(workwear);
            else DestroyImmediate(workwear);

            void DockApronPart(Transform section, Transform bone, Vector3 localDock)
            {
                // The new model already inherits the actor's facing. Retain
                // its imported FBX basis as well as its scale when docking.
                section.position = actor.transform.TransformPoint(localDock);
                section.SetParent(bone, true);
                foreach (Renderer renderer in section.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.GetPropertyBlock(workerClothingBlock);
                    workerClothingBlock.SetColor("_BaseColor", color);
                    workerClothingBlock.SetColor("_Color", color);
                    renderer.SetPropertyBlock(workerClothingBlock);
                    workerClothingBlock.Clear();
                }
            }
        }

        private void ApplyCrewAppearance(int index)
        {
            for (int j = 0; j < workerClothes[index].Length; j++)
            {
                Renderer renderer = workerClothes[index][j];
                renderer.GetPropertyBlock(workerClothingBlock);
                workerClothingBlock.SetColor("_BaseColor", workerClothingColors[index][j]);
                workerClothingBlock.SetColor("_Color", workerClothingColors[index][j]);
                if (IsCrewFabric(renderer.name))
                {
                    workerClothingBlock.SetTexture("_BaseMap", workerFabricTexture);
                    workerClothingBlock.SetTexture("_MainTex", workerFabricTexture);
                    workerClothingBlock.SetVector("_BaseMap_ST", workerFabricUv[index][j]);
                }
                renderer.SetPropertyBlock(workerClothingBlock);
                workerClothingBlock.Clear();
            }
            workerAppearanceDirty[index] = false;
        }

        private static bool IsCrewGlove(string part) => part.StartsWith("GEO_Hand.", StringComparison.Ordinal) ||
            part.StartsWith("GEO_Thumb.", StringComparison.Ordinal);

        private static bool IsCrewFabric(string part) => part.StartsWith("CLO_", StringComparison.Ordinal) &&
            !part.StartsWith("CLO_Shin", StringComparison.Ordinal);

        private static Vector4 CrewFabricUvTransform(Renderer renderer)
        {
            // Preserve the authored mapping while unpacking the resident's
            // grayscale atlas cells to the shared cloth at one repeat/metre.
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
                renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3 size = mesh.bounds.size;
            float width = size.x * Mathf.Abs(renderer.transform.lossyScale.x);
            float height = size.y * Mathf.Abs(renderer.transform.lossyScale.y);
            bool coat = renderer.name == "CLO_WinterCoat";
            bool knit = renderer.name.Contains("Cap") || renderer.name.Contains("Scarf") ||
                renderer.name.Contains("Collar") || renderer.name.Contains("Cuff");
            float cellX = coat ? 2f : 130f, cellY = coat || knit ? 130f : 2f;
            float u = Mathf.Max(.01f, width) * 256f / (coat || knit ? 123f : 60f);
            float v = Mathf.Max(.01f, height) * 256f / 123f;
            return new Vector4(u, v, -cellX / 256f * u, -cellY / 256f * v);
        }
    }
}
