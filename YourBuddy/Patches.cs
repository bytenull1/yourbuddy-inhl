using BepInEx.Configuration;
using HarmonyLib;
using Space;
using Space.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
// ReSharper disable InconsistentNaming - this breaks Harmony patches.

namespace YourBuddy
{
    /// <summary>
    /// Harmony patches for integrating YourBuddy with the game, one nested class per feature so
    /// a game update that breaks one target disables only that feature (YourBuddyPlugin.ApplyPatches).
    /// </summary>
    public static class Patches
    {
        /// <summary>
        /// How near a doorway the player has to be for a door closing there to be
        /// allowed to decide which room they are in.
        /// </summary>
        private const float PlayerAtDoorwayRadius = 2.5f;
        /// <summary>
        /// Gap between buddies in spawn_buddy's row, and the most a point's floor may differ from the middle's.
        /// </summary>
        private const float SpawnSpacing = 0.8f;
        private const float SpawnSameDeck = 0.5f;

        [HarmonyPatch, Description("the manager tick: HUD state, save restore, nav-graph autosave")]
        internal static class Tick
        {
            /// <summary>
            /// Drives BuddyManager.Tick from the game's own FixedUpdate loop so the HUD
            /// state, nav-graph autosave and save restoration always run while a game
            /// scene is loaded.
            /// </summary>
            [HarmonyPatch(typeof(GameManager), "FixedUpdate")]
            [HarmonyPostfix]
            public static void GameManager_FixedUpdate_Postfix()
            {
                BuddyManager.Tick();
            }
        }

        [HarmonyPatch, Description("the buddy waking in a cryo capsule on a new game")]
        internal static class NewGame
        {
            /// <summary>
            /// Before GameManager.Start puts the player in a cryo pod and advances worldTime,
            /// which is the game's own new-game test.
            /// </summary>
            [HarmonyPatch(typeof(GameManager), "Start")]
            [HarmonyPrefix]
            public static void GameManager_Start_Prefix()
            {
                SaveData? save = SceneLoader.Instance != null ? SceneLoader.Instance.SaveData : null;
                BuddyBehaviour.ResetWorldCaches();
                BuddyCryoSpawn.OnGameStarting(save is { worldTime: 0 });
            }
        }

        [HarmonyPatch, Description("the buddy console commands")]
        internal static class ConsoleCommands
        {
            /// <summary>
            /// Adds the YourBuddy console commands when ConsoleMenu initializes.
            /// </summary>
            [HarmonyPatch(typeof(ConsoleMenu), "Init")]
            [HarmonyPostfix]
            public static void ConsoleMenu_Init_Postfix(ConsoleMenu __instance)
            {
                if (__instance == null) return;

                Dictionary<string, Action<string[]>>? commands = GameInternals.ConsoleMenuAccess.GetCommands(__instance);
                if (commands == null) return;

                // Snapshot the game's own commands so the summary below lists only ours.
                HashSet<string> vanillaCommands = [.. commands.Keys];

                ConsoleMenu console = __instance;

                // Replaces every buddy: one as before, or a number for that many. docs/reference.md#2-debug-commands
                commands["spawn_buddy"] = delegate (string[] args)
                {
                    Player? player = GameManager.Instance != null && GameManager.Instance.PlayerShip != null ? GameManager.Instance.PlayerShip.Pilot : null;
                    if (player == null) return;

                    int count = 1;
                    if (args.Length > 0 && (!int.TryParse(args[0], out count) || count < 1))
                    {
                        Print(console, "Usage: spawn_buddy [number] - replaces every buddy with that many (1 by default)");
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
                        Print(console, "No buddy could be spawned - see the log");
                        return;
                    }
                    BuddyManager.SetFocus(first);
                    Print(console, spawned == 1
                        ? first.Name + " spawned in front of you"
                        : spawned + " buddies spawned in front of you");
                };

                commands["buddy_despawn"] = args => ForTargets(console, args, (buddy, _) =>
                {
                    BuddyManager.Despawn(buddy);
                    return buddy.Name + " despawned";
                });

                commands["kill_buddy"] = args => ForTargets(console, args, (buddy, rest) =>
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

                    buddy.Die(impulse);
                    return buddy.Name + " killed";
                });

                commands["buddy_list"] = delegate
                {
                    if (BuddyManager.All.Count == 0) { Print(console, "No buddy exists"); return; }

                    BuddyBehaviour? focus = BuddyManager.Focus;
                    foreach (BuddyBehaviour buddy in BuddyManager.All)
                    {
                        if (buddy != null) Print(console, buddy.ListLine(buddy == focus));
                    }
                    Print(console, "@2 or @name (without spaces) names one buddy in a command, @all every one; * is who commands go to");
                };

