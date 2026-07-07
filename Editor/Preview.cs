using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DooDesch.AvatarKit;
using Il2CppScheduleOne.AvatarFramework;
using S1API.Rendering;
using UnityEngine;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;
using S1MenuRig = Il2CppScheduleOne.UI.MainMenu.MainMenuRig;

namespace Personify.Editor
{
    /// <summary>
    /// Applies a designed NPC to the live menu avatar so it can be seen and rotated. A draft is turned into a fresh
    /// <see cref="AvatarSettings"/> (the SAME mapping the Personnel provider uses) and loaded onto the menu rig, so
    /// the preview matches the shipped NPC exactly. Custom PNG layers are realised via S1API's AvatarLayerFactory.
    /// A baseline of the untouched menu character is captured on open and restored on close.
    /// </summary>
    public static class Preview
    {
        private static Avatar _avatar;
        private static AppearanceDraft _menuBaseline;   // the untouched menu look, for restore + "seed new NPC"
        // Registered custom layers this session (dedup + cleanup). key -> resource path.
        private static readonly Dictionary<string, string> _customLayers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Texture2D> _ownedTextures = new List<Texture2D>();

        /// <summary>Locate (and cache) the main-menu avatar. Returns false if not in the menu scene.</summary>
        public static bool EnsureAvatar()
        {
            if (_avatar != null) return true;
            try
            {
                var rigs = UnityEngine.Object.FindObjectsOfType<S1MenuRig>(true);
                if (rigs == null || rigs.Length == 0) return false;
                _avatar = rigs[0].Avatar;
                if (_avatar != null && _menuBaseline == null)
                    _menuBaseline = CaptureBaseline();
                return _avatar != null;
            }
            catch (Exception e) { Core.Log?.Warning("[preview] find avatar: " + e.Message); return false; }
        }

        /// <summary>The untouched menu look as a draft appearance - a complete, valid starting point for a new NPC.</summary>
        public static AppearanceDraft SeedFromMenu()
        {
            EnsureAvatar();
            return _menuBaseline != null ? Clone(_menuBaseline) : new AppearanceDraft();
        }

        /// <summary>Build the fresh AvatarSettings for a draft and load it onto the menu avatar. Returns false on failure.</summary>
        public static bool Apply(NpcProject project, NpcDraft npc)
        {
            try
            {
                if (!EnsureAvatar() || npc == null) return false;
                AvatarSettings s = BuildSettings(project, npc.Appearance);
                if (s == null) return false;
                _avatar.LoadAvatarSettings(s);
                AvatarDistortion.Apply(_avatar, ToDistortionEntries(npc.Appearance.Distortion));
                return true;
            }
            catch (Exception e) { Core.Log?.Warning("[preview] apply: " + e.Message); return false; }
        }

        /// <summary>Re-applies just the bone/mesh distortion (cheap - no AvatarSettings rebuild). Called every
        /// LateUpdate while the editor is open so it keeps winning against whatever vanilla system (Animator idle
        /// clip, AvatarEffects, ...) also drives these same bones every Update - a one-shot apply isn't enough since
        /// that system reasserts its own values every frame too.</summary>
        public static void ReassertDistortion(NpcDraft npc)
        {
            if (_avatar == null || npc?.Appearance == null) return;
            AvatarDistortion.Apply(_avatar, ToDistortionEntries(npc.Appearance.Distortion));
        }

        /// <summary>Restore the untouched menu character (called on editor close).</summary>
        public static void ExitEditor()
        {
            try
            {
                RestoreRig();
                if (_avatar != null && _menuBaseline != null)
                {
                    _avatar.LoadAvatarSettings(BuildSettings(null, _menuBaseline));
                    AvatarDistortion.Apply(_avatar, ToDistortionEntries(_menuBaseline.Distortion));   // reset any bone scale left over from editing
                }
            }
            catch (Exception e) { Core.Log?.Warning("[preview] exit: " + e.Message); }
        }

        /// <summary>How many body meshes the live avatar has - drives how many "Body mesh N" hide rows the
        /// Experimental tab shows.</summary>
        public static int CurrentBodyMeshCount() => EnsureAvatar() && _avatar?.BodyMeshes != null ? _avatar.BodyMeshes.Length : 0;

