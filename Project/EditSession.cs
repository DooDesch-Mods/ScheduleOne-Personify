using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Personify.Project
{
    /// <summary>
    /// The project open in the editor: field edits by path, undo and redo, and saving shortly after the last edit.
    /// Unity-free, so the offline checks drive it the same way the page does.
    /// </summary>
    public sealed class EditSession
    {
        private const int UndoLimit = 200;

        /// <summary>Seconds without an edit before the project is written.</summary>
        public const double SaveDelay = 0.8;

        public PackProject Project { get; private set; }

        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        private string _last;
        private double _dirtySince = -1;

        /// <summary>Group key of the last edit, so a slider drag becomes one undo step instead of fifty.</summary>
        private string _lastGroup;
        private double _lastEditAt;

        public EditSession(PackProject p)
        {
            Project = p;
            _last = Serialize();
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool Dirty => _dirtySince >= 0;

        private string Serialize() => Project.Manifest.ToString(Formatting.None);

        /// <summary>
        /// Sets <paramref name="path"/> on every NPC in <paramref name="ids"/>, or on the manifest when ids is empty.
        /// "editor.x" on the pack and "personify.x" on an NPC address the editor blocks. Returns the number of
        /// objects changed.
        /// </summary>
        public int Set(IReadOnlyList<string> ids, string path, JToken value, double now)
            => Apply(ids, new[] { new FieldChange { Path = path, Value = value } }, now);

        public int Unset(IReadOnlyList<string> ids, string path, double now)
            => Apply(ids, new[] { new FieldChange { Path = path, Unset = true } }, now);

        /// <summary>
        /// Applies several field changes as one undo step (a customer profile, a start point, skin plus both lid
        /// colours). The same set of paths edited again within a second extends that step, so a slider's live values
        /// and its final value end up as one. Returns the number of field writes that changed something.
        /// </summary>
        public int Apply(IReadOnlyList<string> ids, IReadOnlyList<FieldChange> changes, double now)
        {
            int changed = 0;
            var paths = new List<string>();
            foreach (var c in changes)
            {
                if (c == null) continue;
                string path = PackPath(ids, c.Path);
                paths.Add(path);
                foreach (var target in Targets(ids))
                    if (c.Unset ? JsonPath.Unset(target, path) : JsonPath.Set(target, path, c.Value)) changed++;
            }
            if (changed > 0) Edited(string.Join("+", paths), ids, now);
            return changed;
        }

        // The page names the pack's editor block "editor"; in the manifest it is the "personify" block.
        private static string PackPath(IReadOnlyList<string> ids, string path)
            => (ids == null || ids.Count == 0) && path != null && path.StartsWith("editor.", StringComparison.Ordinal)
                ? PackProject.EditorKey + path.Substring("editor".Length) : path;

        /// <summary>Runs a change that is not a single field (add, remove, role switch) as one undo step.</summary>
        public void Change(string what, Action change, double now)
        {
            change();
            Edited("#" + what + "#" + now, null, now);
        }

        private IEnumerable<JObject> Targets(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0) { yield return Project.Manifest; yield break; }
            foreach (string id in ids)
            {
                var n = Project.FindNpc(id);
                if (n != null) yield return n;
                else ProjectLog.Warn($"[edit] NPC '{id}' is not in '{Project.Folder}' - edit skipped for it.");
            }
        }

        private void Edited(string path, IReadOnlyList<string> ids, double now)
        {
            string current = Serialize();
            if (current == _last) return;
            string group = path + "|" + (ids == null ? "" : string.Join(",", ids));
            // The same field edited again within a second (a slider drag, typing) extends the last undo step.
            bool merge = group == _lastGroup && now - _lastEditAt < 1.0 && _undo.Count > 0;
            if (!merge)
            {
                _undo.Add(_last);
                if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
            }
            _redo.Clear();
            _last = current;
            _lastGroup = group;
            _lastEditAt = now;
            if (_dirtySince < 0) _dirtySince = now;
            _dirtySinceLast = now;
        }

        private double _dirtySinceLast;

        public bool Undo(double now) => Step(_undo, _redo, now);
        public bool Redo(double now) => Step(_redo, _undo, now);

        private bool Step(List<string> from, List<string> to, double now)
        {
            if (from.Count == 0) return false;
            string state = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(_last);
            Replace(state);
            _lastGroup = null;
            if (_dirtySince < 0) _dirtySince = now;
            _dirtySinceLast = now;
            return true;
        }

        /// <summary>Replaces the manifest with another state (undo, redo, history restore).</summary>
        public void Replace(string manifestJson)
        {
            var m = JObject.Parse(manifestJson);
            Project.Manifest.RemoveAll();
            foreach (var prop in m.Properties()) Project.Manifest.Add(prop.Name, prop.Value);
            _last = Serialize();
        }

        /// <summary>Called every frame: writes the project once edits have settled.</summary>
        public bool Tick(double now)
        {
            if (_dirtySince < 0 || now - _dirtySinceLast < SaveDelay) return false;
            return Flush();
        }

        /// <summary>Writes pending edits now (leaving the editor, installing, testing).</summary>
        public bool Flush()
        {
            if (_dirtySince < 0) return true;
            bool ok = ProjectFiles.Save(Project);
            if (ok) _dirtySince = -1;
            else ProjectLog.Error($"[edit] '{Project.Folder}' could not be saved - the edits stay in memory and are retried.");
            return ok;
        }
    }

    /// <summary>One field write of a batch: a value, or the removal of the field.</summary>
    public sealed class FieldChange
    {
        public string Path;
        public JToken Value;
        public bool Unset;
    }

    /// <summary>Dotted paths into JSON objects ("appearance.faceLayers.0.tint").</summary>
    public static class JsonPath
    {
        public static bool Set(JObject root, string path, JToken value)
        {
            var parts = Split(path);
            if (parts == null) return false;
            JToken node = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                JToken next = Child(node, parts[i]);
                if (next == null || next.Type == JTokenType.Null)
                {
                    next = IsIndex(parts[i + 1]) ? (JToken)new JArray() : new JObject();
                    if (!Put(node, parts[i], next)) return false;
                }
                node = next;
            }
            JToken old = Child(node, parts[parts.Length - 1]);
            JToken v = value ?? JValue.CreateNull();
            if (old != null && JToken.DeepEquals(old, v)) return false;
            return Put(node, parts[parts.Length - 1], v.DeepClone());
        }

        public static bool Unset(JObject root, string path)
        {
            var parts = Split(path);
            if (parts == null) return false;
            JToken node = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                node = Child(node, parts[i]);
                if (node == null) return false;
            }
            string last = parts[parts.Length - 1];
            if (node is JObject o) return o.Remove(last);
            if (node is JArray a && int.TryParse(last, out int idx) && idx >= 0 && idx < a.Count) { a.RemoveAt(idx); return true; }
            return false;
        }

        public static JToken Get(JObject root, string path)
        {
            var parts = Split(path);
            if (parts == null) return null;
            JToken node = root;
            foreach (string p in parts) { node = Child(node, p); if (node == null) return null; }
            return node;
        }

        private static string[] Split(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) { ProjectLog.Warn("[edit] empty field path - edit skipped."); return null; }
            var parts = path.Split('.');
            foreach (string p in parts)
                if (p.Length == 0) { ProjectLog.Warn($"[edit] field path '{path}' has an empty part - edit skipped."); return null; }
            return parts;
        }

        private static bool IsIndex(string s) => int.TryParse(s, out int i) && i >= 0;

        private static JToken Child(JToken node, string key)
        {
            if (node is JObject o) return o[key];
            if (node is JArray a && int.TryParse(key, out int i) && i >= 0 && i < a.Count) return a[i];
            return null;
        }

        private static bool Put(JToken node, string key, JToken value)
        {
            if (node is JObject o) { o[key] = value; return true; }
            if (node is JArray a && int.TryParse(key, out int i) && i >= 0)
            {
                if (i < a.Count) a[i] = value;
                else if (i == a.Count) a.Add(value);
                else { ProjectLog.Warn($"[edit] index {i} is past the end of a list of {a.Count} - edit skipped."); return false; }
                return true;
            }
            ProjectLog.Warn($"[edit] '{key}' cannot be set on a {node?.Type.ToString() ?? "missing"} value - edit skipped.");
            return false;
        }
    }
}