                // The orders share their bodies with the dialog window - BuddyCommands.cs.
                commands["buddy_follow"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Follow(b));
                commands["buddy_wander"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Wander(b));
                commands["buddy_stay"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Stay(b));
                commands["buddy_stop"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Follow(b));
                commands["buddy_snack"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Snack(b));
                commands["buddy_tidy"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Tidy(b));
                commands["buddy_sell"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Sell(b));
                commands["buddy_play"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Play(b));
                commands["buddy_hide"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Hide(b));
                commands["buddy_mind"] = args => ForTargets(console, args, (b, _) => BuddyCommands.Mind(b));
                commands["buddy_bout"] = args => ForTargets(console, args, (b, _) => BuddyCommands.EndBout(b));
                commands["buddy_terminal"] = args => ForTargets(console, args, (b, rest) =>
                    rest.Length < 1
                        ? "Usage: buddy_terminal <oxygen|climate> [@who] - switch that unit on now, if it is off"
                        : BuddyCommands.Terminal(b, rest[0]));
                commands["buddy_auto"] = delegate (string[] args)
                {
                    Print(console, BuddyCommands.SetAutonomy(Toggle(args, 0, YourBuddyPlugin.ConfigAutonomy.Value)));
                };

                commands["buddy_password"] = delegate (string[] args)
                {
                    if (args.Length < 1)
                    {
                        Print(console, "Usage: buddy_password <code> - tell the buddy a door code");
                        return;
                    }
                    Print(console, BuddyCommands.GivePassword(args[0]));
                };

                commands["buddy_goto"] = args => ForTargets(console, args, (b, rest) =>
                    rest.Length < 1 || !int.TryParse(rest[0], out int nodeIdx)
                        ? "Usage: buddy_goto <node_index> [@who] - walk a buddy to a specific node"
                        : BuddyCommands.GoToNode(b, nodeIdx));

                commands["buddy_speed"] = args => ForTargets(console, args, (b, rest) =>
                {
                    if (b.IsDead) return b.Name + " is dead";
                    if (rest.Length < 1 || !float.TryParse(rest[0], out float speed)) return "Usage: buddy_speed <meters per second> [@who]";

                    b.MoveSpeed = Mathf.Clamp(speed, 0.5f, 10f);
                    return b.Name + " speed set to " + b.MoveSpeed;
                });

                commands["buddy_debug"] = delegate (string[] args)
                {
                    if (BuddyManager.All.Count == 0) { Print(console, "No buddy exists"); return; }

                    YourBuddyPlugin.ConfigDebugVisuals.Value = Toggle(args, 0, YourBuddyPlugin.ConfigDebugVisuals.Value);

                    foreach (BuddyBehaviour buddy in BuddyManager.All)
                    {
                        if (buddy != null) buddy.EnsureDebugVisuals(YourBuddyPlugin.ConfigDebugVisuals.Value);
                    }
                    Print(console, "Debug visuals: " + (YourBuddyPlugin.ConfigDebugVisuals.Value ? "ON (yellow=path, green/red=probe, cyan=target)" : "OFF"));
                };

                commands["buddy_gates"] = delegate
                {
                    // What the doorway carve-out decided for every gate, and from what.
                    // docs/probes.md
                    foreach (string line in NavProbe.DescribeGates()) Print(console, line);
                };

