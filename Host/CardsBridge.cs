using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Cards;
using Personify.Editor;
using Personify.Project;
using UnityEngine;

namespace Personify.Host
{
    /// <summary>
    /// NPC cards in the editor. An export renders the NPC against the height chart and its head for the mugshot,
    /// draws the card picture (<see cref="CardArt"/>) and hands picture and NPC to Personnel
    /// (<c>Personnel.Cards.CardsApi</c>), which hides the NPC in the pixels. The card list, the import of card files and
    /// the NPC Vault calls go through Personnel too. Renders borrow the preview character like face renders do, so an
    /// export waits for the face queue and runs over a few frames from <see cref="Tick"/>; its result arrives as the
    /// page event "cards.exported". Vault answers arrive as "vault.reply" with the id the page sent.
    /// </summary>
    internal static class CardsBridge
    {
        private const string Api = "Personnel.Cards.CardsApi";
        /// <summary>RuntimePreviewGeneration, the layer the game's icon rig draws; nothing else in the menu uses it.</summary>
        private const int RigLayer = 20;
        private const int SettleFrames = 4;
        private const int HeadSize = 292;
        /// <summary>Renders are drawn this many times larger and reduced, which smooths the edges.</summary>
        private const int Supersample = 2;

        internal static string ExportDir => Path.Combine(Paths.Root, "Cards");
        private static string IndexFile => Path.Combine(ExportDir, "index.json");

        private sealed class Job { public PackProject Project; public string NpcId; public int Frames; }
        private static Job _job;
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly HashSet<string> Thumbs = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True while an export holds the preview character.</summary>
        internal static bool Busy => _job != null;

        /// <summary>The NPC whose card is being made, or null.</summary>
        internal static JObject CurrentNpc => _job?.Project.FindNpc(_job.NpcId);

        // ---- export ------------------------------------------------------------------------------------------------

        internal static JObject StartExport(PackProject p, string npcId)
        {
            if (_job != null) { Core.Log.Warning($"[cards] export of '{npcId}' refused: '{_job.NpcId}' is still exporting."); return Calls.Fail("card_busy"); }
            if (p.FindNpc(npcId) == null) { Core.Log.Warning($"[cards] export refused: no NPC '{npcId}' in '{p.Folder}'."); return Calls.Fail("npc_not_found"); }
            if (Method("WriteCard") == null) return Calls.Fail("personnel_missing");
            _job = new Job { Project = p, NpcId = npcId };
            Core.Log.Msg($"[cards] exporting '{npcId}' from '{p.Folder}'.");
            return new JObject { ["ok"] = true, ["pending"] = true };
        }

        /// <summary>Called every frame while the editor is open.</summary>
        internal static void Tick()
        {
            while (MainThread.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Core.Log.Error($"[cards] a vault answer failed on the main thread ({e.GetType().Name}: {e.Message})."); }
            }

            if (_job == null || Faces.Rendering) return;
            var j = _job;
            var npc = j.Project.FindNpc(j.NpcId);
            if (npc == null) { Core.Log.Warning($"[cards] '{j.NpcId}' was removed while exporting."); Done(j, Calls.Fail("npc_not_found")); return; }
            if (j.Frames == 0 && !LookBridge.Apply(j.Project, npc))
            {
                Core.Log.Warning($"[cards] '{j.NpcId}': the preview character is not available.");
                Done(j, Calls.Fail("preview_unavailable"));
                return;
            }
            // Hair and accessories of the previous look are destroyed only at the end of the frame the new one lands.
            if (++j.Frames < SettleFrames) return;

            JObject result;
            try { result = Export(j.Project, npc); }
            catch (Exception e)
            {
                Core.Log.Error($"[cards] exporting '{j.NpcId}' failed: {e}");
                result = Calls.Fail("card_failed");
                result["text"] = (e as TargetInvocationException)?.InnerException?.Message ?? e.Message;
            }
            Done(j, result);
        }

        private static void Done(Job j, JObject result)
        {
            _job = null;
            result["npcId"] = j.NpcId;
            EditorHost.Emit("cards.exported", result);
            EditorHost.ReapplyPreview();
        }

