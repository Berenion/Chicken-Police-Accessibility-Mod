using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace ChickenPoliceAccessibility
{
    // Patch ClueConnectorConclusion.Show() to read voiceover text as subtitles
    [HarmonyPatch(typeof(ClueConnectorConclusion), "Show")]
    public class ClueConnectorConclusion_Show_Patch
    {
        static void Postfix(ClueConnectorConclusion __instance)
        {
            if (!AccessibilityMod.SubtitleModeEnabled)
                return;

            try
            {
                string text = __instance.fullText;
                if (!string.IsNullOrEmpty(text))
                {
                    MelonLogger.Msg($"[Subtitle] Clue connector conclusion: {text}");
                    ClueConnectorSubtitleTracker.lastAnnouncedText = text;
                    AccessibilityMod.SpeakDirect(text, false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in ClueConnectorConclusion.Show subtitle patch: {ex.Message}");
            }
        }
    }

    // Shared state for clue connector subtitle tracking to avoid double-announcing
    public static class ClueConnectorSubtitleTracker
    {
        public static string lastAnnouncedText = "";
        private static string lastStarterText = "";
        private static bool wasStarterActive = false;

        /// <summary>
        /// Polls for the starting menu popup text (the voiceover line that plays
        /// before the clue connector puzzle is initialized, on the Investigate/Hint/Exit menu).
        /// Called from AccessibilityMod.OnUpdate().
        /// </summary>
        public static void PollStarterText()
        {
            if (!AccessibilityMod.SubtitleModeEnabled)
                return;

            try
            {
                // Check if the starting enabler screen is active
                var starters = UnityEngine.Object.FindObjectsOfType<ClueConnectorStartingEnabler>();
                if (starters == null || starters.Count == 0)
                {
                    if (wasStarterActive)
                    {
                        // Starter just closed, reset tracking
                        wasStarterActive = false;
                        lastStarterText = "";
                    }
                    return;
                }

                var starter = starters[0];
                if (starter == null || !starter.gameObject.activeInHierarchy)
                {
                    if (wasStarterActive)
                    {
                        wasStarterActive = false;
                        lastStarterText = "";
                    }
                    return;
                }

                wasStarterActive = true;

                // Find ClueConnectorLogic to read starterPopupText
                var logics = UnityEngine.Object.FindObjectsOfType<ClueConnectorLogic>();
                if (logics == null || logics.Count == 0)
                    return;

                var logic = logics[0];
                if (logic == null || logic.starterPopupText == null)
                    return;

                string text = logic.starterPopupText.text;
                if (!string.IsNullOrEmpty(text) && text != lastStarterText)
                {
                    lastStarterText = text;
                    MelonLogger.Msg($"[Subtitle] Clue connector starter text: {text}");
                    AccessibilityMod.SpeakDirect(text, false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error polling clue connector starter text: {ex.Message}");
            }
        }
    }

    public static class ClueConnectorAccessibility
    {
        // Non-standard controller mapping for this game: Button1 = Bottom (Confirm), Button2 = Right (Cancel)
        // Windows API for moving the mouse cursor
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        // Windows API for simulating mouse clicks
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        // Navigation modes
        private enum NavigationMode
        {
            ObjectSelection,    // Selecting items (either to place or to connect)
            TargetSelection,    // Selecting drop position to place item
            ConnectionMode,     // Selecting second placed item to create connection
            AnswerPopup         // Answering the case popup (uses MenuSelector)
        }

        // State tracking
        private static ClueConnectorLogic activeLogic = null;
        private static GameObject cachedCaseClosedLayer = null;
        private static NavigationMode currentMode = NavigationMode.ObjectSelection;
        private static List<ClueConnectorDragObject> cachedDragObjects = new List<ClueConnectorDragObject>();
        private static List<ClueConnectorDropPos> cachedDropPositions = new List<ClueConnectorDropPos>();
        private static int currentObjectIndex = 0;
        private static int currentTargetIndex = 0;
        private static ClueConnectorDragObject selectedObject = null;
        private static bool hasAnnouncedStart = false;
        private static int lastKnownDragObjectCount = 0;
        private static int lastKnownDropPositionCount = 0;
        private static int lastKnownConnectionCount = 0;

        // Track which items are placed at which drop positions
        private static Dictionary<ClueConnectorDragObject, ClueConnectorDropPos> itemToDropPos = new Dictionary<ClueConnectorDragObject, ClueConnectorDropPos>();

        public static void HandleInput()
        {
            try
            {
                // First, check if cached case closed layer is active (this persists even when activeLogic is "collected")
                if (cachedCaseClosedLayer != null && cachedCaseClosedLayer.activeSelf)
                {
                    if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.JoystickButton1))
                    {
                        // Find the ButtonClose child and click it
                        var buttonClose = cachedCaseClosedLayer.transform.Find("ButtonClose (1)");
                        if (buttonClose == null)
                            buttonClose = cachedCaseClosedLayer.transform.Find("ButtonClose");

                        if (buttonClose != null)
                        {
                            // Simulate pointer click to close the case closed screen
                            UnityEngine.EventSystems.ExecuteEvents.Execute(buttonClose.gameObject,
                                new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current),
                                UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                        }
                    }
                    return; // Don't process other input while case closed screen is shown
                }

                // Detect active ClueConnectorLogic
                if (activeLogic == null || activeLogic.WasCollected)
                {
                    var logics = UnityEngine.Object.FindObjectsOfType<ClueConnectorLogic>();
                    if (logics != null && logics.Count > 0)
                    {
                        activeLogic = logics[0];
                        // Cache the case closed layer for later use
                        cachedCaseClosedLayer = activeLogic.caseClosedLayer;
                        OnMinigameActivated();
                    }
                    else
                    {
                        // No active mini-game, reset state
                        if (activeLogic != null)
                        {
                            OnMinigameDeactivated();
                        }
                        return;
                    }
                }

                // Update cached case closed layer reference
                if (activeLogic.caseClosedLayer != null)
                {
                    cachedCaseClosedLayer = activeLogic.caseClosedLayer;
                }

                // Don't handle input if paused
                if (activeLogic.isPaused)
                    return;

                // Check if new drag objects or drop positions have appeared (happens after successful connections)
                CheckForNewItems();

                // Check if connection count changed (to detect successful connections)
                CheckConnectionCount();

                // Check if popup is open - handle popup input separately
                bool popupOpen = IsPopupOpen();
                if (popupOpen)
                {
                    HandlePopupInput();
                    return;
                }

                // Handle input based on current mode
                if (currentMode == NavigationMode.ObjectSelection)
                {
                    HandleObjectSelectionInput();
                }
                else if (currentMode == NavigationMode.TargetSelection)
                {
                    HandleTargetSelectionInput();
                }
                else if (currentMode == NavigationMode.ConnectionMode)
                {
                    HandleConnectionModeInput();
                }

                // Handle global commands
                HandleGlobalCommands();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in ClueConnectorAccessibility.HandleInput: {ex}");
            }
        }

        private static void OnMinigameActivated()
        {
            MelonLogger.Msg("Clue Connector mini-game activated");

            // Cache all drag objects (include both unplaced and placed items)
            cachedDragObjects.Clear();
            var dragObjects = UnityEngine.Object.FindObjectsOfType<ClueConnectorDragObject>();
            if (dragObjects != null)
            {
                foreach (var obj in dragObjects)
                {
                    // Include items that are draggable (unplaced) OR connectable (placed)
                    if (obj != null && (obj.isDraggable || obj.isConnectable))
                    {
                        cachedDragObjects.Add(obj);
                        MelonLogger.Msg($"Drag object: {obj.gameObject.name}, type: {obj.type}, isDraggable: {obj.isDraggable}, isConnectable: {obj.isConnectable}");
                    }
                }
            }

            // Sort by position for consistent navigation
            cachedDragObjects = cachedDragObjects.OrderBy(obj => obj.transform.position.x)
                                                   .ThenBy(obj => obj.transform.position.y)
                                                   .ToList();

            // Cache all drop positions (the actual connection targets!)
            cachedDropPositions.Clear();
            var dropPositions = UnityEngine.Object.FindObjectsOfType<ClueConnectorDropPos>();
            if (dropPositions != null)
            {
                foreach (var pos in dropPositions)
                {
                    if (pos != null)
                    {
                        cachedDropPositions.Add(pos);
                        MelonLogger.Msg($"Drop position: {pos.gameObject.name}, type: {pos.type}, has {pos.solutionIds?.Count ?? 0} solution IDs");
                        if (pos.solutionIds != null && pos.solutionIds.Count > 0)
                        {
                            // Convert Il2Cpp list to C# list for logging
                            var solutionsList = new List<string>();
                            foreach (var id in pos.solutionIds)
                            {
                                solutionsList.Add(id);
                            }
                            string solutions = string.Join(", ", solutionsList);
                            MelonLogger.Msg($"  Solution IDs: {solutions}");
                        }
                    }
                }
            }

            // Sort by position for consistent navigation
            cachedDropPositions = cachedDropPositions.OrderBy(pos => pos.transform.position.x)
                                                       .ThenBy(pos => pos.transform.position.y)
                                                       .ToList();

            currentObjectIndex = 0;
            currentTargetIndex = 0;
            currentMode = NavigationMode.ObjectSelection;
            selectedObject = null;
            hasAnnouncedStart = false;

            // Also check for items that are already placed (isConnectable = True)
            var alreadyPlacedItems = dragObjects != null ? dragObjects.Where(obj => obj != null && obj.isConnectable).ToList() : new List<ClueConnectorDragObject>();
            if (alreadyPlacedItems.Count > 0)
            {
                MelonLogger.Msg($"Found {alreadyPlacedItems.Count} items already placed (isConnectable=True):");
                foreach (var item in alreadyPlacedItems)
                {
                    MelonLogger.Msg($"  - {item.gameObject.name}, type: {item.type}");
                }
            }

            MelonLogger.Msg($"Cached {cachedDragObjects.Count} drag objects and {cachedDropPositions.Count} drop positions");

            // Log the expected hint connections
            if (activeLogic != null && activeLogic.hintGood != null)
            {
                MelonLogger.Msg($"Game expects {activeLogic.hintGood.Length} good hint connections:");
                for (int i = 0; i < activeLogic.hintGood.Length; i++)
                {
                    MelonLogger.Msg($"  Hint {i}: {activeLogic.hintGood[i]}");
                }
                MelonLogger.Msg($"Current hintGoodCount: {activeLogic.hintGoodCount}");
            }

            // Initialize known counts for change detection
            lastKnownDragObjectCount = cachedDragObjects.Count;
            lastKnownDropPositionCount = cachedDropPositions.Count;
            lastKnownConnectionCount = activeLogic != null ? activeLogic.actualGoodConnections : 0;

            // Announce mini-game start after a brief delay to let UI load
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => AnnounceStart());
        }

        private static void OnMinigameDeactivated()
        {
            MelonLogger.Msg("Clue Connector mini-game deactivated");
            activeLogic = null;
            cachedCaseClosedLayer = null;
            cachedDragObjects.Clear();
            cachedDropPositions.Clear();
            itemToDropPos.Clear();
            currentObjectIndex = 0;
            currentTargetIndex = 0;
            currentMode = NavigationMode.ObjectSelection;
            selectedObject = null;
            hasAnnouncedStart = false;
            lastKnownDragObjectCount = 0;
            lastKnownDropPositionCount = 0;
            lastKnownConnectionCount = 0;
        }

        private static void CheckConnectionCount()
        {
            if (activeLogic == null)
                return;

            int currentConnectionCount = activeLogic.actualGoodConnections;
            if (currentConnectionCount != lastKnownConnectionCount)
            {
                MelonLogger.Msg($"CONNECTION COUNT CHANGED: {lastKnownConnectionCount} -> {currentConnectionCount}");

                // Log current hint status
                if (activeLogic.hintGood != null)
                {
                    MelonLogger.Msg($"hintGoodCount now: {activeLogic.hintGoodCount}");
                    if (activeLogic.hintGoodCount < activeLogic.hintGood.Length)
                    {
                        MelonLogger.Msg($"Next expected hint: {activeLogic.hintGood[activeLogic.hintGoodCount]}");
                    }
                }

                lastKnownConnectionCount = currentConnectionCount;
            }
        }

        private static void CheckForNewItems()
        {
            // Check if new drag objects have appeared (count both unplaced and placed items)
            var currentDragObjects = UnityEngine.Object.FindObjectsOfType<ClueConnectorDragObject>();
            int dragObjectCount = 0;
            if (currentDragObjects != null)
            {
                foreach (var obj in currentDragObjects)
                {
                    if (obj != null && (obj.isDraggable || obj.isConnectable))
                        dragObjectCount++;
                }
            }

            // Check if new drop positions have appeared
            var currentDropPositions = UnityEngine.Object.FindObjectsOfType<ClueConnectorDropPos>();
            int dropPositionCount = currentDropPositions != null ? currentDropPositions.Count : 0;

            // If counts changed, refresh caches
            bool needsRefresh = false;
            if (dragObjectCount != lastKnownDragObjectCount)
            {
                MelonLogger.Msg($"Drag object count changed: {lastKnownDragObjectCount} -> {dragObjectCount}");
                needsRefresh = true;
            }

            if (dropPositionCount != lastKnownDropPositionCount)
            {
                MelonLogger.Msg($"Drop position count changed: {lastKnownDropPositionCount} -> {dropPositionCount}");
                needsRefresh = true;
            }

            if (needsRefresh)
            {
                MelonLogger.Msg("Refreshing caches due to new items appearing");
                RefreshCaches();
            }
        }

        private static void RefreshCaches()
        {
            // Store current selection if possible
            ClueConnectorDragObject previousSelection = null;
            if (currentObjectIndex >= 0 && currentObjectIndex < cachedDragObjects.Count)
                previousSelection = cachedDragObjects[currentObjectIndex];

            // Refresh drag objects (include both unplaced and placed items)
            cachedDragObjects.Clear();
            var dragObjects = UnityEngine.Object.FindObjectsOfType<ClueConnectorDragObject>();
            if (dragObjects != null)
            {
                foreach (var obj in dragObjects)
                {
                    // Include items that are draggable (unplaced) OR connectable (placed)
                    if (obj != null && (obj.isDraggable || obj.isConnectable))
                    {
                        cachedDragObjects.Add(obj);
                    }
                }
            }

            // Sort by position for consistent navigation
            cachedDragObjects = cachedDragObjects.OrderBy(obj => obj.transform.position.x)
                                                   .ThenBy(obj => obj.transform.position.y)
                                                   .ToList();

            lastKnownDragObjectCount = cachedDragObjects.Count;

            // Refresh drop positions
            cachedDropPositions.Clear();
            var dropPositions = UnityEngine.Object.FindObjectsOfType<ClueConnectorDropPos>();
            if (dropPositions != null)
            {
                foreach (var pos in dropPositions)
                {
                    if (pos != null)
                    {
                        cachedDropPositions.Add(pos);
                    }
                }
            }

            // Sort by position for consistent navigation
            cachedDropPositions = cachedDropPositions.OrderBy(pos => pos.transform.position.x)
                                                       .ThenBy(pos => pos.transform.position.y)
                                                       .ToList();

            lastKnownDropPositionCount = cachedDropPositions.Count;

            // Try to restore previous selection
            if (previousSelection != null && cachedDragObjects.Contains(previousSelection))
            {
                currentObjectIndex = cachedDragObjects.IndexOf(previousSelection);
            }
            else
            {
                // Reset to first item if previous selection no longer valid
                currentObjectIndex = 0;
            }

            // Reset target index
            currentTargetIndex = 0;

            MelonLogger.Msg($"Refreshed caches: {cachedDragObjects.Count} drag objects, {cachedDropPositions.Count} drop positions");
        }

        private static void AnnounceStart()
        {
            if (hasAnnouncedStart || activeLogic == null)
                return;

            hasAnnouncedStart = true;
            int total = activeLogic.maxConnections;
            int current = activeLogic.actualGoodConnections;
            int dropPosCount = cachedDropPositions.Count;
            int itemCount = cachedDragObjects.Count;

            string message = $"Clue connector puzzle. {itemCount} items, {dropPosCount} drop positions. " +
                           $"Make {total} connections. {current} already made. " +
                           $"First, place items on drop positions. Then select placed items to connect them. " +
                           $"Press H for help.";
            AccessibilityMod.Speak(message, true);
        }

        private static void HandleObjectSelectionInput()
        {
            if (cachedDragObjects.Count == 0)
                return;

            bool moved = false;

            // Left bumper (LB) or Left Arrow - previous object
            if (Input.GetKeyDown(KeyCode.JoystickButton4) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                currentObjectIndex--;
                if (currentObjectIndex < 0)
                    currentObjectIndex = cachedDragObjects.Count - 1;
                moved = true;
            }

            // Right bumper (RB) or Right Arrow - next object
            if (Input.GetKeyDown(KeyCode.JoystickButton5) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                currentObjectIndex++;
                if (currentObjectIndex >= cachedDragObjects.Count)
                    currentObjectIndex = 0;
                moved = true;
            }

            if (moved)
            {
                AnnounceCurrentObject();
            }

            // Bottom button or Space - select object
            // NOTE: Enter key triggers popup's Skip/Close, so we use Space instead
            if (Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.Space))
            {
                SelectCurrentObject();
            }

            // Backspace or Y button - remove placed item (return to original position)
            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.JoystickButton3))
            {
                RemoveCurrentPlacedItem();
            }
        }

        private static void HandleTargetSelectionInput()
        {
            if (cachedDropPositions.Count == 0 || selectedObject == null)
                return;

            bool moved = false;

            // Left bumper (LB) or Left Arrow - previous target
            if (Input.GetKeyDown(KeyCode.JoystickButton4) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                currentTargetIndex--;
                if (currentTargetIndex < 0)
                    currentTargetIndex = cachedDropPositions.Count - 1;
                moved = true;
            }

            // Right bumper (RB) or Right Arrow - next target
            if (Input.GetKeyDown(KeyCode.JoystickButton5) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                currentTargetIndex++;
                if (currentTargetIndex >= cachedDropPositions.Count)
                    currentTargetIndex = 0;
                moved = true;
            }

            if (moved)
            {
                AnnounceCurrentTarget();
            }

            // Bottom button or Space - place item at drop position
            // NOTE: Enter key triggers popup's Skip/Close, so we use Space instead
            if (Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.Space))
            {
                ConnectToCurrentTarget();
            }

            // Right button or Escape - cancel
            if (Input.GetKeyDown(KeyCode.JoystickButton2) || Input.GetKeyDown(KeyCode.Backspace))
            {
                CancelConnection();
            }
        }

        private static void HandleConnectionModeInput()
        {
            if (cachedDragObjects.Count == 0 || selectedObject == null)
                return;

            bool moved = false;

            // Left bumper (LB) or Left Arrow - previous placed item
            if (Input.GetKeyDown(KeyCode.JoystickButton4) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                do
                {
                    currentTargetIndex--;
                    if (currentTargetIndex < 0)
                        currentTargetIndex = cachedDragObjects.Count - 1;

                    // Skip if we've looped back to start
                    if (currentTargetIndex == currentObjectIndex)
                        break;
                }
                while (currentTargetIndex != currentObjectIndex && !cachedDragObjects[currentTargetIndex].isConnectable);

                moved = true;
            }

            // Right bumper (RB) or Right Arrow - next placed item
            if (Input.GetKeyDown(KeyCode.JoystickButton5) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                do
                {
                    currentTargetIndex++;
                    if (currentTargetIndex >= cachedDragObjects.Count)
                        currentTargetIndex = 0;

                    // Skip if we've looped back to start
                    if (currentTargetIndex == currentObjectIndex)
                        break;
                }
                while (currentTargetIndex != currentObjectIndex && !cachedDragObjects[currentTargetIndex].isConnectable);

                moved = true;
            }

            if (moved)
            {
                AnnounceConnectionTarget();
            }

            // Bottom button or Space - connect items
            // NOTE: Enter key triggers popup's Skip/Close, so we use Space instead
            if (Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.Space))
            {
                MelonLogger.Msg("Connection input detected");
                ConnectPlacedItems();
            }

            // Right button or Escape - cancel
            if (Input.GetKeyDown(KeyCode.JoystickButton2) || Input.GetKeyDown(KeyCode.Backspace))
            {
                CancelConnection();
            }
        }

        private static MenuSelector cachedPopupMenuSelector = null;
        private static float popupOpenedTime = 0f;
        private static bool popupWasOpened = false;
        private static ClueConnector cachedConnector = null;

        // Call this when we know the popup was opened
        public static void NotifyPopupOpened()
        {
            popupOpenedTime = Time.time;
            popupWasOpened = true;
            MelonLogger.Msg("NotifyPopupOpened: Popup opened notification received");
        }

        private static bool lastPopupOpenState = false;

        private static bool IsPopupOpen()
        {
            if (activeLogic == null)
                return false;

            bool isOpen = false;

            // First, try to directly check the casePopup.isPopupOpen property
            try
            {
                if (activeLogic.casePopup != null && activeLogic.casePopup.isPopupOpen)
                {
                    // Popup is actually open - cache the menu selector
                    if (activeLogic.casePopup.menuSelector != null)
                    {
                        cachedPopupMenuSelector = activeLogic.casePopup.menuSelector;
                    }
                    isOpen = true;
                }
            }
            catch { }

            // Second, try from cached connector
            if (!isOpen && cachedConnector != null)
            {
                try
                {
                    if (cachedConnector.casePopup != null && cachedConnector.casePopup.isPopupOpen)
                    {
                        if (cachedConnector.casePopup.menuSelector != null)
                        {
                            cachedPopupMenuSelector = cachedConnector.casePopup.menuSelector;
                        }
                        isOpen = true;
                    }
                }
                catch { }
            }

            // Third, check if popup was recently opened (time-based fallback for race conditions)
            if (!isOpen && popupWasOpened && (Time.time - popupOpenedTime) < 3f)
            {
                isOpen = true;
            }
            else if (popupWasOpened && (Time.time - popupOpenedTime) >= 3f)
            {
                // Reset the flag after timeout
                popupWasOpened = false;
            }

            // Check if we have a cached popup menu selector that's still valid
            if (!isOpen && cachedPopupMenuSelector != null)
            {
                try
                {
                    if (cachedPopupMenuSelector.gameObject.activeInHierarchy && cachedPopupMenuSelector.is_enabled)
                    {
                        isOpen = true;
                    }
                    else
                    {
                        cachedPopupMenuSelector = null; // Clear invalid cache
                    }
                }
                catch
                {
                    cachedPopupMenuSelector = null;
                }
            }

            // Log state changes only
            if (isOpen != lastPopupOpenState)
            {
                if (isOpen)
                    MelonLogger.Msg("Popup detected as open");
                else
                    MelonLogger.Msg("Popup detected as closed");
                lastPopupOpenState = isOpen;
            }

            return isOpen;
        }

        private static MenuSelector GetPopupMenuSelector()
        {
            // First, try to get directly from activeLogic.casePopup
            try
            {
                if (activeLogic != null && activeLogic.casePopup != null)
                {
                    var ms = activeLogic.casePopup.menuSelector;
                    if (ms != null)
                    {
                        cachedPopupMenuSelector = ms;
                        return ms;
                    }
                }
            }
            catch { }

            // Second, try from cached connector
            try
            {
                if (cachedConnector != null && cachedConnector.casePopup != null)
                {
                    var ms = cachedConnector.casePopup.menuSelector;
                    if (ms != null)
                    {
                        cachedPopupMenuSelector = ms;
                        return ms;
                    }
                }
            }
            catch { }

            // Third, return cached if valid
            if (cachedPopupMenuSelector != null)
            {
                try
                {
                    if (cachedPopupMenuSelector.gameObject.activeInHierarchy)
                    {
                        return cachedPopupMenuSelector;
                    }
                }
                catch
                {
                    cachedPopupMenuSelector = null;
                }
            }

            return null;
        }

        private static void HandlePopupInput()
        {
            try
            {
                var menuSelector = GetPopupMenuSelector();
                if (menuSelector == null)
                    return;

                // Try to enable buttons if not enabled (may help in some cases)
                if (!menuSelector.is_enabled)
                {
                    try
                    {
                        if (activeLogic != null && activeLogic.casePopup != null)
                        {
                            activeLogic.casePopup.setActiveButtons(true);
                        }
                    }
                    catch { }
                }

                // Handle input regardless of is_enabled - we call the methods directly
                // Up arrow - previous option
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    menuSelector.SelectPrev();
                    MelonLogger.Msg($"Popup: SelectPrev -> selectedIdx={menuSelector.selectedIdx}");
                }

                // Down arrow - next option
                if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    menuSelector.SelectNext();
                    MelonLogger.Msg($"Popup: SelectNext -> selectedIdx={menuSelector.selectedIdx}");
                }

                // Space - select current option (Enter closes popup without answering)
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    MelonLogger.Msg($"Popup: ClickOnSelected at idx={menuSelector.selectedIdx}");
                    menuSelector.ClickOnSelected();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error handling popup input: {ex}");
            }
        }

        private static void HandleGlobalCommands()
        {
            // L - List objects or targets
            if (Input.GetKeyDown(KeyCode.L))
            {
                ListItems();
            }

            // S - List all slots with types and status
            if (Input.GetKeyDown(KeyCode.S))
            {
                ListSlots();
            }

            // H - Help
            if (Input.GetKeyDown(KeyCode.H))
            {
                ShowHelp();
            }
        }

        private static void AnnounceCurrentObject()
        {
            if (currentObjectIndex < 0 || currentObjectIndex >= cachedDragObjects.Count)
                return;

            var obj = cachedDragObjects[currentObjectIndex];
            string name = GetObjectName(obj);
            string type = obj.type;

            string announcement;
            if (obj.isConnectable)
            {
                // Check if item has connections (only in current puzzle instance)
                var pins = GetCurrentPins();
                string objId = obj.gameObject.name.Replace("Item_", "");
                bool hasConnections = false;
                foreach (var pin in pins)
                {
                    if (pin.id1 == objId || pin.id2 == objId)
                    {
                        hasConnections = true;
                        break;
                    }
                }

                if (hasConnections)
                {
                    announcement = $"{name}, {type}, placed, connected";
                }
                else
                {
                    announcement = $"{name}, {type}, placed, can remove";
                }
            }
            else
            {
                announcement = $"{name}, {type}, not placed";
            }

            AccessibilityMod.Speak(announcement, true);
        }

        private static void AnnounceCurrentTarget()
        {
            if (currentTargetIndex < 0 || currentTargetIndex >= cachedDropPositions.Count)
                return;

            var dropPos = cachedDropPositions[currentTargetIndex];
            string sourceName = GetObjectName(selectedObject);
            int positionNumber = currentTargetIndex + 1;
            int totalPositions = cachedDropPositions.Count;

            // Check if occupied
            bool occupied = IsDropPositionOccupied(dropPos);
            string occupiedStatus = occupied ? "occupied" : "empty";

            // Get the type this position accepts (person, clue, item)
            string acceptsType = dropPos.type;
            if (string.IsNullOrEmpty(acceptsType))
                acceptsType = "any";

            string announcement = $"Position {positionNumber} of {totalPositions}, accepts {acceptsType}, {occupiedStatus}";
            AccessibilityMod.Speak(announcement, true);
        }

        private static void AnnounceConnectionTarget()
        {
            if (currentTargetIndex < 0 || currentTargetIndex >= cachedDragObjects.Count)
                return;

            var targetObject = cachedDragObjects[currentTargetIndex];

            // Skip if not a placed item (shouldn't happen since navigation skips non-connectable items)
            if (!targetObject.isConnectable)
                return;

            string sourceName = GetObjectName(selectedObject);
            string targetName = GetObjectName(targetObject);
            string targetType = targetObject.type;

            string announcement = $"Connecting {sourceName} to {targetName}, {targetType}";
            AccessibilityMod.Speak(announcement, true);
        }

        private static string GetDropPositionName(ClueConnectorDropPos dropPos)
        {
            // Try to get text from child objects
            string text = AccessibilityMod.GetTextMeshProText(dropPos.gameObject);
            if (!string.IsNullOrEmpty(text))
                return text;

            // Fall back to object name
            string name = dropPos.gameObject.name;
            name = name.Replace("(Clone)", "").Replace("_", " ").Trim();

            if (string.IsNullOrEmpty(name))
                return $"Drop position {cachedDropPositions.IndexOf(dropPos) + 1}";

            return name;
        }

        private static void SelectCurrentObject()
        {
            if (currentObjectIndex < 0 || currentObjectIndex >= cachedDragObjects.Count)
                return;

            selectedObject = cachedDragObjects[currentObjectIndex];
            string name = GetObjectName(selectedObject);

            // Check if this is a placed item (can be connected)
            if (selectedObject.isConnectable)
            {
                // This is a placed item - go to connection mode to select second item
                currentMode = NavigationMode.ConnectionMode;

                // Start at next object to avoid connecting to self
                currentTargetIndex = currentObjectIndex + 1;
                if (currentTargetIndex >= cachedDragObjects.Count)
                    currentTargetIndex = 0;

                string announcement = $"{name} selected. Choose second placed item to connect. Press right button to cancel.";
                AccessibilityMod.Speak(announcement, true);

                // Announce first possible connection target
                AnnounceConnectionTarget();
            }
            else
            {
                // This is an unplaced item - go to drop position selection
                currentMode = NavigationMode.TargetSelection;

                // Start at first drop position
                currentTargetIndex = 0;

                string announcement = $"{name} selected. Choose drop position. Press right button to cancel.";
                AccessibilityMod.Speak(announcement, true);

                // Announce first target
                AnnounceCurrentTarget();
            }
        }

        private static bool IsDropPositionOccupied(ClueConnectorDropPos dropPos)
        {
            // Check our tracking dictionary first
            foreach (var kvp in itemToDropPos)
            {
                if (kvp.Value == dropPos)
                    return true;
            }

            // Also check if any placed item (isConnectable) is at this position
            float threshold = 0.5f; // Position matching threshold
            foreach (var item in cachedDragObjects)
            {
                if (item.isConnectable)
                {
                    float distance = Vector3.Distance(item.transform.position, dropPos.transform.position);
                    if (distance < threshold)
                        return true;
                }
            }

            return false;
        }

        private static void ConnectToCurrentTarget()
        {
            if (selectedObject == null || currentTargetIndex < 0 || currentTargetIndex >= cachedDropPositions.Count)
                return;

            var dropPos = cachedDropPositions[currentTargetIndex];

            // Check if this drop position is already occupied
            if (IsDropPositionOccupied(dropPos))
            {
                AccessibilityMod.Speak("This position is already occupied. Choose another.", true);
                return;
            }

            // Check if the item type matches what this position accepts
            string acceptsType = dropPos.type;
            string itemType = selectedObject.type;
            if (!string.IsNullOrEmpty(acceptsType) && !string.IsNullOrEmpty(itemType) && acceptsType != itemType)
            {
                AccessibilityMod.Speak($"This position accepts {acceptsType}, not {itemType}. Choose another.", true);
                return;
            }

            // Get object names for announcement
            string sourceName = GetObjectName(selectedObject);
            int positionNumber = currentTargetIndex + 1;

            try
            {
                // Get the ClueConnector instance from the source object
                var connector = selectedObject.clueConnector;
                if (connector == null)
                {
                    MelonLogger.Error("ClueConnector reference is null on source object");
                    AccessibilityMod.Speak("Error: Cannot find connection manager", true);
                    return;
                }

                MelonLogger.Msg($"Attempting placement: {sourceName} -> Drop Position {positionNumber}");
                MelonLogger.Msg($"Source type: {selectedObject.type}, Drop position accepts: {dropPos.type}");

                // Directly set game state for placement
                MelonLogger.Msg("Placing item by directly setting game state");

                // Step 1: Move item to drop position
                selectedObject.transform.position = dropPos.transform.position;
                MelonLogger.Msg($"Set item position to {dropPos.transform.position}");

                // Step 2: Set the drop position type
                dropPos.setType(selectedObject.type);
                MelonLogger.Msg($"Set drop position type to {selectedObject.type}");

                // Step 3: Mark item as connectable (placed on board)
                selectedObject.isConnectable = true;
                selectedObject.isDraggable = false; // Can't drag placed items
                selectedObject.dragState = ClueConnectorDragObject.DragState.NotDragging;
                MelonLogger.Msg("Set item as connectable");

                // Step 4: Track the placement in our dictionary
                itemToDropPos[selectedObject] = dropPos;
                MelonLogger.Msg($"Tracked placement: {sourceName} at position {positionNumber}");

                // Announce the placement
                string announcement = $"Placed {sourceName} at position {positionNumber}";
                AccessibilityMod.Speak(announcement, true);

                MelonLogger.Msg("Placement completed");

                // Reset navigation state after successful placement
                selectedObject = null;
                currentMode = NavigationMode.ObjectSelection;
                currentObjectIndex = 0;
                currentTargetIndex = 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error placing item: {ex}");
                AccessibilityMod.Speak("Error placing item", true);

                // Reset state on error
                selectedObject = null;
                currentMode = NavigationMode.ObjectSelection;
            }
        }

        private static void ConnectPlacedItems()
        {
            if (selectedObject == null || currentTargetIndex < 0 || currentTargetIndex >= cachedDragObjects.Count)
                return;

            var targetObject = cachedDragObjects[currentTargetIndex];

            // Make sure both are placed items
            if (!selectedObject.isConnectable || !targetObject.isConnectable)
            {
                AccessibilityMod.Speak("Both items must be placed to create a connection", true);
                return;
            }

            // Don't connect to self
            if (targetObject == selectedObject)
            {
                AccessibilityMod.Speak("Cannot connect item to itself", true);
                return;
            }

            string sourceName = GetObjectName(selectedObject);
            string targetName = GetObjectName(targetObject);

            try
            {
                MelonLogger.Msg($"Creating connection between {sourceName} and {targetName}");

                // Get the ClueConnector to create the pin/connection
                var connector = selectedObject.clueConnector;
                if (connector == null)
                {
                    MelonLogger.Error("ClueConnector is null");
                    AccessibilityMod.Speak("Error: Connection manager not found", true);
                    return;
                }

                // Get IDs for the connection
                string id1 = selectedObject.gameObject.name.Replace("Item_", "");
                string id2 = targetObject.gameObject.name.Replace("Item_", "");

                MelonLogger.Msg($"Attempting connection with IDs: {id1} and {id2}");

                // Check if pin already exists between these items (only in current puzzle instance)
                var existingPins = GetCurrentPins();
                foreach (var existingPin in existingPins)
                {
                    if ((existingPin.id1 == id1 && existingPin.id2 == id2) || (existingPin.id1 == id2 && existingPin.id2 == id1))
                    {
                        MelonLogger.Msg($"Connection already exists between {id1} and {id2}");
                        AccessibilityMod.Speak("Connection already exists between these items", true);
                        return;
                    }
                }

                // Create a pin object from the prefab
                try
                {
                    // Create the pin GameObject from the prefab
                    GameObject pinObj = UnityEngine.Object.Instantiate(connector.pinPrefab);
                    var pinComponent = pinObj.GetComponent<ClueConnectorPin>();

                    if (pinComponent == null)
                    {
                        MelonLogger.Error("Created pin has no ClueConnectorPin component");
                        UnityEngine.Object.Destroy(pinObj);
                        AccessibilityMod.Speak("Error: Invalid pin object", true);
                        return;
                    }

                    // Initialize the pin with ALL required references
                    pinComponent.gc = activeLogic;
                    pinComponent.clueConnectorOwn = connector;
                    pinComponent.id1 = id1;
                    pinComponent.id2 = id2;

                    // Set localization IDs (might be needed for text/display)
                    pinComponent.locId1 = id1;
                    pinComponent.locId2 = id2;

                    // Position the pin at the midpoint between items
                    Vector3 midpoint = (selectedObject.transform.position + targetObject.transform.position) / 2;
                    pinObj.transform.position = midpoint;

                    MelonLogger.Msg($"Created pin between {sourceName} and {targetName}");
                    MelonLogger.Msg($"Pin IDs: {id1} and {id2}, locIds: {pinComponent.locId1} and {pinComponent.locId2}");

                    // Check if this is a valid connection
                    if (pinComponent.isValidConnection(id1, id2))
                    {
                        MelonLogger.Msg("Connection is valid - preparing popup");

                        // Refresh the popup data with our properly initialized pin
                        activeLogic.refreshPopupData(id1, id2, pinComponent);
                        MelonLogger.Msg("Called refreshPopupData with pin");

                        // Add the pin to the connector's pins list so it's tracked by the game
                        if (connector.pins != null)
                        {
                            connector.pins.Add(pinObj);
                            MelonLogger.Msg($"Added pin to connector.pins list (now has {connector.pins.Count} pins)");
                        }
                        else
                        {
                            MelonLogger.Warning("connector.pins is null - creating new list");
                            connector.pins = new Il2CppSystem.Collections.Generic.List<GameObject>();
                            connector.pins.Add(pinObj);
                        }

                        // Open the popup (only if not already open)
                        if (activeLogic.casePopup != null)
                        {
                            MelonLogger.Msg($"Popup GameObject active: {activeLogic.casePopup.gameObject.activeSelf}");
                            MelonLogger.Msg($"Popup isPopupOpen before Open(): {activeLogic.casePopup.isPopupOpen}");

                            // Only call Open() if the popup isn't already open
                            // refreshPopupData may have already opened it
                            if (!activeLogic.casePopup.isPopupOpen)
                            {
                                // Make sure the popup GameObject is active
                                if (!activeLogic.casePopup.gameObject.activeSelf)
                                {
                                    activeLogic.casePopup.gameObject.SetActive(true);
                                    MelonLogger.Msg("Activated popup GameObject");
                                }

                                activeLogic.casePopup.Open();
                                MelonLogger.Msg("Called casePopup.Open()");
                            }
                            else
                            {
                                MelonLogger.Msg("Popup already open, skipping Open() call");
                            }

                            // Check if it's open now
                            MelonLogger.Msg($"Popup isPopupOpen after: {activeLogic.casePopup.isPopupOpen}");

                            // Cache the connector and notify that popup was opened
                            cachedConnector = connector;
                            NotifyPopupOpened();
                            MelonLogger.Msg("Called NotifyPopupOpened() - keyboard navigation should now work");

                            // IMPORTANT: Reset our navigation state immediately so we're ready after popup closes
                            selectedObject = null;
                            currentMode = NavigationMode.ObjectSelection;
                            currentObjectIndex = 0;
                            currentTargetIndex = 0;
                            MelonLogger.Msg("Reset to ObjectSelection mode - game will handle popup");

                            // Check the pin reference
                            if (activeLogic.casePopup.clueConnectorPin != null)
                            {
                                MelonLogger.Msg($"Popup has pin reference: {activeLogic.casePopup.clueConnectorPin.id1} to {activeLogic.casePopup.clueConnectorPin.id2}");
                            }
                            else
                            {
                                MelonLogger.Warning("Popup has no pin reference!");
                            }

                            // Announce
                            string announcement = $"Opening question for connection: {sourceName} to {targetName}";
                            AccessibilityMod.Speak(announcement, true);
                        }
                        else
                        {
                            MelonLogger.Error("activeLogic.casePopup is null");
                            UnityEngine.Object.Destroy(pinObj);
                            AccessibilityMod.Speak("Error: Popup not found", true);
                        }
                    }
                    else
                    {
                        MelonLogger.Msg("Connection is not valid");
                        UnityEngine.Object.Destroy(pinObj);
                        AccessibilityMod.Speak("These items cannot be connected", true);
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error in connection workflow: {ex}");
                    AccessibilityMod.Speak("Error creating connection", true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error connecting placed items: {ex}");
                AccessibilityMod.Speak("Error creating connection", true);

                // Reset state on error
                selectedObject = null;
                currentMode = NavigationMode.ObjectSelection;
            }
        }

        private static void CancelConnection()
        {
            selectedObject = null;
            currentMode = NavigationMode.ObjectSelection;
            AccessibilityMod.Speak("Cancelled. Select an item.", true);
        }

        private static void RemoveCurrentPlacedItem()
        {
            if (currentObjectIndex < 0 || currentObjectIndex >= cachedDragObjects.Count)
                return;

            var obj = cachedDragObjects[currentObjectIndex];

            // Can only remove placed items that aren't connected yet
            if (!obj.isConnectable)
            {
                AccessibilityMod.Speak("This item is not placed", true);
                return;
            }

            // Check if item has any connections in current puzzle (pins)
            var currentPins = GetCurrentPins();
            string objId = obj.gameObject.name.Replace("Item_", "");
            foreach (var pin in currentPins)
            {
                if (pin.id1 == objId || pin.id2 == objId)
                {
                    AccessibilityMod.Speak("Cannot remove. Item has connections.", true);
                    return;
                }
            }

            string name = GetObjectName(obj);

            try
            {
                // Get the drop position this item was placed at
                ClueConnectorDropPos dropPos = null;
                if (itemToDropPos.TryGetValue(obj, out dropPos))
                {
                    // Reset the drop position type to empty
                    dropPos.setType("");
                    itemToDropPos.Remove(obj);
                    MelonLogger.Msg($"Reset drop position type for {name}");
                }

                // Return item to original position
                obj.GoToOriginalPosition();

                // Reset item state
                obj.isConnectable = false;
                obj.isDraggable = true;

                MelonLogger.Msg($"Removed {name} from board, returned to original position");
                AccessibilityMod.Speak($"Removed {name}", true);

                // Refresh caches to update lists
                RefreshCaches();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error removing item: {ex}");
                AccessibilityMod.Speak("Error removing item", true);
            }
        }

        private static void ReportProgress()
        {
            if (activeLogic == null)
                return;

            int current = activeLogic.actualGoodConnections;
            int total = activeLogic.maxConnections;
            string announcement = $"{current} of {total} connections made";

            if (current == total)
            {
                announcement += ". All connections complete!";
            }

            AccessibilityMod.Speak(announcement, true);
        }

        private static void ListItems()
        {
            if (currentMode == NavigationMode.ObjectSelection)
            {
                // List all objects
                if (cachedDragObjects.Count == 0)
                {
                    AccessibilityMod.Speak("No objects available", true);
                    return;
                }

                string list = $"{cachedDragObjects.Count} objects available: ";
                foreach (var obj in cachedDragObjects)
                {
                    string name = GetObjectName(obj);
                    string type = obj.type;
                    list += $"{name} ({type}), ";
                }

                AccessibilityMod.Speak(list, true);
            }
            else if (currentMode == NavigationMode.TargetSelection)
            {
                // List all drop positions
                if (cachedDropPositions.Count == 0)
                {
                    AccessibilityMod.Speak("No drop positions available", true);
                    return;
                }

                int emptyCount = 0;
                int occupiedCount = 0;
                foreach (var pos in cachedDropPositions)
                {
                    if (IsDropPositionOccupied(pos))
                        occupiedCount++;
                    else
                        emptyCount++;
                }

                string list = $"{cachedDropPositions.Count} positions: {emptyCount} empty, {occupiedCount} occupied. ";
                for (int i = 0; i < cachedDropPositions.Count; i++)
                {
                    var dropPos = cachedDropPositions[i];
                    bool occupied = IsDropPositionOccupied(dropPos);
                    list += $"Position {i + 1} {(occupied ? "occupied" : "empty")}, ";
                }

                AccessibilityMod.Speak(list, true);
            }
        }

        private static void ListSlots()
        {
            if (cachedDropPositions.Count == 0)
            {
                AccessibilityMod.Speak("No slots available", true);
                return;
            }

            int emptyCount = 0;
            int occupiedCount = 0;
            foreach (var pos in cachedDropPositions)
            {
                if (IsDropPositionOccupied(pos))
                    occupiedCount++;
                else
                    emptyCount++;
            }

            string list = $"{cachedDropPositions.Count} slots: {emptyCount} empty, {occupiedCount} occupied. ";
            for (int i = 0; i < cachedDropPositions.Count; i++)
            {
                var dropPos = cachedDropPositions[i];
                bool occupied = IsDropPositionOccupied(dropPos);
                string acceptsType = dropPos.type;
                if (string.IsNullOrEmpty(acceptsType))
                    acceptsType = "any";

                list += $"Slot {i + 1}, accepts {acceptsType}, {(occupied ? "occupied" : "empty")}. ";
            }

            AccessibilityMod.Speak(list, true);
        }

        private static void ShowHelp()
        {
            string help = "";

            if (currentMode == NavigationMode.ObjectSelection)
            {
                help = "Object selection mode. Left and right arrows or bumpers to navigate items. " +
                       "Space to select. If item is placed, you'll connect it. If unplaced, you'll place it. " +
                       "Backspace or Y to remove a placed item. L to list all objects. S to list all slots. H for help.";
            }
            else if (currentMode == NavigationMode.TargetSelection)
            {
                help = "Placement mode. Left and right arrows or bumpers to navigate drop positions. " +
                       "Space to place item. Backspace to cancel. S to list all slots. H for help.";
            }
            else if (currentMode == NavigationMode.ConnectionMode)
            {
                help = "Connection mode. Left and right arrows or bumpers to navigate placed items. " +
                       "Space to connect to selected item. Backspace to cancel. S to list all slots. H for help.";
            }
            else if (currentMode == NavigationMode.AnswerPopup)
            {
                help = "Answer popup mode. Up and down arrows to navigate options. Enter to select answer.";
            }

            // Add popup help if popup is open
            if (IsPopupOpen())
            {
                help = "Answer popup open. Up and down arrows to navigate options. Enter to select answer.";
            }

            AccessibilityMod.Speak(help, true);
        }

        private static void ListCompletedConnections()
        {
            // This would require tracking completed connections
            // For now, just report progress
            ReportProgress();
        }

        /// <summary>
        /// Gets pins belonging to the CURRENT puzzle instance only, avoiding stale pins from previous sessions.
        /// Uses connector.pins list instead of FindObjectsOfType to prevent false "already connected" results.
        /// </summary>
        private static List<ClueConnectorPin> GetCurrentPins()
        {
            var result = new List<ClueConnectorPin>();
            try
            {
                // Try to get connector from a cached drag object
                ClueConnector connector = cachedConnector;
                if (connector == null && cachedDragObjects.Count > 0)
                {
                    foreach (var obj in cachedDragObjects)
                    {
                        if (obj != null && obj.clueConnector != null)
                        {
                            connector = obj.clueConnector;
                            break;
                        }
                    }
                }

                if (connector != null && connector.pins != null)
                {
                    foreach (var pinObj in connector.pins)
                    {
                        if (pinObj != null)
                        {
                            var pinComponent = pinObj.GetComponent<ClueConnectorPin>();
                            if (pinComponent != null)
                                result.Add(pinComponent);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting current pins: {ex.Message}");
            }
            return result;
        }

        private static string GetObjectName(ClueConnectorDragObject obj)
        {
            if (obj == null)
                return "Unknown";

            try
            {
                // Try to get text from hintText GameObject
                if (obj.hintText != null)
                {
                    string text = AccessibilityMod.GetTextMeshProText(obj.hintText);
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }

                // Fallback to object name
                return obj.gameObject.name;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting object name: {ex}");
                return "Unknown";
            }
        }
    }
}
