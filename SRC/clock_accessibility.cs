using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the clock puzzle mini-game.
    ///
    /// APPROACH: Simulate controller input via OnLStickMove/OnRStickMove while:
    /// - Blocking mouse input with Harmony patches
    /// - Setting isControlMouse = false to force controller mode
    /// - Maintaining stable target angles (not syncing from observed position)
    /// - Only sending continuous input to the selected hand
    /// </summary>
    public static class ClockAccessibility
    {
        private static ClockGameLogic currentPuzzle = null;
        private static List<ClockChange> clockHands = null;
        private static int selectedHandIndex = 0; // 0 = hour, 1 = minute

        // Track current target angles for each hand (in clock terms: 0-360 degrees, 0=12 o'clock)
        private static float hourHandAngle = 90f;   // Start at 3 o'clock
        private static float minuteHandAngle = 90f; // Start at 15 minutes

        // Track whether keyboard control is active
        public static bool keyboardControlActive = false;
        private static float lastContinuousInputTime = 0f;
        private static readonly float CONTINUOUS_INPUT_INTERVAL = 0.016f; // ~60fps

        // Prevent re-initialization spam during win animation
        private static bool validationTriggered = false;
        private static float validationTime = 0f;

        // For announcements only (don't use these for input!)
        private static int lastAnnouncedHour = -1;
        private static int lastAnnouncedMinutes = -1;

        private static float lastInputTime = 0f;
        private static string lastAnnouncedMessage = "";
        private static float lastAnnouncementTime = 0f;
        private static readonly float INPUT_COOLDOWN = 0.15f;
        private static readonly float ANNOUNCEMENT_COOLDOWN = 1.0f;

        // Rotation amounts (in degrees on clock face)
        private static readonly float HOUR_STEP = 30f;      // 30 degrees = 1 hour
        private static readonly float MINUTE_STEP = 30f;    // 30 degrees = 5 minutes
        private static readonly float FINE_STEP = 6f;       // 6 degrees = 1 minute

        /// <summary>
        /// Main input handler, called from AccessibilityMod.OnUpdate()
        /// </summary>
        public static void HandleInput()
        {
            try
            {
                // If validation was triggered, don't re-initialize during win animation
                // Wait 3 seconds after validation before allowing re-initialization
                if (validationTriggered)
                {
                    if (Time.time - validationTime < 3.0f)
                        return;
                    else
                        validationTriggered = false;
                }

                // Check for clock puzzle
                if (currentPuzzle == null || !currentPuzzle.gameObject.activeInHierarchy)
                {
                    var puzzles = Object.FindObjectsOfType<ClockGameLogic>();
                    if (puzzles != null && puzzles.Count > 0)
                    {
                        foreach (var puzzle in puzzles)
                        {
                            if (puzzle != null && puzzle.gameObject.activeInHierarchy)
                            {
                                InitializePuzzle(puzzle);
                                break;
                            }
                        }
                    }

                    if (currentPuzzle == null)
                        return;
                }

                // Check if puzzle is won
                if (currentPuzzle.winDone)
                {
                    Reset();
                    return;
                }

                // Handle keyboard navigation
                HandleNavigation();

                // Send continuous input to maintain hand position
                if (keyboardControlActive && Time.time - lastContinuousInputTime >= CONTINUOUS_INPUT_INTERVAL)
                {
                    SendContinuousInput();
                    lastContinuousInputTime = Time.time;
                }

                // Announce changes (for feedback only, doesn't affect input)
                AnnounceObservedChanges();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in ClockAccessibility.HandleInput: {ex}");
            }
        }

        private static void InitializePuzzle(ClockGameLogic puzzle)
        {
            currentPuzzle = puzzle;
            clockHands = new List<ClockChange>();

            var hands = Object.FindObjectsOfType<ClockChange>();
            if (hands != null)
            {
                MelonLogger.Msg($"Found {hands.Count} ClockChange objects");
                foreach (var hand in hands)
                {
                    if (hand != null && hand.gameObject.activeInHierarchy)
                    {
                        clockHands.Add(hand);

                        // Force controller mode on each hand
                        hand.isControlMouse = false;
                        hand.isActive = true;
                        hand.untouched = false;

                        MelonLogger.Msg($"  {(hand.isHour ? "Hour" : "Minute")} hand: zRot={hand.zRotation:F1}, X={hand.transform.localEulerAngles.x:F1}");
                    }
                }
            }

            // Sort so hour hand is first
            clockHands.Sort((a, b) => b.isHour.CompareTo(a.isHour));
            selectedHandIndex = 0;

            // Initialize from zRotation (the game's internal tracking)
            if (clockHands.Count >= 2)
            {
                // Read initial positions from zRotation
                hourHandAngle = ZRotationToClockAngle(clockHands[0].zRotation);
                minuteHandAngle = ZRotationToClockAngle(clockHands[1].zRotation);

                lastAnnouncedHour = ClockAngleToHour(hourHandAngle);
                lastAnnouncedMinutes = ClockAngleToMinutes(minuteHandAngle);

                MelonLogger.Msg($"Clock initialized. Hour: {lastAnnouncedHour} o'clock ({hourHandAngle:F1}°), Min: {lastAnnouncedMinutes} ({minuteHandAngle:F1}°)");
            }

            Announce("Clock puzzle. Tab to switch hands, arrow keys to rotate, Shift for fine adjustment, R for positions, H for help.");
            AnnounceCurrentHand();
        }

        /// <summary>
        /// Convert zRotation to clock angle (0-360, where 0=12 o'clock).
        /// zRotation appears to be the game's internal angle tracking.
        /// </summary>
        private static float ZRotationToClockAngle(float zRot)
        {
            // Normalize to 0-360
            float angle = zRot % 360f;
            if (angle < 0) angle += 360f;
            return angle;
        }

        /// <summary>
        /// Convert clock angle to zRotation for setting.
        /// </summary>
        private static float ClockAngleToZRotation(float clockAngle)
        {
            return clockAngle;
        }

        private static void HandleNavigation()
        {
            if (clockHands == null || clockHands.Count == 0)
                return;

            if (Time.time - lastInputTime < INPUT_COOLDOWN)
                return;

            bool inputHandled = false;
            bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Tab: Switch between hands
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                selectedHandIndex = (selectedHandIndex + 1) % clockHands.Count;
                AnnounceCurrentHand();
                inputHandled = true;
            }
            // Up/Right Arrow: Rotate clockwise
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                float step = isShiftHeld ? FINE_STEP : (selectedHandIndex == 0 ? HOUR_STEP : MINUTE_STEP);
                RotateHand(step);
                inputHandled = true;
            }
            // Down/Left Arrow: Rotate counter-clockwise
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                float step = isShiftHeld ? FINE_STEP : (selectedHandIndex == 0 ? HOUR_STEP : MINUTE_STEP);
                RotateHand(-step);
                inputHandled = true;
            }
            // R: Announce current positions
            else if (Input.GetKeyDown(KeyCode.R))
            {
                AnnouncePositions();
                inputHandled = true;
            }
            // H: Help
            else if (Input.GetKeyDown(KeyCode.H))
            {
                ShowHelp();
                inputHandled = true;
            }
            // D: Debug info
            else if (Input.GetKeyDown(KeyCode.D))
            {
                ShowDebugInfo();
                inputHandled = true;
            }
            // Enter/Space: Validate solution (call CheckSolution)
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                ValidateSolution();
                inputHandled = true;
            }
            // Backspace: Exit the minigame
            else if (Input.GetKeyDown(KeyCode.Backspace))
            {
                ExitPuzzle();
                inputHandled = true;
            }

            if (inputHandled)
            {
                lastInputTime = Time.time;
            }
        }

        /// <summary>
        /// Sends continuous stick input to the SELECTED hand only.
        /// </summary>
        private static void SendContinuousInput()
        {
            if (clockHands == null || selectedHandIndex >= clockHands.Count)
                return;

            var hand = clockHands[selectedHandIndex];
            if (hand == null) return;

            float targetAngle = hand.isHour ? hourHandAngle : minuteHandAngle;
            SendStickInput(hand, targetAngle, hand.isHour);
        }

        /// <summary>
        /// Sends stick input to a specific hand.
        /// </summary>
        private static void SendStickInput(ClockChange hand, float clockAngle, bool isHourHand)
        {
            if (hand == null) return;

            // Ensure controller mode
            hand.isControlMouse = false;
            hand.isActive = true;
            hand.untouched = false;

            // Convert clock angle to stick direction
            // Clock: 0=12 o'clock (up), 90=3 o'clock (right), 180=6 o'clock (down), 270=9 o'clock (left)
            // Standard math: x = sin(angle), y = cos(angle) where 0° = up
            float angleRad = clockAngle * Mathf.Deg2Rad;
            Vector2 stickDirection = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad));

            var inputValue = new InputValue();
            inputValue.SetCustomVector2Value(stickDirection);
            inputValue.hasCustomVector2Value = true;

            if (isHourHand)
            {
                hand.OnLStickMove(inputValue);
            }
            else
            {
                hand.OnRStickMove(inputValue);
            }
        }

        /// <summary>
        /// Rotates the selected hand by updating target angle.
        /// </summary>
        private static void RotateHand(float degrees)
        {
            if (clockHands == null || selectedHandIndex >= clockHands.Count)
                return;

            var hand = clockHands[selectedHandIndex];
            if (hand == null) return;

            // Activate keyboard control
            keyboardControlActive = true;

            // Update target angle (NOT synced from observed position)
            float currentAngle = hand.isHour ? hourHandAngle : minuteHandAngle;
            float newAngle = currentAngle + degrees;

            // Normalize to 0-360
            while (newAngle < 0) newAngle += 360f;
            while (newAngle >= 360) newAngle -= 360f;

            // Store the new target
            if (hand.isHour)
                hourHandAngle = newAngle;
            else
                minuteHandAngle = newAngle;

            MelonLogger.Msg($"{(hand.isHour ? "Hour" : "Minute")} hand: Target {currentAngle:F1}° -> {newAngle:F1}°");

            // Send immediate input
            SendStickInput(hand, newAngle, hand.isHour);

            // Announce
            if (hand.isHour)
            {
                int hour = ClockAngleToHour(newAngle);
                Announce($"{hour} o'clock");
            }
            else
            {
                int minutes = ClockAngleToMinutes(newAngle);
                Announce($"{minutes} minutes");
            }
        }

        /// <summary>
        /// Announce changes based on observed transform (for feedback only).
        /// Does NOT update target angles.
        /// </summary>
        private static void AnnounceObservedChanges()
        {
            if (clockHands == null || clockHands.Count < 2) return;

            // Only announce if keyboard control is not active (i.e., controller is being used)
            if (keyboardControlActive) return;

            var hourHand = clockHands[0];
            var minuteHand = clockHands[1];

            if (hourHand != null)
            {
                float angle = ZRotationToClockAngle(hourHand.zRotation);
                int hour = ClockAngleToHour(angle);
                if (hour != lastAnnouncedHour && lastAnnouncedHour != -1)
                {
                    MelonLogger.Msg($"Hour observed: {lastAnnouncedHour} -> {hour}");
                    lastAnnouncedHour = hour;
                }
            }

            if (minuteHand != null)
            {
                float angle = ZRotationToClockAngle(minuteHand.zRotation);
                int minutes = ClockAngleToMinutes(angle);
                if (minutes != lastAnnouncedMinutes && lastAnnouncedMinutes != -1)
                {
                    MelonLogger.Msg($"Minutes observed: {lastAnnouncedMinutes} -> {minutes}");
                    lastAnnouncedMinutes = minutes;
                }
            }
        }

        private static int ClockAngleToHour(float angle)
        {
            int hour = Mathf.RoundToInt(angle / 30f) % 12;
            if (hour == 0) hour = 12;
            return hour;
        }

        private static int ClockAngleToMinutes(float angle)
        {
            int minutes = Mathf.RoundToInt(angle / 6f) % 60;
            return minutes;
        }

        private static void AnnounceCurrentHand()
        {
            if (clockHands == null || selectedHandIndex >= clockHands.Count)
                return;

            var hand = clockHands[selectedHandIndex];
            string handName = hand.isHour ? "Hour hand" : "Minute hand";

            if (hand.isHour)
            {
                int hour = ClockAngleToHour(hourHandAngle);
                Announce($"{handName} at {hour} o'clock");
            }
            else
            {
                int minutes = ClockAngleToMinutes(minuteHandAngle);
                Announce($"{handName} at {minutes} minutes");
            }
        }

        private static void AnnouncePositions()
        {
            int hour = ClockAngleToHour(hourHandAngle);
            int minutes = ClockAngleToMinutes(minuteHandAngle);
            Announce($"Hour hand at {hour} o'clock, Minute hand at {minutes} minutes");
        }

        /// <summary>
        /// Validates the current solution by calling CheckSolution().
        /// The game uses physics triggers to detect correct positions, so this
        /// checks if both hands are in their trigger zones and triggers the win.
        /// </summary>
        private static void ValidateSolution()
        {
            if (currentPuzzle == null)
                return;

            bool hourCorrect = currentPuzzle.isHourCorrect;
            bool minCorrect = currentPuzzle.isMinCorrect;

            MelonLogger.Msg($"Validating solution: isHourCorrect={hourCorrect}, isMinCorrect={minCorrect}");

            if (hourCorrect && minCorrect)
            {
                Announce("Correct!");
                validationTriggered = true;
                validationTime = Time.time;
                currentPuzzle.CheckSolution();
                Reset();
            }
            else if (!hourCorrect && !minCorrect)
            {
                Announce("Both hands incorrect");
            }
            else if (!hourCorrect)
            {
                Announce("Hour hand incorrect");
            }
            else
            {
                Announce("Minute hand incorrect");
            }
        }

        /// <summary>
        /// Exits the clock puzzle minigame.
        /// </summary>
        private static void ExitPuzzle()
        {
            if (currentPuzzle != null)
            {
                MelonLogger.Msg("Exiting clock puzzle");
                Announce("Exiting puzzle");
                currentPuzzle.ExitMinigame();
                Reset();
            }
        }

        private static void ShowHelp()
        {
            Announce("Tab to switch hands. Arrow keys rotate. Shift for fine adjustment. Enter to validate. R announces positions. Backspace to exit.");
        }

        private static void ShowDebugInfo()
        {
            if (clockHands == null || clockHands.Count < 2) return;

            var hourHand = clockHands[0];
            var minuteHand = clockHands[1];

            MelonLogger.Msg("=== CLOCK DEBUG ===");
            MelonLogger.Msg($"Keyboard control: {keyboardControlActive}");
            MelonLogger.Msg($"Selected hand: {selectedHandIndex} ({(selectedHandIndex == 0 ? "Hour" : "Minute")})");

            MelonLogger.Msg($"Hour hand:");
            MelonLogger.Msg($"  Target angle: {hourHandAngle:F1}° ({ClockAngleToHour(hourHandAngle)} o'clock)");
            MelonLogger.Msg($"  zRotation: {hourHand.zRotation:F1}");
            MelonLogger.Msg($"  transform.X: {hourHand.transform.localEulerAngles.x:F1}°");
            MelonLogger.Msg($"  isControlMouse: {hourHand.isControlMouse}");
            MelonLogger.Msg($"  isActive: {hourHand.isActive}, untouched: {hourHand.untouched}");

            MelonLogger.Msg($"Minute hand:");
            MelonLogger.Msg($"  Target angle: {minuteHandAngle:F1}° ({ClockAngleToMinutes(minuteHandAngle)} min)");
            MelonLogger.Msg($"  zRotation: {minuteHand.zRotation:F1}");
            MelonLogger.Msg($"  transform.X: {minuteHand.transform.localEulerAngles.x:F1}°");
            MelonLogger.Msg($"  isControlMouse: {minuteHand.isControlMouse}");
            MelonLogger.Msg($"  isActive: {minuteHand.isActive}, untouched: {minuteHand.untouched}");

            // Show stick vector
            float rad = (selectedHandIndex == 0 ? hourHandAngle : minuteHandAngle) * Mathf.Deg2Rad;
            MelonLogger.Msg($"Stick vector: ({Mathf.Sin(rad):F3}, {Mathf.Cos(rad):F3})");

            if (currentPuzzle != null)
            {
                MelonLogger.Msg($"Puzzle: isHourCorrect={currentPuzzle.isHourCorrect}, isMinCorrect={currentPuzzle.isMinCorrect}, winDone={currentPuzzle.winDone}");
            }
            MelonLogger.Msg("===================");

            Announce("Debug info logged to console");
        }

        private static void Announce(string message)
        {
            if (message == lastAnnouncedMessage && (Time.time - lastAnnouncementTime) < ANNOUNCEMENT_COOLDOWN)
                return;

            lastAnnouncedMessage = message;
            lastAnnouncementTime = Time.time;

            AccessibilityMod.Speak(message, interrupt: true);
            MelonLogger.Msg($"Announced: {message}");
        }

        public static void Reset()
        {
            currentPuzzle = null;
            clockHands = null;
            selectedHandIndex = 0;
            hourHandAngle = 90f;
            minuteHandAngle = 90f;
            keyboardControlActive = false;
            lastContinuousInputTime = 0f;
            lastAnnouncedHour = -1;
            lastAnnouncedMinutes = -1;
            lastInputTime = 0f;
            lastAnnouncedMessage = "";
            lastAnnouncementTime = 0f;
            // Don't reset validationTriggered here - it's reset after timeout in HandleInput
            MelonLogger.Msg("Clock puzzle accessibility reset");
        }
    }

    /// <summary>
    /// Harmony patch to block mouse input on ClockChange when keyboard control is active.
    /// </summary>
    [HarmonyPatch(typeof(ClockChange), nameof(ClockChange.OnMouseMove))]
    public class ClockChange_OnMouseMove_Patch
    {
        static bool Prefix(ClockChange __instance)
        {
            // Block mouse input when keyboard control is active
            if (ClockAccessibility.keyboardControlActive)
            {
                // Ensure controller mode
                __instance.isControlMouse = false;
                return false; // Skip original method
            }
            return true; // Allow original method
        }
    }
}
