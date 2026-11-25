using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public static class CityBalconySmokerArchetypeCatalog
    {
        public static IReadOnlyList<string> EligibleDesignIds => DefaultNpcCatalog.ModelIds;

        public static bool IsEligible(string modelId) =>
            modelId == DefaultNpcCatalog.OrdinaryWorker || modelId == DefaultNpcCatalog.OrdinaryWoman;

        internal static bool UsesCharacter(IEnumerable<CityBalconySmokerDescriptor> selected, string characterId)
        {
            foreach (var resident in selected)
                if (resident.CharacterId == characterId) return true;
            return false;
        }
    }
}
