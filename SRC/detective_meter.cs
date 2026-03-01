using HarmonyLib;
using Il2Cpp;

namespace ChickenPoliceAccessibility.Patches
{
    /// <summary>
    /// Patches DetectiveMeterGauge to announce changes to the detective meter gauge
    /// </summary>
    [HarmonyPatch(typeof(DetectiveMeterGauge))]
    public class DetectiveMeterGaugePatch
    {
        private static float lastAnnouncedValue = -1f;

        [HarmonyPatch(nameof(DetectiveMeterGauge.UpdateValue))]
        [HarmonyPostfix]
        public static void AnnounceGaugeUpdate(DetectiveMeterGauge __instance)
        {
            try
            {
                float currentValue = __instance.value;
                
                // Only announce if value changed significantly (avoid spam)
                if (System.Math.Abs(currentValue - lastAnnouncedValue) < 0.05f)
                {
                    return;
                }

                lastAnnouncedValue = currentValue;

                // Convert to percentage
                int percentage = (int)(currentValue * 100);
                
                string announcement = $"Detective meter: {percentage} percent";
                AccessibilityMod.Speak(announcement, interrupt: false);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in DetectiveMeterGauge.UpdateValue patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patches DetectiveRank to announce rank changes and provide rank info
    /// </summary>
    [HarmonyPatch(typeof(DetectiveRank))]
    public class DetectiveRankPatch
    {
        private static int lastAnnouncedRank = -1;

        [HarmonyPatch(nameof(DetectiveRank.UpdateValue))]
        [HarmonyPostfix]
        public static void AnnounceRankUpdate(DetectiveRank __instance)
        {
            try
            {
                int currentRank = __instance.value;
                
                // Only announce if rank actually changed
                if (currentRank == lastAnnouncedRank)
                {
                    return;
                }

                lastAnnouncedRank = currentRank;

                // Get rank name from stampText if available
                string rankName = null;
                if (__instance.stampText != null)
                {
                    rankName = __instance.stampText.text;
                }

                string announcement;
                if (!string.IsNullOrEmpty(rankName))
                {
                    announcement = $"Detective rank: {rankName}";
                }
                else
                {
                    announcement = $"Detective rank: {currentRank}";
                }

                AccessibilityMod.Speak(announcement, interrupt: true);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in DetectiveRank.UpdateValue patch: {ex.Message}");
            }
        }
    }
}