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
