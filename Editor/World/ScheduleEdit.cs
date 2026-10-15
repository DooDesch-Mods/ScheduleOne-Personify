using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Personify.Editor.World
{
    /// <summary>
    /// The kinds of schedule step the editor offers. Each maps 1:1 onto a Personnel manifest action type. Steps of
    /// any other type (written by hand into a pack) are kept and shown read-only, so opening a pack never loses one.
    /// </summary>
    public enum StepKind { Walk, Inside, HangOut, Sit, Drive, Atm, Vending, Other }

    /// <summary>
    /// Edits an NPC's <c>schedule</c> block in place. The block stays a raw JSON array on the draft, so every field the
    /// editor has no control for (hand-written seat paths, slot machine bets, ...) survives an edit untouched.
    /// </summary>
    public static class ScheduleEdit
    {
        public static readonly StepKind[] Offered =
            { StepKind.Walk, StepKind.Inside, StepKind.HangOut, StepKind.Sit, StepKind.Drive, StepKind.Atm, StepKind.Vending };

        public static string TypeOf(StepKind k) => k switch
        {
            StepKind.Walk => "walkTo",
            StepKind.Inside => "stayInBuilding",
            StepKind.HangOut => "locationAction",
            StepKind.Sit => "sit",
            StepKind.Drive => "driveToCarPark",
            StepKind.Atm => "useAtm",
            StepKind.Vending => "useVendingMachine",
            _ => null
        };

        public static StepKind KindOf(JObject step)
        {
            string t = (string)step?["type"];
            if (string.IsNullOrEmpty(t)) return StepKind.Other;
            foreach (StepKind k in Offered)
                if (string.Equals(TypeOf(k), t, StringComparison.OrdinalIgnoreCase)) return k;
            return StepKind.Other;
        }

        public static string Label(StepKind k) => k switch
        {
            StepKind.Walk => "Walk to",
            StepKind.Inside => "Go inside",
            StepKind.HangOut => "Hang out",
            StepKind.Sit => "Sit down",
            StepKind.Drive => "Drive to",
            StepKind.Atm => "Use an ATM",
            StepKind.Vending => "Use a vending machine",
            _ => "Custom step"
        };

        /// <summary>Whether a step of this kind is placed by a point on the map.</summary>
        public static bool NeedsPoint(StepKind k) => k == StepKind.Walk || k == StepKind.HangOut;

        /// <summary>The NPC's steps as a live array, creating an empty one on first use.</summary>
        public static JArray Steps(NpcDraft npc)
        {
            if (npc.Schedule is JArray a) return a;
            var fresh = new JArray();
            // A non-array schedule is malformed; Personnel would skip it anyway, so start over rather than crash.
            if (npc.Schedule != null && npc.Schedule.Type != JTokenType.Null)
                Core.Log?.Warning($"[schedule] '{npc.Name}' had a schedule that is not a list - replaced with an empty one.");
            npc.Schedule = fresh;
            return fresh;
        }

        public static int Count(NpcDraft npc) => npc.Schedule is JArray a ? a.Count : 0;

        /// <summary>Adds a step at <paramref name="minutes"/> and keeps the list in time order. Returns it.</summary>
        public static JObject Add(NpcDraft npc, StepKind kind, int minutes)
        {
            var step = new JObject { ["type"] = TypeOf(kind), ["time"] = FormatTime(minutes) };
            if (kind == StepKind.Inside || kind == StepKind.HangOut || kind == StepKind.Sit) step["duration"] = 60;
            Steps(npc).Add(step);
            Sort(npc);
            return step;
        }

        /// <summary>Changes a step's kind, dropping the fields only the old kind understood.</summary>
        public static void SetKind(JObject step, StepKind kind)
        {
            StepKind old = KindOf(step);
            if (old == kind) return;
            JToken pos = step["position"];
            string time = (string)step["time"];
            step.RemoveAll();
            step["type"] = TypeOf(kind);
            step["time"] = time ?? "08:00";
            if (pos != null && NeedsPoint(kind)) step["position"] = pos;
            if (kind == StepKind.Inside || kind == StepKind.HangOut || kind == StepKind.Sit) step["duration"] = 60;
        }

        public static void Remove(NpcDraft npc, JObject step)
        {
            if (npc.Schedule is JArray a) a.Remove(step);
        }

        /// <summary>Stable sort by start time. Steps without a readable time keep their place at the end.</summary>
        public static void Sort(NpcDraft npc)
        {
            if (!(npc.Schedule is JArray a) || a.Count < 2) return;
            var items = new List<JToken>(a);
            var keyed = new List<(int key, int index, JToken item)>();
            for (int i = 0; i < items.Count; i++)
                keyed.Add((Minutes(items[i] as JObject) ?? int.MaxValue, i, items[i]));
            keyed.Sort((x, y) => x.key != y.key ? x.key.CompareTo(y.key) : x.index.CompareTo(y.index));
            a.Clear();
            foreach (var k in keyed) a.Add(k.item);
        }

        // ---- fields -------------------------------------------------------------------------------------------

        /// <summary>Minutes after midnight, or null when the step has no readable time.</summary>
        public static int? Minutes(JObject step)
        {
            JToken t = step?["time"];
            if (t == null) return null;
            if (t.Type == JTokenType.Integer) return FromHhmm((int)t);
            string s = ((string)t)?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            int colon = s.IndexOf(':');
            if (colon > 0 &&
                int.TryParse(s.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) &&
                int.TryParse(s.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) &&
                h >= 0 && h < 24 && m >= 0 && m < 60)
                return h * 60 + m;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw) ? FromHhmm(raw) : null;
        }

        private static int? FromHhmm(int hhmm)
        {
            int h = hhmm / 100, m = hhmm % 100;
            return h >= 0 && h < 24 && m >= 0 && m < 60 ? h * 60 + m : (int?)null;
        }

        public static void SetMinutes(JObject step, int minutes) => step["time"] = FormatTime(minutes);

        public static string FormatTime(int minutes)
        {
            minutes = ((minutes % 1440) + 1440) % 1440;
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        public static Vector3? Point(JObject step)
        {
            if (!(step?["position"] is JArray p) || p.Count < 3) return null;
            try { return new Vector3((float)p[0], (float)p[1], (float)p[2]); }
            catch (Exception e)
            {
                Core.Log?.Warning("[schedule] unreadable position " + p.ToString(Newtonsoft.Json.Formatting.None) + ": " + e.Message);
                return null;
            }
        }

        public static void SetPoint(JObject step, Vector3 p) =>
            step["position"] = new JArray(Round(p.x), Round(p.y), Round(p.z));

        private static double Round(float v) => Math.Round(v, 2);

        public static int Duration(JObject step) => (int?)step?["duration"] ?? 60;

        public static void SetDuration(JObject step, int minutes) => step["duration"] = Mathf.Clamp(minutes, 5, 1440);

        public static string Text(JObject step, string key) => (string)step?[key] ?? "";

        public static void SetText(JObject step, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) step.Remove(key);
            else step[key] = value;
        }

        /// <summary>One line for the step list: what the NPC does there, in plain words.</summary>
        public static string Describe(JObject step)
        {
            StepKind k = KindOf(step);
            switch (k)
            {
                case StepKind.Walk:
                    return "Walk " + PointName(step, "to");
                case StepKind.HangOut:
                {
                    string act = Text(step, "action");
                    string what = string.IsNullOrEmpty(act) || act.Equals("none", StringComparison.OrdinalIgnoreCase)
                        ? "Hang out" : ActionLabel(act);
                    return what + " " + PointName(step, "at") + ", " + FormatDuration(Duration(step));
                }
                case StepKind.Inside:
                {
                    string b = Text(step, "building");
                    return "Go inside " + (b.Length > 0 ? b : "(pick a building)") + ", " + FormatDuration(Duration(step));
                }
                case StepKind.Sit:
                {
                    string s = Text(step, "seatSet");
                    if (s.Length == 0) s = Text(step, "seatSetPath");
                    return "Sit at " + (s.Length > 0 ? Place.Pretty(s) : "(pick a seat)") + ", " + FormatDuration(Duration(step));
                }
                case StepKind.Drive:
                {
                    string lot = Text(step, "parkingLot");
                    return "Drive to " + (lot.Length > 0 ? lot : "(pick a car park)");
                }
                case StepKind.Atm: return "Use the nearest ATM";
                case StepKind.Vending: return "Use the nearest vending machine";
                default: return "Custom step: " + Text(step, "type");
            }
        }

        // "near Town hall" when a named place is close, otherwise "<preposition> 12, -40".
        private static string PointName(JObject step, string preposition)
        {
            Vector3? p = Point(step);
            if (p == null) return "(click the map)";
            string place = WorldData.Current?.NearestPlaceName(p.Value);
            return place ?? $"{preposition} {p.Value.x:0}, {p.Value.z:0}";
        }

        public static string FormatDuration(int minutes) =>
            minutes < 60 ? minutes + " min" : minutes % 60 == 0 ? (minutes / 60) + " h" : (minutes / 60) + " h " + (minutes % 60) + " min";

        public static readonly string[] HangOutActions = { "none", "smokeBreak", "drinking", "graffiti" };

        public static string ActionLabel(string action) => action switch
        {
            "smokeBreak" => "Smoke",
            "drinking" => "Drink",
            "graffiti" => "Spray graffiti",
            "holdItem" => "Hold an item",
            _ => "Hang out"
        };
    }
}
