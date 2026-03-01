using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using Il2CppChickenPolice.Data;
using UnityEngine;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the in-game inventory interface.
    /// Provides screen reader announcements for navigation between inventory items.
    /// </summary>
    public static class InventoryAccessibility
    {
        private static int lastAnnouncedIdx = -1;

        /// <summary>
        /// Announces the currently selected inventory item.
        /// </summary>
        private static void AnnounceInventoryItem(InventoryView inventoryView)
        {
            if (inventoryView == null) return;

            try
            {
                int selectedIdx = inventoryView.selectedIdx;

                // Only announce if the selection changed
                if (selectedIdx == lastAnnouncedIdx)
                    return;

                lastAnnouncedIdx = selectedIdx;

                // Get the currently selected item
                if (inventoryView.activeItems != null && 
                    selectedIdx >= 0 && 
                    selectedIdx < inventoryView.activeItems.Count)
                {
                    var selectedItem = inventoryView.activeItems[selectedIdx];
                    if (selectedItem != null && !string.IsNullOrEmpty(selectedItem.item))
                    {
                        // Announce item name with position
                        int itemNumber = selectedIdx + 1;
                        int totalItems = inventoryView.activeItems.Count;
                        string announcement = $"{selectedItem.item}, item {itemNumber} of {totalItems}";
                        
                        AccessibilityMod.Speak(announcement, true);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing inventory item: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets tracking when inventory is closed.
        /// </summary>
        private static void ResetTracking()
        {
            lastAnnouncedIdx = -1;
        }

        #region Harmony Patches

        /// <summary>
        /// Patch for when inventory is enabled/opened.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnEnable")]
        public class InventoryView_OnEnable_Patch
        {
            static void Postfix(InventoryView __instance)
            {
                try
                {
                    ResetTracking();
                    AccessibilityMod.Speak("Inventory opened", true);
                    
                    // Announce initial selection after a brief delay to let UI settle
                    System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
                    {
                        AnnounceInventoryItem(__instance);
                    });
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.OnEnable patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for when inventory is disabled/closed.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnDisable")]
        public class InventoryView_OnDisable_Patch
        {
            static void Prefix()
            {
                try
                {
                    AccessibilityMod.Speak("Inventory closed", true);
                    ResetTracking();
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.OnDisable patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for the selectedIdx setter to announce selection changes.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "set_selectedIdx")]
        public class InventoryView_SetSelectedIdx_Patch
        {
            static void Postfix(InventoryView __instance)
            {
                try
                {
                    AnnounceInventoryItem(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.set_selectedIdx patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for left navigation key.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnKeyLeft")]
        public class InventoryView_OnKeyLeft_Patch
        {
            static void Postfix(InventoryView __instance)
            {
                try
                {
                    AnnounceInventoryItem(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.OnKeyLeft patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for right navigation key.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnKeyRight")]
        public class InventoryView_OnKeyRight_Patch
        {
            static void Postfix(InventoryView __instance)
            {
                try
                {
                    AnnounceInventoryItem(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.OnKeyRight patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for L1 button (left shoulder).
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnButtonL1")]
        public class InventoryView_OnButtonL1_Patch
        {
            static void Postfix(InventoryView __instance)
            {
                try
                {
                    AnnounceInventoryItem(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.OnButtonL1 patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for R1 button (right shoulder).
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnButtonR1")]
        public class InventoryView_OnButtonR1_Patch
        {
            static void Postfix(InventoryView __instance)
            {
                try
                {
                    AnnounceInventoryItem(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.OnButtonR1 patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for when an item is explicitly set as selected.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "SetSelectedItem")]
        public class InventoryView_SetSelectedItem_Patch
        {
            static void Postfix(InventoryView __instance)
            {
                try
                {
                    AnnounceInventoryItem(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.SetSelectedItem patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for escape key to announce closing.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnKeyEsc")]
        public class InventoryView_OnKeyEsc_Patch
        {
            static void Prefix()
            {
                try
                {
                    AccessibilityMod.Speak("Closing inventory", true);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in InventoryView.OnKeyEsc patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for Update to handle Enter key for item interaction.
        /// </summary>
        [HarmonyPatch(typeof(InventoryView), "OnInputModeChange")]
        public class InventoryView_Update_Patch
        {
            private static bool lastEnterState = false;

            /// <summary>
            /// Called from AccessibilityMod.OnUpdate to handle keyboard input for inventory.
            /// </summary>
            public static void HandleInput()
            {
                try
                {
                    var inventoryView = InventoryView.Instance;
                    if (inventoryView == null || !inventoryView.gameObject.activeInHierarchy)
                        return;

                    if (!inventoryView.inputEnabled || !inventoryView.clickEnabled)
                        return;

                    // Check for Enter key press (edge detection)
                    bool enterPressed = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
                    if (enterPressed && !lastEnterState)
                    {
                        ExecuteSelectedItem(inventoryView);
                    }
                    lastEnterState = enterPressed;
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in inventory input handler: {ex.Message}");
                }
            }

            /// <summary>
            /// Executes the currently selected inventory item.
            /// </summary>
            private static void ExecuteSelectedItem(InventoryView inventoryView)
            {
                try
                {
                    int selectedIdx = inventoryView.selectedIdx;
                    if (inventoryView.activeItems != null &&
                        selectedIdx >= 0 &&
                        selectedIdx < inventoryView.activeItems.Count)
                    {
                        var selectedItem = inventoryView.activeItems[selectedIdx];
                        if (selectedItem != null)
                        {
                            MelonLogger.Msg($"Executing inventory item: {selectedItem.item}");
                            selectedItem.Execute();
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error executing inventory item: {ex.Message}");
                }
            }
        }

        #endregion
    }
}