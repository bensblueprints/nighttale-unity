using UnityEngine;

namespace NightTale
{
    public static class RectTransformExtensions
    {
        /// <summary>Convenience: set anchors + offsets in one call (offsets in pixels).</summary>
        public static void SetAnchor(this RectTransform rt,
            float minX, float minY, float maxX, float maxY,
            float offMinX, float offMinY, float offMaxX, float offMaxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(offMinX, offMinY);
            rt.offsetMax = new Vector2(offMaxX, offMaxY);
        }
    }
}
