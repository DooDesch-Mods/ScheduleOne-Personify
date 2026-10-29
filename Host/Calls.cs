using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DooDesch.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Editor;
using Personify.Project;
using Sideload.Api;

namespace Personify.Host
{
    /// <summary>The page's calls (see the page contract). Every handler answers JSON; a thrown error is logged and
    /// answers "", which the page shows as a failed action.</summary>
    internal static class Calls
    {
        private static readonly JObject Ok = new JObject { ["ok"] = true };

        internal static void Register(SurfaceHandle s)
        {
            void On(string name, Func<string, JToken> handler) => s.OnCall(name, arg => Run(name, arg, handler));

            On("app.init", _ => new JObject
            {
                ["lang"] = L10n.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en",
                ["strings"] = EditorHost.Strings(),
                ["expert"] = EditorHost.Expert,
                ["personnelVersion"] = GameData.PersonnelVersion() ?? "",
                ["hasWorld"] = Editor.World.WorldData.Current != null,
                ["sideloadVersion"] = SideloadVersion(),
            });
            On("app.setExpert", a => { EditorHost.Expert = a == "1" || a == "true"; return Ok; });

            On("projects.list", _ => new JArray(ProjectFiles.List(p => Installer.State(p, GameData.PacksRoot)).Select(x => new JObject
            {
                ["folder"] = x.folder, ["name"] = x.name, ["npcs"] = x.npcs, ["formatVersion"] = x.formatVersion, ["install"] = x.install ?? "not_installed",
            })));
            On("projects.create", a =>
            {
                var o = Obj(a);
                string name = ((string)o["name"] ?? "").Trim();
                if (name.Length == 0) return Fail("name_missing");
                var p = ProjectFiles.Create(name, (string)o["author"] ?? "");
                if (p == null) return Fail("create_failed");
                Core.Log.Msg($"[editor] project '{p.Folder}' created.");
                return new JObject { ["ok"] = true, ["folder"] = p.Folder };
            });
            On("projects.open", a =>
            {
                // The open project is saved first: reopening it straight away would otherwise read the file before
                // the last edits reach it, and the next autosave would write the stale copy back.
                EditorHost.CloseProject();
                var p = ProjectFiles.Load(a);
                if (p == null) return Fail("not_found");
                EditorHost.OpenProject(p);
                return State();
            });
            On("projects.delete", a =>
            {
                if (EditorHost.Project?.Folder == a) EditorHost.CloseProject();
                return new JObject { ["ok"] = ProjectFiles.Delete(a) };
            });
            On("projects.importable", _ => PacksBridge.Importable());
            On("projects.import", a =>
            {
                var made = PacksBridge.Import(a);
                return made.Count == 0 ? Fail("import_failed") : new JObject { ["ok"] = true, ["folders"] = new JArray(made) };
            });

            On("project.set", a =>
            {
                var o = Obj(a);
                var ids = Ids(o);
                var changes = new List<FieldChange>();
                if (o["changes"] is JArray batch)
                {
                    foreach (var c in batch.OfType<JObject>())
                        changes.Add(new FieldChange { Path = (string)c["path"], Value = c["value"], Unset = (bool?)c["unset"] == true });
                }
                else changes.Add(new FieldChange { Path = (string)o["path"], Value = o["value"] });
                // Live values (a slider still moving) merge with the final write of the same paths into one undo step.
                Need().Apply(ids, changes, EditorHost.Now);
                foreach (var c in changes) EditorHost.Touched(ids, c.Path);
                return Delta();
            });
            On("project.unset", a =>
            {
                var o = Obj(a);
                var ids = Ids(o);
                string path = (string)o["path"];
                Need().Unset(ids, path, EditorHost.Now);
                EditorHost.Touched(ids, path);
                return Delta();
            });
            On("project.undo", _ => { Need().Undo(EditorHost.Now); EditorHost.Touched(null, null); return State(); });
            On("project.redo", _ => { Need().Redo(EditorHost.Now); EditorHost.Touched(null, null); return State(); });
            On("project.history", _ =>
            {
                var s2 = Need();
                s2.Flush();
                return new JArray(ProjectFiles.History(s2.Project).Select(h => new JObject
                {
                    ["id"] = h.id, ["utc"] = h.savedUtc.ToString("o"), ["npcs"] = h.npcs,
                }));
            });
            On("project.restore", a =>
            {
                var s2 = Need();
                s2.Flush();
                bool ok = false;
                s2.Change("restore", () => ok = ProjectFiles.Restore(s2.Project, a), EditorHost.Now);
                EditorHost.Touched(null, null);
                var st = State();
                st["ok"] = ok;
                if (!ok) st["error"] = "restore_failed";
                return st;
            });

            On("npc.add", a =>
            {
                var o = Obj(a);
                var s2 = Need();
                string role = (string)o["role"] ?? "civilian";
                JObject look = StartLook(s2.Project, (string)o["look"] ?? "random");
                JObject n = null;
                s2.Change("add", () =>
                {
                    n = NpcFactory.Create(s2.Project, (string)o["name"], role, look, GameData.Hints());
                    s2.Project.Npcs.Add(n);
                }, EditorHost.Now);
                string id = (string)n["id"];
                Core.Log.Msg($"[editor] NPC '{id}' added as {role}.");
                EditorHost.ShowPreview(id);
                var st = State();
                st["added"] = id;
                return st;
            });
            On("npc.remove", a =>
            {
                var s2 = Need();
                var ids = Ids(Obj(a));
                int removed = 0;
                s2.Change("remove", () => removed = NpcFactory.Remove(s2.Project, ids), EditorHost.Now);
                Core.Log.Msg($"[editor] {removed} NPC(s) removed.");
                return State();
            });
            On("npc.duplicate", a =>
            {
                var s2 = Need();
                string src = (string)Obj(a)["id"];
                var from = s2.Project.FindNpc(src);
                if (from == null) return Fail("npc_not_found");
                JObject n = null;
                s2.Change("duplicate", () => { n = NpcFactory.Duplicate(s2.Project, from); s2.Project.Npcs.Add(n); }, EditorHost.Now);
                string id = (string)n["id"];
                EditorHost.ShowPreview(id);
                var st = State();
                st["added"] = id;
                return st;
            });
            On("npc.setRole", a =>
            {
                var o = Obj(a);
                var s2 = Need();
                var ids = Ids(o);
                string role = (string)o["role"];
                if (!NpcFactory.Roles.Contains(role)) return Fail("role_unknown");
                var hints = GameData.Hints();
                s2.Change("role", () =>
                {
                    foreach (string id in ids)
                    {
                        var n = s2.Project.FindNpc(id);
                        if (n != null) NpcFactory.SetRole(n, role, hints);
                        else Core.Log.Warning($"[editor] role change: NPC '{id}' not found - skipped.");
                    }
                }, EditorHost.Now);
                return State();
            });
            On("npc.randomLook", a =>
            {
                var s2 = Need();
                string id = (string)Obj(a)["id"];
                var n = s2.Project.FindNpc(id);
                if (n == null) return Fail("npc_not_found");
                var look = LookBridge.RandomLook();
                s2.Change("look", () =>
                {
                    n["appearance"] = look;
                    (n[PackProject.EditorKey] as JObject)?.Remove("hiddenLayers");
                }, EditorHost.Now);
                EditorHost.Touched(new[] { id }, null);
                return State();
            });

            On("preview.show", a => { EditorHost.ShowPreview(a); return Ok; });
            On("preview.rotate", a => { Preview.RotateAvatar(Num(a)); return Ok; });
            On("preview.zoom", a => { Preview.ZoomCamera(Num(a)); return Ok; });
            On("portrait.capture", a =>
            {
                var s2 = Need();
                var n = s2.Project.FindNpc(a);
                if (n == null) return Fail("npc_not_found");
                string rel = LookBridge.CapturePortrait(s2.Project, n);
                if (rel == null) return Fail("portrait_failed");
                s2.Set(new[] { a }, "portrait", rel, EditorHost.Now);
                var d = Delta();
                d["portrait"] = rel;
                return d;
            });
            On("stage.rect", a =>
            {
                var o = Obj(a);
                EditorHost.SetStage((float?)o["x"] ?? 0f, (float?)o["y"] ?? 0f, (float?)o["width"] ?? 0f, (float?)o["height"] ?? 0f);
                return Ok;
            });
            On("stage.mode", a => { EditorHost.SetStageMode(a); return Ok; });

            On("catalog.look", _ => LookBridge.Catalog());
            On("catalog.pictures", _ => LookBridge.Pictures());
            On("look.importPicture", a =>
            {
                string rel = LookBridge.ImportPicture(Need().Project, Obj(a));
                return rel == null ? Fail("picture_failed") : new JObject { ["ok"] = true, ["file"] = rel };
            });
            On("catalog.world", _ => WorldBridge.Catalog());
            On("catalog.effects", _ => GameData.Effects());
            On("world.map", _ => WorldBridge.Map());
            On("world.ground", a => WorldBridge.Ground(Obj(a)));

            On("install", _ =>
            {
                var s2 = Need();
                s2.Flush();
                var r = Installer.Install(s2.Project, GameData.PacksRoot, GameData.KnownNpcs());
                Core.Log.Msg($"[editor] install '{s2.Project.Folder}': {r.code} - {r.text}");
                var d = Delta();
                d["ok"] = r.ok; d["code"] = r.code; d["text"] = r.text;
                return d;
            });
            On("uninstall", _ =>
            {
                var s2 = Need();
                s2.Flush();
                bool ok = Installer.Uninstall(s2.Project, GameData.PacksRoot);
                var d = Delta();
                d["ok"] = ok;
                return d;
            });
            On("publish", _ =>
            {
                var s2 = Need();
                s2.Flush();
                Directory.CreateDirectory(Paths.Exports);
                var r = Installer.Publish(s2.Project, Paths.Exports, GameData.PersonnelVersion() ?? "2.3.0", GameData.KnownNpcs());
                Core.Log.Msg($"[editor] publish '{s2.Project.Folder}': {r.code} - {r.path ?? r.text}");
                return new JObject { ["ok"] = r.ok, ["code"] = r.code, ["path"] = r.path ?? "", ["text"] = r.text ?? "" };
            });

            On("test.saves", _ => TestLaunch.Saves());
            On("test.start", a =>
            {
                var o = Obj(a);
                return TestLaunch.Start((int?)o["slot"] ?? 0, (string)o["npcId"]);
            });

            On("packs.list", _ => PacksBridge.List());
            On("packs.toggle", a => { var o = Obj(a); return PacksBridge.Toggle((string)o["folder"], (bool?)o["enabled"] ?? true); });
            On("packs.remove", a => PacksBridge.Remove(a));

            On("editor.exit", _ => { EditorHost.RequestExit(); return Ok; });
        }

