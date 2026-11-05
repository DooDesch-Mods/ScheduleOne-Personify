using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Personify.Project
{
    /// <summary>
    /// Vanilla NPCs as templates, read from <c>World/templates.json</c> (written by Personnel 2.6+ on every save load),
    /// and the copy rules shared by "new NPC from a template" and "edit a vanilla NPC".
    /// </summary>
    public static class Templates
    {
        /// <summary>One vanilla NPC of the template file.</summary>
        public sealed class Entry
        {
            public string id, name, role, region;
            /// <summary>Why a card may not replace this NPC, or null.</summary>
            public string blocked;
            /// <summary>"pack/npcId" of the entry that replaces it now, or null.</summary>
            public string replacedBy;
            /// <summary>The NPC as a pack entry: name, appearance, role block, voice.</summary>
            public JObject npc;
        }

        /// <summary>Fields a template copies. Name, id, schedule, spawn and relationships are set new.</summary>
        public static readonly string[] Copied = { "appearance", "voice", "customer", "dealer", "supplier", "police", "behavior" };

        /// <summary>Reads the template file; null when it does not exist (no save loaded with Personnel 2.6+ yet).</summary>
        public static List<Entry> Read(string file)
        {
            if (!File.Exists(file)) return null;
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(file)); }
            catch (Exception e) { ProjectLog.Warn($"[template] {file} is unreadable ({e.Message}) - no vanilla templates."); return null; }
            return (root["npcs"] as JArray ?? new JArray()).OfType<JObject>().Select(o => new Entry
            {
                id = (string)o["id"] ?? "",
                name = (string)o["name"] ?? "",
                role = (string)o["role"] ?? "civilian",
                region = (string)o["region"] ?? "",
                blocked = (string)o["blocked"],
                replacedBy = (string)o["replacedBy"],
                npc = o["npc"] as JObject,
            }).Where(e => e.id.Length > 0 && e.npc != null).OrderBy(e => e.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Whether an NPC of role <paramref name="cardRole"/> may take over a vanilla NPC of role
        /// <paramref name="vanillaRole"/>: the same role, or a civilian over a customer. Mirrors Personnel's rule.
        /// </summary>
        public static bool RoleFits(string cardRole, string vanillaRole)
            => cardRole == vanillaRole || (cardRole == "civilian" && vanillaRole == "customer");

        /// <summary>
        /// A new NPC for <paramref name="p"/> with the look, role values and voice of <paramref name="source"/>.
        /// With <paramref name="replaces"/> set it takes over that vanilla NPC: it keeps the vanilla name and gets no
        /// routine or connections (the vanilla ones stay). Otherwise it is a new NPC named <paramref name="name"/>
        /// with the defaults of its role (a connection for a customer, a home for a dealer).
        /// </summary>
        public static JObject Copy(PackProject p, JObject source, string name, string replaces, string voiceFrom, NpcFactory.Hints hints)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            string finalName = string.IsNullOrWhiteSpace(name) ? ((string)source["name"] ?? "New NPC") : name.Trim();
            var n = new JObject { ["id"] = NpcFactory.FreeId(p, finalName), ["name"] = finalName };
            foreach (string key in Copied)
                if (source[key] != null && source[key].Type != JTokenType.Null) n[key] = source[key].DeepClone();
            if (!(n["voice"] is JObject) && !string.IsNullOrEmpty(voiceFrom)) n["voice"] = new JObject { ["from"] = voiceFrom };

            string role = Problems.Role(n);
            if (!string.IsNullOrEmpty(replaces))
            {
                n["replaces"] = replaces;
                // Home and dealer type stay vanilla; a template block must not carry them into the replacement.
                if (n["dealer"] is JObject d) { d.Remove("home"); d.Remove("type"); }
                if (n["supplier"] is JObject s) s.Remove("items");
            }
            else
            {
                if (n["dealer"] is JObject d && d["type"] == null) d["type"] = "player";
                NpcFactory.SetRole(n, role, hints);
                if (n["dealer"] is JObject d2 && string.IsNullOrWhiteSpace((string)d2["home"]) && !string.IsNullOrEmpty(hints?.DealerHome)) d2["home"] = hints.DealerHome;
                // The template file holds no supplier goods (the vanilla assortment is not a pack field).
                if (n["supplier"] is JObject s2 && !(s2["items"] is JArray items && items.Count > 0)) s2["items"] = new JArray("ogkushseed");
            }
            return n;
        }
    }
}