                commands["buddy_node"] = delegate (string[] args)
                {
                    string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                    if (sub == "add")
                    {
                        Player? nodePlayer = GameManager.Instance != null && GameManager.Instance.PlayerShip != null ? GameManager.Instance.PlayerShip.Pilot : null;
                        if (nodePlayer == null || nodePlayer.Controller == null) { Print(console, "No player"); return; }
                        BuddyNodeGraph.AddNode(nodePlayer.Controller.CachedTransform.position);
                        Print(console, "Node added at your position (total " + BuddyNodeGraph.NodeCount + ")");
                    }
                    else if (sub == "count")
                    {
                        Print(console, "Nav nodes: " + BuddyNodeGraph.DescribeNodes());
                    }
                    else if (sub == "clear")
                    {
                        BuddyNodeGraph.Clear();
                        BuddyNodeGraph.Save();
                        Print(console, "Nav graph cleared");
                    }
                    else if (sub == "save")
                    {
                        BuddyNodeGraph.Save();
                        Print(console, "Nav graph saved (" + BuddyNodeGraph.NodeCount + " nodes)");
                    }
                    else if (sub == "list")
                    {
                        List<Vector3> nodes = BuddyNodeGraph.GetAllNodesWorld();
                        Print(console, "Nav nodes (" + nodes.Count + "):");
                        for (int i = 0; i < nodes.Count; i++) Print(console, "  #" + i + ": " + nodes[i].ToString("0.00"));
                    }
                    else if (sub == "remove" || sub == "delete")
                    {
                        if (args.Length < 2 || !int.TryParse(args[1], out int idx))
                        {
                            Print(console, "Usage: buddy_node remove <index>");
                            return;
                        }
                        Print(console, BuddyNodeGraph.RemoveNode(idx) ? "Node #" + idx + " removed" : "Invalid index");
                    }
                    else if (sub == "link")
                    {
                        // buddy_node link <a> <b>
                        if (args.Length < 3 || !int.TryParse(args[1], out int aIdx) || !int.TryParse(args[2], out int bIdx))
                        {
                            Print(console, "Usage: buddy_node link <a> <b>");
                            return;
                        }
                        if (aIdx < 0 || aIdx >= BuddyNodeGraph.NodeCount || bIdx < 0 || bIdx >= BuddyNodeGraph.NodeCount || aIdx == bIdx)
                        {
                            Print(console, "Invalid index");
                            return;
                        }
                        Print(console, BuddyNodeGraph.ToggleLink(aIdx, bIdx)
                            ? "Link created: #" + aIdx + " <-> #" + bIdx
                            : "Link removed: #" + aIdx + " <-> #" + bIdx);
                    }
                    else if (sub == "unlink")
                    {
                        if (args.Length < 2 || !int.TryParse(args[1], out int uIdx))
                        {
                            Print(console, "Usage: buddy_node unlink <index> - remove every manual link of a node");
                            return;
                        }
                        if (uIdx < 0 || uIdx >= BuddyNodeGraph.NodeCount)
                        {
                            Print(console, "Invalid index");
                            return;
                        }
                        Print(console, "Node #" + uIdx + ": " + BuddyNodeGraph.ClearLinks(uIdx) + " link(s) removed");
                    }
                    else if (sub == "type")
                    {
                        if (args.Length < 3 || !int.TryParse(args[1], out int tIdx))
                        {
                            Print(console, "Usage: buddy_node type <index> <ground|stair>");
                            return;
                        }
                        BuddyNodeGraph.NodeType type = args[2].ToLowerInvariant() switch
                        {
                            "ground" => BuddyNodeGraph.NodeType.Ground,
                            "stair" or "stairs" => BuddyNodeGraph.NodeType.Stair,
                            _ => (BuddyNodeGraph.NodeType)(-1)
                        };
                        if ((int)type < 0)
                        {
                            Print(console, "Unknown type '" + args[2] + "' (ground|stair)");
                            return;
                        }
                        if (tIdx < 0 || tIdx >= BuddyNodeGraph.NodeCount)
                        {
                            Print(console, "Invalid index");
                            return;
                        }
                        BuddyNodeGraph.SetNodeType(tIdx, type);
                        Print(console, "Node #" + tIdx + " is now " + type);
                    }
                    else if (sub == "bundled")
                    {
                        Print(console, "Nav graph sources: " + BuddyNodeGraph.DescribeBundle());
                    }
                    else if (sub == "unfork")
                    {
                        if (args.Length < 2)
                        {
                            Print(console, "Usage: buddy_node unfork <owner> - drop your nodes for that ship or " +
                                           "station and go back to the ones shipped with the mod");
                            return;
                        }
                        Print(console, BuddyNodeGraph.UnforkOwner(args[1])
                            ? "'" + args[1] + "' handed back to the bundled graph - restart to load it"
                            : "'" + args[1] + "' is not one of yours (see 'buddy_node bundled')");
                    }
                    else
                    {
                        Print(console, "Usage: buddy_node add | count | clear | save | list | remove <index> | " +
                                       "link <a> <b> | unlink <index> | type <index> <ground|stair> | " +
                                       "bundled | unfork <owner>");
                    }
                };

                commands["node_editor"] = delegate
                {
                    BuddyNodeEditor editor = YourBuddyPlugin.NodeEditor;
                    if (editor == null) { Print(console, "Node editor not available"); return; }
                    editor.Active = !editor.Active;
                    Print(console, "Node editor: " + (editor.Active ? "ON" : "OFF") +
                                  " - " + KeyName(YourBuddyPlugin.ConfigEditorToggleKey) + "=toggle, " +
                                  KeyName(YourBuddyPlugin.ConfigEditorPlaceKey) + "/Numpad0=place, " +
                                  KeyName(YourBuddyPlugin.ConfigEditorDeleteKey) + "=remove, " +
                                  KeyName(YourBuddyPlugin.ConfigEditorLinksKey) + "=connections, " +
                                  KeyName(YourBuddyPlugin.ConfigEditorForceLinkKey) + "=link (2 presses), " +
                                  KeyName(YourBuddyPlugin.ConfigEditorClearLinksKey) + "=clear the node's links, " +
                                  KeyName(YourBuddyPlugin.ConfigEditorTypeKey) + "=node type, " +
                                  KeyName(YourBuddyPlugin.ConfigEditorSaveKey) + "=save");
                };


