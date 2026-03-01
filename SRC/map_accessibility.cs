using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the map interface.
    /// Provides screen reader announcements and keyboard/controller navigation for map locations.
    /// </summary>
    public static class MapAccessibility
    {
        private static MapView currentMapView;
        private static int lastAnnouncedIdx = -1;

        /// <summary>
        /// Updates the current MapView reference.
        /// </summary>
        public static void UpdateMapView(MapView mapView)
        {
            currentMapView = mapView;
            lastAnnouncedIdx = -1;
        }

        /// <summary>
        /// Navigates to the next location on the map.
        /// </summary>
        public static void NextLocation()
        {
            if (currentMapView == null || currentMapView.activeLocations == null)
            {
                AccessibilityMod.Speak("Map not available", true);
                return;
            }

            var locations = currentMapView.activeLocations;
            if (locations.Count == 0)
            {
                AccessibilityMod.Speak("No locations available", true);
                return;
            }

            int currentIdx = currentMapView.selectedIdx;
            int newIdx = (currentIdx + 1) >= locations.Count ? 0 : currentIdx + 1;

            currentMapView.selectedIdx = newIdx;
            MelonLogger.Msg($"Next location: index {newIdx}");
        }

        /// <summary>
        /// Navigates to the previous location on the map.
        /// </summary>
        public static void PreviousLocation()
        {
            if (currentMapView == null || currentMapView.activeLocations == null)
            {
                AccessibilityMod.Speak("Map not available", true);
                return;
            }

            var locations = currentMapView.activeLocations;
            if (locations.Count == 0)
            {
                AccessibilityMod.Speak("No locations available", true);
                return;
            }

            int currentIdx = currentMapView.selectedIdx;
            int newIdx = (currentIdx - 1) < 0 ? locations.Count - 1 : currentIdx - 1;

            currentMapView.selectedIdx = newIdx;
            MelonLogger.Msg($"Previous location: index {newIdx}");
        }

        /// <summary>
        /// Selects/activates the currently focused location.
        /// </summary>
        public static void SelectCurrentLocation()
        {
            if (currentMapView == null || currentMapView.activeLocations == null)
            {
                AccessibilityMod.Speak("Map not available", true);
                return;
            }

            var locations = currentMapView.activeLocations;
            int currentIdx = currentMapView.selectedIdx;

            if (currentIdx < 0 || currentIdx >= locations.Count)
            {
                AccessibilityMod.Speak("No location selected", true);
                return;
            }

            var selectedLocation = locations[currentIdx];
            if (selectedLocation != null)
            {
                // Call the game's click handler
                currentMapView.OnClickLocation(selectedLocation);
                MelonLogger.Msg($"Selecting location: {selectedLocation.locationName}");
            }
        }

        /// <summary>
        /// Announces all available locations on the map.
        /// </summary>
        public static void AnnounceAllLocations()
        {
            if (currentMapView == null || currentMapView.activeLocations == null)
            {
                AccessibilityMod.Speak("Map not available", true);
                return;
            }

            var locations = currentMapView.activeLocations;
            if (locations.Count == 0)
            {
                AccessibilityMod.Speak("No locations available", true);
                return;
            }

            string announcement = $"{locations.Count} locations available: ";

            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location != null && !string.IsNullOrEmpty(location.locationName))
                {
                    announcement += location.locationName;
                    if (i < locations.Count - 1)
                    {
                        announcement += ", ";
                    }
                }
            }

            AccessibilityMod.Speak(announcement, true);
            MelonLogger.Msg($"Announced all locations: {announcement}");
        }

        /// <summary>
        /// Resets tracking state.
        /// </summary>
        public static void Reset()
        {
            currentMapView = null;
            lastAnnouncedIdx = -1;
        }

        /// <summary>
        /// Announces the location at the given index.
        /// </summary>
        private static void AnnounceLocation(MapView mapView, int index)
        {
            if (mapView == null || mapView.activeLocations == null)
                return;

            var locations = mapView.activeLocations;

            if (index < 0 || index >= locations.Count)
                return;

            // Avoid re-announcing the same location
            if (index == lastAnnouncedIdx)
                return;

            lastAnnouncedIdx = index;

            var location = locations[index];
            if (location == null || string.IsNullOrEmpty(location.locationName))
                return;

            string announcement = $"{location.locationName}, location {index + 1} of {locations.Count}";

            AccessibilityMod.Speak(announcement, true);
            MelonLogger.Msg($"Announced location: {announcement}");
        }

        #region Harmony Patches

        /// <summary>
        /// Patch for when the map is opened.
        /// </summary>
        [HarmonyPatch(typeof(MapView), "OnEnable")]
        public class MapView_OnEnable_Patch
        {
            static void Postfix(MapView __instance)
            {
                try
                {
                    UpdateMapView(__instance);

                    // Announce map opened
                    AccessibilityMod.Speak("Map opened", true);

                    // Give UI time to initialize, then announce location count
                    System.Threading.Tasks.Task.Delay(200).ContinueWith(_ =>
                    {
                        if (__instance != null && __instance.activeLocations != null)
                        {
                            int count = __instance.activeLocations.Count;
                            if (count > 0)
                            {
                                // Announce the currently selected location
                                AnnounceLocation(__instance, __instance.selectedIdx);
                            }
                            else
                            {
                                AccessibilityMod.Speak("No locations available", true);
                            }
                        }
                    });

                    MelonLogger.Msg("Map opened - accessibility initialized");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in MapView.OnEnable patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for when the map is closed.
        /// </summary>
        [HarmonyPatch(typeof(MapView), "OnDisable")]
        public class MapView_OnDisable_Patch
        {
            static void Prefix()
            {
                try
                {
                    AccessibilityMod.Speak("Map closed", true);
                    Reset();
                    MelonLogger.Msg("Map closed - accessibility reset");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in MapView.OnDisable patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for when a location is selected/focused.
        /// This is the main announcement patch that fires on any selection change.
        /// </summary>
        [HarmonyPatch(typeof(MapView), "set_selectedIdx")]
        public class MapView_SetSelectedIdx_Patch
        {
            static void Postfix(MapView __instance, int value)
            {
                try
                {
                    AnnounceLocation(__instance, value);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in MapView.set_selectedIdx patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for when a location is clicked/activated.
        /// </summary>
        [HarmonyPatch(typeof(MapView), "OnClickLocation")]
        public class MapView_OnClickLocation_Patch
        {
            static void Prefix(MapView __instance, MapViewLocation m)
            {
                try
                {
                    if (m != null && !string.IsNullOrEmpty(m.locationName))
                    {
                        AccessibilityMod.Speak($"Traveling to {m.locationName}", true);
                        MelonLogger.Msg($"Location clicked: {m.locationName}");
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in MapView.OnClickLocation patch: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Patch for MapView Update to handle custom keyboard and controller input.
        /// </summary>
        [HarmonyPatch(typeof(MapView), "Update")]
        public class MapView_Update_Patch
        {
            static void Postfix()
            {
                try
                {
                    // Keyboard Navigation: Tab/Shift+Tab
                    if (Input.GetKeyDown(KeyCode.Tab))
                    {
                        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        {
                            PreviousLocation();
                        }
                        else
                        {
                            NextLocation();
                        }
                    }

                    // Keyboard Selection: Enter or Space
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                    {
                        SelectCurrentLocation();
                    }

                    // List all locations: L key
                    if (Input.GetKeyDown(KeyCode.L))
                    {
                        AnnounceAllLocations();
                    }

                    // Controller Navigation: D-Pad Up/Down
                    // Note: Using vertical axis which captures both D-pad and left stick
                    float verticalInput = Input.GetAxis("Vertical");

                    // Using GetButtonDown for discrete navigation (not continuous)
                    if (Input.GetButtonDown("Vertical"))
                    {
                        if (verticalInput > 0.5f)
                        {
                            NextLocation();
                        }
                        else if (verticalInput < -0.5f)
                        {
                            PreviousLocation();
                        }
                    }

                    // Controller Selection: Button South (A on Xbox, Cross on PlayStation)
                    // This is typically mapped to "Jump" or "Submit" in Unity's input system
                    if (Input.GetButtonDown("Jump") || Input.GetButtonDown("Submit"))
                    {
                        SelectCurrentLocation();
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in MapView.Update input handler: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
