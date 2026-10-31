#if DEBUG
using System;
using HarmonyLib;
using UnityEngine;

namespace Personify.App
{
    /// <summary>
    /// Dev console command <c>personifyphone &lt;call&gt; [folder|npcId]</c>: runs one phone app call without the phone
    /// and writes its JSON answer to the log. Calls: init, projects, npcs, npc, spawn, step, undo, jump, look. The
    /// folder and the NPC id are separated by "|" because a folder name may contain spaces. Both
    /// <c>Console.SubmitCommand</c> overloads are patched (the string one calls the list one), so one submission is
    /// handled once per frame and signature.
    /// </summary>
    internal static class PhoneConsole
    {
        private const string Prefix = "personifyphone";
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

            string call = p.Length > 1 ? p[1].ToLowerInvariant() : "";
            string arg = p.Length > 2 ? string.Join(" ", p, 2, p.Length - 2).Replace('|', '\n') : "";
            string answer = PhoneApp.DebugCall(call, arg);
            if (answer == null)
                Core.Log.Msg("personifyphone <init|projects|npcs|npc|spawn|step|undo|jump|look> [folder|npcId]");
            else
                Core.Log.Msg($"[phone] {call} -> {answer}");
            return true;
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), "SubmitCommand", new Type[] { typeof(string) })]
    internal static class Personify_PhoneConsole_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !PhoneConsole.TryHandle(args); }
            catch (Exception e) { Core.Log?.Error("[phone] console command failed: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), "SubmitCommand", new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Personify_PhoneConsole_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !PhoneConsole.TryHandle(args); }
            catch (Exception e) { Core.Log?.Error("[phone] console command failed: " + e.Message); return true; }
        }
    }
}
#endif
