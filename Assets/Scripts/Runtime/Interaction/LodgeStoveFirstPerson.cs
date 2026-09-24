using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Camera-local prop only; the stove deliberately renders no first-person hands.</summary>
    internal sealed class LodgeStoveFirstPerson : IDisposable
    {
        private readonly Camera camera;
        private readonly Transform lighter;
        private readonly Transform wheel;
        private readonly Transform lid;
        private readonly Renderer flame;
        private readonly MaterialPropertyBlock flameProperties = new MaterialPropertyBlock();
        private Vector3 withdrawLighter;
        private Quaternion withdrawLidRotation;
        public Transform Lighter => lighter;

        public LodgeStoveFirstPerson(Camera camera)
        {
            this.camera = camera;
            try
            {
                lighter = VillageExpansionAssetProvider.LoadOrThrow().Create("Lighter", "Stove Lighter",
                    camera.transform, Vector3.zero, Quaternion.identity).transform;
                wheel = LodgeStovePlan.Require(lighter, "FlintWheel");
                lid = LodgeStovePlan.Require(lighter, "LighterLidHinge");
                flame = LodgeStovePlan.Require(lighter, "LighterFlame").GetComponent<Renderer>();
                Hide();
            }
            catch { Dispose(); throw; }
        }

        public Vector3 RestPosition => camera.transform.TransformPoint(new Vector3(.30f, -.42f, .5f));

        public void ApplyLighter(Vector3 target, Quaternion rotation, float reach,
            float clickPulse, bool lit, float elapsed)
        {
            lighter.gameObject.SetActive(true);
            lighter.SetPositionAndRotation(Vector3.Lerp(RestPosition, target, Smooth(reach)), rotation);
            lighter.position += rotation * Vector3.down * (clickPulse * .006f);
            wheel.localRotation = Quaternion.Euler(0f, 0f, -clickPulse * 100f);
            float lidOpen = Mathf.Min(Smooth((elapsed - .15f) / .35f), 1f - Smooth((elapsed - 3.12f) / .35f));
            lid.localRotation = Quaternion.Euler(0f, 0f, lidOpen * 110f);
            flame.enabled = lit;
            if (lit)
            {
                flame.GetPropertyBlock(flameProperties);
                flameProperties.SetFloat("_FireTime", elapsed);
                flameProperties.SetFloat("_FireStrength", 1f);
                flame.SetPropertyBlock(flameProperties);
            }
        }

        public void Hide()
        {
            if (lighter != null) lighter.gameObject.SetActive(false);
        }

        public void BeginWithdraw()
        {
            withdrawLighter = lighter.position;
            withdrawLidRotation = lid.localRotation;
            flame.enabled = false;
        }

        public void Withdraw(float amount)
        {
            float t = Smooth(amount);
            if (!lighter.gameObject.activeSelf) return;
            lighter.position = Vector3.Lerp(withdrawLighter, RestPosition, t);
            lid.localRotation = Quaternion.Slerp(withdrawLidRotation, Quaternion.identity, t);
        }

        public void Dispose()
        {
            Hide();
            if (lighter != null) UnityEngine.Object.Destroy(lighter.gameObject);
        }

        private static float Smooth(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));
    }
}
