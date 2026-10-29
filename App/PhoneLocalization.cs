using System;
using System.Collections.Generic;
using System.IO;
using DooDesch.Localization;
using Newtonsoft.Json.Linq;

namespace Personify.App
{
    /// <summary>
    /// The phone app's German table (<c>Assets/phone/de.json</c>, English text -> German). L10n keeps one table per
    /// language and a second Register replaces the first, so the editor's table is merged in before registering.
    /// Lookups fall back to the phone table directly, so the phone stays German even if the editor registers its
    /// table again afterwards.
    /// </summary>
    internal static class PhoneLocalization
    {
        private const string PhoneResource = "Personify.Assets.phone.de.json";
        private const string EditorResource = "Personify.Assets.editor.de.json";

        private static readonly Dictionary<string, string> _phone = new Dictionary<string, string>(StringComparer.Ordinal);

        private static bool German => L10n.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);

        internal static void Register()
        {
            if (!Read(PhoneResource, _phone, required: true)) return;
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            Read(EditorResource, merged, required: false);
            foreach (var kv in _phone)
                if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;
            L10n.Register("de", merged);
            Core.Log.Msg($"[phone] {_phone.Count} German phone strings registered ({merged.Count} with the editor's); language {L10n.Language}.");
        }

        private static bool Read(string resource, Dictionary<string, string> into, bool required)
        {
            try
            {
                using var s = typeof(PhoneLocalization).Assembly.GetManifestResourceStream(resource);
                if (s == null)
                {
                    if (required) Core.Log.Warning($"[phone] {resource} is not embedded - the phone app shows English only.");
                    else Core.Log.Msg($"[phone] {resource} is not embedded - only the phone strings are registered.");
                    return false;
                }
                using var r = new StreamReader(s);
                foreach (var prop in JObject.Parse(r.ReadToEnd()).Properties())
                    if (prop.Value.Type == JTokenType.String) into[prop.Name] = (string)prop.Value;
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Error($"[phone] reading {resource} failed ({e.Message}) - the phone app shows English only.");
                return false;
            }
        }

        /// <summary>The English text in the current language.</summary>
        internal static string T(string en)
        {
            string s = L10n.T(en);
            if (German && string.Equals(s, en, StringComparison.Ordinal) && _phone.TryGetValue(en, out string de)) return de;
            return s;
        }

        /// <summary>Translates, then fills {0}, {1}, ... with <paramref name="args"/>.</summary>
        internal static string T(string en, params object[] args)
        {
            try { return string.Format(T(en), args); }
            catch (FormatException e)
            {
                Core.Log.Warning($"[phone] translation of '{en}' has broken placeholders ({e.Message}) - English is shown.");
                return string.Format(en, args);
            }
        }

        /// <summary>Every phone string in the current language, for the page's t().</summary>
        internal static JObject Strings()
        {
            var o = new JObject();
            foreach (string key in _phone.Keys) o[key] = T(key);
            return o;
        }
    }
}
