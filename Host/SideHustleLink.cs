using System;
using System.Runtime.CompilerServices;
using SideHustle;

namespace Personify.Host
{
    /// <summary>
    /// Every use of a Side Hustle type lives here. The JIT resolves the types of a method body when it compiles that
    /// method, so a Side Hustle type anywhere in <see cref="Core.OnInitializeMelon"/> would make the whole method throw
    /// FileNotFoundException before its first line when SideHustle.dll is missing. Kept apart and never inlined, only
    /// <see cref="Register"/> fails, inside the caller's try.
    /// </summary>
    internal static class SideHustleLink
    {
        private const string HubIconResource = "Personify.hub-icon.png";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(string version)
        {
            var desc = new GamemodeDescriptor
            {
                Id = "doodesch.personify",
                DisplayName = "Personify",
                Description = "Design and export custom NPC packs.",
                Author = "DooDesch",
                Support = GamemodeSupport.Singleplayer,
                Surface = GamemodeSurface.MenuSpace,
                OnLaunchSingleplayer = OnLaunch,
                OnExitToHub = OnExit
            };
            AddHubIcon(desc);
            API.Register(desc);
            Core.Log.Msg($"Personify {version} registered with Side Hustle.");
        }

        // GamemodeDescriptor.IconResource exists since Side Hustle 2.5.0 and Personify supports older versions, so the
        // field is set by name: an older Side Hustle registers Personify without an icon.
        private static void AddHubIcon(GamemodeDescriptor desc)
        {
            try
            {
                var field = typeof(GamemodeDescriptor).GetField("IconResource");
                if (field == null) { Core.Log.Warning("Side Hustle is older than 2.5.0; Personify shows no icon in the hub."); return; }
                field.SetValue(desc, HubIconResource);
            }
            catch (Exception e) { Core.Log.Warning("Personify could not give the hub its icon: " + e.Message); }
        }

        private static void OnLaunch(LaunchContext ctx) => EditorHost.Open(ctx.ReturnToHub);

        private static void OnExit(LaunchContext ctx) => EditorHost.Close();
    }
}
