using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A passive authored pile, attached to the compartment rather than its lid.</summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteGloveboxCoins : MonoBehaviour
    {
        public const int CoinCount = 107;
        public static readonly Vector3 InteriorMinimum = new Vector3(-0.14f, 0.02f, -0.21f);
        public static readonly Vector3 InteriorMaximum = new Vector3(0.14f, 0.12f, 0f);

        public MeshRenderer PileRenderer { get; private set; }

        public static LastRouteGloveboxCoins Create(LastRouteCarAssetRegistry registry)
        {
            if (registry == null || registry.Body == null || registry.GloveboxLidPivot == null)
            {
                return null;
            }

            var owner = new GameObject("Glovebox Coins");
            // The FBX body carries its authoring scale. Reparent a metre-sized
            // world object with that scale preserved; following the body also
            // follows the suspension, while an opening lid cannot move the pile.
            // Body retains the FBX's axis-conversion rotation: its forward is
            // almost vertical. The bare mesh is already +Y-up, like the chess
            // assets, so use the prefab frame and the model's authored -Z nose.
            owner.transform.SetPositionAndRotation(
                registry.GloveboxLidPivot.position,
                registry.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            owner.transform.SetParent(registry.Body, true);
            var coins = owner.AddComponent<LastRouteGloveboxCoins>();
            coins.PileRenderer = LastRouteCoinAsset.AttachRenderer(owner, LastRouteCoinAsset.PileMesh);
            coins.ValidatePlacementOrThrow(registry);
            return coins;
        }

        public void ValidatePlacementOrThrow(LastRouteCarAssetRegistry registry)
        {
            // This checks the placed mesh against the imported compartment,
            // independently of the generator's own local-space bounds gate.
            foreach (LastRouteCarRendererBinding binding in registry.Bindings)
            {
                if (binding.Role != "glovebox_compartment" || binding.Renderer == null)
                    continue;
                MeshFilter compartment = binding.Renderer.GetComponent<MeshFilter>();
                Bounds cavity = compartment.sharedMesh.bounds;
                float scale = Mathf.Min(Mathf.Abs(compartment.transform.lossyScale.x),
                    Mathf.Abs(compartment.transform.lossyScale.y), Mathf.Abs(compartment.transform.lossyScale.z));
                cavity.Expand(0.0002f / Mathf.Max(scale, 0.0001f));
                Bounds pile = LastRouteCoinAsset.PileMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? pile.min.x : pile.max.x,
                        (corner & 2) == 0 ? pile.min.y : pile.max.y,
                        (corner & 4) == 0 ? pile.min.z : pile.max.z);
                    point = compartment.transform.InverseTransformPoint(transform.TransformPoint(point));
                    if (!cavity.Contains(point))
                        throw new InvalidOperationException("The placed coin pile leaves the actual glovebox compartment.");
                }
                return;
            }
            throw new InvalidOperationException("The coin pile requires the authored glovebox compartment.");
        }
    }
}
