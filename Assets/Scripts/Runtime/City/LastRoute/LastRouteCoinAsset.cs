using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The same authored brass coin in the hand and the glovebox.</summary>
    public static class LastRouteCoinAsset
    {
        public const string ResourcePath = "Vehicles/LastRouteCoin3D";
        public const string SingleMeshName = "FerrymanCoin";
        public const string PileMeshName = "GloveboxCoins";

        private static Mesh single;
        private static Mesh pile;

        public static Mesh SingleMesh => Load(ref single, SingleMeshName);
        public static Mesh PileMesh => Load(ref pile, PileMeshName);

        public static void ValidateOrThrow()
        {
            _ = SingleMesh;
            _ = PileMesh;
        }

        public static GameObject CreateSingle(Transform parent)
        {
            var coin = new GameObject("Ferryman Coin");
            coin.transform.SetParent(parent, false);
            AttachRenderer(coin, SingleMesh);
            return coin;
        }

        public static MeshRenderer AttachRenderer(GameObject owner, Mesh mesh)
        {
            owner.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = owner.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            RuntimePrimitiveFactory.SetColor(renderer, LastRouteFerrymanCoin.CoinColor);
            return renderer;
        }

        private static Mesh Load(ref Mesh cached, string name)
        {
            if (cached != null)
            {
                return cached;
            }

            foreach (Mesh candidate in Resources.LoadAll<Mesh>(ResourcePath))
            {
                if (candidate.name == name)
                {
                    if (name == SingleMeshName &&
                        (candidate.bounds.size - new Vector3(
                            LastRouteFerrymanCoin.DiameterMeters,
                            LastRouteFerrymanCoin.ThicknessMeters,
                            LastRouteFerrymanCoin.DiameterMeters)).sqrMagnitude > 0.00000001f)
                    {
                        throw new InvalidOperationException("The Ferryman coin must import in metres with +Y thickness.");
                    }

                    if (name == PileMeshName)
                    {
                        Bounds bounds = candidate.bounds;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            if (bounds.min[axis] < LastRouteGloveboxCoins.InteriorMinimum[axis] - 0.0001f ||
                                bounds.max[axis] > LastRouteGloveboxCoins.InteriorMaximum[axis] + 0.0001f)
                            {
                                throw new InvalidOperationException("The authored coin pile must import inside the glovebox in hinge-relative metres.");
                            }
                        }
                    }

                    cached = candidate;
                    return cached;
                }
            }

            throw new InvalidOperationException($"Missing authored coin mesh '{name}' in '{ResourcePath}'.");
        }
    }
}
