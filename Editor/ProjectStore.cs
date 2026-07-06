using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Personify.Editor
{
    /// <summary>
    /// Loads/saves <see cref="NpcProject"/> as project.json under UserData/Personify/Projects/&lt;name&gt;, and copies
    /// imported PNGs into the project's self-contained sources/ folder.
    /// </summary>
    public static class ProjectStore
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public const string DefaultDescription = "Custom NPCs for Schedule I, made with the Personify editor.";

        public static List<string> List()
        {
            var result = new List<string>();
            try
            {
                Paths.EnsureBaseDirs();
                foreach (string dir in Directory.GetDirectories(Paths.Projects))
                    if (File.Exists(Path.Combine(dir, "project.json")))
                        result.Add(Path.GetFileName(dir));
            }
            catch (Exception e) { Core.Log?.Warning("[project] list failed: " + e.Message); }
            return result;
        }

        public static bool Exists(string name) => File.Exists(Paths.ProjectFile(name));

        public static string DisplayName(string folder)
        {
            try
            {
                string file = Paths.ProjectFile(folder);
                if (File.Exists(file))
                {
                    var p = JsonConvert.DeserializeObject<NpcProject>(File.ReadAllText(file));
                    if (p != null && !string.IsNullOrWhiteSpace(p.Name)) return p.Name;
                }
            }
            catch { }
            return folder;
        }

        public static int NpcCount(string folder)
        {
            try
            {
                string file = Paths.ProjectFile(folder);
                if (File.Exists(file))
                {
                    var p = JsonConvert.DeserializeObject<NpcProject>(File.ReadAllText(file));
                    if (p?.Npcs != null) return p.Npcs.Count;
                }
            }
            catch { }
            return 0;
        }

        public static NpcProject Create(string name, string author = "")
        {
            string folder = Paths.Sanitize(name);
            Directory.CreateDirectory(Paths.SourcesDir(folder));
            var project = new NpcProject { Name = name, Author = author, FolderName = folder, Description = DefaultDescription };
            // Start with one NPC so the editor always has something to show.
            project.Npcs.Add(new NpcDraft { Name = "NPC 1" });
            Save(project);
            return project;
        }

        public static NpcProject Load(string name)
        {
            try
            {
                string file = Paths.ProjectFile(name);
                if (!File.Exists(file)) return null;
                var project = JsonConvert.DeserializeObject<NpcProject>(File.ReadAllText(file));
                if (project == null) return null;
                project.FolderName = Paths.Sanitize(name);
                project.Npcs ??= new List<NpcDraft>();
                return project;
            }
            catch (Exception e)
            {
                Core.Log?.Warning("[project] load '" + name + "' failed: " + e.Message);
                return null;
            }
        }

        public static bool Save(NpcProject project)
        {
            if (project == null) return false;
            try
            {
                string folder = string.IsNullOrEmpty(project.FolderName) ? Paths.Sanitize(project.Name) : project.FolderName;
                project.FolderName = folder;
                Directory.CreateDirectory(Paths.SourcesDir(folder));
                File.WriteAllText(Paths.ProjectFile(folder), JsonConvert.SerializeObject(project, JsonSettings));
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Warning("[project] save failed: " + e.Message);
                return false;
            }
        }

        public static bool Delete(string folder)
        {
            try
            {
                string dir = Paths.ProjectDir(folder);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Warning("[project] delete '" + folder + "' failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Copy a PNG from anywhere into the project's sources/ folder. Returns the project-relative path to store on
        /// a <see cref="LayerDraft"/> (e.g. "sources/grin.png"), or null on failure.
        /// </summary>
        public static string ImportSource(NpcProject project, string absolutePngPath)
        {
            try
            {
                if (project == null || string.IsNullOrEmpty(absolutePngPath) || !File.Exists(absolutePngPath)) return null;
                string folder = project.FolderName;
                string fileName = Path.GetFileName(absolutePngPath);
                string dest = Path.Combine(Paths.SourcesDir(folder), fileName);
                int n = 1;
                while (File.Exists(dest) && !SameLength(absolutePngPath, dest))
                {
                    string stem = Path.GetFileNameWithoutExtension(fileName);
                    dest = Path.Combine(Paths.SourcesDir(folder), $"{stem}_{n}.png");
                    n++;
                }
                Directory.CreateDirectory(Paths.SourcesDir(folder));
                File.Copy(absolutePngPath, dest, true);
                return "sources/" + Path.GetFileName(dest);
            }
            catch (Exception e)
            {
                Core.Log?.Warning("[project] import source failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Absolute path of any project-relative path (e.g. "sources/grin.png"), or null.</summary>
        public static string ResolveRelative(NpcProject project, string relative)
        {
            if (project == null || string.IsNullOrEmpty(relative)) return null;
            string rel = relative.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Paths.ProjectDir(project.FolderName), rel);
        }

        private static bool SameLength(string a, string b)
        {
            try { return new FileInfo(a).Length == new FileInfo(b).Length; }
            catch { return false; }
        }
    }
}
