#if DEBUG
using System;
using System.IO;
using MelonLoader.Utils;
using SideHustle;
using UnityEngine;
using UnityEngine.UI;

namespace Personify.Editor
{
    // DEBUG-only, file-driven UI self-test so the MCP dev loop can exercise the editor without mouse input.
    // Drop UserData/Personify/selftest.txt with a command while the game sits at the main menu:
    //   inkpicker      -> open the editor on a fresh pack, switch to Advanced, click "+ Add face layer"
    //                     (the option picker is left open so a screenshot can capture it)
    //   pick <label>   -> click the row with that display label in the currently open option picker
    //   skin #RRGGBB   -> drive the skin colour the way the Skin row does, then dump
    //   gender m|f     -> drive the gender toggle the way the Gender row does, then dump
    //   tab <name>     -> switch to character | advanced | experimental | world
    //   world click <x> <z>      -> click the World map at that world position, the way a mouse click does
    //   world choose <n|word>    -> pick entry n (1-based) of the open place menu, or the first starting with word
    //   export         -> save and export the open pack (installs it into Personnel when Personnel is loaded)
    //   world pin <i|spawn>      -> click a pin; world drag <i|spawn> <x> <z> -> drag it there and let go
    //   world hover <x> <z>      -> show the hover ring the mouse would show there
    //   world undo               -> press Undo
    //   world select <i|spawn|none> -> select stop i (1-based), the start point, or nothing
    //   world focus <x> <z> [zoom] -> centre the map on a world position
    //   world dump              -> log the start point, every stop and what the map shows
    //   eyelids on|off -> flip the "Eyelids follow skin" switch
    //   scroll <0..1>  -> move the form's scroll position (1 = top, 0 = bottom)
    //   set <f> <v>    -> write one appearance float (gender|height|weight|pupil|browscale|browthick|
    //                     browheight|browangle), then dump
    //   packs          -> save and go back to the pack-select screen
    //   open <folder>  -> load that pack folder the way clicking its row does
    //   newpack <name> -> create a pack seeded from the menu character and open it
    //   customer on    -> enable behaviour and set the conversation to Customer, the way the two rows do
    //   customer start|sample -> pick that segment of the Unlock row
    //   customer add   -> click "+ Add contact" (the picker stays open for "pick <name>")
    //   customer effect -> click "+ Add effect" (the picker stays open for "pick <name>")
    //   customer dump  -> log the relationships and customer blocks the export will write
    //   row <label> <value> -> drive the last form row with that label through its own control: a slider takes a
    //                     number, a switch on|off, a segmented row a segment label, a stepper + or -, a picker row
    //                     opens its picker (then "pick <name>")
    //   resolve        -> resolve every catalog path against the game's avatar object library and log the misses
    //   dump           -> log the AvatarSettings the selected NPC actually builds (colours + layer order),
    //                     which is the only way to check the derived values without a mouse
    // Each command file is consumed (deleted) and progress is logged with a [selftest] prefix.
    public static partial class EditorUI
    {
        private static float _stNextPoll;
        private static int _stStep;
        private static float _stStepAt;
        private static string _stPickLabel;