        private static JObject Export(PackProject p, JObject npc)
        {
            string id = (string)npc["id"];
            var entry = (p.InstallManifest()["npcs"] as JArray)?.OfType<JObject>().FirstOrDefault(o => (string)o["id"] == id);
            if (entry == null) { Core.Log.Warning($"[cards] '{id}' is not in the install manifest."); return Calls.Fail("npc_not_found"); }
            entry = (JObject)entry.DeepClone();

            // What the card carries: the NPC, its contact picture and its custom layer pictures, under card names.
            var layers = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            CollectLayers(p, entry["appearance"]?["faceLayers"] as JArray, layers, id);
            CollectLayers(p, entry["appearance"]?["bodyLayers"] as JArray, layers, id);
            if (entry["relationships"] is JObject rel && rel.Remove("connections"))
                Core.Log.Msg($"[cards] '{id}': relationship connections left out (a card carries one NPC).");
            if (entry["extensions"] is JObject ext) ext.Remove("card");

            byte[] portrait = null;
            string portraitRel = (string)entry["portrait"];
            if (!string.IsNullOrWhiteSpace(portraitRel)) portrait = ReadProjectFile(p, portraitRel, id, "contact picture");
            if (portrait != null) entry["portrait"] = "portrait.png";
            else entry.Remove("portrait");

            // The code is kept while the NPC stays the same; the key leaves out a rendered stand-in portrait, which
            // differs slightly from render to render.
            string lookHash = (string)Invoke("ContentHash", entry.ToString(Formatting.None), portrait, layers);
            var index = LoadIndex();
            string key = p.Folder + "/" + id;
            var known = index[key] as JObject;
            string code = known != null && (string)known["hash"] == lookHash && !string.IsNullOrEmpty((string)known["code"])
                ? (string)known["code"] : (string)Invoke("NewCode");

            byte[] body = RenderBody(id);
            byte[] head = RenderHead(id);
            if (portrait == null)
            {
                portrait = LookBridge.RenderHead(p, npc, 256, "cards", false);
                if (portrait != null) entry["portrait"] = "portrait.png";
            }
            string npcJson = entry.ToString(Formatting.None);
            string contentHash = (string)Invoke("ContentHash", npcJson, portrait, layers);

            // A remix names the card it came from, unless nothing changed.
            var remix = npc[PackProject.EditorKey]?["remixOf"] as JObject;
            string basedOn = remix != null && (string)remix["contentHash"] != contentHash ? (string)remix["code"] : null;
            string creator = Creator(p);
            JObject credit = Credit(p, npc);
            var meta = new JObject
            {
                ["cardId"] = code,
                ["creator"] = creator,
                ["createdAt"] = DateTime.UtcNow.ToString("o"),
                ["editor"] = "Personify " + DooDesch.ModVersion.Current,
                ["basedOn"] = basedOn == null ? null : new JObject { ["code"] = basedOn, ["name"] = (string)remix["name"] },
            };
            if (credit != null) meta["credit"] = credit;

            string name = (string)entry["name"] ?? id;
            string role = RoleOf(entry);
            byte[] art = CardArt.Compose(new CardArtInput
            {
                Name = name, Role = role, Code = code, Creator = creator, BasedOn = basedOn,
                OriginalBy = credit == null ? null : CreditLine(credit),
                Body = body, Head = head, HeadSize = head == null ? 0 : HeadSize,
            });
            byte[] png = (byte[])Invoke("WriteCard", art, npcJson, meta.ToString(Formatting.None), portrait, layers);

            Directory.CreateDirectory(ExportDir);
            string file = Path.Combine(ExportDir, Paths.Sanitize(name).Replace(' ', '-') + "-" + code + ".png");
            if (known != null && (string)known["file"] is string old && old != Path.GetFileName(file))
            {
                string oldPath = Path.Combine(ExportDir, old);
                if (File.Exists(oldPath)) { File.Delete(oldPath); Core.Log.Msg($"[cards] '{id}': older card file {old} replaced."); }
            }
            File.WriteAllBytes(file, png);
            index[key] = new JObject { ["code"] = code, ["hash"] = lookHash, ["file"] = Path.GetFileName(file), ["name"] = name, ["role"] = role };
            SaveIndex(index);

            string url = "s1://card-" + code + "-" + contentHash.Substring(0, 8);
            EditorHost.PublishImage(url.Substring(5), png);
            Core.Log.Msg($"[cards] '{id}' exported as card {code} to {file} ({png.Length / 1024} KB, {layers.Count} layer picture(s), body {(body != null ? "rendered" : "missing")}, head {(head != null ? "rendered" : "missing")}).");
            return new JObject
            {
                ["ok"] = true, ["code"] = code, ["name"] = name, ["role"] = role, ["path"] = file, ["file"] = Path.GetFileName(file),
                ["url"] = url, ["sizeKb"] = png.Length / 1024, ["basedOn"] = basedOn,
            };
        }

