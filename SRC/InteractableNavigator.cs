using System;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine.EventSystems;
using Il2CppChickenPolice;
using Il2CppChickenPolice.Data;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Manages navigation through interactive objects in the game world
    /// </summary>
    public class InteractableNavigator
    {
        private static GameView currentGameView;
        private static int currentIndex = -1;
        private static InteractableLabel lastSelectedLabel;

        /// <summary>
        /// Updates the current GameView reference
        /// </summary>
        public static void UpdateGameView(GameView gameView)
        {
            if (currentGameView != gameView)
            {
                currentGameView = gameView;
                currentIndex = -1;
                lastSelectedLabel = null;
            }
        }

/// <summary>
/// Gets the list of interactable labels from current GameView
/// </summary>
private static List<InteractableLabel> GetInteractables()
{
    MelonLogger.Msg("GetInteractables called");
    
    if (currentGameView == null)
    {
        MelonLogger.Msg("currentGameView is null");
        return null;
    }
    
    MelonLogger.Msg($"currentGameView exists: {currentGameView.GetType().Name}");
    
    if (currentGameView.interactableLabels == null)
    {
        MelonLogger.Msg("interactableLabels is null");
        return null;
    }
    
    MelonLogger.Msg($"interactableLabels count: {currentGameView.interactableLabels.Count}");
    return currentGameView.interactableLabels;
}

/// <summary>
/// Cycles to the next interactable object
/// </summary>
public static void NextInteractable()
{
    MelonLogger.Msg("NextInteractable called");
    
    var labels = GetInteractables();
    if (labels == null || labels.Count == 0)
    {
        AccessibilityMod.Speak("No interactive objects available", true);
        return;
    }

    // Move to next
    currentIndex++;
    if (currentIndex >= labels.Count)
    {
        currentIndex = 0; // Wrap around
    }

    MelonLogger.Msg($"Moving to index {currentIndex}");
    SelectInteractable(currentIndex);
}

        /// <summary>
        /// Cycles to the previous interactable object
        /// </summary>
        public static void PreviousInteractable()
        {
            var labels = GetInteractables();
            if (labels == null || labels.Count == 0)
            {
                AccessibilityMod.Speak("No interactive objects available", true);
                return;
            }

            // Move to previous
            currentIndex--;
            if (currentIndex < 0)
            {
                currentIndex = labels.Count - 1; // Wrap around
            }

            SelectInteractable(currentIndex);
        }

/// <summary>
/// Selects an interactable by index
/// </summary>
private static void SelectInteractable(int index)
{
    var labels = GetInteractables();
    if (labels == null || index < 0 || index >= labels.Count)
        return;

    var label = labels[index];
    if (label == null)
        return;

    // Deselect previous
    if (lastSelectedLabel != null && lastSelectedLabel != label)
    {
        lastSelectedLabel.selected = false;
    }

    // Select current
    label.selected = true;
    lastSelectedLabel = label;
    
    // Update the game's focus to match our selection
    if (currentGameView != null)
    {
        currentGameView.selectedItem = label;
        
        // Move the virtual cursor to this button's position
        if (currentGameView.virtualCursor != null && label.transform != null)
        {
            try
            {
                var virtualCursor = currentGameView.virtualCursor;
                
                // Get the label's RectTransform position (UI coordinates)
                var rectTransform = label.GetComponent<RectTransform>();
                if (rectTransform != null)
                {
                    // Get the screen position of the label
                    Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, rectTransform.position);
                    
                    MelonLogger.Msg($"Label screen position: {screenPos}");
                    MelonLogger.Msg($"VirtualCursor current pos: {virtualCursor.pos}");
                    
                    // Set the virtual cursor position
                    virtualCursor.SetPos(screenPos);
                    
                    MelonLogger.Msg($"VirtualCursor new pos: {virtualCursor.pos}");
                }
                else
                {
                    MelonLogger.Warning("Label has no RectTransform");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error moving virtual cursor: {ex.Message}");
            }
        }
        
        MelonLogger.Msg($"Updated selectedItem to: {GetLabelText(label)}");
    }

    // Announce the object name
    string objectName = GetLabelText(label);
    if (!string.IsNullOrEmpty(objectName))
    {
        string announcement = $"{objectName}, {index + 1} of {labels.Count}";
        AccessibilityMod.Speak(announcement, true);
        MelonLogger.Msg($"Selected: {announcement}");
    }
}

        /// <summary>
        /// Announces all available interactive objects
        /// </summary>
        public static void AnnounceAllInteractables()
        {
            var labels = GetInteractables();
            if (labels == null || labels.Count == 0)
            {
                AccessibilityMod.Speak("No interactive objects available", true);
                return;
            }

            string announcement = $"{labels.Count} interactive objects: ";
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (label != null)
                {
                    string name = GetLabelText(label);
                    if (!string.IsNullOrEmpty(name))
                    {
                        announcement += name;
                        if (i < labels.Count - 1)
                        {
                            announcement += ", ";
                        }
                    }
                }
            }

            AccessibilityMod.Speak(announcement, true);
        }

        /// <summary>
