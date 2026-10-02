using System.Collections.Generic;
using NPC.Core.World;
using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using Space;
using UnityEngine;
using static YourBuddy.Items;

namespace YourBuddy
{
    /// <summary>
    /// Now and then, with nothing else to do, eat or drink something nearby. The food lies about or
    /// sits in a fridge, cabinet, chest or locker, which is opened and closed again. docs/snacks.md
    /// </summary>
    internal sealed class SnackErrand(IErrandBody body) : Errand(body)
    {
        private const float SnackMinInterval = 60f;
        /// <summary>
        /// After no food to go to, or a snack that ended without eating.
        /// </summary>
        private const float SnackRetryDelay = 120f;
        private const float SnackSkipSeconds = 600f;
        private const float SnackSearchRadius = 25f;
        /// <summary>
        /// Snacks tried with a plan before giving up; one already in reach is always taken.
        /// </summary>
        private const int SnackMaxPlans = 3;
        private const float SnackPickSeconds = 0.8f;
        private const float SnackEatSeconds = 2f;
        /// <summary>
        /// Satiety at or below which the player has the Hunger buff (Space/Player.cs).
        /// </summary>
        private const int PlayerHungryAtOrBelow = 250;

        private static readonly List<SnackTask> SnackSpots = [];
        private static readonly List<Food> SnackFoodBuffer = [];
        private static readonly HashSet<Food> ContainedFood = [];

        public override bool Enabled => YourBuddyPlugin.ConfigSnacks.Value;
        public override float Interval => Mathf.Max(SnackMinInterval, YourBuddyPlugin.ConfigSnackIntervalMinutes.Value * 60f);
        public override float RetryDelay => SnackRetryDelay;
        protected override float SkipSeconds => SnackSkipSeconds;
        protected override string Command => "buddy_order snack";
        protected override string Topic => "Snack";

        private enum SnackPhase { Walk, Look, Eat }

        private sealed class SnackTask : ErrandLeg
        {
            private readonly SnackErrand errand;
            public readonly Door[] Doors;
            /// <summary>
            /// A container's Furniture.itemMover, which moves what is inside with it. Null for loose food.
            /// </summary>
            public readonly InstantItemDetector? Contents;
            public readonly HidingSpot? Hideout;
            /// <summary>
            /// The item itself when it lies about, not in a container.
            /// </summary>
            public readonly Food? Item;
            private readonly string kindName;
            public readonly int FoodCount;
            public SnackPhase Phase = SnackPhase.Walk;
            public float PhaseUntil;
            /// <summary>
            /// Only these are closed again. A door the buddy found open stays open.
            /// </summary>
            public readonly List<Door> OpenedDoors = [];

            public SnackTask(SnackErrand errand, Transform container, Door[] doors, InstantItemDetector contents,
                             HidingSpot? hideout, Vector3 point, int foodCount)
                : base(point, container)
            {
                this.errand = errand;
                Doors = doors;
                Contents = contents;
                Hideout = hideout;
                kindName = ContainerKind(container);
                FoodCount = foodCount;
            }

            public SnackTask(SnackErrand errand, Food item, Vector3 point) : base(point, item.transform)
            {
                this.errand = errand;
                Doors = [];
                Item = item;
                kindName = BaseName(item.gameObject.name);
                FoodCount = 1;
            }

            public override string Name => "the " + kindName;
            public override float Reach => SnackReachDist;
            public override float[] StandOffs => SnackStandOffs;
            public override float ReachBelow => SnackReachBelow;

            public override void Defer(float seconds) => errand.Defer(Own, seconds);

            public override Vector3 Approach(out bool wantMove) => errand.Approach(this, out wantMove);

            public override void End() => CloseOpenedDoors(OpenedDoors, Hideout, Name);

            public override string Describe() => "getting a snack from " + Name;
        }

