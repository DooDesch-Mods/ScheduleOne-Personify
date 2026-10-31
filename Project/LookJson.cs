using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Personify.Editor;

namespace Personify.Project
{
    /// <summary>
    /// The manifest <c>appearance</c> block and the editor's <see cref="AppearanceDraft"/> in both directions: the
    /// draft is what the 3D preview and the random look work with, the manifest block is what the project stores.
    /// </summary>
    public static class LookJson
    {
        /// <summary>Manifest block from a draft. Hidden layers go to <paramref name="hidden"/> as
        /// <c>{list, index, path|file, tint}</c>, the shape the editor page keeps in <c>personify.hiddenLayers</c>.</summary>
        public static JObject FromDraft(AppearanceDraft a, JArray hidden)
        {
            var app = new JObject
            {
                ["gender"] = a.Gender, ["height"] = a.Height, ["weight"] = a.Weight,
                ["skinColor"] = a.SkinColor, ["hairPath"] = a.HairPath, ["hairColor"] = a.HairColor,
                ["eyebrowScale"] = a.EyebrowScale, ["eyebrowThickness"] = a.EyebrowThickness,
                ["eyebrowRestingHeight"] = a.EyebrowRestingHeight, ["eyebrowRestingAngle"] = a.EyebrowRestingAngle,
                ["leftEyeLidColor"] = a.LeftEyeLidColor, ["rightEyeLidColor"] = a.RightEyeLidColor,
                ["leftEye"] = new JObject { ["top"] = a.LeftEyeTop, ["bottom"] = a.LeftEyeBottom },
                ["rightEye"] = new JObject { ["top"] = a.RightEyeTop, ["bottom"] = a.RightEyeBottom },
                ["eyeballMaterial"] = a.EyeballMaterial, ["eyeBallTint"] = a.EyeBallTint, ["pupilDilation"] = a.PupilDilation,
                ["faceLayers"] = Layers(a.FaceLayers, "face", "tint", hidden, "faceLayers"),
                ["bodyLayers"] = Layers(a.BodyLayers, "body", "tint", hidden, "bodyLayers"),
                ["accessories"] = Layers(a.Accessories, null, "color", hidden, "accessories"),
            };
            if (a.Distortion != null && a.Distortion.Count > 0)
            {
                var dist = new JObject();
                foreach (var kv in a.Distortion)
                    if (kv.Value != null)
                        dist[kv.Key] = new JObject { ["scaleX"] = kv.Value.ScaleX, ["scaleY"] = kv.Value.ScaleY, ["scaleZ"] = kv.Value.ScaleZ, ["hide"] = kv.Value.Hide };
                app["distortion"] = dist;
            }
            return app;
        }

