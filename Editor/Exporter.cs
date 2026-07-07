using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace Personify.Editor
{
    /// <summary>Outcome of an export attempt, used to drive the result card.</summary>
    public sealed class ExportResult
    {
        public bool Ok;
        public string Message;
        public string ExportDir;
    }

    /// <summary>
    /// Derives a ready-to-release package from an <see cref="NpcProject"/>: the flat Personnel pack
    /// (<c>manifest.json</c> + copied custom PNGs) wrapped in a Thunderstore-style folder with README/LICENSE and a
    /// dependency on Personnel. The pack manifest POCOs are re-declared privately here so this mod never references
    /// Personnel (file-coupled only).
    /// </summary>
    public static class Exporter
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string Validate(NpcProject project)
        {
            if (project == null) return "No project.";
            if (string.IsNullOrWhiteSpace(project.Name)) return "The pack needs a name.";
            if (project.Npcs == null || project.Npcs.Count == 0) return "Add at least one NPC.";
            // Ids are derived (pack_name); a duplicate id means two NPCs share a name in the same pack - reject it.
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in project.Npcs)
            {
                if (string.IsNullOrWhiteSpace(n.Name)) return "Every NPC needs a name.";
                string id = Ids.Make(project.Name, n.Name);
                if (!ids.Add(id)) return $"Two NPCs would get the same id '{id}' - give them unique names.";
            }
            return null;
        }

        public static ExportResult Export(NpcProject project)
        {
            string err = Validate(project);
            if (err != null) return new ExportResult { Ok = false, Message = err };

            try
            {
                string packName = Paths.Sanitize(project.Name);
                string exportRoot = Paths.ExportDir(project.Name);
                string packDir = Path.Combine(exportRoot, "Personnel", "Packs", packName);
                // Empty the export folder IN PLACE (keep the folder itself) rather than delete+recreate it: an already
                // open Windows Explorer window on this path stays valid and picks up the rewritten files, whereas
                // deleting the folder orphans that window so it shows nothing. Best-effort so a locked file preview
                // can't fail the whole export.
                Directory.CreateDirectory(exportRoot);
                EmptyDirectory(exportRoot);
                Directory.CreateDirectory(packDir);

                var manifest = BuildPackManifest(project, packDir);
                File.WriteAllText(Path.Combine(packDir, "manifest.json"), JsonConvert.SerializeObject(manifest, JsonSettings));

                WriteThunderstoreManifest(project, exportRoot);
                WriteReadme(project, exportRoot, packName);
                WriteLicense(project, exportRoot);
                CopyIconIfAny(project, exportRoot);

                Core.Log?.Msg($"[export] wrote pack '{packName}' ({project.Npcs.Count} NPC) -> {exportRoot}");
                return new ExportResult
                {
                    Ok = true,
                    ExportDir = exportRoot,
                    Message = $"Exported {project.Npcs.Count} NPC(s). Copy the 'Personnel/Packs/{packName}' folder into UserData/Personnel/Packs to test."
                };
            }
            catch (Exception e)
            {
                Core.Log?.Warning("[export] failed: " + e.Message);
                return new ExportResult { Ok = false, Message = "Export failed: " + e.Message };
            }
        }

        // Delete a folder's contents without deleting the folder itself (best-effort per entry, so a file locked by an
        // Explorer preview is skipped rather than aborting the export - it gets overwritten by the fresh write anyway).
        private static void EmptyDirectory(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                    try { File.Delete(f); } catch { }
                foreach (string d in Directory.GetDirectories(dir))
                    try { Directory.Delete(d, true); } catch { }
            }
            catch { }
        }

        // ---- pack manifest -------------------------------------------------------------------------------------

        private static PkManifest BuildPackManifest(NpcProject project, string packDir)
        {
            var m = new PkManifest { name = project.Name, author = project.Author };
            var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NpcDraft npc in project.Npcs)
            {
                var a = npc.Appearance ?? new AppearanceDraft();
                var pa = new PkAppear
                {
                    gender = a.Gender, height = a.Height, weight = a.Weight,
                    skinColor = a.SkinColor, hairPath = a.HairPath, hairColor = a.HairColor,
                    eyebrowScale = a.EyebrowScale, eyebrowThickness = a.EyebrowThickness,
                    eyebrowRestingHeight = a.EyebrowRestingHeight, eyebrowRestingAngle = a.EyebrowRestingAngle,
                    leftEyeLidColor = a.LeftEyeLidColor, rightEyeLidColor = a.RightEyeLidColor,
                    leftEye = new PkEye { top = a.LeftEyeTop, bottom = a.LeftEyeBottom },
                    rightEye = new PkEye { top = a.RightEyeTop, bottom = a.RightEyeBottom },
                    eyeballMaterial = a.EyeballMaterial, eyeBallTint = a.EyeBallTint, pupilDilation = a.PupilDilation,
                    faceLayers = ExportLayers(project, a.FaceLayers, "face", packDir, usedFiles),
                    bodyLayers = ExportLayers(project, a.BodyLayers, "body", packDir, usedFiles),
                    accessories = ExportAccessories(a.Accessories),
                    distortion = a.Distortion.Count == 0 ? null : ExportDistortion(a.Distortion)
                };

                var entry = new PkNpc { id = Ids.Make(project.Name, npc.Name), name = npc.Name, appearance = pa };

                if (npc.Behavior != null && npc.Behavior.Enabled)
                    entry.behavior = new PkBehav
                    {
                        aggression = npc.Behavior.Aggression, maxHealth = npc.Behavior.MaxHealth,
                        scale = npc.Behavior.Scale, conversation = npc.Behavior.Conversation
                    };

                if (npc.Backrooms != null && npc.Backrooms.Enabled)
                {
                    var b = npc.Backrooms;
                    entry.extensions = new Dictionary<string, object>
                    {
                        ["backrooms"] = new Dictionary<string, object>
                        {
                            ["archetype"] = b.Archetype,
                            ["tierMin"] = b.TierMin,
                            ["tierMax"] = b.TierMax,
                            ["biomes"] = ParseBiomes(b.Biomes),
                            ["weight"] = b.Weight,
                            ["maxAlive"] = b.MaxAlive,
                            ["hostile"] = b.Hostile,
                            ["ambient"] = b.Ambient,
                            ["spawnCooldown"] = b.SpawnCooldown
                        }
                    };
                }

                m.npcs.Add(entry);
            }
            return m;
        }

        // Face/body layers: existing paths pass through; custom source PNGs are copied into the pack + referenced by file.
        private static List<PkLayer> ExportLayers(NpcProject project, List<LayerDraft> layers, string kind, string packDir, HashSet<string> usedFiles)
        {
            var outp = new List<PkLayer>();
            if (layers == null) return outp;
            foreach (LayerDraft l in layers)
            {
                if (l == null || !l.Visible) continue;   // hidden (eye off) - excluded from the exported pack
                if (!string.IsNullOrWhiteSpace(l.Source))
                {
                    string file = CopySource(project, l.Source, packDir, usedFiles);
                    if (file != null) outp.Add(new PkLayer { file = file, kind = kind, tint = l.Tint });
                }
                else if (!string.IsNullOrWhiteSpace(l.Path))
                {
                    outp.Add(new PkLayer { path = l.Path, tint = l.Tint });
                }
            }
            return outp;
        }

        private static Dictionary<string, PkBone> ExportDistortion(Dictionary<string, BoneDistortionDraft> src)
        {
            var outp = new Dictionary<string, PkBone>();
            foreach (var kv in src)
                if (kv.Value != null) outp[kv.Key] = new PkBone { scaleX = kv.Value.ScaleX, scaleY = kv.Value.ScaleY, scaleZ = kv.Value.ScaleZ, hide = kv.Value.Hide };
            return outp;
        }

        private static List<PkLayer> ExportAccessories(List<LayerDraft> layers)
        {
            var outp = new List<PkLayer>();
            if (layers == null) return outp;
            foreach (LayerDraft l in layers)
            {
                if (l == null || !l.Visible || string.IsNullOrWhiteSpace(l.Path)) continue;
                outp.Add(new PkLayer { path = l.Path, color = l.Tint });
            }
            return outp;
        }

        private static string CopySource(NpcProject project, string source, string packDir, HashSet<string> usedFiles)
        {
            try
            {
                string abs = ProjectStore.ResolveRelative(project, source);
                if (abs == null || !File.Exists(abs)) return null;
                string file = Path.GetFileName(abs);
                int n = 1;
                while (!usedFiles.Add(file))
                    file = Path.GetFileNameWithoutExtension(abs) + "_" + (n++) + Path.GetExtension(abs);
                File.Copy(abs, Path.Combine(packDir, file), true);
                return file;
            }
            catch (Exception e) { Core.Log?.Warning("[export] copy source failed: " + e.Message); return null; }
        }

        private static List<string> ParseBiomes(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (string part in csv.Split(','))
            {
                string p = part.Trim();
                if (p.Length > 0) list.Add(p);
            }
            return list;
        }

        // ---- Thunderstore wrapper ------------------------------------------------------------------------------

        private static void WriteThunderstoreManifest(NpcProject project, string exportRoot)
        {
            var ts = new TsManifest
            {
                name = Paths.Sanitize(project.Name).Replace(' ', '_'),
                version_number = NormalizeVersion(project.ModVersion),
                website_url = project.WebsiteUrl ?? "",
                description = Truncate(string.IsNullOrWhiteSpace(project.Description) ? ProjectStore.DefaultDescription : project.Description, 250),
                dependencies = new[] { "DooDesch-Personnel-" + PersonnelVersion() }
            };
            File.WriteAllText(Path.Combine(exportRoot, "manifest.json"), JsonConvert.SerializeObject(ts, JsonSettings));
        }

        private static void WriteReadme(NpcProject project, string exportRoot, string packName)
        {
            string body =
$@"# {project.Name}

{(string.IsNullOrWhiteSpace(project.Description) ? ProjectStore.DefaultDescription : project.Description)}

Made with the **Personify** editor. Contains {project.Npcs.Count} NPC definition(s).

## Install
Requires the **Personnel** mod. Merge the `Personnel/Packs/{packName}` folder from this package into
`UserData/Personnel/Packs/`. NPC-consuming mods (e.g. Backrooms) pick the definitions up automatically.

## NPCs
{NpcList(project)}

## Note
Thunderstore requires a 256x256 `icon.png` in this folder before upload - add one if you plan to publish.
";
            File.WriteAllText(Path.Combine(exportRoot, "README.md"), body);
        }

        private static string NpcList(NpcProject project)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var n in project.Npcs)
                sb.AppendLine($"- **{n.Name}** (`{n.Id}`)" + (n.Backrooms != null && n.Backrooms.Enabled ? $" - Backrooms: {n.Backrooms.Archetype}" : ""));
            return sb.ToString();
        }

        private static void WriteLicense(NpcProject project, string exportRoot)
        {
            string body = string.IsNullOrWhiteSpace(project.License) ? "All rights reserved" : project.License;
            File.WriteAllText(Path.Combine(exportRoot, "LICENSE"), body + Environment.NewLine);
        }

        private static void CopyIconIfAny(NpcProject project, string exportRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(project.IconSource)) return;
                string abs = ProjectStore.ResolveRelative(project, project.IconSource);
                if (abs != null && File.Exists(abs)) File.Copy(abs, Path.Combine(exportRoot, "icon.png"), true);
            }
            catch (Exception e) { Core.Log?.Warning("[export] icon copy failed: " + e.Message); }
        }

        // Read Personnel's version at runtime (reflection, so this mod never references Personnel).
        private static string PersonnelVersion()
        {
            try
            {
                var t = typeof(MelonLoader.MelonBase);
                var prop = t.GetProperty("RegisteredMelons", BindingFlags.Public | BindingFlags.Static);
                if (prop?.GetValue(null) is System.Collections.IEnumerable list)
                {
                    foreach (var m in list)
                    {
                        var info = m?.GetType().GetProperty("Info")?.GetValue(m);
                        var name = info?.GetType().GetProperty("Name")?.GetValue(info) as string;
                        if (name == "Personnel")
                            return info.GetType().GetProperty("Version")?.GetValue(info) as string ?? "1.0.0";
                    }
                }
            }
            catch { }
            return "1.0.0";
        }

        private static string NormalizeVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "1.0.0";
            var parts = v.Split('.');
            if (parts.Length >= 3) return v;
            return v + string.Concat(System.Linq.Enumerable.Repeat(".0", 3 - parts.Length));
        }

        private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);

        // ---- private pack POCOs (mirror Personnel.Content.NpcPackManifest) --------------------------------------

        private sealed class PkManifest { public string name; public string author; public List<PkNpc> npcs = new List<PkNpc>(); }
        private sealed class PkNpc { public string id; public string name; public PkAppear appearance; public PkBehav behavior; public Dictionary<string, object> extensions; }
        private sealed class PkEye { public float top; public float bottom; }
        private sealed class PkLayer { public string path; public string file; public string kind; public string tint; public string color; }
        private sealed class PkBehav { public float aggression; public float maxHealth; public float scale; public string conversation; }
        private sealed class PkBone { public float? scaleX, scaleY, scaleZ; public bool? hide; }

        private sealed class PkAppear
        {
            public float gender, height, weight;
            public string skinColor, hairPath, hairColor;
            public float eyebrowScale, eyebrowThickness, eyebrowRestingHeight, eyebrowRestingAngle;
            public string leftEyeLidColor, rightEyeLidColor;
            public PkEye leftEye, rightEye;
            public string eyeballMaterial, eyeBallTint;
            public float pupilDilation;
            public List<PkLayer> faceLayers, bodyLayers, accessories;
            public Dictionary<string, PkBone> distortion;
        }

        private sealed class TsManifest { public string name; public string version_number; public string website_url; public string description; public string[] dependencies; }
    }
}