/// Interacts with the currently selected object
/// </summary>
public static void InteractWithCurrent()
{
    if (lastSelectedLabel == null)
    {
        AccessibilityMod.Speak("No object selected", true);
        return;
    }

    if (lastSelectedLabel.buttonRef != null && currentGameView != null)
    {
        try
        {
            string objectName = GetLabelText(lastSelectedLabel);

            // Log detailed information about the interaction
            MelonLogger.Msg("=== INTERACTION DEBUG INFO ===");
            MelonLogger.Msg($"Interacting with: {objectName}");
            MelonLogger.Msg($"Button name: {lastSelectedLabel.buttonRef.name}");
            MelonLogger.Msg($"Button GameObject: {lastSelectedLabel.buttonRef.gameObject.name}");
            if (lastSelectedLabel.buttonRef.transform.parent != null)
            {
                MelonLogger.Msg($"Parent GameObject: {lastSelectedLabel.buttonRef.transform.parent.gameObject.name}");
            }

            // Try to read item description (for items without voiceover)
            TryReadItemDescription(lastSelectedLabel.buttonRef);

            // Use the game's ExecuteButton method
            currentGameView.ExecuteButton(lastSelectedLabel.buttonRef);

            // Skip "Interacting with" announcement when subtitle mode is on,
            // since the description text will be read by the PlayDescription patch
            if (!AccessibilityMod.SubtitleModeEnabled)
                AccessibilityMod.Speak($"Interacting with {objectName}", true);
            MelonLogger.Msg($"Executed button: {objectName}");
            MelonLogger.Msg("==============================");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Error interacting with object: {ex.Message}");
            AccessibilityMod.Speak("Cannot interact with this object", true);
        }
    }
    else
    {
        AccessibilityMod.Speak("No object selected", true);
    }
}

