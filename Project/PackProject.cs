using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Personify.Project
{
    /// <summary>
    /// A Personify project in format 2: a Personnel pack folder that the editor owns. <c>manifest.json</c> is a
    /// regular Personnel manifest (schemaVersion 3) so every field Personnel knows, and every field it does not
    /// know yet, survives editing untouched. Editor-only data sits in <c>personify</c> blocks (one on the pack,
    /// one per NPC) that are stripped on install. Pictures live next to the manifest, editor files under
    /// <c>.personify/</c> (history, migration backup).
    /// </summary>
    public sealed class PackProject
    {
        public const int FormatVersion = 2;
        public const int SchemaVersion = 3;
        public const string ManifestFile = "manifest.json";
        public const string EditorKey = "personify";
        public const string EditorDir = ".personify";

        /// <summary>Folder name under the projects root (also the project's stable key).</summary>
        public string Folder { get; set; }

        /// <summary>Absolute project folder.</summary>
        public string Dir { get; set; }

        /// <summary>The pack manifest including the editor blocks.</summary>
        public JObject Manifest { get; set; }

        public string Name
        {
            get => (string)Manifest["name"] ?? Folder;
            set => Manifest["name"] = value;
        }

        public JArray Npcs
        {
            get
            {
                if (!(Manifest["npcs"] is JArray a)) { a = new JArray(); Manifest["npcs"] = a; }
                return a;
            }
        }

        /// <summary>Pack-level editor block (publish metadata, install bookkeeping); created on first use.</summary>
        public JObject Editor => EditorBlock(Manifest);

        /// <summary>The editor block of a manifest object (pack or NPC), created on first use.</summary>
        public static JObject EditorBlock(JObject owner)
        {
            if (!(owner[EditorKey] is JObject b)) { b = new JObject(); owner[EditorKey] = b; }
            return b;
        }

        /// <summary>The NPC with this id, or null.</summary>
        public JObject FindNpc(string id)
        {
            foreach (var t in Npcs)
                if (t is JObject o && string.Equals((string)o["id"], id, StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }

        /// <summary>Every NPC id in the project.</summary>
        public HashSet<string> NpcIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Npcs)
                if (t is JObject o && !string.IsNullOrWhiteSpace((string)o["id"])) ids.Add((string)o["id"]);
            return ids;
        }

        /// <summary>
        /// A fresh pack manifest: name, author, schemaVersion, empty NPC list and the editor block with defaults.
        /// </summary>
        public static JObject NewManifest(string name, string author)
        {
            var m = new JObject
            {
                ["name"] = name,
                ["author"] = author ?? "",
                ["schemaVersion"] = SchemaVersion,
                ["npcs"] = new JArray(),
            };
            var e = EditorBlock(m);
            e["formatVersion"] = FormatVersion;
            e["modVersion"] = "1.0.0";
            e["description"] = "";
            e["websiteUrl"] = "";
            e["license"] = "All rights reserved";
            return m;
        }

        /// <summary>
        /// Deep copy of the manifest without editor blocks: what Personnel reads. Also raises schemaVersion to the
        /// version the editor writes, since the editor may have added schema 3 fields.
        /// </summary>
        public JObject InstallManifest()
        {
            var m = (JObject)Manifest.DeepClone();
            m.Remove(EditorKey);
            if (m["npcs"] is JArray a)
                foreach (var t in a)
                    if (t is JObject o) o.Remove(EditorKey);
            int v = (int?)m["schemaVersion"] ?? 1;
            if (v < SchemaVersion) m["schemaVersion"] = SchemaVersion;
            // The editor shows "World NPC" as on while autoRegister is unset, but Personnel reads a missing value as
            // false: an NPC without its own spawn.auto would install and never appear. Write the shown state.
            if (m["autoRegister"] == null || m["autoRegister"].Type == JTokenType.Null) m["autoRegister"] = true;
            return m;
        }
    }
}
