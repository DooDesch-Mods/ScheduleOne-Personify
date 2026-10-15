using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace Personify.Editor.World
{
    /// <summary>
    /// The town's walkable ground as a height grid, sampled from the NavMesh while a save is loaded. The map shows the
    /// town from above and a click has no height; this finds the walkable spot under (or next to) the click, so a
    /// schedule point lands on a street rather than inside a wall or under the map.
    ///
    /// The IL2CPP build strips NavMesh.CalculateTriangulation, so each grid cell is found the long way: a ray from
    /// above finds the surfaces at that spot, and the first one the NavMesh agrees is walkable gives the height.
    /// Ground under a roof is not reachable that way; schedule steps inside buildings use the building instead.
    /// </summary>
    public sealed class NavGround
    {
        private const int Magic = 0x4E415648;   // "NAVH"
        public const float DefaultCell = 2f;
        private const float RayTop = 150f;
        private const float RayLength = 300f;
        private const float SnapTolerance = 1.2f;   // how far the NavMesh point may sit from the ray hit, horizontally
        private const int SearchCells = 6;           // how far a click off the ground may sit and still snap

        private readonly float _minX, _minZ, _cell;
        private readonly int _w, _h;
        private readonly float[] _y;   // NaN = nothing walkable in that cell

        public int Width => _w;
        public int Height => _h;
        public int WalkableCells { get; }

        private NavGround(float minX, float minZ, float cell, int w, int h, float[] y)
        {
            _minX = minX; _minZ = minZ; _cell = cell; _w = w; _h = h; _y = y;
            int n = 0;
            foreach (float v in y) if (!float.IsNaN(v)) n++;
            WalkableCells = n;
        }

        /// <summary>
        /// Samples the loaded scene over the given rectangle, a few rows per frame so the game keeps running, and
        /// hands the result to <paramref name="done"/> (null when nothing walkable was found).
        /// </summary>
        public static IEnumerator Sample(float minX, float minZ, float maxX, float maxZ, float cell, Action<NavGround> done)
        {
            int w = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / cell));
            int h = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / cell));
            var y = new float[w * h];
            float started = Time.realtimeSinceStartup;
            int rowsPerFrame = Mathf.Max(1, 4000 / w);
            for (int row = 0; row < h; row++)
            {
                float z = minZ + (row + 0.5f) * cell;
                for (int col = 0; col < w; col++)
                    y[row * w + col] = SampleAt(minX + (col + 0.5f) * cell, z);
                if (row % rowsPerFrame == rowsPerFrame - 1) yield return null;
            }
            var ground = new NavGround(minX, minZ, cell, w, h, y);
            Core.Log?.Msg($"[world] ground sampled: {w}x{h} cells of {cell} m, {ground.WalkableCells} walkable, {Time.realtimeSinceStartup - started:0.0} s.");
            done(ground.WalkableCells > 0 ? ground : null);
        }

        private static float SampleAt(float x, float z)
        {
            var raw = Physics.RaycastAll(new Vector3(x, RayTop, z), Vector3.down, RayLength, ~0, QueryTriggerInteraction.Ignore);
            if (raw == null || raw.Length == 0) return float.NaN;
            var hits = new RaycastHit[raw.Length];
            for (int i = 0; i < hits.Length; i++) hits[i] = raw[i];
            Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));
            foreach (RaycastHit hit in hits)
            {
                if (!NavMesh.SamplePosition(hit.point, out NavMeshHit nav, 1.5f, NavMesh.AllAreas)) continue;
                float dx = nav.position.x - x, dz = nav.position.z - z;
                if (dx * dx + dz * dz <= SnapTolerance * SnapTolerance) return nav.position.y;
            }
            return float.NaN;
        }

        /// <summary>
        /// The walkable point for a map click at world X/Z: the cell under the click, or the nearest walkable cell
        /// around it. False when the click is far from any ground the NavMesh knows.
        /// </summary>
        public bool TryNearest(float x, float z, out Vector3 hit)
        {
            hit = default;
            int cx = Mathf.FloorToInt((x - _minX) / _cell), cz = Mathf.FloorToInt((z - _minZ) / _cell);
            float here = At(cx, cz);
            if (!float.IsNaN(here)) { hit = new Vector3(x, here, z); return true; }

            float bestD = float.MaxValue;
            for (int ring = 1; ring <= SearchCells; ring++)
            {
                for (int gx = cx - ring; gx <= cx + ring; gx++)
                    for (int gz = cz - ring; gz <= cz + ring; gz++)
                    {
                        if (Math.Max(Math.Abs(gx - cx), Math.Abs(gz - cz)) != ring) continue;
                        float gy = At(gx, gz);
                        if (float.IsNaN(gy)) continue;
                        float px = _minX + (gx + 0.5f) * _cell, pz = _minZ + (gz + 0.5f) * _cell;
                        float d = (px - x) * (px - x) + (pz - z) * (pz - z);
                        if (d < bestD) { bestD = d; hit = new Vector3(px, gy, pz); }
                    }
                if (bestD < float.MaxValue) return true;
            }
            return false;
        }

        /// <summary>Whether the cell under world X/Z is walkable (for drawing the ground on the map).</summary>
        public bool IsWalkable(float x, float z) =>
            !float.IsNaN(At(Mathf.FloorToInt((x - _minX) / _cell), Mathf.FloorToInt((z - _minZ) / _cell)));

        private float At(int col, int row) =>
            col < 0 || row < 0 || col >= _w || row >= _h ? float.NaN : _y[row * _w + col];

        public void Save(string file)
        {
            using var w = new BinaryWriter(File.Create(file));
            w.Write(Magic);
            w.Write(_minX); w.Write(_minZ); w.Write(_cell); w.Write(_w); w.Write(_h);
            foreach (float v in _y) w.Write(v);
        }

        public static NavGround Load(string file)
        {
            if (!File.Exists(file)) { Core.Log?.Warning("[world] ground.bin is missing - map clicks keep a flat height."); return null; }
            try
            {
                using var r = new BinaryReader(File.OpenRead(file));
                if (r.ReadInt32() != Magic) { Core.Log?.Warning("[world] ground.bin has an unknown format."); return null; }
                float minX = r.ReadSingle(), minZ = r.ReadSingle(), cell = r.ReadSingle();
                int w = r.ReadInt32(), h = r.ReadInt32();
                if (w <= 0 || h <= 0 || w * h > 4_000_000 || cell <= 0f) { Core.Log?.Warning("[world] ground.bin is damaged."); return null; }
                var y = new float[w * h];
                for (int i = 0; i < y.Length; i++) y[i] = r.ReadSingle();
                return new NavGround(minX, minZ, cell, w, h, y);
            }
            catch (Exception e)
            {
                Core.Log?.Warning("[world] reading ground.bin failed: " + e.Message);
                return null;
            }
        }
    }
}
