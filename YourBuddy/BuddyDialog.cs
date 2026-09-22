using System.Collections.Generic;
using Space;
using UnityEngine;

namespace YourBuddy
{
    /// <summary>
    /// Look at the buddy, press Interact, give it an order. The game's own DialogMenu
    /// is hard-typed to AssistanceBot and dereferences its animator, so this is our own
    /// panel drawn to match it. See docs/dialog.md.
    /// </summary>
    [RequireComponent(typeof(BuddyBehaviour))]
    public sealed class BuddyDialog : MonoBehaviour
    {
        /// <summary>
        /// Measured to the nearest point of the buddy's own capsule, never to its chest:
        /// at arm's length the angle to a chest point blows up, which opened the window
        /// from across the room but not from right in front of it.
        /// </summary>
        private const float LookAngle = 30f;
        /// <summary>
        /// Shorter than the player's own reach, so standing at a keypad or a terminal
        /// does not mean talking to the buddy instead of using it.
        /// </summary>
        private const float TalkRange = 2.4f;

        private BuddyBehaviour buddy;
        private Collider[] hitboxes;
        private InputHandler? subscribedTo;
        private bool open;
        private bool showCommands;
        private string input = "";
        private Vector2 scroll;
        private readonly List<string> lines = [];
        private static readonly RaycastHit[] SightHits = new RaycastHit[16];
        /// <summary>
        /// Reused to measure log lines; OnGUI runs several times a frame.
        /// </summary>
        private static readonly GUIContent LineContent = new();

        private void Awake()
        {
            buddy = GetComponent<BuddyBehaviour>();
            hitboxes = GetComponents<Collider>();
        }

        private void Update()
        {
            // InputHandler drops all listeners on teardown and a scene load brings a new
            // one, so the subscription is re-checked rather than made once.
            InputHandler? handler = GameManager.Instance != null ? GameManager.Instance.InputHandler : null;
            if (handler != subscribedTo)
            {
                if (subscribedTo != null) subscribedTo.OnInteract.RemoveListener(OnInteractPressed);

                subscribedTo = handler;
                if (subscribedTo != null) subscribedTo.OnInteract.AddListener(OnInteractPressed);
            }

            if (open && (buddy == null || buddy.IsDead)) Close();
        }

        private void OnDestroy()
        {
            if (subscribedTo != null) subscribedTo.OnInteract.RemoveListener(OnInteractPressed);

            if (open) Close();
        }

        // ------------------------------------------------------------------
        // Opening
        // ------------------------------------------------------------------

        private void OnInteractPressed()
        {
            if (open || buddy == null || buddy.IsDead || buddy.Asleep || buddy.Hiding) return;

            if (!YourBuddyPlugin.ConfigDialog.Value) return;

            if (PlayerIsFacingMe()) OpenWindow();
        }

        private bool PlayerIsFacingMe()
        {
            Player? player = GameManager.Instance != null && GameManager.Instance.PlayerShip != null ? GameManager.Instance.PlayerShip.Pilot : null;
            if (player == null || player.Controller == null) return false;

            Transform cam = player.Controller.CameraAnimator != null
                ? player.Controller.CameraAnimator.CachedTransform
                : player.Controller.CachedTransform;
            if (cam == null) return false;

            Vector3 aim = NearestPointTo(cam.position) - cam.position;
            float reach = aim.magnitude;
            if (reach > TalkRange) return false;

            if (reach > 0.01f && Vector3.Angle(cam.forward, aim) > LookAngle) return false;

            if (PlayerIsBusy(player.Controller, cam)) return false;

            Vector3 toBuddy = transform.position + Vector3.up * 0.6f - cam.position;
            return !SightBlocked(cam.position, toBuddy, toBuddy.magnitude);
        }

        /// <summary>
        /// The closest point on the buddy's own capsule to `from`, falling back to its
        /// chest if it has no usable collider.
        /// </summary>
        private Vector3 NearestPointTo(Vector3 from)
        {
            Vector3 best = transform.position + Vector3.up * 0.6f;
            float bestDist = float.MaxValue;
            foreach (Collider box in hitboxes)
            {
                if (box == null || !box.enabled) continue;

                Vector3 near = box.ClosestPoint(from);
                float d = (near - from).sqrMagnitude;
                if (d >= bestDist) continue;

                bestDist = d;
                best = near;
            }
            return best;
        }