                commands["debug_level"] = delegate (string[] args)
                {
                    if (args.Length < 1 || !int.TryParse(args[0], out int level))
                    {
                        Print(console, "Usage: debug_level <0-3> (current: " + YourBuddyPlugin.ConfigDebugLevel.Value + ")");
                        return;
                    }
                    YourBuddyPlugin.ConfigDebugLevel.Value = Mathf.Clamp(level, 0, 3);
                    Print(console, "Debug level: " + YourBuddyPlugin.ConfigDebugLevel.Value +
                        (YourBuddyPlugin.ConfigDebugLevel.Value == 0 ? " (quiet)" :
                         YourBuddyPlugin.ConfigDebugLevel.Value == 1 ? " (normal)" :
                         YourBuddyPlugin.ConfigDebugLevel.Value == 2 ? " (thinking)" : " (obstacle)"));
                };

                // Debug only, and deliberately not persisted: docs/reference.md
                commands["ai_disable"] = delegate (string[] args)
                {
                    string target = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
                    if (target != "buddy" && target != "monster" && target != "all")
                    {
                        Print(console, "Usage: ai_disable [buddy|monster|all] [on|off] - now: " + AiDebug.Describe());
                        return;
                    }

                    bool wasOn = target == "monster" ? AiDebug.MonsterDisabled
                        : target == "buddy" ? AiDebug.BuddyDisabled
                        : AiDebug.BuddyDisabled && AiDebug.MonsterDisabled;
                    bool on = Toggle(args, 1, wasOn);

                    if (target != "monster") AiDebug.BuddyDisabled = on;

                    if (target != "buddy") AiDebug.MonsterDisabled = on;

                    AiDebug.Apply();
                    Print(console, "AI: " + AiDebug.Describe());
                };

                commands["ai_notarget"] = delegate (string[] args)
                {
                    AiDebug.NoTarget = Toggle(args, 0, AiDebug.NoTarget);
                    AiDebug.Apply();
                    Print(console, "AI: " + AiDebug.Describe());
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
                    Print(console, "Status HUD: " + (YourBuddyPlugin.ConfigShowHud.Value ? "ON" : "OFF"));
                };

                // List only the commands this mod added (dictionary-derived, so never stale).
                List<string> registered = [];
                foreach (KeyValuePair<string, Action<string[]>> entry in commands)
                {
                    if (!vanillaCommands.Contains(entry.Key)) registered.Add(entry.Key);
                }
                registered.Sort(StringComparer.OrdinalIgnoreCase);
                Print(console, "YourBuddy commands registered (" + registered.Count + "): " + string.Join(", ", [.. registered]));
            }
        }

        [HarmonyPatch, Description("a hiding spot the buddy is inside")]
        internal static class Hiding
        {
            /// <summary>
            /// The buddy is not a Player, so the game would happily mount you into the closet it is
            /// already in. docs/fear.md §6
            /// </summary>
            [HarmonyPatch(typeof(HidingSpot), nameof(HidingSpot.Interact))]
            [HarmonyPrefix]
            public static bool HidingSpot_Interact_Prefix(HidingSpot __instance)
            {
                if (!BuddyManager.BuddyIsHidingIn(__instance)) return true;

                if (MessagePopUp.Instance != null) MessagePopUp.Instance.Show("Occupied");

                return false;
            }
        }

        [HarmonyPatch, Description("the lifecare terminal integration")]
        internal static class Lifecare
        {
            /// <summary>
            /// Works around a game bug that hides the player's own icon (docs/lifecare.md
            /// §3). May only ever turn the icon on, and only from a floor probe:
            /// docs/invariants.md#player-icon-fix-is-one-directional
            /// </summary>
            [HarmonyPatch(typeof(LifecareDisplay), nameof(LifecareDisplay.UpdatePlayerIcon))]
            [HarmonyPrefix]
            public static void LifecareDisplay_UpdatePlayerIcon_Prefix(ref bool enabled)
            {
                if (enabled)
                {
                    return; // the game already counts them; never take an icon away
                }

                GameManager gm = GameManager.Instance;
                Player? pilot = gm != null && gm.PlayerShip != null ? gm.PlayerShip.Pilot : null;
                if (pilot == null || pilot.Controller == null) return;

                if (BuddyManager.FloorOwner(pilot.Controller.CachedTransform.position) ==
                    BuddyManager.FloorOwnership.PlayerShip)
                {
                    enabled = true;
                }
            }

