using System;
using System.ComponentModel;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// YourBuddy - BepInEx 5 plugin adding a follower NPC to Isolated Inhale: node-graph
    /// navigation, doors, mortality, space protection and a save sidecar.
    /// See README.md and docs/architecture.md.
    /// </summary>
    [BepInPlugin("com.bytenull1.yourbuddy", "YourBuddy Mod", "1.0.7")]
    [BepInProcess("Isolated Inhale.exe")]
    public sealed class YourBuddyPlugin : BaseUnityPlugin
    {
        private static YourBuddyPlugin Instance { get; set; }
        private static ManualLogSource? _fallbackLog;
        /// <summary>
        /// The plugin's logger; anything logging before Awake shares one stand-in source.
        /// </summary>
        public static ManualLogSource Log =>
            Instance != null ? Instance.Logger : (_fallbackLog ??= BepInEx.Logging.Logger.CreateLogSource("YourBuddyMod"));
        private static BuddyNodeEditor _nodeEditor;
        public static BuddyNodeEditor NodeEditor
        {
            get
            {
                if (_nodeEditor == null)
                {
                    GameObject? go = GameObject.Find("YourBuddyNodeEditor");
                    if (go == null)
                    {
                        go = new GameObject("YourBuddyNodeEditor");
                        DontDestroyOnLoad(go);
                    }
                    BuddyNodeEditor editor = go.GetComponent<BuddyNodeEditor>();
                    _nodeEditor = editor != null ? editor : go.AddComponent<BuddyNodeEditor>();
                }
                return _nodeEditor;
            }
            private set => _nodeEditor = value;
        }

        // Config entries (live in BepInEx\config\com.bytenull1.yourbuddy.cfg)
        public static ConfigEntry<bool> ConfigSaveSupport;
        public static ConfigEntry<bool> ConfigNativeSpawn;
        public static ConfigEntry<float> ConfigWakeAfterPodOpen;
        public static ConfigEntry<bool> ConfigMortal;
        public static ConfigEntry<bool> ConfigPreventSpace;
        public static ConfigEntry<bool> ConfigAutoDoors;
        public static ConfigEntry<bool> ConfigDebugVisuals;
        public static ConfigEntry<int> ConfigDebugLevel;
        public static ConfigEntry<bool> ConfigShowHud;
        public static ConfigEntry<bool> ConfigDialog;
        public static ConfigEntry<bool> ConfigFear;
        public static ConfigEntry<bool> ConfigAutonomy;
        public static ConfigEntry<bool> ConfigTerminals;
        public static ConfigEntry<bool> ConfigSnacks;
        public static ConfigEntry<float> ConfigSnackIntervalMinutes;
        public static ConfigEntry<bool> ConfigTidying;
        public static ConfigEntry<float> ConfigTidyIntervalMinutes;
        public static ConfigEntry<bool> ConfigSellTrash;
        public static ConfigEntry<bool> ConfigHideInClosets;
        public static ConfigEntry<float> ConfigFleeHideBias;
        public static ConfigEntry<bool> ConfigItemPlay;
        public static ConfigEntry<bool> ConfigItemPlayAnything;
        public static ConfigEntry<float> ConfigItemPlayIntervalMinutes;
        public static ConfigEntry<OrderPersistence> ConfigOrderPersistence;
        public static ConfigEntry<float> ConfigOrderExpirySeconds;
        // ReSharper disable once MemberCanBePrivate.Global
        public static ConfigEntry<float> ConfigMoveSpeed;
        public static ConfigEntry<float> ConfigMaxEdgeDist;
        public static ConfigEntry<bool> ConfigBundledGraph;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorToggleKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorPlaceKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorDeleteKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorLinksKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorForceLinkKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorBlockLinkKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorPriorityLinkKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorClearLinksKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorAutoLinkKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorTypeKey;
        public static ConfigEntry<KeyboardShortcut> ConfigEditorSaveKey;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            ConfigSaveSupport = Config.Bind("General", "SaveSupport", true,
                "Preserve the buddy's location across saves. Writes a sidecar file '<save name>.buddy' next to the game's save files. " +
                "The vanilla game never reads this file, so uninstalling the mod cannot damage saves.");
            ConfigNativeSpawn = Config.Bind("General", "SpawnOnNewGame", true,
                "Start every new game with a buddy: it wakes in a closed cryo capsule next to yours, which opens, " +
                "or stands at the Shipyard station's origin when no capsule can be opened. " +
                "Off: no buddy until 'spawn_buddy'. Loaded saves always bring back their own buddy, or none.");
            ConfigWakeAfterPodOpen = Config.Bind("General", "WakeAfterPodOpenSeconds", 10f,
                "SpawnOnNewGame: seconds after your own pod starts opening before the buddy's capsule opens " +
                "(also after loading a save made while it slept).");
            ConfigMortal = Config.Bind("General", "MortalNPC", true,
                "Allow the buddy to die (turns into a ragdoll) when caught by the Breathless or exposed to a deadly atmosphere.");
            ConfigPreventSpace = Config.Bind("General", "PreventSpace", true,
                "Never open airlock gates for the buddy, refuse to walk into an airlock that is open to space, " +
                "and teleport it back inside if it somehow ends up in open space.");
            ConfigAutoDoors = Config.Bind("General", "AutoDoors", true,
                "Allow the buddy to open room doors (Gate type) in front of it. Cabinet doors and airlocks are never touched.");
            ConfigDebugLevel = Config.Bind("General", "DebugLevel", 1,
                "Amount of debug logging: 0 = quiet (warnings only), 1 = normal (stuck diagnostics, door logs), " +
                "2 = thinking (path planning details, the gate inventory and the gate-frame audit), " +
                "3 = obstacle (per-tick obstacle reports). " +
                "Can also be changed at runtime with 'debug_level'.");
            ConfigDebugVisuals = Config.Bind("General", "DebugVisuals", false,
                "Draw the buddy's target, planned path and obstacle probes in the world. Can also be toggled with 'buddy_debug'.");
            ConfigShowHud = Config.Bind("General", "ShowHud", false,
                "Display an on-screen status panel (mode, room, environment, threat, target) while a buddy exists. Toggle with 'buddy_hud'.");
            ConfigDialog = Config.Bind("General", "Dialog", true,
                "Look at the buddy and press Interact to open a window where you can give it orders " +
                "(follow, do its own thing, stay, walk to a node) and tell it a door password.");
            ConfigFear = Config.Bind("General", "Fear", true,
                "The buddy notices the Breathless when it can see it (never through walls or shut doors): it turns " +
                "to face it, refuses to walk toward it, and flees - first away, then to you - when it has watched it " +
                "too long or it comes too close. Off: it ignores the monster, which can still kill it.");
            ConfigAutonomy = Config.Bind("General", "Autonomy", true,
                "Whether the buddy ever decides for itself. With no order in force it takes turns: it follows you " +
                "for a while, then wanders around on its own, then comes back to you. " +
                "Tell it 'decide for yourself' (or 'buddy_auto on') to hand control back after an order.");
            ConfigTerminals = Config.Bind("General", "Terminals", true,
                "When the air aboard turns dangerous (low oxygen, too cold or too hot) and the oxygen generator or " +
                "climate control is switched off, the buddy walks over and switches it on. Only while it decides for " +
                "itself (Autonomy, no order in force); it never switches anything off and never clears a fault.");
            ConfigSnacks = Config.Bind("General", "Snacks", true,
                "Now and then the buddy opens a nearby fridge, cabinet, chest or locker and eats or drinks one thing " +
                "from it, then closes it again. It does not need to eat. Only while it decides for itself, never while " +
                "you are hungry, and never at a hiding spot you are in. 'buddy_snack' triggers one now.");
            ConfigSnackIntervalMinutes = Config.Bind("General", "SnackIntervalMinutes", 13f,
                "Roughly how many minutes pass between snacks (each time 25% more or less at random; at least 1). " +
                "13 tracks how often the player themselves needs to eat: satiety drains 1/tick (~1.02s) and the game's " +
                "own Hunger buff starts at 250, so from a full stomach (the ~1000 a player can actually eat up to) " +
                "that is about 12-13 minutes.");
            ConfigTidying = Config.Bind("General", "Tidying", true,
                "Now and then the buddy picks up a piece of trash (an empty wrapper or can, never anything still useful) " +
                "lying about or from a fridge, cabinet, chest or locker it closes again, and carries it to a trash can within 25m. Only while it decides " +
                "for itself. 'buddy_tidy' triggers it now.");
            ConfigTidyIntervalMinutes = Config.Bind("General", "TidyIntervalMinutes", 5f,
                "Roughly how many minutes pass between tidying rounds (each time 25% more or less at random; at least 1).");
            ConfigSellTrash = Config.Bind("General", "SellTrash", true,
                "When a full trash can has dropped a trash box and a sell station is within reach, the buddy carries the " +
                "box there, loads it and presses the button; the money is yours. It only presses when the station holds " +
                "nothing sellable but trash boxes and nobody stands inside. Only while it decides for itself. " +
                "'buddy_sell' triggers it now.");
            ConfigHideInClosets = Config.Bind("General", "HideInClosets", true,
                "When the buddy flees the Breathless and a closet or locker is within 8m, it hides inside instead of " +
                "running across the ship: it opens the doors, gets in, closes them, and comes out once it is calm again " +
                "or someone opens a door. It never uses a spot you are in. 'buddy_hide' tries one now.");
            ConfigFleeHideBias = Config.Bind("General", "FleeHideBias", 0.5f,
                "How readily a frightened buddy hides rather than runs, 0 (never hide) to 1 (always try). " +
                "The chance is lowered when the Breathless can see it - it would watch the buddy climb in - " +
                "and when the monster is too close to reach the doors in time, and raised when there is nowhere to run. " +
                "Ignored with HideInClosets off.");
            ConfigItemPlay = Config.Bind("General", "ItemPlay", true,
                "Now and then, with nothing else to do, the buddy plays with something loose within 8m: it either carries " +
                "it a few metres and puts it down, or throws it across the room, at random. Never anything in a container, " +
                "a machine or your hands, and it never throws at you. Only while it decides for itself. " +
                "'buddy_play' triggers it now.");
            ConfigItemPlayAnything = Config.Bind("General", "ItemPlayAnything", false,
                "What the buddy may play with. Off: garbage only - wrappers, cans, empty seed packs, broken loot boxes. " +
                "On: any loose item it finds, your tools, food and cells included, which it will throw about like anything else.");
            ConfigItemPlayIntervalMinutes = Config.Bind("General", "ItemPlayIntervalMinutes", 5f,
                "Roughly how many minutes pass between games (each time 25% more or less at random; at least 1).");
            ConfigOrderPersistence = Config.Bind("General", "OrderPersistence", OrderPersistence.UntilRevoked,
                "UntilRevoked: an order (follow, wander, stay) holds until you give another one. " +
                "Expires: it decides for itself again OrderExpirySeconds after the order. A goto always runs to completion.");
            ConfigOrderExpirySeconds = Config.Bind("General", "OrderExpirySeconds", 90f,
                "How long an order holds when OrderPersistence is Expires. Ignored otherwise.");
            ConfigMoveSpeed = Config.Bind("General", "MoveSpeed", 3.5f,
                "Default buddy movement speed in m/s.");
            ConfigMaxEdgeDist = Config.Bind("Navigation", "MaxEdgeDist", 80f,
                "Maximum distance (meters) at which two nav nodes are auto-connected. " +
                "Lower it for sparser graphs. Manual Force links (editor key K) work at any distance.");
            ConfigBundledGraph = Config.Bind("Navigation", "BundledGraph", true,
                "Use the ready-made nav graph shipped with the mod for stations and the ship. " +
                "Your own nodes always win: the moment you edit anything on a ship or station, " +
                "that one becomes yours and mod updates stop changing it. " +
                "'buddy_node bundled' shows which is which, 'buddy_node unfork <owner>' hands one back.");
            ConfigEditorToggleKey = Config.Bind("NodeEditor", "EditorToggleKey", new KeyboardShortcut(KeyCode.F8),
                "Toggles the node editor overlay. Can also be toggled with the 'node_editor' console command.");
            ConfigEditorPlaceKey = Config.Bind("NodeEditor", "EditorPlaceKey", new KeyboardShortcut(KeyCode.Insert),
                "Places a node at the player's position (Numpad0 works as an always-on alternative).");
            ConfigEditorDeleteKey = Config.Bind("NodeEditor", "EditorDeleteKey", new KeyboardShortcut(KeyCode.Delete),
                "Removes the node nearest to the player.");
            ConfigEditorLinksKey = Config.Bind("NodeEditor", "EditorLinksKey", new KeyboardShortcut(KeyCode.L),
                "Toggles rendering of node connections. Previously C, which fights the game's crouch key.");
            ConfigEditorForceLinkKey = Config.Bind("NodeEditor", "EditorForceLinkKey", new KeyboardShortcut(KeyCode.K),
                "Link mode 1 of 3: press once to mark the selected node, then again at another node to create a Force " +
                "link - the connection always works and skips the hull probe (use for ramps/stair flights the probe rejects).");
            ConfigEditorBlockLinkKey = Config.Bind("NodeEditor", "EditorBlockLinkKey", new KeyboardShortcut(KeyCode.B),
                "Link mode 2 of 3: mark a node, then press at another node to Block the auto connection between them " +
                "(use for connections across railings the probe still allows).");
            ConfigEditorPriorityLinkKey = Config.Bind("NodeEditor", "EditorPriorityLinkKey", new KeyboardShortcut(KeyCode.O),
                "Link mode 3 of 3: mark a node, then press at another node to create a Priority route. Whenever a path " +
                "arrives at the marked node from anywhere else, the ONLY exit is the second node - all other connections " +
                "from it are ignored. Arriving VIA the second node keeps every exit open, so return trips still work.");
            ConfigEditorClearLinksKey = Config.Bind("NodeEditor", "EditorClearLinksKey", new KeyboardShortcut(KeyCode.U),
                "Removes every Force, Block and Priority link of the selected node at once. Automatic connections " +
                "are not stored links - turn them off with the auto-connect key.");
            ConfigEditorAutoLinkKey = Config.Bind("NodeEditor", "EditorAutoLinkKey", new KeyboardShortcut(KeyCode.N),
                "Toggles auto-connect snapping for the selected node. Auto-connect off (blue node) means it only accepts " +
                "manually placed links - useful in large rooms where automatic snapping creates clutter.");
            ConfigEditorTypeKey = Config.Bind("NodeEditor", "EditorTypeKey", new KeyboardShortcut(KeyCode.T),
                "Cycles the selected node's type between Ground and Stair. Stair nodes allow auto edges up to 2m of climb.");
            ConfigEditorSaveKey = Config.Bind("NodeEditor", "EditorSaveKey", new KeyboardShortcut(KeyCode.F6),
                "Saves the node graph. Never bind this to F5: that key is the game's own QuickSave.");

            Logger.LogInfo("[mod] Initializing...");
            ApplyPatches();

            GameInternals.ResolveAll();

            GameObject managerGo = new("YourBuddyManager");
            DontDestroyOnLoad(managerGo);
            managerGo.AddComponent<BuddyManager>();


            // Node editor (toggleable via console command or F8)
            GameObject editorGo = new("YourBuddyNodeEditor");
            DontDestroyOnLoad(editorGo);
            NodeEditor = editorGo.AddComponent<BuddyNodeEditor>();
        }

        /// <summary>
        /// Applies each patch group on its own, so a game update that removes one target
        /// disables that one feature, named in the log, instead of every patch after it.
        /// </summary>
        private void ApplyPatches()
        {
            Harmony harmony = new("com.bytenull1.yourbuddy");
            int applied = 0, total = 0;
            foreach (Type group in typeof(Patches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!group.IsDefined(typeof(HarmonyPatch), false)) continue;

                total++;
                try
                {
                    harmony.CreateClassProcessor(group).Patch();
                    applied++;
                }
                catch (Exception ex)
                {
                    string feature = group.GetCustomAttribute<DescriptionAttribute>()?.Description ?? group.Name;
                    Logger.LogError($"[mod] Patch group '{group.Name}' failed - {feature} disabled: {ex}");
                }
            }

            if (applied == total) Logger.LogInfo($"[mod] All {total} patch groups applied.");
            else Logger.LogWarning($"[mod] {applied} of {total} patch groups applied - see errors above.");
        }

        /// <summary>
        /// Spawns a Buddy NPC at the specified position and rotation.
        /// </summary>
        public static void SpawnBuddy(Vector3 position, Quaternion rotation)
        {
            if (GameManager.Instance == null) return;

            // Remove any existing buddy first - there can only be one.
            BuddyManager.DespawnBuddy();

            // Get the player prefab from GameManager
            Player? playerPrefab = GameInternals.GameManagerAccess.GetPlayerPrefab(GameManager.Instance);
            if (playerPrefab == null) return;

            // Instantiate the buddy and immediately deactivate to prevent Start()/Awake() from crashing
            GameObject npcGo = Instantiate(playerPrefab.gameObject, position, rotation);
            npcGo.SetActive(false);

            // Copy Avatar from the real player to the clone
            Player realPlayer = GameManager.Instance.PlayerShip.Pilot;
            if (realPlayer != null)
            {
                Animator realAnimator = realPlayer.GetComponentInChildren<Animator>(true);
                if (realAnimator != null && realAnimator.avatar != null)
                {
                    foreach (Animator a in npcGo.GetComponentsInChildren<Animator>(true))
                    {
                        if (a.avatar == null)
                        {
                            a.avatar = realAnimator.avatar;
                        }
                    }
                }
            }

            // Capture ragdoll / model references from the player's PlayerController before it is stripped.
            // PlayerController.Ragdoll() swaps between these two child objects, and we reuse the same
            // objects to turn the buddy into a ragdoll when it dies.
            GameObject? ragdollObject = null;
            Rigidbody? ragdollRigidbody = null;
            GameObject? animatedModel = null;
            if (npcGo.TryGetComponent(out PlayerController cloneController))
            {
                ragdollObject = GameInternals.PlayerControllerAccess.GetRagdollObject(cloneController);
                ragdollRigidbody = GameInternals.PlayerControllerAccess.GetRagdollRigidbody(cloneController);
                Animator? modelAnimator = GameInternals.PlayerControllerAccess.GetModelAnimator(cloneController);
                if (modelAnimator != null) animatedModel = modelAnimator.gameObject;
            }

            // Safely clean up game logic components while preserving animation scripts
            MonoBehaviour[] monoBehaviours = npcGo.GetComponentsInChildren<MonoBehaviour>(true);
            string[] animationScriptWhitelist =
            [
                "ArmAnimator", "EyeAnimator", "BlinkAnimator", "MovementAnimator",
                "MaterialAnimator", "FadingAnimator", "FadingSpriteAnimator",
                "ShakeAnimator", "RotationAnimator", "WaveAnimator", "InterferenceAnimator"
            ];

            for (int i = monoBehaviours.Length - 1; i >= 0; i--)
            {
                MonoBehaviour mb = monoBehaviours[i];
                if (mb == null) continue;

                if (mb is BuddyBehaviour or BuddyDialog) continue;

                string? ns = mb.GetType().Namespace;
                if (ns != null && ns.StartsWith("Unity", StringComparison.Ordinal)) continue;

                string tName = mb.GetType().Name;
                if (tName.Contains("IK") || tName.Contains("Bone") || tName.Contains("Rig") || tName.Contains("Constraint")) continue;

                bool isAnimationScript = false;
                foreach (string wl in animationScriptWhitelist)
                {
                    if (tName.Contains(wl)) { isAnimationScript = true; break; }
                }
                if (isAnimationScript) continue;

                mb.enabled = false;
                DestroyImmediate(mb);
            }

            // Fix Physics & Colliders
            foreach (Rigidbody rb in npcGo.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
            }

            CharacterController buddyCc = npcGo.GetComponent<CharacterController>();

            // Disable all colliders except CharacterController (the ragdoll's colliders are
            // re-enabled individually when the buddy dies).
            foreach (Collider col in npcGo.GetComponentsInChildren<Collider>(true))
            {
                if (col is CharacterController) continue;

                col.enabled = false;
            }

            // "ItemBlocker": solid for thrown objects, ignored by both controllers. Uses
            // the CharacterController's exact dimensions, so it covers the whole body
            // and fits wherever the buddy fits.
            GameObject itemBlocker = new("ItemBlocker");
            itemBlocker.transform.SetParent(npcGo.transform, false);
            itemBlocker.layer = 0; // Default layer - skipped by player raycasts, solid for physics

            CapsuleCollider blocker = itemBlocker.AddComponent<CapsuleCollider>();
            if (buddyCc != null)
            {
                blocker.radius = buddyCc.radius;
                blocker.height = buddyCc.height;
                blocker.center = buddyCc.center;
            }
            else
            {
                blocker.radius = 0.3f;
                blocker.height = 1.8f;
                blocker.center = new Vector3(0f, 0.9f, 0f);
            }
            blocker.isTrigger = false;

            if (realPlayer != null)
            {
                if (realPlayer.TryGetComponent(out CharacterController playerCc))
                {
                    // Player and buddy can walk through each other; thrown items still bounce off.
                    if (buddyCc != null) Physics.IgnoreCollision(playerCc, buddyCc);

                    Physics.IgnoreCollision(playerCc, blocker);
                }

                // The player's head/foot triggers must also ignore the buddy, otherwise the
                // head trigger counts the blocker as a "ceiling" and force-crouches the player
                // whenever they stand next to the buddy.
                if (realPlayer.TryGetComponent(out PlayerController realPC))
                {
                    IgnorePlayerTrigger(GameInternals.PlayerControllerAccess.GetHeadTrigger(realPC), buddyCc, blocker);
                    IgnorePlayerTrigger(GameInternals.PlayerControllerAccess.GetFootTrigger(realPC), buddyCc, blocker);
                }
            }
            // The buddy's controller must never collide with its own blocker.
            if (buddyCc != null) Physics.IgnoreCollision(buddyCc, blocker);

            // Deliberately much narrower than the player's capsule, so the buddy fits
            // through any doorway without precision aiming. The visible model is
            // unaffected and the full-size ItemBlocker still stops thrown items.
            if (buddyCc != null) buddyCc.radius = 0.22f;

            // Fix Visibility, Layers, and RootBones
            foreach (Renderer renderer in npcGo.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer = 0; // Force to Default layer
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;

                if (renderer is SkinnedMeshRenderer smr && smr.rootBone != null)
                {
                    smr.rootBone.gameObject.SetActive(true);
                }
            }

            // Hide the face/mask mesh. The mask geometry lives in the skinned "Avatar" mesh under
            // PlayerPilotSuit/armature/torso/chest/head - hide exactly that renderer by path.
            // (EquipmentSystem was already stripped above, so nothing re-enables it.)
            foreach (Renderer r in npcGo.GetComponentsInChildren<Renderer>(true))
            {
                string fullPath = GetGameObjectPath(r.gameObject).ToLowerInvariant();

                if (fullPath.Contains("playerpilotsuit") && fullPath.Contains("armature") &&
                    fullPath.Contains("torso") && fullPath.Contains("chest") &&
                    fullPath.Contains("head") && fullPath.Contains("avatar"))
                {
                    r.enabled = false;
                }
            }

            // Hide cameras and audio
            foreach (Transform child in npcGo.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name.ToLowerInvariant();
                if (name.Contains("camera") || name.Contains("audio") || name.Contains("placepreview"))
                {
                    child.gameObject.SetActive(false);
                }
            }

            // Add AI and Activate
            BuddyBehaviour buddy = npcGo.AddComponent<BuddyBehaviour>();
            buddy.Init(ragdollObject, ragdollRigidbody, animatedModel, blocker,
                realPlayer != null ? realPlayer.GetComponent<CharacterController>() : null, ConfigMoveSpeed.Value);
            npcGo.AddComponent<BuddyDialog>();

            npcGo.SetActive(true);
            BuddyManager.CurrentBuddy = buddy;
            Log.LogInfo($"[mod] Buddy successfully spawned at {position}");
        }

        private static void IgnorePlayerTrigger(HeadTrigger? trigger, CharacterController? buddyCc, Collider? blocker)
        {
            if (trigger == null) return;
            if (!trigger.TryGetComponent(out Collider triggerCollider)) return;

            if (buddyCc != null) Physics.IgnoreCollision(triggerCollider, buddyCc);
            if (blocker != null) Physics.IgnoreCollision(triggerCollider, blocker);
        }

        /// <summary>
        /// Gets the full path of a GameObject in the hierarchy.
        /// </summary>
        private static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }

}