/// <summary>
/// Tries to read the description of an interactable item (for items without voiceover)
/// </summary>
private static void TryReadItemDescription(GameViewButton button)
{
    try
    {
        if (button == null)
            return;

        MelonLogger.Msg($"Checking for description on button type: {button.buttonType}, icon type: {button.type}");

        // Skip CHARACTER types with LOOK or SPEAK - they have voiceovers
        if (button.buttonType == GameViewButton.ButtonType.CHARACTER &&
            (button.type == GameViewButton.IconType.LOOK || button.type == GameViewButton.IconType.SPEAK))
        {
            MelonLogger.Msg("Skipping CHARACTER with voiceover");
            return;
        }

        string description = null;

        // Try to get description based on button type
        if (button.buttonType == GameViewButton.ButtonType.INVENTORY_ITEM)
        {
            MelonLogger.Msg("Processing INVENTORY_ITEM");
            var locationItem = button.locationItem;

            // Log detailed item information for debugging
            string itemId = locationItem.id ?? "null";
            MelonLogger.Msg($"[Item Description] Item ID: {itemId}");

            // Check if this item has a custom description in our library
            if (ItemDescriptions.HasDescription(itemId))
            {
                MelonLogger.Msg($"[Item Description] Reading custom description for item: {itemId}");
                System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                {
                    ItemDescriptions.TryAnnounceDescription(itemId);
                });
                return; // Custom description will be read
            }

            // Check if there's a voiceover - if so, skip (game will handle it)
            if (locationItem.voiceover != null && locationItem.voiceover.Count > 0)
            {
                MelonLogger.Msg($"[Item Description] Item has voiceover, skipping TTS description for: {itemId}");
                return;
            }

            // Log when an item doesn't have a custom description or voiceover
            MelonLogger.Msg($"[Item Description] No custom description for item: {itemId}");

            // No voiceover, read the description
            if (locationItem.localized_description != null &&
                locationItem.localized_description.default_value != null)
            {
                description = locationItem.localized_description.default_value.value;
                MelonLogger.Msg($"Found INVENTORY_ITEM description: {description}");
            }
        }
        else if (button.buttonType == GameViewButton.ButtonType.OBJECT)
        {
            MelonLogger.Msg("Processing OBJECT type button");
            var locationObject = button.locationObject;

            string objectId = locationObject.id;
            string objectName = locationObject.name;

            MelonLogger.Msg($"[Object Description] Object ID: {objectId}, Name: {objectName}");

            // Check if this object has a custom description
            if (ItemDescriptions.HasObjectDescription(objectId))
            {
                MelonLogger.Msg($"[Object Description] Reading custom description for object: {objectId}");

                // Wait a moment, then read the description
                System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                {
                    ItemDescriptions.TryAnnounceObjectDescription(objectId);
                });
                return; // Custom description will be read
            }
            else
            {
                // Log when an object is interacted with that doesn't have a custom description
                // This helps identify objects that might need descriptions added
                MelonLogger.Msg($"[Object Description] No custom description for object: {objectId} (Name: {objectName})");
            }
        }

        // Announce the description if we found one
        if (!string.IsNullOrEmpty(description))
        {
            AccessibilityMod.Speak(description, true);
            MelonLogger.Msg("Announced item description");
        }
    }
    catch (Exception ex)
    {
        MelonLogger.Error($"Error reading item description: {ex.Message}");
        MelonLogger.Error($"Stack trace: {ex.StackTrace}");
    }
}

        /// <summary>
        /// Gets the text from an InteractableLabel
        /// </summary>
        private static string GetLabelText(InteractableLabel label)
        {
            if (label == null)
                return null;

            if (label.text != null && !string.IsNullOrEmpty(label.text.text))
            {
                return label.text.text;
            }

            return "Unknown object";
        }

        /// <summary>
        /// Resets the navigator
        /// </summary>
        public static void Reset()
        {
            currentIndex = -1;
            lastSelectedLabel = null;
        }
    }

/// <summary>
/// Patches into an existing game Update to handle our input
/// </summary>
[HarmonyPatch(typeof(GameView), "Update")]
public class GameView_Update_Patch
{
    private static bool lastLeftTrigger = false;
    private static bool lastRightTrigger = false;

