using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the office phone interface.
    /// </summary>
    public static class PhoneAccessibility
    {
        private static bool lastEnterState = false;
        private static bool lastLeftState = false;
        private static bool lastRightState = false;

        /// <summary>
        /// Called from AccessibilityMod.OnUpdate to handle keyboard input for phone.
        /// </summary>
        public static void HandleInput()
        {
            try
            {
                // Find active phone
                var phones = Object.FindObjectsOfType<OfficePhone>();
                if (phones == null || phones.Count == 0)
                {
                    // Reset state when phone is not active
                    lastEnterState = false;
                    lastLeftState = false;
                    lastRightState = false;
                    return;
                }

                OfficePhone activePhone = null;
                foreach (var phone in phones)
                {
                    if (phone != null && phone.gameObject.activeInHierarchy)
                    {
                        activePhone = phone;
                        break;
                    }
                }

                if (activePhone == null)
                    return;

                var phoneState = activePhone.phoneState;

                // Left arrow - previous number (only in TONE state when dialing)
                bool leftPressed = Input.GetKey(KeyCode.LeftArrow);
                if (leftPressed && !lastLeftState && phoneState == OfficePhone.PhoneState.TONE)
                {
                    activePhone.OnKeyLeft();
                }
                lastLeftState = leftPressed;

                // Right arrow - next number (only in TONE state when dialing)
                bool rightPressed = Input.GetKey(KeyCode.RightArrow);
                if (rightPressed && !lastRightState && phoneState == OfficePhone.PhoneState.TONE)
                {
                    activePhone.OnKeyRight();
                }
                lastRightState = rightPressed;

                // Enter key
                bool enterPressed = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
                if (enterPressed && !lastEnterState)
                {
                    if (phoneState == OfficePhone.PhoneState.STANDBY)
                    {
                        // Pick up the phone
                        MelonLogger.Msg("Phone: Picking up phone via Enter key");
                        activePhone.OnPhoneClick();
                    }
                    else if (phoneState == OfficePhone.PhoneState.TONE)
                    {
                        // Dial the currently selected number
                        int selectedNum = activePhone.selectedNumber;
                        string numStr = (selectedNum == 10) ? "0" : selectedNum.ToString();
                        MelonLogger.Msg($"Phone: Dialing number {numStr} via Enter key");
                        activePhone.OnNumberClick(numStr, false);
                    }
                }
                lastEnterState = enterPressed;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in phone input handler: {ex.Message}");
            }
        }
    }

    // Patch for number selection with rotary dial
    [HarmonyPatch(typeof(OfficePhone), "set_selectedNumber")]
    public class OfficePhone_SetSelectedNumber_Patch
    {
        static void Postfix(OfficePhone __instance, int value)
        {
            try
            {
                if (value >= 0 && value <= 9)
                {
                    AccessibilityMod.Speak(value.ToString(), true);
                }
                else if (value == 10)
                {
                    AccessibilityMod.Speak("0", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in selected number patch: {ex.Message}");
            }
        }
    }

    // Patch for when a number is clicked/dialed
    [HarmonyPatch(typeof(OfficePhone), "OnNumberClick", new System.Type[] { typeof(string), typeof(bool) })]
    public class OfficePhone_OnNumberClick_Patch
    {
        static void Prefix(string number)
        {
            try
            {
                if (!string.IsNullOrEmpty(number))
                {
                    AccessibilityMod.Speak(number, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in number click patch: {ex.Message}");
            }
        }
    }

    // Patch for highlighting a number with keyboard navigation
    [HarmonyPatch(typeof(OfficePhone), "HighlightNumber")]
    public class OfficePhone_HighlightNumber_Patch
    {
        static void Postfix(int num)
        {
            try
            {
                if (num >= 1 && num <= 9)
                {
                    AccessibilityMod.Speak(num.ToString(), true);
                }
                else if (num == 0 || num == 10)
                {
                    AccessibilityMod.Speak("0", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in highlight number patch: {ex.Message}");
            }
        }
    }

    // Patch for dial animation completion
    [HarmonyPatch(typeof(OfficePhone), "OnDoneDialAnimation")]
    public class OfficePhone_OnDoneDialAnimation_Patch
    {
        static void Postfix(OfficePhone __instance)
        {
            try
            {
                // Nothing to announce - the game will handle what happens next
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in done dial animation patch: {ex.Message}");
            }
        }
    }

    // Patch for no answer text display
    [HarmonyPatch(typeof(OfficePhone), "OnDisable")]
    public class OfficePhone_OnDisable_Patch
    {
        static void Prefix(OfficePhone __instance)
        {
            try
            {
                // Check if there's a "no answer" message before closing
                if (__instance.noAnswerText != null && 
                    __instance.noAnswerText.gameObject.activeSelf && 
                    !string.IsNullOrEmpty(__instance.noAnswerText.text))
                {
                    AccessibilityMod.Speak(__instance.noAnswerText.text, true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in phone disable patch: {ex.Message}");
            }
        }
    }
}