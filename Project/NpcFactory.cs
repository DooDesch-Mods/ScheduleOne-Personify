using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Personify.Editor;

namespace Personify.Project
{
    /// <summary>
    /// New NPCs and role switches with values that play in the game right away: a customer gets a buying profile and
    /// a connection, a dealer a home, a supplier something to sell. Defaults are the vanilla ones where the game has
    /// them (DealerNPCData: cut 0.2, signing fee 500; CustomerData: spending 200-500, 1-5 orders a week).
    /// </summary>
    public static class NpcFactory
    {
        public static readonly string[] Roles = { "civilian", "customer", "dealer", "supplier" };

        /// <summary>Hints from the loaded save that make defaults fit the town; any field may be null.</summary>
        public sealed class Hints
        {
            /// <summary>A vanilla NPC a new customer can be connected to.</summary>
            public string Connection;
            /// <summary>A building name a new dealer can live in.</summary>
            public string DealerHome;
        }

        /// <summary>A free id for <paramref name="name"/> inside the project: pack-prefixed, a number appended on a clash.</summary>
        public static string FreeId(PackProject p, string name)
        {
            string baseId = Ids.Make(p.Name, string.IsNullOrWhiteSpace(name) ? "npc" : name);
            var taken = p.NpcIds();
            if (!taken.Contains(baseId)) return baseId;
            for (int i = 2; ; i++)
                if (!taken.Contains(baseId + "_" + i)) return baseId + "_" + i;
        }

        public static JObject Create(PackProject p, string name, string role, JObject appearance, Hints hints)
        {
            var n = new JObject
            {
                ["id"] = FreeId(p, name),
                ["name"] = string.IsNullOrWhiteSpace(name) ? "New NPC" : name.Trim(),
            };
            if (appearance != null) n["appearance"] = appearance.DeepClone();
            SetRole(n, role, hints);
            return n;
        }

        /// <summary>
        /// Makes <paramref name="n"/> play <paramref name="role"/>: removes the other role blocks and fills the new one
        /// with defaults. Fields of a block that is kept stay as they are.
        /// </summary>
        public static void SetRole(JObject n, string role, Hints hints)
        {
            role = (role ?? "civilian").ToLowerInvariant();
            if (Array.IndexOf(Roles, role) < 0)
            {
                ProjectLog.Warn($"[edit] unknown role '{role}' for '{(string)n["id"]}' - civilian used.");
                role = "civilian";
            }
            foreach (string r in new[] { "customer", "dealer", "supplier" })
                if (r != role) n.Remove(r);
            if (n["behavior"] is JObject b)
            {
                b.Remove("conversation");
                if (!b.HasValues) n.Remove("behavior");
            }

            var rel = n["relationships"] as JObject;
            switch (role)
            {
                case "customer":
                    if (!(n["customer"] is JObject))
                        n["customer"] = new JObject
                        {
                            ["spending"] = new JObject { ["min"] = 200, ["max"] = 500 },
                            ["ordersPerWeek"] = new JObject { ["min"] = 1, ["max"] = 5 },
                            ["standards"] = "Moderate",
                            ["allowDirectApproach"] = true,
                        };
                    if (rel == null) { rel = new JObject(); n["relationships"] = rel; }
                    if (rel["delta"] == null) rel["delta"] = 1;
                    if (rel["unlockType"] == null) rel["unlockType"] = "Recommendation";
                    if (!(rel["connections"] is JArray conns) || conns.Count == 0)
                    {
                        if (!string.IsNullOrEmpty(hints?.Connection)) rel["connections"] = new JArray(hints.Connection);
                        else ProjectLog.Info($"[edit] '{(string)n["id"]}': no save loaded yet, so the new customer has no connection.");
                    }
                    break;
                case "dealer":
                    if (!(n["dealer"] is JObject))
                    {
                        var d = new JObject { ["type"] = "player", ["cut"] = 0.2, ["signingFee"] = 500 };
                        if (!string.IsNullOrEmpty(hints?.DealerHome)) d["home"] = hints.DealerHome;
                        n["dealer"] = d;
                    }
                    break;
                case "supplier":
                    if (!(n["supplier"] is JObject))
                        n["supplier"] = new JObject
                        {
                            ["items"] = new JArray("ogkushseed"),
                            ["minOrderLimit"] = 100,
                            ["maxOrderLimit"] = 500,
                        };
                    if (n["spawn"] is JObject s && (bool?)s["physical"] == false) s["physical"] = true;
                    break;
            }
        }

        /// <summary>Copy of an NPC under a new id and name; the editor block comes along, the install bookkeeping does not.</summary>
        public static JObject Duplicate(PackProject p, JObject src)
        {
            var n = (JObject)src.DeepClone();
            string name = ((string)src["name"] ?? "NPC") + " 2";
            n["name"] = name;
            n["id"] = FreeId(p, name);
            n.Remove("saveId");
            return n;
        }

        /// <summary>Removes NPCs by id; connections pointing at them are removed from the remaining NPCs.</summary>
        public static int Remove(PackProject p, IReadOnlyCollection<string> ids)
        {
            var gone = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            for (int i = p.Npcs.Count - 1; i >= 0; i--)
                if (p.Npcs[i] is JObject o && gone.Contains((string)o["id"] ?? "")) { p.Npcs.RemoveAt(i); removed++; }
            foreach (var t in p.Npcs)
                if (t?["relationships"]?["connections"] is JArray c)
                    for (int i = c.Count - 1; i >= 0; i--)
                        if (gone.Contains((string)c[i] ?? "")) c.RemoveAt(i);
            return removed;
        }
    }
}
