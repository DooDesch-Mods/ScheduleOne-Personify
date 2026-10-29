using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Personify.Project
{
    /// <summary>Row of the start screen.</summary>
    public sealed class ProjectSummary
    {
        public string folder;
        public string name;
        public int npcs;
        /// <summary>1 = old project not yet migrated, 2 = current.</summary>
        public int formatVersion;
        /// <summary>"installed", "changed" (installed, edited since) or "not_installed".</summary>
        public string install;
    }

    /// <summary>One saved state in a project's history.</summary>
    public sealed class HistoryEntry
    {
        public string id;
        public DateTime savedUtc;
        public int npcs;
    }

    /// <summary>
    /// Projects on disk: list, create, load (with migration from format 1), save with history, delete. Every
    /// method takes or derives absolute paths from <see cref="Root"/>, so the class runs outside the game.
    /// </summary>
    public static class ProjectFiles
    {
        /// <summary>Folder that holds one sub-folder per project (UserData/Personify/Projects in game).</summary>
        public static string Root { get; set; }

        /// <summary>Maximum number of history entries kept per project; older ones are deleted.</summary>
        public const int HistoryLimit = 50;

        /// <summary>A save within this many seconds of the newest history entry does not add another one.</summary>
        public const double HistorySpacingSeconds = 60;

        private static readonly JsonSerializerSettings Indented = new JsonSerializerSettings { Formatting = Formatting.Indented };

        public static string ProjectDir(string folder) => Path.Combine(Root, folder);
        private static string HistoryDir(string dir) => Path.Combine(dir, PackProject.EditorDir, "history");

        public static List<ProjectSummary> List(Func<PackProject, string> installState = null)
        {
            var result = new List<ProjectSummary>();
            try
            {
                Directory.CreateDirectory(Root);
                foreach (string dir in Directory.GetDirectories(Root))
                {
                    string folder = Path.GetFileName(dir);
                    if (File.Exists(Path.Combine(dir, PackProject.ManifestFile)))
                    {
                        var p = Load(folder, migrate: false);
                        if (p == null) continue;
                        result.Add(new ProjectSummary
                        {
                            folder = folder, name = p.Name, npcs = p.Npcs.Count, formatVersion = 2,
                            install = installState?.Invoke(p) ?? "not_installed",
                        });
                    }
                    else if (File.Exists(Path.Combine(dir, Migration.V1File)))
                    {
                        var v1 = Migration.ReadV1(Path.Combine(dir, Migration.V1File));
                        result.Add(new ProjectSummary
                        {
                            folder = folder, name = v1?.Name ?? folder, npcs = v1?.Npcs?.Count ?? 0, formatVersion = 1,
                            install = "not_installed",
                        });
                    }
                }
            }
            catch (Exception e) { ProjectLog.Error("[project] listing " + Root + " failed: " + e.Message); }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>Folder-safe name (letters, digits, -, _, space).</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Untitled";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ' ? c : '_');
            string outp = sb.ToString().Trim();
            return outp.Length == 0 ? "Untitled" : outp;
        }

        /// <summary>A folder name under <see cref="Root"/> that is not taken yet, based on <paramref name="name"/>.</summary>
        public static string FreeFolder(string name)
        {
            string baseName = Sanitize(name), folder = baseName;
            for (int n = 2; Directory.Exists(ProjectDir(folder)); n++) folder = baseName + " " + n;
            return folder;
        }

        public static PackProject Create(string name, string author)
        {
            string folder = FreeFolder(name);
            var p = new PackProject { Folder = folder, Dir = ProjectDir(folder), Manifest = PackProject.NewManifest(name, author) };
            Directory.CreateDirectory(p.Dir);
            if (!Save(p, snapshot: false)) return null;
            ProjectLog.Info($"[project] created '{name}' in {p.Dir}");
            return p;
        }

        /// <summary>
        /// Loads a project. A format 1 project (project.json only) is migrated first when <paramref name="migrate"/>
        /// is set; without it, or when migration fails, the result is null.
        /// </summary>
        public static PackProject Load(string folder, bool migrate = true)
        {
            string dir = ProjectDir(folder);
            string file = Path.Combine(dir, PackProject.ManifestFile);
            try
            {
                if (!File.Exists(file))
                {
                    if (migrate && File.Exists(Path.Combine(dir, Migration.V1File)))
                        return Migration.Migrate(dir, folder);
                    ProjectLog.Warn($"[project] '{folder}' has no {PackProject.ManifestFile}.");
                    return null;
                }
                var m = JObject.Parse(File.ReadAllText(file));
                var p = new PackProject { Folder = folder, Dir = dir, Manifest = m };
                if (!(m["npcs"] is JArray)) m["npcs"] = new JArray();
                var e = p.Editor;
                if (e["formatVersion"] == null) e["formatVersion"] = PackProject.FormatVersion;
                return p;
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[project] loading '{folder}' failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes the manifest (temp file, then replace, so a crash mid-write leaves the old file). With
        /// <paramref name="snapshot"/> the state also goes into the history, at most one entry per
        /// <see cref="HistorySpacingSeconds"/> unless <paramref name="forceSnapshot"/>.
        /// </summary>
        public static bool Save(PackProject p, bool snapshot = true, bool forceSnapshot = false)
        {
            if (p?.Manifest == null) { ProjectLog.Error("[project] save called without a project."); return false; }
            try
            {
                Directory.CreateDirectory(p.Dir);
                string json = p.Manifest.ToString(Formatting.Indented);
                WriteAtomic(Path.Combine(p.Dir, PackProject.ManifestFile), json);
                if (snapshot) Snapshot(p.Dir, json, forceSnapshot);
                return true;
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[project] saving '{p.Folder}' failed: {e.Message}");
                return false;
            }
        }

        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private static void Snapshot(string dir, string json, bool force)
        {
            string hd = HistoryDir(dir);
            Directory.CreateDirectory(hd);
            var files = Directory.GetFiles(hd, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (!force && files.Count > 0)
            {
                var newest = File.GetLastWriteTimeUtc(files[files.Count - 1]);
                if ((DateTime.UtcNow - newest).TotalSeconds < HistorySpacingSeconds)
                {
                    // Same burst of edits: the newest entry follows the current state instead of adding one.
                    File.WriteAllText(files[files.Count - 1], json);
                    return;
                }
                if (File.ReadAllText(files[files.Count - 1]) == json) return;
            }
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            File.WriteAllText(Path.Combine(hd, id + ".json"), json);
            files.Add(Path.Combine(hd, id + ".json"));
            for (int i = 0; i < files.Count - HistoryLimit; i++)
            {
                try { File.Delete(files[i]); }
                catch (Exception e) { ProjectLog.Warn($"[project] old history entry {files[i]} not deleted: {e.Message}"); }
            }
        }

        public static List<HistoryEntry> History(PackProject p)
        {
            var list = new List<HistoryEntry>();
            string hd = HistoryDir(p.Dir);
            if (!Directory.Exists(hd)) return list;
            foreach (string f in Directory.GetFiles(hd, "*.json").OrderByDescending(f => f, StringComparer.Ordinal))
            {
                int npcs = -1;
                try { npcs = (JObject.Parse(File.ReadAllText(f))["npcs"] as JArray)?.Count ?? 0; }
                catch (Exception e) { ProjectLog.Warn($"[project] history entry {f} unreadable: {e.Message}"); }
                list.Add(new HistoryEntry { id = Path.GetFileNameWithoutExtension(f), savedUtc = File.GetLastWriteTimeUtc(f), npcs = npcs });
            }
            return list;
        }

        /// <summary>Replaces the current state with a history entry. The current state is kept as a new entry first.</summary>
        public static bool Restore(PackProject p, string historyId)
        {
            string f = Path.Combine(HistoryDir(p.Dir), Path.GetFileName(historyId) + ".json");
            if (!File.Exists(f)) { ProjectLog.Error($"[project] history entry '{historyId}' not found."); return false; }
            try
            {
                var m = JObject.Parse(File.ReadAllText(f));
                Save(p, snapshot: true, forceSnapshot: true);
                p.Manifest = m;
                bool ok = Save(p, snapshot: false);
                if (ok) ProjectLog.Info($"[project] '{p.Folder}' restored to {historyId}.");
                return ok;
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[project] restoring '{historyId}' failed: {e.Message}");
                return false;
            }
        }

        public static bool Delete(string folder)
        {
            try
            {
                string dir = ProjectDir(folder);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                ProjectLog.Info($"[project] deleted '{folder}'.");
                return true;
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[project] deleting '{folder}' failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Copies a picture into the project folder (sub-folder <paramref name="sub"/>, e.g. "layers") and returns
        /// its project-relative path with forward slashes, or null. An existing file of the same name and size is
        /// reused; a different one gets a numbered name.
        /// </summary>
        public static string ImportFile(PackProject p, string absolute, string sub)
        {
            try
            {
                if (!File.Exists(absolute)) { ProjectLog.Error($"[project] import: {absolute} does not exist."); return null; }
                string destDir = Path.Combine(p.Dir, sub);
                Directory.CreateDirectory(destDir);
                string name = Path.GetFileName(absolute), dest = Path.Combine(destDir, name);
                for (int n = 1; File.Exists(dest) && new FileInfo(dest).Length != new FileInfo(absolute).Length; n++)
                    dest = Path.Combine(destDir, Path.GetFileNameWithoutExtension(name) + "_" + n + Path.GetExtension(name));
                File.Copy(absolute, dest, true);
                return sub.Replace('\\', '/').TrimEnd('/') + "/" + Path.GetFileName(dest);
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[project] import of {absolute} failed: {e.Message}");
                return null;
            }
        }

        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Indented);
    }
}
