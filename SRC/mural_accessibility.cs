using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Accessibility system for the mural drawing mini-game.
    /// Provides grid-based navigation with proximity audio feedback.
    /// </summary>
    public static class MuralAccessibility
    {
        private static AsylumGameLogic activeAsylum = null;
        private static bool wasActive = false;
        private static float lastAnnouncementTime = 0f;
        private static float announcementCooldown = 1.0f;

        // Grid-based navigation
        private static int gridWidth = 50;  // Increased from 20 for finer control (~0.36 units between points)
        private static int gridHeight = 40; // Increased from 15 for finer control (~0.25 units between points)
        private static int currentGridX = 25; // Start at center
        private static int currentGridY = 20;
        private static string lastAnnouncedRegion = "";

        // Mural bounds (will be detected from game)
        private static Rect muralBounds;
        private static bool boundsDetected = false;

        // Drawing mode
        private static bool isDrawing = false;
        private static List<Vector2> currentPath = new List<Vector2>();

        // Audio feedback
        private static AudioSource audioSource = null;
        private static float lastProximityBeepTime = 0f;
        private static float proximityBeepCooldown = 0.3f;

        // Hotspot tracking
        private static Dictionary<int, Vector3> hotspotPositions = new Dictionary<int, Vector3>();
        private static HashSet<int> triggeredHotspots = new HashSet<int>();

        // Auto-solve
        private static float autoSolveHoldTime = 0f;
        private static bool autoSolveAnnounced = false;

        // Clue-based puzzle system
        private static List<HotspotClueData> clueSequence = null;
        private static int currentTargetIndex = 0;
        private static int currentHintLevel = 0;  // 0=no hints yet, 1-3=hint levels
        private static float hintLevelStartTime = 0f;

        /// <summary>
        /// Category of clue used to describe hotspot location
        /// </summary>
        private enum ClueCategory
        {
            AbsolutePosition,    // Grid region (e.g., "upper left")
            RelativePosition,    // Relative to previous finds (e.g., "east of hotspot 2")
            Geometric,           // Centroid or outlier (e.g., "near the center")
            Ordinal,            // Sorted order (e.g., "rightmost hotspot")
            Triangulation       // Between two hotspots (e.g., "between 1 and 3")
        }

        /// <summary>
        /// Contains the 3 hint levels for a hotspot
        /// </summary>
        private class ClueTemplate
        {
            public string cryptic;   // Level 1: Abstract riddle
            public string clear;     // Level 2: Direct spatial description
            public string direct;    // Level 3: Exact grid coordinates
        }

        /// <summary>
        /// Complete clue data for one hotspot in the sequence
        /// </summary>
        private class HotspotClueData
        {
            public int hotspotIndex;      // Index into activeAsylum.spots[]
            public Vector3 position;      // World position
            public ClueTemplate clues;    // 3 hint levels
            public ClueCategory category; // Type of clue used
        }

        /// <summary>
        /// Main update handler called from AccessibilityMod.OnUpdate()
        /// </summary>
        public static void HandleInput()
        {
            try
            {
                // Detect active mural mini-game
                var asylumGames = UnityEngine.Object.FindObjectsOfType<AsylumGameLogic>();
                bool isActive = asylumGames != null && asylumGames.Length > 0;

                if (isActive && asylumGames.Length > 0)
                {
                    activeAsylum = asylumGames[0];

                    // First time activation
                    if (!wasActive)
                    {
                        OnMuralActivated();
                        wasActive = true;
                    }

                    // Handle input
                    HandleMuralInput();
                }
                else
                {
                    // Cleanup when deactivated
                    if (wasActive)
                    {
                        OnMuralDeactivated();
                        wasActive = false;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Error in MuralAccessibility.HandleInput: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Called when mural mini-game becomes active
        /// </summary>
        private static void OnMuralActivated()
        {
            MelonLogger.Msg("Mural mini-game activated");

            // Reset state
            currentGridX = gridWidth / 2;
            currentGridY = gridHeight / 2;
            isDrawing = false;
            currentPath.Clear();
            boundsDetected = false;
            hotspotPositions.Clear();
            triggeredHotspots.Clear();
            autoSolveHoldTime = 0f;
            autoSolveAnnounced = false;
            lastAnnouncedRegion = "";

            // Reset clue system state
            clueSequence = null;
            currentTargetIndex = 0;
            currentHintLevel = 0;
            hintLevelStartTime = 0f;

            // Detect mural bounds and hotspots
            DetectMuralBounds();
            CacheHotspotPositions();

            // Generate clue sequence
            if (hotspotPositions.Count > 0)
            {
                clueSequence = GenerateClueSequence();
                MelonLogger.Msg($"Generated {clueSequence.Count} clues");
            }

            // Setup audio source
            if (audioSource == null)
            {
                GameObject audioObj = new GameObject("MuralAccessibilityAudio");
                UnityEngine.Object.DontDestroyOnLoad(audioObj);
                audioSource = audioObj.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.volume = 0.8f;
            }

            // Announce intro
            string intro = "Mural riddle puzzle. You must find hidden hotspots by solving spatial riddles. " +
                          $"There are {activeAsylum.treshold} hotspots to discover in sequence. " +
                          "Use arrow keys to navigate the grid, Space to draw lines over hotspots. " +
                          "Press H to hear hints - you get 3 levels: cryptic, clear, and direct. " +
                          "Audio beeps guide you to the current target. " +
                          "Press R for progress, L to list found hotspots, Hold A for 3 seconds to auto-solve.";
            AccessibilityMod.Speak(intro, interrupt: true);

            // Announce first clue if available
            if (clueSequence != null && clueSequence.Count > 0)
            {
                AccessibilityMod.Speak("Press H for first hint.", interrupt: false);
            }
        }

        /// <summary>
        /// Called when mural mini-game becomes inactive
        /// </summary>
        private static void OnMuralDeactivated()
        {
            MelonLogger.Msg("Mural mini-game deactivated");
            activeAsylum = null;
            isDrawing = false;
            currentPath.Clear();
        }

        /// <summary>
        /// Detect the bounds of the mural drawing area
        /// </summary>
        private static void DetectMuralBounds()
        {
            try
            {
                if (activeAsylum == null || activeAsylum.mcursor == null)
                {
                    // Default bounds
                    muralBounds = new Rect(-8f, -6f, 16f, 12f);
                    boundsDetected = true;
                    MelonLogger.Msg("Using default mural bounds");
                    return;
                }

                // Try to detect from camera or existing objects
                Camera camera = Camera.main;
                if (camera != null)
                {
                    float height = camera.orthographicSize * 2f;
                    float width = height * camera.aspect;
                    muralBounds = new Rect(-width / 2f, -height / 2f, width, height);
                    boundsDetected = true;
                    MelonLogger.Msg($"Detected mural bounds from camera: {muralBounds}");
                }
                else
                {
                    muralBounds = new Rect(-8f, -6f, 16f, 12f);
                    boundsDetected = true;
                    MelonLogger.Msg("Using default mural bounds (no camera)");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Error detecting mural bounds: {e.Message}");
                muralBounds = new Rect(-8f, -6f, 16f, 12f);
                boundsDetected = true;
            }
        }

        /// <summary>
        /// Cache all hotspot positions from the game
        /// </summary>
        private static void CacheHotspotPositions()
        {
            try
            {
                if (activeAsylum == null || activeAsylum.spots == null)
                    return;

                hotspotPositions.Clear();
                triggeredHotspots.Clear();

                // Only cache up to treshold (winning condition) hotspots
                int hotspotsToCache = Mathf.Min(activeAsylum.treshold, activeAsylum.spots.Length);

                for (int i = 0; i < hotspotsToCache; i++)
                {
                    var spot = activeAsylum.spots[i];
                    if (spot != null)
                    {
                        Vector3 pos = spot.transform.position;
                        hotspotPositions[i] = pos;

                        if (spot.isAlreadyTriggered)
                        {
                            triggeredHotspots.Add(i);
                        }
                    }
                }

                MelonLogger.Msg($"Cached {hotspotPositions.Count} hotspot positions (game requires {activeAsylum.treshold} to complete)");
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Error caching hotspot positions: {e.Message}");
            }
        }

        /// <summary>
        /// Main input handling
        /// </summary>
        private static void HandleMuralInput()
        {
            if (activeAsylum == null || activeAsylum.isPaused)
                return;

            // Update triggered hotspots
            UpdateTriggeredHotspots();

            // Check auto-solve hold (hold A for 3 seconds)
            if (Input.GetKey(KeyCode.A))
            {
                autoSolveHoldTime += Time.deltaTime;
                if (autoSolveHoldTime >= 3.0f && !autoSolveAnnounced)
                {
                    AccessibilityMod.Speak("Auto-solving puzzle", interrupt: true);
                    activeAsylum.EndMinigame();
                    autoSolveAnnounced = true;
                    return;
                }
            }
            else
            {
                autoSolveHoldTime = 0f;
                autoSolveAnnounced = false;
            }

            // Navigation keys (arrow keys)
            bool moved = false;
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                currentGridY = Mathf.Min(currentGridY + 1, gridHeight - 1);
                moved = true;
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                currentGridY = Mathf.Max(currentGridY - 1, 0);
                moved = true;
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                currentGridX = Mathf.Min(currentGridX + 1, gridWidth - 1);
                moved = true;
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                currentGridX = Mathf.Max(currentGridX - 1, 0);
                moved = true;
            }

            if (moved)
            {
                AnnouncePosition();
                MoveCursorToGridPosition();

                // If drawing, add point to current path
                if (isDrawing)
                {
                    Vector2 worldPos = GridToWorldPosition(currentGridX, currentGridY);
                    currentPath.Add(worldPos);
                }

                // Play proximity beep
                PlayProximityBeep();
            }

            // Drawing toggle (Space)
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ToggleDrawing();
            }

            // Report progress (R)
            if (Input.GetKeyDown(KeyCode.R))
            {
                ReportProgress();
            }

            // Request hint (H) - progressive hints
            if (Input.GetKeyDown(KeyCode.H))
            {
                RequestHint();
            }

            // Repeat current hint (T)
            if (Input.GetKeyDown(KeyCode.T))
            {
                RepeatCurrentHint();
            }

            // Help / Show controls (Question mark - Shift+/)
            if (Input.GetKeyDown(KeyCode.Slash) && Input.GetKey(KeyCode.LeftShift))
            {
                ShowHelp();
            }

            // Debug: Show exact target position (D)
            if (Input.GetKeyDown(KeyCode.D))
            {
                if (clueSequence != null && currentTargetIndex < clueSequence.Count)
                {
                    int targetIdx = clueSequence[currentTargetIndex].hotspotIndex;
                    Vector3 targetPos = hotspotPositions[targetIdx];
                    Vector2 currentPos = GridToWorldPosition(currentGridX, currentGridY);
                    float distance = Vector2.Distance(new Vector2(targetPos.x, targetPos.y), currentPos);
                    AccessibilityMod.Speak($"Debug: Target at {targetPos.x:F2}, {targetPos.y:F2}. You are at {currentPos.x:F2}, {currentPos.y:F2}. Distance: {distance:F2} units", interrupt: true);
                    MelonLogger.Msg($"Target: ({targetPos.x:F2}, {targetPos.y:F2}), Cursor: ({currentPos.x:F2}, {currentPos.y:F2}), Distance: {distance:F2}");
                }
                else
                {
                    AccessibilityMod.Speak("No active target", interrupt: true);
                }
            }

            // Clear all lines (C)
            if (Input.GetKeyDown(KeyCode.C))
            {
                activeAsylum.DeleteAllLine();
                AccessibilityMod.Speak("All lines cleared", interrupt: true);
            }

            // List hotspot status (L)
            if (Input.GetKeyDown(KeyCode.L))
            {
                ListHotspots();
            }

            // Find nearest marker (M)
            if (Input.GetKeyDown(KeyCode.M))
            {
                FindNearestMarker();
            }

            // Periodic proximity beeps
            if (Time.time - lastProximityBeepTime > proximityBeepCooldown)
            {
                PlayProximityBeep(autoPlay: false);
            }
        }

        /// <summary>
        /// Update which hotspots have been triggered (with sequential validation)
        /// </summary>
        private static void UpdateTriggeredHotspots()
        {
            try
            {
                if (activeAsylum == null || activeAsylum.spots == null || clueSequence == null)
                    return;

                for (int i = 0; i < activeAsylum.spots.Length; i++)
                {
                    var spot = activeAsylum.spots[i];
                    if (spot != null && spot.isAlreadyTriggered && !triggeredHotspots.Contains(i))
                    {
                        OnHotspotTriggered(i);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Error updating triggered hotspots: {e.Message}");
            }
        }

        /// <summary>
        /// Handle hotspot trigger with sequential validation
        /// </summary>
        private static void OnHotspotTriggered(int triggeredIndex)
        {
            if (clueSequence == null || currentTargetIndex >= clueSequence.Count)
                return;

            int expectedIndex = clueSequence[currentTargetIndex].hotspotIndex;

            if (triggeredIndex == expectedIndex)
            {
                // Correct! Advance to next
                triggeredHotspots.Add(triggeredIndex);
                AccessibilityMod.Speak($"Hotspot {currentTargetIndex + 1} found! {triggeredHotspots.Count} of {clueSequence.Count}", interrupt: true);

                currentTargetIndex++;
                currentHintLevel = 0;
                hintLevelStartTime = 0f;

                if (currentTargetIndex >= clueSequence.Count)
                {
                    AccessibilityMod.Speak("All hotspots found! Puzzle complete!", interrupt: false);

                    // Call the game's completion method
                    try
                    {
                        MelonLogger.Msg("Calling EndMinigame() to complete the puzzle");
                        activeAsylum.EndMinigame();
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Error($"Error calling EndMinigame: {e.Message}");
                    }
                }
                else
                {
                    AccessibilityMod.Speak($"Next target: Hotspot {currentTargetIndex + 1}. Press H for hint.", interrupt: false);
                }
            }
            else
            {
                // Wrong hotspot (shouldn't happen often in sequential mode)
                AccessibilityMod.Speak($"That is not the current target. Focus on finding hotspot {currentTargetIndex + 1}. Press H for hint.", interrupt: true);
            }
        }

        /// <summary>
        /// Convert grid coordinates to world position
        /// </summary>
        private static Vector2 GridToWorldPosition(int gridX, int gridY)
        {
            if (!boundsDetected)
                DetectMuralBounds();

            float x = muralBounds.x + (gridX / (float)(gridWidth - 1)) * muralBounds.width;
            float y = muralBounds.y + (gridY / (float)(gridHeight - 1)) * muralBounds.height;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Interpolate path to add intermediate points for better collision detection
        /// </summary>
        private static List<Vector2> InterpolatePath(List<Vector2> originalPath, float stepSize)
        {
            List<Vector2> interpolated = new List<Vector2>();

            if (originalPath.Count < 2)
                return new List<Vector2>(originalPath);

            for (int i = 0; i < originalPath.Count - 1; i++)
            {
                Vector2 start = originalPath[i];
                Vector2 end = originalPath[i + 1];
                float distance = Vector2.Distance(start, end);
                int steps = Mathf.Max(2, Mathf.CeilToInt(distance / stepSize));

                for (int j = 0; j < steps; j++)
                {
                    float t = j / (float)(steps - 1);
                    Vector2 point = Vector2.Lerp(start, end, t);
                    interpolated.Add(point);
                }
            }

            return interpolated;
        }

        /// <summary>
        /// Move the game cursor to the current grid position
        /// </summary>
        private static void MoveCursorToGridPosition()
        {
            try
            {
                if (activeAsylum == null || activeAsylum.mcursor == null)
                    return;

                Vector2 worldPos = GridToWorldPosition(currentGridX, currentGridY);
                activeAsylum.mcursor.transform.position = new Vector3(worldPos.x, worldPos.y, activeAsylum.mcursor.transform.position.z);
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Error moving cursor: {e.Message}");
            }
        }

        /// <summary>
        /// Announce current grid position
        /// </summary>
        private static void AnnouncePosition()
        {
            if (Time.time - lastAnnouncementTime < announcementCooldown)
                return;

            string message = $"Row {currentGridY + 1}, Column {currentGridX + 1}";

            // Check if we've entered a new region
            Vector2 currentPos = GridToWorldPosition(currentGridX, currentGridY);
            Vector3 pos3d = new Vector3(currentPos.x, currentPos.y, 0);
            string currentRegion = GetRegionName(pos3d, muralBounds);

            if (currentRegion != lastAnnouncedRegion && !string.IsNullOrEmpty(currentRegion))
            {
                message += $", entering {currentRegion}";
                lastAnnouncedRegion = currentRegion;
            }

            // Check proximity to found hotspots
            int nearestFoundHotspotNum = -1;
            float nearestFoundDist = float.MaxValue;

            int foundIndex = 1;
            foreach (int hotspotIdx in triggeredHotspots)
            {
                if (hotspotPositions.ContainsKey(hotspotIdx))
                {
                    Vector3 hotspotPos = hotspotPositions[hotspotIdx];
                    float dist = Vector2.Distance(currentPos, new Vector2(hotspotPos.x, hotspotPos.y));
                    if (dist < 1.0f && dist < nearestFoundDist)
                    {
                        nearestFoundDist = dist;
                        nearestFoundHotspotNum = foundIndex;
                    }
                }
                foundIndex++;
            }

            if (nearestFoundHotspotNum > 0)
            {
                message += $", near found hotspot {nearestFoundHotspotNum}";
            }

            // Add proximity info for current target (reduced thresholds to match beep range of 1.5)
            float distance = GetNearestHotspotDistance();
            if (distance < 0.5f)
            {
                message += ", very close to target";
            }
            else if (distance < 1.0f)
            {
                message += ", nearby target";
            }

            AccessibilityMod.Speak(message, interrupt: true);
            lastAnnouncementTime = Time.time;
        }

        /// <summary>
        /// Get distance to current target hotspot (target-only proximity system)
        /// </summary>
        private static float GetNearestHotspotDistance()
        {
            if (clueSequence == null || currentTargetIndex >= clueSequence.Count)
                return float.MaxValue;

            Vector2 currentPos = GridToWorldPosition(currentGridX, currentGridY);
            int targetIdx = clueSequence[currentTargetIndex].hotspotIndex;

            if (!hotspotPositions.ContainsKey(targetIdx))
                return float.MaxValue;

            Vector2 targetPos = new Vector2(
                hotspotPositions[targetIdx].x,
                hotspotPositions[targetIdx].y
            );

            return Vector2.Distance(currentPos, targetPos);
        }

        /// <summary>
        /// Play proximity beep - pitch increases as cursor gets closer to hotspots
        /// </summary>
        private static void PlayProximityBeep(bool autoPlay = true)
        {
            if (audioSource == null)
                return;

            float distance = GetNearestHotspotDistance();

            // Only play if within range and cooldown expired (reduced from 5.0 to 1.5 for difficulty)
            float beepRange = 1.5f;
            if (distance > beepRange || Time.time - lastProximityBeepTime < proximityBeepCooldown)
                return;

            // Calculate frequency based on distance
            // Closer = higher pitch (880 Hz at 0, 220 Hz at range limit)
            float maxFreq = 880f;
            float minFreq = 220f;
            float frequency = Mathf.Lerp(maxFreq, minFreq, distance / beepRange);

            // Generate and play beep
            AudioClip beep = GenerateBeep(frequency, 0.1f);
            audioSource.PlayOneShot(beep);
            lastProximityBeepTime = Time.time;
        }

        // ===== HINT SYSTEM FUNCTIONS =====

        /// <summary>
        /// Request next hint level (H key)
        /// </summary>
        private static void RequestHint()
        {
            if (clueSequence == null || currentTargetIndex >= clueSequence.Count)
            {
                AccessibilityMod.Speak("No active target. Puzzle may be complete.", interrupt: true);
                return;
            }

            ClueTemplate currentClues = clueSequence[currentTargetIndex].clues;

            if (currentHintLevel == 0)
            {
                // First hint - cryptic
                currentHintLevel = 1;
                hintLevelStartTime = Time.time;
                AnnounceHint(currentClues.cryptic, 1);
            }
            else if (currentHintLevel == 1)
            {
                // Check if 10 seconds passed
                if (Time.time - hintLevelStartTime >= 10f)
                {
                    currentHintLevel = 2;
                    AnnounceHint(currentClues.clear, 2);
                }
                else
                {
                    float remaining = 10f - (Time.time - hintLevelStartTime);
                    AccessibilityMod.Speak($"Wait {(int)remaining} seconds for next hint level, or press T to repeat current hint.", interrupt: true);
                }
            }
            else if (currentHintLevel == 2)
            {
                // Check if 10 seconds passed
                if (Time.time - hintLevelStartTime >= 10f)
                {
                    currentHintLevel = 3;
                    AnnounceHint(currentClues.direct, 3);
                }
                else
                {
                    float remaining = 10f - (Time.time - hintLevelStartTime);
                    AccessibilityMod.Speak($"Wait {(int)remaining} seconds for final hint, or press T to repeat current hint.", interrupt: true);
                }
            }
            else
            {
                // All hints exhausted
                AccessibilityMod.Speak("All 3 hint levels revealed. Press T to repeat the final hint.", interrupt: true);
            }
        }

        /// <summary>
        /// Announce a hint with level information
        /// </summary>
        private static void AnnounceHint(string hint, int level)
        {
            string levelText = level switch
            {
                1 => "Hint level 1 of 3, cryptic: ",
                2 => "Hint level 2 of 3, clear: ",
                3 => "Final hint, level 3 of 3, direct: ",
                _ => ""
            };

            AccessibilityMod.Speak(levelText + hint, interrupt: true);

            if (level < 3)
            {
                AccessibilityMod.Speak("Press H again in 10 seconds for next hint level, or T to repeat this hint.", interrupt: false);
            }
        }

        /// <summary>
        /// Repeat the current hint level (T key)
        /// </summary>
        private static void RepeatCurrentHint()
        {
            if (clueSequence == null || currentTargetIndex >= clueSequence.Count)
            {
                AccessibilityMod.Speak("No active target. Press H when ready for the first hotspot.", interrupt: true);
                return;
            }

            if (currentHintLevel == 0)
            {
                AccessibilityMod.Speak("No hint revealed yet. Press H to hear the first hint.", interrupt: true);
                return;
            }

            ClueTemplate currentClues = clueSequence[currentTargetIndex].clues;
            string hint = currentHintLevel switch
            {
                1 => currentClues.cryptic,
                2 => currentClues.clear,
                3 => currentClues.direct,
                _ => "No hint available"
            };

            AccessibilityMod.Speak($"Repeating hint level {currentHintLevel}: {hint}", interrupt: true);
        }

        /// <summary>
        /// Toggle drawing mode on/off
        /// </summary>
        private static void ToggleDrawing()
        {
            isDrawing = !isDrawing;

            if (isDrawing)
            {
                currentPath.Clear();
                Vector2 worldPos = GridToWorldPosition(currentGridX, currentGridY);
                currentPath.Add(worldPos);
                AccessibilityMod.Speak("Drawing started", interrupt: true);
            }
            else
            {
                if (currentPath.Count > 1)
                {
                    // Finish the line
                    FinishDrawing();
                    AccessibilityMod.Speak("Drawing finished", interrupt: true);
                }
                else
                {
                    AccessibilityMod.Speak("Drawing cancelled - too short", interrupt: true);
                }
                currentPath.Clear();
            }
        }

        /// <summary>
        /// Finish drawing the current line
        /// </summary>
        private static void FinishDrawing()
        {
            try
            {
                if (activeAsylum == null || activeAsylum.originPen == null || currentPath.Count < 2)
                    return;

                // Interpolate path to add many intermediate points for better collision detection
                List<Vector2> interpolatedPath = InterpolatePath(currentPath, 0.05f); // Point every 0.05 units

                // Check if this line would trigger a hotspot
                bool isSuccessfulAttempt = false;
                int triggeredIdx = -1;
                if (clueSequence != null && currentTargetIndex < clueSequence.Count)
                {
                    int targetIdx = clueSequence[currentTargetIndex].hotspotIndex;
                    Vector3 targetPos = hotspotPositions[targetIdx];

                    // Find closest point in interpolated path to target
                    float minDist = float.MaxValue;
                    foreach (var point in interpolatedPath)
                    {
                        float dist = Vector2.Distance(new Vector2(targetPos.x, targetPos.y), point);
                        if (dist < minDist) minDist = dist;
                    }

                    MelonLogger.Msg($"Line check: {currentPath.Count} original points → {interpolatedPath.Count} interpolated points. Target: ({targetPos.x:F2},{targetPos.y:F2}). Closest point: {minDist:F2}");

                    // Line triggers hotspot if within 0.2 units
                    if (minDist <= 0.2f)
                    {
                        isSuccessfulAttempt = true;
                        triggeredIdx = targetIdx;
                        MelonLogger.Msg($"Line will trigger hotspot {targetIdx} (distance {minDist:F2} <= 0.2), not counting against line limit");
                    }
                }

                // For failed attempts, check max lines limit
                if (!isSuccessfulAttempt && activeAsylum.lines != null && activeAsylum.lines.Count >= activeAsylum.maxLines)
                {
                    AccessibilityMod.Speak($"Maximum {activeAsylum.maxLines} failed attempts reached. Clear lines with C key.", interrupt: true);
                    return;
                }

                // Create a new line GameObject with LineRenderer
                string lineName = isSuccessfulAttempt ? "SuccessfulDiscoveryLine" : "FailedAttemptLine";
                GameObject lineObj = new GameObject(lineName);
                LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();

                // Setup line renderer properties
                lineRenderer.startWidth = 0.1f;
                lineRenderer.endWidth = 0.1f;
                lineRenderer.positionCount = interpolatedPath.Count;

                // Set positions
                Vector3[] positions = new Vector3[interpolatedPath.Count];
                for (int i = 0; i < interpolatedPath.Count; i++)
                {
                    positions[i] = new Vector3(interpolatedPath[i].x, interpolatedPath[i].y, 0f);
                }
                lineRenderer.SetPositions(positions);

                // Set layer/tag to match game's line objects (if needed for collision detection)
                try
                {
                    if (activeAsylum.lines != null && activeAsylum.lines.Count > 0)
                    {
                        GameObject existingLine = activeAsylum.lines[0];
                        if (existingLine != null)
                        {
                            lineObj.layer = existingLine.layer;
                            lineObj.tag = existingLine.tag;
                        }
                    }
                }
                catch
                {
                    // Ignore if unable to match layer/tag
                }

                // Set material (try to copy from original pen, or use default)
                try
                {
                    if (activeAsylum.originPen.lineRenderer != null && activeAsylum.originPen.lineRenderer.material != null)
                    {
                        lineRenderer.material = activeAsylum.originPen.lineRenderer.material;
                    }
                }
                catch
                {
                    // Use default material if original not available
                    lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                    lineRenderer.startColor = Color.white;
                    lineRenderer.endColor = Color.white;
                }

                if (isSuccessfulAttempt)
                {
                    // Successful attempt - don't add to line count, but keep visible
                    // Line stays visible but isn't tracked by the game
                    MelonLogger.Msg($"Drew successful discovery line: {currentPath.Count} original points → {interpolatedPath.Count} interpolated points. Not counted against limit.");

                    // Create permanent marker at hotspot location
                    Vector3 hotspotPos = hotspotPositions[triggeredIdx];
                    GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    marker.name = $"HotspotMarker_{triggeredIdx}";
                    marker.transform.position = hotspotPos;
                    marker.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

                    // Make marker bright and visible
                    var renderer = marker.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material = new Material(Shader.Find("Sprites/Default"));
                        renderer.material.color = Color.green; // Bright green for visibility
                    }

                    MelonLogger.Msg($"Created marker at hotspot {triggeredIdx} position ({hotspotPos.x:F2}, {hotspotPos.y:F2}, {hotspotPos.z:F2})");

                    // Trigger the hotspot
                    OnHotspotTriggered(triggeredIdx);
                    AccessibilityMod.Speak($"Hotspot {currentTargetIndex} found!", interrupt: true);
                }
                else
                {
                    // Failed attempt - add to game's line list (counts against limit)
                    activeAsylum.addNewLine(lineObj);

                    // Call checkSolution() in case game's system can handle it
                    activeAsylum.checkSolution();

                    MelonLogger.Msg($"Drew failed attempt line: {currentPath.Count} original points → {interpolatedPath.Count} interpolated points. Line count: {activeAsylum.lines.Count}/{activeAsylum.maxLines}");

                    int remaining = activeAsylum.maxLines - activeAsylum.lines.Count;
                    if (remaining <= 2)
                    {
                        AccessibilityMod.Speak($"Warning: {remaining} attempts remaining", interrupt: true);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Error finishing drawing: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Report current progress
        /// </summary>
        private static void ReportProgress()
        {
            if (activeAsylum == null || clueSequence == null)
                return;

            int found = triggeredHotspots.Count;
            int total = clueSequence.Count;
            int linesDrawn = activeAsylum.lines != null ? activeAsylum.lines.Count : 0;
            int maxLines = activeAsylum.maxLines;

            string message = $"Progress: {found} of {total} hotspots found. ";
            message += $"Current target: Hotspot {currentTargetIndex + 1}. ";

            if (currentHintLevel > 0)
            {
                message += $"Hint level {currentHintLevel} of 3 revealed. ";
            }
            else
            {
                message += "Press H for first hint. ";
            }

            message += $"{linesDrawn} of {maxLines} failed attempts used.";

            if (found >= total)
            {
                message = "Puzzle complete! All hotspots found!";
            }

            AccessibilityMod.Speak(message, interrupt: true);
        }

        /// <summary>
        /// Show help message
        /// </summary>
        private static void ShowHelp()
        {
            string help = "Mural riddle puzzle controls: " +
                         "Arrow keys to move cursor on 50 by 40 grid. " +
                         "H to request hint - 3 levels: cryptic, clear, then direct. " +
                         "T to repeat current hint. " +
                         "Space to start and stop drawing lines. Successfully finding a hotspot doesn't count as a failed attempt. " +
                         "M to find nearest found hotspot marker (useful for relative clues). " +
                         "D for debug info showing exact target position. " +
                         "R to report progress and current target. " +
                         "C to clear all failed attempt lines. " +
                         "L to list found hotspots. " +
                         "Hold A for 3 seconds to auto-solve puzzle. " +
                         "Audio beeps guide you to the current target hotspot. " +
                         "Green markers appear at found hotspots.";
            AccessibilityMod.Speak(help, interrupt: true);
        }

        /// <summary>
        /// List hotspot status
        /// </summary>
        private static void ListHotspots()
        {
            if (activeAsylum == null || clueSequence == null)
                return;

            int found = triggeredHotspots.Count;
            int total = clueSequence.Count;

            string message = $"Hotspots: {found} found, {total - found} remaining out of {total} total. ";
            message += $"Current target: Hotspot {currentTargetIndex + 1}. ";

            if (found > 0)
            {
                message += "Found hotspots in sequence: ";
                for (int i = 0; i < found; i++)
                {
                    message += $"{i + 1}, ";
                }
            }
            else
            {
                message += "No hotspots found yet. Press H for first hint.";
            }

            AccessibilityMod.Speak(message, interrupt: true);
        }

        /// <summary>
        /// Find and announce the nearest found hotspot marker
        /// </summary>
        private static void FindNearestMarker()
        {
            if (triggeredHotspots.Count == 0)
            {
                AccessibilityMod.Speak("No hotspots found yet. No markers to reference.", interrupt: true);
                return;
            }

            Vector2 currentPos = GridToWorldPosition(currentGridX, currentGridY);
            float minDist = float.MaxValue;
            int nearestMarkerNum = -1;
            Vector3 nearestMarkerPos = Vector3.zero;

            // Find nearest triggered hotspot
            int markerIndex = 1;
            foreach (int hotspotIdx in triggeredHotspots)
            {
                if (hotspotPositions.ContainsKey(hotspotIdx))
                {
                    Vector3 markerPos = hotspotPositions[hotspotIdx];
                    float dist = Vector2.Distance(currentPos, new Vector2(markerPos.x, markerPos.y));
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestMarkerNum = markerIndex;
                        nearestMarkerPos = markerPos;
                    }
                }
                markerIndex++;
            }

            if (nearestMarkerNum > 0)
            {
                // Calculate direction to marker
                string direction = AngleToCompass(new Vector3(currentPos.x, currentPos.y, 0), nearestMarkerPos);
                AccessibilityMod.Speak($"Nearest marker: Hotspot {nearestMarkerNum}, {minDist:F1} units {direction}ward", interrupt: true);
                MelonLogger.Msg($"Nearest marker: Hotspot {nearestMarkerNum} at ({nearestMarkerPos.x:F2}, {nearestMarkerPos.y:F2}), distance {minDist:F2}, direction {direction}");
            }
        }

        // ===== SPATIAL UTILITY FUNCTIONS =====

        /// <summary>
        /// Calculate the centroid (average position) of all hotspots
        /// </summary>
        private static Vector2 CalculateCentroid(Dictionary<int, Vector3> positions)
        {
            if (positions.Count == 0)
                return Vector2.zero;

            float sumX = 0f;
            float sumY = 0f;

            foreach (var pos in positions.Values)
            {
                sumX += pos.x;
                sumY += pos.y;
            }

            return new Vector2(sumX / positions.Count, sumY / positions.Count);
        }

        /// <summary>
        /// Get bounding box containing all hotspots
        /// </summary>
        private static Rect GetBoundingBox(Dictionary<int, Vector3> positions)
        {
            if (positions.Count == 0)
                return new Rect(0, 0, 1, 1);

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var pos in positions.Values)
            {
                if (pos.x < minX) minX = pos.x;
                if (pos.y < minY) minY = pos.y;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y > maxY) maxY = pos.y;
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Get thematic region name for a position
        /// Divides mural into 3x3 grid of named regions
        /// </summary>
        private static string GetRegionName(Vector3 pos, Rect bounds)
        {
            // Determine horizontal region
            float xRel = (pos.x - bounds.x) / bounds.width;
            int colRegion; // 0=left, 1=center, 2=right
            if (xRel < 0.33f)
                colRegion = 0;
            else if (xRel < 0.67f)
                colRegion = 1;
            else
                colRegion = 2;

            // Determine vertical region
            float yRel = (pos.y - bounds.y) / bounds.height;
            int rowRegion; // 0=lower, 1=middle, 2=upper
            if (yRel < 0.33f)
                rowRegion = 0;
            else if (yRel < 0.67f)
                rowRegion = 1;
            else
                rowRegion = 2;

            // Map to thematic names based on chalk mural imagery (3x3 grid)
            // Based on the colorful chalk/graffiti artwork description
            string[,] regionNames = new string[3, 3]
            {
                // Lower row (bottom third) - lower parts of the mural
                { "The House Corner", "The Heart Section", "The Eye Region" },
                // Middle row (middle third) - middle band of the mural
                { "The Western Glyphs", "The Rainbow Circle", "The Eastern Symbols" },
                // Upper row (top third) - upper parts of the mural
                { "The Number Zone", "The Crown Area", "The Star Corner" }
            };

            string regionName = regionNames[rowRegion, colRegion];

            // Debug output
            MelonLogger.Msg($"GetRegionName: pos=({pos.x:F2}, {pos.y:F2}), bounds=({bounds.x:F2}, {bounds.y:F2}, {bounds.width:F2}, {bounds.height:F2}), xRel={xRel:F2}, yRel={yRel:F2}, rowRegion={rowRegion}, colRegion={colRegion}, result={regionName}");

            return regionName;
        }

        /// <summary>
        /// Convert angle between two points to compass direction
        /// </summary>
        private static string AngleToCompass(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dy = to.y - from.y;
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to 8-direction compass
            if (angle < 22.5f || angle >= 337.5f)
                return "east";
            else if (angle < 67.5f)
                return "northeast";
            else if (angle < 112.5f)
                return "north";
            else if (angle < 157.5f)
                return "northwest";
            else if (angle < 202.5f)
                return "west";
            else if (angle < 247.5f)
                return "southwest";
            else if (angle < 292.5f)
                return "south";
            else
                return "southeast";
        }

        /// <summary>
        /// Sort hotspot indices by X coordinate
        /// </summary>
        private static int[] SortByX(Dictionary<int, Vector3> positions)
        {
            return positions.Keys.OrderBy(idx => positions[idx].x).ToArray();
        }

        /// <summary>
        /// Sort hotspot indices by Y coordinate
        /// </summary>
        private static int[] SortByY(Dictionary<int, Vector3> positions)
        {
            return positions.Keys.OrderBy(idx => positions[idx].y).ToArray();
        }

        /// <summary>
        /// Sort hotspot indices by distance from reference point
        /// </summary>
        private static int[] SortByDistanceFrom(Vector3 reference, Dictionary<int, Vector3> positions)
        {
            return positions.Keys.OrderBy(idx =>
            {
                Vector3 pos = positions[idx];
                return Vector2.Distance(new Vector2(reference.x, reference.y),
                                      new Vector2(pos.x, pos.y));
            }).ToArray();
        }

        // ===== CLUE GENERATION FUNCTIONS =====

        /// <summary>
        /// Generate clue sequence for all hotspots
        /// Analyzes spatial relationships and assigns appropriate clue types
        /// </summary>
        private static List<HotspotClueData> GenerateClueSequence()
        {
            List<HotspotClueData> sequence = new List<HotspotClueData>();

            if (hotspotPositions.Count == 0)
                return sequence;

            // Calculate spatial properties
            Vector2 centroid = CalculateCentroid(hotspotPositions);
            Rect bounds = muralBounds; // Use full mural bounds, not just hotspot cluster bounds
            int[] sortedByX = SortByX(hotspotPositions);
            int[] sortedByY = SortByY(hotspotPositions);
            int[] sortedByDist = SortByDistanceFrom(centroid, hotspotPositions);

            MelonLogger.Msg($"Generating clues for {hotspotPositions.Count} hotspots. Centroid: {centroid}, Bounds: {bounds} (using full mural bounds)");

            // First hotspot: Use absolute positioning (corner or distinctive location)
            int firstIdx = sortedByX[0]; // Leftmost
            MelonLogger.Msg($"First hotspot: idx={firstIdx}, pos={hotspotPositions[firstIdx]}, sortedByX={string.Join(",", sortedByX)}");
            sequence.Add(new HotspotClueData
            {
                hotspotIndex = firstIdx,
                position = hotspotPositions[firstIdx],
                category = ClueCategory.AbsolutePosition,
                clues = GenerateAbsoluteClue(firstIdx, hotspotPositions[firstIdx], bounds)
            });

            // If only one hotspot, return early
            if (hotspotPositions.Count == 1)
                return sequence;

            // Second hotspot: Use centroid or another corner
            int secondIdx = sortedByDist[0]; // Closest to center
            if (secondIdx == firstIdx && sortedByDist.Length > 1)
                secondIdx = sortedByDist[1]; // Skip first if same

            // If still the same (only 1 hotspot case, though handled above), use next in sortedByX
            if (secondIdx == firstIdx && sortedByX.Length > 1)
                secondIdx = sortedByX[1];

            sequence.Add(new HotspotClueData
            {
                hotspotIndex = secondIdx,
                position = hotspotPositions[secondIdx],
                category = ClueCategory.Geometric,
                clues = GenerateGeometricClue(secondIdx, hotspotPositions[secondIdx], centroid)
            });

            // Remaining hotspots: Process in spatial order (sorted by X coordinate)
            // This ensures a logical progression and varied clue types
            foreach (var idx in sortedByX)
            {
                if (idx == firstIdx || idx == secondIdx)
                    continue;

                // Decide clue type based on position and what's already in sequence
                Vector3 pos = hotspotPositions[idx];
                ClueCategory category;
                ClueTemplate clues;

                // Check if it's an extreme position (leftmost, rightmost, highest, lowest)
                bool isLeftmost = sortedByX[0] == idx;
                bool isRightmost = sortedByX[sortedByX.Length - 1] == idx;
                bool isHighest = sortedByY[sortedByY.Length - 1] == idx;
                bool isLowest = sortedByY[0] == idx;

                if (isLeftmost || isRightmost || isHighest || isLowest)
                {
                    // Extreme position: use ordinal
                    category = ClueCategory.Ordinal;
                    clues = GenerateOrdinalClue(idx, pos, sortedByX, sortedByY);
                    MelonLogger.Msg($"Hotspot {sequence.Count + 1} (idx {idx}): ORDINAL at ({pos.x:F2}, {pos.y:F2})");
                }
                else
                {
                    // Check if it's an outlier (far from centroid)
                    float distFromCenter = Vector2.Distance(new Vector2(pos.x, pos.y), centroid);
                    float avgDist = sortedByDist.Average(i => Vector2.Distance(new Vector2(hotspotPositions[i].x, hotspotPositions[i].y), centroid));

                    if (distFromCenter > avgDist * 1.5f)
                    {
                        // Outlier: use ordinal
                        category = ClueCategory.Ordinal;
                        clues = GenerateOrdinalClue(idx, pos, sortedByX, sortedByY);
                        MelonLogger.Msg($"Hotspot {sequence.Count + 1} (idx {idx}): ORDINAL (outlier) at ({pos.x:F2}, {pos.y:F2})");
                    }
                    else
                    {
                        // Use relative positioning to nearest already-sequenced hotspot
                        category = ClueCategory.RelativePosition;
                        clues = GenerateRelativeClue(idx, pos, sequence);
                        MelonLogger.Msg($"Hotspot {sequence.Count + 1} (idx {idx}): RELATIVE at ({pos.x:F2}, {pos.y:F2})");
                    }
                }

                sequence.Add(new HotspotClueData
                {
                    hotspotIndex = idx,
                    position = pos,
                    category = category,
                    clues = clues
                });
            }

            // Log full sequence for verification
            MelonLogger.Msg("=== CLUE SEQUENCE SUMMARY ===");
            for (int i = 0; i < sequence.Count; i++)
            {
                var data = sequence[i];
                MelonLogger.Msg($"  Hotspot {i + 1}: idx={data.hotspotIndex}, pos=({data.position.x:F2}, {data.position.y:F2}), category={data.category}");
                MelonLogger.Msg($"    Cryptic: {data.clues.cryptic}");
            }

            return sequence;
        }

        /// <summary>
        /// Generate absolute position clues (grid region)
        /// </summary>
        private static ClueTemplate GenerateAbsoluteClue(int idx, Vector3 pos, Rect bounds)
        {
            string region = GetRegionName(pos, bounds);
            var gridPos = WorldToGrid(pos);

            ClueTemplate clue = new ClueTemplate();

            // Level 1: Cryptic - Just the thematic region name
            clue.cryptic = region;

            // Level 2: Clear - Region + quadrant description
            float xRel = (pos.x - bounds.x) / bounds.width;
            float yRel = (pos.y - bounds.y) / bounds.height;
            string quadrant = "";

            // Subdivide each region into quadrants for more specificity
            if (xRel % 0.33f < 0.165f)
                quadrant += "left side of ";
            else if (xRel % 0.33f > 0.165f)
                quadrant += "right side of ";
            else
                quadrant += "center of ";

            if (yRel % 0.33f < 0.165f)
                quadrant += "lower ";
            else if (yRel % 0.33f > 0.165f)
                quadrant += "upper ";

            clue.clear = $"{quadrant}{region}";

            // Level 3: Direct - Exact grid coordinates with range
            clue.direct = $"Row {gridPos.row} to {gridPos.row + 2}, Column {gridPos.col} to {gridPos.col + 2}";

            return clue;
        }

        /// <summary>
        /// Generate relative position clues (relative to found hotspots)
        /// </summary>
        private static ClueTemplate GenerateRelativeClue(int idx, Vector3 pos, List<HotspotClueData> foundSequence)
        {
            ClueTemplate clue = new ClueTemplate();

            // Find closest previously found hotspot
            int closestIdx = 0;
            float minDist = float.MaxValue;
            for (int i = 0; i < foundSequence.Count; i++)
            {
                float dist = Vector2.Distance(new Vector2(pos.x, pos.y),
                                            new Vector2(foundSequence[i].position.x, foundSequence[i].position.y));
                if (dist < minDist)
                {
                    minDist = dist;
                    closestIdx = i;
                }
            }

            Vector3 refPos = foundSequence[closestIdx].position;
            string compass = AngleToCompass(refPos, pos);
            int refHotspotNum = closestIdx + 1;
            var gridPos = WorldToGrid(pos);

            // Debug: Log relative clue reference
            float refDist = Vector2.Distance(new Vector2(pos.x, pos.y), new Vector2(refPos.x, refPos.y));
            MelonLogger.Msg($"    Relative: from hotspot {refHotspotNum} at ({refPos.x:F2}, {refPos.y:F2}), travel {compass}, distance {refDist:F2}");

            // Level 1: Cryptic (spatial with clear direction)
            clue.cryptic = $"From hotspot {refHotspotNum}, travel {compass}ward";

            // Level 2: Clear
            clue.clear = $"{char.ToUpper(compass[0])}{compass.Substring(1)} of hotspot {refHotspotNum}";

            // Level 3: Direct
            clue.direct = $"From hotspot {refHotspotNum}, go {compass}. Row {gridPos.row}, Column {gridPos.col}";

            return clue;
        }

        /// <summary>
        /// Generate geometric clues (centroid, outlier)
        /// </summary>
        private static ClueTemplate GenerateGeometricClue(int idx, Vector3 pos, Vector2 centroid)
        {
            ClueTemplate clue = new ClueTemplate();

            float dist = Vector2.Distance(new Vector2(pos.x, pos.y), centroid);
            var gridPos = WorldToGrid(pos);
            string region = GetRegionName(pos, muralBounds);

            if (dist < 2.0f)
            {
                // Near center - use region name
                clue.cryptic = $"{region}";
                clue.clear = $"Near the center of the hotspot cluster, in {region}";
                clue.direct = $"Row {gridPos.row} to {gridPos.row + 2}, Column {gridPos.col} to {gridPos.col + 2}";
            }
            else
            {
                // Outlier - still use region but mention isolation
                clue.cryptic = $"{region}, isolated";
                clue.clear = $"Far from the main cluster, isolated in {region}";
                clue.direct = $"Row {gridPos.row} to {gridPos.row + 2}, Column {gridPos.col} to {gridPos.col + 2}";
            }

            return clue;
        }

        /// <summary>
        /// Generate ordinal clues (leftmost, rightmost, etc.)
        /// </summary>
        private static ClueTemplate GenerateOrdinalClue(int idx, Vector3 pos, int[] sortedByX, int[] sortedByY)
        {
            ClueTemplate clue = new ClueTemplate();
            var gridPos = WorldToGrid(pos);

            // Determine ordinal position
            bool isLeftmost = sortedByX[0] == idx;
            bool isRightmost = sortedByX[sortedByX.Length - 1] == idx;
            bool isHighest = sortedByY[sortedByY.Length - 1] == idx;
            bool isLowest = sortedByY[0] == idx;

            if (isRightmost)
            {
                clue.cryptic = "The rightmost mark";
                clue.clear = "Furthest right position, at the eastern edge";
                clue.direct = $"Rightmost hotspot, column {gridPos.col}, row {gridPos.row} to {gridPos.row + 2}";
            }
            else if (isLeftmost)
            {
                clue.cryptic = "The leftmost mark";
                clue.clear = "Furthest left position, at the western edge";
                clue.direct = $"Leftmost hotspot, column {gridPos.col}, row {gridPos.row} to {gridPos.row + 2}";
            }
            else if (isHighest)
            {
                clue.cryptic = "The highest mark";
                clue.clear = "Top-most position, at the upper edge";
                clue.direct = $"Highest hotspot, row {gridPos.row}, column {gridPos.col} to {gridPos.col + 2}";
            }
            else if (isLowest)
            {
                clue.cryptic = "The lowest mark";
                clue.clear = "Bottom-most position, at the lower edge";
                clue.direct = $"Lowest hotspot, row {gridPos.row}, column {gridPos.col} to {gridPos.col + 2}";
            }
            else
            {
                // Not an extreme, fall back to region
                string region = GetRegionName(pos, muralBounds);
                clue.cryptic = region;
                clue.clear = $"Neither an extreme edge nor the center, in {region}";
                clue.direct = $"Row {gridPos.row} to {gridPos.row + 2}, Column {gridPos.col} to {gridPos.col + 2}";
            }

            return clue;
        }

        /// <summary>
        /// Generate triangulation clues (between two hotspots)
        /// </summary>
        private static ClueTemplate GenerateTriangulationClue(int idx, Vector3 pos, Vector3 ref1, Vector3 ref2)
        {
            ClueTemplate clue = new ClueTemplate();
            var gridPos = WorldToGrid(pos);

            // Calculate if point is between the two references
            Vector2 midpoint = new Vector2((ref1.x + ref2.x) / 2f, (ref1.y + ref2.y) / 2f);
            float distToMid = Vector2.Distance(new Vector2(pos.x, pos.y), midpoint);

            clue.cryptic = "Between the first two hotspots, forming a triangle with them";
            clue.clear = "Positioned to form a triangle with hotspots 1 and 2";
            clue.direct = $"Near the midpoint between hotspots 1 and 2, row {gridPos.row}, column {gridPos.col}";

            return clue;
        }

        /// <summary>
        /// Helper: Convert world position to approximate grid position
        /// </summary>
        private static (int row, int col) WorldToGrid(Vector3 worldPos)
        {
            if (!boundsDetected)
                return (7, 10);

            float xRel = (worldPos.x - muralBounds.x) / muralBounds.width;
            float yRel = (worldPos.y - muralBounds.y) / muralBounds.height;

            int col = Mathf.Clamp((int)(xRel * gridWidth), 1, gridWidth);
            int row = Mathf.Clamp((int)(yRel * gridHeight), 1, gridHeight);

            return (row, col);
        }

        /// <summary>
        /// Generate a simple sine wave beep
        /// </summary>
        private static AudioClip GenerateBeep(float frequency, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = (int)(sampleRate * duration);

            AudioClip clip = AudioClip.Create("Beep", sampleCount, 1, sampleRate, false);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.5f;

                // Apply fade envelope
                float envelope = 1.0f;
                if (i < sampleCount * 0.1f)
                    envelope = i / (sampleCount * 0.1f);
                else if (i > sampleCount * 0.9f)
                    envelope = (sampleCount - i) / (sampleCount * 0.1f);

                samples[i] *= envelope;
            }

            clip.SetData(samples, 0);
            return clip;
        }
    }
}
