using UnityEngine;

namespace BarPromenade
{
    /// <summary>Selection and actions for the common shop panels around a real world target.</summary>
    public interface IWorldShelfMenu
    {
        bool IsVisible { get; }
        bool CanNavigate { get; }
        int CashBalance { get; }
        string TitleKey { get; }
        string NameKey { get; }
        string DescriptionKey { get; }
        string ConfirmKey { get; }
        string FeedbackKey { get; }
        int? Price { get; }
        bool TryGetSelectionBounds(out Bounds bounds);
        bool MoveSelection(int direction);
        bool ConfirmSelection();
        void Exit();
    }
}
