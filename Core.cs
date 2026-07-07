using MelonLoader;
using SideHustle;
using UnityEngine;

[assembly: MelonInfo(typeof(Personify.Core), "Personify", "1.0.0", "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Personify")]
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

            try
            {
                API.Register(new GamemodeDescriptor
                {
                    Id = "doodesch.personify",
                    DisplayName = "Personify",
                    Description = "Design and export custom NPC packs.",
                    Author = "DooDesch",
                    Support = GamemodeSupport.Singleplayer,
                    Surface = GamemodeSurface.MenuSpace,
                    OnLaunchSingleplayer = OnLaunch,
                    OnExitToHub = OnExit
                });
                Log.Msg($"Personify {Info.Version} registered with Side Hustle.");
            }
            catch (System.Exception e)
            {
                Log.Warning("Side Hustle not available, Personify cannot register: " + e.Message);
            }
        }

        private static void OnLaunch(LaunchContext ctx) => Editor.EditorUI.Open(ctx);

        private static void OnExit(LaunchContext ctx) => Editor.EditorUI.Close();

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName == "Menu")
            {
                Editor.EditorUI.Close();
                Editor.Preview.Forget();
            }
        }

        public override void OnUpdate()
        {
            if (Editor.EditorUI.IsOpen) Editor.EditorUI.Tick();
        }

        public override void OnLateUpdate()
        {
            if (Editor.EditorUI.IsOpen) Editor.EditorUI.LateTick();
        }
    }
}
