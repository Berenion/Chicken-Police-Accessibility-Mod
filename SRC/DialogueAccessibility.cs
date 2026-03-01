using System;
using Il2CppSystem.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Patches for announcing dialogue options to screen readers
    /// </summary>
    
    /// <summary>
    /// Patch to announce when user navigates through dialogue options
    /// </summary>
    [HarmonyPatch(typeof(QuestioningChoiceSelect), "set_selectedIdx")]
    public class QuestioningChoiceSelect_SetSelectedIdx_Patch
    {
        static void Postfix(QuestioningChoiceSelect __instance, int value)
        {
            try
            {
                if (__instance.choices == null || value < 0 || value >= __instance.choices.Count)
                    return;

                var selectedChoice = __instance.choices[value];
                if (selectedChoice != null && selectedChoice.ChoiceText != null)
                {
                    // LocalizedText.text is a string property
                    string choiceText = selectedChoice.ChoiceText.text;
                    
                    if (!string.IsNullOrEmpty(choiceText))
                    {
                        string announcement = $"{choiceText}, option {value + 1} of {__instance.choices.Count}";
                        AccessibilityMod.Speak(announcement, true);
                        MelonLogger.Msg($"Selected dialogue option: {announcement}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in QuestioningChoiceSelect_SetSelectedIdx patch: {ex.Message}");
            }
        }
    }
}