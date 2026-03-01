using HarmonyLib;
using Il2Cpp;
using Il2CppChickenPolice;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility.Patches
{
    /// <summary>
    /// Patches PieMenu to announce actions when navigating the ActionPanel (pie menu)
    /// </summary>
    [HarmonyPatch(typeof(PieMenu))]
    public class PieMenuPatch
    {
        [HarmonyPatch(nameof(PieMenu.state), MethodType.Setter)]
        [HarmonyPostfix]
        public static void AnnounceSelectedAction(PieMenu __instance, PieMenu.PieState value)
        {
            if (PieMenuAccessibility._isCollectingLabels) return;
            if (PieMenuAccessibility.IsInCooldown()) return;

            try
            {
                Text labelText = __instance.PieMenuLabel;
                if (labelText == null) return;

                string actionText = labelText.text;
                if (string.IsNullOrEmpty(actionText)) return;

                AccessibilityMod.Speak(actionText, interrupt: true);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in PieMenuPatch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Polling-based system to announce all pie menu options when the menu opens
    /// </summary>
    public static class PieMenuAccessibility
    {
        internal static bool _isCollectingLabels = false;
        private static bool _wasActive = false;
        private static float _cooldownUntil = 0f;

        // Direction order for announcement: Up, Right, Down, Left
        private static readonly PieMenu.PieState[] _stateOrder = new[]
        {
            PieMenu.PieState.SPEAK,    // Up
            PieMenu.PieState.ASK,      // Right
            PieMenu.PieState.QUESTION, // Down
            PieMenu.PieState.LOOK      // Left
        };

        private static readonly Dictionary<PieMenu.PieState, string> _directionKeys = new()
        {
            { PieMenu.PieState.SPEAK, "up" },
            { PieMenu.PieState.ASK, "right" },
            { PieMenu.PieState.QUESTION, "down" },
            { PieMenu.PieState.LOOK, "left" }
        };

        // Direction translations keyed by language ID or name (lowercase)
        private static readonly Dictionary<string, Dictionary<string, string>> _directionTranslations = new()
        {
            { "en", new Dictionary<string, string> { {"up","Up"}, {"down","Down"}, {"left","Left"}, {"right","Right"} } },
            { "english", new Dictionary<string, string> { {"up","Up"}, {"down","Down"}, {"left","Left"}, {"right","Right"} } },
            { "de", new Dictionary<string, string> { {"up","Oben"}, {"down","Unten"}, {"left","Links"}, {"right","Rechts"} } },
            { "german", new Dictionary<string, string> { {"up","Oben"}, {"down","Unten"}, {"left","Links"}, {"right","Rechts"} } },
            { "deutsch", new Dictionary<string, string> { {"up","Oben"}, {"down","Unten"}, {"left","Links"}, {"right","Rechts"} } },
            { "fr", new Dictionary<string, string> { {"up","Haut"}, {"down","Bas"}, {"left","Gauche"}, {"right","Droite"} } },
            { "french", new Dictionary<string, string> { {"up","Haut"}, {"down","Bas"}, {"left","Gauche"}, {"right","Droite"} } },
            { "es", new Dictionary<string, string> { {"up","Arriba"}, {"down","Abajo"}, {"left","Izquierda"}, {"right","Derecha"} } },
            { "spanish", new Dictionary<string, string> { {"up","Arriba"}, {"down","Abajo"}, {"left","Izquierda"}, {"right","Derecha"} } },
            { "it", new Dictionary<string, string> { {"up","Su"}, {"down","Gi\u00f9"}, {"left","Sinistra"}, {"right","Destra"} } },
            { "italian", new Dictionary<string, string> { {"up","Su"}, {"down","Gi\u00f9"}, {"left","Sinistra"}, {"right","Destra"} } },
            { "pt", new Dictionary<string, string> { {"up","Cima"}, {"down","Baixo"}, {"left","Esquerda"}, {"right","Direita"} } },
            { "portuguese", new Dictionary<string, string> { {"up","Cima"}, {"down","Baixo"}, {"left","Esquerda"}, {"right","Direita"} } },
            { "ru", new Dictionary<string, string> { {"up","\u0412\u0432\u0435\u0440\u0445"}, {"down","\u0412\u043d\u0438\u0437"}, {"left","\u0412\u043b\u0435\u0432\u043e"}, {"right","\u0412\u043f\u0440\u0430\u0432\u043e"} } },
            { "russian", new Dictionary<string, string> { {"up","\u0412\u0432\u0435\u0440\u0445"}, {"down","\u0412\u043d\u0438\u0437"}, {"left","\u0412\u043b\u0435\u0432\u043e"}, {"right","\u0412\u043f\u0440\u0430\u0432\u043e"} } },
            { "pl", new Dictionary<string, string> { {"up","G\u00f3ra"}, {"down","D\u00f3\u0142"}, {"left","Lewo"}, {"right","Prawo"} } },
            { "polish", new Dictionary<string, string> { {"up","G\u00f3ra"}, {"down","D\u00f3\u0142"}, {"left","Lewo"}, {"right","Prawo"} } },
            { "zh", new Dictionary<string, string> { {"up","\u4e0a"}, {"down","\u4e0b"}, {"left","\u5de6"}, {"right","\u53f3"} } },
            { "chinese", new Dictionary<string, string> { {"up","\u4e0a"}, {"down","\u4e0b"}, {"left","\u5de6"}, {"right","\u53f3"} } },
            { "ja", new Dictionary<string, string> { {"up","\u4e0a"}, {"down","\u4e0b"}, {"left","\u5de6"}, {"right","\u53f3"} } },
            { "japanese", new Dictionary<string, string> { {"up","\u4e0a"}, {"down","\u4e0b"}, {"left","\u5de6"}, {"right","\u53f3"} } },
            { "ko", new Dictionary<string, string> { {"up","\uc704"}, {"down","\uc544\ub798"}, {"left","\uc67c\ucabd"}, {"right","\uc624\ub978\ucabd"} } },
            { "korean", new Dictionary<string, string> { {"up","\uc704"}, {"down","\uc544\ub798"}, {"left","\uc67c\ucabd"}, {"right","\uc624\ub978\ucabd"} } },
            { "tr", new Dictionary<string, string> { {"up","Yukar\u0131"}, {"down","A\u015fa\u011f\u0131"}, {"left","Sol"}, {"right","Sa\u011f"} } },
            { "turkish", new Dictionary<string, string> { {"up","Yukar\u0131"}, {"down","A\u015fa\u011f\u0131"}, {"left","Sol"}, {"right","Sa\u011f"} } },
            { "hu", new Dictionary<string, string> { {"up","Fel"}, {"down","Le"}, {"left","Bal"}, {"right","Jobb"} } },
            { "hungarian", new Dictionary<string, string> { {"up","Fel"}, {"down","Le"}, {"left","Bal"}, {"right","Jobb"} } },
            { "cs", new Dictionary<string, string> { {"up","Nahoru"}, {"down","Dol\u016f"}, {"left","Vlevo"}, {"right","Vpravo"} } },
            { "czech", new Dictionary<string, string> { {"up","Nahoru"}, {"down","Dol\u016f"}, {"left","Vlevo"}, {"right","Vpravo"} } },
            { "ar", new Dictionary<string, string> { {"up","\u0623\u0639\u0644\u0649"}, {"down","\u0623\u0633\u0641\u0644"}, {"left","\u064a\u0633\u0627\u0631"}, {"right","\u064a\u0645\u064a\u0646"} } },
            { "arabic", new Dictionary<string, string> { {"up","\u0623\u0639\u0644\u0649"}, {"down","\u0623\u0633\u0641\u0644"}, {"left","\u064a\u0633\u0627\u0631"}, {"right","\u064a\u0645\u064a\u0646"} } },
        };


        public static bool IsInCooldown()
        {
            return Time.time < _cooldownUntil;
        }

        public static void HandleInput()
        {
            try
            {
                var pieMenus = Object.FindObjectsOfType<PieMenu>();
                PieMenu activePieMenu = null;

                for (int i = 0; i < pieMenus.Length; i++)
                {
                    if (pieMenus[i].gameObject.activeInHierarchy)
                    {
                        activePieMenu = pieMenus[i];
                        break;
                    }
                }

                bool isActive = activePieMenu != null;

                if (isActive && !_wasActive)
                {
                    CollectAndAnnounceLabels(activePieMenu);
                }

                _wasActive = isActive;
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"PieMenuAccessibility error: {ex.Message}");
            }
        }

        private static string GetDirectionName(PieMenu.PieState state)
        {
            string dirKey = _directionKeys[state];

            try
            {
                var lang = Localization.SelectedLanguage;
                string langId = lang.id;
                string langName = lang.name;

                // Try all possible keys: exact id, lowercase id, name, lowercase name, 2-char prefix
                string[] keysToTry = new string[]
                {
                    langId,
                    langId?.ToLower(),
                    langName,
                    langName?.ToLower(),
                    langId?.Length >= 2 ? langId.Substring(0, 2).ToLower() : null,
                    langName?.Length >= 2 ? langName.Substring(0, 2).ToLower() : null
                };

                foreach (var key in keysToTry)
                {
                    if (key != null && _directionTranslations.TryGetValue(key, out var translations))
                    {
                        if (translations.TryGetValue(dirKey, out string translated))
                        {
                            return translated;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"PieMenu language error: {ex.Message}");
            }

            // Fallback to English
            return _directionTranslations["en"][dirKey];
        }

        private static void CollectAndAnnounceLabels(PieMenu pieMenu)
        {
            var buttons = pieMenu.buttons;
            if (buttons == null || buttons.Count == 0) return;

            var label = pieMenu.PieMenuLabel;
            if (label == null) return;

            var originalState = pieMenu.state;

            _isCollectingLabels = true;

            var parts = new List<string>();

            foreach (var state in _stateOrder)
            {
                // Find the button for this state and check if enabled
                PieMenuButton btn = null;
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (buttons[i].state == state)
                    {
                        btn = buttons[i];
                        break;
                    }
                }

                if (btn != null && btn.isEnabled)
                {
                    pieMenu.state = state;
                    string labelText = label.text;
                    if (!string.IsNullOrEmpty(labelText))
                    {
                        string direction = GetDirectionName(state);
                        parts.Add($"{direction}: {labelText}");
                    }
                }
            }

            // Restore original state
            pieMenu.state = originalState;

            _isCollectingLabels = false;

            if (parts.Count > 0)
            {
                // Set cooldown to prevent individual state announcements from interrupting
                _cooldownUntil = Time.time + 3f;
                AccessibilityMod.Speak(string.Join(", ", parts), interrupt: true);
            }
        }
    }
}
