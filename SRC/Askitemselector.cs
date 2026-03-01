using HarmonyLib;
using Il2Cpp;

namespace ChickenPoliceAccessibility.Patches
{
        /// <summary>
    /// Patches AskItemSelector to announce questions/topics when navigating the Ask Panel
    /// </summary>
    [HarmonyPatch(typeof(AskItemSelector))]
    public class AskItemSelectorPatch
    {
        [HarmonyPatch(nameof(AskItemSelector.selectedIdx), MethodType.Setter)]
        [HarmonyPostfix]
        public static void AnnounceSelectedQuestion(AskItemSelector __instance, int value)
        {
            try
            {
                // Suppress during pie menu label collection
                if (PieMenuAccessibility._isCollectingLabels) return;

                // Suppress when any PieMenu is active - ask items get initialized before the ask panel is open
                var pieMenus = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PieMenu>();
                for (int i = 0; i < pieMenus.Length; i++)
                {
                    if (pieMenus[i].gameObject.activeInHierarchy && !pieMenus[i].askOpened)
                        return;
                }

                // Get the list of available questions/topics
                var choices = __instance.choices;
                
                if (choices == null || choices.Count == 0)
                {
                    return;
                }
                
                // Bounds check
                if (value < 0 || value >= choices.Count)
                {
                    return;
                }
                
                // Get the selected item
                var selectedItem = choices[value];
                if (selectedItem == null)
                {
                    return;
                }
                
                // Get the text component
                var textComponent = selectedItem.text;
                if (textComponent == null)
                {
                    return;
                }
                
                // Get the localized text
                string questionText = textComponent.text;
                if (string.IsNullOrEmpty(questionText))
                {
                    return;
                }
                
                // Check status indicators
                string statusPrefix = "";
                if (selectedItem.isChecked)
                {
                    statusPrefix = "Asked: ";
                }
                else if (selectedItem.isUnread)
                {
                    statusPrefix = "New: ";
                }
                
                // Announce with position info
                string announcement = $"{statusPrefix}{questionText}. {value + 1} of {choices.Count}";
                AccessibilityMod.Speak(announcement, interrupt: true);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in AskItemSelectorPatch: {ex.Message}");
            }
        }
    }
}