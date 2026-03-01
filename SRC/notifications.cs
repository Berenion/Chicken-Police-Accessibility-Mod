using HarmonyLib;
using Il2Cpp;
using System.Collections;
using UnityEngine;

namespace ChickenPoliceAccessibility.Patches
{
    /// <summary>
    /// Patches NotificationManager to announce notifications when they appear
    /// </summary>
    [HarmonyPatch(typeof(NotificationManager))]
    public class NotificationManagerPatch
    {
        [HarmonyPatch(nameof(NotificationManager.ShowEntry))]
        [HarmonyPostfix]
        public static void AnnounceNotification(NotificationManager __instance, NotificationManager.NotificationEntryData entry)
        {
            try
            {
                // Start a coroutine to wait for the notification text to be populated
                MelonLoader.MelonCoroutines.Start(AnnounceNotificationDelayed(__instance, entry));
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in NotificationManager.ShowEntry patch: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceNotificationDelayed(NotificationManager manager, NotificationManager.NotificationEntryData entry)
        {
            // Wait multiple frames for the notification to be fully initialized
            // Item notifications seem to need more time than feature unlocks
            yield return null;
            yield return null;
            yield return new UnityEngine.WaitForSeconds(0.1f);

            try
            {
                // Get the notification type for context
                string notificationType = GetNotificationTypeDescription(entry.type);
                
                // Try to get the text from the currently visible notification
                var currentNotification = manager.currentlyVisibleNotificationGameObject;
                if (currentNotification == null)
                {
                    yield break;
                }

                // Get the NotificationEntry component
                var notificationEntry = currentNotification.GetComponent<NotificationEntry>();
                if (notificationEntry == null)
                {
                    yield break;
                }

                // Try multiple ways to get the notification text
                string notificationText = null;

                // Method 1: Direct label text
                if (notificationEntry.label != null)
                {
                    notificationText = notificationEntry.label.text;
                }

                // Method 2: Try LocalizedText components if label text is generic
                if (string.IsNullOrEmpty(notificationText) || 
                    notificationText.Contains("New item!") || 
                    notificationText.Contains("new item"))
                {
                    var localizedTexts = currentNotification.GetComponentsInChildren<LocalizedText>();
                    if (localizedTexts != null)
                    {
                        foreach (var localizedText in localizedTexts)
                        {
                            try
                            {
                                if (localizedText != null && !string.IsNullOrEmpty(localizedText.text))
                                {
                                    // Skip generic phrases, look for specific item names
                                    string text = localizedText.text;
                                    if (!text.Contains("New item!") && 
                                        !text.Contains("new item") &&
                                        !text.Contains("Item found"))
                                    {
                                        notificationText = text;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                // Skip this component
                            }
                        }
                    }
                }

                // Method 3: Try all Text components
                if (string.IsNullOrEmpty(notificationText) || 
                    notificationText.Contains("New item!"))
                {
                    var allTexts = currentNotification.GetComponentsInChildren<UnityEngine.UI.Text>();
                    if (allTexts != null)
                    {
                        foreach (var text in allTexts)
                        {
                            try
                            {
                                if (text != null && !string.IsNullOrEmpty(text.text))
                                {
                                    string textContent = text.text;
                                    if (!textContent.Contains("New item!") && 
                                        !textContent.Contains("new item") &&
                                        !textContent.Contains("Item found"))
                                    {
                                        notificationText = textContent;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                // Skip this component
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(notificationText))
                {
                    // Fallback: just announce the type
                    AccessibilityMod.Speak(notificationType, interrupt: true);
                    yield break;
                }

                // Announce with type prefix
                string announcement = $"{notificationType}. {notificationText}";
                AccessibilityMod.Speak(announcement, interrupt: true);
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in AnnounceNotificationDelayed: {ex.Message}");
            }
        }

        private static string GetNotificationTypeDescription(NotificationManager.NotificationType type)
        {
            switch (type)
            {
                case NotificationManager.NotificationType.ITEM:
                    return "Item found";
                case NotificationManager.NotificationType.LOCATION:
                    return "Location unlocked";
                case NotificationManager.NotificationType.CHARACTER:
                    return "Character revealed";
                case NotificationManager.NotificationType.CLUE:
                    return "Clue unlocked";
                case NotificationManager.NotificationType.PERSONAL_INFO:
                    return "Personal info unlocked";
                case NotificationManager.NotificationType.LOCATION_INFO:
                    return "Location info unlocked";
                case NotificationManager.NotificationType.SHOW_UNLOCKED:
                    return "Show unlocked";
                case NotificationManager.NotificationType.QUESTIONING_UNLOCKED:
                    return "Questioning unlocked";
                case NotificationManager.NotificationType.MAP_UNLOCKED:
                    return "Map unlocked";
                case NotificationManager.NotificationType.INVENTORY_UNLOCKED:
                    return "Inventory unlocked";
                case NotificationManager.NotificationType.NEW_IMPRESSION:
                    return "New impression";
                case NotificationManager.NotificationType.NOTEBOOK_UNLOCKED:
                    return "Notebook unlocked";
                case NotificationManager.NotificationType.COLLECTIBLE_FOUND:
                    return "Collectible found";
                case NotificationManager.NotificationType.CODEX_ENTRY_FOUND:
                    return "Codex entry found";
                case NotificationManager.NotificationType.ACHIEVEMENT_UNLOCKED:
                    return "Achievement unlocked";
                case NotificationManager.NotificationType.ART_UNLOCKED:
                    return "Art unlocked";
                case NotificationManager.NotificationType.OTHER:
                default:
                    return "Notification";
            }
        }
    }
}