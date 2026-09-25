using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Interaction;

namespace YourBuddy
{
    /// <summary>
    /// What the player says to one buddy through NPC.Core's talk window: the orders in
    /// BuddyDialogCommands. See docs/dialog.md.
    /// </summary>
    internal sealed class BuddyConversation(BuddyBehaviour buddy) : INpcConversation
    {
        public INpc Npc => buddy.Agent;

        public bool CanTalk => YourBuddyPlugin.ConfigDialog.Value && !buddy.Asleep && !buddy.Hiding;

        public string Title => buddy.Name;

        public string Greeting => "Standing by.";

        public IReadOnlyList<string> Commands => BuddyDialogCommands.Names;

        public string Answer(string text) => BuddyDialogCommands.Run(buddy, text);

        /// <summary>
        /// Open: console commands without a target now mean this buddy, and it holds still facing you.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (buddy == null) return;

            if (open) BuddyManager.SetFocus(buddy);
            buddy.InDialog = open;
        }
    }
}
