using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Personify.Editor;
using Personify.Project;

namespace Personify.Host
{
    /// <summary>
    /// Vanilla NPCs as templates: the list for the pickers, a new NPC copied from one, "edit a vanilla NPC" (a new
    /// project whose NPC replaces it) and the replacement target of an installed card.
    /// </summary>
    internal static class TemplatesBridge
    {
        private const string CardsApi = "Personnel.Cards.CardsApi";

        /// <summary>Whether the installed Personnel knows replacements (2.6+).</summary>
        internal static bool PersonnelReady => GameData.PersonnelMethod(CardsApi, "SetReplacement") != null;

        /// <summary><c>{ ok, available, personnelReady, npcs: [{ id, name, role, region, blocked, replacedBy }] }</c>.</summary>
        internal static JObject List()
        {
            var list = GameData.Templates();
            var arr = new JArray();
            if (list != null)
                foreach (var t in list)
                    arr.Add(new JObject
                    {
                        ["id"] = t.id, ["name"] = t.name, ["role"] = t.role, ["region"] = t.region,
                        ["blocked"] = t.blocked, ["replacedBy"] = t.replacedBy,
                    });
            return new JObject { ["ok"] = true, ["available"] = list != null, ["personnelReady"] = PersonnelReady, ["npcs"] = arr };
        }

        internal static Templates.Entry Find(string id)
        {
            var e = GameData.Templates()?.FirstOrDefault(t => string.Equals(t.id, id, StringComparison.OrdinalIgnoreCase));
            if (e == null) Core.Log.Warning($"[template] '{id}' is not in {GameData.TemplatesFile} - load a save with Personnel 2.6+ first.");
            return e;
        }

        /// <summary>
        /// The NPC of template <paramref name="id"/> copied into <paramref name="p"/>. With <paramref name="replace"/>
        /// it takes over the vanilla NPC and keeps its name; otherwise it is a new NPC named <paramref name="name"/>.
        /// </summary>
        internal static JObject CopyFrom(PackProject p, string id, string name, bool replace)
        {
            var e = Find(id);
            if (e == null) return null;
            if (replace && !string.IsNullOrEmpty(e.blocked))
            {
                Core.Log.Warning($"[template] '{id}' cannot be replaced: {e.blocked}.");
                return null;
            }
            var src = (JObject)e.npc.DeepClone();
            if (src["appearance"] is JObject a) CanonicalPaths(a);
            var n = Templates.Copy(p, src, replace ? e.name : name, replace ? e.id : null, e.id, replace ? null : GameData.Hints());
            Core.Log.Msg($"[template] '{(string)n["id"]}' copied from vanilla '{e.id}' ({e.role}){(replace ? ", replaces it" : "")}.");
            return n;
        }

        /// <summary>"Edit a vanilla NPC": a new project with one NPC that replaces <paramref name="id"/>. <c>{ ok, folder }</c>.</summary>
        internal static JObject EditVanilla(string id)
        {
            var e = Find(id);
            if (e == null) return Calls.Fail("template_missing");
            if (!string.IsNullOrEmpty(e.blocked)) { var f = Calls.Fail("replace_blocked"); f["text"] = e.blocked; return f; }
            var p = ProjectFiles.Create(e.name, null);
            if (p == null) return Calls.Fail("create_failed");
            var n = CopyFrom(p, id, null, true);
            if (n == null) return Calls.Fail("template_missing");
            p.Npcs.Add(n);
            if (!ProjectFiles.Save(p, snapshot: false)) return Calls.Fail("create_failed");
            Core.Log.Msg($"[template] project '{p.Folder}' edits vanilla '{id}'.");
            return new JObject { ["ok"] = true, ["folder"] = p.Folder };
        }

        /// <summary>A new project with a copy of template <paramref name="id"/> as a new NPC. <c>{ ok, folder }</c>.</summary>
        internal static JObject NewFromTemplate(string id, string name)
        {
            var e = Find(id);
            if (e == null) return Calls.Fail("template_missing");
            string n0 = string.IsNullOrWhiteSpace(name) ? e.name : name.Trim();
            var p = ProjectFiles.Create(n0, null);
            if (p == null) return Calls.Fail("create_failed");
            var n = CopyFrom(p, id, n0, false);
            if (n == null) return Calls.Fail("template_missing");
            p.Npcs.Add(n);
            if (!ProjectFiles.Save(p, snapshot: false)) return Calls.Fail("create_failed");
            return new JObject { ["ok"] = true, ["folder"] = p.Folder };
        }

        /// <summary>Sets which vanilla NPC an installed card replaces (null: a new NPC). Answers Personnel's JSON.</summary>
        internal static JToken SetCardTarget(string code, string target)
        {
            var m = GameData.PersonnelMethod(CardsApi, "SetReplacement");
            if (m == null) { Core.Log.Warning("[cards] setting a card target needs Personnel 2.6 or newer."); return Calls.Fail("personnel_too_old"); }
            string json = (string)m.Invoke(null, new object[] { code, string.IsNullOrWhiteSpace(target) ? null : target });
            Core.Log.Msg($"[cards] card {code} target {(string.IsNullOrWhiteSpace(target) ? "none" : target)}: {json}");
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception ex) { Core.Log.Error($"[cards] target answer is not JSON ({ex.Message})."); return Calls.Fail("bad_answer"); }
            // Personnel's codes collide with the editor's own (blocked = problems, not_found = project).
            switch ((string)o["error"])
            {
                case "blocked": o["error"] = "replace_blocked"; break;
                case "not_found": o["error"] = "card_not_found"; break;
                case "failed": o["error"] = "target_failed"; break;
            }
            return o;
        }

        /// <summary>
        /// After an install: every other pack entry that replaced one of this project's targets lets go of it (one
        /// replacement per vanilla NPC). Answers the entries that changed.
        /// </summary>
        internal static JArray ReleaseOthers(PackProject p)
        {
            var changed = new JArray();
            string pack = Installer.InstallName(p);
            var m = GameData.PersonnelMethod(CardsApi, "ReleaseTarget");
            foreach (var t in p.Npcs.OfType<JObject>())
            {
                string target = PackProject.ReplacesOf(t);
                if (target == null) continue;
                if (m == null) { Core.Log.Warning($"[install] '{(string)t["id"]}' replaces '{target}', but this Personnel does not know replacements (2.6+ needed)."); continue; }
                try
                {
                    var arr = JArray.Parse((string)m.Invoke(null, new object[] { target, pack, (string)t["id"] }));
                    foreach (var x in arr) changed.Add(x);
                }
                catch (Exception ex) { Core.Log.Error($"[install] releasing '{target}' from other packs failed: {ex.Message}"); }
            }
            if (changed.Count > 0) Core.Log.Msg($"[install] '{p.Folder}' took over {changed.Count} replacement(s) from other packs.");
            return changed;
        }

        // A look captured from a live avatar names pieces by object id; the editor's pickers store catalog paths.
        private static void CanonicalPaths(JObject a)
        {
            if ((string)a["hairPath"] is string h && h.Length > 0) a["hairPath"] = PathCatalog.PathOf(h);
            foreach (string list in new[] { "faceLayers", "bodyLayers", "accessories" })
                if (a[list] is JArray arr)
                    foreach (var l in arr.OfType<JObject>())
                        if ((string)l["path"] is string path && path.Length > 0) l["path"] = PathCatalog.PathOf(path);
        }
    }
}
