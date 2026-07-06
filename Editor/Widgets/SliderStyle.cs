using UnityEngine;
using UnityEngine.UI;

namespace Personify.Editor.Widgets
{
    /// <summary>
    /// Personify-local slider styling. Unity's <see cref="Slider"/> re-forces its handle to stretch to the full
    /// track height on every UpdateVisuals, so a slider that fills a 30px form row renders a tall "pill" handle. The
    /// shared DooDesch.UI <c>Components.Slider</c> is fine when given a small fixed height. <see cref="Compact"/> pins an 18px height via a LayoutElement so the
    /// slider sits at a slim size inside the layout-group rows (the row's HorizontalLayoutGroup
    /// must have childForceExpandHeight = false for this height to be honoured). Kept local so the shared component
    /// stays untouched for the other mods.
    /// </summary>
    internal static class SliderStyle
    {
        public static Slider Compact(Slider s, float trackHeight = 18f)
        {
            if (s == null) return s;
            var le = s.GetComponent<LayoutElement>();
            if (le == null) le = s.gameObject.AddComponent<LayoutElement>();
            le.minHeight = trackHeight;
            le.preferredHeight = trackHeight;
            le.flexibleHeight = 0f;
            if (le.flexibleWidth <= 0f) le.flexibleWidth = 1f;   // fill the remaining row width unless a caller overrode it
            return s;
        }
    }
}
