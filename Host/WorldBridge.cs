using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Personify.Editor.World;
using UnityEngine;

namespace Personify.Host
{
    /// <summary>The map, places and NPC catalog for the Routine and Relations areas.</summary>
    internal static class WorldBridge
    {
        internal const string MapImage = "personify-map";
        private static bool _mapSent;

        internal static void Reset() => _mapSent = false;

        internal static JObject Catalog()
        {
            var w = WorldData.Current;
            var npcs = GameData.Npcs();
            return new JObject
            {
                ["hasWorld"] = w != null,
                ["hasNpcs"] = npcs != null,
                ["buildings"] = new JArray((w?.Places.Where(p => p.Kind == "building").Select(p => p.Name).Distinct().OrderBy(n => n) ?? Enumerable.Empty<string>()).ToArray<object>()),
                ["regions"] = new JArray((w?.RegionNames ?? new System.Collections.Generic.List<string>()).ToArray<object>()),
                ["npcs"] = new JArray((npcs ?? new System.Collections.Generic.List<GameData.CatalogNpc>()).Select(n => new JObject
                {
                    ["id"] = n.id, ["name"] = n.name, ["region"] = n.region, ["role"] = n.role, ["source"] = n.source,
                })),
                ["items"] = GameData.Items(),
            };
        }

        internal static JObject Map()
        {
            var w = WorldData.Current;
            if (w == null) return new JObject { ["hasMap"] = false };
            if (!_mapSent)
            {
                string file = Path.Combine(WorldData.Dir, "map.png");
                if (File.Exists(file))
                {
                    try
                    {
                        EditorHost.PublishImage(MapImage, File.ReadAllBytes(file));
                        _mapSent = true;
                        Core.Log.Msg($"[world] map picture sent to the page ({file}).");
                    }
                    catch (Exception e) { Core.Log.Error($"[world] reading the map picture {file} failed: {e.Message}"); }
                }
                else Core.Log.Warning($"[world] no map picture at {file} - the map stays empty until a save is loaded.");
            }
            return new JObject
            {
                ["hasMap"] = _mapSent,
                ["image"] = "s1://" + MapImage,
                ["minX"] = w.MinX, ["minZ"] = w.MinZ, ["maxX"] = w.MaxX, ["maxZ"] = w.MaxZ,
                ["places"] = new JArray(w.Places.Select(p => new JObject
                {
                    ["kind"] = p.Kind, ["name"] = string.IsNullOrEmpty(p.Path) ? p.Name : p.Path, ["label"] = p.Label,
                    ["x"] = Math.Round(p.X, 2), ["y"] = Math.Round(p.Y, 2), ["z"] = Math.Round(p.Z, 2),
                })),
                ["vehicles"] = new JArray(w.Vehicles.Select(v => new JObject { ["code"] = v.Code, ["label"] = v.Label })),
            };
        }

        internal static JObject Ground(JObject a)
        {
            var w = WorldData.Current;
            if (w == null) return Calls.Fail("no_world");
            float x = (float?)a["x"] ?? 0f, z = (float?)a["z"] ?? 0f;
            Vector3 p = w.SnapToGround(new Vector3(x, 0f, z), out bool snapped);
            return new JObject
            {
                ["ok"] = true,
                ["x"] = Math.Round(p.x, 2), ["y"] = Math.Round(p.y, 2), ["z"] = Math.Round(p.z, 2),
                ["region"] = w.RegionAt(p) ?? "",
                ["walkable"] = snapped,
            };
        }
    }
}
