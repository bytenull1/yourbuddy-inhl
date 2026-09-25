using System;
using System.Collections.Generic;
using NPC.Core;
using NPC.Core.Navigation;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddy console commands, registered through NPC.Core so no other mod's are overwritten.
    /// Per-buddy commands take @2, @name or @all. docs/reference.md#2-debug-commands
    /// </summary>
    internal static class BuddyConsole
    {
        private const string Owner = "YourBuddy";

        /// <summary>
        /// Gap between buddies in spawn_buddy's row, and the most a point's floor may differ from the middle's.
        /// </summary>
        private const float SpawnSpacing = 0.8f;
        private const float SpawnSameDeck = 0.5f;

        internal static void Register()
        {
            Dictionary<string, Action<string[]>> commands = [];

            // Replaces every buddy: one as before, or a number for that many. docs/reference.md#2-debug-commands
            commands["spawn_buddy"] = delegate (string[] args)
            {
                Player? player = GameManager.Instance != null && GameManager.Instance.PlayerShip != null ? GameManager.Instance.PlayerShip.Pilot : null;
                if (player == null) return;

                int count = 1;
                if (args.Length > 0 && (!int.TryParse(args[0], out count) || count < 1))
                {
                    Print("Usage: spawn_buddy [number] - replaces every buddy with that many (1 by default)");
                    return;
                }

                BuddyManager.DespawnAll();
                Transform view = player.Controller.CachedTransform;
                BuddyBehaviour? first = null;
                int spawned = 0;
                for (int i = 0; i < count; i++)
                {
                    BuddyBehaviour? buddy = YourBuddyPlugin.SpawnBuddy(SpawnPoint(view, i, count), view.rotation);
                    if (buddy == null) continue;

                    if (first == null) first = buddy;
                    spawned++;
                }
                if (first == null)
                {
                    Print("No buddy could be spawned - see the log");
                    return;
                }
                BuddyManager.SetFocus(first);
                Print(spawned == 1
                    ? first.Name + " spawned in front of you"
                    : spawned + " buddies spawned in front of you");
            };

            commands["buddy_despawn"] = args => ForTargets(args, (buddy, _) =>
            {
                BuddyManager.Despawn(buddy);
                return buddy.Name + " despawned";
            });

            commands["kill_buddy"] = args => ForTargets(args, (buddy, rest) =>
            {
                if (buddy.IsDead) return buddy.Name + " is already dead";

                float force = 6f;
                if (rest.Length > 0 && float.TryParse(rest[0], out float parsedForce))
                {
                    force = Mathf.Clamp(parsedForce, 0f, 100f);
                }
                Player? player = GameManager.Instance != null && GameManager.Instance.PlayerShip != null ? GameManager.Instance.PlayerShip.Pilot : null;
                Vector3 impulse = Vector3.up * 2f;
                if (player != null) impulse += player.Controller.CachedTransform.forward * force;

                buddy.Agent.Die(impulse);
                return buddy.Name + " killed";
            });

            commands["buddy_list"] = delegate
            {
                if (BuddyManager.All.Count == 0) { Print("No buddy exists"); return; }

                BuddyBehaviour? focus = BuddyManager.Focus;
                foreach (BuddyBehaviour buddy in BuddyManager.All)
                {
                    if (buddy != null) Print(buddy.ListLine(buddy == focus));
                }
                Print("@2 or @name (without spaces) names one buddy in a command, @all every one; * is who commands go to");
            };

            // The orders share their bodies with the dialog window - BuddyCommands.cs.
            commands["buddy_follow"] = args => ForTargets(args, (b, _) => BuddyCommands.Follow(b));
            commands["buddy_wander"] = args => ForTargets(args, (b, _) => BuddyCommands.Wander(b));
            commands["buddy_stay"] = args => ForTargets(args, (b, _) => BuddyCommands.Stay(b));
            commands["buddy_stop"] = args => ForTargets(args, (b, _) => BuddyCommands.Follow(b));
            commands["buddy_snack"] = args => ForTargets(args, (b, _) => BuddyCommands.Snack(b));
            commands["buddy_tidy"] = args => ForTargets(args, (b, _) => BuddyCommands.Tidy(b));
            commands["buddy_sell"] = args => ForTargets(args, (b, _) => BuddyCommands.Sell(b));
            commands["buddy_play"] = args => ForTargets(args, (b, _) => BuddyCommands.Play(b));
            commands["buddy_hide"] = args => ForTargets(args, (b, _) => BuddyCommands.Hide(b));
            commands["buddy_mind"] = args => ForTargets(args, (b, _) => BuddyCommands.Mind(b));
            commands["buddy_bout"] = args => ForTargets(args, (b, _) => BuddyCommands.EndBout(b));
            commands["buddy_terminal"] = args => ForTargets(args, (b, rest) =>
                rest.Length < 1
                    ? "Usage: buddy_terminal <oxygen|climate> [@who] - switch that unit on now, if it is off"
                    : BuddyCommands.Terminal(b, rest[0]));
            commands["buddy_auto"] = delegate (string[] args)
            {
                Print(BuddyCommands.SetAutonomy(Toggle(args, 0, YourBuddyPlugin.ConfigAutonomy.Value)));
            };

            commands["buddy_password"] = delegate (string[] args)
            {
                if (args.Length < 1)
                {
                    Print("Usage: buddy_password <code> - tell the buddy a door code");
                    return;
                }
                Print(BuddyCommands.GivePassword(args[0]));
            };

            commands["buddy_goto"] = args => ForTargets(args, (b, rest) =>
                rest.Length < 1 || !int.TryParse(rest[0], out int nodeIdx)
                    ? "Usage: buddy_goto <node_index> [@who] - walk a buddy to a specific node"
                    : BuddyCommands.GoToNode(b, nodeIdx));

            commands["buddy_speed"] = args => ForTargets(args, (b, rest) =>
            {
                if (b.IsDead) return b.Name + " is dead";
                if (rest.Length < 1 || !float.TryParse(rest[0], out float speed)) return "Usage: buddy_speed <meters per second> [@who]";

                b.Agent.MoveSpeed = Mathf.Clamp(speed, 0.5f, 10f);
                return b.Name + " speed set to " + b.Agent.MoveSpeed;
            });

            commands["buddy_debug"] = delegate (string[] args)
            {
                if (BuddyManager.All.Count == 0) { Print("No buddy exists"); return; }

                YourBuddyPlugin.ConfigDebugVisuals.Value = Toggle(args, 0, YourBuddyPlugin.ConfigDebugVisuals.Value);

                foreach (BuddyBehaviour buddy in BuddyManager.All)
                {
                    if (buddy != null) buddy.Agent.EnsureDebugVisuals(YourBuddyPlugin.ConfigDebugVisuals.Value);
                }
                Print("Debug visuals: " + (YourBuddyPlugin.ConfigDebugVisuals.Value ? "ON (yellow=path, green/red=probe, cyan=target)" : "OFF"));
            };

            commands["buddy_hud"] = delegate (string[] args)
            {
                if (args.Length > 0 && bool.TryParse(args[0], out bool enable))
                {
                    YourBuddyPlugin.ConfigShowHud.Value = enable;
                }
                else
                {
                    YourBuddyPlugin.ConfigShowHud.Value = !YourBuddyPlugin.ConfigShowHud.Value;
                }
                Print("Status HUD: " + (YourBuddyPlugin.ConfigShowHud.Value ? "ON" : "OFF"));
            };

            foreach (KeyValuePair<string, Action<string[]>> command in commands) NpcConsole.Register(Owner, command.Key, command.Value);

            // The names these had before they moved to NPC.Core.
            NpcConsole.Alias(Owner, "buddy_node", "npc_node");
            NpcConsole.Alias(Owner, "buddy_gates", "npc_gates");
        }

        /// <summary>
        /// Runs a per-buddy command for each buddy "@2", "@buddy2" or "@all" names, anywhere in `args`, or
        /// for the focused one; `run` gets the other arguments. Naming exactly one moves the focus to it.
        /// docs/reference.md#2-debug-commands
        /// </summary>
        private static void ForTargets(string[] args, Func<BuddyBehaviour, string[], string> run)
        {
            List<string> rest = [];
            List<BuddyBehaviour> targets = [];
            bool named = false;
            foreach (string arg in args)
            {
                if (arg.Length < 2 || arg[0] != '@')
                {
                    rest.Add(arg);
                    continue;
                }
                named = true;
                string who = arg.Substring(1);
                if (who.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (BuddyBehaviour buddy in BuddyManager.All)
                    {
                        if (buddy != null && !targets.Contains(buddy)) targets.Add(buddy);
                    }
                    continue;
                }
                BuddyBehaviour? found = int.TryParse(who, out int number) ? BuddyManager.ByNumber(number) : BuddyManager.ByName(who);
                if (found == null)
                {
                    Print("No buddy called '" + arg + "' - buddy_list shows them");
                    return;
                }
                if (!targets.Contains(found)) targets.Add(found);
            }
            if (!named)
            {
                BuddyBehaviour? focus = BuddyManager.Focus;
                if (focus != null) targets.Add(focus);
            }
            if (targets.Count == 0)
            {
                Print("No buddy exists");
                return;
            }
            if (named && targets.Count == 1) BuddyManager.SetFocus(targets[0]);

            string[] others = [.. rest];
            foreach (BuddyBehaviour buddy in targets)
            {
                using NpcRegistry.ActingScope _ = NpcRegistry.Acting(buddy.Agent);
                Print(run(buddy, others));
            }
        }

        /// <summary>
        /// Spawn_buddy's row: 2 m ahead, SpawnSpacing apart across the view. A point with no floor, or no
        /// knee-height walk to it on the same deck from the middle one, falls back to the middle.
        /// </summary>
        private static Vector3 SpawnPoint(Transform view, int index, int count)
        {
            Vector3 middle = view.position + view.forward * 2f;
            float offset = (index - (count - 1) * 0.5f) * SpawnSpacing;
            if (Mathf.Abs(offset) < 0.01f) return middle;

            Vector3 point = middle + view.right * offset;
            return NavProbe.TryFloorHeight(point, out _) && NavProbe.WalkLos(middle, point, SpawnSameDeck) ? point : middle;
        }

        /// <summary>
        /// "on"/"off"/"true"/"false" at args[index], or a flip when it is absent.
        /// </summary>
        private static bool Toggle(string[] args, int index, bool current)
        {
            if (args.Length <= index) return !current;

            string value = args[index];
            if (bool.TryParse(value, out bool parsed)) return parsed;

            if (value.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;

            if (value.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;

            return !current;
        }

        private static void Print(string text) => NpcConsole.Print(text);
    }
}
