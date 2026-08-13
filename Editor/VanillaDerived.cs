using System;
using UnityEngine;

namespace Personify.Editor
{
    /// <summary>
    /// The parts of an avatar the vanilla character creator never lets you set by hand because it computes them
    /// from something else. AvatarSettings stores them as plain independent fields, so an editor that writes only
    /// the field the user touched leaves the rest behind on the previous character: pale eyelids on dark skin,
    /// a chest tinted for the skin colour two edits ago, men's underwear on a female body.
    /// <para>
    /// Each rule below mirrors a specific piece of vanilla (<c>CustomizationManager.SkinColorChanged</c>,
    /// <c>BasicAvatarSettings.GetAvatarSettings</c>). Applied on the edit that causes them, not on load - a project
    /// whose JSON was hand-tuned keeps what it says until someone moves the control it depends on.
    /// </para>
    /// </summary>
    internal static class VanillaDerived
    {
        internal const string NipplesPath = "Avatar/Layers/Top/Nipples";
        internal const string MaleUnderwearPath = "Avatar/Layers/Bottom/MaleUnderwear";
        internal const string FemaleUnderwearPath = "Avatar/Layers/Bottom/FemaleUnderwear";

        /// <summary>
        /// Set the skin colour and everything vanilla ties to it: both eyelids take the skin colour outright
        /// (CustomizationManager.SkinColorChanged), and the censor layer takes the slightly desaturated variant
        /// BasicAvatarSettings.GetNippleColor computes. The Advanced tab can release the eyelids from the skin
        /// (<see cref="AppearanceDraft.EyelidFollowsSkin"/>), which is the same escape hatch vanilla's creator
        /// offers behind a held Ctrl key.
        /// </summary>
        internal static void SetSkinColor(AppearanceDraft a, string hex)
        {
            if (a == null) return;
            a.SkinColor = hex;
            if (a.EyelidFollowsSkin)
            {
                a.LeftEyeLidColor = hex;
                a.RightEyeLidColor = hex;
            }

            string nipple = Preview.HexOf(NippleColor(Preview.Hex(hex, new Color32(150, 120, 95, 255))));
            foreach (LayerDraft l in a.BodyLayers ?? new System.Collections.Generic.List<LayerDraft>())
                if (l != null && Same(l.Path, NipplesPath)) l.Tint = nipple;
        }

        /// <summary>
        /// Set the gender and swap the underwear base layer with it. The Character tab hides both underwear paths
        /// from the Bottom slot so a garment change cannot delete them, which also means nothing else would ever
        /// correct them.
        /// </summary>
        internal static void SetGender(AppearanceDraft a, float gender)
        {
            if (a == null) return;
            a.Gender = gender;

            string want = gender > 0.5f ? FemaleUnderwearPath : MaleUnderwearPath;
            foreach (LayerDraft l in a.BodyLayers ?? new System.Collections.Generic.List<LayerDraft>())
                if (l != null && (Same(l.Path, MaleUnderwearPath) || Same(l.Path, FemaleUnderwearPath)))
                    l.Path = want;
        }

        /// <summary>
        /// Re-derive the skin-driven tints after a layer was added: a censor layer picked from the Advanced tab's
        /// full catalog arrives with the catalog's default tint, not with this character's skin. The underwear rule
        /// is deliberately NOT re-applied here - picking either underwear layer by hand is a legitimate thing to do
        /// in the Advanced tab, and only the gender control should override it.
        /// </summary>
        internal static void ApplyDerivedTints(AppearanceDraft a)
        {
            if (a == null) return;
            SetSkinColor(a, a.SkinColor);
        }

        /// <summary>BasicAvatarSettings.GetNippleColor: the skin colour pulled a fifth of the way towards mid grey.</summary>
        private static Color NippleColor(Color skin) => Color.Lerp(skin, new Color(0.5f, 0.5f, 0.5f, 1f), 0.2f);

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
