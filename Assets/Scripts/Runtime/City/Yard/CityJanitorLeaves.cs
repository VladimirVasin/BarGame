using UnityEngine;

namespace BarPromenade
{
    /// <summary>The same three dry leaves remain at this worker's site until the visit ends.</summary>
    internal sealed class CityJanitorLeaves
    {
        private static readonly float[] StartX = { -.23f, -.065f, .10f };
        private static readonly Color32[] Colours = {
            new(137, 111, 67, 255), new(156, 132, 78, 255), new(126, 94, 58, 255)
        };
        private static readonly Vector3 Pile = new(.335f, 0f, .462f);
        private static readonly Vector3 Escaped = new(.50f, 0f, .19f);
        private static readonly Vector3 WaitingBroom = new(.13f, 0f, .33f);
        private readonly Transform[] leaves = new Transform[StartX.Length];
        private Quaternion groundFrame;
        private Vector3 origin;
        private float lastTime;
        internal Transform LooseLeaf => leaves[0];

        internal CityJanitorLeaves(Transform parent)
        {
            var prefab = Resources.Load<GameObject>("City/Grass/JanitorLeaf");
            var pigment = Resources.Load<Texture2D>("City/Grass/JanitorLeafAlbedo");
            var finish = new MaterialPropertyBlock();
            finish.SetTexture("_BaseMap", pigment);
            finish.SetFloat("_Smoothness", .12f);
            for (int i = 0; i < leaves.Length; i++)
            {
                Transform leaf = Object.Instantiate(prefab, parent, false).transform;
                leaf.name = "Swept leaf " + i;
                leaf.localScale *= 1f - .085f * i;
                leaves[i] = leaf;
                var renderer = leaf.GetComponentInChildren<MeshRenderer>();
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                finish.SetColor("_BaseColor", Colours[i]);
                renderer.SetPropertyBlock(finish);
            }
        }

        internal void Place(Vector3 feet, Vector3 normal, Vector3 forward)
        {
            origin = feet;
            groundFrame = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, normal), normal);
            lastTime = float.NaN;
        }

        // At zero an ordinary broad stroke begins. Its first second gathers all three leaves.
        // The loose leaf then skims out, settles, and returns only with the extra stroke.
        internal void Sample(float time)
        {
            time = Mathf.Clamp(time, 0f, 3.40f);
            if (time == lastTime) return;
            lastTime = time;
            float brushX = -.36f * Mathf.Cos(Mathf.PI * Mathf.Clamp01(time));
            float skid = Ease(time, 1f, 1.67f);
            float caught = Ease(time, 2.62f, 3.40f);
            for (int i = 0; i < leaves.Length; i++)
            {
                float x = Mathf.Clamp(brushX + .035f, StartX[i], Pile.x - i * .036f);
                Vector3 local = new(x, .002f + i * .002f, .40f + .055f * x / .36f + i * .024f);
                float roll = 0f, turn = 0f;
                if (i == 0)
                {
                    Vector3 landing = Vector3.Lerp(local, Escaped, skid);
                    landing.y += .055f * Mathf.Sin(skid * Mathf.PI);
                    local = Vector3.Lerp(landing, Pile, caught);
                    roll = Mathf.Sin(skid * Mathf.PI) * 52f;
                    turn = skid * 65f - caught * 32f;
                }
                leaves[i].SetPositionAndRotation(origin + groundFrame * local,
                    groundFrame * Quaternion.Euler(0f, 22f + i * 61f + turn, roll));
            }
        }

        internal static Vector3 RecoveryStroke(float time)
        {
            // Meet both neighbouring authored poses at their real bristle positions.
            Vector3 endOfSweep = new(.36f, 0f, .455f);
            if (time < 1.72f)
                return Vector3.Lerp(endOfSweep, WaitingBroom, Ease(time, 1f, 1.65f));
            if (time < 2.62f)
            {
                float reach = Ease(time, 1.72f, 2.62f);
                return Vector3.Lerp(WaitingBroom, Escaped, reach) + Vector3.up * (.06f * Mathf.Sin(reach * Mathf.PI));
            }
            if (time < 3.40f) return Vector3.Lerp(Escaped, Pile, Ease(time, 2.62f, 3.40f));
            float back = Ease(time, 3.40f, 4f);
            return Vector3.Lerp(Pile, new Vector3(-.15f, 0f, .345f), back) +
                Vector3.up * (.04f * Mathf.Sin(back * Mathf.PI));
        }

        private static float Ease(float time, float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, time));
    }
}