        public override bool TryStart(out string report)
        {
            if (PlayerIsHungry(out int satiety))
            {
                report = $"you are hungry (satiety {satiety}) - the food is yours";
                return Failed(report);
            }

            CollectSnackSpots(out int emptyContainers, out int skipped);
            if (SnackSpots.Count == 0)
            {
                report = $"there is no food within {SnackSearchRadius:0}m" +
                         (emptyContainers > 0 ? $" ({emptyContainers} empty container(s) left shut)" : "") +
                         (skipped > 0 ? $" ({skipped} I could not reach lately)" : "");
                return Failed(report);
            }

            string? failure = null;
            int plans = 0;
            foreach (SnackTask task in SnackSpots)
            {
                SetOffResult set = SetOff(task, Begin, ref plans, SnackMaxPlans, ref failure);
                if (set == SetOffResult.NoPlansLeft) break;
                if (set == SetOffResult.NoPlan) continue;

                string what = task.Item != null ? "lying about" : $"{task.FoodCount} thing(s) to eat in it";
                report = $"is getting a snack: {task.Name}, {what}, {HowFar(set, task)}";
                YourBuddyPlugin.Log.LogInfo($"[mind] Decided: get a snack from {task.Name} - {what}, " +
                                            (set == SetOffResult.InReach ? "right here" : $"via node {task.Node:0.0}, then {task.StandPoint:0.0}"));
                return true;
            }
            report = $"none of the {SnackSpots.Count} snack(s) nearby can be reached ({failure})";
            return Failed(report);
        }

        public override int Count(out float nearest)
        {
            CollectSnackSpots(out _, out _);
            nearest = Nearest(SnackSpots.Count, i => SnackSpots[i].TargetPoint);
            return SnackSpots.Count;
        }

        /// <summary>
        /// A null plan means food already in reach, with nothing for the Route to walk. Only eating schedules the
        /// full interval; until then a failed walk tries again after SnackRetryDelay.
        /// </summary>
        private void Begin(SnackTask task, NavPath? plan) => Begin(task, plan, SnackRetryDelay, "on the way to " + task.Name);

        /// <summary>
        /// Every snack near the buddy, nearest first. Containers that hold food, and food lying about.
        /// A container without food is never a snack.
        /// </summary>
        private void CollectSnackSpots(out int emptyContainers, out int skipped)
        {
            SnackSpots.Clear();
            ContainedFood.Clear();
            CheckedContainers.Clear();
            Skips.Prune();
            emptyContainers = 0;
            skipped = 0;
            Vector3 here = Here;
            float floorY = Body.FloorUnderBuddy().y;
            float margin = SnackSearchRadius + SnackContainerMargin;

            foreach (Door door in SceneScan.ThisFrame<Door>())
            {
                Transform? container = ContainerOf(door, out InstantItemDetector? contents);
                // A closet has two doors; the first found brings both.
                if (container == null || !CheckedContainers.Add(container)) continue;

                Door[] doors = container.GetComponentsInChildren<Door>();
                Vector3 point = DoorFacePoint(doors);
                if (FlatDistanceSq(point, here) > margin * margin) continue;

                // Both were found together. Its contents are not loose food, even when it is out of bounds.
                int count = FoodIn(contents!, SnackFoodBuffer);
                ContainedFood.UnionWith(SnackFoodBuffer);
                if (!InBounds(point, here, floorY, container)) continue;

                if (count == 0)
                {
                    emptyContainers++;
                    continue;
                }
                CustomRoom room = container.GetComponentInParent<CustomRoom>(true);
                if (room != null && !room.EnabledStructure) continue;

                SnackTask task = new(this, container, doors, contents!, container.GetComponent<HidingSpot>(), point, count);
                if (SnackBlocker(task) != null) continue;

                if (Skips.Has(container)) skipped++;
                else SnackSpots.Add(task);
            }
            CheckedContainers.Clear();

            foreach (Food food in SceneScan.ThisFrame<Food>())
            {
                if (ContainedFood.Contains(food) || !Edible(food) || Body.TakenByAnother(food.transform)) continue;

                Vector3 point = ItemPoint(food);
                if (!InBounds(point, here, floorY, food.transform)) continue;

                if (Skips.Has(food.transform)) skipped++;
                else SnackSpots.Add(new SnackTask(this, food, point));
            }

            SnackSpots.Sort((a, b) => FlatDistanceSq(a.TargetPoint, here).CompareTo(FlatDistanceSq(b.TargetPoint, here)));
            ShuffleNearest(SnackSpots);
        }

        private bool InBounds(Vector3 point, Vector3 here, float floorY, Transform what) =>
            FlatDistanceSq(point, here) <= SnackSearchRadius * SnackSearchRadius &&
            point.y >= floorY - SnackReachBelow && point.y <= floorY + ReachTask.ReachHeight && Body.OnMyVessel(what);

