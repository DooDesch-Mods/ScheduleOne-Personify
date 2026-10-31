using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Personify.Project
{
    /// <summary>One entry of the problem list.</summary>
    public sealed class Problem
    {
        /// <summary>"error" blocks installing; "warning" does not.</summary>
        public string severity;
        /// <summary>Stable code the page turns into an English or German sentence.</summary>
        public string code;
        /// <summary>Index of the NPC in the manifest, or -1 for the pack.</summary>
        public int npc = -1;
        /// <summary>Area of the editor the click jumps to: "pack", "look", "role", "routine" or "relations".</summary>
        public string area;
        /// <summary>Field path inside the NPC (e.g. "relationships.connections"), for the jump.</summary>
        public string field;
        /// <summary>Values for the sentence (an id, a file name).</summary>
        public string arg;
        /// <summary>English sentence, for the log and as a fallback.</summary>
        public string text;
    }

    /// <summary>One NPC the game knows, read from the NPC catalog Personnel writes on save load.</summary>
    public sealed class KnownNpc
    {
        public string id;
        public string name;
        public string source;
    }

    /// <summary>
    /// The live problem list: checks a project for what stops the pack from working (errors) or what makes an NPC
    /// behave unlike the author expects (warnings). Rules follow Personnel's loader and the vanilla game.
    /// </summary>
    public static class Problems
    {
        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]+$");

        public static int ErrorCount(List<Problem> list) => list.FindAll(p => p.severity == "error").Count;

        /// <param name="catalog">NPCs of the last loaded save; null when no save was loaded yet (connection checks skipped).</param>
        /// <param name="doorless">Buildings without a door an NPC may use, from the captured world; null when no world was captured yet (door checks skipped).</param>
        public static List<Problem> Check(PackProject p, IReadOnlyList<KnownNpc> catalog = null, ISet<string> doorless = null)
        {
            var list = new List<Problem>();
            if (string.IsNullOrWhiteSpace(p.Name)) Add(list, "error", "pack_name_missing", -1, "pack", "name", null, "The pack needs a name.");
            if (p.Npcs.Count == 0) Add(list, "error", "no_npcs", -1, "pack", null, null, "The pack has no NPC yet.");

            var known = new Dictionary<string, KnownNpc>(StringComparer.OrdinalIgnoreCase);
            if (catalog != null) foreach (var k in catalog) if (k?.id != null) known[k.id] = k;
            var own = p.NpcIds();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < p.Npcs.Count; i++)
            {
                if (!(p.Npcs[i] is JObject n)) { Add(list, "error", "npc_invalid", i, "pack", null, null, $"Entry {i + 1} in the NPC list is not an NPC."); continue; }
                string id = (string)n["id"], name = (string)n["name"];

                if (string.IsNullOrWhiteSpace(name)) Add(list, "error", "npc_name_missing", i, "look", "name", null, "An NPC has no name.");
                if (string.IsNullOrWhiteSpace(id)) Add(list, "error", "npc_id_missing", i, "pack", "id", null, $"'{name}' has no id.");
                else
                {
                    if (!IdPattern.IsMatch(id)) Add(list, "error", "npc_id_invalid", i, "pack", "id", id, $"Id '{id}' may only hold a-z, 0-9 and _.");
                    if (!seen.Add(id)) Add(list, "error", "npc_id_duplicate", i, "pack", "id", id, $"Two NPCs share the id '{id}'.");
                    if (known.TryGetValue(id, out var k) && k.source != "personnel")
                        Add(list, "error", "npc_id_taken", i, "pack", "id", id, $"Id '{id}' already belongs to {k.name} in the game.");
                }

                CheckFiles(list, p, n, i);
                CheckRole(list, n, i);
                CheckRelations(list, n, i, id, own, catalog == null ? null : known);
                if (doorless != null) CheckDoors(list, n, i, doorless);
            }
            return list;
        }

        private static void CheckFiles(List<Problem> list, PackProject p, JObject n, int i)
        {
            if (n["appearance"] is JObject a)
                foreach (string listName in new[] { "faceLayers", "bodyLayers" })
                    if (a[listName] is JArray layers)
                        foreach (var l in layers)
                        {
                            string file = (string)l?["file"];
                            if (!string.IsNullOrWhiteSpace(file) && !FileInProject(p, file))
                                Add(list, "error", "file_missing", i, "look", "appearance." + listName, file, $"Picture '{file}' is missing from the project.");
                        }
            string portrait = (string)n["portrait"];
            if (!string.IsNullOrWhiteSpace(portrait) && !FileInProject(p, portrait))
                Add(list, "error", "file_missing", i, "look", "portrait", portrait, $"Contact picture '{portrait}' is missing from the project.");
        }

        private static bool FileInProject(PackProject p, string rel)
        {
            try
            {
                string root = Path.GetFullPath(p.Dir) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(Path.Combine(p.Dir, rel.Replace('/', Path.DirectorySeparatorChar)));
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full);
            }
            catch (Exception e)
            {
                ProjectLog.Warn($"[problems] path '{rel}' could not be checked: {e.Message}");
                return false;
            }
        }

        /// <summary>Role the way Personnel resolves it: dealer, then supplier, then customer, then behavior.conversation.</summary>
        public static string Role(JObject n)
        {
            if (n["dealer"] is JObject) return "dealer";
            if (n["supplier"] is JObject) return "supplier";
            if (n["customer"] is JObject) return "customer";
            string conv = (string)n["behavior"]?["conversation"];
            if (string.Equals(conv, "customer", StringComparison.OrdinalIgnoreCase)) return "customer";
            if (string.Equals(conv, "dealer", StringComparison.OrdinalIgnoreCase)) return "dealer";
            return "civilian";
        }

        private static void CheckRole(List<Problem> list, JObject n, int i)
        {
            int blocks = (n["dealer"] is JObject ? 1 : 0) + (n["supplier"] is JObject ? 1 : 0) + (n["customer"] is JObject ? 1 : 0);
            string role = Role(n);
            if (blocks > 1) Add(list, "warning", "roles_conflict", i, "role", null, role, $"The NPC has more than one role block; it plays {role}.");

            if (role == "dealer" && string.IsNullOrWhiteSpace((string)n["dealer"]?["home"]))
                Add(list, "warning", "dealer_no_home", i, "role", "dealer.home", null, "The dealer has no home and has nowhere to stay.");

            if (role == "supplier")
            {
                if (!(n["supplier"]?["items"] is JArray items) || items.Count == 0)
                    Add(list, "warning", "supplier_no_items", i, "role", "supplier.items", null, "The supplier sells nothing.");
                if ((bool?)n["spawn"]?["physical"] == false)
                    Add(list, "warning", "supplier_not_physical", i, "routine", "spawn.physical", null, "A supplier always stands in the world; 'phone contact only' is ignored.");
            }
        }

        // Personnel skips such a step at runtime; a step that names its own door walks to that door and works.
        private static void CheckDoors(List<Problem> list, JObject n, int i, ISet<string> doorless)
        {
            if (n["schedule"] is JArray schedule)
                foreach (var step in schedule)
                {
                    string building = (string)step?["building"];
                    if (building != null && step["doorIndex"] == null && doorless.Contains(building)
                        && string.Equals((string)step["type"], "stayInBuilding", StringComparison.OrdinalIgnoreCase))
                        Add(list, "warning", "building_no_door", i, "routine", "schedule", building, "No NPC can enter this building; the step is skipped.");
                }

            string home = (string)n["dealer"]?["home"];
            if (home != null && doorless.Contains(home))
                Add(list, "warning", "building_no_door", i, "role", "dealer.home", home, "No NPC can enter the dealer's home.");
        }

        private static void CheckRelations(List<Problem> list, JObject n, int i, string id, HashSet<string> own, Dictionary<string, KnownNpc> known)
        {
            var rel = n["relationships"] as JObject;
            var conns = rel?["connections"] as JArray;
            int count = 0;
            if (conns != null)
                foreach (var c in conns)
                {
                    string cid = (string)c;
                    if (string.IsNullOrWhiteSpace(cid)) continue;
                    count++;
                    if (string.Equals(cid, id, StringComparison.OrdinalIgnoreCase))
                        Add(list, "warning", "connection_self", i, "relations", "relationships.connections", cid, "The NPC lists itself as a connection.");
                    else if (known != null && !own.Contains(cid) && !known.ContainsKey(cid))
                        Add(list, "warning", "connection_unknown", i, "relations", "relationships.connections", cid, $"Connection '{cid}' is not in the game or this pack.");
                }

            bool unlocked = (bool?)rel?["unlocked"] ?? false;
            if (Role(n) == "customer" && !unlocked && count == 0)
                Add(list, "warning", "customer_no_connection", i, "relations", "relationships.connections", null,
                    "A locked customer without connections never takes a sample.");
        }

        private static void Add(List<Problem> list, string severity, string code, int npc, string area, string field, string arg, string text)
            => list.Add(new Problem { severity = severity, code = code, npc = npc, area = area, field = field, arg = arg, text = text });
    }
}
