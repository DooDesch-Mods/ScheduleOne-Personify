using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader.Utils;
using Newtonsoft.Json.Linq;
using Personify.Editor;
using Personify.Editor.World;
using Personify.Project;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;

namespace Personify.Host
{
    /// <summary>What the editor knows about the game outside the project: the NPC catalog Personnel writes on save
    /// load, the item list, Personnel's folders and version.</summary>
    internal static class GameData
    {
        internal static string PacksRoot => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "Packs");
        internal static string DisabledRoot => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "Disabled");
        internal static string NpcCatalogFile => Path.Combine(Paths.Root, "World", "npcs.json");

        internal sealed class CatalogNpc
        {
            public string id, name, region, role, source;
        }

        private static List<CatalogNpc> _npcs;
        private static DateTime _npcsStamp;

        /// <summary>NPCs of the last loaded save; null when no save was loaded with Personnel 2.3+ yet.</summary>
        internal static List<CatalogNpc> Npcs()
        {
            try
            {
                if (!File.Exists(NpcCatalogFile)) return null;
                var stamp = File.GetLastWriteTimeUtc(NpcCatalogFile);
                if (_npcs != null && stamp == _npcsStamp) return _npcs;
                var o = JObject.Parse(File.ReadAllText(NpcCatalogFile));
                _npcs = (o["npcs"] as JArray ?? new JArray()).OfType<JObject>().Select(n => new CatalogNpc
                {
                    id = (string)n["id"] ?? "", name = (string)n["name"] ?? "", region = (string)n["region"] ?? "",
                    role = (string)n["role"] ?? "", source = (string)n["source"] ?? "",
                }).Where(n => n.id.Length > 0).ToList();
                _npcsStamp = stamp;
                Core.Log.Msg($"[world] NPC catalog read: {_npcs.Count} NPC(s) from {NpcCatalogFile}.");
                return _npcs;
            }
            catch (Exception e)
            {
                Core.Log.Error($"[world] reading the NPC catalog {NpcCatalogFile} failed: {e.Message}");
                return null;
            }
        }

        /// <summary>The catalog in the shape the problem check takes; null without a catalog.</summary>
        internal static List<KnownNpc> KnownNpcs()
            => Npcs()?.Select(n => new KnownNpc { id = n.id, name = n.name, source = n.source }).ToList();

        /// <summary>Defaults for a new NPC: a vanilla customer as the first connection, a motel room as dealer home.</summary>
        internal static NpcFactory.Hints Hints()
        {
            var h = new NpcFactory.Hints();
            var customers = Npcs()?.Where(n => n.source == "vanilla" && n.role == "customer").ToList();
            if (customers != null && customers.Count > 0) h.Connection = customers[UnityEngine.Random.Range(0, customers.Count)].id;
            var world = WorldData.Current;
            var motel = world?.Places.FirstOrDefault(p => p.Kind == "building" && p.Name.IndexOf("Motel", StringComparison.OrdinalIgnoreCase) >= 0);
            if (motel != null) h.DealerHome = motel.Name;
            return h;
        }

        private static JArray _items;

        /// <summary>Every item the game registers, for the supplier's goods: [{id,name,category}].</summary>
        internal static JArray Items()
        {
            if (_items != null && _items.Count > 0) return _items;
            var list = new JArray();
            try
            {
                if (!PersistentSingleton<Registry>.InstanceExists) { Core.Log.Warning("[world] the item registry is not up yet - the item list is empty."); return list; }
                var all = PersistentSingleton<Registry>.Instance.GetAllItems();
                for (int i = 0; i < all.Count; i++)
                {
                    var d = all[i];
                    if (d == null || string.IsNullOrEmpty(d.ID)) continue;
                    list.Add(new JObject { ["id"] = d.ID, ["name"] = d.Name ?? d.ID, ["category"] = d.Category.ToString() });
                }
                Core.Log.Msg($"[world] item list read: {list.Count} item(s).");
            }
            catch (Exception e) { Core.Log.Error("[world] reading the item registry failed: " + e.Message); }
            _items = list;
            return list;
        }

        private static JArray _effects;

        /// <summary>Product effects a customer can favour: [{id,name,tier}], from the game's tier folders.</summary>
        internal static JArray Effects()
        {
            if (_effects != null && _effects.Count > 0) return _effects;
            var list = new JArray();
            try
            {
                for (int tier = 1; tier <= 5; tier++)
                {
                    var found = UnityEngine.Resources.LoadAll<Il2CppScheduleOne.Effects.Effect>("Properties/Tier" + tier);
                    if (found == null) continue;
                    foreach (var e in found)
                        if (e != null && !string.IsNullOrEmpty(e.ID))
                            list.Add(new JObject { ["id"] = e.ID, ["name"] = e.Name ?? e.ID, ["tier"] = e.Tier });
                }
                if (list.Count == 0) Core.Log.Warning("[world] no product effects found under Resources/Properties/Tier1-5.");
                else Core.Log.Msg($"[world] effect list read: {list.Count} effect(s).");
            }
            catch (Exception e) { Core.Log.Error("[world] reading the product effects failed: " + e.Message); }
            _effects = list;
            return list;
        }

        private static string _personnelVersion;

        /// <summary>Personnel's version, read at runtime so Personify does not reference Personnel; null when not loaded.</summary>
        internal static string PersonnelVersion()
        {
            if (_personnelVersion != null) return _personnelVersion.Length == 0 ? null : _personnelVersion;
            _personnelVersion = "";
            try
            {
                foreach (var m in MelonLoader.MelonMod.RegisteredMelons)
                    if (m?.Info?.Name == "Personnel") { _personnelVersion = m.Info.Version ?? ""; break; }
                if (_personnelVersion.Length == 0) Core.Log.Warning("[editor] Personnel is not loaded - installing and testing need it.");
            }
            catch (Exception e) { Core.Log.Error("[editor] reading Personnel's version failed: " + e.Message); }
            return _personnelVersion.Length == 0 ? null : _personnelVersion;
        }

        /// <summary>A public static method of a Personnel type, found by name; null after logging when missing.</summary>
        internal static MethodInfo PersonnelMethod(string type, string method)
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "Personnel") continue;
                    var t = asm.GetType(type, false);
                    var mi = t?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
                    if (mi != null) return mi;
                    Core.Log.Error($"[editor] Personnel has no {type}.{method} - Personnel 2.3 or newer is needed.");
                    return null;
                }
                Core.Log.Error("[editor] Personnel is not loaded.");
            }
            catch (Exception e) { Core.Log.Error($"[editor] looking up {type}.{method} failed: {e.Message}"); }
            return null;
        }
    }
}
