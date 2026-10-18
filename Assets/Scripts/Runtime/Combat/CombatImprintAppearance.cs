using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns only the Imprint enemy's authored appearance on its retained rig.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatImprintAppearance : MonoBehaviour
    {
        private readonly CombatAuthoredBodyAppearance body = new CombatAuthoredBodyAppearance();
        public void Configure(string characterId = null) => body.Configure(gameObject, CombatImprintAssetProvider.Bank,
            characterId ?? DefaultNpcPopulation.CombatTestImprintEnemy, "imprint");
        public void Restore() => body.Restore();
        public static void ValidateBodyBinding(SkinnedMeshRenderer target, SkinnedMeshRenderer source)
            => CombatAuthoredBodyAppearance.ValidateBodyBinding(target, source);
        private void OnEnable() => Restore();
    }
}
