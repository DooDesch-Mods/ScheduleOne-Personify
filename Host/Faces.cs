using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Project;

namespace Personify.Host
{
    /// <summary>
    /// The NPC faces the page shows in the crew wall, the NPC list and the field head. An NPC with a contact picture
    /// shows that; every other one gets a small head render, cached under UserData/Personify/Faces/&lt;folder&gt;/ and
    /// keyed by a hash of its look, so a changed look renders again. Faces reach the page as s1:// pictures.
    /// <para>
    /// A render waits a few frames after the look goes onto the preview avatar: the game removes the old hair and
    /// accessories with Object.Destroy, which only takes effect at the end of the frame, so a render in the same frame
    /// still shows the previous look. Missing faces are therefore queued, rendered one by one from <see cref="Tick"/>,
    /// and sent to the page as the event "faces.ready" once the queue is empty.
    /// </para>
    /// </summary>
    internal static class Faces
    {
        private const int Size = 128;
        private const int SettleFrames = 3;
        private const int WallFaces = 6;

        private sealed class Job
        {
            public PackProject Project;
            public JObject Npc;
            public string Id, File, Name;
        }

        private static readonly HashSet<string> Published = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<Job> Pending = new Queue<Job>();
        private static readonly HashSet<string> Queued = new HashSet<string>(StringComparer.Ordinal);
        private static Job _current;
        private static int _frames;
        private static JObject _ready = new JObject();
        private static int _rendered, _failed;
        private static Stopwatch _clock;

        private static string CacheRoot => Path.Combine(Path.GetDirectoryName(ProjectFiles.Root) ?? ProjectFiles.Root, "Faces");

        /// <summary>True while faces are being rendered; the preview avatar then wears their looks.</summary>
        internal static bool Busy => _current != null || Pending.Count > 0;

        /// <summary>A newly mounted page knows no pictures yet, and an unmounted one wants no more.</summary>
        internal static void Reset()
        {
            Published.Clear();
            Pending.Clear();
            Queued.Clear();
            _current = null;
            _ready = new JObject();
        }

        /// <summary>Faces of the open project that exist now: { ok, faces: { id: url }, pending }. Missing ones are queued.</summary>
        internal static JObject Capture(PackProject p)
        {
            var faces = new JObject();
            int before = Pending.Count;
            foreach (var npc in p.Npcs.OfType<JObject>())
            {
                string url = FaceOf(p, npc);
                if (url != null) faces[(string)npc["id"]] = url;
            }
            int queued = Pending.Count - before;
            Core.Log.Msg($"[faces] '{p.Folder}': {faces.Count} of {p.Npcs.Count} face(s) ready, {queued} queued for rendering.");
            return new JObject { ["ok"] = true, ["faces"] = faces, ["pending"] = Pending.Count + (_current != null ? 1 : 0) };
        }

        /// <summary>The start screen's crew: { folder: [{ id, name, face }] } with the first NPCs of each project.
        /// Missing faces are queued and arrive with "faces.ready".</summary>
        internal static JObject Wall()
        {
            var wall = new JObject();
            int before = Pending.Count;
            foreach (var s in ProjectFiles.List())
            {
                var open = EditorHost.Project;
                var p = open != null && open.Folder == s.folder ? open
                    : s.formatVersion < 2 ? ConvertedInMemory(s.folder)
                    : ProjectFiles.Load(s.folder, false);
                if (p == null) { Core.Log.Warning($"[faces] '{s.folder}' could not be read for the start screen."); continue; }
                var crew = new JArray();
                foreach (var npc in p.Npcs.OfType<JObject>().Take(WallFaces))
                {
                    crew.Add(new JObject
                    {
                        ["id"] = (string)npc["id"],
                        ["name"] = (string)npc["name"] ?? (string)npc["id"],
                        ["face"] = FaceOf(p, npc),
                    });
                }
                wall[s.folder] = crew;
            }
            Core.Log.Msg($"[faces] start screen: {wall.Count} project(s), {Pending.Count - before} face(s) queued for rendering.");
            return wall;
        }

        // An old project is converted on disk only when it is opened; for its faces a converted copy in memory is
        // enough, and its NPC ids are the ones the conversion will write.
        private static PackProject ConvertedInMemory(string folder)
        {
            string dir = Path.Combine(ProjectFiles.Root, folder);
            var v1 = Migration.ReadV1(Path.Combine(dir, Migration.V1File));
            if (v1 == null) return null;
            return new PackProject { Folder = folder, Dir = dir, Manifest = Migration.Convert(v1) };
        }