        private static void CollectLayers(PackProject p, JArray list, Dictionary<string, byte[]> into, string id)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!(list[i] is JObject layer) || !(layer["file"] is JValue v) || string.IsNullOrWhiteSpace((string)v)) continue;
                string rel = (string)v;
                byte[] data = ReadProjectFile(p, rel, id, "layer picture");
                if (data == null) { list.RemoveAt(i); continue; }
                string cardName = SafeLayerName(Path.GetFileNameWithoutExtension(rel), into);
                into[cardName] = data;
                layer["file"] = "layers/" + cardName;
            }
        }

        // Personnel accepts letters, digits, '_', '-' and '.', ending in ".png", at most 96 characters.
        private static string SafeLayerName(string stem, Dictionary<string, byte[]> taken)
        {
            var sb = new StringBuilder();
            foreach (char c in stem ?? "") sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '-') ? c : '_');
            string b = sb.ToString().TrimStart('_', '-');
            if (b.Length == 0) b = "layer";
            if (b.Length > 80) b = b.Substring(0, 80);
            string name = b + ".png";
            for (int n = 2; taken.ContainsKey(name); n++) name = b + "-" + n + ".png";
            return name;
        }

        private static byte[] ReadProjectFile(PackProject p, string rel, string id, string what)
        {
            try
            {
                string abs = Path.GetFullPath(Path.Combine(p.Dir, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!abs.StartsWith(Path.GetFullPath(p.Dir), StringComparison.OrdinalIgnoreCase))
                {
                    Core.Log.Warning($"[cards] '{id}': {what} '{rel}' is outside the project - left off the card.");
                    return null;
                }
                if (!File.Exists(abs)) { Core.Log.Warning($"[cards] '{id}': {what} '{rel}' is missing - left off the card."); return null; }
                return File.ReadAllBytes(abs);
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[cards] '{id}': reading {what} '{rel}' failed ({e.Message}) - left off the card.");
                return null;
            }
        }

        private static string RoleOf(JObject npc)
        {
            if (npc["dealer"] is JObject) return "dealer";
            if (npc["supplier"] is JObject) return "supplier";
            if (npc["customer"] is JObject) return "customer";
            return "townsfolk";
        }

        /// <summary>
        /// Who made the NPC this card is based on, for NPCs converted from another mod: <c>{ author, source, url, license }</c>
        /// from the NPC's editor block, else from the project's. Null when there is none or it has no author.
        /// </summary>
        private static JObject Credit(PackProject p, JObject npc)
        {
            var credit = (npc[PackProject.EditorKey]?["credit"] as JObject) ?? (p.Editor["credit"] as JObject);
            if (credit == null) return null;
            string author = ((string)credit["author"] ?? "").Trim();
            if (author.Length == 0)
            {
                Core.Log.Warning($"[cards] '{(string)npc["id"]}': the credit block has no author; the card carries no credit.");
                return null;
            }
            var clean = new JObject { ["author"] = author };
            foreach (string key in new[] { "source", "url", "license" })
                if (((string)credit[key] ?? "").Trim() is string v && v.Length > 0) clean[key] = v;
            return clean;
        }

        /// <summary>"FannsoNetti, More NPCs": the author, then the source mod if there is one.</summary>
        private static string CreditLine(JObject credit)
        {
            string source = (string)credit["source"];
            return string.IsNullOrEmpty(source) ? (string)credit["author"] : (string)credit["author"] + ", " + source;
        }

        private static string Creator(PackProject p)
        {
            string author = ((string)p.Manifest["author"] ?? "").Trim();
            if (author.Length > 0) return author;
            try { return (string)JObject.Parse((string)Invoke("VaultState"))["userName"]; }
            catch (Exception e) { Core.Log.Warning($"[cards] reading the vault user for the creator line failed: {e.Message}"); return null; }
        }

        // ---- renders -----------------------------------------------------------------------------------------------

        /// <summary>The whole NPC filling the height chart, feet on the bottom row, transparent around it.</summary>
        private static byte[] RenderBody(string id)
        {
            var root = Preview.AvatarRoot();
            if (root == null) { Core.Log.Warning($"[cards] '{id}': no preview character for the body render."); return null; }
            float half = CardArt.ChartMeters / 2f;
            // The character's root stands on the ground, so the chart starts there. Skinned mesh bounds are no help:
            // they are far larger than the body.
            Vector3 feet = root.position;
            var target = feet + Vector3.up * half;
            var head = Preview.HeadTransform();
            Core.Log.Msg($"[cards] '{id}': feet at {feet.y:0.00}, head bone {(head != null ? (head.position.y - feet.y).ToString("0.00") + " m above" : "missing")}.");
            return Shoot(root, target, Preview.FacingDirection(), true, half, 3f, CardArt.ChartWidth, CardArt.ChartHeight, id, "body");
        }

        /// <summary>The head for the mugshot, transparent around it.</summary>
        private static byte[] RenderHead(string id)
        {
            var root = Preview.AvatarRoot();
            var head = Preview.HeadTransform();
            if (root == null || head == null) { Core.Log.Warning($"[cards] '{id}': no head bone for the mugshot."); return null; }
            return Shoot(root, head.position + Vector3.up * 0.05f, Preview.FacingDirection(), false, 22f, 1.15f, HeadSize, HeadSize, id, "head");
        }

        /// <summary>
        /// Stops a running blink and puts every eye on the NPC's own resting lids. The game blinks the preview character
        /// every few seconds, and a frame taken mid-blink shows the NPC with its eyes shut. The render runs in the same
        /// frame, so the next blink cannot start before it.
        /// </summary>
        private static void RestEyes(Transform root, string id, string what)
        {
            var eyes = root.GetComponentsInChildren<Il2CppScheduleOne.AvatarFramework.Eye>(true);
            if (eyes.Length == 0) { Core.Log.Warning($"[cards] '{id}': no eyes on the preview character, the {what} shows the lids as they are."); return; }
            int blinking = 0;
            foreach (var eye in eyes)
            {
                if (eye._isBlinking) blinking++;
                eye.StopEyeCoroutines();
                eye.SetEyelidPosition(eye._eyelidRestingPosition);
            }
            Core.Log.Msg($"[cards] '{id}': {eyes.Length} eyes on resting lids for the {what}, {blinking} stopped mid-blink.");
        }

        /// <summary>
        /// Renders only the preview character (moved onto <see cref="RigLayer"/> for the frame) against a clear
        /// background, <see cref="Supersample"/> times larger, and returns RGBA rows top to bottom at the asked size.
        /// The shot brings its own flat ambient and two directional lights: the editor scene is dark, and under its
        /// light the skin and clothes come out grey.
        /// </summary>
        private static byte[] Shoot(Transform root, Vector3 target, Vector3 facing, bool ortho, float sizeOrFov, float distance,
            int width, int height, string id, string what)
        {
            var layers = new List<(GameObject go, int layer)>();
            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            int w = width * Supersample, h = height * Supersample;
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            try
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.64f);
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    layers.Add((t.gameObject, t.gameObject.layer));
                    t.gameObject.layer = RigLayer;
                }

                camGo = new GameObject("Personify_CardCam");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << RigLayer;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.orthographic = ortho;
                if (ortho) cam.orthographicSize = sizeOrFov; else cam.fieldOfView = sizeOrFov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = distance + 3f;
                cam.transform.position = target + facing * distance;
                cam.transform.rotation = Quaternion.LookRotation(-facing, Vector3.up);
                // Key light from the upper left, a dimmer cool fill from the right so the shaded side keeps detail.
                AddLight(camGo, facing, new Vector3(-0.55f, 0.45f, 0.15f), 1.0f, Color.white);
                AddLight(camGo, facing, new Vector3(0.7f, 0.15f, -0.5f), 0.4f, new Color(0.8f, 0.86f, 1f));

                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;
                cam.aspect = (float)w / h;
                RestEyes(root, id, what);
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                byte[] raw = tex.GetRawTextureData();
                if (raw == null || raw.Length != w * h * 4) { Core.Log.Warning($"[cards] '{id}': the {what} render came back with {raw?.Length ?? 0} bytes."); return null; }
                return Reduce(raw, w, h, Supersample);
            }
            catch (Exception e)
            {
                Core.Log.Error($"[cards] '{id}': rendering the {what} failed ({e.GetType().Name}: {e.Message}).");
                return null;
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                foreach (var (go, layer) in layers)
                {
                    try { if (go != null) go.layer = layer; }
                    catch (Exception e) { Core.Log.Error($"[cards] putting a preview object back on layer {layer} failed: {e.Message}"); }
                }
                // Destroy waits for the end of the frame; switching the rig off first keeps its lights out of the
                // scene's own render this frame.
                if (camGo != null) { camGo.SetActive(false); UnityEngine.Object.Destroy(camGo); }
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        private static void AddLight(GameObject parent, Vector3 view, Vector3 offset, float intensity, Color color)
        {
            var go = new GameObject("Personify_CardLight");
            go.transform.SetParent(parent.transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.LookRotation(-(view + offset).normalized, Vector3.up);
        }

        /// <summary>
        /// Averages every <paramref name="f"/> x <paramref name="f"/> block with alpha as the weight (the cleared
        /// background is black, so a plain mean darkens the edges) and turns the bottom-up rows top-down.
        /// </summary>
        private static byte[] Reduce(byte[] src, int w, int h, int f)
        {
            int ow = w / f, oh = h / f, n = f * f;
            var dst = new byte[ow * oh * 4];
            for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < f; j++)
                {
                    int row = ((y * f + j) * w + x * f) * 4;
                    for (int i = 0; i < f; i++)
                    {
                        int k = row + i * 4, pa = src[k + 3];
                        r += src[k] * pa; g += src[k + 1] * pa; b += src[k + 2] * pa; a += pa;
                    }
                }
                int o = ((oh - 1 - y) * ow + x) * 4;
                if (a == 0) continue;
                dst[o] = (byte)(r / a); dst[o + 1] = (byte)(g / a); dst[o + 2] = (byte)(b / a); dst[o + 3] = (byte)((a + n / 2) / n);
            }
            return dst;
        }

        // ---- exported and installed cards --------------------------------------------------------------------------

        /// <summary>Cards this editor wrote, newest first, with a picture for each.</summary>
        internal static JArray Exports()
        {
            var list = new JArray();
            if (!Directory.Exists(ExportDir)) return list;
            var index = LoadIndex();
            var byFile = index.Properties().Select(pr => pr.Value as JObject).Where(o => o != null)
                .GroupBy(o => (string)o["file"] ?? "").ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var fi in new DirectoryInfo(ExportDir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                byFile.TryGetValue(fi.Name, out var info);
                string code = (string)info?["code"];
                list.Add(new JObject
                {
                    ["file"] = fi.Name, ["path"] = fi.FullName, ["code"] = code, ["name"] = (string)info?["name"] ?? Path.GetFileNameWithoutExtension(fi.Name),
                    ["role"] = (string)info?["role"], ["sizeKb"] = fi.Length / 1024, ["writtenAt"] = fi.LastWriteTimeUtc.ToString("o"),
                    ["url"] = PublishFile(fi),
                });
            }
            return list;
        }

        private static string PublishFile(FileInfo fi)
        {
            string name = "cardfile-" + Fnv(fi.FullName + "|" + fi.LastWriteTimeUtc.Ticks);
            if (Thumbs.Contains(name)) return "s1://" + name;
            try { EditorHost.PublishImage(name, File.ReadAllBytes(fi.FullName)); Thumbs.Add(name); return "s1://" + name; }
            catch (Exception e) { Core.Log.Warning($"[cards] showing {fi.Name} failed: {e.Message}"); return null; }
        }

        internal static JToken Installed() => Parse((string)Invoke("List"), "installed cards");

        internal static JToken Remove(string code)
        {
            bool ok = (bool)Invoke("Remove", code);
            Core.Log.Msg($"[cards] removing installed card {code}: {(ok ? "done" : "no such card")}.");
            return new JObject { ["ok"] = ok, ["pendingRestart"] = (bool)Invoke("PendingRestart") };
        }

        /// <summary>Installs a card file. Only files in the export folder or Personnel's import folder are taken.</summary>
        internal static JToken ImportFile(string path)
        {
            string full = Path.GetFullPath(path ?? "");
            string importDir = (string)Invoke("ImportFolder");
            bool allowed = full.StartsWith(Path.GetFullPath(ExportDir), StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(importDir) && full.StartsWith(Path.GetFullPath(importDir), StringComparison.OrdinalIgnoreCase));
            if (!allowed) { Core.Log.Warning($"[cards] import of {full} refused: not in a card folder."); return Calls.Fail("not_allowed"); }
            return Parse((string)Invoke("ImportFile", full), "card import");
        }

        internal static JObject Folders() => new JObject
        {
            ["exports"] = ExportDir, ["import"] = (string)Invoke("ImportFolder"), ["pendingRestart"] = (bool)Invoke("PendingRestart"),
        };

        internal static JObject OpenFolder(string which)
        {
            string dir = which == "import" ? (string)Invoke("ImportFolder") : ExportDir;
            if (string.IsNullOrEmpty(dir)) return Calls.Fail("no_folder");
            Directory.CreateDirectory(dir);
            Application.OpenURL(new Uri(dir).AbsoluteUri);
            Core.Log.Msg($"[cards] opened {dir} in the file browser.");
            return new JObject { ["ok"] = true, ["path"] = dir };
        }

        /// <summary>
        /// Starts a new project from a card file: the NPC with its pictures, marked as a remix of that card, so its
        /// next export names the card it came from. Answers <c>{ ok, folder }</c>.
        /// </summary>
        internal static JObject Remix(string path)
        {
            byte[] png;
            try { png = File.ReadAllBytes(path); }
            catch (Exception e) { Core.Log.Warning($"[cards] remix: reading {path} failed: {e.Message}"); return Calls.Fail("read_failed"); }
            var card = Parse((string)Invoke("ReadCard", png), "card read") as JObject;
            if (card == null || (bool?)card["ok"] != true)
            {
                Core.Log.Warning($"[cards] remix: {path} is not a card ({(string)card?["message"]}).");
                var f = Calls.Fail("not_a_card"); f["text"] = (string)card?["message"]; return f;
            }
            var npc = (JObject)card["npc"];
            var meta = card["meta"] as JObject;
            string name = (string)npc["name"] ?? "Card NPC";
            var p = ProjectFiles.Create(name, null);
            if (p == null) return Calls.Fail("create_failed");
            string id = (string)npc["id"] ?? "npc";

            if (card["portrait"] is JValue pv && (string)pv is string pb && pb.Length > 0)
            {
                Write(p, "portraits/" + id + ".png", Convert.FromBase64String(pb));
                npc["portrait"] = "portraits/" + id + ".png";
            }
            else npc.Remove("portrait");
            if (card["layers"] is JObject layers)
                foreach (var pr in layers.Properties())
                    Write(p, "sources/" + pr.Name, Convert.FromBase64String((string)pr.Value));
            foreach (string list in new[] { "faceLayers", "bodyLayers" })
                if (npc["appearance"]?[list] is JArray arr)
                    foreach (var l in arr.OfType<JObject>())
                        if ((string)l["file"] is string file && file.StartsWith("layers/", StringComparison.Ordinal))
                            l["file"] = "sources/" + file.Substring(7);

            var block = PackProject.EditorBlock(npc);
            block["remixOf"] = new JObject { ["code"] = (string)meta?["cardId"], ["contentHash"] = (string)card["contentHash"], ["name"] = name };
            // A remix keeps crediting whoever made the original NPC.
            if (meta?["credit"] is JObject credit) block["credit"] = credit.DeepClone();
            ((JArray)p.Manifest["npcs"]).Add(npc);
            if (!ProjectFiles.Save(p, snapshot: false)) return Calls.Fail("create_failed");
            Core.Log.Msg($"[cards] remix of card {(string)meta?["cardId"]} ('{name}') started as project '{p.Folder}'.");
            return new JObject { ["ok"] = true, ["folder"] = p.Folder };
        }

        private static void Write(PackProject p, string rel, byte[] data)
        {
            string abs = Path.Combine(p.Dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, data);
        }

        // ---- NPC Vault ---------------------------------------------------------------------------------------------

        internal static JToken VaultState() => Parse((string)Invoke("VaultState"), "vault state");

        internal static JObject VaultConnect()
        {
            string handledCode = null;
            Invoke("VaultConnect", new Action<string>(s =>
            {
                var state = Parse(s, "vault connect");
                // The dialog says the code is copied and the page open, so do both once per code.
                if (state is JObject o && (string)o["state"] == "waiting" && (string)o["userCode"] is string code && code != handledCode)
                {
                    handledCode = code;
                    OpenVerifyPage(code, (string)o["verifyUrl"]);
                }
                EditorHost.Emit("vault.connect", state);
            }));
            return new JObject { ["ok"] = true, ["pending"] = true };
        }

        private static void OpenVerifyPage(string code, string url)
        {
            try { GUIUtility.systemCopyBuffer = code; Core.Log.Msg($"[cards] vault code {code} copied to the clipboard."); }
            catch (Exception e) { Core.Log.Warning($"[cards] vault code not copied ({e.Message}); the dialog shows it."); }
            if (string.IsNullOrEmpty(url)) { Core.Log.Warning("[cards] vault connect has no verify address to open."); return; }
            try { Application.OpenURL(url); Core.Log.Msg($"[cards] opened {url} in the browser."); }
            catch (Exception e) { Core.Log.Warning($"[cards] opening {url} failed ({e.Message}); the dialog shows the address."); }
        }

        internal static JObject VaultDisconnect()
        {
            Invoke("VaultDisconnect", new Action<bool>(ok => EditorHost.Emit("vault.state", VaultState())));
            return new JObject { ["ok"] = true, ["pending"] = true };
        }

        /// <summary>GET on the vault API (path starts with /api/v1/); the answer is "vault.reply" { id, ok, body }.</summary>
        internal static JObject VaultGet(string reqId, string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/api/v1/", StringComparison.Ordinal)) { Core.Log.Warning($"[cards] vault path '{path}' refused."); return Calls.Fail("bad_path"); }
            Invoke("VaultGet", path, new Action<bool, string>((ok, body) =>
            {
                JToken parsed = body;
                if (ok) { try { parsed = JToken.Parse(body); } catch (JsonException e) { Core.Log.Warning($"[cards] vault answer for {path} is not JSON: {e.Message}"); } }
                EditorHost.Emit("vault.reply", new JObject { ["id"] = reqId, ["ok"] = ok, ["body"] = parsed });
            }));
            return new JObject { ["ok"] = true, ["pending"] = true };
        }

        internal static JObject VaultInstall(string reqId, string code)
        {
            Invoke("VaultInstall", code, new Action<string>(r => EditorHost.Emit("vault.reply", new JObject { ["id"] = reqId, ["ok"] = true, ["body"] = Parse(r, "vault install") })));
            return new JObject { ["ok"] = true, ["pending"] = true };
        }

        /// <summary>Publishes an exported card file; the answer is "vault.reply" with Personnel's upload result.</summary>
        internal static JObject VaultUpload(string reqId, string file)
        {
            string full = Path.GetFullPath(Path.Combine(ExportDir, Path.GetFileName(file ?? "")));
            if (!File.Exists(full)) { Core.Log.Warning($"[cards] upload refused: {full} does not exist."); return Calls.Fail("not_found"); }
            byte[] png = File.ReadAllBytes(full);
            Invoke("VaultUpload", png, new Action<string>(r => EditorHost.Emit("vault.reply", new JObject { ["id"] = reqId, ["ok"] = true, ["body"] = Parse(r, "vault upload") })));
            Core.Log.Msg($"[cards] uploading {Path.GetFileName(full)} ({png.Length / 1024} KB) to the vault.");
            return new JObject { ["ok"] = true, ["pending"] = true };
        }

        /// <summary>
        /// Loads a vault card's small picture (public, no sign-in) and shows it to the page as s1://vault-&lt;code&gt;;
        /// the answer is "vault.thumb" { code, url } or { code, url: null } after a logged failure.
        /// </summary>
        internal static JObject VaultThumb(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 16 || !code.All(char.IsLetterOrDigit)) return Calls.Fail("bad_code");
            string name = "vault-" + code;
            if (Thumbs.Contains(name)) return new JObject { ["ok"] = true, ["url"] = "s1://" + name };
            string baseUrl = (string)(VaultState() as JObject)?["baseUrl"];
            if (string.IsNullOrEmpty(baseUrl)) return Calls.Fail("no_vault");
            string url = baseUrl.TrimEnd('/') + "/c/" + code + "/thumb.png";
            Task.Run(async () =>
            {
                byte[] png = null;
                string error = null;
                try
                {
                    using (var res = await Http.GetAsync(url).ConfigureAwait(false))
                    {
                        if (res.IsSuccessStatusCode) png = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        else error = "HTTP " + (int)res.StatusCode;
                    }
                }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                MainThread.Enqueue(() =>
                {
                    if (png == null) { Core.Log.Warning($"[cards] vault picture {url} failed ({error})."); EditorHost.Emit("vault.thumb", new JObject { ["code"] = code, ["url"] = null }); return; }
                    EditorHost.PublishImage(name, png);
                    Thumbs.Add(name);
                    EditorHost.Emit("vault.thumb", new JObject { ["code"] = code, ["url"] = "s1://" + name });
                });
            });
            return new JObject { ["ok"] = true, ["pending"] = true };
        }

        // ---- helpers -----------------------------------------------------------------------------------------------

        private static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        private static MethodInfo Method(string name)
        {
            if (Methods.TryGetValue(name, out var m) && m != null) return m;
            m = GameData.PersonnelMethod(Api, name);
            Methods[name] = m;
            return m;
        }

        /// <summary>Calls a CardsApi method; throws when Personnel is missing or too old (the call handler logs it).</summary>
        private static object Invoke(string name, params object[] args)
        {
            var m = Method(name) ?? throw new InvalidOperationException($"Personnel 2.4 or newer is needed for cards ({name} is missing).");
            return m.Invoke(null, args);
        }

        private static JToken Parse(string json, string what)
        {
            if (string.IsNullOrEmpty(json)) { Core.Log.Warning($"[cards] {what}: Personnel answered nothing."); return new JObject { ["ok"] = false, ["error"] = "empty" }; }
            try { return JToken.Parse(json); }
            catch (JsonException e) { Core.Log.Error($"[cards] {what}: Personnel's answer is not JSON ({e.Message})."); return new JObject { ["ok"] = false, ["error"] = "bad_answer" }; }
        }

        private static JObject LoadIndex()
        {
            try { return File.Exists(IndexFile) ? JObject.Parse(File.ReadAllText(IndexFile)) : new JObject(); }
            catch (Exception e) { Core.Log.Warning($"[cards] {IndexFile} is unreadable ({e.Message}); card codes start fresh."); return new JObject(); }
        }

        private static void SaveIndex(JObject index)
        {
            Directory.CreateDirectory(ExportDir);
            ProjectFiles.WriteAtomic(IndexFile, index.ToString(Formatting.Indented));
        }

        private static string Fnv(string s)
        {
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619; }
            return h.ToString("x8");
        }
    }
}
