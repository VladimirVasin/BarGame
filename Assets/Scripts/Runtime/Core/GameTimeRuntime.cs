using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(-1000)]
    public sealed class GameTimeRuntime : MonoBehaviour
    {
        private static GameTimeRuntime instance;

        public GameDayAnnouncementView DayAnnouncement { get; private set; }

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
            GameTimeScaleRuntime.EnsureInstalled();
            DayAnnouncement = GetComponent<GameDayAnnouncementView>();
            if (DayAnnouncement == null)
            {
                DayAnnouncement = gameObject.AddComponent<
                    GameDayAnnouncementView>();
            }
        }

        private void Update()
        {
            GameSessionState.AdvanceGameTime(
                GameTimeScaleRuntime.CalendarDeltaTime);
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