        internal static void SelfTestTick()
        {
            if (_stStep > 0) { SelfTestInkPickerStep(); return; }
            if (_stPickLabel != null) { SelfTestPick(); return; }
            if (Time.unscaledTime < _stNextPoll) return;
            _stNextPoll = Time.unscaledTime + 2f;

            string path = Path.Combine(MelonEnvironment.UserDataDirectory, "Personify", "selftest.txt");
            string cmd;
            try
            {
                if (!File.Exists(path)) return;
                cmd = File.ReadAllText(path).Trim();
                File.Delete(path);
            }
            catch (Exception e) { Core.Log?.Warning("[selftest] could not read the command file: " + e.Message); return; }
            if (string.IsNullOrEmpty(cmd)) return;

            if (cmd == "inkpicker") { _stStep = 1; _stStepAt = Time.unscaledTime; Core.Log?.Msg("[selftest] inkpicker starting"); }
            else if (cmd.StartsWith("pick ", StringComparison.Ordinal)) _stPickLabel = cmd.Substring(5).Trim();
            else if (cmd.StartsWith("skin ", StringComparison.Ordinal)) SelfTestSkin(cmd.Substring(5).Trim());
            else if (cmd.StartsWith("gender ", StringComparison.Ordinal)) SelfTestGender(cmd.Substring(7).Trim());
            else if (cmd == "dump") SelfTestDump();
            else if (cmd.StartsWith("customer ", StringComparison.Ordinal)) SelfTestCustomer(cmd.Substring(9).Trim());
            else if (cmd.StartsWith("row ", StringComparison.Ordinal)) SelfTestRow(cmd.Substring(4).Trim());
            else if (cmd == "resolve") SelfTestResolve();
            else if (cmd.StartsWith("world ", StringComparison.Ordinal)) SelfTestWorld(cmd.Substring(6).Trim());
            else if (cmd.StartsWith("tab ", StringComparison.Ordinal)) SelfTestTab(cmd.Substring(4).Trim());
            else if (cmd.StartsWith("eyelids ", StringComparison.Ordinal)) SelfTestEyelids(cmd.Substring(8).Trim());
            else if (cmd.StartsWith("scroll ", StringComparison.Ordinal)) SelfTestScroll(cmd.Substring(7).Trim());
            else if (cmd.StartsWith("set ", StringComparison.Ordinal)) SelfTestSet(cmd.Substring(4).Trim());
            else if (cmd == "packs") { ProjectStore.Save(_project); ShowProjectSelect(); Core.Log?.Msg("[selftest] back at pack select"); }
            else if (cmd.StartsWith("open ", StringComparison.Ordinal)) SelfTestOpen(cmd.Substring(5).Trim());
            else if (cmd.StartsWith("newpack ", StringComparison.Ordinal)) SelfTestNewPack(cmd.Substring(8).Trim());
            else if (cmd == "export")
            {
                ProjectStore.Save(_project);
                var r = Exporter.Export(_project);
                Core.Log?.Msg($"[selftest] export ok={r.Ok}: {r.Message} (installed to {r.InstalledDir ?? "nowhere"})");
            }
            else Core.Log?.Warning("[selftest] unknown command: " + cmd);
        }

        private static void SelfTestInkPickerStep()
        {
            if (Time.unscaledTime < _stStepAt) return;
            try
            {
                switch (_stStep)
                {
                    case 1:   // open the editor the way a hub launch would
                        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Menu")
                        { Core.Log?.Warning("[selftest] not on the Menu scene, aborting"); _stStep = 0; return; }
                        Open(new LaunchContext());
                        SelfTestNext(0.5f); return;

                    case 2:   // fresh pack + first NPC, straight into the editor
                    {
                        string name = "SelfTest " + DateTime.Now.ToString("HHmmss");
                        _project = ProjectStore.Create(name);
                        if (_project.Npcs.Count > 0) _project.Npcs[0].Appearance = Preview.SeedFromMenu();
                        ProjectStore.Save(_project);
                        _selected = _project.Npcs.Count > 0 ? _project.Npcs[0] : null;
                        ShowEditor();
                        SelfTestNext(0.5f); return;
                    }

                    case 3:
                        _mode = FormMode.Advanced;
                        RefreshForm();
                        SelfTestNext(0.5f); return;

                    case 4:   // the first "addlyr" button in the form is the face-layer section's
                    {
                        Button add = SelfTestFindButton(_formContent, "addlyr");
                        if (add == null) { Core.Log?.Warning("[selftest] face-layer add button not found"); _stStep = 0; return; }
                        int vanilla = PathCatalog.FaceLayers().Count;
                        int merged = WithInkOptions(PathCatalog.FaceLayers(), true).Count;
                        add.onClick.Invoke();
                        Core.Log?.Msg($"[selftest] face picker open: {merged} options ({merged - vanilla} from Inkorporated packs)");
                        _stStep = 0; return;
                    }
                }
            }
            catch (Exception e) { Core.Log?.Error("[selftest] step " + _stStep + " threw: " + e); _stStep = 0; }
        }

