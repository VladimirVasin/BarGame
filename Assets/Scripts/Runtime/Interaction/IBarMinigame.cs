using System;

namespace BarPromenade
{
    public interface IBarMinigame
    {
        bool IsOpen { get; }
        event Action Completed;
        bool Open(PlayerInteractor interactor);
        void Cancel();
    }
}