        /// <summary>Draft from a manifest NPC: its appearance block (missing fields keep the draft defaults) plus the
        /// hidden layers of its editor block, so the preview and the layer list see the same stack.</summary>
        public static AppearanceDraft ToDraft(JObject npc)
        {
            var d = new AppearanceDraft();
            var a = npc?["appearance"] as JObject;
            var hiddenToken = npc?[PackProject.EditorKey]?["hiddenLayers"];
            if (a != null)
            {
                d.Gender = F(a, "gender", d.Gender); d.Height = F(a, "height", d.Height); d.Weight = F(a, "weight", d.Weight);
                d.SkinColor = S(a, "skinColor", d.SkinColor); d.HairPath = S(a, "hairPath", d.HairPath); d.HairColor = S(a, "hairColor", d.HairColor);
                d.EyebrowScale = F(a, "eyebrowScale", d.EyebrowScale); d.EyebrowThickness = F(a, "eyebrowThickness", d.EyebrowThickness);
                d.EyebrowRestingHeight = F(a, "eyebrowRestingHeight", d.EyebrowRestingHeight);
                d.EyebrowRestingAngle = F(a, "eyebrowRestingAngle", d.EyebrowRestingAngle);
                d.LeftEyeLidColor = S(a, "leftEyeLidColor", d.LeftEyeLidColor); d.RightEyeLidColor = S(a, "rightEyeLidColor", d.RightEyeLidColor);
                d.LeftEyeTop = F(a["leftEye"] as JObject, "top", d.LeftEyeTop); d.LeftEyeBottom = F(a["leftEye"] as JObject, "bottom", d.LeftEyeBottom);
                d.RightEyeTop = F(a["rightEye"] as JObject, "top", d.RightEyeTop); d.RightEyeBottom = F(a["rightEye"] as JObject, "bottom", d.RightEyeBottom);
                d.EyeballMaterial = S(a, "eyeballMaterial", d.EyeballMaterial); d.EyeBallTint = S(a, "eyeBallTint", d.EyeBallTint);
                d.PupilDilation = F(a, "pupilDilation", d.PupilDilation);
                ReadLayers(a["faceLayers"] as JArray, d.FaceLayers, true);
                ReadLayers(a["bodyLayers"] as JArray, d.BodyLayers, true);
                ReadLayers(a["accessories"] as JArray, d.Accessories, true);
                if (a["distortion"] is JObject dist)
                    foreach (var p in dist.Properties())
                        if (p.Value is JObject b)
                            d.Distortion[p.Name] = new BoneDistortionDraft
                            {
                                ScaleX = F(b, "scaleX", 1f), ScaleY = F(b, "scaleY", 1f), ScaleZ = F(b, "scaleZ", 1f), Hide = (bool?)b["hide"] ?? false,
                            };
            }
            if (hiddenToken is JArray hiddenList)
            {
                // Inserted in index order, so each lands where it sat when it was switched off.
                foreach (var h in hiddenList.OfType<JObject>().OrderBy(h => (int?)h["index"] ?? int.MaxValue))
                {
                    var dst = ListOf(d, (string)h["list"]);
                    if (dst == null) continue;
                    var layer = Layer(h, false);
                    int at = Math.Max(0, Math.Min(dst.Count, (int?)h["index"] ?? dst.Count));
                    dst.Insert(at, layer);
                }
            }
            else if (hiddenToken is JObject hidden)
            {
                ReadLayers(hidden["faceLayers"] as JArray, d.FaceLayers, false);
                ReadLayers(hidden["bodyLayers"] as JArray, d.BodyLayers, false);
                ReadLayers(hidden["accessories"] as JArray, d.Accessories, false);
            }
            var ed = npc?[PackProject.EditorKey];
            if ((bool?)ed?["eyelidsCustom"] == true || (bool?)ed?["eyelidFollowsSkin"] == false) d.EyelidFollowsSkin = false;
            return d;
        }

        private static void ReadLayers(JArray src, List<LayerDraft> dst, bool visible)
        {
            if (src == null) return;
            foreach (var t in src)
                if (t is JObject l) dst.Add(Layer(l, visible));
        }

        private static LayerDraft Layer(JObject l, bool visible)
        {
            string tint = (string)l["tint"] ?? (string)l["color"] ?? "#FFFFFF";
            return new LayerDraft { Path = (string)l["path"] ?? "", Source = (string)l["file"] ?? "", Tint = tint, Visible = visible };
        }

        private static List<LayerDraft> ListOf(AppearanceDraft d, string list)
        {
            switch (list)
            {
                case "faceLayers": return d.FaceLayers;
                case "bodyLayers": return d.BodyLayers;
                case "accessories": return d.Accessories;
                default:
                    ProjectLog.Warn($"[look] hidden layer names an unknown list '{list}' - skipped.");
                    return null;
            }
        }

        // Visible layers go into the manifest; hidden ones into the editor block under the same list name. A custom
        // picture keeps its project path ("sources/x.png"), which is also its path inside the installed pack.
        private static JArray Layers(List<LayerDraft> src, string kind, string colourKey, JArray hidden, string listName)
        {
            var visible = new JArray();
            int index = 0;
            foreach (var l in src ?? new List<LayerDraft>())
            {
                if (l == null) continue;
                JObject lo;
                if (!string.IsNullOrWhiteSpace(l.Source) && kind != null) lo = new JObject { ["file"] = l.Source, ["kind"] = kind, [colourKey] = l.Tint };
                else if (!string.IsNullOrWhiteSpace(l.Path)) lo = new JObject { ["path"] = l.Path, [colourKey] = l.Tint };
                else continue;
                if (l.Visible) visible.Add(lo);
                else if (hidden != null)
                {
                    var h = new JObject { ["list"] = listName, ["index"] = index };
                    if (lo["file"] != null) h["file"] = lo["file"]; else h["path"] = lo["path"];
                    h["tint"] = l.Tint;
                    hidden.Add(h);
                }
                index++;
            }
            return visible;
        }

        private static float F(JObject o, string key, float fallback)
        {
            var t = o?[key];
            return t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? (float)t : fallback;
        }

        private static string S(JObject o, string key, string fallback)
        {
            var t = o?[key];
            return t != null && t.Type == JTokenType.String ? (string)t : fallback;
        }
    }
}