            /// <summary>
            /// The lifeforms counter on the lifecare terminal: adds the buddy to the tally.
            /// </summary>
            [HarmonyPatch(typeof(LifecareDisplay), "UpdateLifeformsCount")]
            [HarmonyPostfix]
            public static void LifecareDisplay_UpdateLifeformsCount_Postfix(LifecareDisplay __instance)
            {
                BuddyManager.RecountLifeforms(__instance);
            }
        }

        [HarmonyPatch, Description("door closes: player room tracking and blocked-close retries")]
        internal static class Doors
        {
            /// <summary>
            /// EntryDetector re-files the player whenever a door it knows closes, with no
            /// proximity check. For a close the buddy issued elsewhere, hide the remembered
            /// player. docs/invariants.md#buddy-closes-must-not-move-the-player
            /// </summary>
            [HarmonyPatch(typeof(EntryDetector), "DoorCheckForEnter")]
            [HarmonyPrefix]
            public static void EntryDetector_DoorCheckForEnter_Prefix(EntryDetector __instance, out Player? __state)
            {
                __state = null;

                Gate? gate = GameInternals.EntryDetectorAccess.GetDoor(__instance);
                if (gate == null || !BuddyManager.GateWasClosedByBuddy(gate, out _)) return;

                Player? player = GameInternals.EntryDetectorAccess.GetCurrentPlayer(__instance);
                if (player == null || player.Controller == null) return;

                // The player really is at this doorway: let the game do its normal thing.
                if (PlayerAtDoorway(player, gate)) return;

                if (GameInternals.EntryDetectorAccess.SetCurrentPlayer(__instance, null)) __state = player;
            }

            [HarmonyPatch(typeof(EntryDetector), "DoorCheckForEnter")]
            [HarmonyPostfix]
            public static void EntryDetector_DoorCheckForEnter_Postfix(EntryDetector __instance, Player? __state)
            {
                if (__state != null) GameInternals.EntryDetectorAccess.SetCurrentPlayer(__instance, __state);

                Gate? gate = GameInternals.EntryDetectorAccess.GetDoor(__instance);
                if (gate == null || !BuddyManager.GateWasClosedByBuddy(gate, out BuddyBehaviour? closer)) return;

                // At the doorway the game has just switched rooms for the player. Anywhere else it switched
                // nothing, so the buddy puts back what it loaded: docs/invariants.md#the-buddy-loads-rooms-by-the-door-rule
                Player? player = GameInternals.EntryDetectorAccess.GetCurrentPlayer(__instance);
                if (player != null && player.Controller != null && PlayerAtDoorway(player, gate)) return;

                // The closer's own side test decides; every other buddy keeps its room through ReasonToKeepLoaded.
                if (closer == null) return;

                using BuddyManager.ActingScope _ = BuddyManager.Acting(closer);
                closer.ReleaseRoomsAt(__instance);
            }

            private static bool PlayerAtDoorway(Player player, Gate gate) =>
                (player.Controller.CachedTransform.position - gate.transform.position).sqrMagnitude <=
                PlayerAtDoorwayRadius * PlayerAtDoorwayRadius;

            /// <summary>
            /// A gate's AntiCrasher just undid a close, and nothing in the game will ever
            /// retry it. If the buddy is standing in it, the buddy owns the fix.
            /// See docs/doors.md.
            /// </summary>
            [HarmonyPatch(typeof(Gate), nameof(Gate.FailClose))]
            [HarmonyPostfix]
            public static void Gate_FailClose_Postfix(Gate __instance)
            {
                // Each ignores a gate it is not standing in, so only the one in the way takes the close over.
                foreach (BuddyBehaviour buddy in BuddyManager.Snapshot())
                {
                    if (buddy == null) continue;

                    using BuddyManager.ActingScope _ = BuddyManager.Acting(buddy);
                    buddy.NoteCloseFailed(__instance);
                }
            }
        }

        [HarmonyPatch, Description("the buddy save sidecar")]
        internal static class Save
        {
            /// <summary>
            /// Writes the buddy state sidecar file every time the game writes a save file.
            /// </summary>
            [HarmonyPatch(typeof(SaveParser), "WriteSaveFile")]
            [HarmonyPostfix]
            public static void SaveParser_WriteSaveFile_Postfix(SaveData? save)
            {
                if (save == null) return;

                BuddyManager.WriteSidecar(save.fileName);
            }

            /// <summary>
            /// Deletes the '.buddy' sidecar with the save it belongs to. This is the only
            /// single-save delete path, and it runs after the player confirms - the call
            /// site is inside ConfirmationPopUp's callback, so patching SaveMenu instead
            /// would delete on cancel too.
            /// </summary>
            [HarmonyPatch(typeof(SaveParser), nameof(SaveParser.DeleteSaveFile))]
            [HarmonyPostfix]
            public static void SaveParser_DeleteSaveFile_Postfix(string fileName)
            {
                BuddyManager.DeleteSidecar(fileName);
            }

