using System;
using HarmonyLib;
using UnityEngine;

namespace Personify.Editor.World
{
    /// <summary>
    /// <c>personify capture</c> in the game console reads the map, places and ground again, for a capture that came
    /// out wrong or after a mod moved the town around. Both <c>Console.SubmitCommand</c> overloads are patched (the
    /// string one calls the list one), so one submission is handled once per frame and signature.
    /// </summary>
    internal static class WorldConsole
    {
        private const string Prefix = "personify";
        private static int _lastFrame = -1;
        private static string _lastSig = "";

        internal static bool TryHandle(string line) =>
            !string.IsNullOrWhiteSpace(line) && Dispatch(line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            var p = new string[args.Count];
            for (int i = 0; i < args.Count; i++) p[i] = args[i];
            return Dispatch(p);
        }

        /// <summary>True = ours, swallow it. False = let the game handle the command.</summary>
        private static bool Dispatch(string[] p)
        {
            if (p.Length == 0 || !p[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string sig = string.Join(" ", p);
            if (Time.frameCount == _lastFrame && sig == _lastSig) return true;
            _lastFrame = Time.frameCount; _lastSig = sig;

            string cmd = p.Length > 1 ? p[1].ToLowerInvariant() : "help";
            if (cmd == "capture")
            {
                Core.Log?.Msg("[world] capture requested from the console.");
                WorldCapture.OnMainScene(force: true);
            }
            else Core.Log?.Msg("personify capture - read the map, places and ground again for the World tab.");
            return true;
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), "SubmitCommand", new Type[] { typeof(string) })]
    internal static class Personify_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !WorldConsole.TryHandle(args); }
            catch (Exception e) { Core.Log?.Error("[world] console command failed: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), "SubmitCommand", new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Personify_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !WorldConsole.TryHandle(args); }
            catch (Exception e) { Core.Log?.Error("[world] console command failed: " + e.Message); return true; }
        }
    }
}
