using UnityEngine;

namespace BarPromenade
{
    internal static class AlpineVillageWoodpileBuilder
    {
        public static void Build(Transform parent, AlpineVillagePlan village, AlpineVillageWoodpilePlan plan)
        {
            GameObject pile = VillageLifePropLibrary.Create(VillageLifePropKind.LogStack, parent, plan.Name);
            Vector3 position = plan.Center;
            Vector2 xz = new Vector2(position.x, position.z);
            position.y = Mathf.Max(AlpineVillageTerrainSampler.SampleHeight(village, xz),
                AlpineVillageTerrainSampler.SampleMeshHeight(village, xz));
            pile.transform.SetPositionAndRotation(position, plan.Rotation);
            for (int index = 0; index < 3; index++)
            {
                GameObject log = VillageLifePropLibrary.Create(VillageLifePropKind.Log, pile.transform, "Top Log " + index);
                log.transform.localPosition = VillageLifePropLibrary.GetAnchor(VillageLifePropKind.LogStack, "Log" + index);
            }
            foreach (MeshFilter mesh in pile.GetComponentsInChildren<MeshFilter>())
                mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;

            var target = new GameObject("Woodpile Interaction");
            target.transform.SetParent(pile.transform, false);
            target.transform.localPosition = new Vector3(0f, .45f, -.35f);
            var trigger = target.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.65f, .8f, .30f);
            target.AddComponent<WoodpileInteraction>();
        }
    }
}