        /// <summary>
        /// The player is aiming at something interactive, or already carrying it. The
        /// game's own interaction wins - the buddy must not steal the keypress.
        /// </summary>
        private bool PlayerIsBusy(PlayerController controller, Transform cam)
        {
            // A game update that renames the focus field must not hand every terminal's
            // keypress to the dialog: judge the aim ourselves instead.
            if (!GameInternals.PlayerControllerAccess.CanReadFocus) return AimingAtInteractable(controller, cam);

            Interactable? focused = GameInternals.PlayerControllerAccess.GetFocusedInteractable(controller);
            if (focused == null) return false;

            try
            {
                if (focused.Grabbable != null && focused.Grabbable.IsGrabbed) return true;

                return focused.Highlighted;
            }
            catch (System.Exception)
            {
                // Highlighted dereferences a serialized field the mod does not own.
                return false;
            }
        }

        /// <summary>
        /// The fallback for PlayerIsBusy: the nearest solid thing along the view within
        /// the player's reach, and whether it is something the game would interact with.
        /// Coarser than the game's own focus (no held-item test), but it fails toward the
        /// game's interaction, never toward the dialog.
        /// </summary>
        private bool AimingAtInteractable(PlayerController controller, Transform cam)
        {
            float reach = SceneLoader.Instance != null ? SceneLoader.Instance.GlobalData.Settings.reachRange : TalkRange;
            int count = Physics.RaycastNonAlloc(cam.position, cam.forward, SightHits, reach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            Collider? nearest = null;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hit = SightHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(controller.CachedTransform)) continue;

                if (SightHits[i].distance >= nearestDist) continue;

                nearestDist = SightHits[i].distance;
                nearest = hit;
            }

            return nearest != null && !nearest.transform.IsChildOf(transform) &&
                   nearest.GetComponent<Interactable>() != null;
        }

        /// <summary>
        /// True when solid geometry stands between the player and the buddy. Without it
        /// the cone test alone lets the window be opened through walls and shut doors.
        /// </summary>
        private bool SightBlocked(Vector3 from, Vector3 toBuddy, float distance)
        {
            int count = Physics.RaycastNonAlloc(from, toBuddy / distance, SightHits, distance - 0.25f,
                NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = SightHits[i].collider;
                if (hit == null) continue;

                if (hit.transform.IsChildOf(transform))
                {
                    continue;                  // the buddy
                }

                if (buddy != null && buddy.IsPlayerBody(hit.transform)) continue;

                return true;
            }
            return false;
        }

        private void OpenWindow()
        {
            open = true;
            showCommands = false;
            scroll = Vector2.zero;
            if (lines.Count == 0) Say("Standing by.");

            if (buddy != null) buddy.InDialog = true;
            SetPlayerInUi(true);
        }

        private void Close()
        {
            open = false;
            if (buddy != null) buddy.InDialog = false;
            SetPlayerInUi(false);
        }

        /// <summary>
        /// Same handover AssistanceBot.Talk performs: free the cursor and move the
        /// player onto the UI action map, then put both back.
        /// </summary>
        private static void SetPlayerInUi(bool inUi)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            Player? player = gm.PlayerShip != null ? gm.PlayerShip.Pilot : null;
            if (player != null && player.Controller != null) player.Controller.LockedCursor = !inUi;

            if (gm.InputHandler == null) return;