        private static void SelfTestNext(float delay) { _stStep++; _stStepAt = Time.unscaledTime + delay; }

        private static void SelfTestPick()
        {
            string label = _stPickLabel; _stPickLabel = null;
            try
            {
                Transform scrim = _canvasGO != null ? _canvasGO.transform.Find("DD_OptionScrim") : null;
                if (scrim == null) { Core.Log?.Warning("[selftest] no option picker open"); return; }
                foreach (Button b in scrim.GetComponentsInChildren<Button>(true))
                {
                    if (b.gameObject.name != "opt") continue;
                    Text t = b.GetComponentInChildren<Text>(true);
                    if (t == null || !string.Equals(t.text, label, StringComparison.OrdinalIgnoreCase)) continue;
                    b.onClick.Invoke();
                    var list = _selected?.Appearance.FaceLayers;
                    string last = list != null && list.Count > 0 ? (list[list.Count - 1].Source ?? list[list.Count - 1].Path) : "(none)";
                    Core.Log?.Msg($"[selftest] picked '{label}'; face layers now {list?.Count ?? 0}, last = {last}");
                    return;
                }
                Core.Log?.Warning($"[selftest] no picker row labelled '{label}'");
            }
            catch (Exception e) { Core.Log?.Error("[selftest] pick threw: " + e); }
        }

        private static void SelfTestCustomer(string arg)
        {
            NpcDraft n = _selected;
            if (n == null) { Core.Log?.Warning("[selftest] customer: no NPC selected"); return; }
            switch (arg)
            {
                case "on":
                    n.Behavior.Enabled = true;
                    n.Behavior.Conversation = "customer";
                    RefreshForm(); MarkDirty();
                    break;
                case "start":
                case "sample":
                    // The Unlock row's own callback body.
                    WriteCustomerUnlock(n, arg == "sample", ConnectionIds(n.Relationships as Newtonsoft.Json.Linq.JObject));
                    RefreshForm(); MarkDirty();
                    break;
                case "add":
                {
                    Button add = SelfTestFindButton(_formContent, "addcon");
                    if (add == null) { Core.Log?.Warning("[selftest] customer: no '+ Add contact' button in the form"); return; }
                    add.onClick.Invoke();
                    Core.Log?.Msg("[selftest] customer: contact picker opened");
                    return;
                }
                case "effect":
                {
                    Button add = SelfTestFindButton(_formContent, "addeff");
                    if (add == null) { Core.Log?.Warning("[selftest] customer: no '+ Add effect' button in the form"); return; }
                    add.onClick.Invoke();
                    Core.Log?.Msg("[selftest] customer: effect picker opened");
                    return;
                }
                case "dump":
                    break;
                default:
                    Core.Log?.Warning("[selftest] customer: unknown command '" + arg + "'");
                    return;
            }
            string rel = n.Relationships == null ? "(none)" : n.Relationships.ToString(Newtonsoft.Json.Formatting.None);
            string cust = n.Customer == null ? "(none)" : n.Customer.ToString(Newtonsoft.Json.Formatting.None);
            Core.Log?.Msg($"[selftest] customer '{n.Name}': behaviour={n.Behavior.Enabled} conversation={n.Behavior.Conversation} " +
                          $"addButton={(SelfTestFindButton(_formContent, "addcon") != null)} relationships={rel} customer={cust}");
        }

