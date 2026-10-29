using System;
using System.Collections.Generic;
using System.Globalization;
using DooDesch.UI;
using Newtonsoft.Json.Linq;
using Personify.Editor.Widgets;
using S1API.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Personify.Editor
{
    // The customer data Personnel reads from the "customer" block, plus the starting relationship from
    // "relationships.delta". Each row shows what the NPC gets: the stored key when there is one, otherwise the
    // vanilla CustomerData default S1API starts from. A row writes its key on the first edit, so untouched values
    // stay out of the manifest; keys the editor does not know are kept as they are.
    public static partial class EditorUI
    {
        private static readonly string[] DayValues = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        private static readonly string[] DayLabels = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
        private static readonly string[] StandardValues = { "VeryLow", "Low", "Moderate", "High", "VeryHigh" };
        private static readonly string[] StandardLabels = { "Very low", "Low", "Moderate", "High", "Very high" };

        // Keys are EDrugType names; Personnel and S1API parse them case-insensitively.
        private static readonly (string Key, string Label)[] DrugTypes =
        {
            ("marijuana", "Weed"), ("methamphetamine", "Meth"), ("cocaine", "Cocaine"), ("mdma", "MDMA"), ("shrooms", "Shrooms")
        };

        // Vanilla CustomerData field initializers (ScheduleOne.Economy.CustomerData).
        private const float DefMinSpend = 200f, DefMaxSpend = 500f;
        private const int DefMinOrders = 1, DefMaxOrders = 5;
        private const int DefOrderTime = 1200;
        private const float DefMinMutual = 3f, DefMaxMutual = 5f;
        private const float DefPoliceChance = 0.5f, DefAddiction = 0f, DefDependenceMult = 1f;

        private static void CustomerFieldsSection(NpcDraft n)
        {
            Components.SectionHeader(_formContent, "Customer");

            SliderRow("Relation", 0f, 5f, RelFloat(n, "delta", 0f), v => SetRel(n, "delta", Round2(v)));
            Caption("Relationship at the start, 0 to 5.");

            SliderRow("Spend min", 0f, 5000f, RangeEnd(n, "spending", "min", DefMinSpend),
                v => SetRangeEnd(n, "spending", "min", Mathf.Round(v), DefMinSpend, DefMaxSpend), whole: true);
            SliderRow("Spend max", 0f, 5000f, RangeEnd(n, "spending", "max", DefMaxSpend),
                v => SetRangeEnd(n, "spending", "max", Mathf.Round(v), DefMinSpend, DefMaxSpend), whole: true);
            StepperRow("Orders/week min", 0, 7, Mathf.RoundToInt(RangeEnd(n, "ordersPerWeek", "min", DefMinOrders)),
                v => SetRangeEnd(n, "ordersPerWeek", "min", v, DefMinOrders, DefMaxOrders));
            StepperRow("Orders/week max", 0, 7, Mathf.RoundToInt(RangeEnd(n, "ordersPerWeek", "max", DefMaxOrders)),
                v => SetRangeEnd(n, "ordersPerWeek", "max", v, DefMinOrders, DefMaxOrders));

            int day = IndexOfIgnoreCase(DayValues, CustStr(n, "preferredOrderDay"));
            Components.FormRow(_formContent, "Order day", null, out Transform daySlot, stacked: false, height: 30);
            var dayGO = Components.Segmented(daySlot, DayLabels, day < 0 ? 0 : day, i =>
            {
                SetCust(n, "preferredOrderDay", DayValues[i]);
                Core.Log?.Msg($"[editor] '{n.Name}': customer.preferredOrderDay = {DayValues[i]}");
                MarkDirty();
            }, out _);
            StretchFill(dayGO.GetComponent<RectTransform>());

            // Quarter-hour steps; Personnel stores "HH:MM".
            int hhmm = OrderTimeHhmm(n);
            SliderRow("Order time", 0f, 95f, (hhmm / 100) * 4 + (hhmm % 100) / 15,
                v => SetCust(n, "orderTime", QuarterToTime(Mathf.RoundToInt(v))), format: v => QuarterToTime(Mathf.RoundToInt(v)));

            int std = IndexOfIgnoreCase(StandardValues, CustStr(n, "standards"));
            ChoiceRow("Standards", StandardLabels[std < 0 ? 2 : std], "Quality standards", StandardLabels, StandardValues, v =>
            {
                SetCust(n, "standards", v);
                Core.Log?.Msg($"[editor] '{n.Name}': customer.standards = {v}");
            });

            ToggleRow("Direct approach", CustBool(n, "allowDirectApproach", true), v => SetCust(n, "allowDirectApproach", v));
            ToggleRow("First sample works", CustBool(n, "guaranteeFirstSample", false), v => SetCust(n, "guaranteeFirstSample", v));

            // Vanilla Customer.GetSampleRequestSuccessChance: InverseLerp(min, max, average mutual relation).
            Caption("Chance to accept a sample when approached, by the average relation to this customer's contacts:");
            SliderRow("0% at", 0f, 5f, RangeEnd(n, "mutualRelationRequirement", "min", DefMinMutual),
                v => SetRangeEnd(n, "mutualRelationRequirement", "min", Round2(v), DefMinMutual, DefMaxMutual));
            SliderRow("100% at", 0f, 5f, RangeEnd(n, "mutualRelationRequirement", "max", DefMaxMutual),
                v => SetRangeEnd(n, "mutualRelationRequirement", "max", Round2(v), DefMinMutual, DefMaxMutual));

            SliderRow("Police call", 0f, 1f, CustFloat(n, "callPoliceChance", DefPoliceChance), v => SetCust(n, "callPoliceChance", Round2(v)));

            // Personnel ignores the multiplier without a base, so both keys are written together.
            SliderRow("Addiction", 0f, 1f, DependenceValue(n, "base", DefAddiction), v => SetDependence(n, "base", Round2(v)));
            SliderRow("Addict. rate", 0f, 2f, DependenceValue(n, "multiplier", DefDependenceMult), v => SetDependence(n, "multiplier", Round2(v)));

            Components.SectionHeader(_formContent, "Drug taste");
            Caption("-1 dislikes, 1 loves.");
            foreach (var (key, label) in DrugTypes)
            {
                string k = key;
                SliderRow(label, -1f, 1f, AffinityValue(n, k), v => SetAffinity(n, k, Round2(v)));
            }

            PreferredPropertiesSection(n);
        }

        // --- preferred properties ---

        private static void PreferredPropertiesSection(NpcDraft n)
        {
            Components.SectionHeader(_formContent, "Favourite effects");
            List<string> ids = PreferredPropertyIds(n);
            Dictionary<string, string> names = EffectNames();
            foreach (string id in ids)
            {
                string shown = names.TryGetValue(id, out string nm) ? nm : id;
                RemovableRow("eff", shown, !names.ContainsKey(id), () =>
                {
                    ids.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                    WritePreferredProperties(n, ids);
                    Core.Log?.Msg($"[editor] '{n.Name}': favourite effect '{id}' removed ({ids.Count} left).");
                    RefreshForm(); MarkDirty();
                });
            }

            List<PathOption> options = EffectOptions();
            if (options.Count == 0)
            {
                Caption("No effects loaded yet.");
                return;
            }

            var (addGO, addBtn, _) = UIFactory.ButtonWithLabel("addeff", "+ Add effect", _formContent, Theme.Accent, 0, 32);
            addGO.AddComponent<LayoutElement>().minHeight = 32;
            addBtn.onClick.AddListener((UnityAction)(() =>
            {
                var free = options.FindAll(o => !ids.Exists(x => string.Equals(x, o.Path, StringComparison.OrdinalIgnoreCase)));
                OptionPicker.Show(_canvasGO.transform, "Choose effect", free, null, allowNone: false, chosen =>
                {
                    if (string.IsNullOrEmpty(chosen)) return;
                    ids.Add(chosen);
                    WritePreferredProperties(n, ids);
                    Core.Log?.Msg($"[editor] '{n.Name}': favourite effect '{chosen}' added ({ids.Count} now).");
                    RefreshForm(); MarkDirty();
                });
            }));
        }

        private static List<string> PreferredPropertyIds(NpcDraft n)
        {
            var ids = new List<string>();
            if ((n.Customer as JObject)?["preferredProperties"] is JArray arr)
                foreach (var t in arr)
                {
                    string id = t.Type == JTokenType.String ? (string)t : null;
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
                }
            return ids;
        }

        private static void WritePreferredProperties(NpcDraft n, List<string> ids)
        {
            JObject c = CustomerObj(n);
            if (ids.Count > 0) c["preferredProperties"] = new JArray(ids.ToArray());
            else c.Remove("preferredProperties");
        }

        private static List<PathOption> _effectOptions;
        private static Dictionary<string, string> _effectNames;

        // Product effects ship as Resources under Properties/Tier1..5; S1API resolves preferredProperties by Effect.ID
        // against the same folders. Cached once something loaded, retried while the list is still empty.
        private static List<PathOption> EffectOptions()
        {
            if (_effectOptions != null && _effectOptions.Count > 0) return _effectOptions;
            var options = new List<PathOption>();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int tier = 1; tier <= 5; tier++)
            {
                try
                {
                    var effects = Resources.LoadAll<Il2CppScheduleOne.Effects.Effect>("Properties/Tier" + tier);
                    if (effects == null) continue;
                    foreach (var e in effects)
                    {
                        if (e == null || string.IsNullOrEmpty(e.ID) || names.ContainsKey(e.ID)) continue;
                        string display = string.IsNullOrEmpty(e.Name) ? e.ID : e.Name;
                        names[e.ID] = display;
                        options.Add(new PathOption("Tier " + tier, display, e.ID));
                    }
                }
                catch (Exception ex) { Core.Log?.Warning($"[editor] could not load effects from Properties/Tier{tier}: {ex.Message}"); }
            }
            options.Sort((a, b) => string.CompareOrdinal(a.Group + a.Display, b.Group + b.Display));
            if (options.Count == 0) Core.Log?.Warning("[editor] no product effects found under Properties/Tier1..5");
            else Core.Log?.Msg($"[editor] {options.Count} product effects loaded for the favourite-effect picker");
            _effectOptions = options;
            _effectNames = names;
            return options;
        }

        private static Dictionary<string, string> EffectNames()
        {
            if (_effectNames == null || _effectNames.Count == 0) EffectOptions();
            return _effectNames ?? new Dictionary<string, string>();
        }

        // --- rows ---

        // A closed set of values with labels too long for a segmented control: a pill opening the option picker.
        private static void ChoiceRow(string label, string shown, string title, string[] labels, string[] values, Action<string> set)
        {
            Components.FormRow(_formContent, label, null, out Transform slot, stacked: false, height: 30);
            var wrap = new GameObject("choice"); wrap.transform.SetParent(slot, false);
            StretchFill(wrap.AddComponent<RectTransform>());
            var img = wrap.AddComponent<Image>(); img.color = Theme.SurfaceInput; img.sprite = Theme.RoundedSprite(); img.type = Image.Type.Sliced;
            var btn = wrap.AddComponent<Button>(); btn.targetGraphic = img;

            var txt = UIFactory.Text("t", shown, wrap.transform, Theme.Body, TextAnchor.MiddleLeft);
            txt.color = Theme.TextPrimary; txt.raycastTarget = false;
            var trt = txt.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-26, 0);

            var chevron = UIFactory.Text("chev", "▾", wrap.transform, Theme.Caption, TextAnchor.MiddleRight);
            chevron.color = Theme.TextMuted; chevron.raycastTarget = false;
            var crt = chevron.rectTransform; crt.anchorMin = new Vector2(1, 0); crt.anchorMax = Vector2.one; crt.pivot = new Vector2(1, 0.5f); crt.offsetMin = new Vector2(-22, 0); crt.offsetMax = new Vector2(-8, 0);

            var options = new List<PathOption>();
            for (int i = 0; i < values.Length; i++) options.Add(new PathOption(label, labels[i], values[i]));
            int cur = Array.IndexOf(labels, shown);
            btn.onClick.AddListener((UnityAction)(() =>
                OptionPicker.Show(_canvasGO.transform, title, options, cur >= 0 ? values[cur] : null, false, chosen =>
                {
                    if (string.IsNullOrEmpty(chosen)) return;
                    set(chosen);
                    RefreshForm();
                    MarkDirty();
                })));
        }

        // One list entry with a remove button; muted when the stored value is not known to the game.
        private static void RemovableRow(string name, string text, bool muted, Action remove)
        {
            var row = UIFactory.Panel(name, _formContent, Theme.BgElevated);
            var rimg = row.GetComponent<Image>(); if (rimg != null) { rimg.sprite = Theme.RoundedSprite(); rimg.type = Image.Type.Sliced; }
            row.AddComponent<LayoutElement>().minHeight = 36;

            var t = UIFactory.Text("t", text, row.transform, Theme.Body, TextAnchor.MiddleLeft);
            t.raycastTarget = false;
            if (muted) t.color = Theme.TextMuted;
            var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(12, 0); trt.offsetMax = new Vector2(-38, 0);

            var (rmGO, rmBtn, rmTxt) = UIFactory.ButtonWithLabel("rm", "✕", row.transform, Theme.Button, 26, 26);
            if (rmTxt != null) rmTxt.fontSize = Theme.Body;
            var rmrt = rmGO.GetComponent<RectTransform>();
            rmrt.anchorMin = new Vector2(1, 0.5f); rmrt.anchorMax = new Vector2(1, 0.5f); rmrt.pivot = new Vector2(1, 0.5f);
            rmrt.anchoredPosition = new Vector2(-8, 0); rmrt.sizeDelta = new Vector2(26, 26);
            rmBtn.onClick.AddListener((UnityAction)(() => remove()));
        }

        // --- JSON access ---

        private static JObject CustomerObj(NpcDraft n)
        {
            if (n.Customer is JObject c) return c;
            c = new JObject();
            n.Customer = c;
            return c;
        }

        private static JToken CustToken(NpcDraft n, string key) => (n.Customer as JObject)?[key];

        private static string CustStr(NpcDraft n, string key)
        {
            JToken t = CustToken(n, key);
            return t != null && t.Type == JTokenType.String ? (string)t : null;
        }

        private static float CustFloat(NpcDraft n, string key, float fallback) => Num(CustToken(n, key), fallback);

        private static bool CustBool(NpcDraft n, string key, bool fallback)
        {
            JToken t = CustToken(n, key);
            return t != null && t.Type == JTokenType.Boolean ? (bool)t : fallback;
        }

        private static void SetCust(NpcDraft n, string key, JToken value) => CustomerObj(n)[key] = value;

        private static float RangeEnd(NpcDraft n, string key, string end, float fallback) =>
            Num((CustToken(n, key) as JObject)?[end], fallback);

        // Personnel fills a missing end of a range with the other end, so both ends are written together.
        private static void SetRangeEnd(NpcDraft n, string key, string end, float value, float defMin, float defMax)
        {
            JObject c = CustomerObj(n);
            if (!(c[key] is JObject range)) { range = new JObject(); c[key] = range; }
            if (range["min"] == null) range["min"] = defMin;
            if (range["max"] == null) range["max"] = defMax;
            range[end] = value;
        }

        private static float DependenceValue(NpcDraft n, string end, float fallback) =>
            Num((CustToken(n, "dependence") as JObject)?[end], fallback);

        private static void SetDependence(NpcDraft n, string end, float value)
        {
            JObject c = CustomerObj(n);
            if (!(c["dependence"] is JObject dep)) { dep = new JObject(); c["dependence"] = dep; }
            if (dep["base"] == null) dep["base"] = DefAddiction;
            if (dep["multiplier"] == null) dep["multiplier"] = DefDependenceMult;
            dep[end] = value;
        }

        private static float AffinityValue(NpcDraft n, string key)
        {
            if (CustToken(n, "affinities") is JObject aff)
                foreach (var p in aff.Properties())
                    if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) return Num(p.Value, 0f);
            return 0f;
        }

        // Keeps the casing of an existing key so a hand-written manifest does not end up with two entries.
        private static void SetAffinity(NpcDraft n, string key, float value)
        {
            JObject c = CustomerObj(n);
            if (!(c["affinities"] is JObject aff)) { aff = new JObject(); c["affinities"] = aff; }
            foreach (var p in aff.Properties())
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) { p.Value = value; return; }
            aff[key] = value;
        }

        private static float RelFloat(NpcDraft n, string key, float fallback) => Num((n.Relationships as JObject)?[key], fallback);

        private static void SetRel(NpcDraft n, string key, JToken value)
        {
            var rel = n.Relationships as JObject ?? new JObject();
            rel[key] = value;
            n.Relationships = rel;
        }

        private static int OrderTimeHhmm(NpcDraft n)
        {
            JToken t = CustToken(n, "orderTime");
            if (t == null) return DefOrderTime;
            if (t.Type == JTokenType.Integer) return Mathf.Clamp((int)t, 0, 2359);
            string s = t.Type == JTokenType.String ? ((string)t).Trim() : null;
            if (string.IsNullOrEmpty(s)) return DefOrderTime;
            string[] parts = s.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m) && h >= 0 && h < 24 && m >= 0 && m < 60)
                return h * 100 + m;
            if (int.TryParse(s, out int raw) && raw >= 0 && raw <= 2359) return raw;
            Core.Log?.Warning($"[editor] '{n.Name}': customer.orderTime '{s}' is not a time, showing {DefOrderTime / 100:00}:00");
            return DefOrderTime;
        }

        private static string QuarterToTime(int q)
        {
            q = Mathf.Clamp(q, 0, 95);
            return $"{q / 4:00}:{(q % 4) * 15:00}";
        }

        private static float Num(JToken t, float fallback) =>
            t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? (float)t : fallback;

        private static float Round2(float v) => (float)Math.Round(v, 2);

        private static int IndexOfIgnoreCase(string[] values, string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], value.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