            if (inUi) gm.InputHandler.SwitchToUIInput();
            else gm.InputHandler.SwitchToGameInput();
        }

        // ------------------------------------------------------------------
        // Conversation
        // ------------------------------------------------------------------

        private void Say(string text) => Append("> " + text);
        private void Echo(string text) => Append("$ " + text);

        private void Append(string text)
        {
            lines.Add(text);
            if (lines.Count > 80) lines.RemoveAt(0);

            scroll.y = float.MaxValue;
        }

        private void Submit()
        {
            string text = input.Trim();
            input = "";
            if (text.Length == 0) return;

            Echo(text);
            Say(BuddyDialogCommands.Run(text));
        }

        // ------------------------------------------------------------------
        // Panel
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!open) return;

            DialogSkin.Ensure();

            // Space.Event also exists; this is the IMGUI one.
            UnityEngine.Event e = UnityEngine.Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape) { Close(); return; }
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    Submit();
                    e.Use();
                }
            }

            // Whole pixels throughout: the pixel font drawn at a fractional origin lands
            // between texels and blurs.
            float w = Mathf.Round(Mathf.Clamp(Screen.width * 0.26f, 360f, 540f));
            float h = Mathf.Round(Mathf.Clamp(Screen.height * 0.48f, 280f, 560f));
            // Sits in the right-hand third rather than hard against the edge: the small
            // panel looked thrown into the corner at a 28px margin.
            float x = Mathf.Round(Screen.width * 0.72f - w * 0.5f);
            float y = Mathf.Round((Screen.height - h) * 0.5f);

            DialogSkin.Panel(new Rect(x, y, w, h));

            const float pad = 10f;
            // The game's buttons are sized in UI pixels, so the rows that hold them are too.
            float titleH = 26f * DialogSkin.UiScale;
            float rowH = 24f * DialogSkin.UiScale;

            Rect title = new(x + pad, y + pad, w - pad * 2f, titleH);
            DialogSkin.Panel(title);
            GUI.Label(title, showCommands ? "COMMANDS" : "BUDDY", DialogSkin.Title);

            float closeSide = titleH - 8f;
            Rect close = new(title.xMax - closeSide - 4f, title.y + 4f, closeSide, closeSide);
            if (DialogSkin.CloseButton(close)) { Close(); return; }

            float bodyTop = title.yMax + pad;
            float bodyBottom = y + h - pad - (showCommands ? 0f : rowH + pad);
            Rect body = new(x + pad, bodyTop, w - pad * 2f, bodyBottom - bodyTop);
            DialogSkin.Panel(body);

            if (showCommands) DrawCommands(body);
            else DrawLog(body);

            if (showCommands) return;

            float rowY = body.yMax + pad;
            Rect toggle = new(x + pad, rowY, rowH, rowH);
            if (DialogSkin.CommandsButton(toggle)) showCommands = true;

            Rect send = new(x + w - pad - rowH, rowY, rowH, rowH);
            if (DialogSkin.SendButton(send)) Submit();

            Rect field = new(toggle.xMax + 8f, rowY, send.x - toggle.xMax - 16f, rowH);
            DialogSkin.FieldFrame(field);
            // The text sits on the frame's face, above its lip.
            field = new Rect(field.x + DialogSkin.UiScale * 2f, field.y + DialogSkin.UiScale * 2f,
                field.width - DialogSkin.UiScale * 4f, field.height - DialogSkin.UiScale * 6f);
            GUI.SetNextControlName("buddyInput");
            input = GUI.TextField(field, input, 64, DialogSkin.Field);
            if (input.Length == 0) GUI.Label(field, "Enter message...", DialogSkin.Placeholder);
            // Keep the caret in the field: the panel exists to be typed into.
            if (GUI.GetNameOfFocusedControl() != "buddyInput") GUI.FocusControl("buddyInput");
        }

        private void DrawLog(Rect body)
        {
            Rect inner = new(body.x + 10f, body.y + 8f, body.width - 20f, body.height - 16f);
            float width = inner.width - 18f;
            float total = 4f;
            foreach (string line in lines)
            {
                LineContent.text = line;
                total += DialogSkin.Body.CalcHeight(LineContent, width) + 4f;
            }

            scroll = GUI.BeginScrollView(inner, scroll, new Rect(0f, 0f, width, total));
            float cursor = 0f;
            foreach (string line in lines)
            {
                LineContent.text = line;
                float lh = DialogSkin.Body.CalcHeight(LineContent, width);
                GUI.Label(new Rect(0f, cursor, width, lh), line,
                    line.StartsWith('$') ? DialogSkin.Echo : DialogSkin.Body);
                cursor += lh + 4f;
            }
            GUI.EndScrollView();
        }

        /// <summary>
        /// Two columns, and the row height fitted to the body: the word list outgrew one column,
        /// and the panel is only 240-400px tall. docs/dialog.md §3
        /// </summary>
        private void DrawCommands(Rect body)
        {
            string[] names = BuddyDialogCommands.Names;
            const float gap = 8f;
            int rows = (names.Length + 1) / 2;
            float rowH = Mathf.Floor(Mathf.Clamp((body.height - 14f - 52f) / rows, 24f, 34f));
            float colW = Mathf.Floor((body.width - 36f - gap) * 0.5f);
            for (int i = 0; i < names.Length; i++)
            {
                int col = i / rows;
                int rowIdx = i % rows;
                Rect row = new(body.x + 18f + col * (colW + gap), body.y + 14f + rowIdx * rowH, colW, rowH);
                if (!GUI.Button(row, names[i], DialogSkin.Command)) continue;

                showCommands = false;
                Echo(names[i]);
                Say(BuddyDialogCommands.Run(names[i]));
            }

            Rect back = new(body.x + 18f, body.yMax - 52f, 130f, 40f);
            if (DialogSkin.Button(back, "Back", false)) showCommands = false;
        }
    }
}