        // Label and value are split at the last space, so labels with spaces ("Orders/week min") work.
        private static void SelfTestRow(string arg)
        {
            int cut = arg.LastIndexOf(' ');
            if (cut <= 0) { Core.Log?.Warning("[selftest] row: expected '<label> <value>'"); return; }
            string label = arg.Substring(0, cut).Trim(), value = arg.Substring(cut + 1).Trim();
            if (_formContent == null) { Core.Log?.Warning("[selftest] row: no form"); return; }

            Transform row = null;
            for (int i = 0; i < _formContent.childCount; i++)
            {
                Transform child = _formContent.GetChild(i);
                foreach (Text t in child.GetComponentsInChildren<Text>(true))
                    if (string.Equals(t.text, label, StringComparison.Ordinal)) { row = child; break; }
            }
            if (row == null) { Core.Log?.Warning($"[selftest] row: no row labelled '{label}'"); return; }

            try
            {
                Slider slider = row.GetComponentInChildren<Slider>(true);
                if (slider != null)
                {
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    { Core.Log?.Warning($"[selftest] row '{label}': '{value}' is not a number"); return; }
                    slider.value = v;
                    Core.Log?.Msg($"[selftest] row '{label}': slider set to {slider.value:0.##}");
                    return;
                }
                Toggle toggle = row.GetComponentInChildren<Toggle>(true);
                if (toggle != null)
                {
                    toggle.isOn = value.StartsWith("on", StringComparison.OrdinalIgnoreCase);
                    Core.Log?.Msg($"[selftest] row '{label}': switch {(toggle.isOn ? "on" : "off")}");
                    return;
                }
                foreach (Button b in row.GetComponentsInChildren<Button>(true))
                {
                    string name = b.gameObject.name;
                    Text bt = b.GetComponentInChildren<Text>(true);
                    bool segment = name.StartsWith("seg_", StringComparison.Ordinal) && bt != null && string.Equals(bt.text, value, StringComparison.OrdinalIgnoreCase);
                    bool step = (name == "plus" && value == "+") || (name == "minus" && value == "-");
                    bool picker = name == "choice" || name == "pick";
                    if (!segment && !step && !picker) continue;
                    if (!b.interactable) { Core.Log?.Warning($"[selftest] row '{label}': '{name}' is disabled"); return; }
                    b.onClick.Invoke();
                    Core.Log?.Msg($"[selftest] row '{label}': clicked '{name}'{(segment ? " (" + bt.text + ")" : "")}");
                    return;
                }
                Core.Log?.Warning($"[selftest] row '{label}': no control matches '{value}'");
            }
            catch (Exception e) { Core.Log?.Error($"[selftest] row '{label}' threw: {e}"); }
        }

        private static void SelfTestSkin(string hex)
        {
            var a = _selected?.Appearance;
            if (a == null) { Core.Log?.Warning("[selftest] no NPC selected"); return; }
            VanillaDerived.SetSkinColor(a, hex);   // the exact call the Skin colour row makes
            RefreshForm(); MarkDirty();
            SelfTestDump();
        }

        private static void SelfTestGender(string which)
        {
            var a = _selected?.Appearance;
            if (a == null) { Core.Log?.Warning("[selftest] no NPC selected"); return; }
            VanillaDerived.SetGender(a, which.StartsWith("f", StringComparison.OrdinalIgnoreCase) ? 1f : 0f);
            RefreshForm(); MarkDirty();
            SelfTestDump();
        }

        // Exactly what clicking a pack row does, so the load path can be exercised without a mouse.
        private static void SelfTestOpen(string folder)
        {
            if (!IsOpen)
            {
                Core.Log?.Msg("[selftest] editor was closed, opening it first");
                Open(new LaunchContext());
            }
            OpenProject(folder);
            Core.Log?.Msg($"[selftest] opened '{folder}': project={(_project == null ? "null" : _project.Name)} " +
                          $"npcs={_project?.Npcs.Count ?? -1} selected={_selected?.Name ?? "null"}");
            SelfTestDump();
        }