        private static Dictionary<string, (Vector3 scale, bool hide)> ToDistortionEntries(Dictionary<string, BoneDistortionDraft> src)
        {
            var outp = new Dictionary<string, (Vector3, bool)>();
            if (src == null) return outp;
            foreach (var kv in src)
                if (kv.Value != null) outp[kv.Key] = (new Vector3(kv.Value.ScaleX, kv.Value.ScaleY, kv.Value.ScaleZ), kv.Value.Hide);
            return outp;
        }

        /// <summary>Drop the cached avatar + owned textures (call when leaving the menu scene).</summary>
        public static void Forget()
        {
            RestoreRig();
            _avatar = null;
            _menuBaseline = null;
            _customLayers.Clear();
            foreach (var t in _ownedTextures) if (t != null) UnityEngine.Object.Destroy(t);
            _ownedTextures.Clear();
        }

        // ---- draft -> AvatarSettings (mirror of Personnel.Appearance.AvatarSettingsFactory) ---------------------

        /// <summary>Build a fresh AvatarSettings from a draft appearance, registering any custom PNG layers.</summary>
        public static AvatarSettings BuildSettings(NpcProject project, AppearanceDraft a)
        {
            a ??= new AppearanceDraft();
            var s = ScriptableObject.CreateInstance<AvatarSettings>();
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;

            s.Gender = a.Gender;
            s.Height = a.Height;
            s.Weight = a.Weight;
            s.SkinColor = Hex(a.SkinColor, new Color32(150, 120, 95, 255));
            s.HairPath = a.HairPath ?? string.Empty;
            s.HairColor = Hex(a.HairColor, Color.black);
            s.EyebrowScale = a.EyebrowScale;
            s.EyebrowThickness = a.EyebrowThickness;
            s.EyebrowRestingHeight = a.EyebrowRestingHeight;
            s.EyebrowRestingAngle = a.EyebrowRestingAngle;
            s.LeftEyeLidColor = Hex(a.LeftEyeLidColor, new Color32(150, 120, 95, 255));
            s.RightEyeLidColor = Hex(a.RightEyeLidColor, new Color32(150, 120, 95, 255));
            s.LeftEyeRestingState = new Eye.EyeLidConfiguration { topLidOpen = a.LeftEyeTop, bottomLidOpen = a.LeftEyeBottom };
            s.RightEyeRestingState = new Eye.EyeLidConfiguration { topLidOpen = a.RightEyeTop, bottomLidOpen = a.RightEyeBottom };
            s.EyeballMaterialIdentifier = string.IsNullOrEmpty(a.EyeballMaterial) ? "Default" : a.EyeballMaterial;
            s.EyeBallTint = Hex(a.EyeBallTint, Color.white);
            s.PupilDilation = a.PupilDilation;

            var faceList = new Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting>();
            var bodyList = new Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting>();
            var accList = new Il2CppSystem.Collections.Generic.List<AvatarSettings.AccessorySetting>();

            AddLayers(project, a.FaceLayers, faceList, face: true);
            AddLayers(project, a.BodyLayers, bodyList, face: false);
            foreach (LayerDraft l in a.Accessories ?? new List<LayerDraft>())
            {
                if (l == null || !l.Visible || string.IsNullOrWhiteSpace(l.Path)) continue;
                var acc = new AvatarSettings.AccessorySetting();
                acc.path = l.Path;
                acc.color = Hex(l.Tint, Color.white);
                accList.Add(acc);
            }

            s.FaceLayerSettings = faceList;
            s.BodyLayerSettings = bodyList;
            s.AccessorySettings = accList;
            return s;
        }

        private static void AddLayers(NpcProject project, List<LayerDraft> layers,
            Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting> dst, bool face)
        {
            if (layers == null) return;
            foreach (LayerDraft l in layers)
            {
                if (l == null || !l.Visible) continue;   // hidden (eye toggled off) - skip in the live preview
                string path = l.Path;
                if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(l.Source) && project != null)
                    path = RegisterCustomLayer(ProjectStore.ResolveRelative(project, l.Source), face);
                if (string.IsNullOrEmpty(path)) continue;
                var ls = new AvatarSettings.LayerSetting();
                ls.layerPath = path;
                ls.layerTint = Hex(l.Tint, Color.white);
                dst.Add(ls);
            }
        }