        /// <summary>
        /// The top of the item, the part visible on a floor or a shelf.
        /// </summary>
        private static Vector3 ItemPoint(Food food)
        {
            if (!ColliderBounds(food.gameObject, out Bounds bounds)) return food.transform.position;

            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        /// <summary>
        /// Why the snack is off limits now, or null. Checked at every step of the task.
        /// </summary>
        private string? SnackBlocker(SnackTask task)
        {
            if (task.Own == null || !task.Own.gameObject.activeInHierarchy) return "it is gone";

            if (task.Item != null)
            {
                if (!Edible(task.Item)) return "someone took it or it is used up";

                return MovedBlocker(ItemPoint(task.Item), task.TargetPoint);
            }
            // Opening or closing a hiding spot's door sets the player's hidden flag. docs/snacks.md §1
            if (task.Hideout != null && task.Hideout.CurrentInteractor != null) return "you are hiding in it";

            // Another buddy snacking or hiding there. docs/invariants.md#one-buddy-per-target
            return Body.TakenByAnother(task.Own) ? "another buddy is using it" : null;
        }

        /// <summary>
        /// Not used up, and not in someone's hands.
        /// </summary>
        private static bool Edible(Food? food)
        {
            if (food == null || !food.gameObject.activeInHierarchy || food.Data == null || food.Data.usages == 0) return false;

            return !food.TryGetComponent(out Grabbable grabbable) || !grabbable.IsGrabbed;
        }

        private static int FoodIn(InstantItemDetector contents, List<Food> into)
        {
            into.Clear();
            foreach (Grabbable item in contents.GetItemsInZone())
            {
                if (item.TryGetComponent(out Food food) && Edible(food)) into.Add(food);
            }
            return into.Count;
        }

        /// <summary>
        /// The player's own Hunger band (Space/Player.cs). The buddy does not need the food.
        /// </summary>
        private bool PlayerIsHungry(out int satiety)
        {
            Player? player = NpcPlayer.Pilot;
            satiety = player != null && player.Data != null ? player.Data.HealthSystemData.Satiety : int.MaxValue;
            return satiety <= PlayerHungryAtOrBelow;
        }

        /// <summary>
        /// From UpdateRoute once a snack route's plan is walked. Gets into reach, opens, eats, and closes on finishing.
        /// </summary>
        private Vector3 Approach(SnackTask task, out bool wantMove)
        {
            wantMove = false;
            string? blocker = SnackBlocker(task);
            if (blocker != null) return Leave(task.Name + " alone", blocker);

            switch (task.Phase)
            {
                case SnackPhase.Walk:
                    if (!Body.StepIntoReach(task, out Vector3 move, out wantMove)) return move;

                    if (task.Item == null) OpenContainerDoors(task.Doors, task.OpenedDoors, task.Name);
                    task.Phase = SnackPhase.Look;
                    task.PhaseUntil = Time.time + (task.Item == null ? SnackOpenSeconds : SnackPickSeconds);
                    break;
                case SnackPhase.Look:
                    if (Time.time < task.PhaseUntil) break;

                    EatFrom(task);
                    task.Phase = SnackPhase.Eat;
                    task.PhaseUntil = Time.time + SnackEatSeconds;
                    break;
                case SnackPhase.Eat:
                    if (Time.time < task.PhaseUntil) break;

                    // FinishRoute ends the task, which closes the doors.
                    Body.FinishRoute();
                    return Vector3.zero;
            }
            Body.StandFacing(task.TargetPoint);
            return Vector3.zero;
        }

        /// <summary>
        /// Eating is what schedules the next snack a full interval away.
        /// </summary>
        private void EatFrom(SnackTask task)
        {
            Food? meal = task.Item;
            int count = 1;
            if (task.Contents != null)
            {
                count = FoodIn(task.Contents, SnackFoodBuffer);
                meal = count > 0 ? SnackFoodBuffer[Random.Range(0, count)] : null;
            }
            if (meal == null)
            {
                Last = $"nothing left to eat in {task.Name}";
                YourBuddyPlugin.Log.LogInfo($"[ai] Nothing left to eat in {task.Name}");
                return;
            }
            if (PlayerIsHungry(out int satiety))
            {
                Last = "left the food to you - you are hungry";
                YourBuddyPlugin.Log.LogInfo($"[ai] Leaving {task.Name}'s food to you - you are hungry (satiety {satiety})");
                return;
            }

            string itemName = meal.gameObject.name;
            string verb = meal is Drink ? "Drank" : "Ate";
            string from = task.Item != null ? "" : $" from {task.Name} ({count - 1} left)";
            // The game's own consume without an eater. Plays the sound, uses it up, turns it to trash.
            meal.Consume();
            DueAt = Time.time + NextInterval();
            Last = $"{verb.ToLowerInvariant()} '{itemName}'{from}";
            YourBuddyPlugin.Log.LogInfo($"[ai] {verb} '{itemName}'{from}");
        }
    }
}
