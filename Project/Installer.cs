using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Personify.Project
{
    /// <summary>Outcome of an install or publish, shown to the player as one line.</summary>
    public sealed class InstallResult
    {
        public bool ok;
        /// <summary>Stable code for the page ("installed", "blocked", "failed", "published").</summary>
        public string code;
        public string path;
        public string text;
    }

    /// <summary>
    /// Puts a project into Personnel's pack folder ("In mein Spiel"), tells whether the installed copy is current,
    /// and builds the Thunderstore/Nexus zip ("Veröffentlichen"). Only the folder this project installed last time
    /// is ever replaced or removed.
    /// </summary>
    public static class Installer
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string InstallName(PackProject p)
            => (string)p.Editor["installedAs"] is string s && s.Length > 0 ? s : ProjectFiles.Sanitize(p.Name);

        /// <summary>"not_installed", "installed" (same content) or "changed" (installed, edited since).</summary>
        public static string State(PackProject p, string packsRoot)
        {
            string target = Path.Combine(packsRoot, InstallName(p));
            if (!Directory.Exists(target)) return "not_installed";
            try { return Hash(Expected(p)) == Hash(Read(target)) ? "installed" : "changed"; }
            catch (Exception e)
            {
                ProjectLog.Warn($"[install] comparing '{p.Folder}' with {target} failed: {e.Message}");
                return "changed";
            }
        }

        public static InstallResult Install(PackProject p, string packsRoot, IReadOnlyList<KnownNpc> catalog = null)
        {
            var problems = Problems.Check(p, catalog);
            int errors = Problems.ErrorCount(problems);
            if (errors > 0)
            {
                ProjectLog.Warn($"[install] '{p.Folder}' not installed: {errors} error(s), first: {problems.First(x => x.severity == "error").text}");
                return new InstallResult { ok = false, code = "blocked", text = $"{errors} error(s) block the install." };
            }

            string name = ProjectFiles.Sanitize(p.Name);
            string target = Path.Combine(packsRoot, name);
            string staging = target + ".installing";
            try
            {
                Directory.CreateDirectory(packsRoot);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                foreach (var kv in Expected(p))
                {
                    string dest = Path.Combine(staging, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, kv.Value);
                }

                // A renamed pack must not leave its old folder behind: Personnel would load both and report every
                // NPC twice. Only the folder this project wrote last time is removed.
                string previous = (string)p.Editor["installedAs"];
                if (!string.IsNullOrEmpty(previous) && !string.Equals(previous, name, StringComparison.OrdinalIgnoreCase))
                {
                    string old = Path.Combine(packsRoot, previous);
                    if (Directory.Exists(old)) { Directory.Delete(old, true); ProjectLog.Info($"[install] removed the old install {old} (pack renamed)."); }
                }
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(staging, target);

                p.Editor["installedAs"] = name;
                p.Editor["installedUtc"] = DateTime.UtcNow;
                ProjectFiles.Save(p, snapshot: false);
                ProjectLog.Info($"[install] '{p.Folder}' ({p.Npcs.Count} NPC(s)) installed to {target}.");
                return new InstallResult { ok = true, code = "installed", path = target, text = $"Installed {p.Npcs.Count} NPC(s)." };
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[install] '{p.Folder}' to {target} failed: {e.Message}");
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (Exception e2) { ProjectLog.Warn($"[install] staging folder {staging} not removed: {e2.Message}"); }
                return new InstallResult { ok = false, code = "failed", text = "Install failed: " + e.Message };
            }
        }

        /// <summary>Removes the installed copy of the project (only the folder it installed).</summary>
        public static bool Uninstall(PackProject p, string packsRoot)
        {
            string previous = (string)p.Editor["installedAs"];
            if (string.IsNullOrEmpty(previous)) { ProjectLog.Info($"[install] '{p.Folder}' was never installed - nothing to remove."); return true; }
            string target = Path.Combine(packsRoot, previous);
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                p.Editor.Remove("installedAs");
                p.Editor.Remove("installedUtc");
                ProjectFiles.Save(p, snapshot: false);
                ProjectLog.Info($"[install] removed {target}.");
                return true;
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[install] removing {target} failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Builds <c>&lt;exportsRoot&gt;/&lt;name&gt;-&lt;version&gt;.zip</c>: Thunderstore manifest, README, LICENSE, icon
        /// and the pack under <c>Personnel/Packs/&lt;name&gt;/</c>.
        /// </summary>
        public static InstallResult Publish(PackProject p, string exportsRoot, string personnelVersion, IReadOnlyList<KnownNpc> catalog = null)
        {
            var problems = Problems.Check(p, catalog);
            if (Problems.ErrorCount(problems) > 0)
                return new InstallResult { ok = false, code = "blocked", text = $"{Problems.ErrorCount(problems)} error(s) block publishing." };

            var e = p.Editor;
            string packName = ProjectFiles.Sanitize(p.Name);
            string tsName = Regex(packName.Replace(' ', '_'));
            string version = NormalizeVersion((string)e["modVersion"]);
            string zipPath = Path.Combine(exportsRoot, $"{tsName}-{version}.zip");
            try
            {
                Directory.CreateDirectory(exportsRoot);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var kv in Expected(p))
                        Put(zip, $"Personnel/Packs/{packName}/{kv.Key}", kv.Value);

                    string description = (string)e["description"];
                    if (string.IsNullOrWhiteSpace(description)) description = "Custom NPCs for Schedule I, made with Personify.";
                    var ts = new JObject
                    {
                        ["name"] = tsName,
                        ["version_number"] = version,
                        ["website_url"] = (string)e["websiteUrl"] ?? "",
                        ["description"] = description.Length > 250 ? description.Substring(0, 250) : description,
                        ["dependencies"] = new JArray("DooDesch-Personnel-" + NormalizeVersion(personnelVersion)),
                    };
                    Put(zip, "manifest.json", Utf8.GetBytes(ts.ToString(Formatting.Indented)));
                    Put(zip, "README.md", Utf8.GetBytes(Readme(p, packName, description)));
                    string license = (string)e["license"];
                    Put(zip, "LICENSE", Utf8.GetBytes((string.IsNullOrWhiteSpace(license) ? "All rights reserved" : license) + "\n"));

                    string icon = (string)e["iconSource"];
                    string iconAbs = string.IsNullOrWhiteSpace(icon) ? null : Path.Combine(p.Dir, icon.Replace('/', Path.DirectorySeparatorChar));
                    if (iconAbs != null && File.Exists(iconAbs)) Put(zip, "icon.png", File.ReadAllBytes(iconAbs));
                    else ProjectLog.Warn($"[publish] '{p.Folder}' has no icon - Thunderstore needs a 256x256 icon.png before upload.");
                }
                ProjectLog.Info($"[publish] '{p.Folder}' -> {zipPath}");
                return new InstallResult { ok = true, code = "published", path = zipPath, text = "Package built." };
            }
            catch (Exception ex)
            {
                ProjectLog.Error($"[publish] '{p.Folder}' failed: {ex.Message}");
                return new InstallResult { ok = false, code = "failed", text = "Publishing failed: " + ex.Message };
            }
        }

        private static string Readme(PackProject p, string packName, string description)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(p.Name).Append("\n\n").Append(description).Append("\n\n");
            sb.Append("Made with Personify. Needs the Personnel mod.\n\n## Install\n");
            sb.Append($"A mod manager installs it. By hand: copy `Personnel/Packs/{packName}` into `UserData/Personnel/Packs/`.\n\n## NPCs\n");
            foreach (var t in p.Npcs)
                if (t is JObject n) sb.Append("- **").Append((string)n["name"]).Append("** (").Append(Problems.Role(n)).Append(")\n");
            return sb.ToString();
        }

        private static void Put(ZipArchive zip, string name, byte[] data)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = entry.Open()) s.Write(data, 0, data.Length);
        }

        private static string Regex(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '_');
            return sb.Length == 0 ? "NPC_Pack" : sb.ToString();
        }

        public static string NormalizeVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "1.0.0";
            var parts = v.Trim().TrimStart('v').Split('-')[0].Split('.').Where(x => x.Length > 0).ToList();
            while (parts.Count < 3) parts.Add("0");
            return string.Join(".", parts.Take(3).Select(x => int.TryParse(x, out int n) ? n.ToString() : "0"));
        }

        /// <summary>Relative path (forward slashes) to bytes: what an install writes.</summary>
        private static SortedDictionary<string, byte[]> Expected(PackProject p)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            string root = Path.GetFullPath(p.Dir);
            foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith(PackProject.EditorDir + "/", StringComparison.Ordinal)) continue;
                if (rel == PackProject.ManifestFile || rel.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                files[rel] = File.ReadAllBytes(f);
            }
            files[PackProject.ManifestFile] = Utf8.GetBytes(p.InstallManifest().ToString(Formatting.Indented));
            return files;
        }

        private static SortedDictionary<string, byte[]> Read(string dir)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            string root = Path.GetFullPath(dir);
            foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                files[f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllBytes(f);
            return files;
        }

        private static string Hash(SortedDictionary<string, byte[]> files)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var kv in files)
                {
                    byte[] name = Utf8.GetBytes(kv.Key + "\n");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    sha.TransformBlock(kv.Value, 0, kv.Value.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Convert.ToBase64String(sha.Hash);
            }
        }
    }
}
