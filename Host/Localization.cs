using System;
using System.Collections.Generic;
using System.IO;
using DooDesch.Localization;
using Newtonsoft.Json.Linq;

namespace Personify.Host
{
    /// <summary>
    /// The page's German table (<c>Assets/editor/de.json</c>, English text -> German) registered with DooDesch.L10n,
    /// so the shared language setting and player translation files apply to the editor like to every other mod.
    /// </summary>
    internal static class Localization
    {
        private const string Resource = "Personify.Assets.editor.de.json";

        internal static IReadOnlyCollection<string> Keys => _keys;
        private static readonly List<string> _keys = new List<string>();

        internal static void Register()
        {
            try
            {
                using var s = typeof(Localization).Assembly.GetManifestResourceStream(Resource);
                if (s == null) { Core.Log.Warning($"[l10n] {Resource} is not embedded - the editor shows English only."); return; }
                using var r = new StreamReader(s);
                var table = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in JObject.Parse(r.ReadToEnd()).Properties())
                {
                    if (prop.Value.Type != JTokenType.String) continue;
                    table[prop.Name] = (string)prop.Value;
                    _keys.Add(prop.Name);
                }
                L10n.Register("de", table);
                Core.Log.Msg($"[l10n] {table.Count} German editor strings registered; language {L10n.Language}.");
            }
            catch (Exception e)
            {
                Core.Log.Error($"[l10n] reading {Resource} failed ({e.Message}) - the editor shows English only.");
            }
        }
    }
}
