using System.Collections.Generic;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Fear of the Breathless: seeing it, stress and state, holding back while Alert, and
    /// the Flee mode while Scared. docs/fear.md
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // ReSharper disable RedundantDefaultMemberInitializer
        private float stress = 0f;

        // Fear of the Breathless. Stress runs 0..FearStressCap, rates per second. docs/fear.md
        /// <summary>
        /// Nothing is cast at a monster further away than this.
        /// </summary>
        private const float FearSightRange = 18f;
        /// <summary>
        /// This near and in sight, Scared at once, whichever way it faces. Above the 1.7 m catch.
        /// </summary>
        private const float FearPanicDist = 2.5f;
        /// <summary>
        /// Calm, it notices the monster only within 65 degrees of where it faces. docs/fear.md §2
        /// </summary>
        private const float FearViewCos = 0.42f;
        /// <summary>
        /// Stress per second for a monster in view at the edge of sight range, multiplied
        /// by up to (1 + FearProximityGain) as it closes to FearPanicDist, and by up to
        /// (1 + FearApproachGain) while it closes in at FearApproachSpeed or faster.
        /// </summary>
        private const float FearStressRate = 0.15f;
        private const float FearProximityGain = 2.5f;
        private const float FearApproachGain = 0.5f;
        private const float FearApproachSpeed = 2f;
        private const float FearDecayRate = 0.4f;
        private const float FearStressCap = 5f;
        // Hysteresis: Alert enters at FearAlertEnter and leaves below FearAlertExit; Scared
        // enters at FearScaredEnter and leaves below FearAlertEnter.
        private const float FearAlertEnter = 1f;
        private const float FearAlertExit = 0.5f;
        private const float FearScaredEnter = 3f;
        /// <summary>
        /// Where the buddy looks from, above its feet.
        /// </summary>
        private const float FearEyeHeight = 1.5f;
        private const float FearRestraintCos = 0.5f;
        /// <summary>
        /// A flee with nowhere left to go for this long is a stalemate: the buddy has run as
        /// far as it can and the monster is still in sight. docs/fear.md §5
        /// </summary>
        private const float FleeStalemateSeconds = 6f;
        /// <summary>
        /// What the stress eases back to while stalemated, and no lower while it still sees it:
        /// between FearAlertExit and FearAlertEnter, so the buddy settles at Alert, not Calm.
        /// </summary>
        private const float FearStalemateStress = 0.75f;
        /// <summary>
        /// A stalemate is off again once the monster has closed this much on where it stood
        /// when the flee ran out of options - from there, there may be somewhere to run after all.
        /// </summary>
        private const float FleeStalemateClosed = 2f;
        private float fearTickAt = 0f;
        private float fearTraceAt = 0f;
        private float fearGateLogAt = 0f;
        private float fearHoldLogAt = 0f;
        private bool monsterInSight = false;
        /// <summary>
        /// In a clear line but outside the view cone at the last tick.
        /// </summary>
        private bool monsterBehind = false;
        /// <summary>
        /// monsterDist at the previous tick if it was in sight then, else MaxValue: the closing speed.
        /// </summary>
        private float seenDistBefore = float.MaxValue;
        /// <summary>
        /// When the flee last ran out of things to try - no hide, no retreat, no back-away, or a
        /// player it cannot run to - else 0. Cleared the moment anything works. docs/fear.md §5
        /// </summary>
        private float fleeStuckSince = 0f;
        /// <summary>
        /// monsterDist when that clock started: the stalemate breaks if it closes on that.
        /// </summary>
        private float fleeStuckDist = float.MaxValue;
        /// <summary>
        /// The stalemate itself: stuck for FleeStalemateSeconds with the monster not within
        /// FearPanicDist. The stress eases to FearStalemateStress instead of staying pinned.
        /// </summary>
        private bool fearStalemate = false;
        private float fearStalemateLogAt = 0f;

        /// <summary>
        /// The hide-or-run bias (docs/fear.md §5): multiplied when the monster is watching, and again
        /// when it is this near - climbing into a closet in front of it is not hiding.
        /// </summary>
        private const float HideSeenFactor = 0.35f;
        private const float HideMonsterClose = 6f;
        /// <summary>
        /// Added to the bias when the last retreat found nowhere to run to.
        /// </summary>
        private const float HideNoRetreatBias = 0.3f;
        private float fleeRetreatSince = 0f;
        private float fleeClearanceCheckAt = 0f;
        private float fleeHoldLogAt = 0f;
        /// <summary>
        /// The last retreat attempt found no node to run to: hiding is worth more. docs/fear.md §5
        /// </summary>
        private bool fleeRetreatFailed = false;
        private Vector3 fleeTarget = Vector3.zero;
        private Vector3 fleeFailedTarget = Vector3.zero;
        private float fleeFailedTargetUntil = 0f;
        private readonly List<Vector3> fleeCandidates = [];
        private readonly List<KeyValuePair<float, Vector3>> fleeScored = [];
        /// <summary>
        /// A retreat leg that starts outside this from the monster may not come back within it.
        /// </summary>
        private const float FleeClearance = 3f;
        /// <summary>
        /// A retreat node must be at least this much further from the monster than the buddy is.
        /// </summary>
        private const float FleeMinGain = 4f;
        /// <summary>
        /// Beyond this from the monster, a retreat node is no safer for being further.
        /// </summary>
        private const float FleeSafeDist = 15f;
        /// <summary>
        /// Score cost per metre from a retreat node to the player, and per metre of trip.
        /// </summary>
        private const float FleePlayerWeight = 0.5f;
        private const float FleeTripWeight = 0.1f;
        /// <summary>
        /// Retreat nodes tried per plan, each one A*.
        /// </summary>
        private const int FleePickAttempts = 5;
        private const float FleeRetryDelay = 0.5f;
        private const float FleeHoldRetryDelay = 1.5f;
        private const float FleeBackAwayDist = 3f;
        private const float FleeBackAwayTime = 1f;
        // ReSharper restore RedundantDefaultMemberInitializer

        // ------------------------------------------------------------------
        // Stress and state
        // ------------------------------------------------------------------

        /// <summary>
        /// Phase 2 of SlowUpdate. That runs ahead of Update's catch and dialog early-outs,
        /// so this guards itself against both.
        /// </summary>
        private void UpdateFear()
        {
            if (IsDead || catchInProgress) return;

            // ai_disable leaves a monster that does nothing; cowering at it would be reacting
            // to an AI that is switched off. docs/reference.md
            if (!YourBuddyPlugin.ConfigFear.Value || AiDebug.MonsterDisabled)
            {
                // An ordered hide is not fear's to end: buddy_hide skips the fear setting going in,
                // so the same setting must not pull the buddy straight back out. docs/fear.md §6
                if (Hiding && !hideOrdered)
                {
                    ForceLeaveHidingSpot(YourBuddyPlugin.ConfigFear.Value ? "the monster is switched off" : "fear is switched off");
                }

                ResetFear();
                return;
            }

            // Measured, like AtmosphereTick: the phase interval is not a constant anywhere.
            float dt = fearTickAt > 0f ? Mathf.Clamp(Time.time - fearTickAt, 0f, 1f) : 0.24f;
            fearTickAt = Time.time;

            bool seen = false;
            bool panic = false;
            monsterInSight = false;
            monsterBehind = false;
            monsterDist = float.MaxValue;
            Breathless? breathless = GameManager.Instance != null ? GameManager.Instance.Breathless : null;
            if (breathless != null && breathless.gameObject.activeInHierarchy)
            {
                Vector3 monster = breathless.transform.position;
                monsterDist = Vector3.Distance(monster, transform.position);
                // The cloak hides it from the player, not from the buddy. docs/fear.md §2
                if (monsterDist <= FearSightRange && MonsterInSight(breathless))
                {
                    if (InViewCone(monster))
                    {
                        monsterInSight = true;
                        seen = true;
                        panic = monsterDist <= FearPanicDist;
                        lastMonsterPos = monster;
                    }
                    else
                    {
                        monsterBehind = true;
                    }
                }
            }

            // Behind shut doors it neither sees nor is seen: the stress decays. docs/fear.md §6
            if (hideState == HideState.Hidden)
            {
                if (seen || monsterBehind) hideMonsterSeenAt = Time.time;

                seen = false;
                panic = false;
                monsterInSight = false;
                monsterBehind = false;
            }

            UpdateStalemate(panic);

            float rate;
            if (panic)
            {
                rate = 0f;
                stress = FearStressCap;
            }
            else if (seen && fearStalemate)
            {
                // Run as far as it can be run, and it is still watching: standing there with the
                // stress pinned is the deadlock this breaks. Ease back to Alert and get on with
                // life, warily - it still holds back from walking at it. docs/fear.md 5
                rate = stress > FearStalemateStress ? -FearDecayRate : 0f;
            }
            else if (seen)
            {
                float closeness = 1f - Mathf.Clamp01((monsterDist - FearPanicDist) / (FearSightRange - FearPanicDist));
                float closing = seenDistBefore < float.MaxValue && dt > 0f ? (seenDistBefore - monsterDist) / dt : 0f;
                float approach = Mathf.Clamp01(closing / FearApproachSpeed);
                rate = FearStressRate * (1f + FearProximityGain * closeness) * (1f + FearApproachGain * approach);
            }
            else
            {
                rate = -FearDecayRate;
            }

            stress = Mathf.Clamp(stress + rate * dt, 0f, FearStressCap);
            // While it can still see it, the stalemate settles at Alert rather than decaying to Calm.
            if (seen && fearStalemate) stress = Mathf.Max(stress, FearStalemateStress);

            seenDistBefore = seen ? monsterDist : float.MaxValue;

            SetFearState(NextFearState(panic), panic, seen);
            TraceFear(rate);

            if (fearState == FearState.Scared)
            {
                // A level, not an edge: a flee held back by the dialog starts once it closes.
                if (mode != BuddyMode.Flee)
                {
                    if (!inDialog) StartFlee(panic);
                }
                else if (panic && fleePhase != FleePhase.Retreat && Time.time >= fleeRetryAt)
                {
                    // Caught up with beside the player, or while holding: run again.
                    TryStartRetreat();
                }
            }
            else if (mode == BuddyMode.Flee && !Hiding)
            {
                EndFlee();
            }
        }

        /// <summary>
        /// The flee ran out of things to try: start the stalemate clock, or leave it running.
        /// </summary>
        private void MarkFleeStuck()
        {
            if (fleeStuckSince > 0f) return;

            fleeStuckSince = Time.time;
            fleeStuckDist = monsterDist;
        }

        /// <summary>
        /// Something worked - a retreat, a hide, a back-away, a run to the player: no stalemate.
        /// </summary>
        private void ClearFleeStuck()
        {
            fleeStuckSince = 0f;
            fleeStuckDist = float.MaxValue;
        }

        /// <summary>
        /// Stuck with nowhere to go for FleeStalemateSeconds, and the monster not within
        /// FearPanicDist, is a stalemate. It breaks the moment the monster panics the buddy or
        /// closes FleeStalemateClosed on where it stood when the clock started - from there the
        /// nodes it could not use may be usable. docs/fear.md 5
        /// </summary>
        private void UpdateStalemate(bool panic)
        {
            if (panic || mode != BuddyMode.Flee || Hiding)
            {
                ClearFleeStuck();
                SetStalemate(false);
                return;
            }
            if (fleeStuckSince > 0f && monsterDist < fleeStuckDist - FleeStalemateClosed)
            {
                // It has come at the buddy since: try everything again, frightened.
                ClearFleeStuck();
                SetStalemate(false);
                return;
            }
            SetStalemate(fleeStuckSince > 0f && Time.time - fleeStuckSince >= FleeStalemateSeconds);
        }

        private void SetStalemate(bool next)
        {
            if (next == fearStalemate) return;

            fearStalemate = next;
            if (!next || YourBuddyPlugin.ConfigDebugLevel.Value < 1 || Time.time < fearStalemateLogAt) return;

            fearStalemateLogAt = Time.time + 10f;
            YourBuddyPlugin.Log.LogInfo(
                $"[fear] Nowhere left to run from the Breathless {monsterDist:0.0}m away for " +
                $"{FleeStalemateSeconds:0}s - easing off, staying Alert instead of staring at it");
        }

        /// <summary>
        /// Hysteresis on both thresholds, or the state chatters at the boundary.
        /// </summary>
        private FearState NextFearState(bool panic)
        {
            if (panic || stress >= FearScaredEnter) return FearState.Scared;

            switch (fearState)
            {
                case FearState.Scared when stress >= FearAlertEnter:
                    return FearState.Scared;
                case FearState.Scared:
                case FearState.Alert:
                    return stress < FearAlertExit ? FearState.Calm : FearState.Alert;
                default:
                    return stress >= FearAlertEnter ? FearState.Alert : FearState.Calm;
            }
        }

        private void SetFearState(FearState next, bool panic, bool seen)
        {
            if (next == fearState) return;

            FearState was = fearState;
            fearState = next;
            if (YourBuddyPlugin.ConfigDebugLevel.Value < 1) return;

            string why = panic ? $"the Breathless is {monsterDist:0.0}m away (panic under {FearPanicDist:0.0}m)"
                : seen ? $"the Breathless in sight {monsterDist:0.0}m away"
                : "it is out of sight";
            YourBuddyPlugin.Log.LogInfo($"[fear] {was} -> {next}: stress {stress:0.00}, {why}");
        }

        /// <summary>
        /// Once a second while there is anything to report. docs/logging.md
        /// </summary>
        private void TraceFear(float rate)
        {
            if (YourBuddyPlugin.ConfigDebugLevel.Value < 2 || Time.time < fearTraceAt) return;

            if (stress <= 0f && !monsterInSight && !monsterBehind) return;

            fearTraceAt = Time.time + 1f;
            string sight = monsterInSight ? $"in sight {monsterDist:0.0}m"
                : monsterBehind ? $"clear line but behind it, {monsterDist:0.0}m"
                : monsterDist < float.MaxValue ? $"not in sight, {monsterDist:0.0}m" : "no monster";
            YourBuddyPlugin.Log.LogInfo(
                $"[fear] stress {stress:0.00} ({rate:+0.00;-0.00}/s) {fearState}: {sight}, mode {mode}" +
                (mode == BuddyMode.Flee ? $" ({fleePhase})" : ""));
        }

        /// <summary>
        /// From the buddy's eye to the point the monster casts its own sight from, then to
        /// its origin; either line clear is enough. docs/invariants.md#sight-stops-at-a-shut-door
        /// </summary>
        private bool MonsterInSight(Breathless breathless)
        {
            Vector3 eye = GroundPos(FearEyeHeight);
            Transform body = breathless.transform;
            Transform? point = GameInternals.BreathlessControllerAccess.GetRaycastPoint(breathless.Controller);
            Gate? shut = null;
            if (point != null && NavProbe.CanSee(eye, point.position, body, out shut)) return true;

            if (NavProbe.CanSee(eye, body.position, body, out Gate? shutToBody)) return true;

            LogBehindShutGate(shut != null ? shut : shutToBody);
            return false;
        }

        /// <summary>
        /// Calm, only what is in front of it is noticed; once aware it keeps track all round, and
        /// within FearPanicDist it is felt from any side. docs/fear.md §2
        /// </summary>
        private bool InViewCone(Vector3 monster)
        {
            if (fearState != FearState.Calm || monsterDist <= FearPanicDist) return true;

            Vector3 toMonster = monster - transform.position;
            toMonster.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (toMonster.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f) return true;

            return Vector3.Dot(facing.normalized, toMonster.normalized) >= FearViewCos;
        }

        /// <summary>
        /// The closed-door check of docs/fear.md §6: this line, and no stress, is the pass.
        /// </summary>
        private void LogBehindShutGate(Gate? gate)
        {
            if (gate == null || YourBuddyPlugin.ConfigDebugLevel.Value < 2 || Time.time < fearGateLogAt) return;

            fearGateLogAt = Time.time + 5f;
            YourBuddyPlugin.Log.LogInfo(
                $"[fear] The Breathless {monsterDist:0.0}m away is behind shut gate '{gate.gameObject.name}' - not in sight");
        }

        /// <summary>
        /// Fear switched off, or the monster's AI is: calm at once, and hand the buddy back.
        /// </summary>
        private void ResetFear()
        {
            stress = 0f;
            fearTickAt = 0f;
            ClearFleeStuck();
            fearStalemate = false;
            monsterInSight = false;
            monsterBehind = false;
            seenDistBefore = float.MaxValue;
            monsterDist = float.MaxValue;
            if (fearState != FearState.Calm)
            {
                YourBuddyPlugin.Log.LogInfo("[fear] " + fearState + " -> Calm: fear is switched off");
                fearState = FearState.Calm;
            }
            if (mode == BuddyMode.Flee) EndFlee();
        }

        /// <summary>
        /// For the HUD.
        /// </summary>
        internal string DescribeFear()
        {
            string text = fearState + ", stress " + stress.ToString("0.0") + "/" + FearStressCap.ToString("0");
            if (monsterInSight) text += ", in sight " + monsterDist.ToString("0.0") + "m";
            else if (monsterBehind) text += ", behind it " + monsterDist.ToString("0.0") + "m";

            if (mode == BuddyMode.Flee) text += " [" + fleePhase + "]";
            if (fearStalemate) text += ", stalemate";
            if (Hiding) text += ", " + DescribeHide();

            return text;
        }

        // ------------------------------------------------------------------
        // Alert: holding back
        // ------------------------------------------------------------------

        /// <summary>
        /// Alert or Scared: a step within 60 degrees of the monster's bearing, while near it,
        /// is not taken. A retreat is exempt - its plan was chosen to keep clear of it.
        /// </summary>
        private Vector3 HoldBackFromMonster(Vector3 desired, ref bool wantMove)
        {
            if (!wantMove || fearState == FearState.Calm) return desired;

            // A retreat's plan, and a hide's, were both chosen to keep clear of it.
            if (mode == BuddyMode.Flee && fleePhase is FleePhase.Retreat or FleePhase.Hide) return desired;

            Vector3 toMonster = lastMonsterPos - transform.position;
            float range = toMonster.magnitude;
            if (range > FearRestraintDist) return desired;

            toMonster.y = 0f;
            Vector3 heading = new(desired.x, 0f, desired.z);
            if (toMonster.sqrMagnitude < 0.0001f || heading.sqrMagnitude < 0.0001f) return desired;

            if (Vector3.Dot(heading.normalized, toMonster.normalized) < FearRestraintCos) return desired;

            wantMove = false;
            // docs/logging.md §4: a hold that can last must say so.
            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 1 && Time.time >= fearHoldLogAt)
            {
                fearHoldLogAt = Time.time + 5f;
                YourBuddyPlugin.Log.LogInfo(
                    $"[fear] {fearState}: not walking toward the Breathless {range:0.0}m away - holding ({mode})");
            }
            return Vector3.zero;
        }

        // ------------------------------------------------------------------
        // Scared: the Flee mode
        // ------------------------------------------------------------------

        /// <summary>
        /// Scared: remember what to go back to, then run. docs/fear.md
        /// </summary>
        private void StartFlee(bool panic)
        {
            modeBeforeFlee = mode;
            ClearFleeStuck();
            fearStalemate = false;
            SetMode(BuddyMode.Flee);
            // Already on the way into a closet, or in one: that is the flee. docs/fear.md §6
            if (Hiding)
            {
                hideFromFear = true;
                fleePhase = FleePhase.Hide;
                YourBuddyPlugin.Log.LogInfo($"[fear] Fleeing the Breathless into {hideName}, then back to {modeBeforeFlee}");
                return;
            }
            // A step-off already under way may lead toward the monster, and a Follow wait
            // would keep the run to the player standing still.
            followStepOffUntil = 0f;
            followWaitUntil = 0f;
            fleeRetreatFailed = false;
            fleePhase = FleePhase.Retreat;
            YourBuddyPlugin.Log.LogInfo(
                $"[fear] Fleeing the Breathless {Vector3.Distance(lastMonsterPos, transform.position):0.0}m away " +
                $"({(panic ? "too close" : "watched it too long")}), then back to {modeBeforeFlee}");
            TryStartRetreat();
        }

        /// <summary>
        /// Below Alert again: resume what the flee interrupted, or what was ordered since.
        /// A goto is planned afresh from here. docs/invariants.md#fear-owns-the-buddy
        /// </summary>
        private void EndFlee()
        {
            BuddyMode resume = modeBeforeFlee;
            ClearFleeStuck();
            fearStalemate = false;
            followStepOffUntil = 0f;
            decideAt = 0f;
            if (resume == BuddyMode.Route)
            {
                BuddyNodeGraph.NavPath? plan = BuddyNodeGraph.FindPath(FloorUnderBuddy(), routeGoal);
                if (plan is { Count: > 0 })
                {
                    StartRoute(plan.Value, routeGoal);
                    YourBuddyPlugin.Log.LogInfo($"[fear] Calmer ({fearState}) - walking on to {routeGoal:0.0}");
                    return;
                }
                YourBuddyPlugin.Log.LogInfo($"[fear] Calmer ({fearState}), but there is no way on to {routeGoal:0.0} - following");
                if (orderedMode == BuddyMode.Route) orderedMode = null;

                resume = BuddyMode.Follow;
            }
            if (resume is BuddyMode.Flee or BuddyMode.Dead) resume = BuddyMode.Follow;

            SetMode(resume);
            YourBuddyPlugin.Log.LogInfo($"[fear] Calmer ({fearState}) - back to {resume}");
        }

        /// <summary>
        /// Walks the retreat plan, then runs to the player, or holds facing the monster when
        /// neither is possible. Never ends itself: UpdateFear does.
        /// </summary>
        private Vector3 UpdateFlee(Player? player, out bool wantMove)
        {
            wantMove = false;

            // The back-away, and a doorway step-out. docs/invariants.md#step-off-applies-in-every-mode
            if (TryStepOff(out Vector3 stepOff))
            {
                wantMove = true;
                return stepOff * FleeSpeedFactor;
            }

            if (fleePhase == FleePhase.ToPlayer)
            {
                // Running to a player standing beside the monster is running at the monster.
                if (player != null && player.Controller != null &&
                    (player.Controller.CachedTransform.position - lastMonsterPos).sqrMagnitude <
                    (FleeClearance + FearPanicDist) * (FleeClearance + FearPanicDist))
                {
                    // Standing here is not a plan either: it counts toward the stalemate.
                    hasMoveTarget = false;
                    MarkFleeStuck();
                    return Vector3.zero;
                }
                Vector3 toPlayer = UpdateFollow(player, out wantMove) * FleeSpeedFactor;
                // Standing beside the player with the monster still in view is a stalemate too:
                // the run is over and there is nothing further the flee can do.
                if (wantMove) ClearFleeStuck();
                else MarkFleeStuck();

                return toPlayer;
            }

            if (fleePhase == FleePhase.Hold || navPlan == null || navPathIndex >= navPlan.Value.Count)
            {
                hasMoveTarget = false;
                if (Time.time >= fleeRetryAt) TryStartRetreat();

                return Vector3.zero;
            }

            float budget = 6f + Vector3.Distance(transform.position, fleeTarget) * 1.2f;
            if (Time.time - fleeRetreatSince > budget)
            {
                AbandonRetreat("it is taking too long");
                return Vector3.zero;
            }

            AdvancePastReachedWaypoints();
            if (navPathIndex >= navPlan.Value.Count)
            {
                DropPlan();
                hasMoveTarget = false;
                fleePhase = FleePhase.ToPlayer;
                YourBuddyPlugin.Log.LogInfo($"[fear] Got away to {fleeTarget:0.0} - making for the player");
                return Vector3.zero;
            }

            // docs/invariants.md#off-the-flight-is-off-the-plan
            if (HasLeftStairLeg(out _, out _))
            {
                AbandonRetreat("it left the stair leg");
                return Vector3.zero;
            }

            // The monster moves; a plan clear of where it was may not be clear of where it is.
            if (Time.time >= fleeClearanceCheckAt)
            {
                fleeClearanceCheckAt = Time.time + 0.5f;
                if (!PlanKeepsClear(navPlan.Value, navPathIndex))
                {
                    AbandonRetreat("the Breathless is in the way now");
                    return Vector3.zero;
                }
            }

            wantMove = true;
            currentMoveTarget = navPlan.Value[navPathIndex];
            hasMoveTarget = true;
            return HeadingAlongPlan() * (moveSpeed * FleeSpeedFactor);
        }

        /// <summary>
        /// A retreat plan, else a step backwards, else holding and watching. Whatever plan
        /// was there - the run to the player's is Follow's - is not a retreat.
        /// </summary>
        private void TryStartRetreat()
        {
            DropPlan();
            fleeRetryAt = Time.time + FleeRetryDelay;
            // Hide or run, weighed rather than always the same. docs/fear.md §5
            bool hideFirst = !Hiding && PreferHide();
            if (hideFirst && TryStartHide(true, out _))
            {
                ClearFleeStuck();
                return;
            }

            if (TryPlanRetreat())
            {
                fleeRetreatFailed = false;
                fleePhase = FleePhase.Retreat;
                ClearFleeStuck();
                return;
            }
            fleeRetreatFailed = true;
            // Running turned out to be impossible after all: a closet beats standing here.
            if (!hideFirst && !Hiding && TryStartHide(true, out _))
            {
                ClearFleeStuck();
                return;
            }

            if (TryBackAway())
            {
                fleePhase = FleePhase.Retreat;
                ClearFleeStuck();
                return;
            }

            fleePhase = FleePhase.Hold;
            hasMoveTarget = false;
            MarkFleeStuck();
            fleeRetryAt = Time.time + FleeHoldRetryDelay;
            if (Time.time < fleeHoldLogAt) return;

            fleeHoldLogAt = Time.time + 5f;
            YourBuddyPlugin.Log.LogInfo(
                $"[fear] Nowhere to run from the Breathless {Vector3.Distance(lastMonsterPos, transform.position):0.0}m away - " +
                "holding and watching it");
        }

        private void AbandonRetreat(string why)
        {
            if (navPlan != null)
            {
                YourBuddyPlugin.Log.LogInfo($"[fear] Dropping the retreat to {fleeTarget:0.0}: {why}");
                fleeFailedTarget = fleeTarget;
                fleeFailedTargetUntil = Time.time + 8f;
            }
            DropPlan();
            hasMoveTarget = false;
            fleePhase = FleePhase.Retreat;
            fleeRetryAt = Time.time + FleeRetryDelay;
        }

        /// <summary>
        /// Whether to try a hiding spot before running, this time. A coin weighted by FleeHideBias:
        /// lower while the monster is watching or too near to shut the doors in time, higher when the
        /// last retreat found nowhere to go. docs/fear.md §5
        /// </summary>
        private bool PreferHide()
        {
            if (!YourBuddyPlugin.ConfigHideInClosets.Value) return false;

            float bias = Mathf.Clamp01(YourBuddyPlugin.ConfigFleeHideBias.Value);
            string why = "";
            if (monsterInSight)
            {
                bias *= HideSeenFactor;
                why = ", it is watching me";
            }
            if (monsterDist <= HideMonsterClose)
            {
                bias *= 0.5f;
                why += $", it is only {monsterDist:0.0}m away";
            }
            if (fleeRetreatFailed)
            {
                bias += HideNoRetreatBias;
                why += ", there was nowhere to run last time";
            }
            bias = Mathf.Clamp01(bias);
            bool hide = Random.value < bias;
            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2)
            {
                YourBuddyPlugin.Log.LogInfo($"[fear] Choosing to {(hide ? "hide" : "run")} (hide chance {bias:0.00}{why})");
            }
            return hide;
        }

        /// <summary>
        /// Scores every active node - far enough from the monster, near the player, a short
        /// trip - then plans to the best few. docs/fear.md
        /// </summary>
        private bool TryPlanRetreat()
        {
            BuddyNodeGraph.CollectActiveNodes(fleeCandidates);
            if (fleeCandidates.Count == 0) return false;

            Vector3 here = transform.position;
            float fromMonster = Vector3.Distance(here, lastMonsterPos);
            Player? player = GameManager.Instance != null && GameManager.Instance.PlayerShip != null
                ? GameManager.Instance.PlayerShip.Pilot
                : null;
            bool toPlayer = player != null && player.Controller != null && !IsPlayerInSpace(player);
            // toPlayer implies player and its Controller are non-null.
            Vector3 playerPos = toPlayer ? player!.Controller!.CachedTransform.position : Vector3.zero;

            fleeScored.Clear();
            foreach (Vector3 node in fleeCandidates)
            {
                float away = Vector3.Distance(node, lastMonsterPos);
                if (away < fromMonster + FleeMinGain) continue;

                if (Time.time < fleeFailedTargetUntil && (node - fleeFailedTarget).sqrMagnitude < 0.25f) continue;

                float score = Mathf.Min(away, FleeSafeDist) - FleeTripWeight * Vector3.Distance(here, node) -
                              (toPlayer ? FleePlayerWeight * Vector3.Distance(node, playerPos) : 0f);
                fleeScored.Add(new KeyValuePair<float, Vector3>(score, node));
            }
            fleeScored.Sort((a, b) => b.Key.CompareTo(a.Key));

            Vector3 start = FloorUnderBuddy();
            int tried = 0;
            int unroutable = 0;
            foreach (KeyValuePair<float, Vector3> candidate in fleeScored)
            {
                if (tried++ >= FleePickAttempts) break;

                BuddyNodeGraph.NavPath? plan = BuddyNodeGraph.FindPath(start, candidate.Value);
                if (plan is not { Count: > 0 })
                {
                    unroutable++;
                    continue;
                }
                if (!PlanKeepsClear(plan.Value, 0)) continue;

                navPlan = plan;
                navPathIndex = 0;
                fleeTarget = candidate.Value;
                fleeRetreatSince = Time.time;
                fleeClearanceCheckAt = Time.time + 0.5f;
                YourBuddyPlugin.Log.LogInfo(
                    $"[fear] Retreating to {fleeTarget:0.0}: {Vector3.Distance(fleeTarget, lastMonsterPos):0.0}m from the Breathless " +
                    $"(now {fromMonster:0.0}m)" +
                    (toPlayer ? $", {Vector3.Distance(fleeTarget, playerPos):0.0}m from the player" : "") +
                    $", {plan.Value.Count} waypoints");
                return true;
            }

            if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2)
            {
                YourBuddyPlugin.Log.LogInfo(
                    $"[fear] No retreat plan: {fleeScored.Count} of {fleeCandidates.Count} nodes are {FleeMinGain:0}m+ further " +
                    $"from the Breathless, {Mathf.Min(tried, fleeScored.Count)} tried, {unroutable} unroutable, the rest pass it");
            }
            return false;
        }

        /// <summary>
        /// From where the buddy stands, no leg may take it nearer the monster than the leg
        /// starts, nor within FleeClearance of it once it is outside that.
        /// </summary>
        private bool PlanKeepsClear(BuddyNodeGraph.NavPath plan, int fromIndex)
        {
            Vector3 a = transform.position;
            for (int i = fromIndex; i < plan.Count; i++)
            {
                Vector3 b = plan[i];
                float allowed = Mathf.Min(FleeClearance, Vector3.Distance(a, lastMonsterPos) - 0.3f);
                if (NavProbe.DistanceToSegment(lastMonsterPos, a, b) < allowed) return false;

                a = b;
            }
            return true;
        }

        /// <summary>
        /// No plan: step away from the monster along the first clear bearing, through the
        /// shared step-off. docs/invariants.md#step-off-applies-in-every-mode
        /// </summary>
        private bool TryBackAway()
        {
            Vector3 away = transform.position - lastMonsterPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;

            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = Vector3.back;

            away.Normalize();

            for (int i = -1; i < DetourAngles.Length; i++)
            {
                Vector3 dir = i < 0 ? away : Quaternion.Euler(0f, DetourAngles[i], 0f) * away;
                // A back-away is a detour too. docs/invariants.md#a-detour-may-not-step-off-a-ledge
                if (BodyBlocked(dir, 1f) || StepsOffALedge(dir, 1f)) continue;

                followStepOffTarget = transform.position + dir * FleeBackAwayDist;
                followStepOffUntil = Time.time + FleeBackAwayTime;
                if (YourBuddyPlugin.ConfigDebugLevel.Value >= 2)
                {
                    YourBuddyPlugin.Log.LogInfo($"[fear] No retreat plan - backing away along {dir:0.0}");
                }
                return true;
            }
            return false;
        }
    }
}
