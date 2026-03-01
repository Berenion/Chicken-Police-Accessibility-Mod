using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppChickenPolice;
using Il2CppChickenPolice.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace ChickenPoliceAccessibility
{
    // Patch GameManager.PlayDescription to capture object/inventory/look descriptions
    [HarmonyPatch(typeof(Il2CppChickenPolice.GameManager), "PlayDescription")]
    public class GameManager_PlayDescription_Patch
    {
        static void Postfix(string key, GameData.LocalizedValue value)
        {
            if (!AccessibilityMod.SubtitleModeEnabled)
                return;

            try
            {
                if (value == null)
                    return;

                string text = SubtitleAccessibility.GetLocalizedText(value);

                if (!string.IsNullOrEmpty(text))
                {
                    MelonLogger.Msg($"[Subtitle] Description: {text}");
                    AccessibilityMod.SpeakDirect(text, false);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in PlayDescription subtitle patch: {ex.Message}");
            }
        }
    }

    public static class SubtitleAccessibility
    {
        // Track announced text to avoid duplicates
        private static string lastAnnouncedText = "";
        private static int lastDialogBaseInstanceId = 0;

        // Cache panels
        private static DialogPanel cachedDialogPanel;
        private static NarrationPanel cachedNarrationPanel;

        // Cutscene subtitle tracking
        private static Cutscene cachedCutscene;
        private static string lastCutsceneSubtitle = "";

        /// <summary>
        /// Extracts localized text from a LocalizedValue, matching the current game language.
        /// </summary>
        public static string GetLocalizedText(GameData.LocalizedValue localizedValue)
        {
            try
            {
                // Get the current language from the game's localization system
                var selectedLanguage = Localization._selectedLanguage;
                string currentLangId = selectedLanguage?.id;
                string currentLangName = selectedLanguage?.name;

                // Search translations for the current language
                // Translation language fields use '#' prefix (e.g. '#41:2') while
                // the selected language id does not (e.g. '41:2'), so match both forms
                var translations = localizedValue.translations;
                if (translations != null)
                {
                    string langIdWithHash = "#" + currentLangId;
                    for (int i = 0; i < translations.Length; i++)
                    {
                        var translation = translations[i];
                        if (translation == null) continue;

                        string tLang = translation.language;
                        if (tLang == currentLangId || tLang == langIdWithHash || tLang == currentLangName)
                        {
                            string translatedText = translation.value;
                            if (!string.IsNullOrEmpty(translatedText))
                                return translatedText;
                        }
                    }
                }

                // Fallback: try Localization.Get()
                string locText = Localization.Get(localizedValue);
                if (!string.IsNullOrEmpty(locText))
                    return locText;

                // Last resort: default_value
                var defaultVal = localizedValue.default_value;
                if (defaultVal != null && !string.IsNullOrEmpty(defaultVal.value))
                    return defaultVal.value;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error getting localized text: {ex.Message}");
            }

            return null;
        }

        public static void HandleInput()
        {
            if (!AccessibilityMod.SubtitleModeEnabled)
                return;

            try
            {
                // Monitor DialogPanel (conversations)
                if (cachedDialogPanel == null || !cachedDialogPanel.gameObject.activeInHierarchy)
                {
                    cachedDialogPanel = Object.FindObjectOfType<DialogPanel>();
                }

                if (cachedDialogPanel != null && cachedDialogPanel.gameObject.activeInHierarchy)
                {
                    var dialogBase = cachedDialogPanel.dialog;
                    if (dialogBase != null && dialogBase.is_text_playing)
                    {
                        string speaker = GetSpeakerName();
                        CheckDialogBase(dialogBase, speaker);
                    }
                }

                // Monitor NarrationPanel (location narrations)
                if (cachedNarrationPanel == null || !cachedNarrationPanel.gameObject.activeInHierarchy)
                {
                    cachedNarrationPanel = Object.FindObjectOfType<NarrationPanel>();
                }

                if (cachedNarrationPanel != null && cachedNarrationPanel.gameObject.activeInHierarchy)
                {
                    var dialogBase = cachedNarrationPanel.dialog;
                    if (dialogBase != null && dialogBase.is_text_playing)
                    {
                        CheckDialogBase(dialogBase, null);
                    }
                }

                // Monitor Cutscene subtitles (video cutscenes with SRT subtitles)
                if (cachedCutscene == null || !cachedCutscene.gameObject.activeInHierarchy)
                {
                    cachedCutscene = Object.FindObjectOfType<Cutscene>();
                    if (cachedCutscene != null)
                        lastCutsceneSubtitle = "";
                }

                if (cachedCutscene != null && cachedCutscene.gameObject.activeInHierarchy)
                {
                    string currentSub = cachedCutscene.actualSubStr;
                    if (!string.IsNullOrEmpty(currentSub) && currentSub != lastCutsceneSubtitle)
                    {
                        lastCutsceneSubtitle = currentSub;
                        MelonLogger.Msg($"[Subtitle] Cutscene: {currentSub}");
                        AccessibilityMod.SpeakDirect(currentSub, false);
                    }
                    else if (string.IsNullOrEmpty(currentSub))
                    {
                        lastCutsceneSubtitle = "";
                    }
                }
                else
                {
                    cachedCutscene = null;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in subtitle polling: {ex.Message}");
            }
        }

        private static void CheckDialogBase(DialogBase dialogBase, string speakerName)
        {
            // Try linesMerged first, fall back to currentLine
            string fullText = dialogBase.linesMerged;
            if (string.IsNullOrEmpty(fullText))
                fullText = dialogBase.currentLine;

            if (string.IsNullOrEmpty(fullText))
                return;

            // Detect new text: different content OR different DialogBase instance
            int instanceId = dialogBase.GetInstanceID();
            if (fullText == lastAnnouncedText && instanceId == lastDialogBaseInstanceId)
                return;

            lastAnnouncedText = fullText;
            lastDialogBaseInstanceId = instanceId;

            string announcement = string.IsNullOrEmpty(speakerName) ? fullText : $"{speakerName}: {fullText}";

            MelonLogger.Msg($"[Subtitle] {announcement}");
            AccessibilityMod.SpeakDirect(announcement, false);
        }

        private static string GetSpeakerName()
        {
            try
            {
                if (cachedDialogPanel == null)
                    return null;

                string leftName = GetNameFromLabel(cachedDialogPanel.labelLeft);
                string rightName = GetNameFromLabel(cachedDialogPanel.labelRight);

                if (!string.IsNullOrEmpty(leftName) && !string.IsNullOrEmpty(rightName))
                {
                    bool leftActive = cachedDialogPanel.labelLeft != null && cachedDialogPanel.labelLeft.gameObject.activeInHierarchy;
                    bool rightActive = cachedDialogPanel.labelRight != null && cachedDialogPanel.labelRight.gameObject.activeInHierarchy;

                    if (leftActive && !rightActive) return leftName;
                    if (rightActive && !leftActive) return rightName;
                    return rightName;
                }

                if (!string.IsNullOrEmpty(leftName)) return leftName;
                if (!string.IsNullOrEmpty(rightName)) return rightName;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error getting speaker name: {ex.Message}");
            }
            return null;
        }

        private static string GetNameFromLabel(NameLabel label)
        {
            if (label == null)
                return null;

            if (!label.gameObject.activeInHierarchy)
                return null;

            var localizedText = label.NameLabelText;
            if (localizedText == null)
                return null;

            string text = localizedText.text;
            if (string.IsNullOrEmpty(text))
                return null;

            return text;
        }
    }
}
