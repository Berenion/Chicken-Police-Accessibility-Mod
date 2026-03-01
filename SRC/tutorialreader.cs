using HarmonyLib;
using Il2Cpp;
using UnityEngine.UI;

namespace ChickenPoliceAccessibility.Patches
{
    /// <summary>
    /// Patches PopupWindow to announce popup messages when they appear
    /// </summary>
    [HarmonyPatch(typeof(PopupWindow))]
    public class PopupWindowPatch
    {
        [HarmonyPatch(nameof(PopupWindow.Show))]
        [HarmonyPostfix]
        public static void AnnouncePopup(PopupWindow __instance, string text)
        {
            try
            {
                if (!string.IsNullOrEmpty(text))
                {
                    AccessibilityMod.Speak($"Popup. {text}", interrupt: true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in PopupWindow.Show patch: {ex.Message}");
            }
        }
    }

    // Tutorial panel patch disabled due to Il2CppReferenceArray access violations
    // The array access causes crashes in Il2Cpp when trying to read the steps field
    /*
    [HarmonyPatch(typeof(BaseTutorialPanel))]
    public class BaseTutorialPanelPatch
    {
        [HarmonyPatch(nameof(BaseTutorialPanel.OnEnable))]
        [HarmonyPostfix]
        public static void AnnounceTutorialOnShow(BaseTutorialPanel __instance)
        {
            try
            {
                AnnounceTutorialStep(__instance);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in BaseTutorialPanel.OnEnable patch: {ex.Message}");
            }
        }

        [HarmonyPatch(nameof(BaseTutorialPanel.OnClickNext))]
        [HarmonyPostfix]
        public static void AnnounceTutorialOnNext(BaseTutorialPanel __instance)
        {
            try
            {
                AnnounceTutorialStep(__instance);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in BaseTutorialPanel.OnClickNext patch: {ex.Message}");
            }
        }

        private static void AnnounceTutorialStep(BaseTutorialPanel panel)
        {
            // Disabled - causes AccessViolationException
        }
    }
    */
}