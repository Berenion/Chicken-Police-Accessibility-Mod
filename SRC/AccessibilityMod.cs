using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;

[assembly: MelonInfo(typeof(ChickenPoliceAccessibility.AccessibilityMod), "Chicken Police Accessibility", "0.6", "Berenion")]
[assembly: MelonGame("HandyGames", "Chicken Police")]

namespace ChickenPoliceAccessibility
{
    public class AccessibilityMod : MelonMod
    {
        private static Tolk.Tolk tolk;
        private static AudioAwareAnnouncementManager audioManager;

        // Subtitle mode preferences
        private static MelonPreferences_Category subtitleCategory;
        private static MelonPreferences_Entry<bool> subtitleModeEntry;
        public static bool SubtitleModeEnabled => subtitleModeEntry != null && subtitleModeEntry.Value;

        public override void OnInitializeMelon()
{
    MelonLogger.Msg("Initializing Chicken Police Accessibility Mod v0.5");

    tolk = new Tolk.Tolk();
    tolk.TrySAPI(true);
    tolk.Load();

    if (tolk.IsLoaded())
    {
        string screenReader = tolk.DetectScreenReader();
        MelonLogger.Msg($"Screen reader detected: {screenReader ?? "SAPI"}");
        tolk.Speak("Chicken Police accessibility mod loaded", true);
    }
    else
    {
        MelonLogger.Warning("Failed to initialize screen reader support");
    }

    // Initialize audio-aware announcement manager
    audioManager = AudioAwareAnnouncementManager.Instance;
    audioManager.Initialize(tolk);
    MelonLogger.Msg("AudioAwareAnnouncementManager initialized");

    // Initialize subtitle mode preferences
    subtitleCategory = MelonPreferences.CreateCategory("Accessibility");
    subtitleModeEntry = subtitleCategory.CreateEntry("SubtitleMode", false, "Subtitle Mode", "Send dialogue subtitles to screen reader alongside voiceover");
    MelonLogger.Msg($"Subtitle mode: {(SubtitleModeEnabled ? "on" : "off")}");
}    
        
		
		public override void OnDeinitializeMelon()
        {
            if (tolk != null)
            {
                tolk.Unload();
            }
        }

        public override void OnUpdate()
        {
            // Update audio-aware announcement manager
            if (audioManager != null)
            {
                audioManager.Update();
            }

            // F2 toggles subtitle mode
            if (Input.GetKeyDown(KeyCode.F2) && subtitleModeEntry != null)
            {
                subtitleModeEntry.Value = !subtitleModeEntry.Value;
                subtitleCategory.SaveToFile(false);
                Speak(SubtitleModeEnabled ? "Subtitles on" : "Subtitles off", true);
            }

            // Handle notebook keyboard navigation
            NotebookAccessibility.HandleKeyboardInput();

            // Handle jukebox accessibility input
            JukeboxAccessibility.HandleInput();

            // Handle achievements accessibility input
            AchievementsAccessibility.HandleInput();

            // Handle clock puzzle accessibility input
            ClockAccessibility.HandleInput();

            // Handle safe puzzle accessibility input
            SafeAccessibility.HandleInput();

            // Handle clue connector accessibility input
            ClueConnectorAccessibility.HandleInput();

            // Poll for clue connector starter screen voiceover subtitles
            ClueConnectorSubtitleTracker.PollStarterText();

            // Handle zipper minigame accessibility input
            ZipperAccessibility.HandleInput();

            // Handle knot minigame accessibility input
            KnotAccessibility.HandleInput();

            // Handle car chase minigame accessibility input
            CarChaseAccessibility.HandleInput();

            // Handle mural minigame accessibility input
            MuralAccessibility.HandleInput();

            // Handle shooting range accessibility input
            ShootingRangeAccessibility.HandleInput();

            // Handle inventory keyboard input (Enter key to use item)
            InventoryAccessibility.InventoryView_Update_Patch.HandleInput();

            // Handle phone keyboard input (Enter key to pick up phone)
            PhoneAccessibility.HandleInput();

            // Handle subtitle announcements
            SubtitleAccessibility.HandleInput();

            // Handle pie menu open announcement
            Patches.PieMenuAccessibility.HandleInput();
        }