        /// <summary>Renders the queue, one face every few frames. Called every frame while the editor is open.</summary>
        internal static void Tick()
        {
            if (_current == null)
            {
                if (Pending.Count == 0) return;
                if (_clock == null) { _clock = Stopwatch.StartNew(); _rendered = 0; _failed = 0; }
                _current = Pending.Dequeue();
                _frames = 0;
                if (!LookBridge.Apply(_current.Project, _current.Npc))
                {
                    Core.Log.Warning($"[faces] '{_current.Id}': the preview character is not available.");
                    Finish(null);
                }
                return;
            }
            if (++_frames < SettleFrames) return;
            Finish(LookBridge.RenderHead(_current.Project, _current.Npc, Size, "faces", false));
        }

        private static void Finish(byte[] png)
        {
            var job = _current;
            _current = null;
            Queued.Remove(job.File);
            string url = null;
            if (png == null) _failed++;
            else
            {
                try
                {
                    string dir = Path.GetDirectoryName(job.File);
                    Directory.CreateDirectory(dir);
                    foreach (string old in Directory.GetFiles(dir, Safe(job.Id) + "-*.png")) File.Delete(old);
                    File.WriteAllBytes(job.File, png);
                    url = Publish(job.Name, job.File);
                    _rendered++;
                }
                catch (Exception e)
                {
                    _failed++;
                    Core.Log.Error($"[faces] '{job.Id}': writing {job.File} failed ({e.GetType().Name}: {e.Message}).");
                }
            }
            if (url != null)
            {
                if (!(_ready[job.Project.Folder] is JObject f)) _ready[job.Project.Folder] = f = new JObject();
                f[job.Id] = url;
            }
            if (Pending.Count > 0) return;

            Core.Log.Msg($"[faces] rendered {_rendered} face(s), {_failed} failed, {_clock?.ElapsedMilliseconds ?? 0} ms.");
            _clock = null;
            var ready = _ready;
            _ready = new JObject();
            EditorHost.Emit("faces.ready", ready);
            // The preview avatar wore the faces' looks; the NPC the page shows goes back on.
            EditorHost.ReapplyPreview();
        }

        // The contact picture when there is one, else the cached render for the current look. A missing render is
        // queued and null is returned.
        private static string FaceOf(PackProject p, JObject npc)
        {
            string id = (string)npc["id"];
            if (string.IsNullOrEmpty(id)) return null;
            string who = Hex(Fnv(p.Folder + "/" + id));

            string portrait = (string)npc["portrait"];
            if (!string.IsNullOrEmpty(portrait))
            {
                string abs = Path.Combine(p.Dir, portrait.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(abs))
                {
                    string stamp = Hex(Fnv(portrait + "|" + File.GetLastWriteTimeUtc(abs).Ticks));
                    return Publish("face-" + who + "-" + stamp, abs);
                }
                Core.Log.Warning($"[faces] '{id}': contact picture {portrait} is missing, a head render is used.");
            }

            string key = Hex(Fnv((npc["appearance"]?.ToString(Formatting.None) ?? "") + "|" + (npc[PackProject.EditorKey]?.ToString(Formatting.None) ?? "")));
            string file = Path.Combine(CacheRoot, p.Folder, Safe(id) + "-" + key + ".png");
            string name = "face-" + who + "-" + key;
            if (File.Exists(file)) return Publish(name, file);
            if (Queued.Add(file))
                Pending.Enqueue(new Job { Project = p, Npc = (JObject)npc.DeepClone(), Id = id, File = file, Name = name });
            return null;
        }

        private static string Publish(string name, string file)
        {
            if (!Published.Contains(name))
            {
                byte[] png;
                try { png = File.ReadAllBytes(file); }
                catch (Exception e) { Core.Log.Error($"[faces] reading {file} failed ({e.GetType().Name}: {e.Message})."); return null; }
                EditorHost.PublishImage(name, png);
                Published.Add(name);
            }
            return "s1://" + name;
        }

        private static string Safe(string id)
        {
            var sb = new StringBuilder(id.Length);
            var bad = Path.GetInvalidFileNameChars();
            foreach (char c in id) sb.Append(Array.IndexOf(bad, c) >= 0 || c == '-' ? '_' : c);
            return sb.ToString();
        }

        // FNV-1a over UTF-8: stable across runs, unlike string.GetHashCode.
        private static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619; }
            return h;
        }

        private static string Hex(uint v) => v.ToString("x8");
    }
}
