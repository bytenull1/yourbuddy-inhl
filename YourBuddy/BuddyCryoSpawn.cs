using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace YourBuddy
{
    /// <summary>
    /// A new game's buddy: asleep in a shut prop capsule beside the player's cryo pod until
    /// a while after the player's pod opens, or at the Shipyard's origin when no capsule can
    /// be opened. docs/game-model.md#the-cryo-room
    /// </summary>
    internal static class BuddyCryoSpawn
    {
        private const float ClosedTolerance = 0.02f;
        private const float LoadWaitSeconds = 20f;

        private static bool _armed;
        private static float _armedAt = -1f;
        private static bool _waitLogged;
        private static bool _displaysSwept;
        private static string? _pendingReopen;
        private static string? _openedCapsule;
        private static GameManager? _openedIn;

        // Set while a sleep is tracked, so a sleeper destroyed by despawn is still noticed.
        private static bool _sleeping;
        private static BuddyBehaviour? _sleeper;
        private static CryoPod? _sleeperPod;
        private static PropCapsule _sleeperCapsule;
        private static float _wakeAt = float.MaxValue;
        private static string? _wakeReason;

        /// <summary>
        /// The prop capsule opened for the buddy in the running game, or null. Bound to its
        /// GameManager, so a save written from the menu cannot inherit the last game's.
        /// </summary>
        internal static string? OpenedCapsule =>
            _openedIn != null && _openedIn == GameManager.Instance ? _openedCapsule : null;

        /// <summary>
        /// The capsule the current buddy is still asleep in, or null.
        /// </summary>
        internal static string? SleepingCapsule =>
            _sleeper != null && _sleeper == BuddyManager.CurrentBuddy ? _sleeperCapsule.Capsule.name : null;

        /// <summary>
        /// GameManager.Start is about to run; worldTime 0 is the game's own new-game test.
        /// </summary>
        internal static void OnGameStarting(bool newGame)
        {
            _openedCapsule = null;
            _openedIn = null;
            _armedAt = -1f;
            _waitLogged = false;
            _displaysSwept = false;
            ClearSleep();
            _armed = newGame && YourBuddyPlugin.ConfigNativeSpawn.Value;
            if (_armed) YourBuddyPlugin.Log.LogInfo("[mgr] New game - the buddy will sleep in a cryo capsule beside yours");
        }

        /// <summary>
        /// A loaded save's capsule, opened again once the scene is up.
        /// </summary>
        internal static void ArmReopen(string? capsuleName)
        {
            _pendingReopen = string.IsNullOrEmpty(capsuleName) ? null : capsuleName;
        }

        /// <summary>
        /// Runs from BuddyManager.Tick once the scene is fully loaded and the player exists.
        /// </summary>
        internal static void Tick(GameManager gm)
        {
            if (_pendingReopen != null) Reopen(gm);
            if (!_displaysSwept) SweepOpenCapsuleDisplays();
            if (_armed) TrySpawn(gm);
            if (_sleeping) TickSleep(gm);
        }

        /// <summary>
        /// A buddy restored from a save that was made while it slept: it sleeps on, behind
        /// its shut door, and wakes by the same rules.
        /// </summary>
        internal static void ResumeSleep(BuddyBehaviour buddy, string? capsuleName)
        {
            if (buddy == null || string.IsNullOrEmpty(capsuleName)) return;

            foreach (CryoPod pod in Object.FindObjectsOfType<CryoPod>(true))
            {
                foreach (PropCapsule prop in PropCapsules(pod))
                {
                    if (prop.Capsule.name != capsuleName) continue;

                    Sleep(buddy, pod, prop);
                    YourBuddyPlugin.Log.LogInfo($"[mgr] Buddy still asleep in cryo capsule '{capsuleName}', as in this save");
                    return;
                }
            }
            YourBuddyPlugin.Log.LogWarning($"[mgr] Save has the buddy asleep in cryo capsule '{capsuleName}', which is not in this scene - it is awake");
        }

        private static void Reopen(GameManager gm)
        {
            string? wanted = _pendingReopen;
            _pendingReopen = null;

            foreach (CryoPod pod in Object.FindObjectsOfType<CryoPod>(true))
            {
                foreach (PropCapsule prop in PropCapsules(pod))
                {
                    if (prop.Capsule.name != wanted) continue;

                    DoorMotion motion = DoorMotion.Of(pod);
                    prop.Door.localPosition = new Vector3(0f, motion.OpenRange, -motion.SlideRange);
                    HideDisplays(prop, instantly: true);
                    _openedCapsule = wanted;
                    _openedIn = gm;
                    YourBuddyPlugin.Log.LogInfo($"[mgr] Cryo capsule '{wanted}' kept open, as in this save");
                    return;
                }
            }
            YourBuddyPlugin.Log.LogWarning($"[mgr] Save says cryo capsule '{wanted}' was opened, but it is not in this scene");
        }

        /// <summary>
        /// An open capsule is empty; its vital-signs monitor goes dark, as CryoPod.SetPowered(false) does for the real pod.
        /// </summary>
        private static void SweepOpenCapsuleDisplays()
        {
            _displaysSwept = true;
            foreach (CryoPod pod in Object.FindObjectsOfType<CryoPod>(true))
            {
                Vector3? shut = ShutDoorOffset(pod);
                if (shut == null) continue;

                foreach (PropCapsule prop in PropCapsules(pod))
                {
                    if (!IsShut(prop, shut.Value)) HideDisplays(prop, instantly: true);
                }
            }
        }

        private static void TrySpawn(GameManager gm)
        {
            if (BuddyManager.CurrentBuddy != null)
            {
                _armed = false;
                YourBuddyPlugin.Log.LogInfo("[mgr] A buddy already exists - no cryo wake-up");
                return;
            }
            if (_armedAt < 0f) _armedAt = Time.time;
            bool waitedOut = Time.time - _armedAt >= LoadWaitSeconds;

            CryoPod? pod = NearestPod(gm.PlayerShip.Pilot.transform.position);
            string? reason = "no cryo pod in this scene";
            PropCapsule? capsule = pod != null ? ClosedCapsuleBeside(pod, out reason) : null;
            if (capsule is { } prop)
            {
                if (prop.Capsule.gameObject.activeInHierarchy)
                {
                    // A capsule is only found beside a pod.
                    SleepInCapsule(pod!, prop);
                    return;
                }
                if (!waitedOut)
                {
                    LogWaitOnce($"cryo capsule '{prop.Capsule.name}'");
                    return;
                }
                reason = $"capsule '{prop.Capsule.name}' was still not loaded after {LoadWaitSeconds:0} s";
            }

            ShipyardStation station = Object.FindObjectOfType<ShipyardStation>();
            Transform? content = station != null ? GameInternals.SpaceObjectAccess.GetContentParent(station) : null;
            Transform? origin = content != null ? content : station != null ? station.transform : null;
            if (origin == null)
            {
                _armed = false;
                YourBuddyPlugin.Log.LogWarning($"[mgr] New game: {reason}, and no Shipyard station - no buddy. Use 'spawn_buddy'.");
                return;
            }
            if (!origin.gameObject.activeInHierarchy)
            {
                if (!waitedOut)
                {
                    LogWaitOnce($"'{origin.name}'");
                    return;
                }
                _armed = false;
                YourBuddyPlugin.Log.LogWarning($"[mgr] New game: {reason}, and '{origin.name}' is not loaded - no buddy. Use 'spawn_buddy'.");
                return;
            }

            // Station-local zero; the probe point sits above the floor so the floor itself is found.
            Vector3 position = origin.position;
            if (NavProbe.TryFloorHeight(position + Vector3.up * 0.5f, out float floorY)) position.y = floorY;

            if (Spawn(position, Quaternion.Euler(0f, origin.eulerAngles.y, 0f), origin) == null) return;

            YourBuddyPlugin.Log.LogInfo($"[mgr] New game: {reason} - buddy placed at the '{origin.name}' origin {position:0.00}");
        }

        private static void SleepInCapsule(CryoPod pod, PropCapsule prop)
        {
            // The player's own spawn point and facing, carried into the prop capsule (CryoController.TrySpawnPlayer).
            Vector3 position = prop.Capsule.TransformPoint(pod.transform.InverseTransformPoint(pod.SpawnPos));
            Quaternion rotation = Quaternion.Euler(0f, prop.Capsule.eulerAngles.y + 180f, 0f);
            BuddyBehaviour? buddy = Spawn(position, rotation, prop.Capsule);
            if (buddy == null) return;

            Sleep(buddy, pod, prop);
            YourBuddyPlugin.Log.LogInfo(
                $"[mgr] New game: buddy asleep in cryo capsule '{prop.Capsule.name}' beside your pod '{pod.name}' - " +
                $"wakes {WakeAfterPodOpenSeconds:0.#} s after your pod opens");
        }

        private static void Sleep(BuddyBehaviour buddy, CryoPod pod, PropCapsule prop)
        {
            buddy.Asleep = true;
            _sleeping = true;
            _sleeper = buddy;
            _sleeperPod = pod;
            _sleeperCapsule = prop;
            _wakeAt = float.MaxValue;
            _wakeReason = null;
        }

        private static void TickSleep(GameManager gm)
        {
            if (_sleeper == null || _sleeper != BuddyManager.CurrentBuddy || _sleeper.IsDead)
            {
                Transform capsule = _sleeperCapsule.Capsule;
                YourBuddyPlugin.Log.LogInfo($"[mgr] The sleeping buddy is gone - cryo capsule '{(capsule != null ? capsule.name : "?")}' stays shut");
                ClearSleep();
                return;
            }

            float now = Time.time;
            // Empty flips when CryoPod.Open starts, and is already true on a load with the pod open.
            if (_sleeperPod == null || _sleeperPod.Empty) ScheduleWake(now + WakeAfterPodOpenSeconds, "you left your pod");

            if (now >= _wakeAt) Wake(gm);
        }

        private static void ScheduleWake(float at, string reason)
        {
            if (at >= _wakeAt) return;

            _wakeAt = at;
            _wakeReason = reason;
        }

        /// <summary>
        /// The monitor goes dark and the door opens; the buddy stays put until it is fully open.
        /// </summary>
        private static void Wake(GameManager gm)
        {
            BuddyBehaviour? buddy = _sleeper;
            PropCapsule prop = _sleeperCapsule;
            DoorMotion motion = DoorMotion.Of(_sleeperPod);
            string? reason = _wakeReason;
            ClearSleep();

            HideDisplays(prop, instantly: false);
            prop.Capsule.gameObject.AddComponent<CryoCapsuleDoor>().Init(prop.Door, motion, () =>
            {
                if (buddy == null) return;

                buddy.Asleep = false;
                YourBuddyPlugin.Log.LogInfo($"[mgr] Cryo capsule '{prop.Capsule.name}' is open - buddy awake");
            });
            if (motion.OpenEvent is { IsNull: false } openEvent) RuntimeManager.PlayOneShot(openEvent, prop.Capsule.position);

            _openedCapsule = prop.Capsule.name;
            _openedIn = gm;
            YourBuddyPlugin.Log.LogInfo($"[mgr] Buddy waking ({reason}): cryo capsule '{prop.Capsule.name}' opening");
        }

        private static void ClearSleep()
        {
            _sleeping = false;
            _sleeper = null;
            _sleeperPod = null;
            _sleeperCapsule = default;
            _wakeAt = float.MaxValue;
            _wakeReason = null;
        }

        private static float WakeAfterPodOpenSeconds => Mathf.Max(0f, YourBuddyPlugin.ConfigWakeAfterPodOpen.Value);

        private static BuddyBehaviour? Spawn(Vector3 position, Quaternion rotation, Transform frame)
        {
            _armed = false;
            YourBuddyPlugin.SpawnBuddy(position, rotation);
            BuddyBehaviour? buddy = BuddyManager.CurrentBuddy;
            if (buddy == null)
            {
                YourBuddyPlugin.Log.LogWarning("[mgr] New game: the buddy could not be spawned. Use 'spawn_buddy'.");
                return null;
            }
            // Parented before its first SlowUpdate: docs/invariants.md#the-buddy-rides-its-own-floor
            BuddyManager.AttachToOwner(buddy, BuddyManager.OwnerOfTransform(frame));
            return buddy;
        }

        private static void LogWaitOnce(string what)
        {
            if (_waitLogged) return;

            _waitLogged = true;
            YourBuddyPlugin.Log.LogInfo($"[mgr] New game: waiting for {what} to load before the buddy is put to sleep there (up to {LoadWaitSeconds:0} s)");
        }

        private static void HideDisplays(PropCapsule prop, bool instantly)
        {
            foreach (CryoPodDisplay display in prop.Capsule.GetComponentsInChildren<CryoPodDisplay>(true))
            {
                if (instantly) display.EnabledInstantly = false;
                else display.Enabled = false;
            }
        }

        private static CryoPod? NearestPod(Vector3 near)
        {
            CryoPod? best = null;
            float bestSqr = float.MaxValue;
            foreach (CryoPod pod in Object.FindObjectsOfType<CryoPod>(true))
            {
                float sqr = (pod.transform.position - near).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                best = pod;
                bestSqr = sqr;
            }
            return best;
        }

        /// <summary>
        /// The nearest prop capsule whose door stands where the pod's door stands when shut.
        /// </summary>
        private static PropCapsule? ClosedCapsuleBeside(CryoPod pod, out string? reason)
        {
            List<PropCapsule> props = PropCapsules(pod);
            Vector3? shut = ShutDoorOffset(pod);
            if (props.Count == 0 || shut == null)
            {
                reason = $"no prop capsule beside pod '{pod.name}'";
                return null;
            }

            PropCapsule? best = null;
            float bestSqr = float.MaxValue;
            foreach (PropCapsule prop in props)
            {
                if (!IsShut(prop, shut.Value)) continue;

                float sqr = (prop.Capsule.position - pod.transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                best = prop;
                bestSqr = sqr;
            }
            reason = best == null ? $"every capsule beside pod '{pod.name}' is already open" : null;
            return best;
        }

        // Where the pod's door stands, in the pod's frame, with the door itself at rest.
        private static Vector3? ShutDoorOffset(CryoPod pod)
        {
            Transform? podDoor = GameInternals.CryoPodAnimatorAccess.GetDoor(pod.GetComponent<CryoPodAnimator>());
            return podDoor != null && podDoor.parent != null ? pod.transform.InverseTransformPoint(podDoor.parent.position) : null;
        }

        private static bool IsShut(PropCapsule prop, Vector3 shutOffset)
        {
            Vector3 offset = prop.Capsule.InverseTransformPoint(prop.Door.position);
            return (offset - shutOffset).sqrMagnitude <= ClosedTolerance * ClosedTolerance;
        }

        /// <summary>
        /// Siblings of the pod built from the same mesh with a door at the same path, but no
        /// CryoPod of their own: the room's decoration.
        /// </summary>
        private static List<PropCapsule> PropCapsules(CryoPod pod)
        {
            List<PropCapsule> found = [];
            Transform parent = pod.transform.parent;
            Transform? podDoor = GameInternals.CryoPodAnimatorAccess.GetDoor(pod.GetComponent<CryoPodAnimator>());
            if (parent == null || podDoor == null || podDoor == pod.transform || !podDoor.IsChildOf(pod.transform)) return found;

            string doorPath = podDoor.name;
            for (Transform t = podDoor.parent; t != pod.transform; t = t.parent) doorPath = t.name + "/" + doorPath;

            Mesh? body = pod.TryGetComponent(out MeshFilter podMesh) ? podMesh.sharedMesh : null;
            foreach (Transform sibling in parent)
            {
                if (sibling == pod.transform || sibling.GetComponent<CryoPod>() != null) continue;
                if (body != null && (!sibling.TryGetComponent(out MeshFilter mesh) || mesh.sharedMesh != body)) continue;

                Transform door = sibling.Find(doorPath);
                if (door != null) found.Add(new PropCapsule(sibling, door));
            }
            return found;
        }

        private readonly record struct PropCapsule(Transform Capsule, Transform Door);
    }

    /// <summary>
    /// How the player's pod opens, read from its CryoPodAnimator.
    /// </summary>
    internal readonly record struct DoorMotion(AnimationCurve? Curve, float Speed, float SlideRange, float OpenRange, EventReference? OpenEvent)
    {
        // CryoPodAnimator's serialized values in level1, for when reflection misses.
        private const float DefaultSpeed = 0.8f;
        private const float DefaultSlideRange = 0.061f;
        private const float DefaultOpenRange = 1.08f;

        internal static DoorMotion Of(CryoPod? pod)
        {
            CryoPodAnimator? animator = pod != null ? pod.GetComponent<CryoPodAnimator>() : null;
            return new DoorMotion(
                GameInternals.CryoPodAnimatorAccess.GetCurve(animator),
                GameInternals.CryoPodAnimatorAccess.GetSpeed(animator) ?? DefaultSpeed,
                GameInternals.CryoPodAnimatorAccess.GetSlideRange(animator) ?? DefaultSlideRange,
                GameInternals.CryoPodAnimatorAccess.GetOpenRange(animator) ?? DefaultOpenRange,
                GameInternals.CryoPodAnimatorAccess.GetOpenEvent(animator));
        }
    }

    /// <summary>
    /// CryoPodAnimator.OpenRoutine replayed on a prop capsule's door: slide out, then lift.
    /// Calls back and removes itself once the door is open.
    /// </summary>
    internal sealed class CryoCapsuleDoor : MonoBehaviour
    {
        private Transform door = null!; // set by Init right after AddComponent
        private DoorMotion motion;
        private Action? onOpen;
        private float elapsed;
        private bool lifting;

        internal void Init(Transform doorTransform, DoorMotion doorMotion, Action opened)
        {
            door = doorTransform;
            motion = doorMotion;
            onOpen = opened;
        }

        private void Update()
        {
            if (door == null)
            {
                Finish();
                return;
            }

            elapsed += Time.deltaTime * motion.Speed;
            float t = Mathf.Clamp01(elapsed);
            float k = motion.Curve?.Evaluate(t) ?? t;
            door.localPosition = lifting
                ? new Vector3(0f, k * motion.OpenRange, -motion.SlideRange)
                : new Vector3(0f, 0f, -k * motion.SlideRange);
            if (elapsed < 1f) return;

            if (!lifting)
            {
                lifting = true;
                elapsed = 0f;
                return;
            }
            door.localPosition = new Vector3(0f, motion.OpenRange, -motion.SlideRange);
            Finish();
        }

        private void Finish()
        {
            onOpen?.Invoke();
            onOpen = null;
            Destroy(this);
        }
    }
}
