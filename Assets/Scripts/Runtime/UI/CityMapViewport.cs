using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityMapViewport
    {
        private const float SizeEpsilon = 0.01f;

        private Vector2 viewportSize;
        private Vector2 contentSize;
        private Vector2 scrollOffset;
        private bool hasLayout;

        public Vector2 ViewportSize => viewportSize;
        public Vector2 ContentSize => contentSize;
        public Vector2 ScrollOffset => scrollOffset;
        public Vector2 MaximumScrollOffset => new Vector2(
            Mathf.Max(0f, contentSize.x - viewportSize.x),
            Mathf.Max(0f, contentSize.y - viewportSize.y));
        public bool CanScrollHorizontal =>
            MaximumScrollOffset.x > SizeEpsilon;
        public bool CanScrollVertical =>
            MaximumScrollOffset.y > SizeEpsilon;

        public Rect ContentRect
        {
            get
            {
                Vector2 maximumOffset = MaximumScrollOffset;
                float x = maximumOffset.x > SizeEpsilon
                    ? -scrollOffset.x
                    : (viewportSize.x - contentSize.x) * 0.5f;
                float y = maximumOffset.y > SizeEpsilon
                    ? -scrollOffset.y
                    : (viewportSize.y - contentSize.y) * 0.5f;
                return new Rect(x, y, contentSize.x, contentSize.y);
            }
        }

        public void Configure(
            Rect worldBounds,
            Vector2 cellWorldSize,
            Vector2 availableSize,
            float minimumCellPixels)
        {
            Vector2 previousCenter = hasLayout
                ? GetNormalizedVisibleCenter()
                : new Vector2(0.5f, 0.5f);

            viewportSize = new Vector2(
                Mathf.Max(SizeEpsilon, availableSize.x),
                Mathf.Max(SizeEpsilon, availableSize.y));
            float worldWidth = Mathf.Max(SizeEpsilon, worldBounds.width);
            float worldHeight = Mathf.Max(SizeEpsilon, worldBounds.height);
            float largestCellDimension = Mathf.Max(
                SizeEpsilon,
                Mathf.Max(cellWorldSize.x, cellWorldSize.y));
            float readableScale = Mathf.Max(
                SizeEpsilon,
                minimumCellPixels / largestCellDimension);
            float fitScale = Mathf.Min(
                viewportSize.x / worldWidth,
                viewportSize.y / worldHeight);
            float projectionScale = Mathf.Max(
                readableScale,
                fitScale);
            contentSize = new Vector2(
                worldWidth * projectionScale,
                worldHeight * projectionScale);
            hasLayout = true;

            CenterOnNormalized(previousCenter);
        }

        public void CenterOnWorld(
            Vector3 worldPosition,
            Rect worldBounds)
        {
            float normalizedX = Mathf.InverseLerp(
                worldBounds.xMin,
                worldBounds.xMax,
                worldPosition.x);
            float normalizedZ = Mathf.InverseLerp(
                worldBounds.yMin,
                worldBounds.yMax,
                worldPosition.z);
            CenterOnNormalized(
                new Vector2(normalizedX, 1f - normalizedZ));
        }

        public bool ScrollBy(Vector2 delta)
        {
            return SetScrollOffset(scrollOffset + delta);
        }

        public Rect CreateHorizontalThumb(Rect track)
        {
            if (!CanScrollHorizontal)
            {
                return Rect.zero;
            }

            float thumbWidth = Mathf.Clamp(
                track.width * viewportSize.x / contentSize.x,
                12f,
                track.width);
            float progress = scrollOffset.x /
                             MaximumScrollOffset.x;
            return new Rect(
                Mathf.Lerp(
                    track.xMin,
                    track.xMax - thumbWidth,
                    progress),
                track.y,
                thumbWidth,
                track.height);
        }

        public Rect CreateVerticalThumb(Rect track)
        {
            if (!CanScrollVertical)
            {
                return Rect.zero;
            }

            float thumbHeight = Mathf.Clamp(
                track.height * viewportSize.y / contentSize.y,
                12f,
                track.height);
            float progress = scrollOffset.y /
                             MaximumScrollOffset.y;
            return new Rect(
                track.x,
                Mathf.Lerp(
                    track.yMin,
                    track.yMax - thumbHeight,
                    progress),
                track.width,
                thumbHeight);
        }

        internal void CenterOnNormalized(Vector2 normalizedPosition)
        {
            Vector2 clampedPosition = new Vector2(
                Mathf.Clamp01(normalizedPosition.x),
                Mathf.Clamp01(normalizedPosition.y));
            SetScrollOffset(
                new Vector2(
                    clampedPosition.x * contentSize.x -
                    viewportSize.x * 0.5f,
                    clampedPosition.y * contentSize.y -
                    viewportSize.y * 0.5f));
        }

        private Vector2 GetNormalizedVisibleCenter()
        {
            Vector2 maximumOffset = MaximumScrollOffset;
            return new Vector2(
                contentSize.x <= SizeEpsilon ||
                maximumOffset.x <= SizeEpsilon
                    ? 0.5f
                    : (scrollOffset.x + viewportSize.x * 0.5f) /
                      contentSize.x,
                contentSize.y <= SizeEpsilon ||
                maximumOffset.y <= SizeEpsilon
                    ? 0.5f
                    : (scrollOffset.y + viewportSize.y * 0.5f) /
                      contentSize.y);
        }

        private bool SetScrollOffset(Vector2 requestedOffset)
        {
            Vector2 maximumOffset = MaximumScrollOffset;
            Vector2 clampedOffset = new Vector2(
                Mathf.Clamp(requestedOffset.x, 0f, maximumOffset.x),
                Mathf.Clamp(requestedOffset.y, 0f, maximumOffset.y));
            if ((clampedOffset - scrollOffset).sqrMagnitude <=
                SizeEpsilon * SizeEpsilon)
            {
                return false;
            }

            scrollOffset = clampedOffset;
            return true;
        }
    }
}
