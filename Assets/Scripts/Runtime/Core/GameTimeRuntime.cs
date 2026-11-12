using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(-1000)]
    public sealed class GameTimeRuntime : MonoBehaviour
    {
        private static GameTimeRuntime instance;
        private static int ignoreLoadDeltaThroughFrame = -1;

        public GameDayAnnouncementView DayAnnouncement { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            ignoreLoadDeltaThroughFrame = -1;
        }

        internal static void ExcludeSynchronousLoadFromCalendar()
        {
            // The following frame's unscaled delta includes the wall time spent loading.
            ignoreLoadDeltaThroughFrame = Time.frameCount + 1;
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
            if (Time.frameCount <= ignoreLoadDeltaThroughFrame) return;
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
