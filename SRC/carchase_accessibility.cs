using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using MelonLoader;
using HarmonyLib;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Vertical lock mode for targeting different parts of enemy car
    /// </summary>
    public enum VerticalLockMode
    {
        TireLock,    // Locked to tire/wheel height (weak point - high beep)
        BodyLock,    // Locked to body/center height (normal - medium beep)
        DriverLock,  // Locked to driver/top height (avoid - low beep)
        Off          // No vertical lock (manual 2D aiming)
    }

    /// <summary>
    /// Horizontal panning mode for audio positioning
    /// </summary>
    public enum PanningMode
    {
        Inverse,     // Sound opposite to target (left sound = move right)
        Direct       // Sound follows target (right sound = move right)
    }

    /// <summary>
    /// Which part of the enemy car is being hit
    /// </summary>
    public enum HitLocation
    {
        None,        // Not hitting enemy
        Tire,        // Hitting tire/wheel (weak point)
        Body,        // Hitting body (normal target)
        Driver       // Hitting driver area (upper part)
    }

    /// <summary>
    /// Audio positioning accessibility system for the car chase minigame.
    /// Uses continuous spatial audio (stereo panning + volume variation) to indicate enemy position.
    /// Provides on-target beep feedback when cursor is aimed at enemy.
    /// Maintains skill-based gameplay - player must aim cursor precisely using mouse/controller.
    /// </summary>
    public static class CarChaseAccessibility
    {
        // Audio frequencies
        private const float ENEMY_BASE_FREQ = 350f;    // Base pitch for enemy engine sound (Hz)
        private const float BEEP_BODY_FREQ = 1000f;    // Body hit beep (Hz) - normal
        private const float BEEP_TIRE_FREQ = 1400f;    // Tire hit beep (Hz) - higher pitch for weak point
        private const float BEEP_INVALID_FREQ = 600f;  // Invalid target beep (Hz) - lower pitch (driver area)

        // Audio durations
        private const float BEEP_DURATION = 0.05f;     // 50ms beep
        private const float BEEP_INTERVAL = 0.1f;      // 100ms between lock-on beeps

        // Game references
        private static CarChaseGameLogic activeGame = null;
        private static CarChaseEnemyCar enemyCar = null;
        private static CarChaseCursor cursorComponent = null;
        private static minigameCursor cursor = null;
        private static CarChasePlayerGun playerGun = null;
        private static CarChaseCoverStateHandler coverHandler = null;
        private static Camera gameCamera = null;

        // Audio system
        private static AudioSource enemyAudioSource = null;
        private static AudioSource beepAudioSource = null;
        private static GameObject audioObject = null;
        private static AudioClip engineLoopClip = null;
        private static AudioClip bodyBeepClip = null;
        private static AudioClip tireBeepClip = null;
        private static AudioClip invalidBeepClip = null;

        // State tracking
        private static bool isInitialized = false;
        private static bool wasOnTarget = false;
        private static float lastBeepTime = 0f;
        private static float lastHealthAnnouncementTime = 0f;
        private static float healthAnnouncementCooldown = 0.5f;

        // Vertical lock feature
        private static VerticalLockMode verticalLockMode = VerticalLockMode.TireLock; // Start with tire lock (optimal)

        // Panning mode feature
        private static PanningMode panningMode = PanningMode.Direct; // Start with direct mode (sound follows target)

        // Vertical offsets for different lock modes (relative to enemy center, in viewport units)
        private const float TIRE_OFFSET = -0.15f;   // Aim lower for tires
        private const float BODY_OFFSET = -0.07f;   // Aim slightly below center for body (avoids driver area)
        private const float DRIVER_OFFSET = 0.03f;  // Aim slightly above center for driver area

        // Aim assist feature (soft vertical lock for free mode)
        private static bool aimAssistEnabled = true; // Start with aim assist on
        private const float AIM_ASSIST_BAND = 0.10f; // Viewport units - free aiming range (±0.10 = ~20% of screen)
        private const float AIM_ASSIST_STRENGTH = 0.5f; // Pull strength (0-1, higher = stronger pull toward target)

        // Keyboard aiming feature
        private static bool keyboardAimingEnabled = true; // Start with keyboard aiming enabled

        // Keyboard cover/hide feature
        private static bool isKeyboardCoverActive = false; // Track if we triggered cover via keyboard

        // Health tracking
        private static float lastPlayerCarHP = 100f;
        private static float lastPlayerHP = 100f;
        private static float lastEnemyHP = 100f;

        // Cover state tracking
        private static CarChaseCoverStateHandler.CoverState lastCoverState = CarChaseCoverStateHandler.CoverState.CoverOut;

        // Game state tracking
        private static bool wasGameInProgress = false;
        private static bool gameStartAnnounced = false;

        public static void HandleInput()
        {
            try
            {
                // Detect active car chase
                if (activeGame == null)
                {
                    DetectCarChase();
                    return;
                }

                // Check if game ended or references became stale
                bool needsReset = false;
                try
                {
                    // Check if activeGame is still valid
                    if (activeGame == null || activeGame.gameObject == null || !activeGame.gameObject.activeInHierarchy)
                    {
                        needsReset = true;
                    }
                }
                catch
                {
                    // Exception means the reference is stale
                    needsReset = true;
                }

                if (needsReset)
                {
                    ResetState();
                    return;
                }

                // Initialize audio system
                if (!isInitialized)
                {
                    InitializeAudioSystem();
                }

                // Update game state announcements
                UpdateGameStateAnnouncements();

                // Validate and re-find cursorComponent if needed
                bool cursorComponentValid = false;
                try
                {
                    cursorComponentValid = cursorComponent != null && cursorComponent.gameObject != null && cursorComponent.gameObject.activeInHierarchy;
                }
                catch
                {
                    cursorComponentValid = false;
                }

                if (!cursorComponentValid)
                {
                    cursorComponent = null;
                    var cursors = UnityEngine.Object.FindObjectsOfType<CarChaseCursor>();
                    if (cursors != null && cursors.Count > 0)
                    {
                        cursorComponent = cursors[0];
                        MelonLogger.Msg("Found CarChaseCursor component (re-detected)");
                    }
                }

                // Validate and re-find cursor if needed
                bool cursorValid = false;
                try
                {
                    cursorValid = cursor != null && cursor.gameObject != null && cursor.gameObject.activeInHierarchy;
                }
                catch
                {
                    cursorValid = false;
                }

                if (!cursorValid)
                {
                    cursor = null;
                    // Prefer getting cursor from CarChaseCursor component (that's what the game uses)
                    bool foundFromComponent = false;
                    try
                    {
                        if (cursorComponent != null && cursorComponent.mcursor != null)
                        {
                            cursor = cursorComponent.mcursor;
                            foundFromComponent = true;
                            MelonLogger.Msg("Got minigameCursor from CarChaseCursor.mcursor (re-detected)");
                        }
                    }
                    catch { }

                    // Fallback to finding directly
                    if (!foundFromComponent)
                    {
                        var minigameCursors = UnityEngine.Object.FindObjectsOfType<minigameCursor>();
                        if (minigameCursors != null && minigameCursors.Count > 0)
                        {
                            cursor = minigameCursors[0];
                            MelonLogger.Msg("Found minigameCursor directly (re-detected)");
                        }
                    }

                }

                // Validate and re-find enemyCar if needed
                bool enemyCarValid = false;
                try
                {
                    enemyCarValid = enemyCar != null && enemyCar.gameObject != null && enemyCar.gameObject.activeInHierarchy;
                }
                catch
                {
                    enemyCarValid = false;
                }

                if (!enemyCarValid)
                {
                    enemyCar = null;
                    var enemies = UnityEngine.Object.FindObjectsOfType<CarChaseEnemyCar>();
                    if (enemies != null && enemies.Count > 0)
                    {
                        enemyCar = enemies[0];
                        MelonLogger.Msg("Found CarChaseEnemyCar during update (re-detected)");
                        // Re-initialize audio since enemyCar changed
                        isInitialized = false;
                    }
                }

                // Validate and re-find gameCamera if needed
                bool cameraValid = false;
                try
                {
                    cameraValid = gameCamera != null && gameCamera.gameObject != null && gameCamera.gameObject.activeInHierarchy;
                }
                catch
                {
                    cameraValid = false;
                }

                if (!cameraValid)
                {
                    gameCamera = Camera.main;
                    if (gameCamera == null)
                    {
                        var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                        if (cameras != null && cameras.Count > 0)
                        {
                            gameCamera = cameras[0];
                        }
                    }
                    if (gameCamera != null)
                    {
                        MelonLogger.Msg("Found Camera during update (re-detected)");
                    }
                }

                // Keyboard aiming is handled by the SetPositionFromMouse Harmony patch
                // which runs during the game's Update() at the right time.

                // Update vertical cursor lock if enabled
                UpdateVerticalLock();

                // Update enemy audio positioning
                UpdateEnemyAudioPosition();

                // Update on-target beep
                UpdateTargetingBeep();

                // Update cover state
                UpdateCoverState();

                // Update health announcements
                UpdateHealthAnnouncements();

                // Handle keyboard shooting
                HandleKeyboardShooting();

                // Handle keyboard cover/hide
                HandleKeyboardCover();

                // Handle helper commands
                HandleHelperCommands();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in CarChaseAccessibility.HandleInput: {ex.Message}");
            }
        }

        private static void DetectCarChase()
        {
            var games = UnityEngine.Object.FindObjectsOfType<CarChaseGameLogic>();
            if (games != null && games.Count > 0)
            {
                activeGame = games[0];

                // Cache references
                var enemies = UnityEngine.Object.FindObjectsOfType<CarChaseEnemyCar>();
                if (enemies != null && enemies.Count > 0)
                {
                    enemyCar = enemies[0];
                }

                var cursors = UnityEngine.Object.FindObjectsOfType<CarChaseCursor>();
                if (cursors != null && cursors.Count > 0)
                {
                    cursorComponent = cursors[0];
                    MelonLogger.Msg($"Found CarChaseCursor component");
                }

                // Try to find minigameCursor directly
                var minigameCursors = UnityEngine.Object.FindObjectsOfType<minigameCursor>();
                if (minigameCursors != null && minigameCursors.Count > 0)
                {
                    cursor = minigameCursors[0];
                    MelonLogger.Msg($"Found minigameCursor directly");
                }
                else
                {
                    MelonLogger.Warning("Could not find minigameCursor!");
                }

                // Also try to get it from the component
                if (cursor == null && cursorComponent != null)
                {
                    cursor = cursorComponent.mcursor;
                    if (cursor != null)
                    {
                        MelonLogger.Msg("Got minigameCursor from CarChaseCursor.mcursor");
                    }
                    else
                    {
                        MelonLogger.Warning("CarChaseCursor.mcursor is null!");
                    }
                }

                var guns = UnityEngine.Object.FindObjectsOfType<CarChasePlayerGun>();
                if (guns != null && guns.Count > 0)
                {
                    playerGun = guns[0];
                }

                var handlers = UnityEngine.Object.FindObjectsOfType<CarChaseCoverStateHandler>();
                if (handlers != null && handlers.Count > 0)
                {
                    coverHandler = handlers[0];
                }

                // Find camera
                gameCamera = Camera.main;
                if (gameCamera == null)
                {
                    var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                    if (cameras != null && cameras.Count > 0)
                    {
                        gameCamera = cameras[0];
                    }
                }

                MelonLogger.Msg("Car chase detected - initializing accessibility system");

                // Initialize health values
                if (activeGame != null)
                {
                    lastPlayerCarHP = activeGame.HPPlayerCar;
                    lastPlayerHP = activeGame.HPPlayer;
                    lastEnemyHP = activeGame.HPEnemy;
                }

                isInitialized = false; // Will initialize audio in next frame
            }
        }

        private static void InitializeAudioSystem()
        {
            if (isInitialized || activeGame == null)
                return;

            try
            {
                // Create audio object
                if (audioObject == null)
                {
                    audioObject = new GameObject("CarChaseAccessibilityAudio");
                    UnityEngine.Object.DontDestroyOnLoad(audioObject);
                }

                // Create enemy audio source for positioning (Option 2: Panning + Volume)
                if (enemyAudioSource == null && enemyCar != null)
                {
                    MelonLogger.Msg("Creating enemy audio source...");

                    // Attach to enemy car
                    enemyAudioSource = enemyCar.gameObject.AddComponent<AudioSource>();
                    enemyAudioSource.loop = true;
                    enemyAudioSource.volume = 0.5f; // Base volume, will modulate for vertical
                    enemyAudioSource.spatialBlend = 0f; // 2D sound (we handle panning manually)
                    enemyAudioSource.pitch = 1f; // Keep constant for constant pulse rate

                    // Generate continuous pulsing engine sound
                    engineLoopClip = GenerateContinuousEngineSound(1.0f); // 1 second loop
                    enemyAudioSource.clip = engineLoopClip;
                    enemyAudioSource.Play();

                    MelonLogger.Msg($"Enemy audio source created. Playing: {enemyAudioSource.isPlaying}");
                }

                // Create beep audio source for targeting feedback
                if (beepAudioSource == null)
                {
                    beepAudioSource = audioObject.AddComponent<AudioSource>();
                    beepAudioSource.loop = false;
                    beepAudioSource.volume = 0.7f;
                    beepAudioSource.spatialBlend = 0f;

                    // Generate targeting beeps for different target types
                    bodyBeepClip = GenerateBeep(BEEP_BODY_FREQ, BEEP_DURATION);
                    tireBeepClip = GenerateBeep(BEEP_TIRE_FREQ, BEEP_DURATION);
                    invalidBeepClip = GenerateBeep(BEEP_INVALID_FREQ, BEEP_DURATION);

                    MelonLogger.Msg("Beep audio sources created");
                }

                isInitialized = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error initializing audio system: {ex.Message}");
            }
        }

        private static void UpdateGameStateAnnouncements()
        {
            if (activeGame == null)
                return;

            // Check if game started
            if (activeGame.isGameInProgress && !gameStartAnnounced)
            {
                AccessibilityMod.Speak("Car chase starting", interrupt: true);
                gameStartAnnounced = true;
                wasGameInProgress = true;
            }

            // Check if game ended
            if (wasGameInProgress && !activeGame.isGameInProgress)
            {
                if (activeGame.isOver)
                {
                    // Check if won or lost
                    if (activeGame.HPPlayerCar > 0 && activeGame.HPPlayer > 0)
                    {
                        AccessibilityMod.Speak("Chase complete", interrupt: true);
                    }
                    else
                    {
                        AccessibilityMod.Speak("Failed", interrupt: true);
                    }
                }
                wasGameInProgress = false;
                gameStartAnnounced = false;
            }
        }

        private static void UpdateVerticalLock()
        {
            if (cursor == null || enemyCar == null || gameCamera == null)
                return;

            if (!activeGame.isGameInProgress)
                return;

            try
            {
                // Get enemy world position and convert to screen space
                Vector3 enemyWorldPos = enemyCar.transform.position;
                Vector3 enemyScreenPos = gameCamera.WorldToViewportPoint(enemyWorldPos);

                // Get current cursor position
                Vector3 cursorScreenPos = cursor.cursorPos;
                Vector3 cursorViewportPos = gameCamera.ScreenToViewportPoint(cursorScreenPos);

                if (verticalLockMode != VerticalLockMode.Off)
                {
                    // Hard lock mode - force cursor to specific height
                    float targetViewportY = enemyScreenPos.y;
                    switch (verticalLockMode)
                    {
                        case VerticalLockMode.TireLock:
                            targetViewportY += TIRE_OFFSET;
                            break;
                        case VerticalLockMode.BodyLock:
                            targetViewportY += BODY_OFFSET;
                            break;
                        case VerticalLockMode.DriverLock:
                            targetViewportY += DRIVER_OFFSET;
                            break;
                    }

                    // Convert target viewport Y to screen pixel Y
                    float targetScreenPixelY = targetViewportY * Screen.height;

                    // Set cursor Y to target, keep cursor X unchanged
                    // Must set mousePosition to prevent game from overwriting
                    Vector3 newPos = new Vector3(cursorScreenPos.x, targetScreenPixelY, cursorScreenPos.z);
                    cursor.cursorPos = newPos;
                    cursor.mousePosition = newPos;
                }
                else if (aimAssistEnabled)
                {
                    // Aim assist mode - soft lock that keeps cursor near enemy
                    // Calculate vertical distance from cursor to enemy (in viewport units)
                    float verticalDistance = cursorViewportPos.y - enemyScreenPos.y;

                    // If cursor is outside the free aiming band, apply pull toward enemy
                    if (Mathf.Abs(verticalDistance) > AIM_ASSIST_BAND)
                    {
                        // Calculate how far outside the band we are
                        float overshoot = Mathf.Abs(verticalDistance) - AIM_ASSIST_BAND;

                        // Apply correction proportional to overshoot (soft pull)
                        float correction = overshoot * AIM_ASSIST_STRENGTH;

                        // Pull cursor back toward the band edge (not all the way to enemy center)
                        float targetViewportY;
                        if (verticalDistance > 0)
                        {
                            // Cursor is above enemy - pull down
                            targetViewportY = cursorViewportPos.y - correction;
                        }
                        else
                        {
                            // Cursor is below enemy - pull up
                            targetViewportY = cursorViewportPos.y + correction;
                        }

                        // Convert to screen pixels
                        float targetScreenPixelY = targetViewportY * Screen.height;

                        // Apply the assisted position
                        // Must set mousePosition to prevent game from overwriting
                        Vector3 newPos = new Vector3(cursorScreenPos.x, targetScreenPixelY, cursorScreenPos.z);
                        cursor.cursorPos = newPos;
                        cursor.mousePosition = newPos;
                    }
                    // else: cursor is within the band, no adjustment needed - free aiming
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in UpdateVerticalLock: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies keyboard arrow key movement to cursor position.
        /// Called from the SetPositionFromMouse Harmony patch, which runs during
        /// the game's Update() at exactly the right time to replace mouse input.
        /// OnLStickMove doesn't work here because SetPositionFromJoy is never
        /// called without a real controller connected.
        /// </summary>
        public static void ApplyKeyboardMovement(minigameCursor mc)
        {
            if (!keyboardAimingEnabled || mc == null)
                return;

            if (activeGame == null || !activeGame.isGameInProgress)
                return;

            try
            {
                float moveX = 0f;
                float moveY = 0f;

                if (Input.GetKey(KeyCode.LeftArrow))
                    moveX -= 1f;
                if (Input.GetKey(KeyCode.RightArrow))
                    moveX += 1f;

                // Only allow vertical movement when vertical lock is off
                if (verticalLockMode == VerticalLockMode.Off)
                {
                    if (Input.GetKey(KeyCode.UpArrow))
                        moveY += 1f;
                    if (Input.GetKey(KeyCode.DownArrow))
                        moveY -= 1f;
                }

                if (moveX == 0f && moveY == 0f)
                    return;

                float speedMultiplier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                    ? 1.0f : 0.5f;

                float speed = Screen.width * 0.5f * speedMultiplier * Time.deltaTime;

                Vector3 pos = mc.cursorPos;
                pos.x += moveX * speed;
                pos.y += moveY * speed;

                pos.x = Mathf.Clamp(pos.x, 0, Screen.width);
                pos.y = Mathf.Clamp(pos.y, 0, Screen.height);

                mc.cursorPos = pos;
                mc.mousePosition = pos;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in ApplyKeyboardMovement: {ex.Message}");
            }
        }

        private static void UpdateEnemyAudioPosition()
        {
            if (enemyAudioSource == null || enemyCar == null || gameCamera == null || cursor == null)
                return;

            try
            {
                if (!activeGame.isGameInProgress)
                {
                    // Mute audio when game not in progress
                    if (enemyAudioSource.volume > 0)
                    {
                        enemyAudioSource.volume = 0f;
                    }
                    return;
                }


                // Get enemy world position and convert to screen space
                Vector3 enemyWorldPos = enemyCar.transform.position;
                Vector3 enemyScreenPos = gameCamera.WorldToViewportPoint(enemyWorldPos);

                // Get cursor screen position
                Vector3 cursorScreenPos = cursor.cursorPos;
                // Convert from pixel coordinates to viewport (0-1 range)
                Vector3 cursorViewportPos = gameCamera.ScreenToViewportPoint(cursorScreenPos);

                // Calculate RELATIVE position (cursor relative to enemy)
                // Negative = cursor left of enemy, Positive = cursor right of enemy
                float relativeX = cursorViewportPos.x - enemyScreenPos.x;
                float relativeY = cursorViewportPos.y - enemyScreenPos.y;

                // Calculate distance from cursor to enemy
                float distance = Mathf.Sqrt(relativeX * relativeX + relativeY * relativeY);

                // Map relative position to pan based on panning mode
                float pan;
                if (panningMode == PanningMode.Inverse)
                {
                    // Inverse mode (original): Sound opposite to target
                    // If cursor is LEFT of enemy, pan LEFT (negative) - tells you to move cursor RIGHT
                    // If cursor is RIGHT of enemy, pan RIGHT (positive) - tells you to move cursor LEFT
                    pan = relativeX * 3f;
                }
                else
                {
                    // Direct mode: Sound follows target
                    // If enemy is RIGHT of cursor, pan RIGHT (positive) - sound from right, move right
                    // If enemy is LEFT of cursor, pan LEFT (negative) - sound from left, move left
                    pan = -relativeX * 3f;
                }
                pan = Mathf.Clamp(pan, -1f, 1f);

                // Map relative Y position to volume
                // If vertical lock is enabled, always use max volume
                float volumeMultiplier;
                if (verticalLockMode != VerticalLockMode.Off)
                {
                    volumeMultiplier = 1f; // Always max volume when vertically locked
                }
                else
                {
                    // If cursor is vertically aligned with enemy, louder
                    // If cursor is above or below enemy, quieter
                    float verticalDistance = Mathf.Abs(relativeY);
                    volumeMultiplier = 1f - (verticalDistance * 1.5f); // Gets quieter as you move away vertically
                    volumeMultiplier = Mathf.Clamp(volumeMultiplier, 0.5f, 1f); // Min 50%, max 100%
                }

                // Apply to audio source
                enemyAudioSource.panStereo = pan;
                enemyAudioSource.volume = 0.5f * volumeMultiplier; // Base volume 0.5 * multiplier
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error updating enemy audio position: {ex.Message}");
            }
        }

        private static void UpdateTargetingBeep()
        {
            if (beepAudioSource == null || cursor == null || enemyCar == null || gameCamera == null)
                return;

            if (!activeGame.isGameInProgress)
                return;

            try
            {
                // Perform raycast from cursor position to determine what part we're hitting
                HitLocation hitLocation = GetHitLocation();
                bool isOnTarget = hitLocation != HitLocation.None;

                // Play beep when on target
                if (isOnTarget)
                {
                    // Log when we detect on target
                    if (!wasOnTarget)
                    {
                        MelonLogger.Msg("ON TARGET - Starting beep");
                    }

                    // Play beep at intervals while on target
                    if (Time.time - lastBeepTime >= BEEP_INTERVAL)
                    {
                        // Select beep frequency based on ACTUAL hit location, not vertical lock mode
                        AudioClip beepToPlay = hitLocation switch
                        {
                            HitLocation.Tire => tireBeepClip,      // High beep - weak point
                            HitLocation.Body => bodyBeepClip,      // Medium beep - normal
                            HitLocation.Driver => invalidBeepClip, // Low beep - avoid
                            _ => bodyBeepClip
                        };

                        MelonLogger.Msg($"Playing targeting beep at time {Time.time} (hit location: {hitLocation})");
                        beepAudioSource.PlayOneShot(beepToPlay);
                        lastBeepTime = Time.time;
                    }
                }
                else if (wasOnTarget)
                {
                    MelonLogger.Msg("OFF TARGET - Stopping beep");
                }

                wasOnTarget = isOnTarget;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error updating targeting beep: {ex.Message}");
            }
        }

        private static HitLocation GetHitLocation()
        {
            if (cursor == null || gameCamera == null || enemyCar == null)
                return HitLocation.None;

            try
            {
                // Get cursor screen position from minigameCursor component
                Vector3 cursorScreenPos = cursor.cursorPos;

                // Convert to world space ray
                Ray ray = gameCamera.ScreenPointToRay(cursorScreenPos);

                // Perform raycast with all layers
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 1000f))
                {
                    // Check if hit enemy car directly
                    if (hit.collider != null)
                    {
                        // Check the game object name for debugging
                        string hitName = hit.collider.gameObject.name;
                        bool isEnemyHit = false;

                        if (hit.collider.gameObject == enemyCar.gameObject)
                        {
                            isEnemyHit = true;
                        }
                        else
                        {
                            // Also check parent objects (enemy might be a child)
                            Transform current = hit.collider.transform;
                            while (current != null)
                            {
                                if (current.gameObject == enemyCar.gameObject)
                                {
                                    isEnemyHit = true;
                                    break;
                                }
                                current = current.parent;
                            }

                            // Check if the hit object's name contains "enemy" or "car"
                            if (!isEnemyHit && (hitName.ToLower().Contains("enemy") || hitName.ToLower().Contains("chase")))
                            {
                                isEnemyHit = true;
                            }
                        }

                        if (isEnemyHit)
                        {
                            // Determine which part was hit based on vertical position
                            // Convert hit point and enemy center to viewport space for comparison
                            Vector3 enemyViewport = gameCamera.WorldToViewportPoint(enemyCar.transform.position);
                            Vector3 hitViewport = gameCamera.WorldToViewportPoint(hit.point);

                            // Calculate vertical offset from enemy center
                            float verticalOffset = hitViewport.y - enemyViewport.y;

                            // Classify based on vertical position
                            // These thresholds roughly match our lock mode offsets
                            if (verticalOffset < -0.10f)
                            {
                                return HitLocation.Tire;  // Low hit - tires
                            }
                            else if (verticalOffset > 0.05f)
                            {
                                return HitLocation.Driver;  // High hit - driver area
                            }
                            else
                            {
                                return HitLocation.Body;  // Middle hit - body
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log errors for debugging
                MelonLogger.Msg($"Raycast error: {ex.Message}");
            }

            return HitLocation.None;
        }

        private static bool IsAimingAtEnemy()
        {
            // Wrapper for compatibility - returns true if hitting any part of enemy
            return GetHitLocation() != HitLocation.None;
        }

        private static void UpdateCoverState()
        {
            if (coverHandler == null)
                return;

            try
            {
                CarChaseCoverStateHandler.CoverState currentState = coverHandler.coverState;

                if (currentState != lastCoverState)
                {
                    if (currentState == CarChaseCoverStateHandler.CoverState.CoverIn)
                    {
                        AccessibilityMod.Speak("In cover", interrupt: false);
                    }
                    else if (currentState == CarChaseCoverStateHandler.CoverState.CoverOut)
                    {
                        AccessibilityMod.Speak("Exposed", interrupt: false);
                    }

                    lastCoverState = currentState;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error updating cover state: {ex.Message}");
            }
        }

        private static void UpdateHealthAnnouncements()
        {
            if (activeGame == null)
                return;

            // Throttle announcements
            if (Time.time - lastHealthAnnouncementTime < healthAnnouncementCooldown)
                return;

            try
            {
                // Check player car health
                float currentCarHP = activeGame.HPPlayerCar;
                if (currentCarHP < lastPlayerCarHP && currentCarHP > 0)
                {
                    AccessibilityMod.Speak($"Car health {Mathf.RoundToInt(currentCarHP)} percent", interrupt: false);
                    lastHealthAnnouncementTime = Time.time;
                }
                lastPlayerCarHP = currentCarHP;

                // Check player character health
                float currentPlayerHP = activeGame.HPPlayer;
                if (currentPlayerHP < lastPlayerHP && currentPlayerHP > 0)
                {
                    AccessibilityMod.Speak($"Player health {Mathf.RoundToInt(currentPlayerHP)} percent", interrupt: false);
                    lastHealthAnnouncementTime = Time.time;
                }
                lastPlayerHP = currentPlayerHP;

                // Check enemy health (only if visible to sighted players)
                // We'll announce enemy HP when player hits them
                float currentEnemyHP = activeGame.HPEnemy;
                if (currentEnemyHP < lastEnemyHP && currentEnemyHP > 0)
                {
                    // Only announce if enemy HP bar exists and is visible
                    // For now, we'll announce it as this is useful feedback
                    AccessibilityMod.Speak($"Enemy health {Mathf.RoundToInt(currentEnemyHP)} percent", interrupt: false);
                    lastHealthAnnouncementTime = Time.time;
                }
                lastEnemyHP = currentEnemyHP;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error updating health announcements: {ex.Message}");
            }
        }

        private static void HandleKeyboardShooting()
        {
            if (cursor == null || activeGame == null || !activeGame.isGameInProgress)
                return;

            try
            {
                // Space bar to shoot - set MouseLeftPressure which the game reads
                if (Input.GetKey(KeyCode.Space))
                {
                    // Simulate mouse left button being pressed
                    cursor.MouseLeftPressure = 1.0f;
                }
                else
                {
                    // Only reset if we were the ones who set it
                    // Check if no mouse button is actually pressed
                    if (!Input.GetMouseButton(0))
                    {
                        cursor.MouseLeftPressure = 0f;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in HandleKeyboardShooting: {ex.Message}");
            }
        }

        private static void HandleKeyboardCover()
        {
            if (cursor == null || activeGame == null || !activeGame.isGameInProgress)
                return;

            try
            {
                // Left Control to take cover - set MouseRightPressure which the game reads
                if (Input.GetKey(KeyCode.LeftControl))
                {
                    // Simulate mouse right button being pressed (triggers cover)
                    cursor.MouseRightPressure = 1.0f;
                    isKeyboardCoverActive = true;
                }
                else if (isKeyboardCoverActive)
                {
                    // Only reset if we were the ones who set it
                    // Check if no mouse button is actually pressed
                    if (!Input.GetMouseButton(1))
                    {
                        cursor.MouseRightPressure = 0f;
                    }
                    isKeyboardCoverActive = false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in HandleKeyboardCover: {ex.Message}");
            }
        }

        private static void HandleHelperCommands()
        {
            if (activeGame == null || !activeGame.isGameInProgress)
                return;

            // V key: Cycle through vertical lock modes
            if (Input.GetKeyDown(KeyCode.V))
            {
                // Cycle to next mode
                verticalLockMode = (VerticalLockMode)(((int)verticalLockMode + 1) % 4);

                string status = verticalLockMode switch
                {
                    VerticalLockMode.TireLock => "Locked to tires - weak point, high beep",
                    VerticalLockMode.BodyLock => "Locked to body - normal target, medium beep",
                    VerticalLockMode.DriverLock => "Locked to driver - avoid hitting, low beep",
                    VerticalLockMode.Off => "Vertical lock off - manual 2D aiming",
                    _ => "Unknown mode"
                };

                AccessibilityMod.Speak(status, interrupt: true);
                MelonLogger.Msg($"Vertical lock mode: {verticalLockMode}");
            }

            // P key: Toggle panning mode
            if (Input.GetKeyDown(KeyCode.P))
            {
                // Toggle between modes
                panningMode = panningMode == PanningMode.Inverse ? PanningMode.Direct : PanningMode.Inverse;

                string status = panningMode switch
                {
                    PanningMode.Inverse => "Panning mode: Inverse - sound opposite to target",
                    PanningMode.Direct => "Panning mode: Direct - sound follows target",
                    _ => "Unknown mode"
                };

                AccessibilityMod.Speak(status, interrupt: true);
                MelonLogger.Msg($"Panning mode: {panningMode}");
            }

            // A key: Toggle aim assist
            if (Input.GetKeyDown(KeyCode.A))
            {
                aimAssistEnabled = !aimAssistEnabled;

                string status = aimAssistEnabled
                    ? "Aim assist enabled - cursor stays near target in free mode"
                    : "Aim assist disabled - full manual control";

                AccessibilityMod.Speak(status, interrupt: true);
                MelonLogger.Msg($"Aim assist: {(aimAssistEnabled ? "Enabled" : "Disabled")}");
            }

            // K key: Toggle keyboard aiming
            if (Input.GetKeyDown(KeyCode.K))
            {
                keyboardAimingEnabled = !keyboardAimingEnabled;

                string status = keyboardAimingEnabled
                    ? "Keyboard aiming enabled - use arrow keys to aim"
                    : "Keyboard aiming disabled";

                AccessibilityMod.Speak(status, interrupt: true);
                MelonLogger.Msg($"Keyboard aiming: {(keyboardAimingEnabled ? "Enabled" : "Disabled")}");
            }

            // R key: Announce current health status
            if (Input.GetKeyDown(KeyCode.R))
            {
                string healthStatus = $"Car health {Mathf.RoundToInt(activeGame.HPPlayerCar)} percent, " +
                                     $"Player health {Mathf.RoundToInt(activeGame.HPPlayer)} percent, " +
                                     $"Enemy health {Mathf.RoundToInt(activeGame.HPEnemy)} percent";
                AccessibilityMod.Speak(healthStatus, interrupt: true);
            }

            // H key: Show help
            if (Input.GetKeyDown(KeyCode.H))
            {
                string helpMessage = "Car chase controls: " +
                                   "Arrow keys to aim. Shift plus arrow for faster movement. " +
                                   "Space bar to shoot. " +
                                   "Hold Left Control to take cover and reload. " +
                                   "V to cycle vertical lock modes: tires for weak point, body for normal, driver to avoid, or off for manual aiming. " +
                                   "When vertically locked, only left and right arrow keys work. " +
                                   "P to toggle panning mode. " +
                                   "K to toggle keyboard aiming. " +
                                   "A to toggle aim assist in free mode. " +
                                   "R to announce health. H for help. " +
                                   "Audio panning tells you horizontal position. Center sound means on target. " +
                                   "Beep confirms you are aimed at enemy. High beep equals tires, medium equals body, low equals driver. Aim for tires.";
                AccessibilityMod.Speak(helpMessage, interrupt: true);
            }

            // D key: Debug info
            if (Input.GetKeyDown(KeyCode.D))
            {
                DebugInfo();
            }

            // C key: Continuous hit location feedback (hold to test different positions)
            if (Input.GetKey(KeyCode.C))
            {
                HitLocation hitLoc = GetHitLocation();
                if (hitLoc != HitLocation.None)
                {
                    string locationName = hitLoc switch
                    {
                        HitLocation.Tire => "TIRE (weak point!)",
                        HitLocation.Body => "BODY (normal)",
                        HitLocation.Driver => "DRIVER (avoid!)",
                        _ => "Unknown"
                    };

                    // Get vertical offset for calibration
                    if (cursor != null && gameCamera != null && enemyCar != null)
                    {
                        Vector3 cursorScreenPos = cursor.cursorPos;
                        Ray ray = gameCamera.ScreenPointToRay(cursorScreenPos);
                        RaycastHit hit;
                        if (Physics.Raycast(ray, out hit, 1000f))
                        {
                            Vector3 enemyViewport = gameCamera.WorldToViewportPoint(enemyCar.transform.position);
                            Vector3 hitViewport = gameCamera.WorldToViewportPoint(hit.point);
                            float verticalOffset = hitViewport.y - enemyViewport.y;

                            MelonLogger.Msg($"[CALIBRATION] Hitting: {locationName} | Offset: {verticalOffset:F3} | Object: {hit.collider.gameObject.name}");
                        }
                    }
                }
            }
        }

        private static void DebugInfo()
        {
            MelonLogger.Msg("=== CAR CHASE DEBUG INFO ===");
            MelonLogger.Msg($"Vertical Lock Mode: {verticalLockMode}");
            MelonLogger.Msg($"Panning Mode: {panningMode}");
            MelonLogger.Msg($"Aim Assist: {(aimAssistEnabled ? "Enabled" : "Disabled")}");

            // Show current hit location detection
            HitLocation currentHit = GetHitLocation();
            MelonLogger.Msg($"Current Hit Location: {currentHit}");

            if (enemyCar != null && gameCamera != null && cursor != null)
            {
                Vector3 enemyWorldPos = enemyCar.transform.position;
                Vector3 enemyScreenPos = gameCamera.WorldToViewportPoint(enemyWorldPos);

                Vector3 cursorScreenPos = cursor.cursorPos;
                Vector3 cursorViewportPos = gameCamera.ScreenToViewportPoint(cursorScreenPos);

                // Calculate relative position
                float relativeX = cursorViewportPos.x - enemyScreenPos.x;
                float relativeY = cursorViewportPos.y - enemyScreenPos.y;
                float distance = Mathf.Sqrt(relativeX * relativeX + relativeY * relativeY);

                // Calculate pan and volume (same as UpdateEnemyAudioPosition)
                float pan = relativeX * 3f;
                pan = Mathf.Clamp(pan, -1f, 1f);

                float verticalDistance = Mathf.Abs(relativeY);
                float volumeMultiplier = 1f - (verticalDistance * 1.5f);
                volumeMultiplier = Mathf.Clamp(volumeMultiplier, 0.5f, 1f);
                float finalVolume = 0.5f * volumeMultiplier;

                MelonLogger.Msg($"Enemy Viewport: X={enemyScreenPos.x:F3}, Y={enemyScreenPos.y:F3}");
                MelonLogger.Msg($"Cursor Viewport: X={cursorViewportPos.x:F3}, Y={cursorViewportPos.y:F3}");
                MelonLogger.Msg($"RELATIVE Position: X={relativeX:F3}, Y={relativeY:F3}, Distance={distance:F3}");
                MelonLogger.Msg($"Calculated Pan (RELATIVE): {pan:F3} (-1=cursor left, +1=cursor right)");
                MelonLogger.Msg($"Calculated Volume (RELATIVE): {finalVolume:F3} (louder=vertically aligned, quieter=above/below)");

                if (enemyAudioSource != null)
                {
                    MelonLogger.Msg($"Audio Source - Pan: {enemyAudioSource.panStereo:F3}, Pitch: {enemyAudioSource.pitch:F3}");
                    MelonLogger.Msg($"Audio Source - Volume: {enemyAudioSource.volume:F3}, Playing: {enemyAudioSource.isPlaying}");
                }
                else
                {
                    MelonLogger.Msg("Enemy audio source is NULL!");
                }
            }
            else
            {
                MelonLogger.Msg($"EnemyCar: {(enemyCar != null ? "Found" : "NULL")}");
                MelonLogger.Msg($"GameCamera: {(gameCamera != null ? "Found" : "NULL")}");
            }

            MelonLogger.Msg($"CursorComponent: {(cursorComponent != null ? "Found" : "NULL")}");
            MelonLogger.Msg($"minigameCursor: {(cursor != null ? "Found" : "NULL")}");

            if (cursor != null && gameCamera != null)
            {
                Vector3 cursorScreenPos = cursor.cursorPos;
                MelonLogger.Msg($"Cursor Screen Pos: {cursorScreenPos}");
                MelonLogger.Msg($"Cursor GameObject: {cursor.gameObject.name}");
                MelonLogger.Msg($"Cursor Active: {cursor.gameObject.activeInHierarchy}");

                Ray ray = gameCamera.ScreenPointToRay(cursorScreenPos);
                MelonLogger.Msg($"Raycast Origin: {ray.origin}, Direction: {ray.direction}");

                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 1000f))
                {
                    MelonLogger.Msg($"Raycast HIT: {hit.collider.gameObject.name} at {hit.point}");
                    MelonLogger.Msg($"Hit object hierarchy: {GetHierarchyPath(hit.collider.transform)}");

                    // Show vertical offset calculation for hit location
                    if (enemyCar != null)
                    {
                        Vector3 enemyViewport = gameCamera.WorldToViewportPoint(enemyCar.transform.position);
                        Vector3 hitViewport = gameCamera.WorldToViewportPoint(hit.point);
                        float verticalOffset = hitViewport.y - enemyViewport.y;

                        MelonLogger.Msg($"Enemy Center Viewport Y: {enemyViewport.y:F3}");
                        MelonLogger.Msg($"Hit Point Viewport Y: {hitViewport.y:F3}");
                        MelonLogger.Msg($"Vertical Offset: {verticalOffset:F3}");
                        MelonLogger.Msg($"Hit Location Classification: {currentHit}");
                        MelonLogger.Msg($"  (Tire if < -0.10, Driver if > 0.05, Body otherwise)");
                    }
                }
                else
                {
                    MelonLogger.Msg("Raycast MISSED (no hit)");
                }

                bool isAiming = IsAimingAtEnemy();
                MelonLogger.Msg($"IsAimingAtEnemy: {isAiming}");
            }
            else
            {
                if (cursor == null)
                {
                    MelonLogger.Msg("minigameCursor is NULL - cannot perform raycast!");
                }
                if (gameCamera == null)
                {
                    MelonLogger.Msg("gameCamera is NULL - cannot perform raycast!");
                }
            }

            MelonLogger.Msg("========================");
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            Transform parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static void ResetState()
        {
            MelonLogger.Msg("Resetting car chase accessibility state");

            // Clean up audio
            if (enemyAudioSource != null)
            {
                enemyAudioSource.Stop();
                if (enemyCar != null)
                {
                    UnityEngine.Object.Destroy(enemyAudioSource);
                }
                enemyAudioSource = null;
            }

            if (audioObject != null)
            {
                UnityEngine.Object.Destroy(audioObject);
                audioObject = null;
            }

            if (engineLoopClip != null)
            {
                UnityEngine.Object.Destroy(engineLoopClip);
                engineLoopClip = null;
            }

            if (bodyBeepClip != null)
            {
                UnityEngine.Object.Destroy(bodyBeepClip);
                bodyBeepClip = null;
            }

            if (tireBeepClip != null)
            {
                UnityEngine.Object.Destroy(tireBeepClip);
                tireBeepClip = null;
            }

            if (invalidBeepClip != null)
            {
                UnityEngine.Object.Destroy(invalidBeepClip);
                invalidBeepClip = null;
            }

            beepAudioSource = null;

            // Reset references
            activeGame = null;
            enemyCar = null;
            cursorComponent = null;
            cursor = null;
            playerGun = null;
            coverHandler = null;
            gameCamera = null;

            // Reset state
            isInitialized = false;
            wasOnTarget = false;
            gameStartAnnounced = false;
            wasGameInProgress = false;
            verticalLockMode = VerticalLockMode.TireLock; // Reset to tire lock (optimal starting mode)
            panningMode = PanningMode.Direct; // Reset to direct mode (sound follows target)
            aimAssistEnabled = true; // Reset to aim assist enabled
            keyboardAimingEnabled = true; // Reset to keyboard aiming enabled
            isKeyboardCoverActive = false; // Reset cover state

            // Reset tracking values
            lastPlayerCarHP = 100f;
            lastPlayerHP = 100f;
            lastEnemyHP = 100f;
            lastCoverState = CarChaseCoverStateHandler.CoverState.CoverOut;
        }

        // ==================== AUDIO GENERATION ====================

        private static AudioClip GenerateContinuousEngineSound(float duration)
        {
            try
            {
                int sampleRate = 44100;
                int sampleCount = (int)(sampleRate * duration);

                AudioClip clip = AudioClip.Create("EngineLoop", sampleCount, 1, sampleRate, false);

                float[] samples = new float[sampleCount];

                // Generate a pulsing/beeping pattern for better directional audio
                // This creates a rhythmic pattern that's much easier to localize than continuous tone
                float pulseFrequency = 4f; // 4 pulses per second (CONSTANT)
                float pulseDutyCycle = 0.3f; // 30% on, 70% off for distinct beeps

                for (int i = 0; i < sampleCount; i++)
                {
                    float time = (float)i / sampleRate;

                    // Create pulse envelope (on/off pattern)
                    float pulsePhase = (time * pulseFrequency) % 1f; // 0-1 sawtooth for each pulse
                    float pulseEnvelope = pulsePhase < pulseDutyCycle ? 1f : 0f;

                    // Add smooth attack/decay to each pulse to avoid clicks
                    float attackTime = 0.01f; // 10ms attack
                    float decayTime = 0.02f; // 20ms decay

                    if (pulsePhase < attackTime && pulseEnvelope > 0)
                    {
                        pulseEnvelope = pulsePhase / attackTime; // Fade in
                    }
                    else if (pulsePhase > pulseDutyCycle - decayTime && pulseEnvelope > 0)
                    {
                        pulseEnvelope = (pulseDutyCycle - pulsePhase) / decayTime; // Fade out
                    }

                    // Base frequency sine wave with pulse envelope
                    float sample = Mathf.Sin(2f * Mathf.PI * ENEMY_BASE_FREQ * time) * 0.4f * pulseEnvelope;

                    // Add harmonics for richer sound
                    sample += Mathf.Sin(2f * Mathf.PI * ENEMY_BASE_FREQ * 2f * time) * 0.2f * pulseEnvelope;

                    // Apply overall fade envelope to make loop seamless
                    float loopFadeLength = 0.01f; // 10ms fade
                    float loopFade = 1f;

                    if (time < loopFadeLength)
                    {
                        loopFade = time / loopFadeLength;
                    }
                    else if (time > duration - loopFadeLength)
                    {
                        loopFade = (duration - time) / loopFadeLength;
                    }

                    samples[i] = sample * loopFade;
                }

                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error generating engine sound: {ex.Message}");
                return null;
            }
        }

        private static AudioClip GenerateBeep(float frequency, float duration)
        {
            try
            {
                int sampleRate = 44100;
                int sampleCount = (int)(sampleRate * duration);

                AudioClip clip = AudioClip.Create("TargetBeep", sampleCount, 1, sampleRate, false);

                float[] samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    float time = (float)i / sampleRate;

                    // Generate sine wave
                    float sample = Mathf.Sin(2f * Mathf.PI * frequency * time);

                    // Apply fade envelope
                    float fadeLength = 0.005f; // 5ms fade
                    float fadeEnvelope = 1f;

                    if (time < fadeLength)
                    {
                        fadeEnvelope = time / fadeLength;
                    }
                    else if (time > duration - fadeLength)
                    {
                        fadeEnvelope = (duration - time) / fadeLength;
                    }

                    samples[i] = sample * fadeEnvelope;
                }

                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error generating beep: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Whether keyboard control is actively overriding cursor input.
        /// Used by Harmony patch to block mouse positioning.
        /// </summary>
        public static bool keyboardControlActive => activeGame != null && keyboardAimingEnabled;
    }

    /// <summary>
    /// Harmony patch to replace mouse input with keyboard aiming during car chase.
    /// This runs during the game's Update() at exactly the right time - when the game
    /// would normally set cursor position from mouse. We replace that with arrow key input.
    /// </summary>
    [HarmonyPatch(typeof(minigameCursor), nameof(minigameCursor.SetPositionFromMouse))]
    public class MinigameCursor_SetPositionFromMouse_Patch
    {
        static bool Prefix(minigameCursor __instance)
        {
            if (CarChaseAccessibility.keyboardControlActive)
            {
                // Replace mouse positioning with keyboard aiming
                CarChaseAccessibility.ApplyKeyboardMovement(__instance);
                return false; // Skip original mouse positioning
            }
            return true; // Allow original method
        }
    }
}
