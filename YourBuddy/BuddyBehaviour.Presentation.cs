using System;
using System.Collections.Generic;
using FMODUnity;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Audiovisual output: footstep sounds copied from the player, animation parameters and pooled debug visuals.
    /// </summary>
    public sealed partial class BuddyBehaviour
    {
        // Parallel to anims. Animator.parameters allocates on every read, so which driven
        // parameters each controller declares is read once, not every frame.
        private AnimatorParams[]? animParams;
        private readonly struct AnimatorParams(RuntimeAnimatorController controller, bool resolved,
            bool hasSpeed, bool hasVelocity, bool hasIsWalking, bool hasIsGrounded)
        {
            public readonly RuntimeAnimatorController Controller = controller;
            /// <summary>
            /// False until the animator reported any parameter; an uninitialised one is asked again.
            /// </summary>
            public readonly bool Resolved = resolved;
            public readonly bool HasSpeed = hasSpeed;
            public readonly bool HasVelocity = hasVelocity;
            public readonly bool HasIsWalking = hasIsWalking;
            public readonly bool HasIsGrounded = hasIsGrounded;
        }
        private float currentAnimSpeed = 0f;

        private EventReference[]? playerFootstepEvents;
        private float moveDelta = 0f;
        private bool stepPlayed = false;

        // Debug visualization
        private Transform? debugRoot;
        // Created together with debugRoot, and only read once it exists.
        private LineRenderer pathLine = null!;
        private LineRenderer probeLine = null!;
        private LineRenderer markerLine = null!;
        private LineRenderer jumpLine = null!;

        /// <summary>
        /// Copies footstep event references from the player's CameraAnimator using reflection.
        /// </summary>
        private void CopyFootstepEventsFromPlayer()
        {
            try
            {
                Player player = GameManager.Instance.PlayerShip.Pilot;
                if (player == null) return;

                CameraAnimator playerCameraAnimator = player.Controller.CameraAnimator;
                if (playerCameraAnimator == null) return;

                playerFootstepEvents = GameInternals.CameraAnimatorAccess.GetFootstepEvents(playerCameraAnimator);
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[ai] Failed to copy footstep events: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the footstep sound system using the "muffled" footstep event from the player.
        /// </summary>
        private void InitializeFootsteps()
        {
            try
            {
                if (playerFootstepEvents is { Length: > 0 })
                {
                    int soundIndex = (playerFootstepEvents.Length > 1) ? 1 : 0;
                    footstepInstance = RuntimeManager.CreateInstance(playerFootstepEvents[soundIndex]);
                    footstepInstance.setVolume(0.5f);
                }
            }
            catch (Exception ex)
            {
                YourBuddyPlugin.Log.LogWarning($"[ai] Could not initialize footstep sounds: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays a footstep sound at the peak of the walk cycle (matches CameraAnimator logic).
        /// </summary>
        private void PlayFootstep()
        {
            if (!cc.isGrounded || currentAnimSpeed < 0.1f)
                return;

            float walkShakeSpeed = MoveSpeed * 5f;
            moveDelta += Time.deltaTime * walkShakeSpeed;

            if (moveDelta > 6.2831855f)
            {
                moveDelta = 0f;
                stepPlayed = false;
            }

            float walkOffsetX = Mathf.Sin(moveDelta);
            if (Mathf.Abs(walkOffsetX) >= 0.95f && !stepPlayed)
            {
                if (footstepInstance.isValid())
                {
                    try
                    {
                        footstepInstance.start();
                        stepPlayed = true;
                    }
                    catch (Exception ex)
                    {
                        YourBuddyPlugin.Log.LogWarning($"[ai] Error playing footstep: {ex.Message}");
                    }
                }
            }
        }


        private static readonly int IsWalking = Animator.StringToHash("isWalking");
        private static readonly int IsGrounded = Animator.StringToHash("isGrounded");
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Velocity = Animator.StringToHash("Velocity");

        /// <summary>
        /// Which of the driven parameters an animator declares, with their expected types.
        /// </summary>
        private static AnimatorParams ReadAnimatorParams(Animator a)
        {
            bool hasSpeed = false, hasVelocity = false, hasIsWalking = false, hasIsGrounded = false;
            AnimatorControllerParameter[] declared = a.parameters;
            foreach (AnimatorControllerParameter p in declared)
            {
                switch (p.name)
                {
                    case "Speed": hasSpeed = p.type == AnimatorControllerParameterType.Float; break;
                    case "Velocity": hasVelocity = p.type == AnimatorControllerParameterType.Float; break;
                    case "isWalking": hasIsWalking = p.type == AnimatorControllerParameterType.Bool; break;
                    case "isGrounded": hasIsGrounded = p.type == AnimatorControllerParameterType.Bool; break;
                }
            }
            return new AnimatorParams(a.runtimeAnimatorController, declared.Length > 0,
                hasSpeed, hasVelocity, hasIsWalking, hasIsGrounded);
        }


        /// <summary>
        /// Turns to face the player. Cosmetic only - movement never reads rotation.
        /// </summary>
        private void FacePlayer(Player player)
        {
            if (player == null || player.Controller == null) return;

            FacePoint(player.Controller.CachedTransform.position);
        }

        private void FacePoint(Vector3 point)
        {
            Vector3 toPoint = point - transform.position;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude <= 0.01f) return;

            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(toPoint.normalized), Time.deltaTime * 5f);
        }

        private void UpdateAnimation(Vector3 desired, bool wantMove, Player player)
        {
            bool isMoving = wantMove && desired.sqrMagnitude > 0.001f;

            if (isMoving)
            {
                Vector3 lookDir = new(desired.x, 0f, desired.z);
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(lookDir.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
                }
            }
            else if (fearState != FearState.Calm && !InDialog)
            {
                // Standing still with the Breathless about: watch it - unless being talked
                // to, where Update already faces the player. docs/fear.md
                FacePoint(lastMonsterPos);
            }
            else if (mode == BuddyMode.Follow)
            {
                FacePlayer(player);
            }

            float targetAnimSpeed = isMoving ? 1f : 0f;
            currentAnimSpeed = Mathf.Lerp(currentAnimSpeed, targetAnimSpeed, Time.deltaTime * 5f);

            if (anims is { Length: > 0 })
            {
                if (animParams == null || animParams.Length != anims.Length)
                {
                    animParams = new AnimatorParams[anims.Length];
                }

                for (int i = 0; i < anims.Length; i++)
                {
                    Animator a = anims[i];
                    if (!a.gameObject.activeInHierarchy) continue;

                    AnimatorParams p = animParams[i];
                    if (!p.Resolved || p.Controller != a.runtimeAnimatorController)
                    {
                        p = ReadAnimatorParams(a);
                        animParams[i] = p;
                    }

                    if (p.HasSpeed) a.SetFloat(Speed, currentAnimSpeed);

                    if (p.HasVelocity) a.SetFloat(Velocity, currentAnimSpeed);

                    if (p.HasIsWalking) a.SetBool(IsWalking, currentAnimSpeed > 0.1f);

                    if (p.HasIsGrounded) a.SetBool(IsGrounded, cc.isGrounded);
                }
            }

            if (isMoving && cc.isGrounded) PlayFootstep();

            wasMoving = isMoving;
        }


        // ------------------------------------------------------------------
        // Debug visualization
        // ------------------------------------------------------------------

        public void EnsureDebugVisuals(bool enableDebugVisuals)
        {
            if (enableDebugVisuals)
            {
                if (debugRoot == null)
                {
                    debugRoot = new GameObject("BuddyDebug").transform;
                    debugRoot.SetParent(transform, false);
                    pathLine = CreateDebugLine("Path", new Color(1f, 0.85f, 0.2f));
                    probeLine = CreateDebugLine("Probe", new Color(0.3f, 1f, 0.4f));
                    markerLine = CreateDebugLine("Marker", new Color(0.3f, 0.8f, 1f));
                    jumpLine = CreateDebugLine("JumpProbe", new Color(1f, 0.5f, 0f));
                }
                debugRoot.gameObject.SetActive(true);
            }
            else
            {
                HideDebugVisuals();
            }
        }

        private LineRenderer CreateDebugLine(string lineName, Color color)
        {
            GameObject lineGo = new(lineName);
            lineGo.transform.SetParent(debugRoot, false);
            LineRenderer line = lineGo.AddComponent<LineRenderer>();
            Shader? shader = Shader.Find("Sprites/Default");
            if (shader != null) line.material = new Material(shader);
            line.startWidth = 0.03f;
            line.endWidth = 0.03f;
            line.startColor = color;
            line.endColor = color;
            line.positionCount = 0;
            line.useWorldSpace = true;
            return line;
        }

        private void HideDebugVisuals()
        {
            if (debugRoot != null) debugRoot.gameObject.SetActive(false);
        }

        private void UpdateDebugVisuals()
        {
            if (debugRoot == null || !debugRoot.gameObject.activeInHierarchy) return;

            // Path: buddy -> current target -> upcoming NodeGraph path corners.
            List<Vector3> pathPositions = [GroundPos(0.15f)];
            if (hasMoveTarget) pathPositions.Add(currentMoveTarget + Vector3.up * 0.15f);

            if (navPlan != null) for (int i = navPathIndex; i < navPlan.Value.Count && i < navPathIndex + 10; i++) pathPositions.Add(navPlan.Value[i] + Vector3.up * 0.15f);
            SetLinePositions(pathLine, pathPositions);

            // Obstacle probe.
            Vector3 probeOrigin = GroundPos(0.5f);
            Vector3 probeDir = new(lastAvoidDirection.x, 0f, lastAvoidDirection.z);
            if (probeDir.sqrMagnitude < 0.001f) probeDir = transform.forward;

            bool blocked = BodyBlocked(probeDir.normalized, 0.9f);
            SetLinePositions(probeLine,
            [
                probeOrigin,
                probeOrigin + probeDir.normalized * 0.9f
            ]);
            Color probeColor = blocked ? new Color(1f, 0.25f, 0.25f) : new Color(0.3f, 1f, 0.4f);
            probeLine.startColor = probeColor;
            probeLine.endColor = probeColor;

            // Target marker (diamond around the move target).
            if (hasMoveTarget)
            {
                Vector3 t = currentMoveTarget + Vector3.up * 0.1f;
                SetLinePositions(markerLine,
                [
                    t + Vector3.right * 0.2f,
                    t + Vector3.forward * 0.2f,
                    t + Vector3.left * 0.2f,
                    t + Vector3.back * 0.2f,
                    t + Vector3.right * 0.2f
                ]);
            }
            else
            {
                SetLinePositions(markerLine, []);
            }


            // Shin-level jump probe.
            if (hasMoveTarget && cc != null && cc.isGrounded)
            {
                Vector3 jumpDir = currentMoveTarget - transform.position;
                jumpDir.y = 0f;
                if (jumpDir.sqrMagnitude > 0.001f)
                {
                    jumpDir.Normalize();
                    Vector3 shinOrigin = GroundPos(0.25f);
                    bool jumpBlocked = Physics.Raycast(shinOrigin, jumpDir, 0.7f, ProbeLayers, QueryTriggerInteraction.Ignore);
                    SetLinePositions(jumpLine,
                    [
                        shinOrigin,
                        shinOrigin + jumpDir * 0.7f
                    ]);
                    Color jumpColor = jumpBlocked ? new Color(1f, 0.45f, 0f) : new Color(1f, 0.85f, 0.6f);
                    jumpLine.startColor = jumpColor;
                    jumpLine.endColor = jumpColor;
                }
                else
                {
                    SetLinePositions(jumpLine, []);
                }
            }
            else
            {
                SetLinePositions(jumpLine, []);
            }
        }

        private static void SetLinePositions(LineRenderer line, List<Vector3> positions)
        {
            if (line == null) return;

            line.positionCount = positions.Count;
            for (int i = 0; i < positions.Count; i++)
            {
                line.SetPosition(i, positions[i]);
            }
        }


    }
}