        public static void SpeakDirect(string text, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (tolk == null || !tolk.IsLoaded())
                return;

            tolk.Speak(text, interrupt);
        }

        public static void Speak(string text, bool interrupt = true)
        {
            if (string.IsNullOrEmpty(text))
            {
                MelonLogger.Warning("[TTS] Attempted to speak empty text");
                return;
            }

            if (tolk == null)
            {
                MelonLogger.Error("[TTS] Tolk is null!");
                return;
            }

            if (!tolk.IsLoaded())
            {
                MelonLogger.Error("[TTS] Tolk is not loaded!");
                return;
            }

            // Use audio-aware announcement manager to queue announcements
            // This prevents screen reader from talking over game voiceover
            if (audioManager != null)
            {
                audioManager.QueueAnnouncement(text, interrupt);
            }
            else
            {
                // Fallback to direct speech if manager not initialized
                tolk.Speak(text, interrupt);
            }
        }

        public static string GetTextMeshProText(GameObject obj)
        {
            try
            {
                var components = obj.GetComponentsInChildren<Component>();
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
            catch
            {
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(MenuSelector), "set_selectedIdx")]
    public class MenuSelector_SetSelectedIdx_Patch
    {
        static void Postfix(MenuSelector __instance, int value)
        {
            try
            {
                if (!__instance.is_enabled || __instance.menuButtons == null)
                    return;

                if (value < 0 || value >= __instance.menuButtons.Count)
                    return;

                var selectedButton = __instance.menuButtons[value];
                if (selectedButton == null || !selectedButton.isEnabled)
                    return;

                // Check if this is a save game slot
                var saveSlot = selectedButton.GetComponent<SaveGameSlot>();
                if (saveSlot != null)
                {
                    AnnounceSaveSlot(saveSlot);
                    return;
                }

                string textToSpeak = GetButtonTextAndValue(selectedButton);

                if (!string.IsNullOrEmpty(textToSpeak))
                {
                    // If a prompt just opened, don't interrupt the question text
                    bool interrupt = (Time.time - PromptPopup_Prompt_Patch.lastPromptTime) > 0.5f;
                    AccessibilityMod.Speak(textToSpeak, interrupt);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in MenuSelector patch: {ex.Message}");
            }
        }

        private static string GetButtonTextAndValue(MenuButton button)
        {
            string result = "";

            string label = GetButtonLabel(button);
            if (!string.IsNullOrEmpty(label))
            {
                result = label;
            }

            string value = GetButtonValue(button);
            if (!string.IsNullOrEmpty(value))
            {
                result += " " + value;
            }

            string state = GetControlState(button);
            if (!string.IsNullOrEmpty(state))
            {
                result += " " + state;
            }

            return string.IsNullOrEmpty(result) ? null : result.Trim();
        }

        private static string GetButtonLabel(MenuButton button)
        {
            // Priority 1: LocalizedText component (returns properly localized string)
            var localizedText = button.GetComponentInChildren<LocalizedText>();
            if (localizedText != null && !string.IsNullOrEmpty(localizedText.text))
            {
                return localizedText.text;
            }

            // Priority 2: LocalizedTextMeshPro component
            var localizedTMP = button.GetComponentInChildren<LocalizedTextMeshPro>();
            if (localizedTMP != null && !string.IsNullOrEmpty(localizedTMP.text))
            {
                return localizedTMP.text;
            }

            // Priority 3: Plain Unity Text component
            if (button.text != null && !string.IsNullOrEmpty(button.text.text))
            {
                return button.text.text;
            }

            var textComponent = button.GetComponentInChildren<Text>();
            if (textComponent != null && !string.IsNullOrEmpty(textComponent.text))
            {
                return textComponent.text;
            }

            // Priority 4: TextMeshPro via reflection
            string tmpText = AccessibilityMod.GetTextMeshProText(button.gameObject);
            if (!string.IsNullOrEmpty(tmpText))
            {
                return tmpText;
            }

            return CleanObjectName(button.gameObject.name);
        }

        private static string GetButtonValue(MenuButton button)
        {
            if (!button.hasDynamicContent)
                return null;

            // Priority 1: OptionsSelector (dropdowns like Language, Resolution)
            var optionsSelector = button.GetComponent<OptionsSelector>();
            if (optionsSelector != null && optionsSelector.text != null)
            {
                string selectorValue = optionsSelector.text.text;
                if (!string.IsNullOrEmpty(selectorValue))
                {
                    string label = GetButtonLabel(button);
                    if (selectorValue != label)
                    {
                        return selectorValue;
                    }
                }
            }

            // TODO: Re-enable slider support once type issues are resolved
            /*
            var optionsSlider = button.GetComponent<OptionsSlider>();
            if (optionsSlider != null && optionsSlider.s != null)
            {
                var s = optionsSlider.s;
                return s.wholeNumbers ? s.value.ToString("F0") : s.value.ToString("F1");
            }

            if (button.optionsSlider != null)
            {
                var s = button.optionsSlider;
                return s.wholeNumbers ? s.value.ToString("F0") : s.value.ToString("F1");
            }
            */

            // Priority 2: Additional Unity Text components
            var allTexts = button.GetComponentsInChildren<Text>();
            string labelText = GetButtonLabel(button);
            
            foreach (var txt in allTexts)
            {
                if (txt == button.text) continue;
                if (string.IsNullOrEmpty(txt.text)) continue;
                if (txt.text == labelText) continue;
                
                return txt.text;
            }

            // Priority 3: TextMeshPro components (using reflection)
            string[] tmpTexts = GetAllTextMeshProTexts(button.gameObject);
            foreach (var txt in tmpTexts)
            {
                if (string.IsNullOrEmpty(txt)) continue;
                if (txt == labelText) continue;
                
                return txt;
            }

            return null;
        }

        private static string GetControlState(MenuButton button)
        {
            // Check for OptionsChcekbox (checkboxes/toggles)
            var optionsCheckbox = button.GetComponent<OptionsChcekbox>();
            if (optionsCheckbox != null && optionsCheckbox.toggle != null)
            {
                return optionsCheckbox.toggle.isOn ? "checked" : "unchecked";
            }

            // Fallback: direct Toggle component
            var toggle = button.GetComponent<Toggle>();
            if (toggle != null)
            {
                return toggle.isOn ? "checked" : "unchecked";
            }

            // TODO: Re-enable slider state once type issues are resolved
            /*
            if (button.optionsSlider != null)
            {
                return "slider";
            }
            */

            return null;
        }

        private static string CleanObjectName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Replace("(Clone)", "").Trim();
            name = name.Replace("_", " ");
            return name;
        }

        private static string[] GetAllTextMeshProTexts(GameObject obj)
        {
            var results = new System.Collections.Generic.List<string>();

            try
            {
                var components = obj.GetComponentsInChildren<Component>();
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
                                results.Add(textValue.ToString());
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return results.ToArray();
        }

        /// <summary>
        /// Announces save game slot information including location, time played, and save timestamp.
        /// </summary>
        private static void AnnounceSaveSlot(SaveGameSlot slot)
        {
            try
            {
                string announcement = "";

                // Check if slot has save data
                if (slot.IsFilledSaveSlot())
                {
                    // Get slot title (e.g., "Slot 1" or custom save name)
                    string title = "";
                    if (slot.titleText != null && !string.IsNullOrEmpty(slot.titleText.text))
                    {
                        title = slot.titleText.text;
                    }
                    else
                    {
                        title = $"Slot {slot.slotID}";
                    }
                    announcement = title + ". ";

                    // Get location
                    if (slot.locationText != null && !string.IsNullOrEmpty(slot.locationText.text))
                    {
                        announcement += $"Location: {slot.locationText.text}. ";
                    }

                    // Get elapsed time
                    if (slot.elapsedText != null && !string.IsNullOrEmpty(slot.elapsedText.text))
                    {
                        announcement += $"Play time: {slot.elapsedText.text}. ";
                    }

                    // Get save timestamp
                    if (slot.savedAtText != null && !string.IsNullOrEmpty(slot.savedAtText.text))
                    {
                        announcement += $"Saved at: {slot.savedAtText.text}";
                    }

                    MelonLogger.Msg($"Save slot announcement: {announcement}");
                }
                else
                {
                    // Empty slot
                    string slotName = $"Slot {slot.slotID}";
                    if (slot.titleText != null && !string.IsNullOrEmpty(slot.titleText.text))
                    {
                        slotName = slot.titleText.text;
                    }

                    if (slot.mode == SaveGameSlot.LoadSaveMode.NEW_GAME)
                    {
                        announcement = $"{slotName}, empty, start new game";
                    }
                    else
                    {
                        announcement = $"{slotName}, empty";
                    }

                    MelonLogger.Msg($"Empty slot announcement: {announcement}");
                }

                if (!string.IsNullOrEmpty(announcement))
                {
                    AccessibilityMod.Speak(announcement, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing save slot: {ex}");
                // Fallback: announce slot number
                AccessibilityMod.Speak($"Slot {slot.slotID}", true);
            }
        }
    }

    [HarmonyPatch(typeof(MenuSelector), "ClickOnSelected")]
    public class MenuSelector_ClickOnSelected_Patch
    {
        static void Prefix(MenuSelector __instance)
        {
            try
            {
                if (!__instance.is_enabled || __instance.menuButtons == null)
                    return;

                int idx = __instance.selectedIdx;
                if (idx >= 0 && idx < __instance.menuButtons.Count)
                {
                    var button = __instance.menuButtons[idx];
                    if (button != null && button.isEnabled)
                    {
                        AccessibilityMod.Speak("OK", false);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in ClickOnSelected patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(MenuSelector), "OnBack")]
    public class MenuSelector_OnBack_Patch
    {
        static void Prefix()
        {
            try
            {
                AccessibilityMod.Speak("Back", true);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OnBack patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(OptionsSelector), "Next")]
    public class OptionsSelector_Next_Patch
    {
        static void Postfix(OptionsSelector __instance)
        {
            try
            {
                if (__instance.text != null && !string.IsNullOrEmpty(__instance.text.text))
                {
                    AccessibilityMod.Speak(__instance.text.text, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OptionsSelector.Next patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(OptionsSelector), "Prev")]
    public class OptionsSelector_Prev_Patch
    {
        static void Postfix(OptionsSelector __instance)
        {
            try
            {
                if (__instance.text != null && !string.IsNullOrEmpty(__instance.text.text))
                {
                    AccessibilityMod.Speak(__instance.text.text, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OptionsSelector.Prev patch: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(PromptPopup), "Prompt")]
    public class PromptPopup_Prompt_Patch
    {
        public static float lastPromptTime = 0f;

        static void Postfix(string title)
        {
            try
            {
                if (!string.IsNullOrEmpty(title))
                {
                    lastPromptTime = Time.time;
                    AccessibilityMod.Speak(title, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in PromptPopup patch: {ex.Message}");
            }
        }
    }

    // TODO: Re-enable slider value changes once type issues are resolved
    /*
    [HarmonyPatch(typeof(OptionsSlider), "SetValueFromSliderToUsage")]
    public class OptionsSlider_SetValue_Patch
    {
        static void Postfix(OptionsSlider __instance)
        {
            try
            {
                if (__instance.s != null && __instance.showTempChangesWithoutSaving)
                {
                    var s = __instance.s;
                    string value = s.wholeNumbers ? s.value.ToString("F0") : s.value.ToString("F1");
                    AccessibilityMod.Speak(value, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OptionsSlider patch: {ex.Message}");
            }
        }
    }
    */
}