    /// <summary>
    /// Checks if the notebook is currently open and active.
    /// </summary>
    private static bool IsNotebookOpen()
    {
        try
        {
            var notebooks = UnityEngine.Object.FindObjectsOfType<Notebook>();
            if (notebooks != null && notebooks.Count > 0)
            {
                foreach (var notebook in notebooks)
                {
                    if (notebook != null && notebook.gameObject.activeInHierarchy && notebook.enabled)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Error checking notebook status: {ex.Message}");
        }
        return false;
    }

    static void Postfix(GameView __instance)
    {
        try
        {
            // Check if notebook is open - if so, don't process trigger inputs
            // The notebook uses triggers for category navigation
            bool notebookIsOpen = IsNotebookOpen();

            // ] - Next interactable
            if (UnityEngine.Input.GetKeyDown(KeyCode.RightBracket))
            {
                MelonLogger.Msg("Right bracket detected");
                InteractableNavigator.NextInteractable();
            }

            // [ - Previous interactable
            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket))
            {
                MelonLogger.Msg("Left bracket detected");
                InteractableNavigator.PreviousInteractable();
            }

            // Controller: Right Trigger (RT) - Next interactable
            // Skip if notebook is open (notebook uses triggers for category navigation)
            if (!notebookIsOpen)
            {
                // Using the game's InputManager which tracks trigger state
                bool rightTrigger = InputManager.RTriggered;
                if (!lastRightTrigger && rightTrigger)
                {
                    MelonLogger.Msg("Right trigger detected");
                    InteractableNavigator.NextInteractable();
                }
                lastRightTrigger = rightTrigger;

                // Controller: Left Trigger (LT) - Previous interactable
                bool leftTrigger = InputManager.LTriggered;
                if (!lastLeftTrigger && leftTrigger)
                {
                    MelonLogger.Msg("Left trigger detected");
                    InteractableNavigator.PreviousInteractable();
                }
                lastLeftTrigger = leftTrigger;
            }

            // . (period) - Interact with selected
            if (UnityEngine.Input.GetKeyDown(KeyCode.Period))
            {
                MelonLogger.Msg("Period detected");
                InteractableNavigator.InteractWithCurrent();
            }

            // \ key - List all interactables
            if (UnityEngine.Input.GetKeyDown(KeyCode.Backslash))
            {
                MelonLogger.Msg("Backslash detected");
                InteractableNavigator.AnnounceAllInteractables();
            }

            // Backspace - Go back (for sublists like newspaper articles)
            if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace))
            {
                if (__instance != null)
                {
                    MelonLogger.Msg("Backspace detected - calling GameView.OnBack()");
                    __instance.OnBack();
                    AccessibilityMod.Speak("Back", true);
                }
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Error in input handler: {ex.Message}");
        }
    }
}

    /// <summary>
    /// Patches GameView to track the current game view instance
    /// </summary>
    [HarmonyPatch(typeof(GameView), "OnEnable")]
    public class GameView_OnEnable_Patch
    {
        static void Postfix(GameView __instance)
        {
            try
            {
                InteractableNavigator.UpdateGameView(__instance);
                MelonLogger.Msg("GameView updated");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in GameView_OnEnable patch: {ex.Message}");
            }
        }
    }

    /// <summary>
/// Announces when hovering over objects naturally (optional)
/// </summary>
[HarmonyPatch(typeof(InteractableLabel), "set_selected")]
public class InteractableLabel_SetSelected_Patch
{
    private static InteractableLabel lastAnnouncedLabel = null;

    static void Postfix(InteractableLabel __instance, bool value)
    {
        try
        {
            // Only announce when selection changes to true and it's a different label
            if (value && __instance != lastAnnouncedLabel && 
                __instance.text != null && !string.IsNullOrEmpty(__instance.text.text))
            {
                // Only announce if not using our navigation system
                if (!IsNavigating())
                {
                    AccessibilityMod.Speak(__instance.text.text, true);
                    lastAnnouncedLabel = __instance;
                }
            }
            else if (!value && __instance == lastAnnouncedLabel)
            {
                // Clear last announced when deselected
                lastAnnouncedLabel = null;
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Error in InteractableLabel patch: {ex.Message}");
        }
    }

    private static bool IsNavigating()
    {
        // Check if navigation keys were recently pressed
        return UnityEngine.Input.GetKey(KeyCode.LeftBracket) || 
               UnityEngine.Input.GetKey(KeyCode.RightBracket);
    }
}
}