        private static string SourceLayer(bool face) =>
            face ? "Avatar/Layers/Tattoos/face/Face_Teardrop" : "Avatar/Layers/Tattoos/chest/Chest_Bird";

        /// <summary>Register a PNG as a custom avatar layer and return its Resources path (or null).</summary>
        public static string RegisterCustomLayer(string absPngPath, bool face)
        {
            if (string.IsNullOrEmpty(absPngPath) || !File.Exists(absPngPath)) return null;
            string seg = face ? "Face" : "body";
            string target = "Avatar/Layers/Tattoos/personify/" + seg + "/" + Sanitize(Path.GetFileNameWithoutExtension(absPngPath));
            if (_customLayers.TryGetValue(target, out string done)) return done;
            try
            {
                Texture2D tex = TextureUtils.LoadTextureFromFile(absPngPath);
                if (tex == null) return null;
                _ownedTextures.Add(tex);
                bool ok = AvatarLayerFactory.CreateAndRegisterAvatarLayer(SourceLayer(face), target, "Personify " + seg, tex);
                if (!ok) return null;
                _customLayers[target] = target;
                return target;
            }
            catch (Exception e) { Core.Log?.Warning("[preview] custom layer: " + e.Message); return null; }
        }

        // ---- baseline capture ----------------------------------------------------------------------------------

        private static AppearanceDraft CaptureBaseline()
        {
            var d = new AppearanceDraft();
            try
            {
                var cur = _avatar.CurrentSettings;
                if (cur == null) return d;
                d.Gender = cur.Gender; d.Height = cur.Height; d.Weight = cur.Weight;
                d.SkinColor = HexOf(cur.SkinColor);
                d.HairPath = cur.HairPath ?? "";
                d.HairColor = HexOf(cur.HairColor);
                d.EyebrowScale = cur.EyebrowScale; d.EyebrowThickness = cur.EyebrowThickness;
                d.EyebrowRestingHeight = cur.EyebrowRestingHeight; d.EyebrowRestingAngle = cur.EyebrowRestingAngle;
                d.LeftEyeLidColor = HexOf(cur.LeftEyeLidColor); d.RightEyeLidColor = HexOf(cur.RightEyeLidColor);
                d.LeftEyeTop = cur.LeftEyeRestingState.topLidOpen; d.LeftEyeBottom = cur.LeftEyeRestingState.bottomLidOpen;
                d.RightEyeTop = cur.RightEyeRestingState.topLidOpen; d.RightEyeBottom = cur.RightEyeRestingState.bottomLidOpen;
                d.EyeballMaterial = string.IsNullOrEmpty(cur.EyeballMaterialIdentifier) ? "Default" : cur.EyeballMaterialIdentifier;
                d.EyeBallTint = HexOf(cur.EyeBallTint); d.PupilDilation = cur.PupilDilation;
                CopyLayers(cur.FaceLayerSettings, d.FaceLayers);
                CopyLayers(cur.BodyLayerSettings, d.BodyLayers);
                CopyAccessories(cur.AccessorySettings, d.Accessories);
            }
            catch (Exception e) { Core.Log?.Warning("[preview] baseline capture: " + e.Message); }
            return d;
        }

        private static void CopyLayers(Il2CppSystem.Collections.Generic.List<AvatarSettings.LayerSetting> src, List<LayerDraft> dst)
        {
            if (src == null) return;
            for (int i = 0; i < src.Count; i++)
            {
                var it = src[i];
                if (it == null || string.IsNullOrEmpty(it.layerPath)) continue;
                dst.Add(new LayerDraft { Path = it.layerPath, Tint = HexOf(it.layerTint) });
            }
        }

        private static void CopyAccessories(Il2CppSystem.Collections.Generic.List<AvatarSettings.AccessorySetting> src, List<LayerDraft> dst)
        {
            if (src == null) return;
            for (int i = 0; i < src.Count; i++)
            {
                var it = src[i];
                if (it == null || string.IsNullOrEmpty(it.path)) continue;
                dst.Add(new LayerDraft { Path = it.path, Tint = HexOf(it.color) });
            }
        }

