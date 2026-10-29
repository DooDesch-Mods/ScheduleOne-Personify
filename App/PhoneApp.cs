using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Project;
using Sideload.Api;
using UnityEngine;

namespace Personify.App
{
    /// <summary>
    /// The Personify phone app: a companion inside a loaded save that reads and writes a Personify project. The player
    /// picks a project and an NPC, then sets its start point where they stand, records a route step by step, jumps to
    /// the live NPC, or installs the project and refreshes the NPC's look. Every change is saved to the project at once.
    /// Calls take "folder\nnpcId" and answer JSON; see <c>Assets/phone/app.js</c> for the other side.
    /// </summary>
    public static class PhoneApp
    {
        internal const string AppId = "personify-phone";
        private const string Bundle = "Personify.Assets.phone";

        private static AppHandle _app;
        private static bool _registered;

        // Steps recorded in this session per "folder\nid", newest last, so "Undo" removes only what the phone added.
        private static readonly Dictionary<string, List<JObject>> _recorded = new Dictionary<string, List<JObject>>(StringComparer.OrdinalIgnoreCase);

        private static string PacksRoot => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "Packs");

        /// <summary>Registers the app with Sideload once. Call from OnInitializeMelon, after the editor's localization.</summary>
        public static void Register()
        {
            if (_registered) { Core.Log.Warning("[phone] Register called again - the app is already registered."); return; }
            _registered = true;
            try
            {
                if (string.IsNullOrEmpty(ProjectFiles.Root))
                {
                    ProjectFiles.Root = Editor.Paths.Projects;
                    Core.Log.Msg("[phone] projects root was not set yet - using " + ProjectFiles.Root + ".");
                }
                PhoneLocalization.Register();
                PhoneState.Load();
                PhoneState.CaptureLaunchMarker();

                _app = Apps.Register(AppId, Bundle, "Personify", "Personify");
                _app.Orientation("portrait", "landscape");
                On("phone.init", _ => Init());
                On("phone.projects", _ => Projects());
                On("phone.npcs", NpcList);
                On("phone.npc", a => WithNpc(a, (p, n) => Answer(true, null, p, n, remember: true)));
                On("phone.spawn", a => WithNpc(a, SetSpawn));
                On("phone.step", a => WithNpc(a, AddStep));
                On("phone.undo", a => WithNpc(a, UndoStep));
                On("phone.jump", a => WithNpc(a, Jump));
                On("phone.look", a => WithNpc(a, ApplyLook));

                Core.Log.Msg(Apps.Available
                    ? "[phone] app registered with Sideload."
                    : "[phone] Sideload is not loaded yet - the app registration is queued.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[phone] registering the phone app failed: " + e.Message);
            }
        }

        private static void On(string name, Func<string, JObject> handler) => _app.OnCall(name, arg => Run(name, arg, handler));

        private static string Run(string name, string arg, Func<string, JObject> handler)
        {
            try { return (handler(arg) ?? Fail("Something went wrong. See the log.")).ToString(Formatting.None); }
            catch (Exception e)
            {
                Core.Log.Error($"[phone] {name}({arg?.Replace("\n", " / ")}) failed: {e}");
                return Fail("Something went wrong. See the log.").ToString(Formatting.None);
            }
        }

        private static JObject Fail(string en, params object[] args) =>
            new JObject { ["ok"] = false, ["msg"] = PhoneLocalization.T(en, args) };

        // ---- lists -------------------------------------------------------------------------------------------------

        private static JObject Init() => new JObject
        {
            ["ok"] = true,
            ["strings"] = PhoneLocalization.Strings(),
            ["last"] = new JObject { ["project"] = PhoneState.Project, ["npc"] = PhoneState.Npc },
        };

        private static JObject Projects()
        {
            var list = new JArray();
            foreach (var s in ProjectFiles.List())
                list.Add(new JObject { ["folder"] = s.folder, ["name"] = s.name, ["npcs"] = s.npcs });
            Core.Log.Msg($"[phone] listed {list.Count} project(s) from {ProjectFiles.Root}.");
            return new JObject { ["ok"] = true, ["projects"] = list };
        }