        private static string Run(string name, string arg, Func<string, JToken> handler)
        {
            try
            {
                var r = handler(arg ?? "");
                return r?.ToString(Formatting.None) ?? "";
            }
            catch (NoProjectException)
            {
                Core.Log.Warning($"[editor] call '{name}' needs an open project - none is open.");
                return Fail("no_project").ToString(Formatting.None);
            }
            catch (Exception e)
            {
                Core.Log.Error($"[editor] call '{name}' failed: {e}");
                return "";
            }
        }

        private sealed class NoProjectException : Exception { }

        private static EditSession Need() => EditorHost.Session ?? throw new NoProjectException();

        internal static JObject Fail(string code) => new JObject { ["ok"] = false, ["error"] = code };

        private static JObject Obj(string a)
        {
            if (string.IsNullOrWhiteSpace(a)) return new JObject();
            return JObject.Parse(a);
        }

        private static List<string> Ids(JObject o)
            => (o["ids"] as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();

        private static float Num(string a)
            => float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;

        private static JObject StartLook(PackProject p, string look)
        {
            if (look == "menu") return LookBridge.MenuLook();
            if (look.StartsWith("copy:", StringComparison.Ordinal))
            {
                var src = p.FindNpc(look.Substring(5));
                if (src?["appearance"] is JObject a) return (JObject)a.DeepClone();
                Core.Log.Warning($"[editor] start look '{look}': that NPC has no look - a random one is used.");
            }
            else if (look.StartsWith("template:", StringComparison.Ordinal))
            {
                var t = LookBridge.Template(look.Substring(9));
                if (t != null) return t;
                Core.Log.Warning($"[editor] start look '{look}': unknown template - a random one is used.");
            }
            return LookBridge.RandomLook();
        }

        // ---- project state for the page ----------------------------------------------------------------------------

        internal static JObject ProjectJson(PackProject p)
        {
            var manifest = (JObject)p.Manifest.DeepClone();
            manifest.Remove("npcs");
            var editor = manifest[PackProject.EditorKey] as JObject ?? new JObject();
            manifest.Remove(PackProject.EditorKey);
            return new JObject
            {
                ["folder"] = p.Folder, ["name"] = p.Name, ["editor"] = editor, ["manifest"] = manifest, ["npcs"] = p.Npcs.DeepClone(),
            };
        }

        internal static JArray ProblemsJson(PackProject p)
            => new JArray(Problems.Check(p, GameData.KnownNpcs()).Select(x => new JObject
            {
                ["severity"] = x.severity, ["code"] = x.code, ["npc"] = x.npc, ["area"] = x.area,
                ["field"] = x.field, ["arg"] = x.arg, ["text"] = x.text,
            }));

        internal static JObject State()
        {
            var s = EditorHost.Session;
            if (s == null) return Fail("no_project");
            var d = Delta();
            d["project"] = ProjectJson(s.Project);
            return d;
        }

        internal static JObject Delta()
        {
            var s = EditorHost.Session;
            if (s == null) return Fail("no_project");
            return new JObject
            {
                ["ok"] = true,
                ["problems"] = ProblemsJson(s.Project),
                ["install"] = Installer.State(s.Project, GameData.PacksRoot),
                ["canUndo"] = s.CanUndo,
                ["canRedo"] = s.CanRedo,
            };
        }

        private static string SideloadVersion()
        {
            foreach (var m in MelonLoader.MelonMod.RegisteredMelons)
                if (m?.Info?.Name == "Sideload") return m.Info.Version ?? "";
            return "";
        }
    }
}
