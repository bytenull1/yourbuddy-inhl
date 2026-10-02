using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Interaction;

namespace YourBuddy
{
    /// <summary>
    /// One buddy's side of NPC.Core's talk window. The orders are in BuddyDialogCommands.
    /// See docs/dialog.md.
    /// </summary>
    internal sealed class BuddyConversation(BuddyBehaviour buddy) : INpcConversation
    {
        public INpc Npc => buddy.Agent;

        // Not while it is gone or frozen staring at you. docs/anomalies.md
        public bool CanTalk => YourBuddyPlugin.ConfigDialog.Value && !buddy.Asleep && !buddy.Hiding && !buddy.IgnoresYou;

        // Once, the wrong name. docs/anomalies.md#wrongname
        public string Title => buddy.TitleOverride ?? buddy.Name;

        public string Greeting => "Standing by.";

        public IReadOnlyList<string> Commands => BuddyDialogCommands.NamesFor(buddy);

        public string Answer(string text) => BuddyDialogCommands.Run(buddy, text);

        /// <summary>
        /// While open, untargeted console commands mean this buddy, and it holds still facing you.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (buddy == null) return;

            if (open) BuddyManager.SetFocus(buddy);
            buddy.InDialog = open;
            if (open) buddy.OnTalkOpened();
        }
    }
}
