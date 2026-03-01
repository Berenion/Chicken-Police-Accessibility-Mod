using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using MelonLoader;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Rhythm-based accessibility system for the zipper minigame.
    /// Replaces mouse precision dragging with timing-based button presses.
    /// Uses audio beeps for rhythm cues (TTS is too slow for timing).
    /// </summary>
    public static class ZipperAccessibility
    {
        // Rhythm parameters (aligned with knot minigame for consistency)
        private const int TOTAL_PULLS = 25;
        private const float TIMING_WINDOW = 0.3f; // ±300ms (matches knot)
        private const float PERFECT_WINDOW = 0.1f; // ±100ms for perfect hits (matches knot)

        // Difficulty curve intervals (seconds between pulls)
        private const float EASY_INTERVAL = 1.98f;   // 0-33%
        private const float NORMAL_INTERVAL = 1.21f; // 33-66%
        private const float HARD_INTERVAL = 0.715f;  // 66-100%

        // Error tracking
        private const float MAX_ERROR_TIME = 7.0f;
        private const float EARLY_LATE_PENALTY = 0.5f;
        private const float TIMEOUT_PENALTY = 1.0f;

        // Sound frequencies (Hz) - same as test program
        private const float BEEP_COUNTDOWN = 440f;  // A4 note
        private const float BEEP_PROMPT = 880f;     // A5 note (octave higher)
        private const float BEEP_SUCCESS = 523f;    // C5 note
        private const float BEEP_PERFECT = 659f;    // E5 note
        private const float BEEP_ERROR = 200f;      // Low rumble

        // Sound durations (seconds)
        private const float BEEP_SHORT = 0.08f;  // 80ms
        private const float BEEP_LONG = 0.15f;   // 150ms

        // Countdown timing (matches knot minigame)
        private const float COUNTDOWN_BEEP_INTERVAL = 0.2f; // 200ms between countdown beeps (matches knot)
        private const int COUNTDOWN_BEEP_COUNT = 3;

        // Audio system
        private static AudioSource audioSource = null;
        private static GameObject audioObject = null;

        // State tracking
        private static ZipperLogic activeZipper = null;
        private static bool isRhythmActive = false;
        private static int successfulPulls = 0;
        private static int totalAttempts = 0;
        private static float currentErrorTime = 0f;
        private static int currentDifficulty = 0;

        // Non-blocking timing (using Time.time like knot minigame)
        private static float nextPullTime = 0f;
        private static float promptTime = 0f;
        private static bool waitingForInput = false;
        private static bool inputReceived = false;

        // Non-blocking countdown state
        private static int countdownBeepsRemaining = 0;
        private static float nextCountdownBeepTime = 0f;
        private static bool countdownInProgress = false;

        public static void HandleInput()
        {
            try
            {
                // Detect zipper minigame activation
                if (activeZipper == null)
                {
                    var zippers = UnityEngine.Object.FindObjectsOfType<ZipperLogic>();
                    if (zippers != null && zippers.Count > 0)
                    {
                        activeZipper = zippers[0];
                        StartRhythmChallenge();
                    }
                    return;
                }

                // Check if zipper was closed/completed
                if (activeZipper == null || !activeZipper.gameObject.activeInHierarchy)
                {
                    ResetState();
                    return;
                }

                // Handle rhythm challenge
                if (isRhythmActive)
                {
                    UpdateRhythmChallenge();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in ZipperAccessibility.HandleInput: {ex.Message}");
            }
        }

        private static void StartRhythmChallenge()
        {
            isRhythmActive = true;
            successfulPulls = 0;
            totalAttempts = 0;
            currentErrorTime = 0f;
            currentDifficulty = 0;

            // Initialize audio system
            InitializeAudio();

            AccessibilityMod.Speak("Zipper minigame detected. Rhythm challenge started.", true);
            AccessibilityMod.Speak($"Complete {TOTAL_PULLS} pulls by pressing Space or A button on the high-pitched beep.", false);

            // Schedule first pull sequence
            ScheduleNextPull();

            MelonLogger.Msg("Zipper rhythm challenge started");
        }

        private static void UpdateRhythmChallenge()
        {
            // Check win/fail conditions
            if (successfulPulls >= TOTAL_PULLS)
            {
                WinChallenge();
                return;
            }

            if (currentErrorTime >= MAX_ERROR_TIME)
            {
                FailChallenge();
                return;
            }

            // Handle countdown beeps (non-blocking, processed each frame)
            if (countdownBeepsRemaining > 0 && Time.time >= nextCountdownBeepTime)
            {
                PlayBeep(BEEP_COUNTDOWN, BEEP_SHORT);
                countdownBeepsRemaining--;
                nextCountdownBeepTime = Time.time + COUNTDOWN_BEEP_INTERVAL;
                return;
            }

            // Time for prompt beep (countdown finished, prompt time reached)
            if (countdownInProgress && countdownBeepsRemaining == 0 && !waitingForInput && Time.time >= nextPullTime)
            {
                PlayBeep(BEEP_PROMPT, BEEP_LONG);
                waitingForInput = true;
                inputReceived = false;
                promptTime = Time.time;
                countdownInProgress = false;
                totalAttempts++;
            }

            // Check for input during prompt window
            if (waitingForInput)
            {
                CheckForInput();
            }
        }

        private static void ScheduleNextPull()
        {
            float progress = (float)successfulPulls / TOTAL_PULLS;
            float interval = GetPullInterval(progress);

            // Calculate countdown timing - beeps should end just before the prompt
            float countdownDuration = COUNTDOWN_BEEP_COUNT * COUNTDOWN_BEEP_INTERVAL;

            // Schedule prompt after the full interval
            nextPullTime = Time.time + interval;

            // Schedule countdown beeps to finish just before the prompt
            float countdownStartDelay = interval - countdownDuration - 0.15f;
            if (countdownStartDelay < 0.1f) countdownStartDelay = 0.1f;

            countdownBeepsRemaining = COUNTDOWN_BEEP_COUNT;
            nextCountdownBeepTime = Time.time + countdownStartDelay;
            countdownInProgress = true;
        }

        private static void CheckForInput()
        {
            bool actionPressed = IsActionButtonPressed();

            if (actionPressed && !inputReceived)
            {
                inputReceived = true;
                float timingDifference = Time.time - promptTime;
                EvaluateTiming(timingDifference);

                // Schedule next pull
                waitingForInput = false;
                ScheduleNextPull();
            }
            else if (!inputReceived && Time.time - promptTime > TIMING_WINDOW * 2)
            {
                // Timeout
                HandleTimeout();

                waitingForInput = false;
                ScheduleNextPull();
            }
        }

        private static bool IsActionButtonPressed()
        {
            return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0);
        }

        private static void EvaluateTiming(float difference)
        {
            float absDifference = Math.Abs(difference);

            if (absDifference <= TIMING_WINDOW)
            {
                // SUCCESS
                successfulPulls++;

                // Update zipper progress
                float progress = (float)successfulPulls / TOTAL_PULLS;
                if (activeZipper != null)
                {
                    activeZipper.SetProgress(progress);
                }

                if (absDifference <= PERFECT_WINDOW)
                {
                    // Perfect hit - two-tone ascending sound
                    PlayPerfectSound();
                }
                else
                {
                    // Good hit - single positive tone
                    PlaySuccessSound();
                }

                // Announce progress milestones (TTS is okay for non-timing-critical info)
                if (successfulPulls % 5 == 0)
                {
                    int percentage = (int)(progress * 100);
                    AccessibilityMod.Speak($"{percentage}% complete, {TOTAL_PULLS - successfulPulls} pulls remaining", false);
                }
            }
            else
            {
                // Too late - error sound
                currentErrorTime += EARLY_LATE_PENALTY;
                PlayErrorSound();
                ShowErrorWarning();
            }
        }

        private static void HandleTimeout()
        {
            currentErrorTime += TIMEOUT_PENALTY;
            PlayTimeoutSound();
            ShowErrorWarning();
        }

        private static void ShowErrorWarning()
        {
            float errorsLeft = MAX_ERROR_TIME - currentErrorTime;
            if (errorsLeft <= 2.0f)
            {
                AccessibilityMod.Speak($"Warning: Only {errorsLeft:F1} seconds of errors remaining", false);
            }
        }

        private static float GetPullInterval(float progress)
        {
            int newDifficulty;
            float interval;

            if (progress < 0.33f)
            {
                newDifficulty = 0;
                interval = EASY_INTERVAL;
            }
            else if (progress < 0.66f)
            {
                newDifficulty = 1;
                interval = NORMAL_INTERVAL;
            }
            else
            {
                newDifficulty = 2;
                interval = HARD_INTERVAL;
            }

            // Announce difficulty change
            if (newDifficulty != currentDifficulty && currentDifficulty < newDifficulty)
            {
                PlayDifficultyChangeSound();

                if (newDifficulty == 1)
                {
                    AccessibilityMod.Speak("Pace increasing!", false);
                }
                else if (newDifficulty == 2)
                {
                    AccessibilityMod.Speak("Maximum intensity!", false);
                }

                currentDifficulty = newDifficulty;
            }

            return interval;
        }

        private static void WinChallenge()
        {
            // Play victory fanfare!
            PlayVictoryFanfare();
            AccessibilityMod.Speak("Zipper complete! You win!", true);

            if (activeZipper != null)
            {
                activeZipper.Win();
            }

            ResetState();
        }

        private static void FailChallenge()
        {
            // Play failure sound (long low beep)
            PlayBeep(BEEP_ERROR, 0.5f);
            AccessibilityMod.Speak("Zipper failed. Too many errors.", true);

            if (activeZipper != null)
            {
                activeZipper.Loose();
            }

            ResetState();
        }

        private static void ResetState()
        {
            isRhythmActive = false;
            activeZipper = null;
            successfulPulls = 0;
            totalAttempts = 0;
            currentErrorTime = 0f;
            currentDifficulty = 0;
            waitingForInput = false;
            inputReceived = false;
            countdownBeepsRemaining = 0;
            countdownInProgress = false;
        }

        // Audio system methods

        private static void InitializeAudio()
        {
            if (audioObject == null)
            {
                audioObject = new GameObject("ZipperAccessibilityAudio");
                UnityEngine.Object.DontDestroyOnLoad(audioObject);
                audioSource = audioObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.volume = 0.5f;
                MelonLogger.Msg("Zipper accessibility audio system initialized");
            }
        }

        private static void PlayBeep(float frequency, float duration)
        {
            if (audioSource == null)
            {
                InitializeAudio();
            }

            try
            {
                int sampleRate = 44100;
                int sampleCount = (int)(sampleRate * duration);

                AudioClip clip = AudioClip.Create("Beep", sampleCount, 1, sampleRate, false);

                float[] samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    // Generate sine wave
                    samples[i] = Mathf.Sin(2.0f * Mathf.PI * frequency * i / sampleRate);

                    // Apply envelope to avoid clicks (fade in/out)
                    float envelope = 1.0f;
                    if (i < sampleRate * 0.01f) // 10ms fade in
                    {
                        envelope = i / (sampleRate * 0.01f);
                    }
                    else if (i > sampleCount - sampleRate * 0.01f) // 10ms fade out
                    {
                        envelope = (sampleCount - i) / (sampleRate * 0.01f);
                    }
                    samples[i] *= envelope * 0.5f;
                }

                clip.SetData(samples, 0);
                audioSource.PlayOneShot(clip);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error playing beep: {ex.Message}");
            }
        }

        private static void PlaySuccessSound()
        {
            PlayBeep(BEEP_SUCCESS, BEEP_LONG);
        }

        private static void PlayPerfectSound()
        {
            // Two-tone ascending sound (played as single combined clip)
            try
            {
                if (audioSource == null) return;

                int sampleRate = 44100;
                float duration = 0.16f;
                int sampleCount = (int)(sampleRate * duration);
                int halfSample = sampleCount / 2;

                AudioClip clip = AudioClip.Create("PerfectBeep", sampleCount, 1, sampleRate, false);
                float[] samples = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    float frequency = (i < halfSample) ? BEEP_SUCCESS : BEEP_PERFECT;
                    float envelope = 1.0f;

                    if (i < sampleRate * 0.01f)
                        envelope = i / (sampleRate * 0.01f);
                    else if (i > sampleCount - sampleRate * 0.01f)
                        envelope = (sampleCount - i) / (sampleRate * 0.01f);

                    samples[i] = Mathf.Sin(2.0f * Mathf.PI * frequency * i / sampleRate) * envelope * 0.5f;
                }

                clip.SetData(samples, 0);
                audioSource.PlayOneShot(clip);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error playing perfect sound: {ex.Message}");
            }
        }

        private static void PlayErrorSound()
        {
            PlayBeep(BEEP_ERROR, BEEP_LONG);
        }

        private static void PlayTimeoutSound()
        {
            // Double error beep as single clip
            try
            {
                if (audioSource == null) return;

                int sampleRate = 44100;
                float beepDur = BEEP_SHORT;
                float gapDur = 0.05f;
                float totalDur = beepDur + gapDur + beepDur;
                int sampleCount = (int)(sampleRate * totalDur);
                int beepSamples = (int)(sampleRate * beepDur);
                int gapSamples = (int)(sampleRate * gapDur);

                AudioClip clip = AudioClip.Create("TimeoutBeep", sampleCount, 1, sampleRate, false);
                float[] samples = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    bool inGap = (i >= beepSamples && i < beepSamples + gapSamples);
                    if (inGap)
                    {
                        samples[i] = 0f;
                    }
                    else
                    {
                        samples[i] = Mathf.Sin(2.0f * Mathf.PI * BEEP_ERROR * i / sampleRate) * 0.5f;
                    }
                }

                clip.SetData(samples, 0);
                audioSource.PlayOneShot(clip);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error playing timeout sound: {ex.Message}");
            }
        }

        private static void PlayVictoryFanfare()
        {
            // C-E-G-C ascending as single clip
            try
            {
                if (audioSource == null) return;

                int sampleRate = 44100;
                float noteDur = 0.15f;
                float lastNoteDur = 0.30f;
                float gapDur = 0.05f;
                float totalDur = noteDur * 3 + gapDur * 3 + lastNoteDur;
                int sampleCount = (int)(sampleRate * totalDur);

                float[] freqs = { 523f, 659f, 784f, 1047f };
                float[] durations = { noteDur, noteDur, noteDur, lastNoteDur };

                AudioClip clip = AudioClip.Create("VictoryFanfare", sampleCount, 1, sampleRate, false);
                float[] samples = new float[sampleCount];

                int pos = 0;
                for (int n = 0; n < 4; n++)
                {
                    int noteSamples = (int)(sampleRate * durations[n]);
                    for (int i = 0; i < noteSamples && pos < sampleCount; i++, pos++)
                    {
                        float envelope = 1.0f;
                        if (i < sampleRate * 0.01f)
                            envelope = i / (sampleRate * 0.01f);
                        else if (i > noteSamples - sampleRate * 0.01f)
                            envelope = (noteSamples - i) / (sampleRate * 0.01f);
                        samples[pos] = Mathf.Sin(2.0f * Mathf.PI * freqs[n] * i / sampleRate) * envelope * 0.5f;
                    }
                    int gapSamps = (int)(sampleRate * gapDur);
                    for (int i = 0; i < gapSamps && pos < sampleCount; i++, pos++)
                    {
                        samples[pos] = 0f;
                    }
                }

                clip.SetData(samples, 0);
                audioSource.PlayOneShot(clip);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error playing victory fanfare: {ex.Message}");
            }
        }

        private static void PlayDifficultyChangeSound()
        {
            // Ascending warning tones as single clip
            try
            {
                if (audioSource == null) return;

                int sampleRate = 44100;
                float noteDur = 0.10f;
                float lastDur = 0.15f;
                float gapDur = 0.03f;
                float totalDur = noteDur * 2 + gapDur * 2 + lastDur;
                int sampleCount = (int)(sampleRate * totalDur);

                float[] freqs = { 440f, 554f, 659f };
                float[] durations = { noteDur, noteDur, lastDur };

                AudioClip clip = AudioClip.Create("DifficultyChange", sampleCount, 1, sampleRate, false);
                float[] samples = new float[sampleCount];

                int pos = 0;
                for (int n = 0; n < 3; n++)
                {
                    int noteSamples = (int)(sampleRate * durations[n]);
                    for (int i = 0; i < noteSamples && pos < sampleCount; i++, pos++)
                    {
                        float envelope = 1.0f;
                        if (i < sampleRate * 0.005f)
                            envelope = i / (sampleRate * 0.005f);
                        else if (i > noteSamples - sampleRate * 0.005f)
                            envelope = (noteSamples - i) / (sampleRate * 0.005f);
                        samples[pos] = Mathf.Sin(2.0f * Mathf.PI * freqs[n] * i / sampleRate) * envelope * 0.5f;
                    }
                    int gapSamps = (int)(sampleRate * gapDur);
                    for (int i = 0; i < gapSamps && pos < sampleCount; i++, pos++)
                    {
                        samples[pos] = 0f;
                    }
                }

                clip.SetData(samples, 0);
                audioSource.PlayOneShot(clip);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error playing difficulty change sound: {ex.Message}");
            }
        }
    }
}
