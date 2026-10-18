using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns Long Exposure's opaque authored surface on its retained rig.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatLongExposureAppearance : MonoBehaviour
    {
        private readonly CombatAuthoredBodyAppearance body = new CombatAuthoredBodyAppearance();
        public void Configure(string characterId = null) => body.Configure(gameObject, CombatLongExposureAssetProvider.Bank,
            characterId ?? DefaultNpcPopulation.CombatTestLongExposureEnemy, "long-exposure");
        public void Restore() => body.Restore();
        private void OnEnable() => Restore();
    }
}
