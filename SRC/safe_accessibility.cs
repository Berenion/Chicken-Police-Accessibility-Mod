using MelonLoader;
using Il2Cpp;
using UnityEngine;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the safe puzzle mini-game.
    /// Uses polling approach since SafeGameLogic only has private lifecycle methods.
    /// Announces digit positions without revealing the solution.
    /// </summary>
    public static class SafeAccessibility
    {
        private static SafeGameLogic currentSafe = null;
        private static List<SafeDisplay> displays = null;
        private static int selectedDisplayIndex = 0; // 0-3 for 4 digits
        private static int[] lastAnnouncedPositions = new int[4] { -1, -1, -1, -1 };
        private static float lastInputTime = 0f;
        private static readonly float INPUT_COOLDOWN = 0.15f;
        private static float lastHorizontalAxis = 0f;
        private static float lastVerticalAxis = 0f;

        /// <summary>
        /// Handles safe puzzle input processing - called from AccessibilityMod.OnUpdate()
        /// </summary>
        public static void HandleInput()
        {
            try
            {
                // Check if safe puzzle is active
                if (currentSafe == null)
                {
                    // Try to find active safe using FindObjectsOfType
                    var safes = UnityEngine.Object.FindObjectsOfType<SafeGameLogic>();
                    if (safes != null && safes.Count > 0)
                    {
                        foreach (var safe in safes)
                        {
                            if (safe != null && safe.gameObject.activeInHierarchy)
                            {
                                InitializeSafe(safe);
                                break;
                            }
                        }
                    }
                    else
                    {
                        return; // No safe active
                    }
                }

                // Check if safe is still valid
                if (currentSafe == null || !currentSafe.gameObject.activeInHierarchy)
                {
                    Reset();
                    return;
                }

                // Handle keyboard and controller navigation
                HandleNavigation();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in SafeAccessibility.HandleInput: {ex}");
            }
        }

        /// <summary>
        /// Initializes the safe puzzle and caches display components.
        /// </summary>
        private static void InitializeSafe(SafeGameLogic safe)
        {
            try
            {
                currentSafe = safe;
                selectedDisplayIndex = 0;
                lastAnnouncedPositions = new int[4] { -1, -1, -1, -1 };
                lastHorizontalAxis = 0f;
                lastVerticalAxis = 0f;

                // Cache all display components
                displays = new List<SafeDisplay>();
                if (currentSafe.display1 != null) displays.Add(currentSafe.display1);
                if (currentSafe.display2 != null) displays.Add(currentSafe.display2);
                if (currentSafe.display3 != null) displays.Add(currentSafe.display3);
                if (currentSafe.display4 != null) displays.Add(currentSafe.display4);

                MelonLogger.Msg($"Safe puzzle initialized with {displays.Count} displays");
                AccessibilityMod.Speak("Safe puzzle opened. Use arrow keys to navigate and rotate digits, H for help", true);

                // Announce initial position of first digit
                if (displays.Count > 0)
                {
                    AnnounceCurrentDisplay();
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error initializing safe puzzle: {ex}");
            }
        }

        /// <summary>
        /// Handles keyboard and controller navigation for safe puzzle.
        /// </summary>
        private static void HandleNavigation()
        {
            if (!CanProcessInput())
                return;

            if (displays == null || displays.Count == 0)
                return;

            bool inputDetected = false;

            // Keyboard: Left Arrow - Previous digit
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                SwitchToPreviousDisplay();
                inputDetected = true;
            }
            // Keyboard: Right Arrow - Next digit
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                SwitchToNextDisplay();
                inputDetected = true;
            }
            // Keyboard: Up Arrow - Increment digit
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                RotateSelectedDisplay(1);
                inputDetected = true;
            }
            // Keyboard: Down Arrow - Decrement digit
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                RotateSelectedDisplay(-1);
                inputDetected = true;
            }
            // H: Help message
            else if (Input.GetKeyDown(KeyCode.H))
            {
                ShowHelp();
                inputDetected = true;
            }
            // R: Read all digits
            else if (Input.GetKeyDown(KeyCode.R))
            {
                AnnounceAllDigits();
                inputDetected = true;
            }
            // Enter or Space or A button: Confirm combination (pull lever)
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0))
            {
                ConfirmCombination();
                inputDetected = true;
            }

            // Controller: Horizontal axis (Left stick/D-pad Left/Right) - Switch digits
            if (!inputDetected)
            {
                float horizontalInput = Input.GetAxis("Horizontal");

                // Detect axis crossing threshold (from neutral to left/right)
                if (Mathf.Abs(lastHorizontalAxis) < 0.5f && Mathf.Abs(horizontalInput) >= 0.5f)
                {
                    if (horizontalInput < -0.5f)
                    {
                        SwitchToPreviousDisplay();
                        inputDetected = true;
                    }
                    else if (horizontalInput > 0.5f)
                    {
                        SwitchToNextDisplay();
                        inputDetected = true;
                    }
                }
                lastHorizontalAxis = horizontalInput;
            }

            // Controller: Vertical axis (Left stick/D-pad Up/Down) - Rotate digit
            if (!inputDetected)
            {
                float verticalInput = Input.GetAxis("Vertical");

                // Detect axis crossing threshold (from neutral to up/down)
                if (Mathf.Abs(lastVerticalAxis) < 0.5f && Mathf.Abs(verticalInput) >= 0.5f)
                {
                    if (verticalInput > 0.5f)
                    {
                        RotateSelectedDisplay(1);
                        inputDetected = true;
                    }
                    else if (verticalInput < -0.5f)
                    {
                        RotateSelectedDisplay(-1);
                        inputDetected = true;
                    }
                }
                lastVerticalAxis = verticalInput;
            }

            if (inputDetected)
            {
                lastInputTime = Time.time;
            }
        }

        /// <summary>
        /// Switches to the previous digit display.
        /// </summary>
        private static void SwitchToPreviousDisplay()
        {
            if (displays == null || displays.Count == 0)
                return;

            selectedDisplayIndex--;
            if (selectedDisplayIndex < 0)
            {
                selectedDisplayIndex = displays.Count - 1; // Wrap to last
            }

            AnnounceCurrentDisplay();
        }

        /// <summary>
        /// Switches to the next digit display.
        /// </summary>
        private static void SwitchToNextDisplay()
        {
            if (displays == null || displays.Count == 0)
                return;

            selectedDisplayIndex++;
            if (selectedDisplayIndex >= displays.Count)
            {
                selectedDisplayIndex = 0; // Wrap to first
            }

            AnnounceCurrentDisplay();
        }

        /// <summary>
        /// Rotates the selected display digit up or down.
        /// </summary>
        private static void RotateSelectedDisplay(int direction)
        {
            if (displays == null || selectedDisplayIndex >= displays.Count)
                return;

            var display = displays[selectedDisplayIndex];
            if (display == null)
                return;

            try
            {
                // Use the public Go method to rotate the digit
                display.Go(direction);

                MelonLogger.Msg($"Rotated display {selectedDisplayIndex + 1} in direction {direction}");

                // Announce the new value after a brief delay to allow animation
                System.Threading.Tasks.Task.Delay(50).ContinueWith(_ => AnnounceCurrentDisplay());
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error rotating display: {ex}");
            }
        }

        /// <summary>
        /// Announces the currently selected digit and its value.
        /// </summary>
        private static void AnnounceCurrentDisplay()
        {
            try
            {
                if (displays == null || selectedDisplayIndex >= displays.Count)
                    return;

                var display = displays[selectedDisplayIndex];
                if (display == null)
                    return;

                int position = display.position;
                string ordinal = GetOrdinal(selectedDisplayIndex + 1);
                string animalName = GetAnimalName(position);

                string announcement = $"{ordinal} digit, currently {animalName}";
                AccessibilityMod.Speak(announcement, true);

                // Update last announced position
                lastAnnouncedPositions[selectedDisplayIndex] = position;

                MelonLogger.Msg($"Announced: {announcement}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing display: {ex}");
            }
        }

        /// <summary>
        /// Announces all four digits in the current combination.
        /// </summary>
        private static void AnnounceAllDigits()
        {
            try
            {
                if (displays == null || displays.Count == 0)
                    return;

                string announcement = "Combination: ";
                for (int i = 0; i < displays.Count; i++)
                {
                    var display = displays[i];
                    if (display != null)
                    {
                        string animalName = GetAnimalName(display.position);
                        announcement += animalName;
                        if (i < displays.Count - 1)
                        {
                            announcement += ", ";
                        }
                    }
                }

                AccessibilityMod.Speak(announcement, true);
                MelonLogger.Msg($"Announced all digits: {announcement}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing all digits: {ex}");
            }
        }

        /// <summary>
        /// Confirms the current combination by pulling the lever (calls CheckSolution).
        /// </summary>
        private static void ConfirmCombination()
        {
            try
            {
                if (currentSafe == null)
                    return;

                AccessibilityMod.Speak("Checking combination", true);
                MelonLogger.Msg("Player confirmed combination - calling CheckSolution");

                // Call the game's CheckSolution method with true for central handle
                currentSafe.CheckSolution(true);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error confirming combination: {ex}");
                AccessibilityMod.Speak("Error checking combination", true);
            }
        }

        /// <summary>
        /// Shows help message with available controls.
        /// </summary>
        private static void ShowHelp()
        {
            string help = "Safe puzzle controls: " +
                          "Each dial shows an animal symbol. " +
                          "Left or Right arrow to switch between dials. " +
                          "Up or Down arrow to rotate selected dial through animals. " +
                          "Enter or Space to confirm combination. " +
                          "R to hear all animals in current combination. " +
                          "H for this help message. " +
                          "Controller: Left stick to navigate and rotate dials, A button to confirm.";
            AccessibilityMod.Speak(help, true);
            MelonLogger.Msg("Showed safe puzzle help");
        }

        /// <summary>
        /// Converts a dial position to its animal name.
        /// Each position on the safe dial is represented by an animal symbol.
        /// </summary>
        private static string GetAnimalName(int position)
        {
            switch (position)
            {
                case 0: return "Stork";
                case 1: return "Sheep";
                case 2: return "Tiger";
                case 3: return "Fox";
                case 4: return "Lion";
                case 5: return "Falcon";
                case 6: return "Wolf";
                default: return $"Unknown ({position})";
            }
        }

        /// <summary>
        /// Converts a number to its ordinal representation (1st, 2nd, 3rd, 4th).
        /// </summary>
        private static string GetOrdinal(int number)
        {
            switch (number)
            {
                case 1: return "1st";
                case 2: return "2nd";
                case 3: return "3rd";
                case 4: return "4th";
                default: return $"{number}th";
            }
        }

        /// <summary>
        /// Checks if input can be processed (cooldown check).
        /// </summary>
        private static bool CanProcessInput()
        {
            return (Time.time - lastInputTime) >= INPUT_COOLDOWN;
        }

        /// <summary>
        /// Resets the safe puzzle accessibility state.
        /// </summary>
        public static void Reset()
        {
            currentSafe = null;
            displays = null;
            selectedDisplayIndex = 0;
            lastAnnouncedPositions = new int[4] { -1, -1, -1, -1 };
            lastInputTime = 0f;
            lastHorizontalAxis = 0f;
            lastVerticalAxis = 0f;
            MelonLogger.Msg("Safe puzzle accessibility reset");
        }
    }
}
