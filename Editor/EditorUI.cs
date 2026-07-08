using System;
using System.Collections.Generic;
using System.IO;
using DooDesch.UI;
using Personify.Editor.Widgets;
using S1API.UI;
using SideHustle;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Personify.Editor
{
    /// <summary>
    /// The in-game NPC editor overlay. Opened from the Side Hustle hub, it runs in the menu scene ON TOP of the live
    /// character rig (no save loaded). The centre stays transparent so the designed NPC shows through and updates
    /// live as you edit. Flow: choose / create a project -> add NPCs -> tune appearance (and optional behaviour /
    /// Backrooms binding) -> see it live on the character -> export a ready-to-release Personnel NPC pack.
    /// </summary>
    public static class EditorUI
    {
        private static LaunchContext _ctx;
        private static GameObject _canvasGO;
        private static GameObject _screen;
        private static NpcProject _project;
        private static NpcDraft _selected;

        private static Transform _railList;      // NPC button list (left rail)
        private static RectTransform _formContent;  // scroll content (right panel)
        private static Text _titleText;

        private static bool _previewDirty;
        private static float _lastEdit;

        private const float RailW = 300f;
        private const float PanelW = 400f;
        private const float Gutter = 16f;

        public static bool IsOpen => _canvasGO != null;

        // --- open / close ---

        public static void Open(LaunchContext ctx)
        {
            _ctx = ctx;
            if (_canvasGO == null) BuildCanvas();
            Preview.EnsureAvatar();
            ShowProjectSelect();
        }

        public static void Close()
        {
            try { Preview.ExitEditor(); } catch { }
            try { Toast.Clear(); } catch { }
            if (_canvasGO != null) { UnityEngine.Object.Destroy(_canvasGO); _canvasGO = null; }
            _screen = null; _project = null; _selected = null;
            _railList = null; _formContent = null; _titleText = null;
            _previewDirty = false;
        }

        public static void Tick()
        {
            try { Toast.Tick(); } catch { }
            if (_previewDirty && Time.time - _lastEdit > 0.12f)
            {
                _previewDirty = false;
                if (_project != null && _selected != null) Preview.Apply(_project, _selected);
            }
        }

        // Runs in LateUpdate (after Animator evaluation) so bone distortion keeps winning against whatever vanilla
        // system also drives HeadBone/etc. every Update - see Preview.ReassertDistortion.
        public static void LateTick()
        {
            if (_selected != null) Preview.ReassertDistortion(_selected);
        }

        private static void BuildCanvas()
        {
            _canvasGO = new GameObject("Personify_EditorCanvas");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGO);
            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            _canvasGO.AddComponent<GraphicRaycaster>();
            Toast.Init(_canvasGO.transform);
        }

        private static void ClearScreen()
        {
            if (_screen != null) { UnityEngine.Object.Destroy(_screen); _screen = null; }
        }

        private static void MarkDirty() { _previewDirty = true; _lastEdit = Time.time; }

        // --- screen: project select ---

        private static void ShowProjectSelect()
        {
            ClearScreen();
            _screen = UIFactory.Panel("ProjectSelect", _canvasGO.transform, Theme.BgBase, fullAnchor: true);

            float cardH = Mathf.Min(720f, Screen.height * 0.84f);
            var card = UIFactory.Panel("Card", _screen.transform, Theme.BgPanel);
            var cimg = card.GetComponent<Image>(); if (cimg != null) { cimg.sprite = Theme.RoundedSprite(); cimg.type = Image.Type.Sliced; }
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(640f, cardH);
            var ol = card.AddComponent<Outline>(); ol.effectColor = Theme.HairlineStrong; ol.effectDistance = new Vector2(1, -1);

            var title = UIFactory.Text("Title", "Personify", card.transform, Theme.H1, TextAnchor.UpperCenter, FontStyle.Bold);
            title.color = Theme.TextPrimary; title.raycastTarget = false;
            var trt = title.rectTransform; trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(Gutter, -70); trt.offsetMax = new Vector2(-Gutter, -26);

            var sub = UIFactory.Text("Sub", "Design and export custom NPC packs.", card.transform, 14, TextAnchor.UpperCenter);
            sub.color = Theme.TextMuted; sub.raycastTarget = false;
            var subrt = sub.rectTransform; subrt.anchorMin = new Vector2(0, 1); subrt.anchorMax = new Vector2(1, 1); subrt.pivot = new Vector2(0.5f, 1);
            subrt.offsetMin = new Vector2(Gutter, -98); subrt.offsetMax = new Vector2(-Gutter, -74);

            var (newGO, newBtn, _) = UIFactory.ButtonWithLabel("New", "New pack", card.transform, Theme.Accent, 210, 48);
            var nrt = newGO.GetComponent<RectTransform>(); nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 1); nrt.pivot = new Vector2(0.5f, 1);
            nrt.anchoredPosition = new Vector2(0, -116); nrt.sizeDelta = new Vector2(210, 48);
            newBtn.onClick.AddListener((UnityAction)(() => CreateProjectFlow()));

            var (exitGO, exitBtn, _2) = UIFactory.ButtonWithLabel("Exit", "Exit to hub", card.transform, Theme.Button, 150, 36);
            var ert = exitGO.GetComponent<RectTransform>(); ert.anchorMin = ert.anchorMax = new Vector2(1, 1); ert.pivot = new Vector2(1, 1);
            ert.anchoredPosition = new Vector2(-Gutter, -20); ert.sizeDelta = new Vector2(130, 36);
            exitBtn.onClick.AddListener((UnityAction)(() => ExitToHub()));

            var div = Components.Divider(card.transform);
            var dvrt = div.GetComponent<RectTransform>();
            dvrt.anchorMin = new Vector2(0, 1); dvrt.anchorMax = new Vector2(1, 1); dvrt.pivot = new Vector2(0.5f, 1);
            dvrt.offsetMin = new Vector2(Gutter, -179); dvrt.offsetMax = new Vector2(-Gutter, -178);

            List<string> projects = ProjectStore.List();
            var hdr = UIFactory.Text("YP", "Your packs (" + projects.Count + ")", card.transform, 15, TextAnchor.UpperLeft, FontStyle.Bold);
            hdr.color = Theme.TextPrimary; hdr.raycastTarget = false;
            var hrt = hdr.rectTransform; hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.offsetMin = new Vector2(Gutter, -206); hrt.offsetMax = new Vector2(-Gutter, -186);

            var listPanel = UIFactory.Panel("List", card.transform, Theme.Clear);
            var lrt = listPanel.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1); lrt.pivot = new Vector2(0.5f, 1);
            lrt.offsetMin = new Vector2(Gutter, 20); lrt.offsetMax = new Vector2(-Gutter, -214);

            var content = Components.ScrollList(listPanel.transform, out _, 8f);
            if (projects.Count == 0)
            {
                var empty = UIFactory.Text("empty", "No packs yet. Click \"New pack\" to start.", content, 13, TextAnchor.UpperLeft);
                empty.color = Theme.TextMuted; empty.gameObject.AddComponent<LayoutElement>().minHeight = 40;
            }
            foreach (string folder in projects) AddProjectRow(content, folder);

            Interactions.PolishButtons(card.transform);
        }

        private static void AddProjectRow(Transform content, string folder)
        {
            string display = ProjectStore.DisplayName(folder);
            int count = ProjectStore.NpcCount(folder);

            var row = UIFactory.Panel("row_" + folder, content, Theme.BgElevated);
            var rimg = row.GetComponent<Image>(); if (rimg != null) { rimg.sprite = Theme.RoundedSprite(); rimg.type = Image.Type.Sliced; }
            row.AddComponent<LayoutElement>().minHeight = 54;

            var open = row.AddComponent<Button>(); open.targetGraphic = rimg;
            open.onClick.AddListener((UnityAction)(() => OpenProject(folder)));

            var t = UIFactory.Text("t", display, row.transform, Theme.Label, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.color = Theme.TextPrimary; t.raycastTarget = false;
            var trt = t.rectTransform; trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1); trt.offsetMin = new Vector2(14, 20); trt.offsetMax = new Vector2(-120, -6);

            var sub = UIFactory.Text("s", count + " NPC(s)", row.transform, Theme.Caption, TextAnchor.LowerLeft);
            sub.color = Theme.TextMuted; sub.raycastTarget = false;
            var srt = sub.rectTransform; srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1); srt.offsetMin = new Vector2(14, 6); srt.offsetMax = new Vector2(-120, -28);

            var (delGO, delBtn, _) = UIFactory.ButtonWithLabel("del", "Delete", row.transform, Theme.Button, 90, 32);
            var drt = delGO.GetComponent<RectTransform>(); drt.anchorMin = drt.anchorMax = new Vector2(1, 0.5f); drt.pivot = new Vector2(1, 0.5f);
            drt.anchoredPosition = new Vector2(-12, 0); drt.sizeDelta = new Vector2(90, 32);
            delBtn.onClick.AddListener((UnityAction)(() =>
                Components.ConfirmDialog(_canvasGO.transform, "Delete pack?", $"Permanently delete \"{display}\" and all its NPCs?", "Delete",
                    () => { ProjectStore.Delete(folder); ShowProjectSelect(); })));
        }

        private static void CreateProjectFlow()
        {
            Components.PromptDialog(_canvasGO.transform, "New NPC pack", "Give your pack a name.", "My NPC Pack", "Create", name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
                if (ProjectStore.Exists(name)) return "A pack with that name already exists.";
                _project = ProjectStore.Create(name);
                // Seed the first NPC's appearance from the live menu character so the preview is complete.
                if (_project.Npcs.Count > 0) _project.Npcs[0].Appearance = Preview.SeedFromMenu();
                ProjectStore.Save(_project);
                _selected = _project.Npcs.Count > 0 ? _project.Npcs[0] : null;
                ShowEditor();
                return null;
            });
        }

        private static void OpenProject(string folder)
        {
            _project = ProjectStore.Load(folder);
            if (_project == null) { Toast.Show("Failed to open pack.", Severity.Danger); return; }
            _selected = _project.Npcs.Count > 0 ? _project.Npcs[0] : null;
            ShowEditor();
        }

        // --- screen: editor ---

        private static void ShowEditor()
        {
            ClearScreen();
            _screen = UIFactory.Panel("Editor", _canvasGO.transform, Theme.Clear, fullAnchor: true);
            BuildLeftRail();
            BuildRightPanel();
            BuildBottomBar();
            RefreshRail();
            RefreshForm();
            MarkDirty();
        }

        private static void BuildLeftRail()
        {
            var rail = UIFactory.Panel("Rail", _screen.transform, Theme.BgPanel);
            var rt = rail.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(RailW, 0); rt.anchoredPosition = Vector2.zero;

            _titleText = UIFactory.Text("pack", _project?.Name ?? "Pack", rail.transform, Theme.H3, TextAnchor.UpperLeft, FontStyle.Bold);
            _titleText.color = Theme.TextPrimary; _titleText.raycastTarget = false;
            var trt = _titleText.rectTransform; trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(Gutter, -50); trt.offsetMax = new Vector2(-Gutter, -18);

            var (addGO, addBtn, _) = UIFactory.ButtonWithLabel("add", "+ Add NPC", rail.transform, Theme.Accent, 0, 38);
            var art = addGO.GetComponent<RectTransform>(); art.anchorMin = new Vector2(0, 1); art.anchorMax = new Vector2(1, 1); art.pivot = new Vector2(0.5f, 1);
            art.offsetMin = new Vector2(Gutter, -96); art.offsetMax = new Vector2(-Gutter, -58);
            addBtn.onClick.AddListener((UnityAction)(() => AddNpc()));

            // NPC list scroll (fills the middle)
            var listPanel = UIFactory.Panel("npcs", rail.transform, Theme.Clear);
            var lrt = listPanel.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1); lrt.pivot = new Vector2(0.5f, 1);
            lrt.offsetMin = new Vector2(Gutter, 150); lrt.offsetMax = new Vector2(-Gutter, -104);
            _railList = Components.ScrollList(listPanel.transform, out _, 6f);

            // Bottom actions
            BuildRailButton(rail, "Save", -1, 0, Theme.Button, () => { if (ProjectStore.Save(_project)) Toast.Show("Saved.", Severity.Success); });
            BuildRailButton(rail, "Export", -1, 1, Theme.Accent, () => ExportFlow());
            BuildRailButton(rail, "Packs", -1, 2, Theme.Button, () => { ProjectStore.Save(_project); ShowProjectSelect(); });
            BuildRailButton(rail, "Exit", -1, 3, Theme.Button, () => { ProjectStore.Save(_project); ExitToHub(); });

            Interactions.PolishButtons(rail.transform);
        }

        // Four small buttons in a 2x2 grid at the bottom of the rail.
        private static void BuildRailButton(GameObject rail, string label, int _, int index, Color color, Action onClick)
        {
            var (go, btn, __) = UIFactory.ButtonWithLabel("b_" + label, label, rail.transform, color, 0, 34);
            var rt = go.GetComponent<RectTransform>();
            float w = (RailW - Gutter * 2 - 10) / 2f;
            int col = index % 2, rowi = index / 2;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(w, 34);
            rt.anchoredPosition = new Vector2(Gutter + col * (w + 10), 18 + (1 - rowi) * 42);
            btn.onClick.AddListener((UnityAction)(() => onClick()));
        }

        private static void BuildRightPanel()
        {
            var panel = UIFactory.Panel("Form", _screen.transform, Theme.BgPanel);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(PanelW, 0); rt.anchoredPosition = Vector2.zero;

            var scrollPanel = UIFactory.Panel("scroll", panel.transform, Theme.Clear);
            var srt = scrollPanel.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = new Vector2(6, 6); srt.offsetMax = new Vector2(-6, -6);
            _formContent = Components.ScrollList(scrollPanel.transform, out _, 6f);
        }

        private static void BuildBottomBar()
        {
            var bar = UIFactory.Panel("BottomBar", _screen.transform, Theme.WithAlpha(Theme.BgElevated, 0.9f));
            var img = bar.GetComponent<Image>(); if (img != null) { img.sprite = Theme.RoundedSprite(); img.type = Image.Type.Sliced; }
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(430, 46); rt.anchoredPosition = new Vector2((RailW - PanelW) / 2f, 20);

            var rowGO = UIFactory.ButtonRow("row", bar.transform, 8f);
            var rrt = rowGO.GetComponent<RectTransform>(); rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.offsetMin = new Vector2(8, 6); rrt.offsetMax = new Vector2(-8, -6);

            BarButton(rowGO.transform, "< Rotate", () => Preview.RotateAvatar(-25f));
            BarButton(rowGO.transform, "Rotate >", () => Preview.RotateAvatar(25f));
            BarButton(rowGO.transform, "Zoom +", () => Preview.ZoomCamera(1f));
            BarButton(rowGO.transform, "Zoom -", () => Preview.ZoomCamera(-1f));
            BarButton(rowGO.transform, "Base human", () => { if (_selected != null) { _selected.Appearance = Preview.SeedFromMenu(); RefreshForm(); MarkDirty(); } });

            Interactions.PolishButtons(bar.transform);
        }

        private static void BarButton(Transform parent, string label, Action onClick)
        {
            var (go, btn, txt) = UIFactory.ButtonWithLabel("bb_" + label, label, parent, Theme.Button, 78, 34);
            if (txt != null) txt.fontSize = Theme.Caption;
            var le = go.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.minHeight = 34;
            btn.onClick.AddListener((UnityAction)(() => onClick()));
        }

        // --- rail list ---

        private static void RefreshRail()
        {
            if (_railList == null) return;
            UIFactory.ClearChildren(_railList);
            if (_project?.Npcs == null) return;
            foreach (NpcDraft npc in _project.Npcs)
            {
                NpcDraft captured = npc;
                bool sel = ReferenceEquals(npc, _selected);
                var (go, btn, txt) = UIFactory.ButtonWithLabel("npc_" + npc.Id, npc.Name + "  (" + Ids.Make(_project.Name, npc.Name) + ")", _railList, sel ? Theme.Accent : Theme.Button, 0, 40);
                if (txt != null) { txt.fontSize = Theme.Label; txt.alignment = TextAnchor.MiddleLeft; txt.rectTransform.offsetMin = new Vector2(10, 0); }
                go.AddComponent<LayoutElement>().minHeight = 40;
                btn.onClick.AddListener((UnityAction)(() => { _selected = captured; RefreshRail(); RefreshForm(); MarkDirty(); }));
            }
            Interactions.PolishButtons(_railList);
        }

        private static void AddNpc()
        {
            if (_project == null) return;
            var npc = new NpcDraft { Name = "NPC " + (_project.Npcs.Count + 1), Appearance = Preview.SeedFromMenu() };
            _project.Npcs.Add(npc);
            _selected = npc;
            RefreshRail(); RefreshForm(); MarkDirty();
        }

        private static void RemoveSelected()
        {
            if (_project == null || _selected == null) return;
            _project.Npcs.Remove(_selected);
            _selected = _project.Npcs.Count > 0 ? _project.Npcs[0] : null;
            RefreshRail(); RefreshForm(); MarkDirty();
        }

        // --- form (selected NPC) ---

        private enum FormMode { Basic, Advanced, Experimental }
        private static FormMode _mode = FormMode.Basic;

        private static void RefreshForm()
        {
            if (_formContent == null) return;
            UIFactory.ClearChildren(_formContent);
            if (_selected == null)
            {
                var t = UIFactory.Text("none", "No NPC selected. Click \"+ Add NPC\".", _formContent, 13, TextAnchor.UpperLeft);
                t.color = Theme.TextMuted; t.gameObject.AddComponent<LayoutElement>().minHeight = 40;
                return;
            }

            NpcDraft n = _selected;
            n.Appearance ??= new AppearanceDraft();

            ModeToggle();
            if (_mode == FormMode.Basic) BuildBasicForm(n);
            else if (_mode == FormMode.Advanced) BuildAdvancedForm(n);
            else BuildExperimentalForm(n);

            Interactions.PolishButtons(_formContent);
        }

        // A three-segment "Character | Advanced | Experimental" switch at the top of the form. Character = a
        // simplified, vanilla-character-creator-like view (single-select clothing/face slots + the essentials);
        // Advanced = the full editor (every layer + knob); Experimental = extreme body distortion for Backrooms-style
        // horror (unclamped bone scale/hide - not a vanilla look).
        private static void ModeToggle()
        {
            var row = new GameObject("mode"); row.transform.SetParent(_formContent, false); row.AddComponent<RectTransform>();
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 34; rle.preferredHeight = 34; rle.flexibleWidth = 1;
            int idx = _mode == FormMode.Basic ? 0 : _mode == FormMode.Advanced ? 1 : 2;
            var seg = Components.Segmented(row.transform, new[] { "Character", "Advanced", "Experimental" }, idx,
                i => { _mode = i == 0 ? FormMode.Basic : i == 1 ? FormMode.Advanced : FormMode.Experimental; RefreshForm(); }, out _);
            StretchFill(seg.GetComponent<RectTransform>());
        }

        // The full editor: every layer + knob.
        private static void BuildAdvancedForm(NpcDraft n)
        {
            AppearanceDraft a = n.Appearance;

            Components.SectionHeader(_formContent, "Identity");
            TextRow("Name", n.Name, v => { n.Name = v; RefreshForm(); }, refreshRailOnEdit: true);
            IdCaption(n);   // id is auto-derived from pack + name (read-only), so ids never collide
            RemoveRow();

            Components.SectionHeader(_formContent, "Body");
            GenderRow(a);
            SliderRow("Height", 0.7f, 1.3f, a.Height, v => a.Height = v);
            SliderRow("Weight", 0f, 1f, a.Weight, v => a.Weight = v);
            ColorRow("Skin colour", a.SkinColor, v => a.SkinColor = v);

            Components.SectionHeader(_formContent, "Hair");
            ColorRow("Hair colour", a.HairColor, v => a.HairColor = v);
            PickerRow("Hair style", a.HairPath, "Choose a hair style", PathCatalog.HairStyles(), allowNone: true, v => a.HairPath = v);

            Components.SectionHeader(_formContent, "Eyes");
            ColorRow("Eyeball tint", a.EyeBallTint, v => a.EyeBallTint = v);
            SliderRow("Pupil", 0f, 1f, a.PupilDilation, v => a.PupilDilation = v);
            SliderRow("L. eyelid", 0f, 1f, a.LeftEyeTop, v => { a.LeftEyeTop = v; a.LeftEyeBottom = v; });
            SliderRow("R. eyelid", 0f, 1f, a.RightEyeTop, v => { a.RightEyeTop = v; a.RightEyeBottom = v; });

            Components.SectionHeader(_formContent, "Eyebrows");
            SliderRow("Scale", 0f, 2f, a.EyebrowScale, v => a.EyebrowScale = v);
            SliderRow("Thickness", 0f, 2f, a.EyebrowThickness, v => a.EyebrowThickness = v);
            SliderRow("Height", 0f, 1f, a.EyebrowRestingHeight, v => a.EyebrowRestingHeight = v);
            SliderRow("Angle", 0f, 1f, a.EyebrowRestingAngle, v => a.EyebrowRestingAngle = v);

            LayerSection("Face layers", a.FaceLayers, PathCatalog.FaceLayers(), "face layer");
            LayerSection("Body layers", a.BodyLayers, PathCatalog.BodyLayers(), "body layer");
            LayerSection("Accessories", a.Accessories, PathCatalog.Accessories(), "accessory", allowCustomImport: false);

            Components.SectionHeader(_formContent, "Behaviour (S1API, optional)");
            ToggleRow("Enable behaviour", n.Behavior.Enabled, v => { n.Behavior.Enabled = v; RefreshForm(); });
            if (n.Behavior.Enabled)
            {
                SliderRow("Aggression", 0f, 1f, n.Behavior.Aggression, v => n.Behavior.Aggression = v);
                SliderRow("Max health", 1f, 500f, n.Behavior.MaxHealth, v => n.Behavior.MaxHealth = v);
                SliderRow("Scale", 0.5f, 2f, n.Behavior.Scale, v => n.Behavior.Scale = v);
                ConversationRow("Conversation", n.Behavior.Conversation, v => n.Behavior.Conversation = v);
            }

            BackroomsSection(n);   // gated on the Backrooms mod being installed
        }

        // Extreme body distortion for Backrooms-style horror: unclamped non-uniform bone scale (0-8x) or a full
        // hide (zero-scale, collapsing the bone and everything below it) per bone, plus per-mesh hide toggles.
        // Not part of vanilla AvatarSettings - applied as a separate pass by Preview/Personnel after the normal
        // appearance load (DooDesch.AvatarKit.AvatarDistortion).
        // canHide is false only for the skeleton root (Hips): zero-scaling it collapses every bone parented under
        // it - i.e. the whole character, not just the hips - so hiding it isn't a locally-scoped effect and the
        // control is intentionally not offered. Its scale sliders stay (stretching the whole rig is still useful).
        // The other spine bones are interior nodes with a narrower, still-legible blast radius (e.g. hiding
        // MiddleSpine removes the upper body but leaves hips/legs standing) so they keep their hide toggle.
        private static readonly (string key, string label, bool canHide)[] BoneRows =
        {
            ("HeadBone", "Head", true), ("HipBone", "Hips", false),
            ("LeftFootBone", "Left foot", true), ("RightFootBone", "Right foot", true),
            ("LeftShoulder", "Left shoulder", true), ("RightShoulder", "Right shoulder", true),
            ("MiddleSpine", "Mid spine", true), ("LowerSpine", "Lower spine", true), ("LowestSpine", "Lowest spine", true),
        };

        private static void BuildExperimentalForm(NpcDraft n)
        {
            AppearanceDraft a = n.Appearance;

            Components.SectionHeader(_formContent, "Experimental - extreme distortion");
            var warn = UIFactory.Text("warn", "Unclamped bone scale (0-8x) and part hiding. For Backrooms-style body horror, not a vanilla look.", _formContent, Theme.Caption, TextAnchor.UpperLeft);
            warn.color = Theme.TextMuted; warn.raycastTarget = false; warn.horizontalOverflow = HorizontalWrapMode.Wrap; warn.gameObject.AddComponent<LayoutElement>().minHeight = 34;

            foreach (var (key, label, canHide) in BoneRows) BoneDistortionRows(a, key, label, canHide);

            Components.SectionHeader(_formContent, "Hide meshes");
            MeshHideRow(a, DooDesch.AvatarKit.AvatarDistortion.FaceMeshKey, "Face mesh");
            int bodyMeshCount = Preview.CurrentBodyMeshCount();
            for (int i = 0; i < bodyMeshCount; i++)
                MeshHideRow(a, DooDesch.AvatarKit.AvatarDistortion.BodyMeshKeyPrefix + i, "Body mesh " + i);
        }

        private static BoneDistortionDraft Dist(AppearanceDraft a, string key)
        {
            if (!a.Distortion.TryGetValue(key, out var d)) { d = new BoneDistortionDraft(); a.Distortion[key] = d; }
            return d;
        }

        private static void BoneDistortionRows(AppearanceDraft a, string key, string label, bool canHide)
        {
            a.Distortion.TryGetValue(key, out BoneDistortionDraft d);
            bool hidden = canHide && (d?.Hide ?? false);
            if (canHide) ToggleRow(label + " - hide", hidden, v => { Dist(a, key).Hide = v; RefreshForm(); });
            if (hidden) return;
            SliderRow(label + " scale X", 0f, 8f, d?.ScaleX ?? 1f, v => Dist(a, key).ScaleX = v);
            SliderRow(label + " scale Y", 0f, 8f, d?.ScaleY ?? 1f, v => Dist(a, key).ScaleY = v);
            SliderRow(label + " scale Z", 0f, 8f, d?.ScaleZ ?? 1f, v => Dist(a, key).ScaleZ = v);
        }

        private static void MeshHideRow(AppearanceDraft a, string key, string label)
        {
            a.Distortion.TryGetValue(key, out BoneDistortionDraft d);
            ToggleRow(label + " - hide", d?.Hide ?? false, v => Dist(a, key).Hide = v);
        }

        // The simplified, vanilla-character-creator-like view: gender/body/hair/skin, single-select Face and Clothing
        // "slots" (each owns one layer of its category), plus the essential Backrooms binding. Everything here writes
        // to the same AppearanceDraft the Advanced view edits, so switching tabs never loses data.
        private static void BuildBasicForm(NpcDraft n)
        {
            AppearanceDraft a = n.Appearance;

            Components.SectionHeader(_formContent, "Identity");
            TextRow("Name", n.Name, v => { n.Name = v; RefreshForm(); }, refreshRailOnEdit: true);
            IdCaption(n);   // id is auto-derived from pack + name (read-only), so ids never collide
            RemoveRow();

            // Sections + slots mirror the vanilla New-Game character creator (BasicAvatarSettings + ECategory order).
            Components.SectionHeader(_formContent, "Body");
            GenderRow(a);
            SliderRow("Weight", 0f, 1f, a.Weight, v => a.Weight = v);
            ColorRow("Skin", a.SkinColor, v => a.SkinColor = v);

            Components.SectionHeader(_formContent, "Hair");
            PickerRow("Style", a.HairPath, "Choose a hair style", PathCatalog.HairStyles(), allowNone: true, v => a.HairPath = v);
            ColorRow("Colour", a.HairColor, v => a.HairColor = v);

            Components.SectionHeader(_formContent, "Face");
            SlotRow("Mouth", a.FaceLayers, PathCatalog.Mouths(), withTint: false);
            SlotRow("Facial hair", a.FaceLayers, PathCatalog.FacialHairStyles(), withTint: false);
            SlotRow("Facial details", a.FaceLayers, PathCatalog.FacialDetails(), withTint: false);

            Components.SectionHeader(_formContent, "Eyes");
            ColorRow("Eye colour", a.EyeBallTint, v => a.EyeBallTint = v);
            SliderRow("Pupil", 0f, 1f, a.PupilDilation, v => a.PupilDilation = v);
            SliderRow("Upper lid", 0f, 1f, a.LeftEyeTop, v => { a.LeftEyeTop = v; a.RightEyeTop = v; });
            SliderRow("Lower lid", 0f, 1f, a.LeftEyeBottom, v => { a.LeftEyeBottom = v; a.RightEyeBottom = v; });

            Components.SectionHeader(_formContent, "Eyebrows");
            SliderRow("Scale", 0f, 2f, a.EyebrowScale, v => a.EyebrowScale = v);
            SliderRow("Thickness", 0f, 2f, a.EyebrowThickness, v => a.EyebrowThickness = v);
            SliderRow("Height", 0f, 1f, a.EyebrowRestingHeight, v => a.EyebrowRestingHeight = v);
            SliderRow("Angle", 0f, 1f, a.EyebrowRestingAngle, v => a.EyebrowRestingAngle = v);

            Components.SectionHeader(_formContent, "Clothing");
            SlotRow("Top", a.BodyLayers, PathCatalog.Tops());
            SlotRow("Bottom", a.BodyLayers, PathCatalog.Bottoms());

            Components.SectionHeader(_formContent, "Accessories");
            SlotRow("Shoes", a.Accessories, PathCatalog.Shoes());
            SlotRow("Headwear", a.Accessories, PathCatalog.Headwear());
            SlotRow("Eyewear", a.Accessories, PathCatalog.Eyewear());

            Components.SectionHeader(_formContent, "Tattoos");
            TattooSection(a);

            // Backrooms binding is intentionally NOT in the Character view - it lives in the Advanced tab (and only
            // when the Backrooms mod is installed).

            var hint = UIFactory.Text("hint", "Tip: pick a Top/Bottom, then tap its Colour swatch to recolour it. Advanced tab: stacked/custom PNG layers, per-layer visibility & full behaviour.", _formContent, Theme.Caption, TextAnchor.UpperLeft);
            hint.color = Theme.TextMuted; hint.raycastTarget = false; hint.horizontalOverflow = HorizontalWrapMode.Wrap; hint.gameObject.AddComponent<LayoutElement>().minHeight = 44;
        }

        // Tattoos are a flat multi-select list (matching the vanilla creator's Tattoos field): the currently-applied
        // ones with a remove button, plus an "Add tattoo" picker. Each tattoo routes to the face or body layer list
        // by its path so preview/export place it correctly.
        private static void TattooSection(AppearanceDraft a)
        {
            List<PathOption> options = PathCatalog.Tattoos();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in options) set.Add(o.Path);
            ShowTattooRows(a.FaceLayers, set);
            ShowTattooRows(a.BodyLayers, set);

            var (addGO, addBtn, _) = UIFactory.ButtonWithLabel("addtat", "+ Add tattoo", _formContent, Theme.Accent, 0, 30);
            addGO.AddComponent<LayoutElement>().minHeight = 30;
            addBtn.onClick.AddListener((UnityAction)(() =>
                OptionPicker.Show(_canvasGO.transform, "Choose tattoo", options, null, allowNone: false, chosen =>
                {
                    if (string.IsNullOrEmpty(chosen)) return;
                    var list = chosen.IndexOf("/Face/", StringComparison.OrdinalIgnoreCase) >= 0 ? a.FaceLayers : a.BodyLayers;
                    if (!HasPath(list, chosen)) list.Add(new LayerDraft { Path = chosen, Tint = "#FFFFFF" });
                    RefreshForm(); MarkDirty();
                })));

            // When the Inkorporated mod is installed, its custom tattoo packs are offered too. Picking one copies the
            // tattoo PNG into this NPC pack as a custom layer (self-contained: the export needs no Inkorporated).
            if (InkorporatedInstalled())
            {
                var ink = InkorporatedCatalog.All();
                bool any = ink.Count > 0;
                var (inkGO, inkBtn, inkTxt) = UIFactory.ButtonWithLabel("addink",
                    any ? "+ Add Inkorporated tattoo" : "Inkorporated: no tattoo packs found",
                    _formContent, any ? Theme.Button : Theme.BgElevated, 0, 30);
                if (inkTxt != null) inkTxt.fontSize = Theme.Caption;
                inkGO.AddComponent<LayoutElement>().minHeight = 30;
                if (any)
                    inkBtn.onClick.AddListener((UnityAction)(() =>
                    {
                        var inkOptions = new List<PathOption>();
                        foreach (InkTattoo t in ink) inkOptions.Add(new PathOption(t.Pack, t.Name, t.PngPath));
                        OptionPicker.Show(_canvasGO.transform, "Inkorporated tattoos", inkOptions, null, allowNone: false, chosen =>
                        {
                            InkTattoo t = InkorporatedCatalog.ByPng(chosen);
                            if (t == null || _project == null) return;
                            string rel = ProjectStore.ImportSource(_project, t.PngPath);
                            if (rel == null) { Toast.Show("Import failed.", Severity.Danger); return; }
                            (t.IsFace ? a.FaceLayers : a.BodyLayers).Add(new LayerDraft { Source = rel, Tint = "#FFFFFF" });
                            RefreshForm(); MarkDirty();
                        });
                    }));
            }
        }

        // Shows tattoos already applied (both built-in path tattoos and imported custom/Inkorporated PNG layers) with
        // a remove button, so they're editable from the Character tab's Tattoos section.
        private static void ShowTattooRows(List<LayerDraft> list, HashSet<string> tattooPaths)
        {
            for (int i = 0; i < list.Count; i++)
            {
                LayerDraft l = list[i];
                if (l == null) continue;
                bool builtin = !string.IsNullOrEmpty(l.Path) && tattooPaths.Contains(l.Path);
                bool custom = !string.IsNullOrWhiteSpace(l.Source);
                if (!builtin && !custom) continue;
                string desc = custom ? Path.GetFileNameWithoutExtension(l.Source) : PathCatalog.DisplayName(l.Path);
                LayerDraft cap = l;
                var (go, btn, txt) = UIFactory.ButtonWithLabel("tat", "✕  " + desc, _formContent, Theme.Button, 0, 28);
                if (txt != null) { txt.fontSize = Theme.Caption; txt.alignment = TextAnchor.MiddleLeft; txt.rectTransform.offsetMin = new Vector2(10, 0); }
                go.AddComponent<LayoutElement>().minHeight = 28;
                btn.onClick.AddListener((UnityAction)(() => { list.Remove(cap); RefreshForm(); MarkDirty(); }));
            }
        }

        private static bool HasPath(List<LayerDraft> list, string path)
        {
            if (list == null) return false;
            foreach (LayerDraft l in list)
                if (l != null && string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The Backrooms-binding section - rendered ONLY when the Backrooms mod is actually installed (so a plain
        // NPC-pack author never sees it). Shared by both the Basic and Advanced forms.
        private static void BackroomsSection(NpcDraft n)
        {
            if (!BackroomsInstalled()) return;
            Components.SectionHeader(_formContent, "Backrooms binding (optional)");
            ToggleRow("Enable Backrooms", n.Backrooms.Enabled, v => { n.Backrooms.Enabled = v; RefreshForm(); });
            if (!n.Backrooms.Enabled) return;
            ArchetypeRow(n.Backrooms);
            StepperRow("Tier min", 0, 5, n.Backrooms.TierMin, v => n.Backrooms.TierMin = v);
            StepperRow("Tier max", 0, 5, n.Backrooms.TierMax, v => n.Backrooms.TierMax = v);
            ToggleRow("Hostile", n.Backrooms.Hostile, v => n.Backrooms.Hostile = v);
            ToggleRow("Ambient spawn", n.Backrooms.Ambient, v => n.Backrooms.Ambient = v);
            SliderRow("Spawn weight", 0f, 30f, n.Backrooms.Weight, v => n.Backrooms.Weight = v);
            StepperRow("Max alive", 1, 8, n.Backrooms.MaxAlive, v => n.Backrooms.MaxAlive = v);
            TextRow("Biomes (csv, e.g. L0,L1)", n.Backrooms.Biomes, v => n.Backrooms.Biomes = v);
        }

        private static bool BackroomsInstalled() => ModInstalled("Backrooms");
        private static bool InkorporatedInstalled() => ModInstalled("Inkorporated");

        // Is a given mod loaded? (Cached per name.) Reflected off MelonLoader's registered-melon list so Personify
        // needs no compile-time reference to it.
        private static readonly Dictionary<string, bool> _modInstalled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static bool ModInstalled(string modName)
        {
            if (_modInstalled.TryGetValue(modName, out bool cached)) return cached;
            bool found = false;
            try
            {
                var prop = typeof(MelonLoader.MelonBase).GetProperty("RegisteredMelons",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop?.GetValue(null) is System.Collections.IEnumerable melons)
                    foreach (var m in melons)
                    {
                        var info = m?.GetType().GetProperty("Info")?.GetValue(m);
                        var name = info?.GetType().GetProperty("Name")?.GetValue(info) as string;
                        if (string.Equals(name, modName, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                    }
            }
            catch { }
            _modInstalled[modName] = found;
            return found;
        }

        // A single-select "slot" over one catalog category (e.g. Shirts, Feet, Eyes): shows the current pick + a
        // trailing tint swatch, and opens the OptionPicker limited to that category. Picking replaces whatever layer
        // of that category is currently in <paramref name="list"/> ("None" removes it) - the simple, vanilla-creator
        // way to dress a slot without touching the full multi-layer list.
        private static void SlotRow(string label, List<LayerDraft> list, List<PathOption> options, bool withTint = true)
        {
            var paths = new HashSet<string>(options.Select(o => o.Path), StringComparer.OrdinalIgnoreCase);
            LayerDraft current = null;
            foreach (var l in list) if (l != null && !string.IsNullOrEmpty(l.Path) && paths.Contains(l.Path)) { current = l; break; }
            string display = current != null ? PathCatalog.DisplayName(current.Path) : "(None)";

            var row = new GameObject("slot"); row.transform.SetParent(_formContent, false); row.AddComponent<RectTransform>();
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 32; rle.preferredHeight = 32; rle.flexibleWidth = 1;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            var lbl = UIFactory.Text("l", label, row.transform, Theme.Body, TextAnchor.MiddleLeft);
            lbl.color = Theme.TextPrimary; lbl.raycastTarget = false; lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lle = lbl.gameObject.AddComponent<LayoutElement>(); lle.minWidth = 84; lle.preferredWidth = 84; lle.flexibleWidth = 0;

            var (pillGO, pillBtn, pillTxt) = UIFactory.ButtonWithLabel("pick", display + "   ▾", row.transform, Theme.SurfaceInput, 0, 30);
            if (pillTxt != null) { pillTxt.fontSize = Theme.Body; pillTxt.alignment = TextAnchor.MiddleLeft; pillTxt.rectTransform.offsetMin = new Vector2(10, 0); pillTxt.color = current != null ? Theme.TextPrimary : Theme.TextMuted; }
            var ple = pillGO.AddComponent<LayoutElement>(); ple.flexibleWidth = 1; ple.minHeight = 30;
            string keepTint = current?.Tint ?? "#FFFFFF";
            string currentPath = current?.Path ?? "";
            pillBtn.onClick.AddListener((UnityAction)(() =>
                OptionPicker.Show(_canvasGO.transform, "Choose " + label, options, currentPath, allowNone: true, chosen =>
                {
                    list.RemoveAll(x => x != null && !string.IsNullOrEmpty(x.Path) && paths.Contains(x.Path));
                    if (!string.IsNullOrEmpty(chosen)) list.Add(new LayerDraft { Path = chosen, Tint = keepTint });
                    RefreshForm(); MarkDirty();
                })));

            if (withTint && current != null)   // trailing colour control (only for slots with a value set)
            {
                var swLbl = UIFactory.Text("swl", "Colour", row.transform, Theme.Caption, TextAnchor.MiddleRight);
                swLbl.color = Theme.TextMuted; swLbl.raycastTarget = false;
                var swlle = swLbl.gameObject.AddComponent<LayoutElement>(); swlle.minWidth = 46; swlle.preferredWidth = 46; swlle.flexibleWidth = 0;

                var swGO = new GameObject("sw"); swGO.transform.SetParent(row.transform, false);
                var swImg = swGO.AddComponent<Image>(); swImg.sprite = Theme.RoundedSprite(); swImg.type = Image.Type.Sliced; swImg.color = Preview.Hex(current.Tint, Color.white);
                var swOutline = swGO.AddComponent<Outline>(); swOutline.effectColor = Theme.HairlineStrong; swOutline.effectDistance = new Vector2(1, -1);
                var swBtn = swGO.AddComponent<Button>(); swBtn.targetGraphic = swImg;
                var swle = swGO.AddComponent<LayoutElement>(); swle.minWidth = 30; swle.preferredWidth = 30;
                LayerDraft cap = current;
                swBtn.onClick.AddListener((UnityAction)(() =>
                    ColorPicker.Show(_canvasGO.transform, label + " colour", cap.Tint, hx => { cap.Tint = hx; swImg.color = Preview.Hex(hx, Color.white); MarkDirty(); })));
            }
        }

        // --- form widgets ---

        // Compact one-line row built directly (not via FormRow's 40/60 split, which wasted the whole label column on
        // short labels): a FIXED narrow label | the slider fills all remaining width | a compact live value. Gives
        // the slider maximum drag width for easy operation.
        private const float SliderLabelW = 84f;
        private const float SliderValueW = 38f;

        private static void SliderRow(string label, float min, float max, float value, Action<float> set, bool whole = false)
        {
            var row = new GameObject("sliderRow"); row.transform.SetParent(_formContent, false); row.AddComponent<RectTransform>();
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 28; rle.preferredHeight = 28; rle.flexibleWidth = 1;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            var lbl = UIFactory.Text("l", label, row.transform, Theme.Body, TextAnchor.MiddleLeft);
            lbl.color = Theme.TextPrimary; lbl.raycastTarget = false; lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lle = lbl.gameObject.AddComponent<LayoutElement>(); lle.minWidth = SliderLabelW; lle.preferredWidth = SliderLabelW; lle.flexibleWidth = 0;

            var valText = UIFactory.Text("v", Fmt(value, whole), row.transform, Theme.Caption, TextAnchor.MiddleRight);
            valText.color = Theme.TextMuted; valText.raycastTarget = false;
            var vle = valText.gameObject.AddComponent<LayoutElement>(); vle.minWidth = SliderValueW; vle.preferredWidth = SliderValueW; vle.flexibleWidth = 0;

            var slider = SliderStyle.Compact(Components.Slider(row.transform, min, max, value, v =>
            {
                set(v);
                valText.text = Fmt(v, whole);
                MarkDirty();
            }));
            slider.transform.SetSiblingIndex(1);   // order: label | slider | value
        }

        private static string Fmt(float v, bool whole) => whole ? Mathf.RoundToInt(v).ToString() : v.ToString("0.00");

        private static void TextRow(string label, string value, Action<string> set, bool refreshRailOnEdit = false)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: true);
            var input = Components.TextInput(slot, value, s =>
            {
                set(s);
                if (refreshRailOnEdit) RefreshRail();
                MarkDirty();
            });
            StretchFill(input.GetComponent<RectTransform>());   // fill the slot so the input box is visible + clickable
        }

        private static void ToggleRow(string label, bool value, Action<bool> set)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var tg = Components.Toggle(slot, value, v => { set(v); MarkDirty(); });
            var trt = tg.GetComponent<RectTransform>();   // left-align the fixed-size switch inside the control column
            trt.anchorMin = trt.anchorMax = new Vector2(0, 0.5f); trt.pivot = new Vector2(0, 0.5f); trt.anchoredPosition = Vector2.zero;
        }

        // Compact integer stepper ("-  N  +") for small closed ranges (Tier 0-5, Max alive 1-8) that don't read
        // well as a continuous slider. Left-aligned in the control column; +/- disable at the bounds.
        private static void StepperRow(string label, int min, int max, int value, Action<int> set)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var wrap = new GameObject("stepper"); wrap.transform.SetParent(slot, false);
            StretchFill(wrap.AddComponent<RectTransform>());
            var h = wrap.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            int current = Mathf.Clamp(value, min, max);

            var (minusGO, minusBtn, minusTxt) = UIFactory.ButtonWithLabel("minus", "-", wrap.transform, Theme.Button, 28, 28);
            if (minusTxt != null) minusTxt.fontSize = Theme.Label;
            minusGO.AddComponent<LayoutElement>().minWidth = 28;

            var valText = UIFactory.Text("v", current.ToString(), wrap.transform, Theme.Label, TextAnchor.MiddleCenter, FontStyle.Bold);
            valText.color = Theme.TextPrimary; valText.raycastTarget = false;
            var vle = valText.gameObject.AddComponent<LayoutElement>(); vle.minWidth = 36; vle.preferredWidth = 36;

            var (plusGO, plusBtn, plusTxt) = UIFactory.ButtonWithLabel("plus", "+", wrap.transform, Theme.Button, 28, 28);
            if (plusTxt != null) plusTxt.fontSize = Theme.Label;
            plusGO.AddComponent<LayoutElement>().minWidth = 28;

            void Apply(int v)
            {
                current = Mathf.Clamp(v, min, max);
                valText.text = current.ToString();
                minusBtn.interactable = current > min;
                plusBtn.interactable = current < max;
                set(current);
                MarkDirty();
            }
            minusBtn.interactable = current > min;
            plusBtn.interactable = current < max;
            minusBtn.onClick.AddListener((UnityAction)(() => Apply(current - 1)));
            plusBtn.onClick.AddListener((UnityAction)(() => Apply(current + 1)));
        }

        // Gender is a binary choice, not a magnitude - a labelled two-segment control (reads as a toggle) instead of
        // a slider. Maps to the vanilla 0..1 Gender field (0 = male, 1 = female).
        private static void GenderRow(AppearanceDraft a)
        {
            string[] labels = { "Male", "Female" };
            int idx = a.Gender >= 0.5f ? 1 : 0;
            var row = Components.FormRow(_formContent, "Gender", null, out Transform slot, stacked: false, height: 30);
            var segGO = Components.Segmented(slot, labels, idx, i => { a.Gender = i == 0 ? 0f : 1f; MarkDirty(); }, out _);
            StretchFill(segGO.GetComponent<RectTransform>());
        }

        // A small closed set of string values (e.g. Conversation) reads better as a Segmented control than free text.
        private static void ConversationRow(string label, string current, Action<string> set)
        {
            string[] values = { "none", "customer", "dealer" };
            string[] labels = { "None", "Customer", "Dealer" };
            int idx = Array.IndexOf(values, (current ?? "none").ToLowerInvariant());
            if (idx < 0) idx = 0;

            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var segGO = Components.Segmented(slot, labels, idx, i => { set(values[i]); MarkDirty(); }, out _);
            StretchFill(segGO.GetComponent<RectTransform>());
        }

        // A colour field row: a small clickable swatch chip + hex text inside a pill-shaped clickable background.
        // Opens the ColorPicker modal; on confirm writes the #RRGGBB string back and refreshes the swatch in place
        // (no full form rebuild, so repeatedly tweaking a colour doesn't reset scroll position).
        private static void ColorRow(string label, string hex, Action<string> set)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var wrap = new GameObject("colorwrap"); wrap.transform.SetParent(slot, false);
            StretchFill(wrap.AddComponent<RectTransform>());
            var wrapImg = wrap.AddComponent<Image>(); wrapImg.color = Theme.SurfaceInput; wrapImg.sprite = Theme.RoundedSprite(); wrapImg.type = Image.Type.Sliced;
            var wrapBtn = wrap.AddComponent<Button>(); wrapBtn.targetGraphic = wrapImg;
            var h = wrap.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.padding = new RectOffset(6, 10, 3, 3); h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            var swatchGO = new GameObject("swatch"); swatchGO.transform.SetParent(wrap.transform, false);
            var swImg = swatchGO.AddComponent<Image>(); swImg.sprite = Theme.RoundedSprite(); swImg.type = Image.Type.Sliced; swImg.color = Preview.Hex(hex, Color.white); swImg.raycastTarget = false;
            var swle = swatchGO.AddComponent<LayoutElement>(); swle.minWidth = 24; swle.preferredWidth = 24;

            var hexText = UIFactory.Text("hex", (hex ?? "#FFFFFF").ToUpperInvariant(), wrap.transform, Theme.Body, TextAnchor.MiddleLeft);
            hexText.color = Theme.TextPrimary; hexText.raycastTarget = false;
            var hle = hexText.gameObject.AddComponent<LayoutElement>(); hle.flexibleWidth = 1; hle.minWidth = 0; hle.preferredWidth = 0;

            string currentHex = hex;   // kept in sync so re-opening the picker after an earlier pick shows the latest colour
            wrapBtn.onClick.AddListener((UnityAction)(() =>
                ColorPicker.Show(_canvasGO.transform, label, currentHex, newHex =>
                {
                    currentHex = newHex;
                    set(newHex);
                    swImg.color = Preview.Hex(newHex, Color.white);
                    hexText.text = newHex.ToUpperInvariant();
                    MarkDirty();
                })));
        }

        // A path field row (Hair style, etc.): a clickable pill showing the current display name (or "(None)"),
        // opens the OptionPicker modal listing the real catalog options. Picking rebuilds the form (simplest way to
        // keep every dependent row in sync, matching how ToggleRow's dependent sections already refresh).
        private static void PickerRow(string label, string currentPath, string pickerTitle, List<PathOption> options, bool allowNone, Action<string> set)
        {
            var row = Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var wrap = new GameObject("pick"); wrap.transform.SetParent(slot, false);
            StretchFill(wrap.AddComponent<RectTransform>());
            var img = wrap.AddComponent<Image>(); img.color = Theme.SurfaceInput; img.sprite = Theme.RoundedSprite(); img.type = Image.Type.Sliced;
            var btn = wrap.AddComponent<Button>(); btn.targetGraphic = img;

            bool has = !string.IsNullOrEmpty(currentPath);
            var txt = UIFactory.Text("t", has ? PathCatalog.DisplayName(currentPath) : "(None)", wrap.transform, Theme.Body, TextAnchor.MiddleLeft);
            txt.color = has ? Theme.TextPrimary : Theme.TextMuted; txt.raycastTarget = false;
            var trt = txt.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-26, 0);

            var chevron = UIFactory.Text("chev", "▾", wrap.transform, Theme.Caption, TextAnchor.MiddleRight);
            chevron.color = Theme.TextMuted; chevron.raycastTarget = false;
            var crt = chevron.rectTransform; crt.anchorMin = new Vector2(1, 0); crt.anchorMax = Vector2.one; crt.pivot = new Vector2(1, 0.5f); crt.offsetMin = new Vector2(-22, 0); crt.offsetMax = new Vector2(-8, 0);

            btn.onClick.AddListener((UnityAction)(() =>
                OptionPicker.Show(_canvasGO.transform, pickerTitle, options, currentPath, allowNone, chosen =>
                {
                    set(chosen ?? "");
                    RefreshForm();
                    MarkDirty();
                })));
        }

        // FormRow slots have no layout group of their own, so a control placed inside must fill the slot explicitly.
        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // The NPC's id is auto-derived (normalized pack_name); shown read-only so consumers know what to reference.
        private static void IdCaption(NpcDraft n)
        {
            string id = _project != null ? Ids.Make(_project.Name, n.Name) : Ids.Normalize(n.Name);
            var t = UIFactory.Text("idcap", "id: " + id, _formContent, Theme.Caption, TextAnchor.UpperLeft);
            t.color = Theme.TextMuted; t.raycastTarget = false;
            t.gameObject.AddComponent<LayoutElement>().minHeight = 18;
        }

        private static void RemoveRow()
        {
            var (go, btn, _) = UIFactory.ButtonWithLabel("rm", "Remove this NPC", _formContent, Theme.Button, 0, 32);
            go.AddComponent<LayoutElement>().minHeight = 32;
            btn.onClick.AddListener((UnityAction)(() =>
                Components.ConfirmDialog(_canvasGO.transform, "Remove NPC?", $"Remove \"{_selected?.Name}\" from this pack?", "Remove", () => RemoveSelected())));
        }

        private static void ArchetypeRow(BackroomsDraft b)
        {
            // Added straight to the scroll content (which controls width + honours the wrap's LayoutElement height),
            // not into a fixed-height FormRow slot - the archetype grid is several rows tall.
            var lbl = UIFactory.Text("archlbl", "Archetype (which Backrooms brain)", _formContent, Theme.Label, TextAnchor.LowerLeft);
            lbl.color = Theme.TextPrimary; lbl.raycastTarget = false;
            lbl.gameObject.AddComponent<LayoutElement>().minHeight = 22;

            var wrap = new GameObject("arch"); wrap.transform.SetParent(_formContent, false); wrap.AddComponent<RectTransform>();
            var grid = wrap.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2((PanelW - 60) / 2f, 30); grid.spacing = new Vector2(6, 6); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            int gridRows = Mathf.CeilToInt(BackroomsPreset.Archetypes.Length / 2f);
            var wle = wrap.AddComponent<LayoutElement>(); wle.minHeight = gridRows * 36; wle.preferredHeight = gridRows * 36;
            var buttons = new List<Button>();
            for (int i = 0; i < BackroomsPreset.Archetypes.Length; i++)
            {
                string arch = BackroomsPreset.Archetypes[i];
                var (go, btn, txt) = UIFactory.ButtonWithLabel("a_" + arch, arch, wrap.transform, arch == b.Archetype ? Theme.Accent : Theme.Button, 0, 30);
                if (txt != null) txt.fontSize = Theme.Caption;
                buttons.Add(btn);
                btn.onClick.AddListener((UnityAction)(() =>
                {
                    b.Archetype = arch;
                    for (int k = 0; k < buttons.Count; k++)
                    {
                        var im = buttons[k].GetComponent<Image>();
                        if (im != null) im.color = BackroomsPreset.Archetypes[k] == arch ? Theme.Accent : Theme.Button;
                    }
                }));
            }
            Interactions.PolishButtons(wrap.transform);
        }

        // --- layers ---

        // Face/Body layers AND Accessories are all just List<LayerDraft> {Path|Source, Tint}, so one widget serves
        // all three sections: each existing entry is a row (tint swatch + display name + remove), "+ Add ..." opens
        // the OptionPicker over the real catalog for that category, and (Face/Body only) a secondary "Import custom
        // PNG..." keeps the original free-form import flow available.
        private static void LayerSection(string title, List<LayerDraft> list, List<PathOption> catalog, string kindLabel, bool allowCustomImport = true)
        {
            Components.SectionHeader(_formContent, title + " (" + (list?.Count ?? 0) + ")");
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    LayerRow(list, list[i]);

            var (addGO, addBtn, _) = UIFactory.ButtonWithLabel("addlyr", "+ Add " + kindLabel, _formContent, Theme.Accent, 0, 32);
            addGO.AddComponent<LayoutElement>().minHeight = 32;
            addBtn.onClick.AddListener((UnityAction)(() =>
                OptionPicker.Show(_canvasGO.transform, "Choose " + kindLabel, catalog, null, allowNone: false, chosen =>
                {
                    if (string.IsNullOrEmpty(chosen) || list == null) return;
                    list.Add(new LayerDraft { Path = chosen, Tint = "#FFFFFF" });
                    RefreshForm(); MarkDirty();
                })));

            if (allowCustomImport)
            {
                var (impGO, impBtn, impTxt) = UIFactory.ButtonWithLabel("implyr", "Import custom PNG...", _formContent, Theme.Button, 0, 28);
                if (impTxt != null) impTxt.fontSize = Theme.Caption;
                impGO.AddComponent<LayoutElement>().minHeight = 28;
                impBtn.onClick.AddListener((UnityAction)(() => ImportLayerFlow(list, kindLabel)));
            }
        }

        // One existing layer/accessory entry: a visibility eye (leftmost), a clickable tint swatch (opens ColorPicker),
        // the display name, and a remove (X) button. Positioned with explicit anchors (not a layout group).
        private static void LayerRow(List<LayerDraft> list, LayerDraft l)
        {
            string desc = !string.IsNullOrWhiteSpace(l.Source) ? "PNG " + Path.GetFileName(l.Source) : PathCatalog.DisplayName(l.Path);
            LayerDraft captured = l;

            var row = UIFactory.Panel("lyr", _formContent, Theme.BgElevated);
            var rimg = row.GetComponent<Image>(); if (rimg != null) { rimg.sprite = Theme.RoundedSprite(); rimg.type = Image.Type.Sliced; }
            row.AddComponent<LayoutElement>().minHeight = 36;

            // Visibility eye (leftmost): toggles whether the layer shows in the live preview and is included in export.
            var (eyeGO, eyeBtn, eyeTxt) = UIFactory.ButtonWithLabel("eye", "", row.transform, Theme.Button, 26, 26);
            var eyert = eyeGO.GetComponent<RectTransform>();
            eyert.anchorMin = eyert.anchorMax = new Vector2(0, 0.5f); eyert.pivot = new Vector2(0, 0.5f);
            eyert.anchoredPosition = new Vector2(8, 0); eyert.sizeDelta = new Vector2(26, 26);
            var eyeIconGO = new GameObject("i"); eyeIconGO.transform.SetParent(eyeGO.transform, false);
            var eyeImg = eyeIconGO.AddComponent<Image>(); eyeImg.raycastTarget = false; eyeImg.preserveAspect = true;
            var eirt = eyeIconGO.GetComponent<RectTransform>(); eirt.anchorMin = eirt.anchorMax = new Vector2(0.5f, 0.5f); eirt.pivot = new Vector2(0.5f, 0.5f); eirt.sizeDelta = new Vector2(16, 16);

            var swatchGO = new GameObject("swatch"); swatchGO.transform.SetParent(row.transform, false);
            var swImg = swatchGO.AddComponent<Image>(); swImg.sprite = Theme.RoundedSprite(); swImg.type = Image.Type.Sliced; swImg.color = Preview.Hex(l.Tint, Color.white);
            var swBtn = swatchGO.AddComponent<Button>(); swBtn.targetGraphic = swImg;
            var swrt = swatchGO.GetComponent<RectTransform>();
            swrt.anchorMin = new Vector2(0, 0.5f); swrt.anchorMax = new Vector2(0, 0.5f); swrt.pivot = new Vector2(0, 0.5f);
            swrt.anchoredPosition = new Vector2(42, 0); swrt.sizeDelta = new Vector2(24, 24);
            swBtn.onClick.AddListener((UnityAction)(() =>
                ColorPicker.Show(_canvasGO.transform, "Layer tint", l.Tint, newHex => { l.Tint = newHex; swImg.color = Preview.Hex(newHex, Color.white); MarkDirty(); })));

            var t = UIFactory.Text("t", desc, row.transform, Theme.Body, TextAnchor.MiddleLeft);
            t.raycastTarget = false;
            var trt = t.rectTransform; trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1); trt.offsetMin = new Vector2(74, 0); trt.offsetMax = new Vector2(-38, 0);

            var (rmGO, rmBtn, rmTxt) = UIFactory.ButtonWithLabel("rm", "✕", row.transform, Theme.Button, 26, 26);
            if (rmTxt != null) rmTxt.fontSize = Theme.Body;
            var rmrt = rmGO.GetComponent<RectTransform>();
            rmrt.anchorMin = new Vector2(1, 0.5f); rmrt.anchorMax = new Vector2(1, 0.5f); rmrt.pivot = new Vector2(1, 0.5f);
            rmrt.anchoredPosition = new Vector2(-8, 0); rmrt.sizeDelta = new Vector2(26, 26);
            rmBtn.onClick.AddListener((UnityAction)(() => { list.Remove(captured); RefreshForm(); MarkDirty(); }));

            void RenderEye()
            {
                Sprite sp = Icons.GetSprite(captured.Visible ? "eye" : "eye_off");
                if (sp != null)
                {
                    eyeImg.enabled = true; eyeImg.sprite = sp;
                    eyeImg.color = captured.Visible ? Theme.TextPrimary : Theme.TextMuted;
                    if (eyeTxt != null) eyeTxt.text = "";
                }
                else if (eyeTxt != null)   // font-glyph fallback if the PNG is unavailable
                {
                    eyeImg.enabled = false;
                    eyeTxt.text = captured.Visible ? "◉" : "○";   // filled / hollow circle
                    eyeTxt.fontSize = Theme.Body; eyeTxt.color = captured.Visible ? Theme.TextPrimary : Theme.TextMuted;
                }
                t.color = captured.Visible ? Theme.TextPrimary : Theme.TextDisabled;   // dim the name when hidden
            }
            RenderEye();
            eyeBtn.onClick.AddListener((UnityAction)(() => { captured.Visible = !captured.Visible; RenderEye(); MarkDirty(); }));
        }

        private static void ImportLayerFlow(List<LayerDraft> list, string kindLabel)
        {
            string hint = "Put a PNG in:\n" + Paths.Import + "\nthen enter its filename.";
            Components.PromptDialog(_canvasGO.transform, "Import PNG " + kindLabel, hint, "grin.png", "Import", fileName =>
            {
                if (string.IsNullOrWhiteSpace(fileName)) return "Enter a filename.";
                string abs = Path.Combine(Paths.Import, fileName.Trim());
                if (!File.Exists(abs)) return "Not found in the Import folder.";
                string rel = ProjectStore.ImportSource(_project, abs);
                if (rel == null) return "Import failed.";
                list.Add(new LayerDraft { Source = rel, Tint = "#FFFFFF" });
                RefreshForm(); MarkDirty();
                return null;
            });
        }

        // --- export / exit ---

        private static void ExportFlow()
        {
            ProjectStore.Save(_project);
            string err = Exporter.Validate(_project);
            if (err != null) { Toast.Show(err, Severity.Warning); return; }
            ExportResult r = Exporter.Export(_project);   // all files are fully written before we open the folder
            Toast.Show(r.Message, r.Ok ? Severity.Success : Severity.Danger);
            if (r.Ok && !string.IsNullOrEmpty(r.ExportDir))
                RevealInExplorer(r.ExportDir);
        }

        // Open the finished export folder AFTER all files are written, forcing Explorer to refresh: '/select' navigates
        // to (and re-reads) the folder even if a stale window already had it open, so the just-written files show up.
        private static void RevealInExplorer(string dir)
        {
            try
            {
                string target = null;
                try
                {
                    string m = Path.Combine(dir, "manifest.json");
                    if (File.Exists(m)) target = m;
                    else { var files = Directory.GetFiles(dir); if (files.Length > 0) target = files[0]; }
                }
                catch { }

                var psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = true };
                if (target != null) { psi.FileName = "explorer.exe"; psi.Arguments = "/select,\"" + target + "\""; }
                else psi.FileName = dir;
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        private static void ExitToHub()
        {
            if (_ctx != null) { try { _ctx.ReturnToHub(); return; } catch { } }
            Close();
        }
    }
}
