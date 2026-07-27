using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class IntoxicationStatusController : MonoBehaviour
    {
        private const float WastedSpeedMultiplier = 0.75f;

        private PlayerMotor motor;
        private PlayerSpriteRig spriteRig;

        public void Initialize(PlayerMotor playerMotor, PlayerSpriteRig visual)
        {
            motor = playerMotor;
            spriteRig = visual;
            ApplyPresentation();
        }

        private void Update()
        {
            GameSessionState.AdvanceWasted(Time.unscaledDeltaTime);
            ApplyPresentation();
        }

        private void OnDisable()
        {
            RestorePresentation();
        }

        private void OnDestroy()
        {
            RestorePresentation();
        }

        private void ApplyPresentation()
        {
            bool wasted = GameSessionState.IsWasted;
            motor?.SetSpeedMultiplier(wasted ? WastedSpeedMultiplier : 1f);
            spriteRig?.SetWasted(wasted);
        }

        private void RestorePresentation()
        {
            motor?.SetSpeedMultiplier(1f);
            spriteRig?.SetWasted(false);
        }
    }
}
