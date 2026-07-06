using System.Text;

namespace Personify.Editor
{
    /// <summary>
    /// Deterministic NPC ids, kept in sync by hand with Personnel's <c>Ids</c>:
    /// an NPC's exported id is always <c>normalize(packName)_normalize(npcName)</c> so ids never collide across packs
    /// and the same pack can't hold two NPCs with the same name.
    /// </summary>
    public static class Ids
    {
        public static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool under = false;
            foreach (char ch in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); under = false; }
                else if (!under) { sb.Append('_'); under = true; }
            }
            return sb.ToString().Trim('_');
        }

        public static string Make(string pack, string name)
        {
            string p = Normalize(pack), n = Normalize(name);
            if (string.IsNullOrEmpty(p)) return n;
            if (string.IsNullOrEmpty(n)) return p;
            return p + "_" + n;
        }
    }
}
