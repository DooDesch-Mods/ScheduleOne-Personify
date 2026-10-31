using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Editor;
using Personify.Project;

// Checks of the Personify project core. Every run works in a fresh temp folder; an optional argument names a
// folder of real format 1 projects (UserData/Personify/Projects) that are migrated as copies.
int failed = 0, passed = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) passed++; else failed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  - " + detail : "")}");
}

var logs = new List<string>();
ProjectLog.Info = m => logs.Add("info " + m);
ProjectLog.Warn = m => logs.Add("warn " + m);
ProjectLog.Error = m => logs.Add("error " + m);

string root = Path.Combine(Path.GetTempPath(), "personify-corecheck-" + Guid.NewGuid().ToString("N"));
string projects = Path.Combine(root, "Projects"), packs = Path.Combine(root, "Packs"), exports = Path.Combine(root, "Exports");
Directory.CreateDirectory(projects);
ProjectFiles.Root = projects;

try
{
    // 1. Create, save, reload, history.
    var p = ProjectFiles.Create("My Crew", "Tester");
    Check("create writes manifest.json", p != null && File.Exists(Path.Combine(p.Dir, "manifest.json")));
    p.Npcs.Add(new JObject { ["id"] = "my_crew_ana", ["name"] = "Ana", ["customer"] = new JObject(), ["relationships"] = new JObject { ["unlocked"] = true } });
    ProjectFiles.Save(p);
    var again = ProjectFiles.Load(p.Folder);
    Check("reload keeps the NPC", again?.FindNpc("my_crew_ana") != null);
    Check("pack carries schemaVersion 3 and editor block", (int?)again.Manifest["schemaVersion"] == 3 && (int?)again.Editor["formatVersion"] == 2);
    Check("install manifest spawns NPCs when autoRegister is unset", (bool?)again.InstallManifest()["autoRegister"] == true);
    again.Manifest["autoRegister"] = false;
    Check("install manifest keeps autoRegister switched off", (bool?)again.InstallManifest()["autoRegister"] == false);
    again.Manifest.Remove("autoRegister");
    var hist = ProjectFiles.History(again);
    Check("history has an entry", hist.Count >= 1, $"{hist.Count} entries");
    ProjectFiles.Save(again, snapshot: true, forceSnapshot: true);
    again.Npcs.Clear();
    ProjectFiles.Save(again, snapshot: true, forceSnapshot: true);
    var h2 = ProjectFiles.History(again);
    var withAna = h2.Find(h => h.npcs == 1);
    Check("restore brings an older state back", withAna != null && ProjectFiles.Restore(again, withAna.id) && ProjectFiles.Load(p.Folder).Npcs.Count == 1);
    Check("second project with the same name gets its own folder", ProjectFiles.Create("My Crew", "")?.Folder == "My Crew 2");

    // 2. Migration from format 1.
    string v1Dir = Path.Combine(projects, "Old Pack");
    Directory.CreateDirectory(Path.Combine(v1Dir, "sources"));
    File.WriteAllBytes(Path.Combine(v1Dir, "sources", "grin.png"), new byte[] { 137, 80, 78, 71 });
    var v1 = new NpcProject { Name = "Old Pack", Author = "A", ModVersion = "1.2", Description = "d" };
    var draft = new NpcDraft { Name = "Bob Stone" };
    draft.Appearance.FaceLayers.Add(new LayerDraft { Source = "sources/grin.png", Tint = "#FF0000" });
    draft.Appearance.FaceLayers.Add(new LayerDraft { Path = "slightsmile", Visible = false });
    draft.Appearance.Distortion["Head"] = new BoneDistortionDraft { ScaleX = 1.5f };
    draft.Behavior = new BehaviorDraft { Enabled = false, Aggression = 0.7f };
    draft.Backrooms = new BackroomsDraft { Enabled = true, Biomes = "L0, L1" };
    draft.Customer = JObject.Parse("{\"standards\":\"High\"}");
    draft.Relationships = JObject.Parse("{\"connections\":[\"kyle_cooley\"],\"futureField\":5}");
    v1.Npcs.Add(draft);
    File.WriteAllText(Path.Combine(v1Dir, "project.json"), JsonConvert.SerializeObject(v1));
    var list = ProjectFiles.List();
    Check("list shows the old project as format 1", list.Exists(s => s.folder == "Old Pack" && s.formatVersion == 1));
    var mig = ProjectFiles.Load("Old Pack");
    var bob = mig?.FindNpc("old_pack_bob_stone");
    Check("migration keeps the 1.x id", bob != null);
    Check("migration keeps the custom layer as a pack file", (string)bob?["appearance"]?["faceLayers"]?[0]?["file"] == "sources/grin.png");
    Check("hidden layer moves to the editor block", (string)bob?["personify"]?["hiddenLayers"]?[0]?["path"] == "slightsmile" && (string)bob["personify"]["hiddenLayers"][0]["list"] == "faceLayers"
                                                    && ((JArray)bob["appearance"]["faceLayers"]).Count == 1);
    Check("switched-off behaviour is kept, not exported", bob?["behavior"] == null && (float?)bob?["personify"]?["behaviorOff"]?["aggression"] == 0.7f);
    Check("backrooms extension exported", (string)bob?["extensions"]?["backrooms"]?["biomes"]?[1] == "L1");
    Check("unknown field inside a block survives", (int?)bob?["relationships"]?["futureField"] == 5);
    Check("distortion migrated", (float?)bob?["appearance"]?["distortion"]?["Head"]?["scaleX"] == 1.5f);
    Check("backup of project.json kept", File.Exists(Path.Combine(v1Dir, ".personify", "project.v1.json")) && !File.Exists(Path.Combine(v1Dir, "project.json")));
    Check("publish metadata migrated", (string)mig?.Editor["modVersion"] == "1.2" && (string)mig.Editor["description"] == "d");

    // 3. Problems.
    var probs = Problems.Check(mig, new List<KnownNpc> { new KnownNpc { id = "kyle_cooley", name = "Kyle", source = "vanilla" } });
    Check("migrated pack has no errors", Problems.ErrorCount(probs) == 0, string.Join("; ", probs.ConvertAll(x => x.code)));
    var bad = ProjectFiles.Create("Bad", "");
    bad.Npcs.Add(new JObject { ["id"] = "dup", ["name"] = "A", ["customer"] = new JObject() });
    bad.Npcs.Add(new JObject { ["id"] = "dup", ["name"] = "B", ["portrait"] = "nope.png", ["dealer"] = new JObject(), ["customer"] = new JObject() });
    bad.Npcs.Add(new JObject { ["id"] = "Bad Id", ["name"] = "", ["relationships"] = new JObject { ["connections"] = new JArray("ghost") } });
    bad.Npcs.Add(new JObject { ["id"] = "kyle_cooley", ["name"] = "K", ["supplier"] = new JObject(), ["spawn"] = new JObject { ["physical"] = false } });
    var bp = Problems.Check(bad, new List<KnownNpc> { new KnownNpc { id = "kyle_cooley", name = "Kyle Cooley", source = "vanilla" } });
    var codes = new HashSet<string>(bp.ConvertAll(x => x.code));
    foreach (string c in new[] { "npc_id_duplicate", "file_missing", "roles_conflict", "dealer_no_home", "npc_id_invalid", "npc_name_missing",
                                  "connection_unknown", "customer_no_connection", "npc_id_taken", "supplier_no_items", "supplier_not_physical" })
        Check("problem " + c, codes.Contains(c));
    var noCat = Problems.Check(bad, null);
    Check("without a catalog no connection_unknown", !noCat.Exists(x => x.code == "connection_unknown"));

    // 4. Install, state, rename, blocked.
    Check("state before install", Installer.State(mig, packs) == "not_installed");
    var ir = Installer.Install(mig, packs);
    Check("install succeeds", ir.ok, ir.text);
    string inst = Path.Combine(packs, "Old Pack");
    var instManifest = JObject.Parse(File.ReadAllText(Path.Combine(inst, "manifest.json")));
    Check("installed manifest has no editor blocks", instManifest["personify"] == null && instManifest["npcs"][0]["personify"] == null);
    Check("installed pack carries the picture", File.Exists(Path.Combine(inst, "sources", "grin.png")));
    Check("installed pack has no editor folder", !Directory.Exists(Path.Combine(inst, ".personify")));
    Check("state after install", Installer.State(mig, packs) == "installed");
    mig.FindNpc("old_pack_bob_stone")["name"] = "Bob Stoner";
    ProjectFiles.Save(mig);
    Check("state after an edit", Installer.State(mig, packs) == "changed");
    mig.Name = "Renamed Pack";
    ProjectFiles.Save(mig);
    Check("install after rename", Installer.Install(mig, packs).ok);
    Check("rename removes the old install", !Directory.Exists(inst) && Directory.Exists(Path.Combine(packs, "Renamed Pack")));
    Check("errors block the install", Installer.Install(bad, packs).code == "blocked" && !Directory.Exists(Path.Combine(packs, "Bad")));
    Check("list reports install state", ProjectFiles.List(x => Installer.State(x, packs)).Exists(s => s.folder == "Old Pack" && s.install == "installed"));

    // 5. Import a folder with fields the editor does not know, install, compare.
    string foreign = Path.Combine(root, "ForeignPack");
    Directory.CreateDirectory(Path.Combine(foreign, "img"));
    File.WriteAllBytes(Path.Combine(foreign, "img", "face.png"), new byte[] { 1, 2, 3 });
    var fm = JObject.Parse("{\"name\":\"Foreign\",\"schemaVersion\":2,\"packId\":\"foreign\",\"customTop\":{\"a\":1},\"npcs\":[{\"id\":\"foreign_x\",\"name\":\"X\",\"weirdField\":[1,2],\"appearance\":{\"faceLayers\":[{\"file\":\"img/face.png\",\"kind\":\"face\"}]},\"relationships\":{\"unlocked\":true}}]}");
    File.WriteAllText(Path.Combine(foreign, "manifest.json"), fm.ToString());
    var imp = PackImport.Import(foreign);
    Check("import a folder", imp.Count == 1 && imp[0].FindNpc("foreign_x") != null);
    Check("imported picture copied", imp.Count == 1 && File.Exists(Path.Combine(imp[0].Dir, "img", "face.png")));
    Installer.Install(imp[0], packs);
    var back = JObject.Parse(File.ReadAllText(Path.Combine(packs, "Foreign", "manifest.json")));
    Check("unknown fields survive import and install", (int?)back["customTop"]?["a"] == 1 && (int?)back["npcs"][0]["weirdField"][1] == 2 && (string)back["packId"] == "foreign");
    Check("install raises schemaVersion to 3", (int?)back["schemaVersion"] == 3);

    // 6. Import a Thunderstore zip, then publish one.
    string zipPath = Path.Combine(root, "Some-Pack-1.0.0.zip");
    using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
        void Put(string n, string t) { using var w = new StreamWriter(z.CreateEntry(n).Open()); w.Write(t); }
        Put("manifest.json", "{\"name\":\"Some_Pack\",\"version_number\":\"1.0.0\"}");
        Put("Personnel/Packs/Zipped/manifest.json", "{\"name\":\"Zipped\",\"npcs\":[{\"id\":\"zipped_a\",\"name\":\"A\"}]}");
        Put("../evil.txt", "x");
    }
    var zi = PackImport.Import(zipPath);
    Check("import a zip finds the pack, skips the Thunderstore manifest", zi.Count == 1 && zi[0].Name == "Zipped");
    Check("zip entry outside the archive is skipped", !File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")) && logs.Exists(l => l.Contains("points outside")));
    var pub = Installer.Publish(mig, exports, "2.1.0");
    Check("publish builds a zip", pub.ok && File.Exists(pub.path), pub.path ?? pub.text);
    if (pub.ok)
        using (var z = ZipFile.OpenRead(pub.path))
        {
            var names = new HashSet<string>(z.Entries.Select(e => e.FullName));
            Check("zip layout", names.Contains("manifest.json") && names.Contains("README.md") && names.Contains("LICENSE")
                                && names.Contains("Personnel/Packs/Renamed Pack/manifest.json") && names.Contains("Personnel/Packs/Renamed Pack/sources/grin.png"),
                  string.Join(", ", names));
            var ts = JObject.Parse(new StreamReader(z.GetEntry("manifest.json").Open()).ReadToEnd());
            Check("thunderstore manifest", (string)ts["name"] == "Renamed_Pack" && (string)ts["version_number"] == "1.2.0" && (string)ts["dependencies"][0] == "DooDesch-Personnel-2.1.0");
        }

    // 6b. Edit session: set by path, multi-select, undo merge, redo, autosave.
    ProjectFiles.Root = projects;
    var ep = ProjectFiles.Create("Edit Pack", "");
    ep.Npcs.Add(NpcFactory.Create(ep, "Ana", "customer", null, new NpcFactory.Hints { Connection = "kyle_cooley" }));
    ep.Npcs.Add(NpcFactory.Create(ep, "Ana", "dealer", null, new NpcFactory.Hints { DealerHome = "Motel Room 1" }));
    ep.Npcs.Add(NpcFactory.Create(ep, "Sully", "supplier", null, null));
    ProjectFiles.Save(ep);
    var ids = ep.NpcIds().ToList();
    Check("factory gives a free id on a name clash", ids.Contains("edit_pack_ana") && ids.Contains("edit_pack_ana_2"), string.Join(",", ids));
    var ana = ep.FindNpc("edit_pack_ana");
    Check("new customer is playable", (string)ana["relationships"]?["connections"]?[0] == "kyle_cooley" && (int?)ana["customer"]?["spending"]?["max"] == 500);
    Check("new dealer has home and vanilla cut", (string)ep.FindNpc("edit_pack_ana_2")["dealer"]?["home"] == "Motel Room 1" && (double?)ep.FindNpc("edit_pack_ana_2")["dealer"]?["cut"] == 0.2);
    Check("new supplier sells something", ep.FindNpc("edit_pack_sully")["supplier"]?["items"]?[0] != null);
    Check("new NPCs raise no error", Problems.ErrorCount(Problems.Check(ep)) == 0, string.Join("; ", Problems.Check(ep).ConvertAll(x => x.code)));

    var es = new EditSession(ep);
    double t0 = 100;
    es.Set(new[] { "edit_pack_ana", "edit_pack_sully" }, "appearance.height", 1.1, t0);
    Check("multi-select sets every NPC", (double?)ep.FindNpc("edit_pack_sully")["appearance"]?["height"] == 1.1 && (double?)ana["appearance"]?["height"] == 1.1);
    es.Set(new[] { "edit_pack_ana" }, "appearance.weight", 0.2, t0 + 0.1);
    es.Set(new[] { "edit_pack_ana" }, "appearance.weight", 0.3, t0 + 0.2);
    es.Set(new[] { "edit_pack_ana" }, "appearance.weight", 0.4, t0 + 0.3);
    Check("a drag is one undo step", es.Undo(t0 + 1) && (double?)es.Project.FindNpc("edit_pack_ana")["appearance"]?["weight"] == null
                                     && (double?)es.Project.FindNpc("edit_pack_ana")["appearance"]?["height"] == 1.1);
    Check("redo brings the drag back", es.Redo(t0 + 2) && (double?)es.Project.FindNpc("edit_pack_ana")["appearance"]?["weight"] == 0.4);
    es.Set(Array.Empty<string>(), "editor.modVersion", "1.1.0", t0 + 3);
    Check("pack field through editor block", (string)es.Project.Editor["modVersion"] == "1.1.0");
    es.Set(new[] { "edit_pack_ana" }, "appearance.faceLayers.0", JObject.Parse("{\"path\":\"Avatar/Layers/Face/Face_SlightSmile\",\"tint\":\"#FFFFFF\"}"), t0 + 4);
    Check("set creates a list entry", (string)es.Project.FindNpc("edit_pack_ana")["appearance"]?["faceLayers"]?[0]?["path"] == "Avatar/Layers/Face/Face_SlightSmile");
    es.Unset(new[] { "edit_pack_ana" }, "appearance.faceLayers.0", t0 + 5);
    Check("unset removes a list entry", ((JArray)es.Project.FindNpc("edit_pack_ana")["appearance"]["faceLayers"]).Count == 0);
    Check("nothing written before the edits settle", !es.Tick(t0 + 5.1) && es.Dirty);
    Check("autosave after the delay", es.Tick(t0 + 5 + EditSession.SaveDelay + 0.01) && !es.Dirty
          && (string)ProjectFiles.Load(ep.Folder).Editor["modVersion"] == "1.1.0");
    es.Change("role", () => NpcFactory.SetRole(es.Project.FindNpc("edit_pack_ana"), "dealer", null), t0 + 10);
    var anaNow = es.Project.FindNpc("edit_pack_ana");
    Check("role switch removes the old block", anaNow["customer"] == null && anaNow["dealer"] is JObject);
    Check("role switch is undoable", es.Undo(t0 + 11) && es.Project.FindNpc("edit_pack_ana")["customer"] is JObject);
    es.Change("remove", () => NpcFactory.Remove(es.Project, new[] { "edit_pack_ana_2" }), t0 + 12);
    Check("remove drops the NPC", es.Project.FindNpc("edit_pack_ana_2") == null);

    var es2 = new EditSession(ep);
    es2.Apply(new[] { "edit_pack_sully" }, new[] { new FieldChange { Path = "appearance.skinColor", Value = "#111111" }, new FieldChange { Path = "appearance.leftEyeLidColor", Value = "#111111" } }, 500);
    es2.Apply(new[] { "edit_pack_sully" }, new[] { new FieldChange { Path = "appearance.skinColor", Value = "#222222" }, new FieldChange { Path = "appearance.leftEyeLidColor", Value = "#222222" } }, 500.3);
    Check("a batch and its live values are one undo step", es2.Undo(501) && es2.Project.FindNpc("edit_pack_sully")["appearance"]?["skinColor"] == null && !es2.CanUndo);
    es2.Apply(new[] { "edit_pack_sully" }, new[] { new FieldChange { Path = "behavior.scale", Value = 1.2 }, new FieldChange { Path = "behavior.scale", Unset = true } }, 600);
    Check("batch unset removes the field", es2.Project.FindNpc("edit_pack_sully")["behavior"]?["scale"] == null);

    // 6c. Look round trip through the preview draft.
    var look = JObject.Parse("{\"appearance\":{\"gender\":0.7,\"leftEye\":{\"top\":0.3,\"bottom\":0.6},\"faceLayers\":[{\"file\":\"sources/a.png\",\"kind\":\"face\",\"tint\":\"#FF0000\"}],\"accessories\":[{\"path\":\"Avatar/Accessories/Head/Cap/Cap\",\"color\":\"#00FF00\"}],\"distortion\":{\"Head\":{\"scaleX\":2}}},\"personify\":{\"hiddenLayers\":[{\"list\":\"bodyLayers\",\"index\":0,\"path\":\"x\",\"tint\":\"#FFFFFF\"}]}}");
    var dr = LookJson.ToDraft(look);
    Check("draft reads the manifest look", dr.Gender == 0.7f && dr.LeftEyeTop == 0.3f && dr.FaceLayers[0].Source == "sources/a.png"
          && dr.Accessories[0].Tint == "#00FF00" && dr.Distortion["Head"].ScaleX == 2f && dr.BodyLayers.Count == 1 && !dr.BodyLayers[0].Visible);
    var hid = new JArray();
    var back2 = LookJson.FromDraft(dr, hid);
    Check("draft writes the same look back", (string)back2["faceLayers"][0]["file"] == "sources/a.png" && (string)back2["accessories"][0]["color"] == "#00FF00"
          && (string)hid[0]?["path"] == "x" && (string)hid[0]["list"] == "bodyLayers" && Math.Abs((double)back2["leftEye"]["bottom"] - 0.6) < 1e-5);

    // 7. Real format 1 projects, migrated as copies.
    if (args.Length > 0 && Directory.Exists(args[0]))
    {
        string copyRoot = Path.Combine(root, "Real");
        foreach (string d in Directory.GetDirectories(args[0]))
        {
            if (!File.Exists(Path.Combine(d, "project.json"))) continue;
            string dest = Path.Combine(copyRoot, Path.GetFileName(d));
            Directory.CreateDirectory(dest);
            foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
            {
                string t = Path.Combine(dest, Path.GetRelativePath(d, f));
                Directory.CreateDirectory(Path.GetDirectoryName(t));
                File.Copy(f, t);
            }
        }
        ProjectFiles.Root = copyRoot;
        foreach (var s in ProjectFiles.List())
        {
            var real = ProjectFiles.Load(s.folder);
            var rp = real == null ? null : Problems.Check(real);
            Check($"real project '{s.folder}' migrates", real != null && real.Npcs.Count == s.npcs,
                  real == null ? "load failed" : $"{real.Npcs.Count} NPC(s), {Problems.ErrorCount(rp)} error(s): {string.Join("; ", rp.FindAll(x => x.severity == "error").ConvertAll(x => x.text))}");
        }
    }
}
catch (Exception e)
{
    Check("no exception", false, e.ToString());
}
finally
{
    foreach (var l in logs.Where(l => l.StartsWith("error"))) Console.WriteLine("  log: " + l);
    try { Directory.Delete(root, true); } catch (Exception e) { Console.WriteLine("temp folder not removed: " + e.Message); }
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
