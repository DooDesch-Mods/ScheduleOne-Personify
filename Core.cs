using MelonLoader;
using SideHustle;
using UnityEngine;

[assembly: MelonInfo(typeof(Personify.Core), "Personify", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Personify")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonOptionalDependencies("SideHustle")]

namespace Personify
{
    /// <summary>
    /// MelonLoader entry point for Personify. Registers itself as a singleplayer, menu-space gamemode with Side
    /// Hustle. Launching it (from the main-menu hub) opens the NPC editor overlay on top of the live menu rig - no
    /// save is loaded. The editor lets the player design an NPC's appearance live and export a Personnel NPC pack.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;
            Project.ProjectLog.Info = m => Log.Msg(m);
            Project.ProjectLog.Warn = m => Log.Warning(m);
            Project.ProjectLog.Error = m => Log.Error(m);
            Project.ProjectFiles.Root = Editor.Paths.Projects;
            Host.Localization.Register();
            App.PhoneApp.Register();

            try
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
                Log.Msg($"Personify {Info.Version} registered with Side Hustle.");
            }
            catch (System.Exception e)
            {
                Log.Warning("Side Hustle not available, Personify cannot register: " + e.Message);
            }
        }

        private const string HubIconResource = "Personify.hub-icon.png";

        // GamemodeDescriptor.IconResource exists since Side Hustle 2.5.0 and Personify supports older versions, so the
        // field is set by name: an older Side Hustle registers Personify without an icon.
        private static void AddHubIcon(GamemodeDescriptor desc)
        {
            try
            {
                var field = typeof(GamemodeDescriptor).GetField("IconResource");
                if (field == null) { Log.Warning("Side Hustle is older than 2.5.0; Personify shows no icon in the hub."); return; }
                field.SetValue(desc, HubIconResource);
            }
            catch (System.Exception e) { Log.Warning("Personify could not give the hub its icon: " + e.Message); }
        }

        private static void OnLaunch(LaunchContext ctx) => Host.EditorHost.Open(ctx);

        private static void OnExit(LaunchContext ctx) => Host.EditorHost.Close();

        // The World tab's map comes out of a loaded save; see Editor.World.WorldCapture.
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == "Main") Editor.World.WorldCapture.OnMainScene();
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName == "Menu")
            {
                Host.EditorHost.Close();
                Editor.Preview.Forget();
            }
        }

        public override void OnUpdate()
        {
            if (Host.EditorHost.IsOpen) Host.EditorHost.Tick();
        }

        public override void OnLateUpdate()
        {
            if (Host.EditorHost.IsOpen) Host.EditorHost.LateTick();
        }
    }
}
