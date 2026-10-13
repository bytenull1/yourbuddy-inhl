using System;
using System.Linq;
using NPC.Core;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using YourBuddy;

// One fact: the checks share static state and run in order.
public sealed class Checks(ITestOutputHelper output)
{
    [Fact]
    public void All()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        ResourceDuty.RegisterLifecycle();
        ResourceDutySettings settings = new() { Buying = true, Budget = 250 };
        settings.Oxygen.Enabled = true;
        ResourceDuty.Load(settings);
        NpcEvents.ResetWorld();
        NpcEvents.Start(false);
        Check(ResourceDuty.Settings.Oxygen.Enabled && ResourceDuty.Settings.Buying && ResourceDuty.Settings.Budget == 250,
            "post-load WorldReset and GameStarting(false) preserve sidecar settings");
        string json = JsonConvert.SerializeObject(ResourceDuty.Settings);
        ResourceDuty.Load(JsonConvert.DeserializeObject<ResourceDutySettings>(json));
        Check(ResourceDuty.Settings.Budget == 250 && ResourceDuty.Settings.Oxygen.Enabled, "next save retains settings");
        ResourceErrand job = new();
        ResourceDuty.Owner = job;
        NpcEvents.Frame();
        NpcEvents.ResetWorld();
        Check(job.Cancelled && ResourceDuty.Owner == null && ResourceDuty.Settings.Budget == 250,
            "scene reset cancels runtime job without clearing saved policy");
        NpcEvents.Start(true);
        Check(!ResourceDuty.Enabled && !ResourceDuty.Settings.Buying && ResourceDuty.Settings.Budget == 0, "new game resets policy");
        ResourceDuty.Load(null);
        Check(!ResourceDuty.Enabled, "save without sidecar defaults off");
        ResourceDuty.Load(JsonConvert.DeserializeObject<ResourceDutySettings>("{\"Oxygen\":null,\"Budget\":-5}"));
        Check(!ResourceDuty.Settings.Oxygen.Enabled && ResourceDuty.Settings.Budget == 0, "malformed save normalized");
        for (int i = 0; i < 3; i++)
        {
            ResourceRule rule = ResourceDuty.Settings.Rule(i);
            Check(!rule.NeedsRefill(20, false), "disabled duty stays off");
            rule.Enabled = true;
            Check(rule.NeedsRefill(30, false) && !rule.NeedsRefill(31, false), "fixed start threshold");
            Check(rule.NeedsRefill(79, true) && !rule.NeedsRefill(80, true), "fixed target hysteresis");
            Check(!rule.NeedsRefill(float.NaN, true) && !rule.NeedsRefill(-1, true), "invalid levels rejected");
        }
        settings = ResourceDuty.Settings;
        settings.Buying = true;
        settings.Budget = 100;
        Check(settings.CanBuy(settings.Oxygen, 100, 100), "exact budget and wallet allowed");
        Check(!settings.CanBuy(settings.Oxygen, 101, 1000) && !settings.CanBuy(settings.Oxygen, 100, 99), "purchase limits enforced");
        Check(!settings.CanBuy(settings.Oxygen, 0, 100) && !settings.CanBuy(settings.Oxygen, -1, 100), "invalid price rejected");
        settings.Buying = false;
        Check(!settings.CanBuy(settings.Oxygen, 1, 100), "buying permission enforced");
        settings.Budget = 0;
        foreach (int expected in new[] {1500, 3000, 6000, 12000, 0})
        {
            settings.CycleBudget();
            Check(settings.Budget == expected, "click cycles spending presets");
        }
        settings.Budget = 170;
        settings.CycleBudget();
        Check(settings.Budget == 1500, "partly spent budget cycles to next preset");
        ResourceDutyMenu menu = new();
        BuddyBehaviour buddy = new() { IsOutside = false, Floating = false, SuitSuited = false, IsDead = false, Asleep = false, Hiding = false, ReachDescription = null };
        string Answer(string text) => menu.Answer(text) ?? BuddyDialogCommands.Run(buddy, text);
        var flat = BuddyDialogCommands.NamesFor(buddy);
        Check(menu.Commands(flat).Take(flat.Count).SequenceEqual(flat), "flat orders preserved");
        Answer("Resources");
        Check(menu.Open && menu.KeepPage && menu.Commands(flat).Count == 6, "single resources page stays open");
        string oxygen = menu.Commands(flat)[0];
        bool wasEnabled = settings.Oxygen.Enabled;
        Check(!Answer(oxygen).Contains('\n') && settings.Oxygen.Enabled != wasEnabled && menu.KeepPage, "toggle uses one-line reply and stays open");
        menu.Commands(flat);
        Answer("follow me");
        Check(buddy.LastOrder == "follow" && !menu.Open && !menu.KeepPage, "ordinary order never swallowed by settings");
        Answer("resources");
        Check(Answer("1234") == "code:1234", "numbers retain door-code semantics");
        Answer("resources");
        menu.Commands(flat);
        Answer("Orders");
        Check(!menu.Open && menu.KeepPage && menu.Commands(flat).Contains("Follow"), "Orders returns to flat page in place");
        menu.Reset();
        Check(!menu.KeepPage, "closing conversation resets navigation");
        ResourceProbeBudget budget = new(4);
        for(int i = 0; i < 4; i++) Check(budget.Take(100), "bounded storage candidate allowed");
        Check(!budget.Take(100) && !budget.Take(100), "same-frame searches share exhausted budget");
        Check(budget.Take(101), "storage search continues next frame");
        output.WriteLine($"Passed {checks} resource lifecycle, policy, menu and probe-budget checks.");
    }
}
