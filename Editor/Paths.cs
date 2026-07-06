using System.IO;
using MelonLoader.Utils;

namespace Personify.Editor
{
    /// <summary>
    /// Filesystem layout under UserData/Personify. Import = drop-zone for source PNGs; Projects = editable
    /// projects; Exports = ready-to-release Personnel NPC packs.
    /// </summary>
    public static class Paths
    {
        public static string Root => Path.Combine(MelonEnvironment.UserDataDirectory, "Personify");
        public static string Import => Path.Combine(Root, "Import");
        public static string Projects => Path.Combine(Root, "Projects");
        public static string Exports => Path.Combine(Root, "Exports");

        public static string ProjectDir(string name) => Path.Combine(Projects, Sanitize(name));
        public static string ProjectFile(string name) => Path.Combine(ProjectDir(name), "project.json");
        public static string SourcesDir(string name) => Path.Combine(ProjectDir(name), "sources");
        public static string ExportDir(string name) => Path.Combine(Exports, Sanitize(name));

        public static void EnsureBaseDirs()
        {
            Directory.CreateDirectory(Import);
            Directory.CreateDirectory(Projects);
            Directory.CreateDirectory(Exports);
        }

        /// <summary>Folder-safe name (letters/digits/-/_/space).</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Untitled";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ') ? c : '_');
            string outp = sb.ToString().Trim();
            return outp.Length == 0 ? "Untitled" : outp;
        }
    }
}
