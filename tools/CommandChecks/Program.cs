using System;
using System.Linq;
using YourBuddy;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}
BuddyBehaviour buddy = new();
BuddyManager.Buddies.Add(buddy);
foreach ((string text, string expected) in new[]
{
    ("fetch suit", "fetch suit"), ("bring back the suit", "fetch suit"),
    ("oxygen on", "unchanged"), ("switch on oxygen", "unchanged"), ("climate on", "unchanged"),
    ("turn on climate", "unchanged"), ("turn off oxygen", "unchanged"),
    ("no, sell", "unchanged"), ("not outside", "unchanged"), ("dont eat", "unchanged"),
    ("don't fetch suit", "unchanged"), ("do not turn on oxygen", "unchanged"),
    ("store items", "store"), ("put away the food", "store"), ("organise", "store"),
    ("come and tidy", "tidy"), ("come and eat", "snack"), ("come here", "follow"),
    ("FOLLOW me!", "follow"), ("stop following", "stay"), ("stop moving", "stay"),
    ("come and hide", "hide"), ("sell trash box", "sell"), ("go to the workshop", "goto"),
    ("take the suit off outside", "unsuit"), ("come inside", "inside"), ("go outside", "outside"),
    ("decide for yourself whether to follow", "decide"), ("be autonomous", "decide"),
    ("great", "unchanged"), ("display", "unchanged"), ("moneyless", "unchanged"),
    ("get some snacks", "snack"), ("start cleaning", "tidy"), ("keep following me", "follow"),
    ("hiding", "hide"), ("stopping", "stay"), ("stopped", "stay"),
    ("fetches", "unchanged"), ("trash boxes", "sell"), ("cleaned", "tidy"),
    ("concealed", "hide"), ("notes follow", "follow"), ("nobody follow", "follow"),
    ("cleanliness", "unchanged"), ("eatingly", "unchanged"),
    ("monkey", "unchanged"), ("don't follow me", "unchanged"),
    ("don\u2019t go outside", "unchanged"), ("do not sell", "unchanged"), ("never eat", "unchanged")
})
{
    buddy.LastOrder = "unchanged";
    BuddyDialogCommands.Run(buddy, text);
    Check(buddy.LastOrder == expected, text);
}
Check(BuddyDialogCommands.Run(buddy, "password 1423") == "code:1423", "password");
Check(BuddyDialogCommands.Run(buddy, "1423") == "code:1423", "bare code");
BuddyBehaviour other = new() { Name = "Buddy 2" };
BuddyBehaviour sleeping = new() { Asleep = true };
BuddyBehaviour dead = new() { IsDead = true };
BuddyManager.Buddies.AddRange([other, sleeping, dead]);
BuddyDialogCommands.Run(buddy, "everyone follow me");
Check(buddy.LastOrder == "follow" && other.LastOrder == "follow", "group command");
Check(sleeping.LastOrder == "unchanged" && dead.LastOrder == "unchanged", "group excludes unavailable buddies");
other.LastOrder = "unchanged";
BuddyDialogCommands.Run(buddy, "everyones follow");
Check(other.LastOrder == "unchanged", "group words stay exact");
buddy.SetState(BuddyMode.Route);
Check(buddy.ConversationStatus.Contains("place you chose", StringComparison.Ordinal), "goto status");
buddy.ReachDescription = "fetching a suit";
Check(buddy.ConversationStatus.Contains("fetching a suit", StringComparison.Ordinal), "errand status describes task");
buddy.ReachDescription = null;
Check(!BuddyDialogCommands.NamesFor(buddy).Contains("Oxygen on") &&
    !BuddyDialogCommands.NamesFor(buddy).Contains("Climate on"), "omit terminal shortcuts");
buddy.SetState(BuddyMode.Stay);
string status = BuddyDialogCommands.Run(buddy, "what are you doing?");
Check(status.Contains("wait here", StringComparison.Ordinal), "status reflects stay");
Check(buddy.LastOrder == "follow", "status does not issue an order");
buddy.SetState(BuddyMode.Stay, FearState.Scared);
Check(buddy.ConversationStatus.Contains("safe", StringComparison.Ordinal), "fear outranks stay greeting");
buddy.SetState(BuddyMode.Route, FearState.Alert);
Check(buddy.ConversationStatus.Contains("watch", StringComparison.Ordinal), "alert outranks task greeting");
Check(BuddyDialogCommands.Run(buddy, "everyone status").Contains("Buddy 2:", StringComparison.Ordinal), "group status identifies speakers");
Check(BuddyDialogCommands.NamesFor(buddy).First() == "Status", "inside status button");
Check(!BuddyDialogCommands.NamesFor(buddy).Contains("Unsuit"), "unsuited command list");
buddy.SuitSuited = true;
Check(BuddyDialogCommands.NamesFor(buddy).Contains("Unsuit"), "suited command list");
Check(BuddyDialogCommands.NamesFor(buddy).Contains("Fetch suit"), "inside task shortcuts");
buddy.IsOutside = true;
Check(!BuddyDialogCommands.NamesFor(buddy).Contains("Fetch suit"), "outside omits inside tasks");
Check(BuddyDialogCommands.NamesFor(buddy).First() == "Status", "outside status button");
buddy.Floating = true;
Check(BuddyDialogCommands.NamesFor(buddy).First() == "Status" && !BuddyDialogCommands.NamesFor(buddy).Contains("Goto"), "floating commands");
buddy.Hiding = true;
buddy.SetState(BuddyMode.Stay);
Check(buddy.ConversationStatus.Contains("hiding", StringComparison.Ordinal), "hidden status");
buddy.SetState(BuddyMode.Stay); buddy.IsDead = buddy.Asleep = buddy.Hiding = false;
buddy.storeOrder = true;
Check(buddy.ConversationStatus.Contains("storage", StringComparison.Ordinal), "waiting store order explains its actual work");
buddy.SetState(BuddyMode.Stay, FearState.Scared);
Check(buddy.ConversationStatus.Contains("safe", StringComparison.Ordinal), "danger outranks continuing storage status");
Console.WriteLine($"Passed {checks} command and status checks.");
