using System.Collections.Generic;
using System.Reflection;
using S1API.Rendering;
using UnityEngine;

namespace Personify.Editor
{
    /// <summary>
    /// Loads embedded UI icon PNGs (Personify.Assets.Icons.&lt;name&gt;.png) as cached Sprites for buttons (e.g. the
    /// per-layer visibility eye). Returns null when an icon is missing, so callers fall back to a text glyph.
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        public static Sprite GetSprite(string name)
        {
            if (_sprites.TryGetValue(name, out var cached)) return cached;
            Texture2D tex = GetTexture(name);
            Sprite sp = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            if (sp != null) sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _sprites[name] = sp;
            return sp;
        }

        private static Texture2D GetTexture(string name)
        {
            if (_tex.TryGetValue(name, out var cached)) return cached;
            Texture2D tex = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string res = "Personify.Assets.Icons." + name + ".png";
                using var s = asm.GetManifestResourceStream(res);
                if (s != null)
                {
                    byte[] b = new byte[s.Length];
                    s.Read(b, 0, b.Length);
                    tex = TextureUtils.LoadTextureFromBytes(b);
                    if (tex != null) { tex.filterMode = FilterMode.Bilinear; tex.hideFlags = HideFlags.DontUnloadUnusedAsset; }
                }
                else Core.Log?.Warning("[icon] missing resource: " + res);
            }
            catch (System.Exception e) { Core.Log?.Warning("[icon] '" + name + "': " + e.Message); tex = null; }
            _tex[name] = tex;
            return tex;
        }
    }
}
