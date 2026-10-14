using UnityEngine;

namespace BarPromenade
{
    /// <summary>Metric lamp attachment measured on the imported production chest.</summary>
    public sealed class CombatFlashlightBindings : ScriptableObject
    {
        public Vector3 BoneLocalPosition;
        public Quaternion BoneLocalRotation = Quaternion.identity;
        public string SourceSignature;
    }
}
