using System;
using System.Reflection;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personify.App
{
    /// <summary>
    /// Refreshes a live NPC's look through Personnel, found by reflection so Personify loads without it:
    /// <c>Personnel.API.Reload()</c> reads every pack manifest from disk again, <c>TryGet</c> returns the new
    /// definition, <c>ApplyAppearance</c> and <c>ApplyDistortion</c> put it on the NPC's avatar.
    /// </summary>
    internal static class PhoneLook
    {
        internal enum Outcome { Applied, NoPersonnel, NoDefinition, NotInWorld, Failed }

        private static Type _api;
        private static bool _searched;

        private static Type Api()
        {
            if (_searched) return _api;
            _searched = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Personnel") continue;
                _api = asm.GetType("Personnel.API", false);
                break;
            }
            if (_api == null) Core.Log.Warning("[phone] Personnel.API not found - a look applies on the next save load.");
            return _api;
        }

        private static MethodInfo Method(Type t, string name)
        {
            var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (m == null) Core.Log.Warning($"[phone] Personnel.API.{name} not found - this Personnel is older than the phone app needs.");
            return m;
        }

        /// <summary>Re-reads the installed packs and puts the definition <paramref name="defId"/> on the live NPC.</summary>
        internal static Outcome Apply(string defId, string saveId)
        {
            Type api = Api();
            if (api == null) return Outcome.NoPersonnel;
            MethodInfo reload = Method(api, "Reload"), tryGet = Method(api, "TryGet"),
                       appearance = Method(api, "ApplyAppearance"), distortion = Method(api, "ApplyDistortion");
            if (reload == null || tryGet == null || appearance == null) return Outcome.NoPersonnel;

            try
            {
                reload.Invoke(null, null);
                Core.Log.Msg("[phone] Personnel re-read the installed packs.");

                var args = new object[] { defId, null };
                if (!(tryGet.Invoke(null, args) is bool found && found) || args[1] == null)
                {
                    Core.Log.Warning($"[phone] Personnel has no definition '{defId}' after the reload.");
                    return Outcome.NoDefinition;
                }
                object def = args[1];

                S1NPC npc = PhoneWorld.FindNpc(saveId, defId);
                if (npc == null) { Core.Log.Warning($"[phone] look: NPC '{saveId ?? defId}' is not in this world."); return Outcome.NotInWorld; }
                if (npc.Avatar == null) { Core.Log.Warning($"[phone] look: NPC '{npc.ID}' has no avatar."); return Outcome.Failed; }

                if (!(appearance.Invoke(null, new object[] { npc.Avatar, def }) is bool ok && ok))
                {
                    Core.Log.Error($"[phone] look: Personnel could not build the look of '{defId}'.");
                    return Outcome.Failed;
                }
                if (distortion != null) distortion.Invoke(null, new object[] { npc.Avatar, def });
                Core.Log.Msg($"[phone] look of '{defId}' applied to the live NPC '{npc.ID}'.");
                return Outcome.Applied;
            }
            catch (Exception e)
            {
                Core.Log.Error($"[phone] applying the look of '{defId}' failed: {e.InnerException?.Message ?? e.Message}");
                return Outcome.Failed;
            }
        }
    }
}
