// StatsQuestioningCardPatches.cs
// Reads the questioning end screen directly from UI elements

using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;

namespace ChickenPoliceAccessibility
{
    // Hook when the "Ending" GameObject becomes active
    [HarmonyPatch(typeof(GameObject), "SetActive")]
    public class GameObject_SetActive_Ending_Patch
    {
        private static bool hasAnnounced = false;

        static void Postfix(GameObject __instance, bool value)
        {
            try
            {
                if (!value || __instance == null) return;
                
                // Check if this is the "Ending" GameObject
                if (__instance.name == "Ending")
                {
                    // Check if it has the QuestioningAnimEventHandler component
                    var handler = __instance.GetComponent<QuestioningAnimEventHandler>();
                    if (handler != null)
                    {
                        MelonLogger.Msg("Ending screen activated");
                        hasAnnounced = false; // Reset for new questioning session
                        MelonCoroutines.Start(AnnounceEndingScreen(__instance));
                    }
                }
            }
            catch
            {
                // Silently ignore
            }
        }

        private static System.Collections.IEnumerator AnnounceEndingScreen(GameObject ending)
        {
            // Wait for animation and UI to populate
            yield return new WaitForSeconds(1.5f);
            
            try
            {
                if (hasAnnounced)
                {
                    MelonLogger.Msg("Already announced this session");
                    yield break;
                }

                hasAnnounced = true;
                
                string announcement = BuildEndingAnnouncement(ending);
                if (!string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg($"Announcing: {announcement}");
                    AccessibilityMod.Speak(announcement, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing ending screen: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static string BuildEndingAnnouncement(GameObject ending)
        {
            var parts = new System.Collections.Generic.List<string>();

            try
            {
                parts.Add("Questioning complete");

                // Debug: List all children of the ending GameObject
                MelonLogger.Msg($"Ending GameObject has {ending.transform.childCount} children:");
                for (int i = 0; i < ending.transform.childCount; i++)
                {
                    var child = ending.transform.GetChild(i);
                    MelonLogger.Msg($"  Child {i}: {child.name}");
                }

                // Get the QuestioningAnimEventHandler component to access detectiveRank
                var handler = ending.GetComponent<QuestioningAnimEventHandler>();
                if (handler == null)
                {
                    MelonLogger.Warning("Could not find QuestioningAnimEventHandler");
                    return null;
                }

                // Get the detective rank component for star count
                var detectiveRank = handler.detectiveRank;
                if (detectiveRank != null)
                {
                    // DetectiveRank.value is ALSO inverted: 1 = 5 stars, 2 = 4 stars, etc.
                    int rawValue = detectiveRank.value;
                    int starCount = 6 - rawValue; // Invert to get actual star count
                    int totalStars = 5; // The game always has 5 stars

                    // Try to get rank name from detectiveRank.stampText
                    string rankName = null;
                    if (detectiveRank.stampText != null && !string.IsNullOrEmpty(detectiveRank.stampText.text))
                    {
                        rankName = detectiveRank.stampText.text.Trim();
                        // Filter out placeholders or invalid text
                        if (rankName.StartsWith("_") || rankName.StartsWith("#"))
                        {
                            rankName = null;
                        }
                    }

                    if (rankName != null)
                    {
                        parts.Add(rankName);
                    }

                    string starText = starCount == 1 ? "star" : "stars";
                    parts.Add($"{starCount} out of {totalStars} {starText}");
                }

                // Find the Content > Stats GameObject
                var content = ending.transform.Find("Content");
                if (content != null)
                {

                    var stats = content.Find("Stats");
                    if (stats != null)
                    {
                        // Check for stamp (completion indicator)
                        var stampObj = stats.Find("StampImage");
                        if (stampObj != null && stampObj.gameObject.activeSelf)
                        {
                            parts.Add("Completed");
                            MelonLogger.Msg("Has completion stamp");
                        }

                        // Get StatsList items (questions asked, etc.)
                        var statsList1 = stats.Find("StatsList");
                        if (statsList1 != null)
                        {
                            ReadStatsList(statsList1, parts);
                        }

                        var statsList2 = stats.Find("StatsList (1)");
                        if (statsList2 != null)
                        {
                            ReadStatsList(statsList2, parts);
                        }
                    }
                }

                return parts.Count > 1 ? string.Join(". ", parts) : null;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error building announcement: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private static void ReadStatsList(Transform statsList, System.Collections.Generic.List<string> parts)
        {
            try
            {
                // Look through all children for Text components
                var texts = statsList.GetComponentsInChildren<Text>();
                foreach (var txt in texts)
                {
                    if (txt != null && !string.IsNullOrEmpty(txt.text))
                    {
                        // Filter out obvious labels and keep actual values
                        string text = txt.text.Trim();
                        if (!string.IsNullOrEmpty(text) &&
                            !text.StartsWith("_") &&
                            text.Length > 0)
                        {
                            parts.Add(text);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error reading stats list: {ex.Message}");
            }
        }
    }

}