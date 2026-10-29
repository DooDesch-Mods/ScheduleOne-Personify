using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Editor;

namespace Personify.Project
{
    /// <summary>
    /// Lifts a format 1 project (Personify 1.x, <c>project.json</c> with typed drafts) to format 2 (a Personnel
    /// manifest with editor blocks). The NPC ids stay the ones 1.x exported (<c>pack_name</c>), so installed packs
    /// and saves keep their NPCs. Nothing is dropped: switched-off behaviour and Backrooms settings and hidden
    /// layers move into the editor blocks. The old file stays as <c>.personify/project.v1.json</c>.
    /// </summary>
    public static class Migration
    {
        public const string V1File = "project.json";

        public static NpcProject ReadV1(string file)
        {
            try { return JsonConvert.DeserializeObject<NpcProject>(File.ReadAllText(file)); }
            catch (Exception e) { ProjectLog.Error($"[migrate] {file} unreadable: {e.Message}"); return null; }
        }

        public static PackProject Migrate(string dir, string folder)
        {
            string v1File = Path.Combine(dir, V1File);
            var v1 = ReadV1(v1File);
            if (v1 == null) return null;
            try
            {
                var p = new PackProject { Folder = folder, Dir = dir, Manifest = Convert(v1) };
                string backupDir = Path.Combine(dir, PackProject.EditorDir);
                Directory.CreateDirectory(backupDir);
                File.Copy(v1File, Path.Combine(backupDir, "project.v1.json"), true);
                if (!ProjectFiles.Save(p, snapshot: true, forceSnapshot: true)) return null;
                File.Delete(v1File);
                ProjectLog.Info($"[migrate] '{folder}': {v1.Npcs?.Count ?? 0} NPC(s) moved to format 2; backup in {PackProject.EditorDir}/project.v1.json.");
                return p;
            }
            catch (Exception e)
            {
                ProjectLog.Error($"[migrate] '{folder}' failed, project left as it was: {e.Message}");
                return null;
            }
        }

        public static JObject Convert(NpcProject v1)
        {
            var m = PackProject.NewManifest(v1.Name, v1.Author);
            if (!string.IsNullOrWhiteSpace(v1.PackId)) m["packId"] = v1.PackId;
            if (v1.AutoRegister.HasValue) m["autoRegister"] = v1.AutoRegister.Value;
            var e = PackProject.EditorBlock(m);
            e["modVersion"] = v1.ModVersion ?? "1.0.0";
            e["description"] = v1.Description ?? "";
            e["websiteUrl"] = v1.WebsiteUrl ?? "";
            e["license"] = v1.License ?? "All rights reserved";
            if (!string.IsNullOrWhiteSpace(v1.IconSource)) e["iconSource"] = v1.IconSource;

            var npcs = (JArray)m["npcs"];
            foreach (var d in v1.Npcs ?? new List<NpcDraft>())
                if (d != null) npcs.Add(ConvertNpc(v1.Name, d));
            return m;
        }

        private static JObject ConvertNpc(string packName, NpcDraft d)
        {
            var o = new JObject { ["id"] = Ids.Make(packName, d.Name), ["name"] = d.Name };
            if (!string.IsNullOrWhiteSpace(d.SaveId)) o["saveId"] = d.SaveId;
            var ed = PackProject.EditorBlock(o);

            var a = d.Appearance ?? new AppearanceDraft();
            var hidden = new JArray();
            o["appearance"] = LookJson.FromDraft(a, hidden);
            if (hidden.Count > 0) ed["hiddenLayers"] = hidden;
            if (!a.EyelidFollowsSkin) ed["eyelidsCustom"] = true;

            var b = d.Behavior;
            if (b != null)
            {
                var bo = new JObject { ["aggression"] = b.Aggression, ["maxHealth"] = b.MaxHealth, ["scale"] = b.Scale, ["conversation"] = b.Conversation };
                if (b.Enabled) o["behavior"] = bo; else ed["behaviorOff"] = bo;
            }

            Copy(o, "spawn", d.Spawn);
            Copy(o, "contact", d.Contact);
            Copy(o, "relationships", d.Relationships);
            Copy(o, "customer", d.Customer);
            Copy(o, "dealer", d.Dealer);
            Copy(o, "inventory", d.Inventory);
            Copy(o, "schedule", d.Schedule);

            var br = d.Backrooms;
            if (br != null)
            {
                var bro = new JObject
                {
                    ["archetype"] = br.Archetype, ["tierMin"] = br.TierMin, ["tierMax"] = br.TierMax,
                    ["biomes"] = new JArray(SplitCsv(br.Biomes)), ["weight"] = br.Weight, ["maxAlive"] = br.MaxAlive,
                    ["hostile"] = br.Hostile, ["ambient"] = br.Ambient, ["spawnCooldown"] = br.SpawnCooldown,
                };
                if (br.Enabled) o["extensions"] = new JObject { ["backrooms"] = bro };
                else ed["backroomsOff"] = bro;
            }
            return o;
        }

        private static void Copy(JObject o, string key, JToken t)
        {
            if (t != null && t.Type != JTokenType.Null) o[key] = t.DeepClone();
        }

        private static IEnumerable<string> SplitCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) yield break;
            foreach (string part in csv.Split(','))
                if (part.Trim().Length > 0) yield return part.Trim();
        }
    }
}
