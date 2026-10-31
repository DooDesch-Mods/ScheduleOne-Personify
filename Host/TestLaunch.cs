using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;

namespace Personify.Host
{
    /// <summary>"Test": installs the project, leaves a launch marker for Personnel and restarts the game, which then
    /// loads the chosen save and puts the player next to the NPC.</summary>
    internal static class TestLaunch
    {
        internal static JArray Saves()
        {
            var list = new JArray();
            try
            {
                if (PersistentSingleton<LoadManager>.InstanceExists)
                {
                    PersistentSingleton<LoadManager>.Instance.RefreshSaveInfo();
                    // Refreshing the save info dresses the menu character in the newest save's look, which is the
                    // preview character; the shown NPC is put back on it.
                    EditorHost.ReapplyPreview();
                }
                var games = LoadManager.SaveGames;
                if (games == null) { Core.Log.Warning("[test] the game has no save list yet."); return list; }
                DateTime newest = DateTime.MinValue;
                foreach (var g in games)
                {
                    if (g == null) continue;
                    DateTime played = Directory.Exists(g.SavePath) ? Directory.GetLastWriteTimeUtc(g.SavePath) : DateTime.MinValue;
                    if (played > newest) newest = played;
                    list.Add(new JObject
                    {
                        ["slot"] = g.SaveSlotNumber, ["name"] = g.OrganisationName ?? "", ["org"] = g.OrganisationName ?? "",
                        ["lastPlayed"] = played == DateTime.MinValue ? "" : played.ToString("o"),
                    });
                }
                foreach (JObject o in list)
                    if ((string)o["lastPlayed"] == newest.ToString("o")) { o["newest"] = true; break; }
            }
            catch (Exception e) { Core.Log.Error("[test] reading the save list failed: " + e.Message); }
            return list;
        }

        internal static JObject Start(int slot, string npcId)
        {
            var s = EditorHost.Session;
            if (s == null) return Calls.Fail("no_project");
            if (slot <= 0) return Calls.Fail("no_save");
            if (string.IsNullOrEmpty(npcId) || s.Project.FindNpc(npcId) == null) return Calls.Fail("npc_not_found");

            if (!s.Flush()) return Calls.Fail("save_failed");
            var r = Project.Installer.Install(s.Project, GameData.PacksRoot, GameData.KnownNpcs());
            if (!r.ok) { Core.Log.Warning($"[test] not started: install answered {r.code} ({r.text})."); return Calls.Fail(r.code ?? "install_failed"); }

            var write = GameData.PersonnelMethod("Personnel.Testing.LaunchMarker", "Write");
            var restart = GameData.PersonnelMethod("Personnel.Testing.LaunchMarker", "RestartGame");
            if (write == null || restart == null) return Calls.Fail("personnel_missing");
            try
            {
                if (!(write.Invoke(null, new object[] { slot, npcId }) is bool w && w)) return Calls.Fail("marker_failed");
                Core.Log.Msg($"[test] '{s.Project.Folder}' installed, marker for save {slot} and NPC '{npcId}' written - restarting.");
                EditorHost.CloseProject();
                if (!(restart.Invoke(null, null) is bool ok && ok)) return Calls.Fail("restart_failed");
                return new JObject { ["ok"] = true };
            }
            catch (Exception e)
            {
                Core.Log.Error($"[test] starting the test failed: {e.InnerException?.Message ?? e.Message}");
                return Calls.Fail("restart_failed");
            }
        }
    }
}