            /// <summary>
            /// The bulk wipe. Scene-wired with no C# caller, like Gate.InvokeOpen.
            /// </summary>
            [HarmonyPatch(typeof(SaveParser), nameof(SaveParser.ClearSaveFiles))]
            [HarmonyPostfix]
            public static void SaveParser_ClearSaveFiles_Postfix()
            {
                BuddyManager.DeleteAllSidecars();
            }

            /// <summary>
            /// Self-healing sweep for sidecars whose save vanished by some other route.
            /// Runs on menu open and after every write, and touches nothing else.
            /// </summary>
            [HarmonyPatch(typeof(SaveParser), nameof(SaveParser.SyncSavePreviews))]
            [HarmonyPostfix]
            public static void SaveParser_SyncSavePreviews_Postfix()
            {
                BuddyManager.PruneOrphanSidecars();
            }
        }

        [HarmonyPatch, Description("restoring the buddy when a save loads")]
        internal static class Load
        {
            /// <summary>
            /// Arms a pending buddy spawn when the player loads a save file.
            /// </summary>
            [HarmonyPatch(typeof(SceneLoader), "LoadGame")]
            [HarmonyPostfix]
            public static void SceneLoader_LoadGame_Postfix(string saveName)
            {
                // New scene: the 5 s gate caches may reference destroyed gates.
                NavProbe.InvalidateGates();
                BuddyBehaviour.ResetWorldCaches();
                AiDebug.Reset();
                YourBuddyPlugin.Log.LogInfo($"[mgr] LoadGame('{saveName}') detected");
                if (SceneLoader.Instance == null) return;

                if (SceneLoader.Instance.SaveData == null)
                {
                    YourBuddyPlugin.Log.LogWarning("[mgr] SaveData is null after load - save failed to load");
                    return;
                }
                if (SceneLoader.Instance.SaveData.fileName != saveName)
                {
                    YourBuddyPlugin.Log.LogWarning($"[mgr] Save name mismatch: requested '{saveName}', data holds '{SceneLoader.Instance.SaveData.fileName}'");
                    return;
                }
                BuddyManager.ArmPendingSpawn(saveName);
            }
        }

        [HarmonyPatch, Description("the ai_notarget debug command")]
        internal static class MonsterDebug
        {
            /// <summary>
            /// ai_notarget: the monster's idle AI starts its chase routine from this
            /// callback, so suppressing it is half of "it does not see me".
            /// </summary>
            [HarmonyPatch(typeof(BreathlessController), "PlayerEntered")]
            [HarmonyPrefix]
            public static bool BreathlessController_PlayerEntered_Prefix()
            {
                return !AiDebug.PlayerIgnored;
            }

            /// <summary>
            /// The other half, and the one that actually kills. Both AI components grab and
            /// kill from an ObjectDetector UnityEvent, which fires whether or not the
            /// component listening to it is enabled - so disabling the component alone left
            /// the kill trigger live. Only a player detection is suppressed: the item branch
            /// still works, and the buddy never reaches here (it has no Player component),
            /// which is what keeps it huntable under ai_notarget.
            /// </summary>
            [HarmonyPatch(typeof(BreathlessController), "DetectItem")]
            [HarmonyPrefix]
            public static bool BreathlessController_DetectItem_Prefix(GameObject detectedObject)
            {
                return !IgnoredTarget(detectedObject);
            }

            [HarmonyPatch(typeof(BreathlessAggressor), "DetectItem")]
            [HarmonyPrefix]
            public static bool BreathlessAggressor_DetectItem_Prefix(GameObject detectedObject)
            {
                return !IgnoredTarget(detectedObject);
            }

            private static bool IgnoredTarget(GameObject detectedObject)
            {
                return AiDebug.PlayerIgnored && detectedObject != null &&
                       detectedObject.GetComponent<Player>() != null;
            }
        }

        [HarmonyPatch, Description("the buddy undocking with the ship")]
        internal static class Docking
        {
            /// <summary>
            /// The buddies in the undocking collar, judged before the station's content (and every
            /// collider in it) is switched off. Null when nothing was docked: Undock is then a no-op, and
            /// moving a buddy for it would be a teleport out of nowhere.
            /// </summary>
            [HarmonyPatch(typeof(Docker), nameof(Docker.Undock))]
            [HarmonyPrefix]
            public static void Docker_Undock_Prefix(Docker __instance, out List<BuddyBehaviour>? __state)
            {
                __state = null;
                if (__instance == null || !__instance.IsDocked) return;

                __state = [];
                foreach (BuddyBehaviour buddy in BuddyManager.All)
                {
                    if (buddy != null && buddy.gameObject.activeInHierarchy &&
                        NavProbe.TryFloorCollider(buddy.transform.position, out Collider? floor) && floor != null &&
                        floor.GetComponentInParent<Docker>() == __instance)
                    {
                        __state.Add(buddy);
                    }
                }
            }

