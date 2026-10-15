using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Personify.Editor.World
{
    /// <summary>One named place a schedule can point at: a building door, a car park, a seat set.</summary>
    public sealed class Place
    {
        [JsonProperty("kind")] public string Kind { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        /// <summary>Scene path, for seat sets that share a name.</summary>
        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)] public string Path { get; set; }
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("z")] public float Z { get; set; }

        [JsonIgnore] public Vector3 Position => new Vector3(X, Y, Z);

        /// <summary>The name as a player reads it. Seat sets carry raw object names ("OutdoorBench (1)").</summary>
        [JsonIgnore] public string Label => Pretty(Name);

        public static string Pretty(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string s = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+\)$", "").Replace('_', ' ').Trim();
            if (s.IndexOf(' ') >= 0) return s;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(s[i - 1])) { sb.Append(' '); c = char.ToLowerInvariant(c); }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// What the schedule map knows about the town: the map picture, how world positions land on it, the named places,
    /// and the walkable ground. The menu scene has none of this, so it is read out of a loaded save once
    /// (<see cref="WorldCapture"/>) and kept under UserData/Personify/World, tagged with the game version it came from.
    /// </summary>
    public sealed class WorldData
    {
        public const int FormatVersion = 1;

        [JsonProperty("format")] public int Format { get; set; } = FormatVersion;
        [JsonProperty("gameVersion")] public string GameVersion { get; set; } = "";
        [JsonProperty("capturedUtc")] public string CapturedUtc { get; set; } = "";

        /// <summary>World X/Z of the map picture's bottom-left and top-right corners.</summary>
        [JsonProperty("minX")] public float MinX { get; set; }
        [JsonProperty("minZ")] public float MinZ { get; set; }
        [JsonProperty("maxX")] public float MaxX { get; set; }
        [JsonProperty("maxZ")] public float MaxZ { get; set; }

        [JsonProperty("places")] public List<Place> Places { get; set; } = new List<Place>();

        /// <summary>Which region (Northtown, Westville, ...) each cell of a coarse grid over the map belongs to, as
        /// one byte per cell indexing <see cref="RegionNames"/>, base64. Feeds <c>spawn.region</c>.</summary>
        [JsonProperty("regionCell")] public float RegionCell { get; set; }
        [JsonProperty("regionW")] public int RegionW { get; set; }
        [JsonProperty("regionH")] public int RegionH { get; set; }
        [JsonProperty("regionNames")] public List<string> RegionNames { get; set; } = new List<string>();
        [JsonProperty("regions")] public string Regions { get; set; } = "";

        private byte[] _regionBytes;

        [JsonIgnore] public Texture2D Map { get; private set; }
        [JsonIgnore] public NavGround Ground { get; private set; }

        public static string Dir => System.IO.Path.Combine(Paths.Root, "World");
        private static string JsonFile => System.IO.Path.Combine(Dir, "world.json");
        private static string MapFile => System.IO.Path.Combine(Dir, "map.png");
        private static string NavFile => System.IO.Path.Combine(Dir, "ground.bin");

        private static WorldData _current;
        private static bool _loadTried;

        /// <summary>The captured world, loaded on first use. Null until a save was loaded once with Personify.</summary>
        public static WorldData Current
        {
            get
            {
                if (_current == null && !_loadTried) { _loadTried = true; _current = Load(); }
                return _current;
            }
        }

        /// <summary>Forget the cached copy so the next <see cref="Current"/> reads the files again.</summary>
        public static void Invalidate()
        {
            if (_current?.Map != null) UnityEngine.Object.Destroy(_current.Map);
            _current = null;
            _loadTried = false;
        }

        private static WorldData Load()
        {
            try
            {
                if (!File.Exists(JsonFile)) { Core.Log?.Msg("[world] no captured world yet (" + JsonFile + ")."); return null; }
                var w = JsonConvert.DeserializeObject<WorldData>(File.ReadAllText(JsonFile));
                if (w == null || w.Format != FormatVersion)
                {
                    Core.Log?.Warning("[world] world.json has another format - capture it again by loading a save.");
                    return null;
                }
                if (File.Exists(MapFile))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(tex, File.ReadAllBytes(MapFile))) { tex.wrapMode = TextureWrapMode.Clamp; w.Map = tex; }
                    else { Core.Log?.Warning("[world] map.png could not be decoded."); UnityEngine.Object.Destroy(tex); }
                }
                else Core.Log?.Warning("[world] map.png is missing - the map shows without its picture.");
                w.Ground = NavGround.Load(NavFile);
                Core.Log?.Msg($"[world] loaded: game {w.GameVersion}, {w.Places.Count} places, map {(w.Map != null ? w.Map.width + "x" + w.Map.height : "none")}, ground {(w.Ground != null ? w.Ground.WalkableCells + " walkable cells" : "none")}.");
                return w;
            }
            catch (Exception e)
            {
                Core.Log?.Error("[world] loading the captured world failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Writes the capture. <paramref name="mapPng"/> may be null when the picture could not be read.</summary>
        public static bool Save(WorldData w, byte[] mapPng, NavGround ground)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(JsonFile, JsonConvert.SerializeObject(w, Formatting.Indented));
                if (mapPng != null) File.WriteAllBytes(MapFile, mapPng);
                ground?.Save(NavFile);
                Invalidate();
                Core.Log?.Msg($"[world] saved capture: {w.Places.Count} places, map {(mapPng != null ? mapPng.Length / 1024 + " KB" : "missing")}, ground {(ground != null ? ground.WalkableCells + " walkable cells" : "missing")} -> {Dir}");
                return true;
            }
            catch (Exception e)
            {
                Core.Log?.Error("[world] saving the capture failed: " + e.Message);
                return false;
            }
        }

        // ---- map <-> world ------------------------------------------------------------------------------------

        /// <summary>World position to 0..1 map coordinates (0,0 = bottom-left).</summary>
        public Vector2 ToMap(Vector3 world) => new Vector2(
            Mathf.InverseLerp(MinX, MaxX, world.x),
            Mathf.InverseLerp(MinZ, MaxZ, world.z));

        /// <summary>0..1 map coordinates to world X/Z (Y is left at 0 - see <see cref="Ground"/>).</summary>
        public Vector3 ToWorld(Vector2 map) => new Vector3(
            Mathf.LerpUnclamped(MinX, MaxX, map.x), 0f,
            Mathf.LerpUnclamped(MinZ, MaxZ, map.y));

        /// <summary>
        /// The walkable spot nearest to a map click, with its real height. Falls back to the flat X/Z position when the
        /// ground was not captured; the NPC then finds its own way to the closest reachable point.
        /// </summary>
        public Vector3 SnapToGround(Vector3 world, out bool snapped)
        {
            snapped = false;
            if (Ground == null) return world;
            if (Ground.TryNearest(world.x, world.z, out Vector3 hit)) { snapped = true; return hit; }
            return world;
        }

        public Place NearestPlace(Vector3 world, string kind, float maxDistance)
        {
            Place best = null;
            float bestD = maxDistance * maxDistance;
            foreach (Place p in Places)
            {
                if (kind != null && p.Kind != kind) continue;
                float dx = p.X - world.x, dz = p.Z - world.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>A place of one kind by its scene path (seats) or name, or null.</summary>
        public Place FindPlace(string kind, string name, string path = null)
        {
            if (!string.IsNullOrEmpty(path))
            {
                Place byPath = Places.Find(p => p.Kind == kind && string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
                if (byPath != null) return byPath;
            }
            if (string.IsNullOrEmpty(name)) return null;
            return Places.Find(p => p.Kind == kind && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The region name at a world position, or null when the grid was not captured.</summary>
        public string RegionAt(Vector3 world)
        {
            if (RegionCell <= 0f || RegionW <= 0 || RegionH <= 0 || string.IsNullOrEmpty(Regions)) return null;
            if (_regionBytes == null)
            {
                try { _regionBytes = Convert.FromBase64String(Regions); }
                catch (FormatException e) { Core.Log?.Warning("[world] the region grid is damaged: " + e.Message); Regions = ""; return null; }
            }
            int cx = Mathf.Clamp(Mathf.FloorToInt((world.x - MinX) / RegionCell), 0, RegionW - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((world.z - MinZ) / RegionCell), 0, RegionH - 1);
            int i = cz * RegionW + cx;
            if (i >= _regionBytes.Length) return null;
            int r = _regionBytes[i];
            return r < RegionNames.Count ? RegionNames[r] : null;
        }

        /// <summary>"near Hyland Bank" style label for a point, or null when nothing named is close.</summary>
        public string NearestPlaceName(Vector3 world)
        {
            Place p = NearestPlace(world, null, 25f);
            return p == null ? null : "near " + p.Label;
        }
    }
}
