using NPC.Core.Agents;

namespace YourBuddy
{
    /// <summary>
    /// What every buddy's NpcAgent may do, read live from YourBuddy's config.
    /// </summary>
    internal sealed class BuddyAgentSettings : NpcAgentSettings
    {
        internal static readonly BuddyAgentSettings Instance = new();

        public override float MoveSpeed => YourBuddyPlugin.ConfigMoveSpeed.Value;
        public override bool CanOpenDoors => YourBuddyPlugin.ConfigAutoDoors.Value;
        public override bool KeepOutOfSpace => YourBuddyPlugin.ConfigPreventSpace.Value;
        public override bool Mortal => YourBuddyPlugin.ConfigMortal.Value;
        public override bool DebugVisuals => YourBuddyPlugin.ConfigDebugVisuals.Value;
    }
}
