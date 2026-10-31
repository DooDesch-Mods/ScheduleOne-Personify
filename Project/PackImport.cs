using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;

namespace Personify.Project
{
    /// <summary>
    /// Turns any Personnel pack into a project: an installed pack folder, a pack folder anywhere, or a zip
    /// (Thunderstore/Nexus download). The manifest is taken as it is, so fields the editor does not show stay in
    /// the file and go back out on install. Pictures and other files are copied along.
    /// </summary>
    public static class PackImport
    {
        /// <summary>
        /// Imports the pack in <paramref name="path"/> (folder with manifest.json, or a .zip). For a zip with several
        /// packs every pack becomes a project. Returns the new projects; empty on failure (logged).
        /// </summary>
        public static List<PackProject> Import(string path, string installedAs = null)
        {
            var result = new List<PackProject>();
            try
            {
                if (Directory.Exists(path))
                {
                    var p = FromFolder(path, installedAs);
                    if (p != null) result.Add(p);
                }
                else if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    result.AddRange(FromZip(path));
                else
                    ProjectLog.Error($"[import] {path} is neither a pack folder nor a .zip.");
            }
            catch (Exception e) { ProjectLog.Error($"[import] {path} failed: {e.Message}"); }
            return result;
        }

        /// <summary>True when the folder holds a Personnel manifest (a JSON object with an "npcs" list).</summary>
        public static bool IsPackFolder(string dir)
        {
            string f = Path.Combine(dir, PackProject.ManifestFile);
            if (!File.Exists(f)) return false;
            try { return JObject.Parse(File.ReadAllText(f))["npcs"] is JArray; }
            catch (Exception e) { ProjectLog.Warn($"[import] {f} is not valid JSON: {e.Message}"); return false; }
        }

        private static PackProject FromFolder(string dir, string installedAs)
        {
            if (!IsPackFolder(dir)) { ProjectLog.Error($"[import] {dir} has no Personnel manifest.json with an npcs list."); return null; }
            var m = JObject.Parse(File.ReadAllText(Path.Combine(dir, PackProject.ManifestFile)));
            string name = (string)m["name"];
            if (string.IsNullOrWhiteSpace(name)) { name = Path.GetFileName(dir.TrimEnd('/', '\\')); m["name"] = name; }

            string folder = ProjectFiles.FreeFolder(name);
            string dest = ProjectFiles.ProjectDir(folder);
            CopyTree(dir, dest);

            var p = new PackProject { Folder = folder, Dir = dest, Manifest = m };
            var e = p.Editor;
            e["formatVersion"] = PackProject.FormatVersion;
            if (e["modVersion"] == null) e["modVersion"] = "1.0.0";
            if (e["license"] == null) e["license"] = "All rights reserved";
            e["importedFrom"] = dir;
            if (!string.IsNullOrEmpty(installedAs)) e["installedAs"] = installedAs;
            if (!ProjectFiles.Save(p, snapshot: true, forceSnapshot: true)) return null;
            ProjectLog.Info($"[import] '{name}' ({p.Npcs.Count} NPC(s)) from {dir} -> project '{folder}'.");
            return p;
        }

        private static List<PackProject> FromZip(string zip)
        {
            var result = new List<PackProject>();
            string tmp = Path.Combine(Path.GetTempPath(), "personify-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string root = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
                using (var z = ZipFile.OpenRead(zip))
                {
                    foreach (var entry in z.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(tmp, entry.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            ProjectLog.Warn($"[import] {zip}: entry '{entry.FullName}' points outside the archive - skipped.");
                            continue;
                        }
                        if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }
                }
                foreach (string f in Directory.GetFiles(tmp, PackProject.ManifestFile, SearchOption.AllDirectories))
                {
                    string dir = Path.GetDirectoryName(f);
                    if (!IsPackFolder(dir)) continue;   // the Thunderstore manifest.json at the top has no npcs list
                    var p = FromFolder(dir, null);
                    if (p != null) { p.Editor["importedFrom"] = zip; ProjectFiles.Save(p, snapshot: false); result.Add(p); }
                }
                if (result.Count == 0) ProjectLog.Error($"[import] {zip} contains no Personnel pack.");
            }
            finally
            {
                try { Directory.Delete(tmp, true); }
                catch (Exception e) { ProjectLog.Warn($"[import] temp folder {tmp} not removed: {e.Message}"); }
            }
            return result;
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from))
            {
                if (Path.GetFileName(d) == PackProject.EditorDir) continue;
                CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
            }
        }
    }
}
