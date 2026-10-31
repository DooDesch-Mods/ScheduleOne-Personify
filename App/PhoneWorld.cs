using System;
using UnityEngine;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.PlayerScripts;
using GameMap = Il2CppScheduleOne.Map.Map;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personify.App
{
    /// <summary>
    /// What the phone app reads from and does to the loaded save: the local player's spot, the game clock, the region
    /// of a point, finding a live NPC and putting the player next to it. Each failure says why in the log and as a
    /// short reason for the status line.
    /// </summary>
    internal static class PhoneWorld
    {
        /// <summary>The local player's feet position and yaw (the value the game saves as the player's rotation).</summary>
        internal static bool TryPlayer(out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            Player local = null;
            try { local = Player.Local; }
            catch (Exception e) { Core.Log.Warning("[phone] reading the local player failed: " + e.Message); }
            if (local == null || local.transform == null) { Core.Log.Warning("[phone] no local player - not in a loaded save."); return false; }
            pos = local.transform.position;
            yaw = local.transform.eulerAngles.y;
            return true;
        }

        /// <summary>The game clock as "HH:MM", or null when there is no clock (not in a save).</summary>
        internal static string Clock()
        {
            try
            {
                if (!NetworkSingleton<TimeManager>.InstanceExists) { Core.Log.Warning("[phone] no TimeManager - not in a loaded save."); return null; }
                int hhmm = NetworkSingleton<TimeManager>.Instance.CurrentTime;
                int h = hhmm / 100, m = hhmm % 100;
                if (h < 0 || h > 23 || m < 0 || m > 59) { Core.Log.Warning($"[phone] the game clock reads {hhmm}, not a time of day."); return null; }
                return h.ToString("00") + ":" + m.ToString("00");
            }
            catch (Exception e)
            {
                Core.Log.Error("[phone] reading the game clock failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// The region name a manifest's <c>spawn.region</c> takes. The captured world comes first, as in the editor's
        /// map; the live map answers when no world was captured yet.
        /// </summary>
        internal static string RegionAt(Vector3 pos)
        {
            string r = Editor.World.WorldData.Current?.RegionAt(pos);
            if (!string.IsNullOrEmpty(r)) return r;
            try
            {
                if (!Singleton<GameMap>.InstanceExists) { Core.Log.Warning("[phone] no captured world and no live map - the point gets no region."); return null; }
                return Singleton<GameMap>.Instance.GetRegionFromPosition(pos).ToString();
            }
            catch (Exception e)
            {
                Core.Log.Warning("[phone] reading the region from the live map failed: " + e.Message);
                return null;
            }
        }

        /// <summary>The live NPC with one of these ids (the save id first, then the pack id), or null.</summary>
        internal static S1NPC FindNpc(params string[] ids)
        {
            var reg = Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry;
            if (reg == null) { Core.Log.Warning("[phone] the game's NPC registry is missing."); return null; }
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                foreach (var n in reg)
                    if (n != null && string.Equals(n.ID, id, StringComparison.OrdinalIgnoreCase)) return n;
            }
            return null;
        }

        /// <summary>Puts the player two metres in front of the NPC, facing it.</summary>
        internal static bool TeleportNextTo(S1NPC npc)
        {
            try
            {
                if (!PlayerSingleton<PlayerMovement>.InstanceExists || Player.Local == null)
                {
                    Core.Log.Error("[phone] jump: no local player to move.");
                    return false;
                }
                var mv = PlayerSingleton<PlayerMovement>.Instance;
                Transform t = npc.transform;
                Vector3 pos = t.position + t.forward * 2f + Vector3.up * 0.1f;
                Vector3 look = t.position - pos; look.y = 0f;
                Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                mv.Teleport(pos, rot, false);
                Core.Log.Msg($"[phone] jump: player placed next to '{npc.ID}' at {pos}.");
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Error($"[phone] jump: teleport failed ({e.Message}).");
                return false;
            }
        }
    }
}
