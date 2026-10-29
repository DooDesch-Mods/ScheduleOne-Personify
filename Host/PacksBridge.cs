using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Personify.Editor;
using Personify.Project;

namespace Personify.Host
{
    /// <summary>Installed Personnel packs: the overview (switch off, remove) and the import into a project.
    /// Switched-off packs move to UserData/Personnel/Disabled, which Personnel does not read.</summary>
    internal static class PacksBridge
    {
        internal static JArray List()
        {
            var own = OwnInstalls();
            var list = new JArray();
            AddFrom(list, GameData.PacksRoot, true, own);
            AddFrom(list, GameData.DisabledRoot, false, own);
            return list;
        }

        private static void AddFrom(JArray list, string root, bool enabled, Dictionary<string, string> own)
        {
            if (!Directory.Exists(root)) return;
            foreach (string dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (dir.EndsWith(".installing", StringComparison.OrdinalIgnoreCase)) continue;
                var m = ReadManifest(dir);
                if (m == null) continue;
                string folder = Path.GetFileName(dir);
                var o = new JObject
                {
                    ["folder"] = folder,
                    ["name"] = (string)m["name"] ?? folder,
                    ["version"] = (string)m["version"] ?? "",
                    ["author"] = (string)m["author"] ?? "",
                    ["npcs"] = (m["npcs"] as JArray)?.Count ?? 0,
                    ["enabled"] = enabled,
                };
                if (own.TryGetValue(folder, out string project)) o["ownProject"] = project;
                list.Add(o);
            }
        }

        private static JObject ReadManifest(string dir)
        {
            string f = Path.Combine(dir, "manifest.json");
            if (!File.Exists(f)) return null;
            try { return JObject.Parse(File.ReadAllText(f)); }
            catch (Exception e) { Core.Log.Warning($"[packs] {f} unreadable: {e.Message}"); return null; }
        }

        // Installed folder -> project folder, for "this pack is one of yours".
        private static Dictionary<string, string> OwnInstalls()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in ProjectFiles.List())
            {
                var p = ProjectFiles.Load(s.folder, migrate: false);
                string inst = p == null ? null : (string)p.Editor["installedAs"];
                if (!string.IsNullOrEmpty(inst)) map[inst] = s.folder;
            }
            return map;
        }

        internal static JObject Toggle(string folder, bool enabled)
        {
            if (!SafeName(folder)) return Calls.Fail("bad_folder");
            string from = Path.Combine(enabled ? GameData.DisabledRoot : GameData.PacksRoot, folder);
            string to = Path.Combine(enabled ? GameData.PacksRoot : GameData.DisabledRoot, folder);
            if (!Directory.Exists(from)) return Calls.Fail("not_found");
            if (Directory.Exists(to)) return Calls.Fail("exists");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                Directory.Move(from, to);
                Core.Log.Msg($"[packs] '{folder}' switched {(enabled ? "on" : "off")} ({from} -> {to}). Takes effect on the next save load.");
                return new JObject { ["ok"] = true };
            }
            catch (Exception e)
            {
                Core.Log.Error($"[packs] switching '{folder}' {(enabled ? "on" : "off")} failed: {e.Message}");
                return Calls.Fail("move_failed");
            }
        }

        internal static JObject Remove(string folder)
        {
            if (!SafeName(folder)) return Calls.Fail("bad_folder");
            foreach (string root in new[] { GameData.PacksRoot, GameData.DisabledRoot })
            {
                string dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir)) continue;
                try
                {
                    Directory.Delete(dir, true);
                    Core.Log.Msg($"[packs] '{folder}' removed ({dir}).");
                    return new JObject { ["ok"] = true };
                }
                catch (Exception e)
                {
                    Core.Log.Error($"[packs] removing {dir} failed: {e.Message}");
                    return Calls.Fail("remove_failed");
                }
            }
            Core.Log.Warning($"[packs] remove: '{folder}' is not installed.");
            return Calls.Fail("not_found");
        }

        private static bool SafeName(string folder)
        {
            bool ok = !string.IsNullOrWhiteSpace(folder) && folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && folder != "." && folder != "..";
            if (!ok) Core.Log.Warning($"[packs] refused folder name '{folder}'.");
            return ok;
        }

        /// <summary>Installed packs and zips in UserData/Personify/Import a project can be made from.</summary>
        internal static JArray Importable()
        {
            var list = new JArray();
            foreach (string root in new[] { GameData.PacksRoot, GameData.DisabledRoot })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    if (!PackImport.IsPackFolder(dir)) continue;
                    var m = ReadManifest(dir);
                    list.Add(new JObject { ["path"] = dir, ["name"] = (string)m?["name"] ?? Path.GetFileName(dir), ["kind"] = "installed", ["npcs"] = (m?["npcs"] as JArray)?.Count ?? 0 });
                }
            }
            try
            {
                Directory.CreateDirectory(Paths.Import);
                foreach (string zip in Directory.GetFiles(Paths.Import, "*.zip"))
                    list.Add(new JObject { ["path"] = zip, ["name"] = Path.GetFileNameWithoutExtension(zip), ["kind"] = "zip", ["npcs"] = -1 });
            }
            catch (Exception e) { Core.Log.Error($"[packs] listing {Paths.Import} failed: {e.Message}"); }
            return list;
        }

        /// <summary>Makes projects from an importable path; returns the new project folders.</summary>
        internal static List<string> Import(string path)
        {
            bool known = Importable().OfType<JObject>().Any(o => string.Equals((string)o["path"], path, StringComparison.OrdinalIgnoreCase));
            if (!known) { Core.Log.Warning($"[packs] import refused: '{path}' is not in the import list."); return new List<string>(); }
            string installedAs = Directory.Exists(path) && Path.GetDirectoryName(path) == GameData.PacksRoot ? Path.GetFileName(path) : null;
            var made = PackImport.Import(path, installedAs).Select(p => p.Folder).ToList();
            Core.Log.Msg($"[packs] import of {path}: {made.Count} project(s) {string.Join(", ", made)}.");
            return made;
        }
    }
}
