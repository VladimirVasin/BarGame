using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Compatibility adapter retaining the mountain-cafe API while the
    /// actual hint implementation is shared by every physical counter menu.
    /// </summary>
    public sealed class MountainRoadCafeMenuHintView : CounterMenuHintView
    {
        public const string SelectHintKey =
            "mountain.cafe.menu.select_hint";
        public const string OrderHintKey =
            "mountain.cafe.menu.order_hint";
        public new const float Width = CounterMenuHintView.Width;
        public new const float Height = CounterMenuHintView.Height;
        public new const float BottomMargin =
            CounterMenuHintView.BottomMargin;

        public static MountainRoadCafeMenuHintView Create(Transform parent)
        {
            var host = new GameObject("Mountain Cafe Menu Hint");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            MountainRoadCafeMenuHintView view =
                host.AddComponent<MountainRoadCafeMenuHintView>();
            view.Configure(SelectHintKey, OrderHintKey);
            return view;
        }
    }
}
