using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Interaction;
using NPC.Core.Saves;
using NPC.Core.World;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// YourBuddy - BepInEx 5 plugin adding a follower NPC to Isolated Inhale, built on NPC.Core
    /// (navigation, doors, mortality, space protection) with a save sidecar of its own.
    /// See README.md and docs/architecture.md.
    /// </summary>
    [BepInPlugin("com.bytenull1.yourbuddy", "YourBuddy Mod", "1.0.8")]
    [BepInDependency(NpcCorePlugin.Guid, NpcCorePlugin.Version)]
    [BepInProcess("Isolated Inhale.exe")]
    public sealed class YourBuddyPlugin : BaseUnityPlugin
    {
        private static YourBuddyPlugin Instance { get; set; }
        private static ManualLogSource? _fallbackLog;
        /// <summary>
        /// The acting buddy's logger while its code runs, else the plugin's; anything logging before
        /// Awake shares one stand-in source. docs/logging.md#4-rules-for-adding-logs
        /// </summary>
        public static ManualLogSource Log =>
            NpcRegistry.ActingLog ??
            (Instance != null ? Instance.Logger : (_fallbackLog ??= BepInEx.Logging.Logger.CreateLogSource("YourBuddyMod")));

        // Config entries (live in BepInEx\config\com.bytenull1.yourbuddy.cfg)
        public static ConfigEntry<bool> ConfigSaveSupport;
        public static ConfigEntry<bool> ConfigNativeSpawn;
        public static ConfigEntry<float> ConfigWakeAfterPodOpen;
        public static ConfigEntry<bool> ConfigMortal;
        public static ConfigEntry<bool> ConfigPreventSpace;
        public static ConfigEntry<bool> ConfigAutoDoors;
        public static ConfigEntry<bool> ConfigDebugVisuals;
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

            Logger.LogInfo("[mod] Initializing...");
            GameInternals.ResolveAll();

            // The game's moments and shared services come from NPC.Core, patched once for every NPC mod.
            NpcEvents.Tick += BuddyManager.Tick;
            NpcEvents.GameStarting += BuddyCryoSpawn.OnGameStarting;
            NpcEvents.SaveLoaded += BuddyManager.ArmPendingSpawn;
            NpcSaves.RegisterSidecar(BuddyBehaviour.ModName, BuddyManager.SidecarExtension, BuddyManager.SidecarContents);
            SellRoomsKeeper.Register();

            BuddyConsole.Register();

            GameObject managerGo = new("YourBuddyManager");
            DontDestroyOnLoad(managerGo);
            managerGo.AddComponent<BuddyManager>();
        }

        /// <summary>
        /// Spawns one more buddy, beside any that exist. `wantedNumber` is kept when free (a save's
        /// own); otherwise the next free ones are used. Null with no game running.
        /// </summary>
        public static BuddyBehaviour? SpawnBuddy(Vector3 position, Quaternion rotation, int wantedNumber = 0)
        {
            if (GameManager.Instance == null) return null;

            // Get the player prefab from GameManager
            Player? playerPrefab = GameInternals.GameManagerAccess.GetPlayerPrefab(GameManager.Instance);
            if (playerPrefab == null) return null;

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

                if (mb is BuddyBehaviour) continue;

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

            // The player, other NPCs and its own controller pass through it: NPC.Core's rule.

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

            // Add the brain and NPC.Core's walking agent, and activate. Registered first, so OnEnable finds
            // the other NPCs to pass through.
            int buddyNumber = BuddyManager.FreeNumber(wantedNumber);
            string buddyName = BuddyManager.NameFor(buddyNumber);
            npcGo.name = "YourBuddy " + buddyName;
            BuddyBehaviour buddy = npcGo.AddComponent<BuddyBehaviour>();
            NpcBody body = new()
            {
                Ragdoll = ragdollObject,
                RagdollBody = ragdollRigidbody,
                AnimatedModel = animatedModel,
                ItemBlocker = blocker,
                FootstepEvents = NpcPlayer.FootstepEvents()
            };
            NpcAgent agent = NpcAgent.Attach(npcGo, new NpcIdentity(BuddyBehaviour.ModName, buddyName, buddyNumber), body,
                BuddyAgentSettings.Instance, buddy);
            buddy.Init(agent);
            BuddyManager.Register(buddy);
            NpcInteraction.Register(new BuddyConversation(buddy));

            npcGo.SetActive(true);
            using NpcRegistry.ActingScope _ = NpcRegistry.Acting(agent);
            Log.LogInfo($"[mod] {buddyName} successfully spawned at {position}");
            return buddy;
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
