using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;
using HarmonyLib;
using Il2Cpp;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Provides accessible shooting range gameplay.
    /// Replaces visual aiming with number-based target selection (0-9).
    /// Uses pre-recorded voice files for distinct enemy vs civilian announcements.
    /// Uses game's native gun for ammo tracking and sound effects.
    /// </summary>
    public static class ShootingRangeAccessibility
    {
        // Scoring constants
        private const int ENEMY_HIT_POINTS = 130;
        private const int CIVILIAN_HIT_PENALTY = -500;

        // Audio file paths - use game's parent directory + UserData
        private static string _audioBasePath = null;
        private static string AUDIO_BASE_PATH
        {
            get
            {
                if (_audioBasePath == null)
                {
                    // Get game's data path and go up to game folder, then to UserData
                    string gameDataPath = Application.dataPath;
                    string gameFolder = Path.GetDirectoryName(gameDataPath);
                    _audioBasePath = Path.Combine(gameFolder, "UserData", "ShootingRangeAudio");
                }
                return _audioBasePath;
            }
        }

        // Target tracking
        private class TargetInfo
        {
            public int assignedNumber;      // 0-9
            public bool isEnemy;            // true = enemy, false = civilian
            public float deadline;          // Time.time when target closes
            public bool hasBeenShot;        // Prevent double-scoring
        }

        // State variables
        private static ShootingRangeGameLogic cachedGameLogic = null;
        private static Dictionary<int, TargetInfo> activeTargets = new Dictionary<int, TargetInfo>();
        private static int score = 0;
        private static int enemiesHit = 0;
        private static int civiliansHit = 0;
        private static bool isGameActive = false;
        private static System.Random random = new System.Random();

        // Audio system
        private static GameObject audioObject = null;
        private static AudioSource audioSource = null;
        private static Dictionary<string, AudioClip> audioCache = new Dictionary<string, AudioClip>();
        private static bool audioInitialized = false;

        /// <summary>
        /// Main input handler called from AccessibilityMod.OnUpdate()
        /// </summary>
        public static void HandleInput()
        {
            try
            {
                // Find active shooting range
                var gameLogics = UnityEngine.Object.FindObjectsOfType<ShootingRangeGameLogic>();

                if (gameLogics == null || gameLogics.Length == 0)
                {
                    // Clean up when leaving shooting range
                    if (cachedGameLogic != null)
                    {
                        ResetGameState();
                    }
                    return;
                }

                var gc = gameLogics[0];
                cachedGameLogic = gc;

                // Always clean up expired targets when we have active targets
                if (activeTargets.Count > 0)
                {
                    CleanupExpiredTargets();
                }

                // Check if game is in progress - use our own flag OR the game's flag
                // Our flag gets set when OnTargetOpen is called (via Harmony patch)
                bool gameInProgress = isGameActive || (gc.targets != null && gc.targets.isGameInProgress);

                if (gameInProgress && gc.gun != null)
                {
                    // Handle gameplay input
                    HandleGameplayInput(gc);
                }
                else if (isGameActive && activeTargets.Count == 0)
                {
                    // Game ended - no more active targets and game flag is false
                    EndGame();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ShootingRange] Error in HandleInput: {ex.Message}");
            }
        }

        private static void HandleGameplayInput(ShootingRangeGameLogic gc)
        {
            // Number keys 0-9 to shoot targets
            for (int i = 0; i <= 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                {
                    ShootTarget(i, gc);
                }
            }

            // R key to reload
            if (Input.GetKeyDown(KeyCode.R))
            {
                ReloadGun(gc);
            }

            // H key for help
            if (Input.GetKeyDown(KeyCode.H))
            {
                AnnounceHelp();
            }

            // S key for score
            if (Input.GetKeyDown(KeyCode.S))
            {
                AnnounceScore();
            }

            // A key for ammo
            if (Input.GetKeyDown(KeyCode.A))
            {
                AnnounceAmmo(gc);
            }

            // L key to list active targets
            if (Input.GetKeyDown(KeyCode.L))
            {
                ListActiveTargets();
            }
        }

        private static void ShootTarget(int number, ShootingRangeGameLogic gc)
        {
            MelonLogger.Msg($"[ShootingRange] ShootTarget called for number {number}");

            var gun = gc.gun;
            if (gun == null)
            {
                MelonLogger.Warning("[ShootingRange] Gun is null!");
                return;
            }

            MelonLogger.Msg($"[ShootingRange] Gun ammo: {gun.roundActual}/{gun.roundSize}");

            // Check ammo using game's native tracking
            if (gun.roundActual <= 0)
            {
                // Play empty click sound
                if (gun.soundClick != null)
                {
                    gun.soundClick.Play();
                }
                AccessibilityMod.Speak("Out of ammo. Press R to reload", true);
                return;
            }

            // Decrement ammo and play gun sound (game's native)
            gun.roundActual--;
            if (gun.soundShoot != null)
            {
                gun.soundShoot.Play();
                MelonLogger.Msg("[ShootingRange] Gun sound played");
            }

            // Check if target exists with this number
            if (!activeTargets.TryGetValue(number, out var info))
            {
                // Shot fired but no target with this number - just a miss
                return;
            }

            // Check if already shot
            if (info.hasBeenShot)
                return;

            // Check timing - was the shot in time?
            bool inTime = Time.time <= info.deadline;

            if (inTime)
            {
                info.hasBeenShot = true;
                if (info.isEnemy)
                {
                    score += ENEMY_HIT_POINTS;
                    enemiesHit++;
                    SyncScoreToGame();
                    AccessibilityMod.Speak($"Hit! {score}", true);
                    MelonLogger.Msg($"[ShootingRange] Enemy hit! +{ENEMY_HIT_POINTS}. Total: {score}");
                }
                else
                {
                    score += CIVILIAN_HIT_PENALTY;
                    civiliansHit++;
                    SyncScoreToGame();
                    AccessibilityMod.Speak($"Civilian! Penalty! {score}", true);
                    MelonLogger.Msg($"[ShootingRange] Civilian hit! {CIVILIAN_HIT_PENALTY}. Total: {score}");
                }
            }
            // Late shots: gun fires (ammo decremented, sound played) but no score effect

            // Remove from active targets
            activeTargets.Remove(number);
        }

        private static void ReloadGun(ShootingRangeGameLogic gc)
        {
            if (gc.gun == null) return;

            // Use game's native reload method
            gc.gun.Reload();
            AccessibilityMod.Speak("Reloading", true);
            MelonLogger.Msg("[ShootingRange] Reloading");
        }

        private static void AnnounceHelp()
        {
            string help = "Shooting range controls. " +
                          "Press 0 through 9 to shoot numbered targets. " +
                          "R to reload. " +
                          "S for score. " +
                          "A for ammo. " +
                          "L to list active targets.";
            AccessibilityMod.Speak(help, true);
        }

        private static void AnnounceScore()
        {
            string scoreInfo = $"Score: {score}. " +
                               $"Enemies hit: {enemiesHit}. " +
                               $"Civilians shot: {civiliansHit}.";
            AccessibilityMod.Speak(scoreInfo, true);
        }

        private static void AnnounceAmmo(ShootingRangeGameLogic gc)
        {
            if (gc.gun != null)
            {
                string ammoInfo = $"{gc.gun.roundActual} out of {gc.gun.roundSize} rounds";
                AccessibilityMod.Speak(ammoInfo, true);
            }
        }

        private static void ListActiveTargets()
        {
            if (activeTargets.Count == 0)
            {
                AccessibilityMod.Speak("No active targets", true);
                return;
            }

            var targetList = string.Join(". ", activeTargets.OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}, {(kvp.Value.isEnemy ? "enemy" : "civilian")}"));

            AccessibilityMod.Speak($"{activeTargets.Count} active targets. {targetList}", true);
        }

        private static void CleanupExpiredTargets()
        {
            // Remove targets whose deadline has passed
            var toRemove = new List<int>();
            float currentTime = Time.time;

            foreach (var kvp in activeTargets)
            {
                if (currentTime > kvp.Value.deadline)
                {
                    toRemove.Add(kvp.Key);
                    MelonLogger.Msg($"[ShootingRange] Target #{kvp.Key} expired (deadline {kvp.Value.deadline:F2}, now {currentTime:F2})");
                }
            }

            foreach (var key in toRemove)
            {
                activeTargets.Remove(key);
            }
        }

        /// <summary>
        /// Called from Harmony patch when a target opens
        /// </summary>
        public static void OnTargetOpen(bool isEnemy, float deadline)
        {
            try
            {
                // Mark game as active when first target opens (fallback if StartRound patch doesn't fire)
                if (!isGameActive)
                {
                    StartGame();
                }

                // Initialize audio if needed
                if (!audioInitialized)
                {
                    InitializeAudio();
                }

                // Assign a random number 0-9 that's not currently in use
                int assignedNumber = GetAvailableNumber();

                if (assignedNumber == -1)
                {
                    MelonLogger.Warning("[ShootingRange] No available numbers for new target");
                    return;
                }

                // Create target info
                var targetInfo = new TargetInfo
                {
                    assignedNumber = assignedNumber,
                    isEnemy = isEnemy,
                    deadline = deadline,
                    hasBeenShot = false
                };

                // Add to active targets
                activeTargets[assignedNumber] = targetInfo;

                // Announce with appropriate voice
                AnnounceTarget(targetInfo);

                MelonLogger.Msg($"[ShootingRange] Target assigned: #{assignedNumber} ({(isEnemy ? "ENEMY" : "CIVILIAN")}) - Deadline: {deadline:F2}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ShootingRange] Error in OnTargetOpen: {ex.Message}");
            }
        }

        private static int GetAvailableNumber()
        {
            // Get list of numbers 0-9 that aren't in use
            var available = Enumerable.Range(0, 10).Where(n => !activeTargets.ContainsKey(n)).ToList();

            if (available.Count == 0)
                return -1;

            // Return random available number
            return available[random.Next(available.Count)];
        }

        private static void AnnounceTarget(TargetInfo info)
        {
            // Try to play pre-recorded audio file
            string filename = info.isEnemy
                ? $"enemy_{info.assignedNumber}.wav"
                : $"civilian_{info.assignedNumber}.wav";

            AudioClip clip = LoadWavFile(filename);

            if (clip != null && audioSource != null)
            {
                audioSource.PlayOneShot(clip);
            }
            else
            {
                // Fallback to TTS
                string announcement = info.isEnemy
                    ? $"{info.assignedNumber}"
                    : $"Civilian! {info.assignedNumber}";
                AccessibilityMod.Speak(announcement, true);
            }
        }

        private static void InitializeAudio()
        {
            try
            {
                // Create audio object
                audioObject = new GameObject("ShootingRangeAccessibilityAudio");
                UnityEngine.Object.DontDestroyOnLoad(audioObject);
                audioSource = audioObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.volume = 0.8f;
                audioSource.spatialBlend = 0f; // 2D audio

                audioInitialized = true;
                MelonLogger.Msg("[ShootingRange] Audio system initialized");

                // Create audio directory if it doesn't exist
                if (!Directory.Exists(AUDIO_BASE_PATH))
                {
                    Directory.CreateDirectory(AUDIO_BASE_PATH);
                    MelonLogger.Msg($"[ShootingRange] Created audio directory: {AUDIO_BASE_PATH}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ShootingRange] Failed to initialize audio: {ex.Message}");
            }
        }

        private static AudioClip LoadWavFile(string filename)
        {
            try
            {
                string fullPath = Path.Combine(AUDIO_BASE_PATH, filename);

                // Check cache first
                if (audioCache.TryGetValue(filename, out var cached))
                    return cached;

                // Check file exists
                if (!File.Exists(fullPath))
                {
                    MelonLogger.Warning($"[ShootingRange] Audio file not found: {fullPath}");
                    return null;
                }

                // Read WAV file
                byte[] wavData = File.ReadAllBytes(fullPath);

                // Validate WAV header
                if (wavData.Length < 44)
                {
                    MelonLogger.Warning($"[ShootingRange] Invalid WAV file (too small): {filename}");
                    return null;
                }

                // Check RIFF header
                if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F')
                {
                    MelonLogger.Warning($"[ShootingRange] Invalid WAV file (not RIFF): {filename}");
                    return null;
                }

                // Parse WAV header (standard PCM format)
                // Bytes 22-23: num channels (1 or 2)
                // Bytes 24-27: sample rate
                // Bytes 34-35: bits per sample (8 or 16)
                // Bytes 40-43: data chunk size
                // Data starts at byte 44

                int channels = BitConverter.ToInt16(wavData, 22);
                int sampleRate = BitConverter.ToInt32(wavData, 24);
                int bitsPerSample = BitConverter.ToInt16(wavData, 34);
                int dataSize = BitConverter.ToInt32(wavData, 40);

                // Validate parsed values
                if (channels < 1 || channels > 2)
                {
                    MelonLogger.Warning($"[ShootingRange] Invalid channel count in WAV: {channels}");
                    return null;
                }

                if (sampleRate < 8000 || sampleRate > 96000)
                {
                    MelonLogger.Warning($"[ShootingRange] Invalid sample rate in WAV: {sampleRate}");
                    return null;
                }

                // Convert to float samples
                float[] samples;
                int dataStart = 44;

                // Make sure we don't read past the file
                int availableData = wavData.Length - dataStart;
                dataSize = Math.Min(dataSize, availableData);

                if (bitsPerSample == 16)
                {
                    int sampleCount = dataSize / 2;
                    samples = new float[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        short sample = BitConverter.ToInt16(wavData, dataStart + i * 2);
                        samples[i] = sample / 32768f;
                    }
                }
                else if (bitsPerSample == 8)
                {
                    samples = new float[dataSize];
                    for (int i = 0; i < dataSize; i++)
                    {
                        samples[i] = (wavData[dataStart + i] - 128) / 128f;
                    }
                }
                else
                {
                    MelonLogger.Warning($"[ShootingRange] Unsupported bit depth: {bitsPerSample}");
                    return null;
                }

                // Create AudioClip
                int samplesPerChannel = samples.Length / channels;
                AudioClip clip = AudioClip.Create(filename, samplesPerChannel, channels, sampleRate, false);
                clip.SetData(samples, 0);

                // Cache and return
                audioCache[filename] = clip;
                MelonLogger.Msg($"[ShootingRange] Loaded audio: {filename} ({channels}ch, {sampleRate}Hz, {bitsPerSample}bit)");
                return clip;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ShootingRange] Failed to load WAV file {filename}: {ex.Message}");
                return null;
            }
        }

        private static void StartGame()
        {
            // This is a fallback - normally OnGameStart() handles this via Harmony patch
            if (isGameActive)
            {
                MelonLogger.Msg("[ShootingRange] StartGame called but game already active - skipping");
                return;
            }

            isGameActive = true;
            score = 0;
            enemiesHit = 0;
            civiliansHit = 0;
            activeTargets.Clear();

            AccessibilityMod.Speak("Shooting range started. Listen for numbers. Press H for help.", true);
            MelonLogger.Msg("[ShootingRange] Game started (fallback)");
        }

        private static void EndGame()
        {
            if (!isGameActive)
                return;

            isGameActive = false;

            // Sync our score to the game's score display
            SyncScoreToGame();

            string finalStats = $"Round ended. Final score: {score}. " +
                               $"Enemies hit: {enemiesHit}. " +
                               $"Civilians shot: {civiliansHit}.";

            AccessibilityMod.Speak(finalStats, true);
            MelonLogger.Msg($"[ShootingRange] Game ended - Score: {score}, Enemies: {enemiesHit}, Civilians: {civiliansHit}");

            activeTargets.Clear();
        }

        /// <summary>
        /// Sync our tracked score to the game's score property
        /// </summary>
        private static void SyncScoreToGame()
        {
            try
            {
                if (cachedGameLogic != null)
                {
                    cachedGameLogic.score = score;
                    MelonLogger.Msg($"[ShootingRange] Synced score to game: {score}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ShootingRange] Failed to sync score: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from Harmony patch when the game ends
        /// </summary>
        public static void OnGameEnd()
        {
            if (isGameActive)
            {
                MelonLogger.Msg("[ShootingRange] OnGameEnd called from Harmony patch");
                EndGame();
            }
        }

        /// <summary>
        /// Called from Harmony patch when the game starts (including "try again")
        /// </summary>
        public static void OnGameStart()
        {
            MelonLogger.Msg("[ShootingRange] OnGameStart called from Harmony patch - resetting state");

            // Reset all state for new round
            isGameActive = true;
            score = 0;
            enemiesHit = 0;
            civiliansHit = 0;
            activeTargets.Clear();

            AccessibilityMod.Speak("Shooting range started. Listen for numbers. Press H for help.", true);
        }

        private static void ResetGameState()
        {
            cachedGameLogic = null;
            activeTargets.Clear();
            score = 0;
            enemiesHit = 0;
            civiliansHit = 0;
            isGameActive = false;

            MelonLogger.Msg("[ShootingRange] Reset game state");
        }

    }

    /// <summary>
    /// Harmony patch to hook target opening
    /// </summary>
    [HarmonyPatch(typeof(ShootingRangeTarget), "Open")]
    public class ShootingRangeTarget_Open_Patch
    {
        static void Postfix(ShootingRangeTarget __instance, float closeTime, bool isNormalTargetParam)
        {
            // isNormalTargetParam = true means enemy target
            // closeTime = duration until target closes
            float deadline = Time.time + closeTime;
            ShootingRangeAccessibility.OnTargetOpen(isNormalTargetParam, deadline);
        }
    }

    /// <summary>
    /// Harmony patch to hook game end
    /// </summary>
    [HarmonyPatch(typeof(ShootingRangeTargetsHandler), "EndGame")]
    public class ShootingRangeTargetsHandler_EndGame_Patch
    {
        static void Prefix(ShootingRangeTargetsHandler __instance)
        {
            // Sync our score before the game ends
            ShootingRangeAccessibility.OnGameEnd();
        }
    }

    /// <summary>
    /// Harmony patch to hook round start (StartRoundOne - first round or "try again")
    /// </summary>
    [HarmonyPatch(typeof(ShootingRangeGameLogic), "StartRoundOne")]
    public class ShootingRangeGameLogic_StartRoundOne_Patch
    {
        static void Postfix(ShootingRangeGameLogic __instance)
        {
            MelonLogger.Msg("[ShootingRange] StartRoundOne called - resetting state");
            ShootingRangeAccessibility.OnGameStart();
        }
    }

    /// <summary>
    /// Harmony patch to hook round start (StartRound - any round including "try again")
    /// </summary>
    [HarmonyPatch(typeof(ShootingRangeGameLogic), "StartRound")]
    public class ShootingRangeGameLogic_StartRound_Patch
    {
        static void Postfix(ShootingRangeGameLogic __instance, int levelToStart)
        {
            MelonLogger.Msg($"[ShootingRange] StartRound({levelToStart}) called - resetting state");
            ShootingRangeAccessibility.OnGameStart();
        }
    }
}
