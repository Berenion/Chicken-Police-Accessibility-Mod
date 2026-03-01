using MelonLoader;
using UnityEngine;
using Il2Cpp;
using UnityStandardAssets.Utility;
using System;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    public static class KnotAccessibility
    {
        // Game references
        private static KnotGameLogic activeLogic = null;
        private static WaypointProgressTracker progressTracker = null;
        private static WaypointCircuit circuit = null;

        // State tracking
        private static bool isInitialized = false;
        private static bool challengeActive = false;
        private static float initializationStartTime = 0f;
        private static bool hasWon = false; // Permanent flag: never reinitialize after win
        private static bool justLost = false; // Temporary flag: allow retry after cooldown
        private static float lossTime = 0f;
        private static readonly float retryCooldown = 3f; // Wait 3 seconds after loss before allowing retry
        private static bool waitingForRetry = false; // Track if we're waiting for player to retry via popup

        // Rhythm challenge state
        private static float nextPromptTime = 0f;
        private static float lastPromptTime = 0f;
        private static bool waitingForInput = false;
        private static int promptCount = 0;
        private static int totalPrompts = 30; // Total prompts needed to complete
        private static int missedPrompts = 0;
        private static int maxMissedPrompts = 8; // Allow 8 misses before failure

        // Timing configuration
        private static float baseInterval = 1.8f; // Start interval (seconds)
        private static float fastInterval = 1.4f; // Fast interval (seconds)
        private static float fastestInterval = 1.0f; // Fastest interval (seconds)
        private static float timingWindow = 0.3f; // ±300ms window for success
        private static float perfectWindow = 0.1f; // ±100ms window for perfect
        private static float countdownBeepInterval = 0.2f; // 200ms between countdown beeps

        // Countdown state
        private static int countdownBeepsRemaining = 0;
        private static float nextCountdownBeepTime = 0f;

        // Audio
        private static AudioSource audioSource = null;
        private static readonly float countdownFreq = 440f; // A4
        private static readonly float promptFreq = 880f; // A5 (octave higher)
        private static readonly float perfectFreq1 = 523.25f; // C5
        private static readonly float perfectFreq2 = 659.25f; // E5
        private static readonly float goodFreq = 523.25f; // C5
        private static readonly float errorFreq = 200f; // Low rumble

        // Progress announcements
        private static int lastAnnouncedProgress = 0;

        // Auto-progress (when circuit is available)
        private static float autoProgressSpeed = 0f;
        private static float totalCircuitLength = 0f;

        // Last announcement tracking (prevent spam)
        private static string lastAnnouncement = "";
        private static float lastAnnouncementTimestamp = 0f;
        private static readonly float announcementCooldown = 1f;

        public static void HandleInput()
        {
            try
            {
                // Never reinitialize after winning (mini-game is done forever)
                if (hasWon)
                {
                    return;
                }

                // Allow retry after cooldown following a loss
                if (justLost)
                {
                    if (Time.time - lossTime < retryCooldown)
                    {
                        // Still in cooldown period after loss
                        return;
                    }
                    else
                    {
                        // Cooldown expired, now waiting for retry popup
                        justLost = false;
                        waitingForRetry = true;
                        MelonLogger.Msg("Retry cooldown expired, waiting for player to retry via popup");
                    }
                }

                // If waiting for retry, check if game has restarted
                if (waitingForRetry)
                {
                    // Check if game has restarted (isGameInProgress becomes true again)
                    if (activeLogic != null && !activeLogic.WasCollected && activeLogic.isGameInProgress)
                    {
                        MelonLogger.Msg("Player retried, game restarting");
                        waitingForRetry = false;
                        // Reset internal state for new attempt but keep activeLogic
                        isInitialized = false;
                        challengeActive = false;
                        initializationStartTime = 0f;
                        waitingForInput = false;
                        promptCount = 0;
                        missedPrompts = 0;
                        countdownBeepsRemaining = 0;
                        lastAnnouncedProgress = 0;
                        // Don't reset audioSource - reuse it
                    }
                    else
                    {
                        // Still waiting for retry, don't spam
                        return;
                    }
                }

                // Detect active knot game
                if (activeLogic == null || activeLogic.WasCollected)
                {
                    if (activeLogic != null && activeLogic.WasCollected)
                    {
                        MelonLogger.Msg("KnotGameLogic was collected, resetting...");
                    }
                    ResetState();
                    var knotLogics = UnityEngine.Object.FindObjectsOfType<KnotGameLogic>();
                    if (knotLogics != null && knotLogics.Count > 0)
                    {
                        activeLogic = knotLogics[0];
                        MelonLogger.Msg($"Found KnotGameLogic, isGameInProgress={activeLogic.isGameInProgress}");
                        // Only initialize if game is actually in progress
                        if (activeLogic.isGameInProgress)
                        {
                            InitializeKnotGame();
                        }
                        else
                        {
                            MelonLogger.Msg("Game not in progress yet, waiting...");
                        }
                    }
                    return;
                }

                // Game is no longer active (but not because of loss/retry)
                if (!activeLogic.isGameInProgress)
                {
                    // Don't spam reset if we're in a retry situation
                    if (!waitingForRetry && !justLost)
                    {
                        MelonLogger.Msg("Game is no longer in progress, resetting...");
                        ResetState();
                    }
                    return;
                }

                // Keep trying to initialize until successful
                if (!isInitialized)
                {
                    InitializeKnotGame();
                    return;
                }

                // Handle countdown beeps
                if (countdownBeepsRemaining > 0 && Time.time >= nextCountdownBeepTime)
                {
                    PlayBeep(countdownFreq, 0.08f);
                    countdownBeepsRemaining--;
                    nextCountdownBeepTime = Time.time + countdownBeepInterval;
                    return;
                }

                // Start rhythm challenge when initialized
                if (isInitialized && !challengeActive)
                {
                    StartRhythmChallenge();
                }

                // Handle rhythm challenge
                if (challengeActive)
                {
                    HandleRhythmChallenge();
                }

                // Handle help command
                if (Input.GetKeyDown(KeyCode.H))
                {
                    ShowHelp();
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Error in KnotAccessibility.HandleInput: {e.Message}");
                MelonLogger.Error(e.StackTrace);
            }
        }

        private static void InitializeKnotGame()
        {
            try
            {
                if (activeLogic == null) return;

                // First initialization attempt - start the timer
                if (initializationStartTime == 0f)
                {
                    initializationStartTime = Time.time;
                    MelonLogger.Msg("Starting knot game initialization...");
                }

                // Get WaypointProgressTracker directly from KnotGameLogic
                if (progressTracker == null)
                {
                    progressTracker = activeLogic.wp;
                    if (progressTracker == null)
                    {
                        MelonLogger.Msg("WaypointProgressTracker not found on KnotGameLogic - going standalone");
                        InitializeStandalone();
                        return;
                    }
                    MelonLogger.Msg("WaypointProgressTracker found!");
                }

                // Get WaypointCircuit
                if (circuit == null)
                {
                    circuit = progressTracker.circuit;
                    if (circuit == null)
                    {
                        MelonLogger.Msg("WaypointCircuit not found - going standalone");
                        InitializeStandalone();
                        return;
                    }
                    MelonLogger.Msg("WaypointCircuit found!");
                }


                // Use fixed speed based on waypoint count instead of circuit.Length
                // This avoids dependency on circuit.Length which may not initialize properly
                if (circuit.waypointList?.items != null && circuit.waypointList.items.Length > 0)
                {
                    int waypointCount = circuit.waypointList.items.Length;

                    // Estimate distance per waypoint (reasonable for unit-scale waypoints in this game)
                    float estimatedDistancePerWaypoint = 0.5f;

                    // Calculate estimated circuit length from waypoint count
                    totalCircuitLength = waypointCount * estimatedDistancePerWaypoint;

                    // Set circuit.Length so the game recognizes completion
                    circuit.Length = totalCircuitLength;

                    // Calculate speed needed to traverse circuit during challenge
                    float estimatedTime = totalPrompts * baseInterval;
                    autoProgressSpeed = totalCircuitLength / estimatedTime;

                    MelonLogger.Msg($"Circuit initialized: {waypointCount} waypoints, estimated length {totalCircuitLength:F1}, speed {autoProgressSpeed:F3}");
                }
                else
                {
                    MelonLogger.Warning("No waypoints found in circuit - going to standalone mode");
                    InitializeStandalone();
                    return;
                }

                // Get or create AudioSource
                audioSource = activeLogic.gameObject.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = activeLogic.gameObject.AddComponent<AudioSource>();
                }
                audioSource.volume = 0.5f;
                audioSource.spatialBlend = 0f; // 2D sound

                isInitialized = true;

                // Announce start
                Speak("Knot mini-game detected. Rhythm challenge with progress tracking activated. Press H for help.", true);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Error initializing knot game: {e.Message}");
                MelonLogger.Error(e.StackTrace);
            }
        }

        private static void InitializeStandalone()
        {
            try
            {
                if (activeLogic == null)
                {
                    MelonLogger.Error("Cannot initialize standalone - activeLogic is null");
                    return;
                }

                MelonLogger.Msg("Initializing standalone mode...");

                // Get or create AudioSource
                audioSource = activeLogic.gameObject.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = activeLogic.gameObject.AddComponent<AudioSource>();
                    MelonLogger.Msg("Created new AudioSource");
                }
                audioSource.volume = 0.5f;
                audioSource.spatialBlend = 0f; // 2D sound

                isInitialized = true;
                MelonLogger.Msg("Standalone initialization complete, isInitialized set to true");

                // Announce start
                Speak("Knot mini-game detected. Standalone rhythm challenge activated. Press H for help.", true);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Error in standalone initialization: {e.Message}");
                MelonLogger.Error(e.StackTrace);
            }
        }

        private static void StartRhythmChallenge()
        {
            try
            {
                MelonLogger.Msg("Starting rhythm challenge...");
                challengeActive = true;
                promptCount = 0;
                missedPrompts = 0;
                lastAnnouncedProgress = 0;

                // Start first prompt sequence
                ScheduleNextPrompt();

                MelonLogger.Msg("Challenge started, first prompt scheduled");
                Speak("Challenge started! Listen for the beeps and press Space or A button on the high-pitched beep.", true);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Error starting rhythm challenge: {e.Message}");
                MelonLogger.Error(e.StackTrace);
            }
        }

        private static void HandleRhythmChallenge()
        {
            try
            {
                // Auto-progress along the circuit if available
                if (progressTracker != null && totalCircuitLength > 0)
                {
                    progressTracker.progressDistance += autoProgressSpeed * Time.deltaTime;
                }

                // Check for completion based on prompt count
                if (promptCount >= totalPrompts)
                {
                    // Victory!
                    challengeActive = false;
                    hasWon = true; // Permanent flag - never retry after win

                    // Ensure progressDistance reaches circuit.Length for game to recognize completion
                    if (progressTracker != null && circuit != null)
                    {
                        progressTracker.progressDistance = circuit.Length;
                    }

                    Speak($"Rhythm challenge complete! You made {promptCount} successful prompts with {missedPrompts} misses.", true);

                    // Complete the mini-game using multiple methods
                    // (Win() alone doesn't set isGameInProgress=false, we need all three steps)
                    activeLogic.Win();
                    activeLogic.isGameInProgress = false;
                    activeLogic.EndMinigame();

                    MelonLogger.Msg("Knot mini-game completed successfully");
                    return;
                }

                // Handle countdown beeps (already handled in main loop)
                if (countdownBeepsRemaining > 0)
                {
                    return;
                }

                // Time for prompt
                if (!waitingForInput && Time.time >= nextPromptTime)
                {
                    // Play prompt beep
                    PlayBeep(promptFreq, 0.12f);
                    waitingForInput = true;
                    lastPromptTime = Time.time;
                }

                // Check for input during prompt window
                if (waitingForInput)
                {
                    bool pressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0);

                    if (pressed)
                    {
                        float responseTime = Time.time - lastPromptTime;

                        if (responseTime <= timingWindow)
                        {
                            // Success!
                            promptCount++;

                            // Check for progress milestones
                            AnnounceProgress();

                            // Determine quality
                            if (responseTime <= perfectWindow)
                            {
                                // Perfect hit - two-tone ascending
                                PlayTwoToneBeep(perfectFreq1, perfectFreq2, 0.15f);
                            }
                            else
                            {
                                // Good hit - single tone
                                PlayBeep(goodFreq, 0.12f);
                            }

                            waitingForInput = false;
                            ScheduleNextPrompt();
                        }
                    }

                    // Check for timeout (missed prompt)
                    if (Time.time - lastPromptTime > timingWindow * 2)
                    {
                        // Missed prompt
                        missedPrompts++;
                        PlayBeep(errorFreq, 0.2f);

                        Speak($"Miss! Total misses: {missedPrompts}.", false);

                        // Check for failure
                        if (missedPrompts >= maxMissedPrompts)
                        {
                            challengeActive = false;
                            justLost = true; // Temporary flag - allow retry after cooldown
                            lossTime = Time.time;
                            Speak($"Too many missed prompts. Game over.", true);
                            MelonLogger.Msg("Calling Loose() - entering retry cooldown");
                            activeLogic.Loose();
                            return;
                        }

                        waitingForInput = false;
                        ScheduleNextPrompt();
                    }
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Error in HandleRhythmChallenge: {e.Message}");
            }
        }

        private static void ScheduleNextPrompt()
        {
            // Calculate next interval based on progress
            float progressPercent = (float)promptCount / totalPrompts;
            float currentInterval;

            if (progressPercent < 0.33f)
            {
                currentInterval = baseInterval;
            }
            else if (progressPercent < 0.66f)
            {
                currentInterval = fastInterval;
            }
            else
            {
                currentInterval = fastestInterval;
            }

            // Calculate countdown timing - beeps should end just before the prompt
            float countdownDuration = (3 * countdownBeepInterval); // ~0.6s for 3 beeps

            // Schedule prompt after the full interval
            nextPromptTime = Time.time + currentInterval;

            // Schedule countdown beeps to finish just before the prompt
            // Start countdown beeps so they end ~0.15s before the prompt
            float countdownStartDelay = currentInterval - countdownDuration - 0.15f;
            if (countdownStartDelay < 0.1f) countdownStartDelay = 0.1f; // Minimum delay

            countdownBeepsRemaining = 3;
            nextCountdownBeepTime = Time.time + countdownStartDelay;
        }

        private static void AnnounceProgress()
        {
            float progressPercent = ((float)promptCount / totalPrompts) * 100f;
            int progressInt = (int)progressPercent;

            // Announce at 25%, 50%, 75%
            if (progressInt >= 25 && lastAnnouncedProgress < 25)
            {
                lastAnnouncedProgress = 25;
                Speak("25 percent complete.", false);
            }
            else if (progressInt >= 50 && lastAnnouncedProgress < 50)
            {
                lastAnnouncedProgress = 50;
                Speak("50 percent complete.", false);
            }
            else if (progressInt >= 75 && lastAnnouncedProgress < 75)
            {
                lastAnnouncedProgress = 75;
                Speak("75 percent complete.", false);
            }
        }

        private static void PlayBeep(float frequency, float duration)
        {
            try
            {
                if (audioSource == null) return;

                AudioClip beep = CreateBeep(frequency, duration);
                audioSource.PlayOneShot(beep);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Error playing beep: {e.Message}");
            }
        }

        private static void PlayTwoToneBeep(float freq1, float freq2, float duration)
        {
            try
            {
                if (audioSource == null) return;

                AudioClip beep = CreateTwoToneBeep(freq1, freq2, duration);
                audioSource.PlayOneShot(beep);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error($"Error playing two-tone beep: {e.Message}");
            }
        }

        private static AudioClip CreateBeep(float frequency, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.FloorToInt(sampleRate * duration);
            AudioClip clip = AudioClip.Create("Beep", sampleCount, 1, sampleRate, false);

            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = 1f;

                // Fade in (first 5%)
                if (i < sampleCount * 0.05f)
                {
                    envelope = (float)i / (sampleCount * 0.05f);
                }
                // Fade out (last 20%)
                else if (i > sampleCount * 0.8f)
                {
                    envelope = 1f - ((float)(i - sampleCount * 0.8f) / (sampleCount * 0.2f));
                }

                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            }

            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateTwoToneBeep(float freq1, float freq2, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.FloorToInt(sampleRate * duration);
            AudioClip clip = AudioClip.Create("TwoToneBeep", sampleCount, 1, sampleRate, false);

            float[] samples = new float[sampleCount];
            int halfSample = sampleCount / 2;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float frequency = (i < halfSample) ? freq1 : freq2;
                float envelope = 1f;

                // Fade in (first 5%)
                if (i < sampleCount * 0.05f)
                {
                    envelope = (float)i / (sampleCount * 0.05f);
                }
                // Fade out (last 20%)
                else if (i > sampleCount * 0.8f)
                {
                    envelope = 1f - ((float)(i - sampleCount * 0.8f) / (sampleCount * 0.2f));
                }

                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            }

            clip.SetData(samples, 0);
            return clip;
        }

        private static void ShowHelp()
        {
            string help = "Knot mini-game accessibility. " +
                         "Listen for three countdown beeps followed by a high-pitched prompt beep. " +
                         "Press Space or A button when you hear the prompt beep. " +
                         $"You can miss up to {maxMissedPrompts} prompts. " +
                         "Perfect timing gives a two-tone sound, good timing gives a single tone, " +
                         "and misses give a low rumble.";
            Speak(help, true);
        }

        private static void Speak(string text, bool interrupt)
        {
            // Prevent duplicate announcements
            if (text == lastAnnouncement && Time.time - lastAnnouncementTimestamp < announcementCooldown)
            {
                return;
            }

            lastAnnouncement = text;
            lastAnnouncementTimestamp = Time.time;
            AccessibilityMod.Speak(text, interrupt);
        }

        private static void ResetState()
        {
            activeLogic = null;
            progressTracker = null;
            circuit = null;
            isInitialized = false;
            challengeActive = false;
            initializationStartTime = 0f;
            waitingForInput = false;
            promptCount = 0;
            missedPrompts = 0;
            countdownBeepsRemaining = 0;
            lastAnnouncedProgress = 0;
            autoProgressSpeed = 0f;
            totalCircuitLength = 0f;
            audioSource = null;
            waitingForRetry = false;
        }
    }
}
