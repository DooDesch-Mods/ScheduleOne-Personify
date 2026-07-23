using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Personify.Editor
{
    /// <summary>One tattoo offered by an installed Inkorporated pack.</summary>
    public sealed class InkTattoo
    {
        public string Name;
        public string PngPath;   // absolute path to the tattoo PNG
        public bool IsFace;      // Inkorporated routes "face" placement to the face mesh, everything else to the body
        public string Pack;      // owning pack (for the picker's section header)
    }

    /// <summary>
    /// Reads the tattoo packs shipped by the Inkorporated mod (<c>UserData/Inkorporated/Packs/&lt;pack&gt;/manifest.json</c>)
    /// so Personify can offer them in the Tattoos picker when Inkorporated is present. Purely file-based (Personify
    /// never references Inkorporated); picking a tattoo copies its PNG into the NPC pack as a custom layer, so the
    /// exported pack is self-contained and needs no Inkorporated at play time.
    /// </summary>
    public static class InkorporatedCatalog
    {
        private static List<InkTattoo> _cache;

        public static string PacksRoot => Path.Combine(MelonEnvironment.UserDataDirectory, "Inkorporated", "Packs");

        /// <summary>Drops the cached pack list so the next <see cref="All"/> re-scans the manifests on disk.
        /// Called when the editor opens and when a layer picker builds its options, so packs installed
        /// mid-session show up.</summary>
        public static void Refresh() => _cache = null;

        public static List<InkTattoo> All()
        {
            if (_cache != null) return _cache;
            var list = new List<InkTattoo>();
            try
            {
                string root = PacksRoot;
                if (Directory.Exists(root))
                {
                    foreach (string packDir in Directory.GetDirectories(root))
                    {
                        string manifest = Path.Combine(packDir, "manifest.json");
                        if (!File.Exists(manifest)) continue;
                        string folder = new DirectoryInfo(packDir).Name;

                        InkManifest m = null;
                        try { m = JsonConvert.DeserializeObject<InkManifest>(File.ReadAllText(manifest)); }
                        catch (Exception e) { Core.Log?.Warning($"[ink] pack '{folder}' manifest: {e.Message}"); }
                        if (m?.tattoos == null) continue;

                        string packName = string.IsNullOrWhiteSpace(m.name) ? folder : m.name;
                        foreach (InkEntry e in m.tattoos)
                        {
                            if (e == null || string.IsNullOrWhiteSpace(e.id)) continue;
                            string file = string.IsNullOrWhiteSpace(e.file) ? (e.id + ".png") : e.file;
                            string png = Path.Combine(packDir, file);
                            if (!File.Exists(png)) continue;
                            list.Add(new InkTattoo
                            {
                                Name = string.IsNullOrWhiteSpace(e.name) ? e.id : e.name,
                                PngPath = png,
                                IsFace = string.Equals((e.placement ?? "").Trim(), "face", StringComparison.OrdinalIgnoreCase),
                                Pack = packName
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { Core.Log?.Warning("[ink] catalog read failed: " + ex.Message); }
            _cache = list;
            return list;
        }

        /// <summary>Look up a tattoo by its PNG path (the value the option-picker returns).</summary>
        public static InkTattoo ByPng(string png)
        {
            foreach (InkTattoo t in All())
                if (string.Equals(t.PngPath, png, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        // Deserialization shape mirroring Inkorporated's own pack manifest (lowercase fields). Assigned by Newtonsoft
        // via reflection, hence the CS0649 suppression.
#pragma warning disable CS0649
        private sealed class InkManifest { public string name; public string author; public List<InkEntry> tattoos; }
        private sealed class InkEntry { public string id; public string name; public string placement; public string file; public float price; }
#pragma warning restore CS0649
    }
}