            /// <summary>
            /// A buddy with no floor of its own, or in the collar, goes aboard as the game's own NPC
            /// does (decompiled/Docker.cs:111); one elsewhere on the station is parked with it.
            /// docs/invariants.md#the-buddy-rides-its-own-floor
            /// </summary>
            [HarmonyPatch(typeof(Docker), nameof(Docker.Undock))]
            [HarmonyPostfix]
            public static void Docker_Undock_Postfix(SpaceShip ship, List<BuddyBehaviour>? __state)
            {
                if (__state == null || ship == null || ship.Airlock == null) return;

                foreach (BuddyBehaviour buddy in BuddyManager.Snapshot())
                {
                    if (buddy == null || buddy.IsDead) continue;

                    if (!__state.Contains(buddy))
                    {
                        if (!buddy.gameObject.activeInHierarchy) continue;

                        BuddyManager.FloorOwner(buddy.transform.position, out string? owner, out _);
                        if (owner != null) continue;
                    }
                    using BuddyManager.ActingScope scope = BuddyManager.Acting(buddy);
                    buddy.PullAboard(ship.Airlock.transform.position);
                }
            }
        }

        [HarmonyPatch, Description("the buddy riding a ship rebuild")]
        internal static class ShipRebuild
        {
            /// <summary>
            /// Where a buddy aboard stood before the player ship is rebuilt, judged while every room
            /// is still where it was. Room is null on a floor of no room, or with no floor at all.
            /// </summary>
            public struct ShipRebuildState
            {
                public BuddyBehaviour Buddy;
                public int LayoutBefore;
                public bool HadFloor;
                public CustomRoom? Room;
                public Vector3 RoomAt;
            }

            /// <summary>
            /// SetShipLevel switches every room off and moves some; the buddy rides the scene root,
            /// so its room is found here, before ClearLevel. docs/invariants.md#the-buddy-rides-its-own-floor
            /// </summary>
            [HarmonyPatch(typeof(StorymodeShipBuilder), nameof(StorymodeShipBuilder.SetShipLevel))]
            [HarmonyPrefix]
            public static void StorymodeShipBuilder_SetShipLevel_Prefix(StorymodeShipBuilder __instance,
                out List<ShipRebuildState>? __state)
            {
                __state = null;
                SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
                if (ship == null || __instance == null || __instance != ship.StorymodeShipBuilder) return;

                int layoutBefore = BuddyNodeGraph.ShipLayoutSignature();
                foreach (BuddyBehaviour buddy in BuddyManager.All)
                {
                    if (buddy == null || buddy.IsDead || !buddy.gameObject.activeInHierarchy || !buddy.IsAboardPlayerShip())
                    {
                        continue;
                    }

                    ShipRebuildState state = new() { Buddy = buddy, LayoutBefore = layoutBefore };
                    if (NavProbe.TryFloorCollider(buddy.transform.position, out Collider? floor) && floor != null)
                    {
                        state.HadFloor = true;
                        CustomRoom room = floor.GetComponentInParent<CustomRoom>();
                        if (room != null && Array.IndexOf(ship.Rooms, room) >= 0)
                        {
                            state.Room = room;
                            state.RoomAt = ship.transform.InverseTransformPoint(room.transform.position);
                        }
                    }
                    __state ??= [];
                    __state.Add(state);
                }
            }

            /// <summary>
            /// A buddy in a room still built moves with it; one whose room is gone is left to the space
            /// protection. Either way its plan and safe spot belong to the old layout.
            /// </summary>
            [HarmonyPatch(typeof(StorymodeShipBuilder), nameof(StorymodeShipBuilder.SetShipLevel))]
            [HarmonyPostfix]
            public static void StorymodeShipBuilder_SetShipLevel_Postfix(byte level, List<ShipRebuildState>? __state)
            {
                if (__state == null) return;

                SpaceShip? ship = GameManager.Instance != null ? GameManager.Instance.PlayerShip : null;
                if (ship == null) return;

                if (BuddyNodeGraph.ShipLayoutSignature() == __state[0].LayoutBefore)
                {
                    if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1)
                    {
                        YourBuddyPlugin.Log.LogInfo($"[ai] Ship rebuilt (stage {level}), layout unchanged - buddies left as they were");
                    }
                    return;
                }

