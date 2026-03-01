using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using Il2CppChickenPolice.UI;
using UnityEngine;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the collectibles system.
    /// Uses Harmony patches to announce collectible content and pagination.
    /// Follows the same pattern as notebook.cs for consistency.
    /// </summary>
    public static class CollectiblesAccessibility
    {
        private static string lastAnnouncedCollectible = null;
        private static int lastAnnouncedPage = -1;

        /// <summary>
        /// Handles collectible list item announcements from MenuSelector patch.
        /// Called from MenuSelector_SetSelectedIdx_Patch when on ExtrasCollectibles window.
        /// </summary>
        public static void AnnounceCollectibleItem(MenuButton button)
        {
            try
            {
                if (button == null)
                    return;

                // Get ExtrasCollectiblesItem component
                var collectibleItem = button.GetComponent<ExtrasCollectiblesItem>();
                if (collectibleItem == null)
                    return;

                // Get the localized label text
                string itemName = GetCollectibleItemName(collectibleItem);
                if (string.IsNullOrEmpty(itemName))
                    return;

                // Check if unlocked
                bool isUnlocked = false;
                try
                {
                    isUnlocked = collectibleItem.IsUnlocked();
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"Could not check unlock status: {ex.Message}");
                }

                // Build announcement
                string announcement = itemName;
                if (!isUnlocked)
                {
                    announcement += ", Locked";
                }

                AccessibilityMod.Speak(announcement, true);
                MelonLogger.Msg($"Announced collectible item: {announcement}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing collectible item: {ex}");
            }
        }

        /// <summary>
        /// Gets the name of a collectible item from the menu list.
        /// </summary>
        private static string GetCollectibleItemName(ExtrasCollectiblesItem item)
        {
            try
            {
                // Try GetLocalizedLabelText method
                if (item != null)
                {
                    try
                    {
                        string labelText = item.GetLocalizedLabelText();
                        if (!string.IsNullOrEmpty(labelText))
                        {
                            return labelText;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"Could not get localized label text: {ex.Message}");
                    }
                }

                // Try itemLabel.text property
                if (item != null && item.itemLabel != null)
                {
                    try
                    {
                        string text = item.itemLabel.text;
                        if (!string.IsNullOrEmpty(text))
                        {
                            return text;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"Could not get itemLabel.text: {ex.Message}");
                    }
                }

                // Fallback to itemName property
                if (item != null && !string.IsNullOrEmpty(item.itemName))
                {
                    return item.itemName;
                }

                return "Unknown collectible";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error getting collectible item name: {ex}");
                return "Unknown collectible";
            }
        }

        /// <summary>
        /// Checks if the collectibles menu is currently active.
        /// Used by MenuSelector patch to determine if we should announce collectible items.
        /// </summary>
        public static bool IsCollectiblesMenuActive()
        {
            try
            {
                var windowManager = WindowManager.instance;
                if (windowManager == null)
                    return false;

                var window = windowManager.GetWindow(WindowID.ExtrasCollectibles);
                if (window == null)
                    return false;

                return window.gameObject.activeInHierarchy;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error checking if collectibles menu is active: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Announces the current collectible content (heading and description).
        /// Called from OpenCollectible patch.
        /// </summary>
        private static void AnnounceCollectibleContent(ExtrasCollectibles viewer)
        {
            try
            {
                if (viewer == null)
                    return;

                // Get heading
                string heading = GetLocalizedTextContent(viewer.headingLocalizedText);

                // Get description
                string description = GetLocalizedTextContent(viewer.descriptionLocalizedText);

                // Build announcement
                if (!string.IsNullOrEmpty(heading))
                {
                    string announcement = heading;
                    if (!string.IsNullOrEmpty(description))
                    {
                        announcement += ". " + description;
                    }

                    AccessibilityMod.Speak(announcement, true);
                    lastAnnouncedCollectible = heading;
                    MelonLogger.Msg($"Announced collectible content: {heading}");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing collectible content: {ex}");
            }
        }

        /// <summary>
        /// Extracts text content from a LocalizedText component.
        /// Similar to how notebook extracts character names and clue text.
        /// </summary>
        private static string GetLocalizedTextContent(Il2Cpp.LocalizedText localizedText)
        {
            if (localizedText == null)
                return "";

            try
            {
                // Method 1: Try direct text property
                try
                {
                    string text = localizedText.text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"Could not get text via text property: {ex.Message}");
                }

                // Method 2: Try textRef property (Unity Text component)
                try
                {
                    if (localizedText.textRef != null)
                    {
                        string text = localizedText.textRef.text;
                        if (!string.IsNullOrEmpty(text))
                        {
                            return text;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"Could not get text via textRef: {ex.Message}");
                }

                // Method 3: Try reflection to find TextMeshPro components
                try
                {
                    var components = localizedText.gameObject.GetComponentsInChildren<Component>();
                    foreach (var component in components)
                    {
                        if (component == null) continue;

                        var componentType = component.GetType();
                        if (componentType.Name.Contains("TextMeshPro"))
                        {
                            var textProperty = componentType.GetProperty("text");
                            if (textProperty != null)
                            {
                                var textValue = textProperty.GetValue(component);
                                if (textValue != null && !string.IsNullOrEmpty(textValue.ToString()))
                                {
                                    return textValue.ToString();
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"Could not get text via TextMeshPro reflection: {ex.Message}");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error getting localized text content: {ex}");
            }

            return "";
        }

        /// <summary>
        /// Announces the current page number.
        /// Called from page navigation patches.
        /// </summary>
        private static void AnnounceCurrentPage(ExtrasCollectibles viewer)
        {
            try
            {
                if (viewer == null || viewer.extrasPager == null)
                    return;

                var pager = viewer.extrasPager;
                int currentPage = pager.pageIdx + 1; // Convert to 1-based
                int totalPages = 0;

                if (pager.pages != null)
                {
                    totalPages = pager.pages.Count;
                }

                if (currentPage != lastAnnouncedPage)
                {
                    string announcement = $"Page {currentPage}";
                    if (totalPages > 0)
                    {
                        announcement += $" of {totalPages}";
                    }

                    AccessibilityMod.Speak(announcement, true);
                    lastAnnouncedPage = currentPage;
                    MelonLogger.Msg($"Announced page: {announcement}");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing current page: {ex}");
            }
        }

        /// <summary>
        /// Resets tracking when collectibles viewer is closed.
        /// </summary>
        private static void ResetTracking()
        {
            lastAnnouncedCollectible = null;
            lastAnnouncedPage = -1;
            MelonLogger.Msg("Collectibles tracking reset");
        }

        #region Harmony Patches

        /// <summary>
        /// Patch for when a collectible is opened/selected.
        /// This is called AFTER the user selects a collectible from the menu,
        /// so the localization should be fully loaded by this point.
        /// </summary>
        [HarmonyPatch(typeof(ExtrasCollectibles), "OpenCollectible")]
        public class ExtrasCollectibles_OpenCollectible_Patch
        {
            static void Postfix(ExtrasCollectibles __instance, string name)
            {
                try
                {
                    MelonLogger.Msg($"OpenCollectible called for: {name}");

                    // Reset page tracking when opening a new collectible
                    lastAnnouncedPage = -1;

                    // Announce the collectible content
                    AnnounceCollectibleContent(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in ExtrasCollectibles.OpenCollectible patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for navigating to the next page.
        /// </summary>
        [HarmonyPatch(typeof(ExtrasCollectibles), "SetNextPageInBackground")]
        public class ExtrasCollectibles_SetNextPageInBackground_Patch
        {
            static void Postfix(ExtrasCollectibles __instance)
            {
                try
                {
                    // Force re-announcement by clearing the last announced page
                    lastAnnouncedPage = -1;
                    AnnounceCurrentPage(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in ExtrasCollectibles.SetNextPageInBackground patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for navigating to the previous page.
        /// </summary>
        [HarmonyPatch(typeof(ExtrasCollectibles), "SetPrevPageInBackground")]
        public class ExtrasCollectibles_SetPrevPageInBackground_Patch
        {
            static void Postfix(ExtrasCollectibles __instance)
            {
                try
                {
                    // Force re-announcement by clearing the last announced page
                    lastAnnouncedPage = -1;
                    AnnounceCurrentPage(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in ExtrasCollectibles.SetPrevPageInBackground patch: {ex}");
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Extends MenuSelector patch to handle collectibles list navigation.
    /// </summary>
    [HarmonyPatch(typeof(MenuSelector), "set_selectedIdx")]
    public class MenuSelector_SetSelectedIdx_Collectibles_Patch
    {
        static void Postfix(MenuSelector __instance, int value)
        {
            try
            {
                // Only process if we're in the collectibles menu
                if (!CollectiblesAccessibility.IsCollectiblesMenuActive())
                    return;

                if (!__instance.is_enabled || __instance.menuButtons == null)
                    return;

                if (value < 0 || value >= __instance.menuButtons.Count)
                    return;

                var selectedButton = __instance.menuButtons[value];
                if (selectedButton == null || !selectedButton.isEnabled)
                    return;

                // Check if this button has a collectible item component
                var collectibleItem = selectedButton.GetComponent<ExtrasCollectiblesItem>();
                if (collectibleItem != null)
                {
                    CollectiblesAccessibility.AnnounceCollectibleItem(selectedButton);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in MenuSelector collectibles patch: {ex.Message}");
            }
        }
    }
}