        private static void SelfTestNewPack(string name)
        {
            if (!IsOpen)
            {
                Core.Log?.Msg("[selftest] editor was closed, opening it first");
                Open(new LaunchContext());
            }
            _project = ProjectStore.Create(name);
            if (_project.Npcs.Count > 0) _project.Npcs[0].Appearance = Preview.SeedFromMenu();
            ProjectStore.Save(_project);
            _selected = _project.Npcs.Count > 0 ? _project.Npcs[0] : null;
            ShowEditor();
            Core.Log?.Msg($"[selftest] new pack '{name}' (folder '{_project.FolderName}')");
        }

        // Writes one appearance float straight onto the draft, so the Experimental tab's unclamped rails can be
        // exercised without a mouse (the value is not range-checked here either - that is the point).
        private static void SelfTestSet(string arg)
        {
            var a = _selected?.Appearance;
            if (a == null) { Core.Log?.Warning("[selftest] no NPC selected"); return; }
            int sp = arg.IndexOf(' ');
            if (sp <= 0) { Core.Log?.Warning("[selftest] set needs '<field> <value>'"); return; }
            string field = arg.Substring(0, sp).Trim().ToLowerInvariant();
            if (!float.TryParse(arg.Substring(sp + 1).Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
            { Core.Log?.Warning("[selftest] set: not a number"); return; }

            switch (field)
            {
                case "gender": a.Gender = v; break;
                case "height": a.Height = v; break;
                case "weight": a.Weight = v; break;
                case "pupil": a.PupilDilation = v; break;
                case "browscale": a.EyebrowScale = v; break;
                case "browthick": a.EyebrowThickness = v; break;
                case "browheight": a.EyebrowRestingHeight = v; break;
                case "browangle": a.EyebrowRestingAngle = v; break;
                default: Core.Log?.Warning("[selftest] set: unknown field '" + field + "'"); return;
            }
            RefreshForm(); MarkDirty();
            Core.Log?.Msg($"[selftest] set {field} = {v:0.###}");
        }

        private static void SelfTestTab(string which)
        {
            _mode = which.StartsWith("c", StringComparison.OrdinalIgnoreCase) ? FormMode.Basic
                  : which.StartsWith("e", StringComparison.OrdinalIgnoreCase) ? FormMode.Experimental
                  : which.StartsWith("w", StringComparison.OrdinalIgnoreCase) ? FormMode.World
                  : FormMode.Advanced;
            RefreshForm();
            Core.Log?.Msg("[selftest] tab = " + _mode);
        }

        private static void SelfTestWorld(string arg)
        {
            string[] p = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float F(int i) => float.Parse(p[i], ci);
            if (_selected == null) { Core.Log?.Warning("[selftest] world: no NPC selected"); return; }
            if (_mode != FormMode.World) { _mode = FormMode.World; RefreshForm(); }
            switch (p.Length > 0 ? p[0] : "")
            {
                case "click":
                    MapClick(new Vector3(F(1), 0f, F(2)));
                    break;
                case "select":
                    _worldSel = p[1] == "spawn" ? SelSpawn : p[1] == "none" ? SelNone : int.Parse(p[1], ci) - 1;
                    RefreshForm();
                    break;
                case "choose":
                {
                    // A number picks by position, a word picks the first entry starting with it ("drive").
                    int i = int.TryParse(p[1], System.Globalization.NumberStyles.Integer, ci, out int num) ? num - 1
                        : _menuEntries.FindIndex(m => m.Label.StartsWith(p[1], StringComparison.OrdinalIgnoreCase));
                    if (i < 0 || i >= _menuEntries.Count) { Core.Log?.Warning($"[selftest] world: no menu entry {p[1]} ({_menuEntries.Count} open)"); return; }
                    Core.Log?.Msg("[selftest] world: choosing '" + _menuEntries[i].Label + "'");
                    _menuEntries[i].Do();
                    break;
                }
                case "pin":
                    SelectPin(p[1] == "spawn" ? SelSpawn : int.Parse(p[1], ci) - 1);
                    break;
                case "drag":
                    DropPin(p[1] == "spawn" ? SelSpawn : int.Parse(p[1], ci) - 1, new Vector3(F(2), 0f, F(3)));
                    break;
                case "undo":
                    Undo();
                    break;
                case "hover":
                {
                    // Same order as the mouse: a pin under the cursor wins over a place.
                    var at = new Vector3(F(1), 0f, F(2));
                    if (_mapHover != null) _mapHover.text = HoverText(at);
                    int pin = PinAt(WorldToScreen(at));
                    if (pin != NoPin)
                    {
                        ShowPinTip(pin);
                        Core.Log?.Msg($"[selftest] world: hover pin {(pin == SelSpawn ? "spawn" : (pin + 1).ToString())}, tip '{(_hoverLabel != null ? _hoverLabel.text : "")}'");
                        break;
                    }
                    World.Place t = HoverTarget(at);
                    ShowHoverTarget(t);
                    Core.Log?.Msg($"[selftest] world: hover target {(t != null ? t.Kind + " '" + t.Label + "'" : "none")}");
                    break;
                }
                case "focus":
                    if (p.Length > 3) { _mapZoom = Mathf.Clamp(F(3), 1f, MapMaxZoom); ApplyMapTransform(true); }
                    FocusWorld(new Vector3(F(1), 0f, F(2)));
                    break;
                case "dump":
                    break;
                default:
                    Core.Log?.Warning("[selftest] world: unknown command '" + arg + "'");
                    return;
            }
            SelfTestWorldDump();
        }

        // Stop blocks on the live track; the old track waits for Destroy and is inactive.
        private static int TimelineBlocks()
        {
            if (_timeline == null) return -1;
            for (int i = 0; i < _timeline.childCount; i++)
            {
                Transform c = _timeline.GetChild(i);
                if (c.name != "track" || !c.gameObject.activeSelf) continue;
                int count = 0;
                for (int j = 0; j < c.childCount; j++) if (c.GetChild(j).name.StartsWith("s", StringComparison.Ordinal)) count++;
                return count;
            }
            return 0;
        }

        private static int ActivePins()
        {
            if (_pinLayer == null) return -1;
            int count = 0;
            for (int i = 0; i < _pinLayer.childCount; i++) if (_pinLayer.GetChild(i).gameObject.activeSelf) count++;
            return count;
        }

        private static void SelfTestWorldDump()
        {
            NpcDraft n = _selected;
            World.WorldData w = World.WorldData.Current;
            Core.Log?.Msg($"[selftest] world: map {(w?.Map != null ? "yes" : "no")}, places {w?.Places.Count ?? 0}, zoom {_mapZoom:0.00}, pan {_mapPan}, sel {_worldSel}, pins {ActivePins()}");
            Core.Log?.Msg("[selftest] world: spawn " + (n.Spawn != null ? n.Spawn.ToString(Newtonsoft.Json.Formatting.None) : "none"));
            if (n.Schedule is Newtonsoft.Json.Linq.JArray a)
                for (int i = 0; i < a.Count; i++)
                    Core.Log?.Msg($"[selftest] world: stop {i + 1} {World.ScheduleEdit.Describe(a[i] as Newtonsoft.Json.Linq.JObject)} | {a[i].ToString(Newtonsoft.Json.Formatting.None)}");
            if (_mapHover != null) Core.Log?.Msg("[selftest] world: hint '" + _mapHover.text + "'");
            Core.Log?.Msg($"[selftest] world: menu {(_menu != null ? string.Join(" | ", _menuEntries.ConvertAll(m => m.Label)) : "closed")}, undo steps {_history.Count - 1}, timeline blocks {TimelineBlocks()}");
        }

        private static void SelfTestEyelids(string onOff)
        {
            var a = _selected?.Appearance;
            if (a == null) { Core.Log?.Warning("[selftest] no NPC selected"); return; }
            a.EyelidFollowsSkin = onOff.StartsWith("on", StringComparison.OrdinalIgnoreCase);
            if (a.EyelidFollowsSkin) VanillaDerived.SetSkinColor(a, a.SkinColor);
            RefreshForm(); MarkDirty();
            Core.Log?.Msg("[selftest] eyelids follow skin = " + a.EyelidFollowsSkin);
        }

        // The form is a Components.ScrollList, so its ScrollRect sits on the content's parent.
        private static void SelfTestScroll(string pos)
        {
            if (_formContent == null) { Core.Log?.Warning("[selftest] no form"); return; }
            var scroll = _formContent.GetComponentInParent<ScrollRect>();
            if (scroll == null) { Core.Log?.Warning("[selftest] no ScrollRect"); return; }
            if (!float.TryParse(pos, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v)) v = 1f;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(v);
            Core.Log?.Msg("[selftest] scrolled to " + scroll.verticalNormalizedPosition.ToString("0.00"));
        }

        // What the character actually gets, read back off the built AvatarSettings rather than off the draft - the
        // face slot order and the layer budget only exist there.
        private static void SelfTestDump()
        {
            var npc = _selected;
            if (npc == null) { Core.Log?.Warning("[selftest] no NPC selected"); return; }
            var s = Preview.BuildSettings(_project, npc.Appearance);
            if (s == null) { Core.Log?.Warning("[selftest] BuildSettings returned null"); return; }

            Core.Log?.Msg("[selftest] LIVE " + Preview.LiveSummary());
            Core.Log?.Msg($"[selftest] skin={Preview.HexOf(s.SkinColor)} lidL={Preview.HexOf(s.LeftEyeLidColor)} " +
                          $"lidR={Preview.HexOf(s.RightEyeLidColor)} hair={Preview.HexOf(s.HairColor)} gender={s.Gender:0.00}");
            Core.Log?.Msg($"[selftest] eyes L(top={s.LeftEyeRestingState.topLidOpen:0.00} bot={s.LeftEyeRestingState.bottomLidOpen:0.00}) " +
                          $"R(top={s.RightEyeRestingState.topLidOpen:0.00} bot={s.RightEyeRestingState.bottomLidOpen:0.00})");
            for (int i = 0; i < s.FaceLayerSettings.Count; i++)
            {
                var l = s.FaceLayerSettings[i];
                string role = i == 0 ? "mouth" : i == 1 ? "facialhair(hair-tinted)" : "free";
                Core.Log?.Msg($"[selftest] face[{i}] {role,-24} {Preview.HexOf(l.layerTint)}  {l.layerPath}");
            }
            for (int i = 0; i < s.BodyLayerSettings.Count; i++)
            {
                var l = s.BodyLayerSettings[i];
                Core.Log?.Msg($"[selftest] body[{i}] {Preview.HexOf(l.layerTint)}  {l.layerPath}");
            }
        }

        // Every option the pickers offer must turn into an avatar object on this game version; a miss is an option
        // that silently shows nothing on the character.
        private static void SelfTestResolve()
        {
            var groups = new (string Name, System.Collections.Generic.List<PathOption> List)[]
            {
                ("hair", PathCatalog.HairStyles()), ("face", PathCatalog.FaceLayers()),
                ("body", PathCatalog.BodyLayers()), ("accessories", PathCatalog.Accessories()),
                ("tattoos", PathCatalog.Tattoos()),
            };
            int total = 0, missing = 0;
            foreach (var g in groups)
            {
                int miss = 0;
                foreach (PathOption o in g.List)
                {
                    total++;
                    if (DooDesch.AvatarKit.AvatarLook.CanResolve(o.Path)) continue;
                    miss++; missing++;
                    Core.Log?.Warning($"[selftest] resolve miss ({g.Name}): {o.Path}");
                }
                Core.Log?.Msg($"[selftest] resolve {g.Name}: {g.List.Count - miss}/{g.List.Count}");
            }
            Core.Log?.Msg($"[selftest] resolve done: {total - missing}/{total} paths resolve");
        }

        private static Button SelfTestFindButton(Transform root, string goName)
        {
            if (root == null) return null;
            foreach (Button b in root.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == goName) return b;
            return null;
        }
    }
}
#endif
