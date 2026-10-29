using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Personify.Editor;
using Personify.Project;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Personify.Host
{
    /// <summary>The 3D preview, the look catalog, the random look and the contact picture for format 2 NPCs.</summary>
    internal static class LookBridge
    {
        // The preview resolves custom pictures through the v1 project shape; the folder is all it reads.
        private static NpcProject Shell(PackProject p) => new NpcProject { Name = p.Name, FolderName = p.Folder };

        internal static bool Apply(PackProject p, JObject npc)
        {
            var draft = new NpcDraft { Id = (string)npc["id"], Name = (string)npc["name"], Appearance = LookJson.ToDraft(npc) };
            return Preview.Apply(Shell(p), draft);
        }

        internal static void ReassertDistortion(JObject npc)
        {
            if (!(npc["appearance"]?["distortion"] is JObject)) return;
            Preview.ReassertDistortion(new NpcDraft { Appearance = LookJson.ToDraft(npc) });
        }

        internal static JObject Catalog()
        {
            JArray List(IEnumerable<PathOption> opts) => new JArray(opts.Select(o => new JObject
            {
                ["path"] = o.Path, ["name"] = o.Display, ["group"] = o.Group ?? "", ["tint"] = PathCatalog.DefaultTint(o.Path) ?? "#FFFFFF",
            }));
            // "group" tells the page which list a tattoo goes into; "category" is what the picker shows.
            var tattoos = new JArray(PathCatalog.Tattoos().Select(o => new JObject
            {
                ["path"] = o.Path, ["name"] = o.Display, ["category"] = o.Group ?? "",
                ["group"] = (o.Path ?? "").IndexOf("/Face/", StringComparison.OrdinalIgnoreCase) >= 0 ? "face" : "body",
            }));
            foreach (var ink in InkorporatedCatalog.All())
                tattoos.Add(new JObject { ["file"] = ink.PngPath, ["name"] = ink.Name, ["category"] = "Inkorporated: " + ink.Pack, ["group"] = ink.IsFace ? "face" : "body" });
            return new JObject
            {
                ["hair"] = List(PathCatalog.HairStyles()),
                ["tops"] = List(PathCatalog.Tops()),
                ["bottoms"] = List(PathCatalog.Bottoms()),
                ["shoes"] = List(PathCatalog.Shoes()),
                ["headwear"] = List(PathCatalog.Headwear()),
                ["eyewear"] = List(PathCatalog.Eyewear()),
                ["mouths"] = List(PathCatalog.Mouths()),
                ["facialHair"] = List(PathCatalog.FacialHairStyles()),
                ["facialDetails"] = List(PathCatalog.FacialDetails()),
                ["tattoos"] = tattoos,
                ["faceLayers"] = List(PathCatalog.FaceLayers()),
                ["bodyLayers"] = List(PathCatalog.BodyLayers()),
                ["accessories"] = List(PathCatalog.Accessories()),
                ["eyeballMaterials"] = new JArray("Default", "Reptile", "Zombie"),
                ["templates"] = new JArray(new JObject { ["id"] = "menu", ["name"] = "Menu character" }),
            };
        }

        /// <summary>The menu character's look as a manifest block (the "menu character" start look).</summary>
        internal static JObject MenuLook() => LookJson.FromDraft(Preview.SeedFromMenu(), null);

        /// <summary>A start look by template id (see "templates" in the catalog); null for an unknown id.</summary>
        internal static JObject Template(string id) => id == "menu" ? MenuLook() : null;

        private static readonly string[] Skins = { "#F2D6C2", "#E0B897", "#C89A74", "#A57650", "#8A5A3B", "#6B4630", "#4A3022" };
        private static readonly string[] Hairs = { "#101014", "#2E1E14", "#5A3A22", "#8C6239", "#C9A26B", "#E8D6A0", "#9A9A9A", "#B03A2E" };

        /// <summary>A random but plausible look: body, skin, hair, mouth, top, bottom, shoes, sometimes a cap or glasses.</summary>
        internal static JObject RandomLook()
        {
            string Pick(List<PathOption> l) => l == null || l.Count == 0 ? null : l[Random.Range(0, l.Count)].Path;
            string PickS(string[] l) => l[Random.Range(0, l.Length)];
            var a = Preview.SeedFromMenu();
            a.Gender = Random.value < 0.5f ? Random.Range(0f, 0.25f) : Random.Range(0.75f, 1f);
            a.Height = Random.Range(0.92f, 1.06f);
            a.Weight = Random.Range(0.15f, 0.8f);
            a.SkinColor = PickS(Skins);
            a.LeftEyeLidColor = a.RightEyeLidColor = a.SkinColor;
            a.HairColor = PickS(Hairs);
            a.HairPath = Random.value < 0.08f ? "" : Pick(PathCatalog.HairStyles()) ?? "";
            a.FaceLayers.Clear(); a.BodyLayers.Clear(); a.Accessories.Clear();
            void Add(List<LayerDraft> to, string path, string tint) { if (path != null) to.Add(new LayerDraft { Path = path, Tint = tint }); }
            Add(a.FaceLayers, Pick(PathCatalog.Mouths()), "#000000");
            if (a.Gender < 0.5f && Random.value < 0.35f) Add(a.FaceLayers, Pick(PathCatalog.FacialHairStyles()), a.HairColor);
            Add(a.BodyLayers, Pick(PathCatalog.Tops()), RandomCloth());
            Add(a.BodyLayers, Pick(PathCatalog.Bottoms()), RandomCloth());
            Add(a.Accessories, Pick(PathCatalog.Shoes()), RandomCloth());
            if (Random.value < 0.25f) Add(a.Accessories, Pick(PathCatalog.Headwear()), RandomCloth());
            if (Random.value < 0.2f) Add(a.Accessories, Pick(PathCatalog.Eyewear()), "#202020");
            return LookJson.FromDraft(a, null);
        }

        /// <summary>PNGs in UserData/Personify/Import a layer can be made from: [{file,name}], file is the file name.</summary>
        internal static JArray Pictures()
        {
            var list = new JArray();
            try
            {
                Directory.CreateDirectory(Paths.Import);
                foreach (string f in Directory.GetFiles(Paths.Import, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    list.Add(new JObject { ["file"] = Path.GetFileName(f), ["name"] = Path.GetFileNameWithoutExtension(f) });
            }
            catch (Exception e) { Core.Log.Error($"[look] listing {Paths.Import} failed: {e.Message}"); }
            return list;
        }

        /// <summary>
        /// Copies a picture into the project's <c>sources/</c> folder and returns its project path ("sources/x.png"),
        /// which is also its path inside the installed pack. <c>from</c> is "import" (a file name in the Import folder)
        /// or "inkorporated" (a tattoo PNG from the Inkorporated catalog). Anything else is refused; null on failure.
        /// </summary>
        internal static string ImportPicture(PackProject p, JObject a)
        {
            string from = (string)a?["from"], file = (string)a?["file"];
            string src = null;
            if (from == "import")
            {
                if (string.IsNullOrWhiteSpace(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                { Core.Log.Warning($"[look] picture refused: '{file}' is not a file name in the Import folder."); return null; }
                src = Path.Combine(Paths.Import, file);
            }
            else if (from == "inkorporated")
            {
                src = InkorporatedCatalog.ByPng(file)?.PngPath;
                if (src == null) { Core.Log.Warning($"[look] picture refused: '{file}' is not an Inkorporated tattoo."); return null; }
            }
            else { Core.Log.Warning($"[look] picture refused: unknown source '{from}'."); return null; }
            if (!File.Exists(src)) { Core.Log.Warning($"[look] picture '{src}' does not exist."); return null; }
            try
            {
                string dir = Path.Combine(p.Dir, "sources");
                Directory.CreateDirectory(dir);
                string name = Path.GetFileName(src);
                string dest = Path.Combine(dir, name);
                long size = new FileInfo(src).Length;
                // A file of the same name and size is taken to be the same picture; another one gets a number.
                for (int n = 1; File.Exists(dest) && new FileInfo(dest).Length != size; n++)
                    dest = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)}_{n}.png");
                if (!File.Exists(dest)) File.Copy(src, dest);
                string rel = "sources/" + Path.GetFileName(dest);
                Core.Log.Msg($"[look] picture {src} copied into '{p.Folder}' as {rel}.");
                return rel;
            }
            catch (Exception e) { Core.Log.Error($"[look] copying picture {src} into '{p.Folder}' failed: {e.Message}"); return null; }
        }

        private static string RandomCloth()
        {
            var c = Color.HSVToRGB(Random.value, Random.Range(0.1f, 0.6f), Random.Range(0.2f, 0.9f));
            return Preview.HexOf(c);
        }

        /// <summary>
        /// Renders the preview avatar's head into <c>portraits/&lt;id&gt;.png</c> (256x256) with a temporary camera,
        /// and returns the project path, or null after logging why not.
        /// </summary>
        internal static string CapturePortrait(PackProject p, JObject npc)
        {
            string id = (string)npc["id"];
            if (!Apply(p, npc)) { Core.Log.Warning($"[portrait] '{id}': the preview character is not available."); return null; }
            var head = Preview.HeadTransform();
            if (head == null) { Core.Log.Warning($"[portrait] '{id}': no head bone on the preview character."); return null; }

            GameObject go = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                go = new GameObject("Personify_PortraitCam");
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.11f, 0.12f, 0.16f, 1f);
                cam.fieldOfView = 22f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 10f;
                Vector3 target = head.position + Vector3.up * 0.05f;
                Vector3 forward = Preview.FacingDirection();
                cam.transform.position = target + forward * 1.15f;
                cam.transform.LookAt(target);
                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(256, 256, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                byte[] png = ImageConversion.EncodeToPNG(tex);
                string rel = "portraits/" + id + ".png";
                string abs = Path.Combine(p.Dir, "portraits", id + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(abs));
                File.WriteAllBytes(abs, png);
                Core.Log.Msg($"[portrait] '{id}': contact picture written to {abs} ({png.Length} bytes).");
                return rel;
            }
            catch (Exception e)
            {
                Core.Log.Error($"[portrait] '{id}': capture failed ({e.GetType().Name}: {e.Message}).");
                return null;
            }
            finally
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
