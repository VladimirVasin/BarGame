using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(-1000)]
    public sealed class GameTimeRuntime : MonoBehaviour
    {
        private static GameTimeRuntime instance;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            GameSessionState.AdvanceGameTime(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
