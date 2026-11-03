using System;
using System.Linq;
using MelonLoader;
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
    /// This class names no Side Hustle type, so it loads without SideHustle.dll; see Host.SideHustleLink.
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

            if (!SideHustleLoaded())
            {
                Log.Warning("Side Hustle is not installed: the Personify editor is not in the main-menu hub. Install Side Hustle to open it.");
                return;
            }
            try
            {
                Host.SideHustleLink.Register(Info.Version);
            }
            catch (Exception e)
            {
                Log.Warning("Side Hustle not available, Personify cannot register: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static bool SideHustleLoaded() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SideHustle");

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
