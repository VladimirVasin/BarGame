using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The renderer reads this facade; one persistent driver owns the clock.</summary>
    public static class AlpineColdExposure
    {
        private static AlpineColdExposureDriver instance;
        public static float FrostAmount => instance != null ? instance.Model.FrostAmount : 0f;
        public static bool IsVisible(Camera camera) => instance != null && instance.IsVisible(camera);

        public static void Bind(MonoBehaviour owner, Camera camera, Func<bool> ready,
            Func<bool> sheltered)
        {
            if (owner == null || camera == null || ready == null || sheltered == null)
                throw new ArgumentNullException(nameof(owner));
            if (instance == null)
            {
                var go = new GameObject("Alpine Cold Exposure");
                UnityEngine.Object.DontDestroyOnLoad(go);
                instance = go.AddComponent<AlpineColdExposureDriver>();
            }
            instance.Bind(owner, camera, ready, sheltered);
        }

        public static void ResetSession()
        {
            if (instance != null) instance.ResetSession();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;
    }
}
