using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Personify.Editor
{
    /// <summary>
    /// One selectable appearance path for an option-picker modal: which catalog group it belongs to (used as a
    /// section header + search term), a friendly display name, and the full asset path actually stored on the draft.
    /// </summary>
    public readonly struct PathOption
    {
        public readonly string Group;
        public readonly string Display;
        public readonly string Path;
        public PathOption(string group, string display, string path) { Group = group; Display = display; Path = path; }
    }

    /// <summary>
    /// The REAL available appearance option paths, mirrored from the same S1API "field" classes vanilla's own
    /// <c>NPCAppearance.GenerateRandomAppearance</c> reflects over:
    /// <c>BaseAppearance.GetConstPaths&lt;HairStyle&gt;()</c>, <c>BaseFaceAppearance.GetConstPaths&lt;Eyes|Face|FacialHair&gt;()</c>,
    /// <c>BaseBodyAppearance.GetConstPaths&lt;Shirts|Pants&gt;()</c> and
    /// <c>BaseAccessoryAppearance.GetConstPaths&lt;Bottom|Chest|Feet|Hands|Head|Neck|Waist&gt;()</c>.
    /// <para>
    /// Those <c>GetConstPaths&lt;T&gt;()</c> helpers are <c>internal</c> to S1API, so Personify cannot call them
    /// directly across the assembly boundary. Each "field" class (e.g. <see cref="S1API.Entities.Appearances.CustomizationFields.HairStyle"/>)
    /// is nothing more than a bag of <c>public const string</c> asset paths, so instead we reflect over the exact
    /// same public const fields ourselves - purely a metadata read off the S1API assembly's type definitions. That
    /// means these lists need only S1API to be loaded (S1API is a hard dependency) - NOT a live
    /// game/save/menu. If a future S1API version renames or removes one of these classes the corresponding list
    /// silently comes back empty (see <see cref="ConstPaths"/>) rather than throwing.
    /// </para>
    /// </summary>
    public static class PathCatalog
    {
        private static List<string> ConstPaths(Type t)
        {
            try
            {
                return t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                    .Select(f => (string)f.GetRawConstantValue())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            catch (Exception e) { Core.Log?.Warning("[catalog] reflect " + t.Name + " failed: " + e.Message); return new List<string>(); }
        }

        private static IEnumerable<PathOption> Group(string label, Type t) =>
            ConstPaths(t).Select(p => new PathOption(label, DisplayName(p), p));

        /// <summary>Friendly name for a picker row: the last "/"-segment of the asset path, underscores as spaces
        /// (e.g. "Avatar/Layers/Face/Face_Agape" -&gt; "Face Agape"). The full path is still what gets stored.</summary>
        public static string DisplayName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(None)";
            int i = path.LastIndexOf('/');
            string seg = i >= 0 && i < path.Length - 1 ? path.Substring(i + 1) : path;
            return seg.Replace('_', ' ');
        }

        /// <summary>The subset of a catalog list belonging to one group/category (e.g. "Shirts", "Feet", "Eyes") -
        /// used by the Basic view's single-select slots.</summary>
        public static List<PathOption> InGroup(List<PathOption> all, string group) =>
            (all ?? new List<PathOption>()).Where(o => string.Equals(o.Group, group, StringComparison.OrdinalIgnoreCase)).ToList();

        private static List<PathOption> _hair, _face, _body, _accessories;

        /// <summary>All hair styles (HairPath). Source: CustomizationFields.HairStyle.</summary>
        public static List<PathOption> HairStyles() => _hair ??=
            Group("Hair", typeof(S1API.Entities.Appearances.CustomizationFields.HairStyle)).ToList();

        /// <summary>All face-layer options. Sources: FaceLayerFields.{Eyes,Face,FacialHair,FaceTattoos}.</summary>
        public static List<PathOption> FaceLayers() => _face ??= new[]
        {
            Group("Eyes", typeof(S1API.Entities.Appearances.FaceLayerFields.Eyes)),
            Group("Face", typeof(S1API.Entities.Appearances.FaceLayerFields.Face)),
            Group("Facial hair", typeof(S1API.Entities.Appearances.FaceLayerFields.FacialHair)),
            Group("Face tattoos", typeof(S1API.Entities.Appearances.FaceLayerFields.FaceTattoos)),
        }.SelectMany(g => g).ToList();

        /// <summary>All body-layer options. Sources: BodyLayerFields.{Shirts,Pants,ChestTattoos,LeftArmTattoos,
        /// RightArmTattoos,Accessories(gloves)}.</summary>
        public static List<PathOption> BodyLayers() => _body ??= new[]
        {
            Group("Shirts", typeof(S1API.Entities.Appearances.BodyLayerFields.Shirts)),
            Group("Pants", typeof(S1API.Entities.Appearances.BodyLayerFields.Pants)),
            Group("Chest tattoos", typeof(S1API.Entities.Appearances.BodyLayerFields.ChestTattoos)),
            Group("Left arm tattoos", typeof(S1API.Entities.Appearances.BodyLayerFields.LeftArmTattoos)),
            Group("Right arm tattoos", typeof(S1API.Entities.Appearances.BodyLayerFields.RightArmTattoos)),
            Group("Gloves", typeof(S1API.Entities.Appearances.BodyLayerFields.Accessories)),
        }.SelectMany(g => g).ToList();

        /// <summary>All accessory options across every slot, grouped by slot (Head/Chest/Hands/Feet/Neck/Waist/
        /// Bottom/Facial hair) for the option-picker's section headers. Sources: AccessoryFields.*.</summary>
        public static List<PathOption> Accessories() => _accessories ??= new[]
        {
            Group("Head", typeof(S1API.Entities.Appearances.AccessoryFields.Head)),
            Group("Chest", typeof(S1API.Entities.Appearances.AccessoryFields.Chest)),
            Group("Hands", typeof(S1API.Entities.Appearances.AccessoryFields.Hands)),
            Group("Feet", typeof(S1API.Entities.Appearances.AccessoryFields.Feet)),
            Group("Neck", typeof(S1API.Entities.Appearances.AccessoryFields.Neck)),
            Group("Waist", typeof(S1API.Entities.Appearances.AccessoryFields.Waist)),
            Group("Bottom", typeof(S1API.Entities.Appearances.AccessoryFields.Bottom)),
            Group("Facial hair", typeof(S1API.Entities.Appearances.AccessoryFields.FacialHairAccessory)),
        }.SelectMany(g => g).ToList();

        // --- Character (Basic) mode slot lists ------------------------------------------------------------------
        // The S1API "field" classes lump non-garment BASE layers (censor nipples, underwear, body hair) and TATTOOS
        // in with real clothing, and conflate hats + glasses into one "Head" class. These accessors reproduce the
        // vanilla character creator's actual per-slot option lists (BasicAvatarSettings) by filtering those out.
        // Excluding a base layer here ALSO protects it: the Basic slot never removes it when you change garments.

        private static readonly string[] EyewearPaths =
        {
            "Avatar/Accessories/Head/LegendSunglasses/LegendSunglasses",
            "Avatar/Accessories/Head/Oakleys/Oakleys",
            "Avatar/Accessories/Head/RectangleFrameGlasses/RectangleFrameGlasses",
            "Avatar/Accessories/Head/SmallRoundGlasses/SmallRoundGlasses",
        };

        /// <summary>Tops (Shirts) minus the censor nipple / body-hair / tattoo layers that aren't garments.</summary>
        public static List<PathOption> Tops() => Without(InGroup(BodyLayers(), "Shirts"),
            "Avatar/Layers/Top/Nipples", "Avatar/Layers/Top/ChestHair1", "Avatar/Layers/Top/UpperBodyTattoos");

        /// <summary>Bottoms (Pants) minus the underwear base layers.</summary>
        public static List<PathOption> Bottoms() => Without(InGroup(BodyLayers(), "Pants"),
            "Avatar/Layers/Bottom/MaleUnderwear", "Avatar/Layers/Bottom/FemaleUnderwear");

        /// <summary>Mouth expressions (Face) minus the stray face-tattoo entry.</summary>
        public static List<PathOption> Mouths() => Without(InGroup(FaceLayers(), "Face"),
            "Avatar/Layers/Face/FaceTattoos1");

        public static List<PathOption> FacialHairStyles() => InGroup(FaceLayers(), "Facial hair");
        public static List<PathOption> FacialDetails() => InGroup(FaceLayers(), "Eyes");
        public static List<PathOption> Shoes() => InGroup(Accessories(), "Feet");

        /// <summary>Glasses only - split out of the game's single "Head" accessory class into the vanilla Eyewear slot.</summary>
        public static List<PathOption> Eyewear() => Only(InGroup(Accessories(), "Head"), EyewearPaths);
        /// <summary>Hats/headgear = the Head accessory class minus the glasses (those are the Eyewear slot).</summary>
        public static List<PathOption> Headwear() => Without(InGroup(Accessories(), "Head"), EyewearPaths);

        /// <summary>All tattoos (face + chest + both arms), including the two reclaimed from the shirt/face classes.</summary>
        public static List<PathOption> Tattoos()
        {
            var list = new List<PathOption>();
            list.AddRange(Group("Face", typeof(S1API.Entities.Appearances.FaceLayerFields.FaceTattoos)));
            list.Add(new PathOption("Face", DisplayName("Avatar/Layers/Face/FaceTattoos1"), "Avatar/Layers/Face/FaceTattoos1"));
            list.AddRange(Group("Chest", typeof(S1API.Entities.Appearances.BodyLayerFields.ChestTattoos)));
            list.Add(new PathOption("Chest", DisplayName("Avatar/Layers/Top/UpperBodyTattoos"), "Avatar/Layers/Top/UpperBodyTattoos"));
            list.AddRange(Group("Left arm", typeof(S1API.Entities.Appearances.BodyLayerFields.LeftArmTattoos)));
            list.AddRange(Group("Right arm", typeof(S1API.Entities.Appearances.BodyLayerFields.RightArmTattoos)));
            return list;
        }

        private static List<PathOption> Without(List<PathOption> src, params string[] exclude)
        {
            var set = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);
            return src.Where(o => !set.Contains(o.Path)).ToList();
        }

        private static List<PathOption> Only(List<PathOption> src, params string[] include)
        {
            var set = new HashSet<string>(include, StringComparer.OrdinalIgnoreCase);
            return src.Where(o => set.Contains(o.Path)).ToList();
        }
    }
}
