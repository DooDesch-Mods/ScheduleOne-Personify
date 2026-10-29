using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DooDesch.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Editor;
using Personify.Project;
using SideHustle;
using Sideload.Api;
using UnityEngine;

namespace Personify.Host
{
    /// <summary>
    /// The Personify 2 editor: a full-screen Sideload surface over the menu scene. The page holds the forms; this
    /// side owns the project files, undo, the 3D preview under the page's transparent stage, installing and the
    /// test launch. Every call answers JSON (see the page contract).
    /// </summary>
    internal static class EditorHost
    {
        internal const string SurfaceId = "personify-editor";
        private const string Bundle = "Personify.Assets.editor";

        private static GameObject _root;
        private static SurfaceHandle _surface;
        private static LaunchContext _ctx;
        private static EditSession _session;
        private static string _previewId;
        private static bool _previewDirty;
        private static float _lastEdit;
        private static Rect _stage;
        private static bool _stageOn;
        private static Rect _camRect = new Rect(0, 0, 1, 1);
        private static Camera _cam;
        private static bool _expert;

        internal static bool IsOpen => _root != null;
        internal static PackProject Project => _session?.Project;

        internal static void Open(LaunchContext ctx)
        {
            _ctx = ctx;
            if (!Surfaces.Available)
            {
                Core.Log.Error("Sideload 1.36 or newer is needed for the editor - it was not found. Install or update Sideload.");
                ExitToHub();
                return;
            }
            InkorporatedCatalog.Refresh();
            Preview.EnsureAvatar();
            Build();
        }

        internal static void Close()
        {
            CloseProject();
            try { Preview.ExitEditor(); } catch (Exception e) { Core.Log.Warning("[editor] restoring the menu character failed: " + e.Message); }
            RestoreCamera();
            try { Surfaces.Unmount(SurfaceId); } catch (Exception e) { Core.Log.Warning("[editor] unmount failed: " + e.Message); }
            if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
            _surface = null;
        }

        private static void Build()
        {
            _root = new GameObject("Personify_Editor");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            _root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var rect = _root.GetComponent<RectTransform>();

            _surface = Surfaces.Mount(rect, SurfaceId, Bundle);
            if (!Surfaces.IsMounted(SurfaceId))
            {
                Core.Log.Error("[editor] the editor page could not be mounted - see the Sideload log lines above.");
                return;
            }
            WorldBridge.Reset();
            Calls.Register(_surface);
            Core.Log.Msg("[editor] editor page mounted.");
        }

        internal static void ExitToHub()
        {
            if (_ctx != null)
            {
                try { _ctx.ReturnToHub(); return; }
                catch (Exception e) { Core.Log.Warning("[editor] returning to the Side Hustle hub failed: " + e.Message); }
            }
            Close();
        }

        // ---- per frame ---------------------------------------------------------------------------------------------

        private static bool _exitRequested;

        /// <summary>Leaves the editor on the next frame, so the page's call returns before the page is unmounted.</summary>
        internal static void RequestExit() => _exitRequested = true;

        internal static void Tick()
        {
            if (_exitRequested) { _exitRequested = false; ExitToHub(); return; }
            // A surface has no back gesture: Escape and right-click step the page back one level.
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) Emit("editor.back", null);
            try { _session?.Tick(Time.realtimeSinceStartupAsDouble); }
            catch (Exception e) { Core.Log.Error("[editor] autosave failed: " + e.Message); }

            if (_stageOn) ApplyCamera();
            if (!_previewDirty || Time.time - _lastEdit <= 0.12f) return;
            var npc = _previewId == null ? null : Project?.FindNpc(_previewId);
            if (npc == null) { _previewDirty = false; return; }
            if (LookBridge.Apply(Project, npc)) _previewDirty = false;
            else _lastEdit = Time.time;
        }

        internal static void LateTick()
        {
            var npc = _previewId == null ? null : Project?.FindNpc(_previewId);
            if (npc != null) LookBridge.ReassertDistortion(npc);
        }

        // ---- project ------------------------------------------------------------------------------------------------

        internal static void OpenProject(PackProject p)
        {
            CloseProject();
            _session = new EditSession(p);
            _previewId = null;
            Core.Log.Msg($"[editor] opened '{p.Folder}' ({p.Npcs.Count} NPC(s)).");
        }

        internal static void CloseProject()
        {
            if (_session == null) return;
            try { _session.Flush(); }
            catch (Exception e) { Core.Log.Error($"[editor] saving '{_session.Project.Folder}' on close failed: {e.Message}"); }
            _session = null;
            _previewId = null;
        }

        internal static EditSession Session => _session;

        internal static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>Marks the preview for a rebuild when the shown NPC is among the changed ones (or all changed).</summary>
        internal static void Touched(IReadOnlyList<string> ids, string path)
        {
            if (_previewId == null) return;
            bool looks = path == null || path.StartsWith("appearance", StringComparison.Ordinal) || path.StartsWith("personify", StringComparison.Ordinal);
            if (!looks) return;
            if (ids != null && ids.Count > 0 && !ids.Any(i => string.Equals(i, _previewId, StringComparison.OrdinalIgnoreCase))) return;
            _previewDirty = true;
            _lastEdit = Time.time;
        }

        internal static void ShowPreview(string id)
        {
            _previewId = id;
            _previewDirty = true;
            _lastEdit = 0f;
        }

        internal static bool Expert { get => _expert; set => _expert = value; }

        // ---- stage: the 3D preview drawn into the page's transparent hole -------------------------------------------

        /// <summary>The page's stage rect in css px, which with designShortSide 0 are screen pixels from the top left.</summary>
        internal static void SetStage(float x, float y, float w, float h)
        {
            _stage = new Rect(x, y, w, h);
            if (_stageOn) ApplyCamera();
        }

        internal static void SetStageMode(string mode)
        {
            _stageOn = mode == "preview";
            if (_stageOn) ApplyCamera(); else RestoreCamera();
        }

        private static void ApplyCamera()
        {
            if (_stage.width < 8 || _stage.height < 8) return;
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
                _camRect = _cam.rect;
            }
            float sw = Screen.width, sh = Screen.height;
            var r = new Rect(_stage.x / sw, 1f - (_stage.y + _stage.height) / sh, _stage.width / sw, _stage.height / sh);
            if (_cam.rect != r) _cam.rect = r;
        }

        private static void RestoreCamera()
        {
            if (_cam == null) return;
            try { _cam.rect = _camRect; }
            catch (Exception e) { Core.Log.Warning("[editor] restoring the menu camera failed: " + e.Message); }
            _cam = null;
        }

        // ---- events to the page ------------------------------------------------------------------------------------

        internal static void Emit(string name, JToken payload)
        {
            if (_surface == null) { Core.Log.Warning($"[editor] event '{name}' dropped: the page is not mounted."); return; }
            _surface.Emit(name, payload?.ToString(Formatting.None) ?? "");
        }

        internal static void PublishImage(string name, byte[] png)
        {
            if (_surface == null) { Core.Log.Warning($"[editor] picture '{name}' dropped: the page is not mounted."); return; }
            _surface.Image(name, png);
        }

        /// <summary>The page's strings in the current language: every key of the German table, through L10n so a
        /// player translation file wins.</summary>
        internal static JObject Strings()
        {
            var o = new JObject();
            foreach (string key in Localization.Keys) o[key] = L10n.T(key);
            return o;
        }
    }
}
