using NPC.Core;
using NPC.Core.Agents;
using NPC.Core.Navigation;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Life-support terminals: when the air aboard turns dangerous and the unit that would fix
    /// it is switched off, walk to it and flip its power switch. docs/terminals.md
    /// </summary>
    internal sealed class LifeSupport(IErrandBody body)
    {
        /// <summary>
        /// The player's own buff bands (Space/Player.cs), as in AtmosphereTick.
        /// </summary>
        private const int OxygenDangerBelow = 1950;
        private const int ColdDangerBelow = 1400;
        private const int HeatDangerAbove = 3200;
        private const float TerminalRetryDelay = 60f;
        private const float TerminalVerifyDelay = 1.5f;

        private TerminalTask? terminalCheck;
        private float terminalCheckAt;
        // Indexed by TerminalKind.
        private readonly float[] terminalRetryAt = new float[2];
        private readonly float[] terminalTraceAt = new float[2];

        private enum TerminalKind { Oxygen, Climate }

        private sealed class TerminalTask(LifeSupport owner, TerminalKind kind, Controller controller, Switch powerSwitch)
            : ErrandLeg(powerSwitch.transform.position, controller.transform)
        {
            public readonly TerminalKind Kind = kind;
            public readonly Controller Controller = controller;
            public readonly Switch PowerSwitch = powerSwitch;

            public override string Name => "the " + TerminalName(Kind);

            public override void Defer(float seconds) => owner.terminalRetryAt[(int)Kind] = Time.time + seconds;

            public override Vector3 Approach(out bool wantMove) => owner.Approach(this, out wantMove);

            public override string Describe() => "switching on " + Name;

            // A flee resumes a terminal route. docs/snacks.md §2
            public override bool EndsOnFlee => false;
        }

        private static string TerminalName(TerminalKind kind) =>
            kind == TerminalKind.Oxygen ? "oxygen generator" : "climate control";

        /// <summary>
        /// Whether the air aboard is in a band a terminal could fix. The full question - whether a unit
        /// is off, may be switched on and can be walked to - is TryStart's; this is only what the decider
        /// weighs. docs/terminals.md
        /// </summary>
        public bool AirIsDangerous()
        {
            if (!YourBuddyPlugin.ConfigTerminals.Value || !body.IsAboardPlayerShip()) return false;

            // IsAboardPlayerShip is false without a player ship.
            Environment env = GameManager.Instance.PlayerShip!.Environment;
            if (env == null || env.Data == null) return false;

            int temperature = body.FeltTemperature(env);
            return env.Data.Oxygen < OxygenDangerBelow ||
                   temperature < ColdDangerBelow || temperature > HeatDangerAbove;
        }

        /// <summary>
        /// From the decider, once life support has won the round. True when the buddy set off for a terminal.
        /// </summary>
        public bool TryStart()
        {
            if (!YourBuddyPlugin.ConfigTerminals.Value || !body.IsAboardPlayerShip()) return false;

            // IsAboardPlayerShip is false without a player ship.
            SpaceShip ship = GameManager.Instance.PlayerShip!;
            Environment env = ship.Environment;
            if (env == null || env.Data == null) return false;

            int oxygen = env.Data.Oxygen;
            // Danger as the buddy feels it; the log shows the room.
            int temperature = body.FeltTemperature(env);
            if (oxygen < OxygenDangerBelow &&
                TryStartTerminal(TerminalKind.Oxygen, ship.OxygenController, $"oxygen at {oxygen / 100f:0.0}%", false, out _))
            {
                return true;
            }
            return (temperature < ColdDangerBelow || temperature > HeatDangerAbove) &&
                   TryStartTerminal(TerminalKind.Climate, ship.ClimatController, $"temperature at {env.Data.Temperature / 100f:0.0}C", false, out _);
        }

        /// <summary>
        /// buddy_terminal: switch the unit on now, air or no air, retry timer or not - but only one that is
        /// off and may be switched on: docs/invariants.md#a-terminal-is-only-switched-on
        /// </summary>
        public string StartNow(string which)
        {
            TerminalKind kind;
            if (which.StartsWith("o", System.StringComparison.OrdinalIgnoreCase)) kind = TerminalKind.Oxygen;
            else if (which.StartsWith("c", System.StringComparison.OrdinalIgnoreCase)) kind = TerminalKind.Climate;
            else return "Which one: oxygen or climate?";

            string? busy = body.BusyForCommand();
            if (busy != null) return busy;

            if (!body.IsAboardPlayerShip()) return body.Name + " is not aboard your ship";

            // IsAboardPlayerShip is false without a player ship.
            SpaceShip ship = GameManager.Instance.PlayerShip!;
            Controller? controller = kind == TerminalKind.Oxygen ? ship.OxygenController : ship.ClimatController;
            return TryStartTerminal(kind, controller, "you asked", true, out string report)
                ? body.Name + " " + report
                : "Not switching on the " + TerminalName(kind) + ": " + report;
        }

        /// <summary>
        /// `forced` skips the retry timer and the blocker trace. `report` finishes a sentence either way.
        /// </summary>
        private bool TryStartTerminal(TerminalKind kind, Controller? controller, string why, bool forced, out string report)
        {
            string? blocker = TerminalBlocker(controller, out Switch? powerSwitch);
            // controller is set whenever TerminalBlocker found nothing. docs/invariants.md#one-buddy-per-target
            if (blocker == null && body.TakenByAnother(controller!.transform)) blocker = "another buddy is on it";
            if (blocker != null)
            {
                report = blocker;
                // A running unit is the normal case, not a stall.
                if (!forced) TraceTerminal(kind, why + ", but " + blocker, controller != null && controller.Enabled ? 2 : 1);
                return false;
            }
            if (!forced && Time.time < terminalRetryAt[(int)kind])
            {
                report = $"retrying in {terminalRetryAt[(int)kind] - Time.time:0}s";
                TraceTerminal(kind, why + ", " + report, 1);
                return false;
            }

            // A null blocker means both were found.
            TerminalTask task = new(this, kind, controller!, powerSwitch!);
            if (body.InReach(task))
            {
                report = "switched on the " + TerminalName(kind) + " right here";
                YourBuddyPlugin.Log.LogInfo("[mind] Decided: switch on the " + TerminalName(kind) + " - " + why + ", right here");
                Operate(task);
                return true;
            }

            string? failure = body.PlanReach(task, out NavPath plan);
            if (failure != null)
            {
                report = failure;
                task.Defer(ReachTask.ReachReplanDelay);
                YourBuddyPlugin.Log.LogInfo("[mind] Wanted to switch on the " + TerminalName(kind) + " (" + why + "), but " +
                                            failure + $" - retrying in {ReachTask.ReachReplanDelay:0}s");
                return false;
            }

            body.Walk(task, plan);
            report = $"is walking to the {TerminalName(kind)}, {Vector3.Distance(body.Transform.position, task.TargetPoint):0.0}m away";
            YourBuddyPlugin.Log.LogInfo($"[mind] Decided: switch on the {TerminalName(kind)} - {why}, " +
                                        $"via node {task.Node:0.0}, then {task.StandPoint:0.0}");
            return true;
        }

        /// <summary>
        /// For the HUD and buddy_mind: the air aboard against the danger bands, and each unit's retry timer.
        /// </summary>
        public string Describe()
        {
            string text;
            if (!YourBuddyPlugin.ConfigTerminals.Value)
            {
                text = "terminals off (buddy_terminal still works)";
            }
            else if (!body.IsAboardPlayerShip() || GameManager.Instance.PlayerShip!.Environment is not { Data: not null } env)
            {
                // IsAboardPlayerShip is false without a player ship.
                text = "not aboard your ship";
            }
            else
            {
                int temperature = body.FeltTemperature(env);
                bool lowOxygen = env.Data.Oxygen < OxygenDangerBelow;
                bool badTemperature = temperature < ColdDangerBelow || temperature > HeatDangerAbove;
                text = $"O2 {env.Data.Oxygen / 100f:0.0}%{(lowOxygen ? " LOW" : "")}, " +
                       $"{env.Data.Temperature / 100f:0.0}C{(badTemperature ? " BAD" : "")}" +
                       (lowOxygen || badTemperature ? " - wants a unit on" : " - fine");
            }
            for (int i = 0; i < terminalRetryAt.Length; i++)
            {
                float wait = terminalRetryAt[i] - Time.time;
                if (wait > 0f) text += $"; {TerminalName((TerminalKind)i)} retry in {wait:0}s";
            }
            return text;
        }

        /// <summary>
        /// Why the buddy cannot help with this unit, or null with its power switch.
        /// It only ever switches a unit on: docs/invariants.md#a-terminal-is-only-switched-on
        /// </summary>
        private static string? TerminalBlocker(Controller? controller, out Switch? powerSwitch)
        {
            powerSwitch = null;
            if (controller == null || controller.Data == null) return "this ship has none";

            CustomRoom room = controller.GetComponentInParent<CustomRoom>(true);
            if (room != null && !room.EnabledStructure) return "its room is not built";

            if (controller.Data.broken) return "it is broken and needs a repair";

            if (!controller.Powered) return "it has no power";

            if (controller.Enabled) return "it is already running";

            powerSwitch = PowerSwitchOf(controller);
            if (powerSwitch == null) return "it has no power switch I can find";
            // Switch on, unit off: a fault (OxygenError / ClimateError) the player fixes by hand.
            if (powerSwitch.Enabled) return "its switch is on and it is still off - a fault only you can clear";

            return null;
        }

        /// <summary>
        /// The unit's own power switch: a Switch on a direct child. Climate's format switch sits deeper.
        /// </summary>
        private static Switch? PowerSwitchOf(Controller controller)
        {
            foreach (Transform child in controller.transform)
            {
                if (child.TryGetComponent(out Switch found)) return found;
            }
            return null;
        }

        /// <summary>
        /// UpdateRoute, once a terminal route's plan is walked: the last steps to the switch.
        /// </summary>
        private Vector3 Approach(TerminalTask task, out bool wantMove)
        {
            wantMove = false;
            string? blocker = task.PowerSwitch == null ? "its switch is gone" : TerminalBlocker(task.Controller, out _);
            if (blocker != null)
            {
                YourBuddyPlugin.Log.LogInfo("[ai] No need to switch on the " + TerminalName(task.Kind) + " any more - " + blocker);
                body.FinishRoute();
                return Vector3.zero;
            }

            if (!body.StepIntoReach(task, out Vector3 move, out wantMove)) return move;

            Operate(task);
            return Vector3.zero;
        }

        /// <summary>
        /// Through the switch's own SwitchState, so the scene's wiring powers the unit.
        /// </summary>
        private void Operate(TerminalTask task)
        {
            task.Defer(TerminalRetryDelay);
            body.FacePoint(task.PowerSwitch.transform.position);
            task.PowerSwitch.SwitchState();
            terminalCheck = task;
            terminalCheckAt = Time.time + TerminalVerifyDelay;
            YourBuddyPlugin.Log.LogInfo("[ai] Switched on the " + TerminalName(task.Kind) + " ('" + task.Controller.gameObject.name + "')");
            if (body.OnRoute) body.FinishRoute();
        }

        /// <summary>
        /// SlowUpdate phase 3: did the flip actually start the unit?
        /// </summary>
        public void Update()
        {
            if (terminalCheck == null || Time.time < terminalCheckAt) return;

            TerminalTask task = terminalCheck;
            terminalCheck = null;
            if (task.Controller == null) return;

            if (task.Controller.Enabled)
            {
                YourBuddyPlugin.Log.LogInfo("[ai] The " + TerminalName(task.Kind) + " is running");
                return;
            }
            YourBuddyPlugin.Log.LogWarning("[ai] Flipped the " + TerminalName(task.Kind) + "'s switch, but it is still off " +
                $"(switch={(task.PowerSwitch != null && task.PowerSwitch.Enabled)}, powered={task.Controller.Powered}, " +
                $"broken={task.Controller.Data.broken})");
        }

        /// <summary>
        /// Why a dangerous atmosphere is not being fixed, throttled per unit. docs/logging.md §4
        /// </summary>
        private void TraceTerminal(TerminalKind kind, string line, int level)
        {
            if (NpcLog.Level < level || Time.time < terminalTraceAt[(int)kind]) return;

            terminalTraceAt[(int)kind] = Time.time + 30f;
            YourBuddyPlugin.Log.LogInfo("[mind] Not switching on the " + TerminalName(kind) + ": " + line);
        }
    }
}
