using System;
using System.IO;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Personify.Project;

namespace Personify.App
{
    /// <summary>
    /// The phone app's last choice of project and NPC, kept in <c>UserData/Personify/phone.json</c> so the app opens
    /// where the player left it. A "Test" launch from the editor wins over that choice: Personnel deletes its
    /// <c>launch.json</c> once the save starts loading, so the marker is read at mod start, while it still exists.
    /// </summary>
    internal static class PhoneState
    {
        internal static string FilePath => Path.Combine(Editor.Paths.Root, "phone.json");

        private static string LaunchFile => Path.Combine(MelonEnvironment.UserDataDirectory, "Personnel", "launch.json");

        /// <summary>Folder of the last project, or null.</summary>
        internal static string Project { get; private set; }

        /// <summary>Id of the last NPC, or null.</summary>
        internal static string Npc { get; private set; }

        internal static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { Core.Log.Msg("[phone] no phone.json yet - the app opens at the project list."); return; }
                var o = JObject.Parse(File.ReadAllText(FilePath));
                Project = (string)o["project"];
                Npc = (string)o["npc"];
                Core.Log.Msg($"[phone] last choice: project '{Project}', NPC '{Npc}'.");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[phone] {FilePath} is unreadable ({e.Message}) - the app opens at the project list.");
            }
        }

        /// <summary>Stores the choice. A null <paramref name="npc"/> keeps only the project.</summary>
        internal static void Remember(string folder, string npc)
        {
            if (string.Equals(folder, Project, StringComparison.Ordinal) && string.Equals(npc, Npc, StringComparison.Ordinal)) return;
            Project = folder;
            Npc = npc;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var o = new JObject { ["project"] = folder, ["npc"] = npc };
                ProjectFiles.WriteAtomic(FilePath, o.ToString(Formatting.Indented));
                Core.Log.Msg($"[phone] remembered project '{folder}', NPC '{npc}'.");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[phone] writing {FilePath} failed ({e.Message}) - the choice is kept until the game closes.");
            }
        }

        /// <summary>
        /// Reads Personnel's launch marker, if one is waiting, and makes the project that holds its NPC the current
        /// choice. Runs at mod start because the marker is gone by the time the save is loaded.
        /// </summary>
        internal static void CaptureLaunchMarker()
        {
            if (!File.Exists(LaunchFile)) { Core.Log.Msg("[phone] no Personnel launch marker - keeping the last phone choice."); return; }

            string id;
            try { id = (string)JObject.Parse(File.ReadAllText(LaunchFile))["npcId"]; }
            catch (Exception e) { Core.Log.Warning($"[phone] launch marker {LaunchFile} unreadable ({e.Message}) - keeping the last phone choice."); return; }
            if (string.IsNullOrWhiteSpace(id)) { Core.Log.Msg("[phone] launch marker names no NPC - keeping the last phone choice."); return; }

            if (string.IsNullOrEmpty(ProjectFiles.Root)) { Core.Log.Warning("[phone] projects root is not set yet - the launch marker is ignored."); return; }
            foreach (var s in ProjectFiles.List())
            {
                if (s.formatVersion != PackProject.FormatVersion) continue;
                var p = ProjectFiles.Load(s.folder, migrate: false);
                var npc = p?.FindNpc(id);
                if (npc == null) continue;
                Remember(s.folder, (string)npc["id"]);
                Core.Log.Msg($"[phone] launch marker: NPC '{id}' is in project '{s.folder}', the app opens there.");
                return;
            }
            Core.Log.Warning($"[phone] launch marker names NPC '{id}', but no project holds it - keeping the last phone choice.");
        }
    }
}
