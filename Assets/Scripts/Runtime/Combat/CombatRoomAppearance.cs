using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns only the Room enemy's authored appearance on its retained rig.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatRoomAppearance : MonoBehaviour
    {
        private readonly CombatAuthoredBodyAppearance body = new CombatAuthoredBodyAppearance();
        public void Configure(string characterId = null) => body.Configure(gameObject, CombatRoomAssetProvider.Bank,
            characterId ?? DefaultNpcPopulation.CombatTestRoomEnemy, "room");
        public void Restore() => body.Restore();
        public static void ValidateBodyBinding(SkinnedMeshRenderer target, SkinnedMeshRenderer source)
            => CombatAuthoredBodyAppearance.ValidateBodyBinding(target, source);
        private void OnEnable() => Restore();
    }
}