        // ---- camera rotate / zoom -----------------------------------------------------------------------------

        private static Transform _pivot, _origParent;
        private static int _origSibling;
        private static Camera _menuCam;
        private static Vector3 _origCamPos;
        private static bool _camCaptured;
        private static float _baseDist;
        private static int _zoomPercent = 100;
        public static int ZoomPercent => _zoomPercent;

        private static void EnsurePivot()
        {
            if (_pivot != null || _avatar == null) return;
            var at = _avatar.gameObject != null ? _avatar.gameObject.transform : null;
            if (at == null) return;
            _origParent = at.parent; _origSibling = at.GetSiblingIndex();
            var pgo = new GameObject("Personify_RotPivot");
            _pivot = pgo.transform;
            _pivot.SetParent(_origParent, false);
            _pivot.position = at.position; _pivot.rotation = at.rotation;
            at.SetParent(_pivot, true);
        }

        public static void RotateAvatar(float deg)
        {
            if (!EnsureAvatar()) return;
            try { EnsurePivot(); if (_pivot != null) _pivot.Rotate(0f, deg, 0f, Space.World); }
            catch (Exception e) { Core.Log?.Warning("[preview] rotate: " + e.Message); }
        }

        private static Camera FindMenuCamera()
        {
            if (_menuCam != null) return _menuCam;
            if (Camera.main != null) { _menuCam = Camera.main; return _menuCam; }
            try
            {
                var cams = UnityEngine.Object.FindObjectsOfType<Camera>(false);
                if (cams != null)
                    foreach (var c in cams)
                        if (c != null && c.enabled && c.gameObject.activeInHierarchy) { _menuCam = c; break; }
            }
            catch (Exception e) { Core.Log?.Warning("[preview] find camera: " + e.Message); }
            return _menuCam;
        }

        public static void ZoomCamera(float delta)
        {
            if (!EnsureAvatar()) return;
            try
            {
                var cam = FindMenuCamera();
                if (cam == null || _avatar.gameObject == null) return;
                if (!_camCaptured) { _origCamPos = cam.transform.position; _camCaptured = true; }
                Vector3 target = _avatar.gameObject.transform.position + Vector3.up * 1.1f;
                Vector3 to = target - cam.transform.position;
                float dist = to.magnitude;
                if (dist < 0.001f) return;
                if (_baseDist <= 0f) _baseDist = dist;
                float newDist = Mathf.Clamp(dist - delta * 0.12f * dist, _baseDist * 0.35f, _baseDist * 1.6f);
                cam.transform.position = target - to.normalized * newDist;
                _zoomPercent = Mathf.RoundToInt(_baseDist / Mathf.Max(0.01f, newDist) * 100f);
            }
            catch (Exception e) { Core.Log?.Warning("[preview] zoom: " + e.Message); }
        }

        private static void RestoreRig()
        {
            try
            {
                if (_pivot != null)
                {
                    var at = _avatar != null && _avatar.gameObject != null ? _avatar.gameObject.transform : null;
                    if (at != null && _origParent != null) { at.SetParent(_origParent, true); at.SetSiblingIndex(_origSibling); }
                    UnityEngine.Object.Destroy(_pivot.gameObject);
                }
                if (_menuCam != null && _camCaptured) _menuCam.transform.position = _origCamPos;
            }
            catch { }
            _pivot = null; _origParent = null; _menuCam = null; _camCaptured = false; _baseDist = 0f; _zoomPercent = 100;
        }

        // ---- helpers -------------------------------------------------------------------------------------------

        private static AppearanceDraft Clone(AppearanceDraft a)
        {
            var j = Newtonsoft.Json.JsonConvert.SerializeObject(a);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<AppearanceDraft>(j);
        }

        public static Color Hex(string s, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim(); if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8) return fallback;
            try
            {
                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                byte al = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
                return new Color32(r, g, b, al);
            }
            catch { return fallback; }
        }

        public static string HexOf(Color c)
        {
            Color32 c32 = c;
            return $"#{c32.r:X2}{c32.g:X2}{c32.b:X2}";
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "x";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_');
            return sb.ToString();
        }
    }
}
