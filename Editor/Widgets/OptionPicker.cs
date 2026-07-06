using System;
using System.Collections.Generic;
using DooDesch.UI;
using Personify.Editor;
using S1API.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Personify.Editor.Widgets
{
    /// <summary>
    /// A modal, searchable option-picker (dimmed scrim + centred card on the canvas root), modelled on
    /// <see cref="Components.ConfirmDialog"/>/<see cref="Components.PromptDialog"/>: a live-filtering search box, a
    /// scrollable list grouped under section headers (e.g. accessory slot, or face-layer sub-category), the
    /// currently-selected option highlighted, and an optional leading "(None)" entry. Picking an option (or "(None)")
    /// invokes the callback with the stored asset path (or "" for none) and closes the dialog; there is no separate
    /// OK button - clicking a row commits immediately, mirroring how the NPC list / project list rows work elsewhere.
    /// </summary>
    public static class OptionPicker
    {
        public static void Show(Transform canvasRoot, string title, List<PathOption> options, string currentPath, bool allowNone, Action<string> onPick)
        {
            if (canvasRoot == null) return;
            options ??= new List<PathOption>();

            var scrim = UIFactory.Panel("DD_OptionScrim", canvasRoot, new Color(0f, 0f, 0f, 0.6f), fullAnchor: true);
            scrim.transform.SetAsLastSibling();
            void Close() { UnityEngine.Object.Destroy(scrim); }

            var catcher = UIFactory.Panel("catcher", scrim.transform, new Color(0f, 0f, 0f, 0.01f), fullAnchor: true);
            var cbtn = catcher.AddComponent<Button>(); cbtn.targetGraphic = catcher.GetComponent<Image>();
            cbtn.onClick.AddListener((UnityAction)(() => Close()));

            var card = UIFactory.Panel("card", scrim.transform, Theme.BgElevated);
            var cimg = card.GetComponent<Image>(); if (cimg != null) { cimg.sprite = Theme.RoundedSprite(); cimg.type = Image.Type.Sliced; }
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(460, 560);
            var ol = card.AddComponent<Outline>(); ol.effectColor = Theme.HairlineStrong; ol.effectDistance = new Vector2(1, -1);

            var t = UIFactory.Text("title", title, card.transform, Theme.H3, TextAnchor.UpperLeft, FontStyle.Bold);
            t.color = Theme.TextPrimary; t.raycastTarget = false;
            var trt = t.rectTransform; trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(20, -50); trt.offsetMax = new Vector2(-56, -18);   // leave room for the close (X) button

            var (closeGO, closeBtn, closeTxt) = UIFactory.ButtonWithLabel("x", "✕", card.transform, Theme.Button, 28, 28);
            if (closeTxt != null) closeTxt.fontSize = Theme.Body;
            var clrt = closeGO.GetComponent<RectTransform>(); clrt.anchorMin = new Vector2(1, 1); clrt.anchorMax = new Vector2(1, 1); clrt.pivot = new Vector2(1, 1);
            clrt.anchoredPosition = new Vector2(-16, -16); clrt.sizeDelta = new Vector2(28, 28);
            closeBtn.onClick.AddListener((UnityAction)(() => Close()));

            // Search box - TextInput's built-in onChange only fires on onEndEdit (blur/Enter); live filtering needs
            // onValueChanged, wired directly (null onChange skips the onEndEdit listener).
            InputField search = Components.TextInput(card.transform, "", null, "Search...", 40);
            var srt = search.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1); srt.pivot = new Vector2(0.5f, 1);
            srt.offsetMin = new Vector2(20, -92); srt.offsetMax = new Vector2(-20, -58);

            var listPanel = UIFactory.Panel("list", card.transform, Theme.Clear);
            var lrt = listPanel.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1); lrt.pivot = new Vector2(0.5f, 1);
            lrt.offsetMin = new Vector2(10, 20); lrt.offsetMax = new Vector2(-10, -100);
            var content = Components.ScrollList(listPanel.transform, out _, 4f);

            void Rebuild(string filter)
            {
                UIFactory.ClearChildren(content);
                string f = (filter ?? "").Trim();
                bool any = false;

                if (allowNone && (f.Length == 0 || "none".IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    any = true;
                    AddRow(content, "(None)", "", string.IsNullOrEmpty(currentPath), onPick, Close);
                }

                string lastGroup = null;
                foreach (var opt in options)
                {
                    if (f.Length > 0
                        && opt.Display.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                        && opt.Group.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    any = true;
                    if (opt.Group != lastGroup)
                    {
                        AddGroupHeader(content, opt.Group);
                        lastGroup = opt.Group;
                    }
                    AddRow(content, opt.Display, opt.Path, opt.Path == currentPath, onPick, Close);
                }

                if (!any)
                {
                    var empty = UIFactory.Text("empty", "No matches.", content, 13, TextAnchor.UpperLeft);
                    empty.color = Theme.TextMuted; empty.gameObject.AddComponent<LayoutElement>().minHeight = 32;
                }
                Interactions.PolishButtons(content);
            }

            search.onValueChanged.AddListener((UnityAction<string>)(s => Rebuild(s)));
            Rebuild("");
        }

        private static void AddGroupHeader(Transform content, string group)
        {
            var lbl = UIFactory.Text("grp", (group ?? "").ToUpperInvariant(), content, Theme.Caption, TextAnchor.LowerLeft, FontStyle.Bold);
            lbl.color = Theme.Accent; lbl.raycastTarget = false;
            var le = lbl.gameObject.AddComponent<LayoutElement>(); le.minHeight = 22; le.preferredHeight = 22;
        }

        private static void AddRow(Transform content, string display, string path, bool selected, Action<string> onPick, Action close)
        {
            var (go, btn, txt) = UIFactory.ButtonWithLabel("opt", display, content, selected ? Theme.Accent : Theme.Button, 0, 32);
            if (txt != null) { txt.fontSize = Theme.Body; txt.alignment = TextAnchor.MiddleLeft; txt.rectTransform.offsetMin = new Vector2(12, 0); }
            go.AddComponent<LayoutElement>().minHeight = 32;
            btn.onClick.AddListener((UnityAction)(() => { close(); onPick?.Invoke(path); }));
        }
    }
}
