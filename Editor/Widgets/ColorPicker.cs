using System;
using DooDesch.UI;
using Personify.Editor;
using S1API.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Personify.Editor.Widgets
{
    /// <summary>
    /// A modal colour-picker (dimmed scrim + centred card on the canvas root), modelled on
    /// <see cref="Components.ConfirmDialog"/>/<see cref="Components.PromptDialog"/>: a live preview swatch, an R/G/B
    /// slider trio kept in sync with a hex text field, a row of preset swatches, and Cancel / "Use colour". Reuses
    /// <see cref="Preview.Hex"/>/<see cref="Preview.HexOf"/> for parsing so it stays byte-identical with how the
    /// draft's <c>#RRGGBB</c> strings are turned into <see cref="Color"/>s elsewhere.
    /// </summary>
    public static class ColorPicker
    {
        // The game's standard clothing palette (the 27 EClothingColor names), so recolouring an NPC's clothes
        // lands on the familiar in-game colours in one tap. NPC layer tints are free RGBA, so the R/G/B sliders
        // below still allow any custom colour - these are just quick picks.
        private static readonly string[] Presets =
        {
            "#FFFFFF", "#C8C8C8", "#6E6E6E", "#3C3C3C", "#141414", "#FF6B6B", "#D6342C", "#9C1B2E", "#E8862B",
            "#C8A165", "#6E4423", "#FF7F5C", "#E8D8B0", "#F2D14A", "#A6D63C", "#6FCB6F", "#2E7D32", "#3FC8C8",
            "#6EC6E8", "#2F6FD6", "#24408E", "#1B2A4A", "#4A2E8E", "#7A3FB0", "#C83FB0", "#F25CB0", "#FF4FA3",
        };

        public static void Show(Transform canvasRoot, string title, string initialHex, Action<string> onConfirm)
        {
            if (canvasRoot == null) return;
            Color32 current = Preview.Hex(initialHex, Color.white);

            var scrim = UIFactory.Panel("DD_ColorScrim", canvasRoot, new Color(0f, 0f, 0f, 0.6f), fullAnchor: true);
            scrim.transform.SetAsLastSibling();
            void Close() { UnityEngine.Object.Destroy(scrim); }

            var catcher = UIFactory.Panel("catcher", scrim.transform, new Color(0f, 0f, 0f, 0.01f), fullAnchor: true);
            var cbtn = catcher.AddComponent<Button>(); cbtn.targetGraphic = catcher.GetComponent<Image>();
            cbtn.onClick.AddListener((UnityAction)(() => Close()));

            var card = UIFactory.Panel("card", scrim.transform, Theme.BgElevated);
            var cimg = card.GetComponent<Image>(); if (cimg != null) { cimg.sprite = Theme.RoundedSprite(); cimg.type = Image.Type.Sliced; }
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(420, 468);
            var ol = card.AddComponent<Outline>(); ol.effectColor = Theme.HairlineStrong; ol.effectDistance = new Vector2(1, -1);

            // A full-width "band" positioned `top` px below the card's top edge - the layout unit every row below
            // is built from (mirrors the offset math ConfirmDialog/PromptDialog use for their title/message rects).
            Transform Band(float top, float height)
            {
                var bgo = new GameObject("band"); bgo.transform.SetParent(card.transform, false);
                var rt = bgo.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(20, -(top + height)); rt.offsetMax = new Vector2(-20, -top);
                return bgo.transform;
            }

            var t = UIFactory.Text("title", title, card.transform, Theme.H3, TextAnchor.UpperLeft, FontStyle.Bold);
            t.color = Theme.TextPrimary; t.raycastTarget = false;
            var trt = t.rectTransform; trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(20, -50); trt.offsetMax = new Vector2(-20, -18);

            // swatch + hex field
            var swatchHexBand = Band(58, 68);
            var shH = swatchHexBand.gameObject.AddComponent<HorizontalLayoutGroup>();
            shH.spacing = 14; shH.childAlignment = TextAnchor.MiddleLeft;
            shH.childControlWidth = true; shH.childControlHeight = true; shH.childForceExpandWidth = false; shH.childForceExpandHeight = true;

            var swatchGO = new GameObject("swatch"); swatchGO.transform.SetParent(swatchHexBand, false);
            var swImg = swatchGO.AddComponent<Image>(); swImg.sprite = Theme.RoundedSprite(); swImg.type = Image.Type.Sliced; swImg.color = current;
            var swOutline = swatchGO.AddComponent<Outline>(); swOutline.effectColor = Theme.HairlineStrong; swOutline.effectDistance = new Vector2(1, -1);
            var swle = swatchGO.AddComponent<LayoutElement>(); swle.minWidth = 64; swle.preferredWidth = 64; swle.flexibleWidth = 0;

            var hexBlock = new GameObject("hexBlock"); hexBlock.transform.SetParent(swatchHexBand, false);
            hexBlock.AddComponent<RectTransform>();
            var hv = hexBlock.AddComponent<VerticalLayoutGroup>();
            hv.spacing = 4; hv.childControlWidth = true; hv.childControlHeight = true; hv.childForceExpandWidth = true; hv.childForceExpandHeight = false; hv.childAlignment = TextAnchor.UpperLeft;
            var hble = hexBlock.AddComponent<LayoutElement>(); hble.flexibleWidth = 1;

            var hexLabel = UIFactory.Text("hl", "HEX", hexBlock.transform, Theme.Caption, TextAnchor.UpperLeft, FontStyle.Bold);
            hexLabel.color = Theme.TextMuted; hexLabel.raycastTarget = false;
            hexLabel.gameObject.AddComponent<LayoutElement>().minHeight = 16;

            // R/G/B sliders (created below) need to be pushed to when the hex field or a preset changes; declared
            // up-front so the hex field's callback (built next) can reach them.
            var sliders = new Slider[3];

            InputField hexInput = Components.TextInput(hexBlock.transform, Preview.HexOf(current), null, "#RRGGBB", 7);
            hexInput.gameObject.AddComponent<LayoutElement>().minHeight = 32;

            void Refresh() { swImg.color = current; hexInput.text = Preview.HexOf(current); }

            void PushToSliders()
            {
                // Slider.Set() no-ops (and does not fire onValueChanged) when the value doesn't actually change, so
                // this can't loop back into the per-channel callbacks below.
                if (sliders[0] != null) sliders[0].value = current.r;
                if (sliders[1] != null) sliders[1].value = current.g;
                if (sliders[2] != null) sliders[2].value = current.b;
            }

            // onEndEdit (fires on blur/Enter) - typing a hex value updates the swatch + sliders.
            hexInput.onEndEdit.AddListener((UnityAction<string>)(s =>
            {
                current = Preview.Hex(s, current);
                Refresh();
                PushToSliders();
            }));

            void ChannelRow(int idx, string label, float top, byte initial, Action<byte> apply)
            {
                var band = Band(top, 30);
                var h = band.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 10; h.childAlignment = TextAnchor.MiddleLeft;
                h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;

                var lbl = UIFactory.Text("lbl", label, band, Theme.Label, TextAnchor.MiddleLeft, FontStyle.Bold);
                lbl.color = Theme.TextPrimary; lbl.raycastTarget = false;
                var lle = lbl.gameObject.AddComponent<LayoutElement>(); lle.minWidth = 18; lle.preferredWidth = 18;

                var valText = UIFactory.Text("val", initial.ToString(), band, Theme.Caption, TextAnchor.MiddleRight);
                valText.color = Theme.TextMuted; valText.raycastTarget = false;
                var vle = valText.gameObject.AddComponent<LayoutElement>(); vle.minWidth = 34; vle.preferredWidth = 34;

                var slider = SliderStyle.Compact(Components.Slider(band, 0, 255, initial, v =>
                {
                    byte b = (byte)Mathf.RoundToInt(v);
                    apply(b);
                    valText.text = b.ToString();
                    Refresh();
                }));
                sliders[idx] = slider;
                valText.transform.SetAsLastSibling();
            }

            ChannelRow(0, "R", 136, current.r, b => current.r = b);
            ChannelRow(1, "G", 172, current.g, b => current.g = b);
            ChannelRow(2, "B", 208, current.b, b => current.b = b);

            var presetLabel = UIFactory.Text("pl", "PRESETS", Band(244, 16), Theme.Caption, TextAnchor.LowerLeft, FontStyle.Bold);
            presetLabel.color = Theme.TextMuted; presetLabel.raycastTarget = false;
            var plrt = presetLabel.rectTransform; plrt.anchorMin = Vector2.zero; plrt.anchorMax = Vector2.one; plrt.offsetMin = Vector2.zero; plrt.offsetMax = Vector2.zero;

            var presetBand = Band(262, 96);
            var pg = presetBand.gameObject.AddComponent<GridLayoutGroup>();
            pg.cellSize = new Vector2(28, 26); pg.spacing = new Vector2(6, 6);
            pg.childAlignment = TextAnchor.UpperLeft;
            pg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; pg.constraintCount = 9;
            foreach (string hex in Presets)
            {
                var pGO = new GameObject("preset"); pGO.transform.SetParent(presetBand, false);
                var pImg = pGO.AddComponent<Image>(); pImg.sprite = Theme.RoundedSprite(); pImg.type = Image.Type.Sliced; pImg.color = Preview.Hex(hex, Color.white);
                var pBtn = pGO.AddComponent<Button>(); pBtn.targetGraphic = pImg;
                string captured = hex;
                pBtn.onClick.AddListener((UnityAction)(() =>
                {
                    current = Preview.Hex(captured, current);
                    Refresh();
                    PushToSliders();
                }));
            }

            var (cancelGO, cancelBtn, _) = UIFactory.ButtonWithLabel("Cancel", "Cancel", card.transform, Theme.Button, 140, 40);
            var cart = cancelGO.GetComponent<RectTransform>();
            cart.anchorMin = new Vector2(0, 0); cart.anchorMax = new Vector2(0, 0); cart.pivot = new Vector2(0, 0);
            cart.anchoredPosition = new Vector2(20, 18); cart.sizeDelta = new Vector2(140, 40);
            cancelBtn.onClick.AddListener((UnityAction)(() => Close()));

            var (okGO, okBtn, _2) = UIFactory.ButtonWithLabel("Confirm", "Use colour", card.transform, Theme.Accent, 180, 40);
            var okrt = okGO.GetComponent<RectTransform>();
            okrt.anchorMin = new Vector2(1, 0); okrt.anchorMax = new Vector2(1, 0); okrt.pivot = new Vector2(1, 0);
            okrt.anchoredPosition = new Vector2(-20, 18); okrt.sizeDelta = new Vector2(180, 40);
            okBtn.onClick.AddListener((UnityAction)(() => { Close(); onConfirm?.Invoke(Preview.HexOf(current)); }));

            Interactions.PolishButtons(card.transform);
        }
    }
}
