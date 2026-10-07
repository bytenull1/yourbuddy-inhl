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
    /// BepInEx 5 plugin adding a follower NPC to Isolated Inhale. Built on NPC.Core (navigation,
    /// doors, mortality, space protection), with its own save sidecar. See README.md and
    /// docs/architecture.md.
    /// </summary>
    [BepInPlugin("com.bytenull1.yourbuddy", "YourBuddy Mod", "1.0.9")]
    [BepInDependency(NpcCorePlugin.Guid, NpcCorePlugin.Version)]
    [BepInProcess("Isolated Inhale.exe")]
    public sealed class YourBuddyPlugin : BaseUnityPlugin
    {
        private static YourBuddyPlugin Instance { get; set; }
        private static ManualLogSource? _fallbackLog;
        /// <summary>
        /// The acting buddy's logger while its code runs, else the plugin's. Anything logging before
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
        public static ConfigEntry<bool> ConfigEvaSuit;
        public static ConfigEntry<bool> ConfigSuitFetch;
        public static ConfigEntry<bool> ConfigSnacks;
        public static ConfigEntry<float> ConfigSnackIntervalMinutes;
        public static ConfigEntry<bool> ConfigStoring;
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
        public static ConfigEntry<bool> ConfigAnomalies;
        public static ConfigEntry<AnomalyLevel> ConfigAnomalyDifficulty;
        public static ConfigEntry<float> ConfigAnomalyFrequency;
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
                "Off: no buddy until 'buddy_spawn'. Loaded saves always bring back their own buddy, or none.");
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
                "Draw the buddy's target, planned path and obstacle probes in the world. Can also be toggled with 'buddy_dev visuals'.");
            ConfigShowHud = Config.Bind("General", "ShowHud", false,
                "Display an on-screen status panel (mode, room, environment, threat, target) while a buddy exists. Toggle with 'buddy_dev hud'.");
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
                "Tell it 'decide for yourself' (or 'buddy_order auto on') to hand control back after an order.");
            ConfigTerminals = Config.Bind("General", "Terminals", true,
                "When the air aboard turns dangerous (low oxygen, too cold or too hot) and the oxygen generator or " +
                "climate control is switched off, the buddy walks over and switches it on - even under an order, since " +
                "deadly air outranks one (Autonomy must be on). It never switches anything off and never clears a fault.");
            ConfigEvaSuit = Config.Bind("General", "EvaSuit", true,
                "The buddy can put on an EVA suit: it takes a spare Space_Suit item (never your only one), wears it drawn " +
                "with the mod's own EVA skin, and can then be ordered outside - it walks into the docked station's exit " +
                "airlock and waits for you to cycle it - and back inside the same way. It also suits up by itself when " +
                "the air aboard turns deadly and no terminal can fix it, and brings a suit you forgot on the docked " +
                "station back to the ship (see SuitFetch). 'buddy_order outside', 'buddy_order inside' and 'buddy_order suit' trigger it now. docs/eva.md");
            ConfigSuitFetch = Config.Bind("General", "SuitFetch", true,
                "While you are both aboard and docked, the buddy fetches an EVA suit you left lying on the station and " +
                "sets it down inside your ship's airlock, until two suits are yours or aboard. Needs EvaSuit. " +
                "'buddy_order fetchsuit' starts a fetch now, whatever this setting says.");
            ConfigSnacks = Config.Bind("General", "Snacks", true,
                "Now and then the buddy opens a nearby fridge, cabinet, chest or locker and eats or drinks one thing " +
                "from it, then closes it again. It does not need to eat. Only while it decides for itself, never while " +
                "you are hungry, and never at a hiding spot you are in. 'buddy_order snack' triggers one now.");
            ConfigSnackIntervalMinutes = Config.Bind("General", "SnackIntervalMinutes", 13f,
                "Roughly how many minutes pass between snacks (each time 25% more or less at random; at least 1). " +
                "13 tracks how often the player themselves needs to eat: satiety drains 1/tick (~1.02s) and the game's " +
                "own Hunger buff starts at 250, so from a full stomach (the ~1000 a player can actually eat up to) " +
                "that is about 12-13 minutes.");
            ConfigStoring = Config.Bind("General", "Storing", true,
                "Put away loose items and sort misplaced storage contents aboard while deciding for itself. " +
                "Leaves correctly stored items and machine contents alone; uses checked floor overflow when needed. The Store items order continues until replaced by another order.");
            ConfigTidying = Config.Bind("General", "Tidying", true,
                "Now and then the buddy picks up a piece of trash (an empty wrapper or can, never anything still useful) " +
                "lying about or from a fridge, cabinet, chest or locker it closes again, and carries it to a trash can within 25m. Only while it decides " +
                "for itself. 'buddy_order tidy' triggers it now.");
            ConfigTidyIntervalMinutes = Config.Bind("General", "TidyIntervalMinutes", 5f,
                "Roughly how many minutes pass between tidying rounds (each time 25% more or less at random; at least 1).");
            ConfigSellTrash = Config.Bind("General", "SellTrash", true,
                "When a full trash can has dropped a trash box and a sell station is within reach, the buddy carries the " +
                "box there, loads it and presses the button; the money is yours. It only presses when the station holds " +
                "nothing sellable but trash boxes and nobody stands inside. Only while it decides for itself. " +
                "'buddy_order sell' triggers it now.");
            ConfigHideInClosets = Config.Bind("General", "HideInClosets", true,
                "When the buddy flees the Breathless and a closet or locker is within 8m, it hides inside instead of " +
                "running across the ship: it opens the doors, gets in, closes them, and comes out once it is calm again " +
                "or someone opens a door. It never uses a spot you are in. 'buddy_order hide' tries one now.");
            ConfigFleeHideBias = Config.Bind("General", "FleeHideBias", 0.5f,
                "How readily a frightened buddy hides rather than runs, 0 (never hide) to 1 (always try). " +
                "The chance is lowered when the Breathless can see it - it would watch the buddy climb in - " +
                "and when the monster is too close to reach the doors in time, and raised when there is nowhere to run. " +
                "Ignored with HideInClosets off.");
            ConfigItemPlay = Config.Bind("General", "ItemPlay", true,
                "Now and then, with nothing else to do, the buddy plays with something loose within 8m: it either carries " +
                "it a few metres and puts it down, or throws it across the room, at random. Never anything in a container, " +
                "a machine or your hands, and it never throws at you. Only while it decides for itself. " +
                "'buddy_order play' triggers it now.");
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
            ConfigAnomalies = Config.Bind("Anomalies", "Anomalies", true,
                "Now and then the buddy does something strange or frightening, and you start to wonder whether it is " +
                "still the friend you woke up with. How far it goes follows the difficulty (see AnomalyDifficulty) and your " +
                "progress: Harmless none; Normal strange, scary after the first story task, extreme after " +
                "the third; Expert scary and extreme after the first. " +
                "'buddy_anomaly' lists them and starts one now. docs/anomalies.md");
            ConfigAnomalyDifficulty = Config.Bind("Anomalies", "AnomalyDifficulty", AnomalyLevel.Game,
                "Game: follow the game's difficulty (its events frequency). Harmless, Normal or Expert: use that one for the " +
                "buddy whatever the game is set to.");
            ConfigAnomalyFrequency = Config.Bind("Anomalies", "AnomalyFrequency", 1f,
                "How often anomalies happen: multiplies their chance and divides the quiet after one. 0: never by " +
                "themselves ('buddy_anomaly' still works).");

            Logger.LogInfo("[mod] Initializing...");
            Logger.LogInfo($"[store-detail] diagnostics v1 build={typeof(YourBuddyPlugin).Module.ModuleVersionId}");
            GameInternals.ResolveAll();

            // The game's moments and shared services come from NPC.Core, patched once for every NPC mod.
            NpcEvents.Tick += BuddyManager.Tick;
            ResourceDuty.RegisterLifecycle();
            NpcEvents.GameStarting += BuddyCryoSpawn.OnGameStarting;
            NpcEvents.GameStarting += AnomalyMemory.OnGameStarting;
            NpcEvents.SaveLoaded += BuddyManager.ArmPendingSpawn;
            NpcSaves.RegisterSidecar(BuddyBehaviour.ModName, BuddyManager.SidecarExtension, BuddyManager.SidecarContents);

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

            Player? playerPrefab = GameInternals.GameManagerAccess.GetPlayerPrefab(GameManager.Instance);
            if (playerPrefab == null) return null;

            // Deactivate at once so the player's Start()/Awake() never run on the clone.
            GameObject npcGo = Instantiate(playerPrefab.gameObject, position, rotation);
            npcGo.SetActive(false);

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

            // Read before PlayerController is stripped. Its Ragdoll() swaps these two children, and the
            // buddy reuses them to ragdoll when it dies.
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

            // Strip game logic, keeping animation scripts.
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

            foreach (Rigidbody rb in npcGo.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
            }

            CharacterController buddyCc = npcGo.GetComponent<CharacterController>();

            // Only the CharacterController stays on. The ragdoll's colliders come back when the buddy dies.
            foreach (Collider col in npcGo.GetComponentsInChildren<Collider>(true))
            {
                if (col is CharacterController) continue;

                col.enabled = false;
            }

            // Solid for thrown objects, ignored by both controllers. It has the CharacterController's
            // exact size, so it covers the whole body and fits wherever the buddy fits.
            GameObject itemBlocker = new("ItemBlocker");
            itemBlocker.transform.SetParent(npcGo.transform, false);
            itemBlocker.layer = 0; // Default layer, skipped by player raycasts but solid for physics

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

            // The player, other NPCs and its own controller pass through it (NPC.Core's rule).

            // Deliberately much narrower than the player's capsule, so the buddy fits
            // through any doorway without precision aiming. The visible model is
            // unaffected and the full-size ItemBlocker still stops thrown items.
            if (buddyCc != null) buddyCc.radius = 0.22f;

            foreach (Renderer renderer in npcGo.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer = 0;
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;

                if (renderer is SkinnedMeshRenderer smr && smr.rootBone != null)
                {
                    smr.rootBone.gameObject.SetActive(true);
                }
            }

            // The face mask is the skinned "Avatar" mesh under PlayerPilotSuit/armature/torso/chest/head.
            // EquipmentSystem is already stripped, so nothing re-enables it.
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

            foreach (Transform child in npcGo.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name.ToLowerInvariant();
                if (name.Contains("camera") || name.Contains("audio") || name.Contains("placepreview"))
                {
                    child.gameObject.SetActive(false);
                }
            }

            // Add the brain and NPC.Core's agent. Registered before activation, so OnEnable finds the
            // other NPCs to pass through.
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
                new BuddyAgentSettings(buddy), buddy);
            buddy.Init(agent);
            buddy.Model = animatedModel;
            BuddyManager.Register(buddy);
            BuddyConversation conversation = new(buddy);
            buddy.Conversation = conversation;
            NpcInteraction.Register(conversation);

            npcGo.SetActive(true);
            using NpcRegistry.ActingScope _ = NpcRegistry.Acting(agent);
            Log.LogInfo($"[mod] {buddyName} successfully spawned at {position}");
            return buddy;
        }

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
