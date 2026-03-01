using MelonLoader;
using Il2Cpp;
using Il2CppChickenPolice.UI;
using UnityEngine;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the achievements system.
    /// Uses polling approach since achievements don't use MenuSelector navigation.
    /// </summary>
    public static class AchievementsAccessibility
    {
        private static ExtrasAchievements currentViewer = null;
        private static List<ExtrasAchievementItem> cachedAchievements = null;
        private static int currentIndex = -1;
        private static int lastAnnouncedIndex = -1;
        private static float lastInputTime = 0f;
        private static readonly float INPUT_COOLDOWN = 0.2f;

        /// <summary>
        /// Handles achievements input processing - called from AccessibilityMod.OnUpdate()
        /// </summary>
        public static void HandleInput()
        {
            try
            {
                // Check if achievements window is active
                if (currentViewer == null)
                {
                    // Try to find active viewer using FindObjectsOfType
                    var viewers = Object.FindObjectsOfType<ExtrasAchievements>();
                    if (viewers != null && viewers.Count > 0)
                    {
                        foreach (var viewer in viewers)
                        {
                            if (viewer != null && viewer.gameObject.activeInHierarchy)
                            {
                                InitializeViewer(viewer);
                                break;
                            }
                        }
                    }
                    else
                    {
                        return; // No viewer active
                    }
                }

                // Check if viewer is still valid
                if (currentViewer == null || !currentViewer.gameObject.activeInHierarchy)
                {
                    Reset();
                    return;
                }

                // Handle keyboard/controller navigation
                HandleNavigation();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in AchievementsAccessibility.HandleInput: {ex}");
            }
        }

        /// <summary>
        /// Initializes the achievements viewer and caches all achievement items.
        /// </summary>
        private static void InitializeViewer(ExtrasAchievements viewer)
        {
            try
            {
                currentViewer = viewer;
                currentIndex = 0;
                lastAnnouncedIndex = -1;

                // Find all ExtrasAchievementItem components
                // Search globally since they might not be direct children or might be inactive
                cachedAchievements = new List<ExtrasAchievementItem>();

                // Try method 1: Search as children (including inactive)
                var items = currentViewer.GetComponentsInChildren<ExtrasAchievementItem>(true);
                if (items != null && items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        if (item != null)
                        {
                            cachedAchievements.Add(item);
                            MelonLogger.Msg($"Found achievement via GetComponentsInChildren: {item.itemName}");
                        }
                    }
                }

                // Try method 2: Search globally if nothing found
                if (cachedAchievements.Count == 0)
                {
                    MelonLogger.Warning("No achievements found in children, searching globally...");
                    var allItems = Object.FindObjectsOfType<ExtrasAchievementItem>();
                    if (allItems != null)
                    {
                        foreach (var item in allItems)
                        {
                            if (item != null)
                            {
                                cachedAchievements.Add(item);
                                MelonLogger.Msg($"Found achievement via FindObjectsOfType: {item.itemName}");
                            }
                        }
                    }
                }

                MelonLogger.Msg($"Achievements viewer initialized with {cachedAchievements.Count} achievements");
                AccessibilityMod.Speak("Achievements menu opened", true);

                // Announce first achievement
                if (cachedAchievements.Count > 0)
                {
                    AnnounceCurrentAchievement();
                }
                else
                {
                    AccessibilityMod.Speak("No achievements found", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error initializing achievements viewer: {ex}");
            }
        }

        /// <summary>
        /// Handles keyboard and controller navigation.
        /// </summary>
        private static void HandleNavigation()
        {
            if (!CanProcessInput())
                return;

            if (cachedAchievements == null || cachedAchievements.Count == 0)
                return;

            bool inputDetected = false;

            // Keyboard: Tab/Shift+Tab or Arrow keys
            if (Input.GetKeyDown(KeyCode.Tab) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                NextAchievement();
                inputDetected = true;
            }
            else if ((Input.GetKeyDown(KeyCode.Tab) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) ||
                     Input.GetKeyDown(KeyCode.UpArrow))
            {
                PreviousAchievement();
                inputDetected = true;
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                NextAchievement();
                inputDetected = true;
            }

            // Controller: D-pad Up/Down
            else if (Input.GetKeyDown(KeyCode.JoystickButton13)) // D-pad Up
            {
                PreviousAchievement();
                inputDetected = true;
            }
            else if (Input.GetKeyDown(KeyCode.JoystickButton14)) // D-pad Down
            {
                NextAchievement();
                inputDetected = true;
            }

            // List all achievements (L key or Y button)
            else if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.JoystickButton3))
            {
                ListAllAchievements();
                inputDetected = true;
            }

            if (inputDetected)
            {
                lastInputTime = Time.time;
            }
        }

        /// <summary>
        /// Navigates to the next achievement.
        /// </summary>
        private static void NextAchievement()
        {
            if (cachedAchievements == null || cachedAchievements.Count == 0)
                return;

            currentIndex = (currentIndex + 1) % cachedAchievements.Count;
            AnnounceCurrentAchievement();
        }

        /// <summary>
        /// Navigates to the previous achievement.
        /// </summary>
        private static void PreviousAchievement()
        {
            if (cachedAchievements == null || cachedAchievements.Count == 0)
                return;

            currentIndex = (currentIndex - 1 + cachedAchievements.Count) % cachedAchievements.Count;
            AnnounceCurrentAchievement();
        }

        /// <summary>
        /// Announces the current achievement (name, description, and unlock status).
        /// </summary>
        private static void AnnounceCurrentAchievement()
        {
            try
            {
                if (cachedAchievements == null || currentIndex < 0 || currentIndex >= cachedAchievements.Count)
                    return;

                var achievement = cachedAchievements[currentIndex];
                if (achievement == null)
                    return;

                // Get achievement name
                string name = GetLocalizedTextContent(achievement.nameLocale);
                if (string.IsNullOrEmpty(name))
                {
                    name = achievement.itemName; // Fallback to itemName
                }

                // Get achievement description
                string description = GetLocalizedTextContent(achievement.descLocale);

                // Check if achievement is unlocked (based on image visibility)
                bool isUnlocked = IsAchievementUnlocked(achievement);

                // Build announcement
                string announcement = $"{name}";
                announcement += isUnlocked ? ", Unlocked" : ", Locked";

                if (!string.IsNullOrEmpty(description))
                {
                    announcement += ". " + description;
                }

                // Add position info
                announcement += $". {currentIndex + 1} of {cachedAchievements.Count}";

                AccessibilityMod.Speak(announcement, true);
                lastAnnouncedIndex = currentIndex;
                MelonLogger.Msg($"Announced achievement: {name} ({(isUnlocked ? "Unlocked" : "Locked")})");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing achievement: {ex}");
            }
        }

        /// <summary>
        /// Lists all achievements with their unlock status.
        /// </summary>
        private static void ListAllAchievements()
        {
            try
            {
                if (cachedAchievements == null || cachedAchievements.Count == 0)
                {
                    AccessibilityMod.Speak("No achievements available", true);
                    return;
                }

                int unlockedCount = 0;
                foreach (var achievement in cachedAchievements)
                {
                    if (IsAchievementUnlocked(achievement))
                    {
                        unlockedCount++;
                    }
                }

                string message = $"{cachedAchievements.Count} achievements total. {unlockedCount} unlocked, {cachedAchievements.Count - unlockedCount} locked.";
                AccessibilityMod.Speak(message, true);
                MelonLogger.Msg(message);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error listing achievements: {ex}");
            }
        }

        /// <summary>
        /// Determines if an achievement is unlocked based on image visibility.
        /// </summary>
        private static bool IsAchievementUnlocked(ExtrasAchievementItem achievement)
        {
            try
            {
                // Check if normalImage is active/visible (unlocked) vs lockedImage (locked)
                if (achievement.normalImage != null && achievement.normalImage.gameObject.activeInHierarchy)
                {
                    return true;
                }
                if (achievement.lockedImage != null && achievement.lockedImage.gameObject.activeInHierarchy)
                {
                    return false;
                }
                // Default to locked if can't determine
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts text from LocalizedText component.
        /// </summary>
        private static string GetLocalizedTextContent(LocalizedText localizedText)
        {
            if (localizedText == null)
                return "";

            try
            {
                string text = localizedText.text;
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
            catch
            {
            }

            return "";
        }

        /// <summary>
        /// Checks if input can be processed (cooldown check).
        /// </summary>
        private static bool CanProcessInput()
        {
            return (Time.time - lastInputTime) >= INPUT_COOLDOWN;
        }

        /// <summary>
        /// Resets the achievements accessibility state.
        /// </summary>
        public static void Reset()
        {
            currentViewer = null;
            cachedAchievements = null;
            currentIndex = -1;
            lastAnnouncedIndex = -1;
            lastInputTime = 0f;
            MelonLogger.Msg("Achievements accessibility reset");
        }
    }
}
