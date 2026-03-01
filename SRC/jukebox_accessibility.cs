using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using Il2CppChickenPolice;
using UnityEngine;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the jukebox mini-game.
    /// Provides screen reader announcements and keyboard/controller navigation for music tracks.
    /// </summary>
    public static class JukeboxAccessibility
    {
        private static JukeboxPlayer currentJukeboxPlayer;
        private static JukeboxLogic currentJukeboxLogic;
        private static JukeboxPoster currentJukeboxPoster;
        private static List<JukeboxTitle> availableTracks;
        private static int currentTrackIndex = -1;
        private static int lastAnnouncedIdx = -1;
        private static float lastInputTime = 0f;
        private static readonly float INPUT_COOLDOWN = 0.2f;

        /// <summary>
        /// Initializes the jukebox accessibility system when the jukebox opens.
        /// </summary>
        public static void Initialize(JukeboxLogic jukeboxLogic)
        {
            try
            {
                currentJukeboxLogic = jukeboxLogic;
                currentJukeboxPlayer = null;
                availableTracks = new List<JukeboxTitle>();
                currentTrackIndex = -1;
                lastAnnouncedIdx = -1;

                // Find the JukeboxPlayer in the scene
                var jukeboxPlayers = Object.FindObjectsOfType<JukeboxPlayer>();
                if (jukeboxPlayers != null && jukeboxPlayers.Count > 0)
                {
                    currentJukeboxPlayer = jukeboxPlayers[0];
                    CacheAvailableTracks();

                    // Find the JukeboxPoster (glass display) in the scene
                    var jukeboxPosters = Object.FindObjectsOfType<JukeboxPoster>();
                    if (jukeboxPosters != null && jukeboxPosters.Count > 0)
                    {
                        currentJukeboxPoster = jukeboxPosters[0];
                        MelonLogger.Msg("JukeboxPoster found in scene");
                    }

                    AnnounceJukeboxOpened();
                }
                else
                {
                    MelonLogger.Warning("JukeboxPlayer not found in scene");
                    AccessibilityMod.Speak("Jukebox opened, but tracks not found", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error initializing jukebox accessibility: {ex}");
            }
        }

        /// <summary>
        /// Caches all available (unlocked) tracks for navigation.
        /// Only includes tracks where isTitleEnabled is true to maintain parity with sighted players.
        /// </summary>
        private static void CacheAvailableTracks()
        {
            try
            {
                availableTracks.Clear();

                if (currentJukeboxPlayer == null || currentJukeboxPlayer.titles == null)
                {
                    MelonLogger.Warning("JukeboxPlayer or titles array is null");
                    return;
                }

                var titles = currentJukeboxPlayer.titles;
                for (int i = 0; i < titles.Count; i++)
                {
                    var title = titles[i];
                    if (title != null && title.isTitleEnabled)
                    {
                        availableTracks.Add(title);
                    }
                }

                MelonLogger.Msg($"Cached {availableTracks.Count} available tracks");

                // Set initial selection to first track if available
                if (availableTracks.Count > 0)
                {
                    currentTrackIndex = 0;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error caching available tracks: {ex}");
            }
        }

        /// <summary>
        /// Announces that the jukebox has opened and how many tracks are available.
        /// </summary>
        private static void AnnounceJukeboxOpened()
        {
            try
            {

                if (availableTracks.Count == 0)
                {
                    AccessibilityMod.Speak("Jukebox opened. No tracks available.", true);
                }
                else
                {
                    string announcement = $"Jukebox opened. {availableTracks.Count} track";
                    if (availableTracks.Count != 1)
                    {
                        announcement += "s";
                    }
                    announcement += " available. Use Tab to navigate, Enter to play, Backspace to exit. Press L to list all tracks. Press G to break the glass display.";
                    AccessibilityMod.Speak(announcement, true);

                    // Announce the first track
                    AnnounceCurrentTrack(false);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing jukebox opened: {ex}");
            }
        }

        /// <summary>
        /// Navigates to the next track.
        /// </summary>
        public static void NextTrack()
        {
            try
            {
                if (!CanProcessInput()) return;

                if (availableTracks == null || availableTracks.Count == 0)
                {
                    AccessibilityMod.Speak("No tracks available", true);
                    return;
                }

                currentTrackIndex = (currentTrackIndex + 1) % availableTracks.Count;
                AnnounceCurrentTrack(true);
                lastInputTime = Time.time;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error navigating to next track: {ex}");
            }
        }

        /// <summary>
        /// Navigates to the previous track.
        /// </summary>
        public static void PreviousTrack()
        {
            try
            {
                if (!CanProcessInput()) return;

                if (availableTracks == null || availableTracks.Count == 0)
                {
                    AccessibilityMod.Speak("No tracks available", true);
                    return;
                }

                currentTrackIndex--;
                if (currentTrackIndex < 0)
                {
                    currentTrackIndex = availableTracks.Count - 1;
                }

                AnnounceCurrentTrack(true);
                lastInputTime = Time.time;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error navigating to previous track: {ex}");
            }
        }

        /// <summary>
        /// Plays the currently selected track.
        /// </summary>
        public static void PlayCurrentTrack()
        {
            try
            {
                if (currentJukeboxPlayer == null)
                {
                    AccessibilityMod.Speak("Jukebox player not available", true);
                    return;
                }

                if (availableTracks == null || availableTracks.Count == 0)
                {
                    AccessibilityMod.Speak("No tracks available", true);
                    return;
                }

                if (currentTrackIndex < 0 || currentTrackIndex >= availableTracks.Count)
                {
                    AccessibilityMod.Speak("No track selected", true);
                    return;
                }

                var selectedTrack = availableTracks[currentTrackIndex];
                if (selectedTrack == null)
                {
                    AccessibilityMod.Speak("Track not available", true);
                    return;
                }

                string trackName = GetTrackName(selectedTrack);
                string trackNumStr = GetTrackNumber(selectedTrack);

                // Parse the track number to get the button index
                // TrackButton naming: TrackButton0-9 (0-based)
                // JukeboxTitle.trackNum: "1"-"10" (1-based string)
                // So: buttonIndex = int.Parse(trackNum) - 1
                if (!int.TryParse(trackNumStr, out int trackNum))
                {
                    MelonLogger.Error($"Failed to parse track number: {trackNumStr}");
                    AccessibilityMod.Speak("Could not play track", true);
                    return;
                }

                int buttonIndex = trackNum - 1;

                // Directly call PlayTrack with the button index (0-based)
                // This triggers the same workflow as clicking the button
                if (currentJukeboxPlayer != null)
                {
                    currentJukeboxPlayer.PlayTrack(buttonIndex);
                    AccessibilityMod.Speak($"Playing {trackName}", true);
                }
                else
                {
                    AccessibilityMod.Speak("Jukebox not available", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error playing current track: {ex}");
            }
        }

        /// <summary>
        /// Exits the jukebox mini-game and returns to the game world.
        /// </summary>
        public static void ExitJukebox()
        {
            try
            {
                if (currentJukeboxLogic == null)
                {
                    MelonLogger.Warning("Cannot exit jukebox - JukeboxLogic not found");
                    AccessibilityMod.Speak("Jukebox not available", true);
                    return;
                }

                MelonLogger.Msg("Exiting jukebox mini-game");

                // Call the game's exit method
                currentJukeboxLogic.ExitMinigame();

                AccessibilityMod.Speak("Exiting jukebox", true);

                // Reset state will be handled by the OnJukeboxClosed patch
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error exiting jukebox: {ex}");
                AccessibilityMod.Speak("Error exiting jukebox", true);
            }
        }


        /// <summary>
        /// Gets the track ID (index in original titles array) for a given JukeboxTitle.
        /// </summary>
        private static int GetTrackId(JukeboxTitle track)
        {
            try
            {
                if (currentJukeboxPlayer == null || currentJukeboxPlayer.titles == null || track == null)
                {
                    return -1;
                }

                var titles = currentJukeboxPlayer.titles;
                for (int i = 0; i < titles.Count; i++)
                {
                    if (titles[i] == track)
                    {
                        return i;
                    }
                }

                return -1;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error getting track ID: {ex}");
                return -1;
            }
        }

        /// <summary>
        /// Announces the currently selected track.
        /// </summary>
        private static void AnnounceCurrentTrack(bool interrupt)
        {
            try
            {
                if (availableTracks == null || currentTrackIndex < 0 || currentTrackIndex >= availableTracks.Count)
                {
                    return;
                }

                // Avoid duplicate announcements
                if (currentTrackIndex == lastAnnouncedIdx && interrupt)
                {
                    return;
                }

                var track = availableTracks[currentTrackIndex];
                if (track == null)
                {
                    return;
                }

                string trackName = GetTrackName(track);
                string trackNumber = GetTrackNumber(track);

                string announcement = "";
                if (!string.IsNullOrEmpty(trackNumber))
                {
                    announcement = $"Track {trackNumber}: ";
                }
                announcement += trackName;

                AccessibilityMod.Speak(announcement, interrupt);
                lastAnnouncedIdx = currentTrackIndex;

                MelonLogger.Msg($"Announced track: {announcement}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing current track: {ex}");
            }
        }

        /// <summary>
        /// Announces all available tracks.
        /// </summary>
        public static void AnnounceAllTracks()
        {
            try
            {
                if (availableTracks == null || availableTracks.Count == 0)
                {
                    AccessibilityMod.Speak("No tracks available", true);
                    return;
                }

                string announcement = $"Jukebox has {availableTracks.Count} track";
                if (availableTracks.Count != 1)
                {
                    announcement += "s";
                }
                announcement += ": ";

                for (int i = 0; i < availableTracks.Count; i++)
                {
                    var track = availableTracks[i];
                    if (track != null)
                    {
                        string trackNumber = GetTrackNumber(track);
                        string trackName = GetTrackName(track);

                        if (!string.IsNullOrEmpty(trackNumber))
                        {
                            announcement += $"Track {trackNumber}: ";
                        }
                        announcement += trackName;

                        if (i < availableTracks.Count - 1)
                        {
                            announcement += ", ";
                        }
                    }
                }

                AccessibilityMod.Speak(announcement, true);
                MelonLogger.Msg($"Announced all tracks: {announcement}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing all tracks: {ex}");
            }
        }

        /// <summary>
        /// Extracts the track name from a JukeboxTitle.
        /// </summary>
        private static string GetTrackName(JukeboxTitle title)
        {
            try
            {
                if (title == null)
                {
                    return "Unknown track";
                }

                // Try to get text from LocalizedTextMeshPro
                if (title.textRef != null)
                {
                    try
                    {
                        var textMeshPro = title.textRef;
                        var textProperty = textMeshPro.GetType().GetProperty("text");
                        if (textProperty != null)
                        {
                            var text = textProperty.GetValue(textMeshPro)?.ToString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                return text;
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"Could not get text from textRef: {ex.Message}");
                    }
                }

                // Fallback to localization key
                if (!string.IsNullOrEmpty(title.localizationKey))
                {
                    return title.localizationKey;
                }

                // Last resort: use track number
                if (!string.IsNullOrEmpty(title.trackNum))
                {
                    return $"Track {title.trackNum}";
                }

                return "Unknown track";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error getting track name: {ex}");
                return "Unknown track";
            }
        }

        /// <summary>
        /// Gets the track number as a string.
        /// </summary>
        private static string GetTrackNumber(JukeboxTitle title)
        {
            try
            {
                if (title != null && !string.IsNullOrEmpty(title.trackNum))
                {
                    return title.trackNum;
                }
                return "";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error getting track number: {ex}");
                return "";
            }
        }

        /// <summary>
        /// Checks if input can be processed (cooldown check).
        /// </summary>
        private static bool CanProcessInput()
        {
            return (Time.time - lastInputTime) >= INPUT_COOLDOWN;
        }


        /// <summary>
        /// Resets the jukebox accessibility state when the jukebox closes.
        /// </summary>
        public static void Reset()
        {
            currentJukeboxPlayer = null;
            currentJukeboxLogic = null;
            currentJukeboxPoster = null;
            availableTracks = null;
            currentTrackIndex = -1;
            lastAnnouncedIdx = -1;
            lastInputTime = 0f;
            MelonLogger.Msg("Jukebox accessibility reset");
        }

        /// <summary>
        /// Breaks the glass display on top of the jukebox by directly activating the cracked state.
        /// </summary>
        public static void BreakGlass()
        {
            try
            {
                if (currentJukeboxPoster == null)
                {
                    // Try to find the poster
                    var jukeboxPosters = Object.FindObjectsOfType<JukeboxPoster>();
                    if (jukeboxPosters != null && jukeboxPosters.Count > 0)
                    {
                        currentJukeboxPoster = jukeboxPosters[0];
                    }
                    else
                    {
                        AccessibilityMod.Speak("Glass display not found", true);
                        return;
                    }
                }

                // Check if the glass is already broken
                if (currentJukeboxPoster.posterCracked != null && currentJukeboxPoster.posterCracked.activeSelf)
                {
                    AccessibilityMod.Speak("Glass is already broken", true);
                    return;
                }

                // Directly activate the cracked poster and trigger the win
                if (currentJukeboxPoster.posterCracked != null)
                {
                    // Activate the cracked glass visual
                    currentJukeboxPoster.posterCracked.SetActive(true);

                    // Start the delayed win coroutine
                    currentJukeboxPoster.StartCoroutine(currentJukeboxPoster.delayedWin());

                    MelonLogger.Msg("Glass broken - activated posterCracked and started delayedWin coroutine");
                    AccessibilityMod.Speak("Glass broken", true);
                }
                else
                {
                    MelonLogger.Warning("posterCracked GameObject not found");
                    AccessibilityMod.Speak("Could not break glass", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error breaking glass: {ex}");
                AccessibilityMod.Speak("Error breaking glass", true);
            }
        }

        /// <summary>
        /// Checks if the jukebox is currently active.
        /// </summary>
        public static bool IsActive()
        {
            return currentJukeboxLogic != null && currentJukeboxPlayer != null;
        }

        /// <summary>
        /// Checks if the jukebox is currently paused.
        /// </summary>
        public static bool IsPaused()
        {
            return currentJukeboxLogic != null && currentJukeboxLogic.isPaused;
        }

        /// <summary>
        /// Handles jukebox input processing - called from AccessibilityMod.OnUpdate()
        /// </summary>
        public static void HandleInput()
        {
            try
            {
                // Check if jukebox is active
                if (currentJukeboxLogic == null)
                {
                    // Try to find active jukebox
                    var jukeboxes = Object.FindObjectsOfType<JukeboxLogic>();
                    if (jukeboxes != null && jukeboxes.Count > 0)
                    {
                        Initialize(jukeboxes[0]);
                    }
                    else
                    {
                        return; // No jukebox active
                    }
                }

                // Check if jukebox is still valid
                if (currentJukeboxLogic == null || !currentJukeboxLogic.gameObject.activeInHierarchy)
                {
                    Reset();
                    return;
                }

                // Don't process input if paused
                if (IsPaused())
                {
                    return;
                }

                // Keyboard navigation
                // Backspace or Escape - Exit jukebox
                if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Escape))
                {
                    ExitJukebox();
                }
                // Tab - Next track
                else if (Input.GetKeyDown(KeyCode.Tab) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                {
                    NextTrack();
                }
                // Shift+Tab - Previous track
                else if (Input.GetKeyDown(KeyCode.Tab) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    PreviousTrack();
                }
                // Enter or Space - Play track
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                {
                    PlayCurrentTrack();
                }
                // L - List all tracks
                else if (Input.GetKeyDown(KeyCode.L))
                {
                    AnnounceAllTracks();
                }
                // H - Announce current track (help)
                else if (Input.GetKeyDown(KeyCode.H))
                {
                    AnnounceAllTracks();
                }
                // G - Break the glass display (easter egg)
                else if (Input.GetKeyDown(KeyCode.G))
                {
                    BreakGlass();
                }

                // Controller: Y button (JoystickButton3) - Break glass
                if (Input.GetKeyDown(KeyCode.JoystickButton3))
                {
                    BreakGlass();
                }

                // Controller navigation is handled by Harmony patches on minigameCursor methods
                // See: MinigameCursor_OnButtonSouth_Patch, MinigameCursor_OnButtonEast_Patch, MinigameCursor_OnLStickMove_Patch
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in JukeboxAccessibility.HandleInput: {ex}");
            }
        }
    }

    /// <summary>
    /// Patch minigameCursor.OnButtonSouth to intercept controller A/Cross button in jukebox
    /// </summary>
    [HarmonyPatch(typeof(minigameCursor), "OnButtonSouth")]
    public class MinigameCursor_OnButtonSouth_Patch
    {
        static bool Prefix(minigameCursor __instance)
        {
            try
            {
                // Only intercept if jukebox is active
                if (JukeboxAccessibility.IsActive())
                {
                    MelonLogger.Msg("[Jukebox] OnButtonSouth intercepted - Playing track");
                    JukeboxAccessibility.PlayCurrentTrack();
                    return false; // Prevent original method from executing
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OnButtonSouth patch: {ex}");
            }

            return true; // Allow original method to execute
        }
    }

    /// <summary>
    /// Patch minigameCursor.OnButtonEast to intercept controller B/Circle button in jukebox
    /// </summary>
    [HarmonyPatch(typeof(minigameCursor), "OnButtonEast")]
    public class MinigameCursor_OnButtonEast_Patch
    {
        static bool Prefix(minigameCursor __instance)
        {
            try
            {
                // Only intercept if jukebox is active
                if (JukeboxAccessibility.IsActive())
                {
                    MelonLogger.Msg("[Jukebox] OnButtonEast intercepted - Exiting jukebox");
                    JukeboxAccessibility.ExitJukebox();
                    return false; // Prevent original method from executing
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OnButtonEast patch: {ex}");
            }

            return true; // Allow original method to execute
        }
    }

    /// <summary>
    /// Patch minigameCursor.OnLStickMove to intercept left stick for navigation in jukebox
    /// </summary>
    [HarmonyPatch(typeof(minigameCursor), "OnLStickMove")]
    public class MinigameCursor_OnLStickMove_Patch
    {
        private static float lastInputTime = 0f;
        private const float INPUT_COOLDOWN = 0.3f;

        static bool Prefix(minigameCursor __instance, InputValue value)
        {
            try
            {
                // Only intercept if jukebox is active
                if (JukeboxAccessibility.IsActive())
                {
                    // In Il2Cpp, InputValue.Get<T>() doesn't work the same way
                    // Instead, we read the current axis values directly
                    float verticalInput = Input.GetAxis("Vertical");

                    // Check cooldown
                    if (Time.time - lastInputTime < INPUT_COOLDOWN)
                    {
                        return false; // Block input during cooldown
                    }

                    // Vertical navigation
                    if (Mathf.Abs(verticalInput) > 0.5f)
                    {
                        if (verticalInput > 0.5f)
                        {
                            // Up - Previous track
                            MelonLogger.Msg($"[Jukebox] Left stick UP - Previous track");
                            JukeboxAccessibility.PreviousTrack();
                            lastInputTime = Time.time;
                        }
                        else if (verticalInput < -0.5f)
                        {
                            // Down - Next track
                            MelonLogger.Msg($"[Jukebox] Left stick DOWN - Next track");
                            JukeboxAccessibility.NextTrack();
                            lastInputTime = Time.time;
                        }
                        return false; // Prevent cursor movement
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in OnLStickMove patch: {ex}");
            }

            return true; // Allow original cursor movement
        }
    }
}