                foreach (ShipRebuildState state in __state)
                {
                    if (state.Buddy == null || state.Buddy.IsDead) continue;

                    using BuddyManager.ActingScope _ = BuddyManager.Acting(state.Buddy);
                    RideRebuild(state, ship, level);
                }
            }

            private static void RideRebuild(ShipRebuildState state, SpaceShip ship, byte level)
            {
                BuddyBehaviour buddy = state.Buddy;
                CustomRoom? room = state.Room;
                if (room == null)
                {
                    buddy.RideShipRebuild(level, Vector3.zero, state.HadFloor,
                        state.HadFloor ? "on a floor of no room, which does not move" : "no floor under it to judge by");
                }
                else if (!room.EnabledStructure)
                {
                    buddy.RideShipRebuild(level, Vector3.zero, false,
                        $"'{room.gameObject.name}' is not built any more - left to the space protection");
                }
                else
                {
                    Vector3 shift = ship.transform.InverseTransformPoint(room.transform.position) - state.RoomAt;
                    string what = shift.sqrMagnitude < 0.0001f
                        ? $"'{room.gameObject.name}' did not move"
                        : $"moved with '{room.gameObject.name}' by {shift:0.00}";
                    buddy.RideShipRebuild(level, ship.transform.TransformVector(shift), true, what);
                }
            }
        }

        [HarmonyPatch, Description("parking the buddy with an unloaded ship")]
        internal static class ShipContent
        {
            /// <summary>
            /// Airlock.Exit unloads the player's ship and Airlock.Enter reloads it; a buddy
            /// aboard is parked in between. Prefix: before the rooms fire OnContentStateChanged.
            /// docs/invariants.md#an-unloaded-ship-parks-the-buddy
            /// </summary>
            [HarmonyPatch(typeof(SpaceShip), nameof(SpaceShip.SetContentEnabled))]
            [HarmonyPrefix]
            public static void SpaceShip_SetContentEnabled_Prefix(SpaceShip __instance, bool value)
            {
                GameManager gm = GameManager.Instance;
                if (gm == null || __instance != gm.PlayerShip) return;

                foreach (BuddyBehaviour buddy in BuddyManager.Snapshot())
                {
                    if (buddy == null) continue;

                    using BuddyManager.ActingScope _ = BuddyManager.Acting(buddy);
                    if (value) buddy.UnparkFromShip();
                    else buddy.ParkWithShip();
                }
            }
        }

        [HarmonyPatch, Description("keeping sell station rooms loaded")]
        internal static class SellRooms
        {
            private static readonly HashSet<int> Logged = [];

            /// <summary>
            /// No one switches off a room holding a sell station while a buddy may sell: the game's own
            /// doorway, dock and airlock unloads included. docs/invariants.md#a-sell-station-room-stays-loaded
            /// </summary>
            [HarmonyPatch(typeof(Room), nameof(Room.SetContentEnabled))]
            [HarmonyPrefix]
            public static bool Room_SetContentEnabled_Prefix(Room __instance, bool state)
            {
                if (state || BuddyManager.All.Count == 0 || !YourBuddyPlugin.ConfigSellTrash.Value ||
                    !SellPens.HoldsSellStation(__instance))
                {
                    return true;
                }
                if (Logged.Add(__instance.GetInstanceID()))
                {
                    YourBuddyPlugin.Log.LogInfo("[ai] Keeping room '" + __instance.gameObject.name +
                                                "' loaded: it holds a sell station (logged once per room)");
                }
                return false;
            }
        }

        /// <summary>
        /// Runs a per-buddy command for each buddy "@2", "@buddy2" or "@all" names, anywhere in `args`, or
        /// for the focused one; `run` gets the other arguments. Naming exactly one moves the focus to it.
        /// docs/reference.md#2-debug-commands
        /// </summary>
        private static void ForTargets(ConsoleMenu console, string[] args, Func<BuddyBehaviour, string[], string> run)
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
                    Print(console, "No buddy called '" + arg + "' - buddy_list shows them");
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
                Print(console, "No buddy exists");
                return;
            }
            if (named && targets.Count == 1) BuddyManager.SetFocus(targets[0]);

            string[] others = [.. rest];
            foreach (BuddyBehaviour buddy in targets)
            {
                using BuddyManager.ActingScope _ = BuddyManager.Acting(buddy);
                Print(console, run(buddy, others));
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

        private static string KeyName(ConfigEntry<KeyboardShortcut> entry)
        {
            KeyboardShortcut shortcut = entry.Value;
            return shortcut.MainKey != KeyCode.None ? shortcut.MainKey.ToString() : "<unset>";
        }

        private static void Print(ConsoleMenu console, string text)
        {
            GameInternals.ConsoleMenuAccess.Print(console, text);
        }
    }

}
