using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using AvatarSeatSet = Il2CppScheduleOne.AvatarFramework.Animation.AvatarSeatSet;
using GameMap = Il2CppScheduleOne.Map.Map;
using MapApp = Il2CppScheduleOne.UI.Phone.Map.MapApp;
using MapPositionUtility = Il2CppScheduleOne.Map.MapPositionUtility;
using NPCEnterableBuilding = Il2CppScheduleOne.Map.NPCEnterableBuilding;
using ParkingLot = Il2CppScheduleOne.Map.ParkingLot;

namespace Personify.Editor.World
{
    /// <summary>
    /// Reads what the schedule map needs out of a loaded save: the phone map's picture, where it sits in the world,
    /// every building, car park and seat set an NPC can be sent to, and the walkable ground. The editor runs in the
    /// main menu, where none of this exists, so the capture runs once in the background after a save has loaded and
    /// is kept on disk (<see cref="WorldData"/>). A new game version captures again.
    /// </summary>
    public static class WorldCapture
    {
        // Buildings the game uses for hidden NPC bookkeeping; a schedule must not send anyone there.
        private static readonly HashSet<string> HiddenBuildings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NPC Hiding Building",
        };

        private const int MapSize = 2048;
        private static bool _running;

        public static bool Running => _running;

        /// <summary>Called when the Main scene loads. Captures once per game version, unless <paramref name="force"/>.</summary>
        public static void OnMainScene(bool force = false)
        {
            if (_running) { Core.Log?.Msg("[world] a capture is already running."); return; }
            WorldData have = WorldData.Current;
            if (!force && have != null && have.GameVersion == Application.version && have.Ground != null && have.Map != null)
            {
                Core.Log?.Msg($"[world] capture for game {have.GameVersion} is present - skipping.");
                return;
            }
            MelonCoroutines.Start(Run());
        }

        private static IEnumerator Run()
        {
            _running = true;
            float deadline = Time.realtimeSinceStartup + 300f;
            while (!SaveIsUp())
            {
                if (Time.realtimeSinceStartup > deadline) { Core.Log?.Warning("[world] the save never finished loading - no capture."); _running = false; yield break; }
                yield return new WaitForSeconds(1f);
            }
            yield return new WaitForSeconds(5f);   // let the town settle: doors, car parks and seats register in Start

            WorldData w = null;
            byte[] png = null;
            try
            {
                w = new WorldData
                {
                    GameVersion = Application.version,
                    CapturedUtc = DateTime.UtcNow.ToString("o"),
                };
                if (!ReadExtent(w)) { _running = false; yield break; }
                png = ReadMapPicture();
                ReadPlaces(w);
                ReadRegions(w);
            }
            catch (Exception e)
            {
                Core.Log?.Error("[world] capture failed: " + e);
                _running = false;
                yield break;
            }

            NavGround ground = null;
            yield return NavGround.Sample(w.MinX, w.MinZ, w.MaxX, w.MaxZ, NavGround.DefaultCell, g => ground = g);
            if (ground == null) Core.Log?.Warning("[world] no walkable ground found - map clicks keep a flat height.");

            WorldData.Save(w, png, ground);
            _running = false;
        }

        private static bool SaveIsUp()
        {
            if (!PersistentSingleton<LoadManager>.InstanceExists) return false;
            LoadManager lm = PersistentSingleton<LoadManager>.Instance;
            return lm.IsGameLoaded && !lm.IsLoading;
        }

        // The phone map draws the town around MapPositionUtility's origin: the picture spans MapDimensions map units,
        // and Distance(origin, edge) world metres equal half of that.
        private static bool ReadExtent(WorldData w)
        {
            if (!Singleton<MapPositionUtility>.InstanceExists) { Core.Log?.Error("[world] MapPositionUtility is missing - no capture."); return false; }
            MapPositionUtility u = Singleton<MapPositionUtility>.Instance;
            if (u.OriginPoint == null || u.EdgePoint == null) { Core.Log?.Error("[world] the map origin or edge point is missing - no capture."); return false; }
            Vector3 o = u.OriginPoint.position;
            float half = Vector3.Distance(o, u.EdgePoint.position);
            if (half < 10f) { Core.Log?.Error($"[world] the map spans only {half:0.0} m - no capture."); return false; }
            w.MinX = o.x - half; w.MaxX = o.x + half;
            w.MinZ = o.z - half; w.MaxZ = o.z + half;
            Core.Log?.Msg($"[world] map extent X {w.MinX:0.00}..{w.MaxX:0.00}, Z {w.MinZ:0.00}..{w.MaxZ:0.00} (origin {o.x:0.00}/{o.z:0.00}, half {half:0.00} m).");
            return true;
        }