        private static JObject NpcList(string folder)
        {
            var p = Open(folder);
            if (p == null) return Fail("Project not found.");
            PhoneState.Remember(p.Folder, PhoneState.Project == p.Folder ? PhoneState.Npc : null);
            var list = new JArray();
            foreach (var t in p.Npcs)
            {
                if (!(t is JObject n) || string.IsNullOrWhiteSpace((string)n["id"])) continue;
                list.Add(new JObject
                {
                    ["id"] = (string)n["id"],
                    ["name"] = (string)n["name"] ?? (string)n["id"],
                    ["steps"] = (n["schedule"] as JArray)?.Count ?? 0,
                    ["spawn"] = n["spawn"] is JObject s && s["x"] != null,
                });
            }
            return new JObject { ["ok"] = true, ["folder"] = p.Folder, ["name"] = p.Name, ["npcs"] = list };
        }

        private static PackProject Open(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) { Core.Log.Warning("[phone] no project folder given."); return null; }
            // A path from the page must stay a single folder name under the projects root.
            if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder.Contains("..")) { Core.Log.Warning($"[phone] '{folder}' is not a project folder name."); return null; }
            if (!Directory.Exists(ProjectFiles.ProjectDir(folder))) { Core.Log.Warning($"[phone] project '{folder}' does not exist."); return null; }
            var p = ProjectFiles.Load(folder);
            if (p == null) Core.Log.Warning($"[phone] project '{folder}' could not be loaded.");
            return p;
        }

        private static JObject WithNpc(string arg, Func<PackProject, JObject, JObject> action)
        {
            string[] parts = (arg ?? "").Split('\n');
            if (parts.Length != 2) { Core.Log.Warning($"[phone] call argument '{arg}' is not 'folder\\nnpcId'."); return Fail("NPC not found."); }
            var p = Open(parts[0]);
            if (p == null) return Fail("Project not found.");
            var n = p.FindNpc(parts[1]);
            if (n == null) { Core.Log.Warning($"[phone] project '{p.Folder}' has no NPC '{parts[1]}'."); return Fail("NPC not found."); }
            return action(p, n);
        }

        // ---- the NPC screen ----------------------------------------------------------------------------------------

        private static string Key(PackProject p, JObject n) => p.Folder + "\n" + (string)n["id"];

        private static JObject Answer(bool ok, string msg, PackProject p, JObject n, bool remember = false)
        {
            string id = (string)n["id"];
            if (remember) PhoneState.Remember(p.Folder, id);

            JObject spawn = null;
            if (n["spawn"] is JObject s && s["x"] != null && s["z"] != null)
            {
                spawn = new JObject
                {
                    ["region"] = (string)s["region"] ?? "",
                    ["at"] = Coords((double?)s["x"] ?? 0, (double?)s["y"] ?? 0, (double?)s["z"] ?? 0),
                };
            }

            var steps = new JArray();
            if (n["schedule"] is JArray a)
                foreach (var t in a)
                    if (t is JObject step) steps.Add(new JObject { ["time"] = (string)step["time"] ?? "", ["text"] = Describe(step) });

            return new JObject
            {
                ["ok"] = ok,
                ["msg"] = msg,
                ["npc"] = new JObject
                {
                    ["id"] = id,
                    ["name"] = (string)n["name"] ?? id,
                    ["folder"] = p.Folder,
                    ["project"] = p.Name,
                    ["live"] = PhoneWorld.FindNpc((string)n["saveId"], id) != null,
                    ["spawn"] = spawn,
                    ["steps"] = steps,
                    ["canUndo"] = _recorded.TryGetValue(Key(p, n), out var rec) && rec.Count > 0,
                },
            };
        }

        private static string Coords(double x, double y, double z) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", x, y, z);

        private static string Describe(JObject step)
        {
            string type = (string)step["type"] ?? "";
            string label = type.ToLowerInvariant() switch
            {
                "walkto" => "Walk to",
                "stayinbuilding" => "Go inside",
                "locationaction" => "Hang out",
                "sit" => "Sit down",
                "drivetocarpark" => "Drive to",
                "useatm" => "Use an ATM",
                "usevendingmachine" => "Use a vending machine",
                _ => "Custom step",
            };
            string where = (string)step["building"] ?? (string)step["parkingLot"] ?? (string)step["seatSet"];
            if (string.IsNullOrEmpty(where) && step["position"] is JArray pos && pos.Count >= 3)
            {
                try
                {
                    var v = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]);
                    where = PhoneWorld.RegionAt(v) ?? Coords(v.x, v.y, v.z);
                }
                catch (Exception e) { Core.Log.Warning($"[phone] unreadable step position {pos.ToString(Formatting.None)}: {e.Message}"); }
            }
            string text = PhoneLocalization.T(label);
            return string.IsNullOrEmpty(where) ? text : text + " " + where;
        }

        private static JObject Saved(PackProject p, JObject n, string en, params object[] args)
        {
            if (!ProjectFiles.Save(p)) return Answer(false, PhoneLocalization.T("Saving failed. See the log."), p, n);
            return Answer(true, PhoneLocalization.T(en, args), p, n);
        }

        // ---- actions -----------------------------------------------------------------------------------------------

        // Same block the editor's map writes: a start point placed in the world means the NPC lives in town, so it is
        // physical and Personnel spawns it on its own.
        private static JObject SetSpawn(PackProject p, JObject n)
        {
            if (!PhoneWorld.TryPlayer(out Vector3 pos, out float yaw)) return Answer(false, PhoneLocalization.T("Load into a save first."), p, n);
            if (!(n["spawn"] is JObject s)) { s = new JObject(); n["spawn"] = s; }
            s["x"] = Math.Round(pos.x, 2); s["y"] = Math.Round(pos.y, 2); s["z"] = Math.Round(pos.z, 2);
            s["rotationY"] = Math.Round(yaw, 1);
            string region = PhoneWorld.RegionAt(pos);
            if (!string.IsNullOrEmpty(region)) s["region"] = region;
            else if (s.Remove("region")) Core.Log.Warning($"[phone] '{n["id"]}': no region at {pos}, the old region was removed.");
            s["physical"] = true;
            s["auto"] = true;
            Core.Log.Msg($"[phone] '{n["id"]}' starts at {pos} facing {yaw:0} ({region ?? "no region"}).");
            return string.IsNullOrEmpty(region)
                ? Saved(p, n, "Start point set.")
                : Saved(p, n, "Start point set in {0}.", region);
        }

        // The shape Personnel's own "personnel route" writes: a walkTo at the game clock, warped there when skipped.
        private static JObject AddStep(PackProject p, JObject n)
        {
            if (!PhoneWorld.TryPlayer(out Vector3 pos, out _)) return Answer(false, PhoneLocalization.T("Load into a save first."), p, n);
            string time = PhoneWorld.Clock();
            if (time == null) return Answer(false, PhoneLocalization.T("Load into a save first."), p, n);

            var step = new JObject
            {
                ["type"] = "walkTo",
                ["time"] = time,
                ["position"] = new JArray(Math.Round(pos.x, 2), Math.Round(pos.y, 2), Math.Round(pos.z, 2)),
                ["warpIfSkipped"] = true,
            };
            if (!(n["schedule"] is JArray a))
            {
                if (n["schedule"] != null && n["schedule"].Type != JTokenType.Null)
                    Core.Log.Warning($"[phone] '{n["id"]}' had a schedule that is not a list - replaced with an empty one.");
                a = new JArray();
                n["schedule"] = a;
            }
            a.Add(step);
            SortByTime(a);

            string key = Key(p, n);
            if (!_recorded.TryGetValue(key, out var rec)) { rec = new List<JObject>(); _recorded[key] = rec; }
            rec.Add((JObject)step.DeepClone());

            Core.Log.Msg($"[phone] '{n["id"]}' route step {a.Count} at {time}: {pos}.");
            return Saved(p, n, "Step {0} added at {1}.", a.IndexOf(step) + 1, time);
        }

        private static JObject UndoStep(PackProject p, JObject n)
        {
            string key = Key(p, n);
            if (!_recorded.TryGetValue(key, out var rec) || rec.Count == 0)
            {
                Core.Log.Msg($"[phone] undo for '{n["id"]}': nothing recorded in this session.");
                return Answer(false, PhoneLocalization.T("No recorded step to remove."), p, n);
            }
            JObject last = rec[rec.Count - 1];
            rec.RemoveAt(rec.Count - 1);
            if (n["schedule"] is JArray a)
            {
                for (int i = a.Count - 1; i >= 0; i--)
                {
                    if (!JToken.DeepEquals(a[i], last)) continue;
                    a.RemoveAt(i);
                    Core.Log.Msg($"[phone] '{n["id"]}' recorded step at {last["time"]} removed.");
                    return Saved(p, n, "Last step removed.");
                }
            }
            Core.Log.Warning($"[phone] undo for '{n["id"]}': the recorded step at {last["time"]} is no longer in the schedule (edited elsewhere).");
            return Answer(false, PhoneLocalization.T("That step was already changed elsewhere."), p, n);
        }

        private static JObject Jump(PackProject p, JObject n)
        {
            string id = (string)n["id"], name = (string)n["name"] ?? id;
            var npc = PhoneWorld.FindNpc((string)n["saveId"], id);
            if (npc == null)
            {
                Core.Log.Msg($"[phone] jump: '{id}' is not in this world.");
                return Answer(false, PhoneLocalization.T("{0} is not in this world.", name), p, n);
            }
            return PhoneWorld.TeleportNextTo(npc)
                ? Answer(true, PhoneLocalization.T("Jumped to {0}.", name), p, n)
                : Answer(false, PhoneLocalization.T("Jump failed. See the log."), p, n);
        }

        private static JObject ApplyLook(PackProject p, JObject n)
        {
            string id = (string)n["id"], name = (string)n["name"] ?? id;
            if (!ProjectFiles.Save(p)) return Answer(false, PhoneLocalization.T("Saving failed. See the log."), p, n);

            var r = Installer.Install(p, PacksRoot, null);
            if (!r.ok)
            {
                Core.Log.Warning($"[phone] look for '{id}': install answered {r.code} ({r.text}).");
                return Answer(false, PhoneLocalization.T(r.code == "blocked" ? "Not installed: the project has errors." : "Install failed. See the log."), p, n);
            }

            switch (PhoneLook.Apply(id, (string)n["saveId"]))
            {
                case PhoneLook.Outcome.Applied: return Answer(true, PhoneLocalization.T("Look applied."), p, n);
                case PhoneLook.Outcome.NoPersonnel: return Answer(true, PhoneLocalization.T("Installed. The look changes on the next save load."), p, n);
                case PhoneLook.Outcome.NoDefinition: return Answer(false, PhoneLocalization.T("Installed. Personnel did not load this NPC."), p, n);
                case PhoneLook.Outcome.NotInWorld: return Answer(true, PhoneLocalization.T("Installed. {0} is not in this world.", name), p, n);
                default: return Answer(false, PhoneLocalization.T("Installed. The look could not be applied. See the log."), p, n);
            }
        }

        // ---- schedule order ----------------------------------------------------------------------------------------

        /// <summary>Stable sort by start time, as the editor keeps it. Steps without a readable time stay at the end.</summary>
        private static void SortByTime(JArray a)
        {
            if (a.Count < 2) return;
            var keyed = new List<(int key, int index, JToken item)>();
            for (int i = 0; i < a.Count; i++) keyed.Add((Minutes(a[i] as JObject) ?? int.MaxValue, i, a[i]));
            keyed.Sort((x, y) => x.key != y.key ? x.key.CompareTo(y.key) : x.index.CompareTo(y.index));
            a.Clear();
            foreach (var k in keyed) a.Add(k.item);
        }

        private static int? Minutes(JObject step)
        {
            JToken t = step?["time"];
            if (t == null) return null;
            int hhmm;
            if (t.Type == JTokenType.Integer) hhmm = (int)t;
            else
            {
                string s = ((string)t)?.Trim().Replace(":", "");
                if (string.IsNullOrEmpty(s) || !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out hhmm)) return null;
            }
            int h = hhmm / 100, m = hhmm % 100;
            return h >= 0 && h < 24 && m >= 0 && m < 60 ? h * 60 + m : (int?)null;
        }

#if DEBUG
        /// <summary>Runs one call the way the page would, for the dev console command.</summary>
        internal static string DebugCall(string name, string arg)
        {
            switch (name)
            {
                case "init": return Run(name, arg, _ => Init());
                case "projects": return Run(name, arg, _ => Projects());
                case "npcs": return Run(name, arg, NpcList);
                case "npc": return Run(name, arg, a => WithNpc(a, (p, n) => Answer(true, null, p, n, remember: true)));
                case "spawn": return Run(name, arg, a => WithNpc(a, SetSpawn));
                case "step": return Run(name, arg, a => WithNpc(a, AddStep));
                case "undo": return Run(name, arg, a => WithNpc(a, UndoStep));
                case "jump": return Run(name, arg, a => WithNpc(a, Jump));
                case "look": return Run(name, arg, a => WithNpc(a, ApplyLook));
                default: return null;
            }
        }
#endif
    }
}
