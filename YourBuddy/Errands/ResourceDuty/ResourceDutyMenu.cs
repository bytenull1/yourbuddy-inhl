using System;
using System.Collections.Generic;

namespace YourBuddy
{
    internal sealed class ResourceDutyMenu
    {
        internal bool Open { get; private set; }
        internal bool KeepPage { get; private set; }
        private readonly Dictionary<string, string> buttons = new(StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyList<string> Commands(IReadOnlyList<string> normal)
        {
            buttons.Clear();
            if (!Open) return [.. normal, "Resources"];
            ResourceDutySettings s = ResourceDuty.Settings;
            List<string> labels = [];
            void Add(string label, string action) { labels.Add(label); buttons[label] = action; }
            for (int i = 0; i < 3; i++)
                Add($"{ResourceDutySettings.Label(i)}: {(s.Rule(i).Enabled ? "on" : "off")}", i.ToString());
            Add($"Buying: {(s.Buying ? "on" : "off")}", "buying");
            Add($"Limit: {s.Budget}", "budget");
            Add("Orders", "back");
            return labels;
        }

        internal string? Answer(string text)
        {
            text = text.Trim();
            if (text.Equals("resources", StringComparison.OrdinalIgnoreCase))
            {
                Open = KeepPage = true;
                return "Enable duties, then choose Orders > Decide to let Buddy work.";
            }
            if (!Open || !buttons.TryGetValue(text, out string? action)) { Reset(); return null; }
            ResourceDutySettings s = ResourceDuty.Settings;
            if (action == "back") { Reset(); KeepPage = true; return "Choose an order."; }
            s.Paused = false;
            if (action == "buying") { s.Buying = !s.Buying; return s.Buying ? "Buying allowed within the limit." : "Buying off."; }
            if (action == "budget") { s.CycleBudget(); return $"Spending limit: {s.Budget}."; }
            int kind = int.Parse(action);
            ResourceRule rule = s.Rule(kind);
            rule.Enabled = !rule.Enabled;
            return $"{ResourceDutySettings.Label(kind)} duty {(rule.Enabled ? "on" : "off")}.";
        }

        internal void Reset() { Open = KeepPage = false; buttons.Clear(); }
    }
}