        // The sprite is not CPU-readable, so it is drawn into a render texture and read back from there.
        private static byte[] ReadMapPicture()
        {
            if (!PlayerSingleton<MapApp>.InstanceExists) { Core.Log?.Warning("[world] the phone map app is missing - the map has no picture."); return null; }
            Sprite sprite = PlayerSingleton<MapApp>.Instance.MainMapSprite;
            if (sprite == null || sprite.texture == null) { Core.Log?.Warning("[world] the phone map has no sprite - the map has no picture."); return null; }

            Texture2D src = sprite.texture;
            Rect r = sprite.textureRect;
            var scale = new Vector2(r.width / src.width, r.height / src.height);
            var offset = new Vector2(r.x / src.width, r.y / src.height);

            RenderTexture rt = RenderTexture.GetTemporary(MapSize, MapSize, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Texture2D read = null;
            try
            {
                Graphics.Blit(src, rt, scale, offset);
                RenderTexture.active = rt;
                read = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, MapSize, MapSize), 0, 0);
                read.Apply();
                byte[] png = ImageConversion.EncodeToPNG(read);
                Core.Log?.Msg($"[world] map picture read: '{sprite.name}' {r.width}x{r.height} -> {MapSize}x{MapSize}, {png.Length / 1024} KB.");
                return png;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) UnityEngine.Object.Destroy(read);
            }
        }

        private static void ReadPlaces(WorldData w)
        {
            int buildings = 0, lots = 0, seats = 0;

            foreach (NPCEnterableBuilding b in UnityEngine.Object.FindObjectsOfType<NPCEnterableBuilding>(true))
            {
                if (b == null || string.IsNullOrWhiteSpace(b.BuildingName) || HiddenBuildings.Contains(b.BuildingName)) continue;
                Vector3 at = b.transform.position;
                var doors = b.Doors;
                if (doors != null && doors.Length > 0 && doors[0] != null)
                    at = doors[0].AccessPoint != null ? doors[0].AccessPoint.position : doors[0].transform.position;
                w.Places.Add(new Place { Kind = "building", Name = b.BuildingName, X = at.x, Y = at.y, Z = at.z });
                buildings++;
            }

            foreach (ParkingLot l in UnityEngine.Object.FindObjectsOfType<ParkingLot>(true))
            {
                if (l == null) continue;
                Vector3 at = l.EntryPoint != null ? l.EntryPoint.position : l.transform.position;
                w.Places.Add(new Place { Kind = "parking", Name = l.gameObject.name, X = at.x, Y = at.y, Z = at.z });
                lots++;
            }

            foreach (AvatarSeatSet s in UnityEngine.Object.FindObjectsOfType<AvatarSeatSet>(true))
            {
                if (s == null) continue;
                Vector3 at = s.transform.position;
                w.Places.Add(new Place { Kind = "seat", Name = s.gameObject.name, Path = ScenePath(s.transform), X = at.x, Y = at.y, Z = at.z });
                seats++;
            }

            w.Places.Sort((a, b) => string.Compare(a.Kind + a.Name, b.Kind + b.Name, StringComparison.OrdinalIgnoreCase));
            Core.Log?.Msg($"[world] places: {buildings} buildings, {lots} car parks, {seats} seat sets.");
        }

        private const float RegionCell = 8f;

        // Byte value of a grid cell that lies in no region (open country, water).
        private const byte NoRegion = byte.MaxValue;

        // A coarse grid of the game's region polygons is enough to label a spawn point with the region its NPC
        // belongs to. The polygons are asked directly: Map.GetRegionFromPosition answers Northtown for any point
        // outside every region and logs an error each time.
        private static void ReadRegions(WorldData w)
        {
            if (!Singleton<GameMap>.InstanceExists) { Core.Log?.Warning("[world] the town map is missing - spawn points get no region."); return; }
            var regions = Singleton<GameMap>.Instance.Regions;
            if (regions == null || regions.Length == 0) { Core.Log?.Warning("[world] the town map lists no regions - spawn points get no region."); return; }
            int cw = Mathf.CeilToInt((w.MaxX - w.MinX) / RegionCell), ch = Mathf.CeilToInt((w.MaxZ - w.MinZ) / RegionCell);
            var bytes = new byte[cw * ch];
            var index = new Dictionary<string, int>();
            int outside = 0, unbounded = 0;
            for (int z = 0; z < ch; z++)
                for (int x = 0; x < cw; x++)
                {
                    var at = new Vector3(w.MinX + (x + 0.5f) * RegionCell, 0f, w.MinZ + (z + 0.5f) * RegionCell);
                    byte cell = NoRegion;
                    for (int r = 0; r < regions.Length; r++)
                    {
                        var data = regions[r];
                        if (data == null || data.RegionBounds == null) { unbounded++; continue; }
                        if (!data.RegionBounds.IsPointInsidePolygon(at)) continue;
                        string name = data.Region.ToString();
                        if (!index.TryGetValue(name, out int i)) { i = index.Count; index[name] = i; w.RegionNames.Add(name); }
                        cell = (byte)i;
                        break;
                    }
                    if (cell == NoRegion) outside++;
                    bytes[z * cw + x] = cell;
                }
            if (unbounded > 0) Core.Log?.Warning($"[world] {unbounded / (cw * ch)} region(s) have no outline and were skipped.");
            w.RegionCell = RegionCell; w.RegionW = cw; w.RegionH = ch;
            w.Regions = Convert.ToBase64String(bytes);
            Core.Log?.Msg($"[world] regions: {string.Join(", ", w.RegionNames)} on a {cw}x{ch} grid, {outside} cells outside any region.");
        }

        private static string ScenePath(Transform t)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
