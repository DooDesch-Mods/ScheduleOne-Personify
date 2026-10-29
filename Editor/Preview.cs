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
    /// the preview matches the shipped NPC exactly. Custom PNG layers are realised via AvatarLook.RegisterCustomLayer.
    /// A baseline of the untouched menu character is captured before the first apply and restored on close.
    /// </summary>
    public static class Preview
    {
        private static Avatar _avatar;
        private static AppearanceDraft _menuBaseline;   // the untouched menu look as a draft, for "seed new NPC"
        private static AvatarLook.Snapshot _menuSnapshot;   // the same look as the game holds it, for an exact restore
        private static bool _baselineLocked;   // set by the first apply: from then on the avatar shows a draft, not the menu look
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
                if (_avatar == null) { Core.Log?.Warning("[preview] the menu rig has no avatar."); return false; }
                TryCaptureBaseline();
                return true;
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
                TryCaptureBaseline();
                _baselineLocked = true;
                AvatarSettings s = BuildSettings(project, npc.Appearance);
                if (s == null) return false;
                AvatarLayerSlots.LoadAndClean(_avatar, s);
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
                if (_avatar == null) return;
                if (_menuSnapshot != null) AvatarLook.Restore(_avatar, _menuSnapshot);
                else if (_menuBaseline != null) AvatarLayerSlots.LoadAndClean(_avatar, BuildSettings(null, _menuBaseline));
                else Core.Log?.Msg("[preview] no menu look was captured - the menu character keeps the last preview.");
                AvatarDistortion.Apply(_avatar, null);   // reset any bone scale left over from editing
            }
            catch (Exception e) { Core.Log?.Warning("[preview] exit: " + e.Message); }
        }

        /// <summary>What the rig Personify is driving actually carries right now, for checking that an apply landed
        /// on the character the player can see rather than on some other avatar in the scene.</summary>
        public static string LiveSummary()
        {
            try
            {
                int rigCount = 0;
                var rigs = UnityEngine.Object.FindObjectsOfType<S1MenuRig>(true);
                if (rigs != null) rigCount = rigs.Length;
                if (!EnsureAvatar()) return "rigs=" + rigCount + " no avatar";
                var cur = _avatar.Appearance != null ? _avatar.Appearance.AppliedNakedAppearance : null;
                string id = _avatar.gameObject != null
                    ? _avatar.gameObject.name + "#" + _avatar.gameObject.GetInstanceID() +
                      (_avatar.gameObject.activeInHierarchy ? " active" : " INACTIVE")
                    : "(no gameObject)";
                if (cur == null) return "rigs=" + rigCount + " " + id + " appearance=null";
                int worn = _avatar.Appearance._wornObjects != null ? _avatar.Appearance._wornObjects.Count : -1;
                return $"rigs={rigCount} {id} gender={cur.Gender:0.00} height={cur.Height / AvatarLook.HeightScale:0.00} " +
                       $"weight={cur.Weight:0.00} skin={HexOf(cur.SkinColor)} objects={cur.AvatarObjects?.Length ?? -1} worn={worn}";
            }
            catch (Exception e) { return "live summary threw: " + e.Message; }
        }

        /// <summary>The preview character's head bone, for framing the contact picture; null when there is none.</summary>
        public static Transform HeadTransform()
        {
            if (!EnsureAvatar()) return null;
            try { return _avatar._attachmentAnchorProvider != null ? _avatar._attachmentAnchorProvider.Head : null; }
            catch (Exception e) { Core.Log?.Warning("[preview] head bone: " + e.Message); return null; }
        }

        /// <summary>The preview character's root object, for whole-body renders; null when there is none.</summary>
        public static Transform AvatarRoot()
        {
            if (!EnsureAvatar() || _avatar.gameObject == null) return null;
            return _avatar.gameObject.transform;
        }

        /// <summary>The direction the preview character faces (its forward on the ground plane).</summary>
        public static Vector3 FacingDirection()
        {
            if (!EnsureAvatar() || _avatar.gameObject == null) return Vector3.forward;
            Vector3 f = _avatar.gameObject.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.001f ? Vector3.forward : f.normalized;
        }

        /// <summary>How many body meshes the live avatar has - drives how many "Body mesh N" hide rows the
        /// expert view shows.</summary>
        public static int CurrentBodyMeshCount() => EnsureAvatar() ? AvatarDistortion.BodyMeshCount(_avatar) : 0;

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
            _menuSnapshot = null;
            _baselineLocked = false;
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

            // The face list is read by POSITION, not as a flat stack: entry 0 is the mouth and entry 1 is the facial
            // hair, which the game draws in the avatar's hair colour. Sorting them into those roles is what keeps a
            // freckle or eyeshadow layer from being drawn as hair.
            AvatarLayerSlots.OrderFaceLayers(faceList);
            foreach (var d in AvatarLayerSlots.TrimFaceToBudget(faceList, BudgetPriority))
                Core.Log?.Warning("[preview] face layer budget exceeded, dropped " + d.layerPath +
                                  " (only " + AvatarLayerSlots.FreeFaceEntries + " render besides the mouth and facial hair)");

            // The character renders only eight body layers at once; anything past that is written to a material
            // slot that does not exist and vanishes without a trace. Drop the surplus deliberately and say so.
            foreach (var d in AvatarLayerSlots.TrimToBudget(bodyList, BudgetPriority))
                Core.Log?.Warning("[preview] body layer budget exceeded, dropped " + d.layerPath +
                                  " (only " + AvatarLayerSlots.BodySlots + " render at once)");

            s.FaceLayerSettings = faceList;
            s.BodyLayerSettings = bodyList;
            s.AccessorySettings = accList;
            return s;
        }

        // Which body layer gives up its slot first. Custom art is why someone opened the editor, so it outranks
        // stock tattoos, and both outrank clothing: a missing garment is obvious enough that the user can see what
        // happened and turn a layer off, while a missing tattoo just looks like the editor lost it.
        private static int BudgetPriority(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (path.IndexOf("personify", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (path.IndexOf("/Tattoos/", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
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
            face ? "Avatar/Layers/Tattoos/Face/Face_Teardrop" : "Avatar/Layers/Tattoos/Chest/Chest_Bird";

        /// <summary>Register a PNG as a custom avatar layer and return its Resources path (or null).</summary>
        public static string RegisterCustomLayer(string absPngPath, bool face)
        {
            if (string.IsNullOrEmpty(absPngPath) || !File.Exists(absPngPath)) return null;
            string seg = face ? "Face" : "body";
            // The registry is global and has no unregister, so the path has to identify the FILE, not just its
            // name - two NPCs each importing their own "grin.png" would otherwise share whichever loaded first.
            string target = "Avatar/Layers/Tattoos/personify/" + seg + "/" +
                            Sanitize(Path.GetFileNameWithoutExtension(absPngPath)) + "_" + SourceKey(absPngPath);
            if (_customLayers.TryGetValue(target, out string done)) return done;
            try
            {
                Texture2D tex = TextureUtils.LoadTextureFromFile(absPngPath);
                if (tex == null) { Core.Log?.Warning("[preview] custom layer: could not read " + absPngPath); return null; }
                _ownedTextures.Add(tex);
                bool ok = AvatarLook.RegisterCustomLayer(SourceLayer(face), target, "Personify " + seg, tex);
                if (!ok) { Core.Log?.Warning("[preview] custom layer: registering " + absPngPath + " failed."); return null; }
                _customLayers[target] = target;
                return target;
            }
            catch (Exception e) { Core.Log?.Warning("[preview] custom layer: " + e.Message); return null; }
        }

        // Short stable tag for a source file's full path, so same-named PNGs from different projects stay apart.
        private static string SourceKey(string absPath)
        {
            uint h = 2166136261;
            string p = absPath.Replace('\\', '/').ToLowerInvariant();
            foreach (char c in p) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        // ---- baseline capture ----------------------------------------------------------------------------------

        // The menu character gets its look only once the save info has loaded, so an early call finds nothing and a
        // later one tries again - but never after the first apply, when the avatar shows a draft instead.
        private static void TryCaptureBaseline()
        {
            if (_baselineLocked || _avatar == null) return;
            if (_menuSnapshot == null) _menuSnapshot = AvatarLook.Capture(_avatar);
            if (_menuBaseline == null) _menuBaseline = CaptureBaseline();
        }

        private static AppearanceDraft CaptureBaseline()
        {
            var d = new AppearanceDraft();
            AvatarSettings cur = null;
            try
            {
                cur = AvatarLook.CaptureSettings(_avatar);
                if (cur == null) return null;
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
            finally { if (cur != null) UnityEngine.Object.Destroy(cur); }
            Core.Log?.Msg($"[preview] menu look captured: {d.FaceLayers.Count} face, {d.BodyLayers.Count} body, {d.Accessories.Count} worn, hair '{d.HairPath}'.");
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
        private static Quaternion _origCamRot;
        private static bool _camCaptured, _framed;
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

        /// <summary>The camera the main menu renders with; Camera.main is null there, so the first enabled camera counts.</summary>
        public static Camera FindMenuCamera()
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

        private static void CaptureCamera(Camera cam)
        {
            if (_camCaptured) return;
            _origCamPos = cam.transform.position;
            _origCamRot = cam.transform.rotation;
            _camCaptured = true;
        }

        /// <summary>
        /// Puts the menu camera in front of the preview character so the whole body fills the stage. The menu framing
        /// keeps the character at the screen's left edge, which lands off-centre and small once the camera draws
        /// into the stage only. Runs once per editor visit; zoom then moves along the same line.
        /// </summary>
        public static void FrameCamera()
        {
            if (_framed) return;
            if (!EnsureAvatar() || _avatar.gameObject == null) return;
            var cam = FindMenuCamera();
            if (cam == null) { Core.Log?.Warning("[preview] no camera to frame the preview character with."); return; }
            try
            {
                CaptureCamera(cam);
                Vector3 target = _avatar.gameObject.transform.position + Vector3.up * 0.95f;
                // 1.35 m half-height around the target keeps a tall NPC with a hat and the feet inside the frame.
                float dist = 1.35f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                Vector3 from = target + FacingDirection() * dist + Vector3.up * 0.15f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from, Vector3.up));
                _baseDist = (target - from).magnitude;
                _zoomPercent = 100;
                _framed = true;
                Core.Log?.Msg($"[preview] camera framed on the preview character ({_baseDist:0.00} m, fov {cam.fieldOfView:0}).");
            }
            catch (Exception e) { Core.Log?.Warning("[preview] framing the camera failed: " + e.Message); }
        }

        public static void ZoomCamera(float delta)
        {
            if (!EnsureAvatar()) return;
            try
            {
                var cam = FindMenuCamera();
                if (cam == null || _avatar.gameObject == null) return;
                CaptureCamera(cam);
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
                if (_menuCam != null && _camCaptured) _menuCam.transform.SetPositionAndRotation(_origCamPos, _origCamRot);
            }
            catch (Exception e) { Core.Log?.Warning("[preview] restoring the rig failed: " + e.Message); }
            _pivot = null; _origParent = null; _menuCam = null; _camCaptured = false; _framed = false; _baseDist = 0f; _zoomPercent = 100;
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

        /// <summary>
        /// "#RRGGBB", or "#RRGGBBAA" when the colour is not fully opaque. Layer tints carry their strength in the
        /// alpha channel - vanilla's freckles and eye shadow are black at a fraction of an alpha - so dropping it
        /// would quietly turn every captured or edited detail layer up to full.
        /// </summary>
        public static string HexOf(Color c)
        {
            Color32 c32 = c;
            return c32.a == 255
                ? $"#{c32.r:X2}{c32.g:X2}{c32.b:X2}"
                : $"#{c32.r:X2}{c32.g:X2}{c32.b:X2}{c32.a:X2}";
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
