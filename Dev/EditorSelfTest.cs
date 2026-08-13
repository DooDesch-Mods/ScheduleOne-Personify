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
    //   tab <name>     -> switch to character | advanced | experimental
    //   eyelids on|off -> flip the "Eyelids follow skin" switch
    //   scroll <0..1>  -> move the form's scroll position (1 = top, 0 = bottom)
    //   set <f> <v>    -> write one appearance float (gender|height|weight|pupil|browscale|browthick|
    //                     browheight|browangle), then dump
    //   packs          -> save and go back to the pack-select screen
    //   open <folder>  -> load that pack folder the way clicking its row does
    //   newpack <name> -> create a pack seeded from the menu character and open it
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
            catch { return; }
            if (string.IsNullOrEmpty(cmd)) return;

            if (cmd == "inkpicker") { _stStep = 1; _stStepAt = Time.unscaledTime; Core.Log?.Msg("[selftest] inkpicker starting"); }
            else if (cmd.StartsWith("pick ", StringComparison.Ordinal)) _stPickLabel = cmd.Substring(5).Trim();
            else if (cmd.StartsWith("skin ", StringComparison.Ordinal)) SelfTestSkin(cmd.Substring(5).Trim());
            else if (cmd.StartsWith("gender ", StringComparison.Ordinal)) SelfTestGender(cmd.Substring(7).Trim());
            else if (cmd == "dump") SelfTestDump();
            else if (cmd.StartsWith("tab ", StringComparison.Ordinal)) SelfTestTab(cmd.Substring(4).Trim());
            else if (cmd.StartsWith("eyelids ", StringComparison.Ordinal)) SelfTestEyelids(cmd.Substring(8).Trim());
            else if (cmd.StartsWith("scroll ", StringComparison.Ordinal)) SelfTestScroll(cmd.Substring(7).Trim());
            else if (cmd.StartsWith("set ", StringComparison.Ordinal)) SelfTestSet(cmd.Substring(4).Trim());
            else if (cmd == "packs") { ProjectStore.Save(_project); ShowProjectSelect(); Core.Log?.Msg("[selftest] back at pack select"); }
            else if (cmd.StartsWith("open ", StringComparison.Ordinal)) SelfTestOpen(cmd.Substring(5).Trim());
            else if (cmd.StartsWith("newpack ", StringComparison.Ordinal)) SelfTestNewPack(cmd.Substring(8).Trim());
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
            OpenProject(folder);
            Core.Log?.Msg($"[selftest] opened '{folder}': project={(_project == null ? "null" : _project.Name)} " +
                          $"npcs={_project?.Npcs.Count ?? -1} selected={_selected?.Name ?? "null"}");
            SelfTestDump();
        }

        private static void SelfTestNewPack(string name)
        {
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
                  : FormMode.Advanced;
            RefreshForm();
            Core.Log?.Msg("[selftest] tab = " + _mode);
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
