using System;
using System.Collections.Generic;
using System.Text;
using NPC.Core;
using NPC.Core.Navigation;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// The buddy console commands. `buddy` is help, then buddy_spawn, buddy_despawn, buddy_kill, buddy_list and one
    /// command per category. Registered through NPC.Core so no other mod's are overwritten. Per-buddy
    /// subcommands take @2, @name or @all. docs/reference.md#2-debug-commands
    /// </summary>
    internal static class BuddyConsole
    {
        private const string Owner = "YourBuddy";

        /// <summary>
        /// Gap between buddies in `buddy_spawn`'s row, and the most a point's floor may differ from the middle's.
        /// </summary>
        private const float SpawnSpacing = 0.8f;
        private const float SpawnSameDeck = 0.5f;

        /// <summary>
        /// One subcommand and its body, with the arguments and description the help shows.
        /// </summary>
        private sealed record Sub(string Args, string Help, Action<string[]> Run);

        /// <summary>
        /// A console command with its subcommands, or with its own usage line when it parses its arguments itself.
        /// </summary>
        private sealed record Category(string Name, string About, Dictionary<string, Sub> Subs, string? Usage = null);

        private static readonly List<Category> Categories = [];

        /// <summary>
        /// Commands of their own, used too often to sit under a category.
        /// </summary>
        private static readonly Dictionary<string, Sub> Singles = new()
        {
            ["buddy_spawn"] = new("[number]", "replace every buddy with that many (1 by default)", Spawn),
            ["buddy_despawn"] = PerBuddy("", "remove it", (b, _) =>
            {
                BuddyManager.Despawn(b);
                return b.Name + " despawned";
            }),
            ["buddy_kill"] = PerBuddy("[force]", "kill it, pushed away from you", Kill),
            ["buddy_list"] = new("", "the buddies, their numbers and names; * marks the one commands go to", List),
        };

        internal static void Register()
        {
            Add(new Category("buddy_order", "tell a buddy what to do; the dialog gives the same orders", new()
            {
                ["auto"] = new("[on|off]", "let the buddies decide for themselves, or only do as told",
                    args => Print(BuddyCommands.SetAutonomy(NpcConsole.Toggle(args, 0, YourBuddyPlugin.ConfigAutonomy.Value)))),
                ["follow"] = Order("follow you", BuddyCommands.Follow),
                ["stop"] = Order("drop what it is doing and follow you", BuddyCommands.Follow),
                ["wander"] = Order("wander about", BuddyCommands.Wander),
                ["stay"] = Order("hold its place until told otherwise", BuddyCommands.Stay),
                ["snack"] = Order("eat or drink something nearby now", BuddyCommands.Snack),
                ["store"] = Order("put a loose item aboard into storage", BuddyCommands.Store),
                ["tidy"] = Order("clear the rubbish nearby into a trash can", BuddyCommands.Tidy),
                ["sell"] = Order("sell the trash boxes nearby at a sell station", BuddyCommands.Sell),
                ["play"] = Order("play with something loose", BuddyCommands.Play),
                ["hide"] = Order("get into a closet or locker until the next order", BuddyCommands.Hide),
                ["outside"] = Order("suit up and wait in an airlock for you to cycle it out", BuddyCommands.GoOutside),
                ["inside"] = Order("come back in through the airlock you stand in", BuddyCommands.GoInside),
                ["fetchsuit"] = Order("fetch a suit you left on the docked station", BuddyCommands.FetchSuit),
                ["suit"] = PerBuddy("<on|off>", "put a spare suit on, or take the worn one off",
                    (b, rest) => rest.Length < 1 ? "Usage: buddy_order suit <on|off> [@who]" : BuddyCommands.Suit(b, rest[0])),
                ["terminal"] = PerBuddy("<oxygen|climate>", "switch that unit on now, if it is off",
                    (b, rest) => rest.Length < 1 ? "Usage: buddy_order terminal <oxygen|climate> [@who]" : BuddyCommands.Terminal(b, rest[0])),
                ["resources"] = new("[oxygen|fuel|energy|buying [on|off]] | [limit <amount>]",
                    "show or change the resource duties every buddy shares",
                    args => Print(BuddyCommands.Resources(Array.FindAll(args, a => !a.StartsWith("@"))))),
                ["password"] = new("<code>", "tell every buddy a door code",
                    args => Print(args.Length < 1 ? "Usage: buddy_order password <code>" : BuddyCommands.GivePassword(args[0]))),
            }));

            // docs/anomalies.md#7-testing
            Add(new Category("buddy_anomaly", "the anomalies: state, start one, end it", [], "[<kind>|end|roll|list|forget] [@who]"));

            Add(new Category("buddy_dev", "testing and debugging", new()
            {
                ["drain"] = new("<oxygen|fuel|energy>", "lower a ship resource to 20% for testing",
                    args => Print(BuddyResourceDebug.Drain(args))),
                ["goto"] = PerBuddy("<node>", "walk to that nav node",
                    (b, rest) => rest.Length < 1 || !int.TryParse(rest[0], out int node)
                        ? "Usage: buddy_dev goto <node> [@who]"
                        : BuddyCommands.GoToNode(b, node)),
                ["speed"] = PerBuddy("<m/s>", "set its walking speed (0.5-10)", Speed),
                ["mind"] = Order("what it is weighing and when it acts next", BuddyCommands.Mind),
                ["bout"] = Order("end the current follow or wander stretch now", BuddyCommands.EndBout),
                ["visuals"] = new("[on|off]", "path, probe and target markers", Visuals),
                ["sound"] = PerBuddy("[<category> [<n>]]", "play an anomaly sound from it; no category lists them", Sound),
                ["skin"] = PerBuddy("<name|default>", "wear skins/<name>.png; no name lists them", Skin),
                ["hud"] = new("[on|off]", "the status HUD", args =>
                {
                    YourBuddyPlugin.ConfigShowHud.Value = NpcConsole.Toggle(args, 0, YourBuddyPlugin.ConfigShowHud.Value);
                    Print("Status HUD: " + (YourBuddyPlugin.ConfigShowHud.Value ? "ON" : "OFF"));
                }),
            }));

            NpcConsole.Register(Owner, "buddy", _ => Print(Help()));
            foreach (KeyValuePair<string, Sub> single in Singles) NpcConsole.Register(Owner, single.Key, single.Value.Run);

            foreach (Category category in Categories)
            {
                Action<string[]> run = category.Name == "buddy_anomaly" ? Anomaly : args => Dispatch(category, args);
                NpcConsole.Register(Owner, category.Name, run);
            }
        }

        private static void Add(Category category) => Categories.Add(category);

        /// <summary>
        /// Every category and its subcommands, for `buddy`.
        /// </summary>
        private static string Help()
        {
            StringBuilder text = new("YourBuddy commands. A per-buddy one takes @2, @name or @all; without, it goes to the * buddy.");
            foreach (KeyValuePair<string, Sub> single in Singles) text.Append('\n').Append(Line(single.Key, single.Value).TrimStart());

            foreach (Category category in Categories)
            {
                text.Append('\n').Append(category.Name).Append(" - ").Append(category.About);
                if (category.Usage != null) text.Append("\n  ").Append(category.Usage);

                foreach (KeyValuePair<string, Sub> sub in category.Subs) text.Append('\n').Append(Line(sub.Key, sub.Value));
            }
            text.Append("\nThe nav graph, doors and AI switches are NPC.Core's: npc_node, npc_gates, node_editor, debug_level, ai_disable");
            return text.ToString();
        }

        private static string Line(string name, Sub sub) => "  " + name + (sub.Args.Length > 0 ? " " + sub.Args : "") + " - " + sub.Help;

        /// <summary>
        /// The first argument that is not an @who picks the subcommand, which gets the rest.
        /// </summary>
        private static void Dispatch(Category category, string[] args)
        {
            int at = Array.FindIndex(args, a => !a.StartsWith("@"));
            if (at >= 0 && category.Subs.TryGetValue(args[at].ToLowerInvariant(), out Sub? sub))
            {
                string[] rest = new string[args.Length - 1];
                Array.Copy(args, 0, rest, 0, at);
                Array.Copy(args, at + 1, rest, at, args.Length - at - 1);
                sub.Run(rest);
                return;
            }
            StringBuilder text = new(at >= 0 ? "No '" + args[at] + "' in " + category.Name + ". " : "");
            text.Append(category.Name).Append(" - ").Append(category.About);
            foreach (KeyValuePair<string, Sub> each in category.Subs) text.Append('\n').Append(Line(each.Key, each.Value));

            Print(text.ToString());
        }

        private static Sub Order(string help, Func<BuddyBehaviour, string> run) => PerBuddy("", help, (b, _) => run(b));

        private static Sub PerBuddy(string args, string help, Func<BuddyBehaviour, string[], string> run) =>
            new(args.Length > 0 ? args + " [@who]" : "[@who]", help, a => ForTargets(a, run));

        // Bodies

        /// <summary>
        /// Replaces every buddy with one, or with the given number. docs/reference.md#2-debug-commands
        /// </summary>
        private static void Spawn(string[] args)
        {
            Player? player = NpcPlayer.Pilot;
            if (player == null) return;

            int count = 1;
            if (args.Length > 0 && (!int.TryParse(args[0], out count) || count < 1))
            {
                Print("Usage: buddy_spawn [number] - replaces every buddy with that many (1 by default)");
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
            Print(spawned == 1 ? first.Name + " spawned in front of you" : spawned + " buddies spawned in front of you");
        }

        private static void List(string[] args)
        {
            if (BuddyManager.All.Count == 0)
            {
                Print("No buddy exists");
                return;
            }
            BuddyBehaviour? focus = BuddyManager.Focus;
            foreach (BuddyBehaviour buddy in BuddyManager.All)
            {
                if (buddy != null) Print(buddy.ListLine(buddy == focus));
            }
            Print("@2 or @name (without spaces) names one buddy in a command, @all every one; * is who commands go to");
        }

        private static string Kill(BuddyBehaviour buddy, string[] rest)
        {
            if (buddy.IsDead) return buddy.Name + " is already dead";

            float force = 6f;
            if (rest.Length > 0 && float.TryParse(rest[0], out float parsedForce)) force = Mathf.Clamp(parsedForce, 0f, 100f);

            Player? player = NpcPlayer.Pilot;
            Vector3 impulse = Vector3.up * 2f;
            if (player != null) impulse += player.Controller.CachedTransform.forward * force;

            buddy.Agent.Die(impulse);
            return buddy.Name + " killed";
        }

        /// <summary>
        /// One anomaly sound from the buddy's chest, as an anomaly plays it: docs/anomalies.md#7-testing
        /// </summary>
        private static string Sound(BuddyBehaviour buddy, string[] rest)
        {
            if (rest.Length < 1) return ScareSounds.List();

            if (!System.Enum.TryParse(rest[0], true, out ScareSound kind)) return "Categories: " + string.Join(", ", System.Enum.GetNames(typeof(ScareSound)));

            int? index = null;
            if (rest.Length >= 2)
            {
                if (!int.TryParse(rest[1], out int n)) return "Usage: buddy_dev sound [<category> [<n>]] [@who]";

                index = n;
            }
            return ScareSounds.Audition(kind, index, buddy.Agent.GroundPos(1.2f), buddy.transform);
        }

        private static string Skin(BuddyBehaviour buddy, string[] rest)
        {
            if (rest.Length >= 1) return BuddySkin.Apply(buddy, rest[0]);

            List<string> names = BuddySkin.Available();
            return "Usage: buddy_dev skin <name|default> [@who] - skins in " + BuddySkin.Folder + ": " +
                   (names.Count == 0 ? "none" : string.Join(", ", names));
        }

        private static string Speed(BuddyBehaviour buddy, string[] rest)
        {
            if (buddy.IsDead) return buddy.Name + " is dead";

            if (rest.Length < 1 || !float.TryParse(rest[0], out float speed)) return "Usage: buddy_dev speed <meters per second> [@who]";

            buddy.Agent.MoveSpeed = Mathf.Clamp(speed, 0.5f, 10f);
            return buddy.Name + " speed set to " + buddy.Agent.MoveSpeed;
        }

        private static void Visuals(string[] args)
        {
            if (BuddyManager.All.Count == 0)
            {
                Print("No buddy exists");
                return;
            }
            YourBuddyPlugin.ConfigDebugVisuals.Value = NpcConsole.Toggle(args, 0, YourBuddyPlugin.ConfigDebugVisuals.Value);
            foreach (BuddyBehaviour buddy in BuddyManager.All)
            {
                if (buddy != null) buddy.Agent.EnsureDebugVisuals(YourBuddyPlugin.ConfigDebugVisuals.Value);
            }
            Print("Debug visuals: " + (YourBuddyPlugin.ConfigDebugVisuals.Value ? "ON (yellow=path, green/red=probe, cyan=target)" : "OFF"));
        }

        /// <summary>
        /// docs/anomalies.md#7-testing
        /// </summary>
        private static void Anomaly(string[] args)
        {
            string first = args.Length > 0 && !args[0].StartsWith("@") ? args[0] : "";
            if (first.Length == 0)
            {
                Print("Anomalies: " + AnomalyDirector.Describe() + "\nThis save: " + AnomalyMemory.Describe() +
                      "\nSounds: " + ScareSounds.Describe());
                ForTargets(args, (b, _) => b.Name + ": " + b.DescribeAnomaly());
                return;
            }
            if (first.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (AnomalyInfo info in Anomalies.All)
                {
                    string? why = AnomalyDirector.WhyNot(info);
                    Print(info.Kind + " - " + info.Name + " (" + info.Severity.ToString().ToLowerInvariant() +
                          (why == null ? ")" : " - not now: " + why + ")"));
                }
                Print("Usage: buddy_anomaly [<kind>|end|roll|list|forget] [@who]");
                return;
            }
            if (first.Equals("roll", StringComparison.OrdinalIgnoreCase))
            {
                Print(AnomalyDirector.RollNow());
                return;
            }
            if (first.Equals("forget", StringComparison.OrdinalIgnoreCase))
            {
                AnomalyMemory.Load(null);
                Print("This save forgets every anomaly and line it had - they can all happen again");
                return;
            }
            ForTargets(args, (b, rest) => BuddyCommands.Anomaly(b, rest[0]));
        }

        /// <summary>
        /// Runs a per-buddy command for each buddy named by "@2", "@buddy2" or "@all" anywhere in `args`,
        /// else for the focused one. `run` gets the other arguments. Naming exactly one moves the focus.
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
        /// The spawn row, 2 m ahead and SpawnSpacing apart across the view. A point with no floor, or no
        /// knee-height walk to it from the middle on the same deck, falls back to the middle.
        /// </summary>
        private static Vector3 SpawnPoint(Transform view, int index, int count)
        {
            Vector3 middle = view.position + view.forward * 2f;
            float offset = (index - (count - 1) * 0.5f) * SpawnSpacing;
            if (Mathf.Abs(offset) < 0.01f) return middle;

            Vector3 point = middle + view.right * offset;
            return NavProbe.TryFloorHeight(point, out _) && NavProbe.WalkLos(middle, point, SpawnSameDeck) ? point : middle;
        }

        private static void Print(string text) => NpcConsole.Print(text);
    }
}
