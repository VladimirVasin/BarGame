using UnityEngine;

namespace BarPromenade
{
    /// <summary>A measured Blender lid, pivoting at its actual rear hinge.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletLid : MonoBehaviour
    {
        // Beyond vertical the 0.51 m leaf tips into the cistern. At 90
        // degrees its rear face retains the measured hinge-to-tank gap.
        public const float OpenDegrees = 90f;
        private float angle;
        private bool open;
        public float Angle => angle;
        public bool IsOpen => open;

        public static HomeToiletLid Create(Transform room)
        {
            GameObject asset = Resources.Load<GameObject>("HomeToiletAction/Models/ToiletLid");
            if (asset == null)
                throw new System.InvalidOperationException("Missing Blender-authored Home toilet lid.");
            var hinge = new GameObject("Home Bathroom Toilet Lid");
            hinge.transform.SetParent(room, false);
            hinge.transform.localPosition = new Vector3(4.32f, 0.655f, 1.40f);
            GameObject model = Instantiate(asset, hinge.transform, false);
            model.name = "Toilet Lid Model";
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                RuntimePrimitiveFactory.SetColor(renderer, new Color(0.33f, 0.31f, 0.24f));
            }
            HomeToiletLid lid = hinge.AddComponent<HomeToiletLid>();
            lid.Apply();
            return lid;
        }

        public static GameObject CreateWater(Transform room)
        {
            GameObject asset = Resources.Load<GameObject>("HomeToiletAction/Models/BowlWater");
            if (asset == null)
                throw new System.InvalidOperationException("Missing Blender-authored toilet water surface.");
            var root = new GameObject("Home Bathroom Toilet Water");
            root.transform.SetParent(room, false);
            root.transform.localPosition = new Vector3(4.05f, 0.4373f, 1.40f);
            GameObject model = Instantiate(asset, root.transform, false);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                RuntimePrimitiveFactory.SetColor(renderer, new Color(0.14f, 0.16f, 0.12f));
            }
            return root;
        }

        public static GameObject CreatePaper(Transform room, Bounds cistern)
        {
            GameObject asset = Resources.Load<GameObject>("HomeToiletAction/Models/ToiletPaperRoll");
            if (asset == null)
                throw new System.InvalidOperationException("Missing Blender-authored toilet paper roll.");
            var root = new GameObject("Home Bathroom Toilet Paper");
            root.transform.SetParent(room, false);
            root.transform.position = new Vector3(cistern.center.x,
                cistern.max.y + 0.0475f, cistern.min.z + 0.06f);
            GameObject model = Instantiate(asset, root.transform, false);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                RuntimePrimitiveFactory.SetColor(renderer, renderer.name.Contains("Core")
                    ? new Color(0.36f, 0.29f, 0.19f) : new Color(0.72f, 0.70f, 0.61f));
            }
            return root;
        }

        public void Open() { open = true; }
        public void Close() { open = false; }
        private void Update()
        {
            angle = Mathf.MoveTowards(angle, open ? OpenDegrees : 0f, Time.deltaTime * 300f);
            Apply();
        }
        private void Apply() => transform.localRotation = Quaternion.Euler(0f, 90f, 0f) *
            Quaternion.AngleAxis(angle, Vector3.right);
    }
}
