using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using System.Text;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Handles accessibility features for the in-game notebook interface.
    /// Provides screen reader announcements for navigation between pages and categories.
    /// </summary>
    public static class NotebookAccessibility
    {
        private static Notebook.PageType lastAnnouncedCategory = (Notebook.PageType)(-1);
        private static int lastAnnouncedPageIdx = -1;
        private static int lastAnnouncedCharacterIdx = -1;

        /// <summary>
        /// Reads all statistics from the NotebookStats page and announces them.
        /// Handles the notebook spread layout (two pages side-by-side).
        /// </summary>
        private static void ReadAllStats()
        {
            try
            {

                // Find the active notebook to get current page index
                var notebooks = UnityEngine.Object.FindObjectsOfType<Notebook>();
                Notebook activeNotebook = null;

                if (notebooks != null && notebooks.Count > 0)
                {
                    foreach (var nb in notebooks)
                    {
                        if (nb != null && nb.gameObject.activeInHierarchy && nb.enabled)
                        {
                            activeNotebook = nb;
                            break;
                        }
                    }
                }

                if (activeNotebook == null)
                {
                    MelonLogger.Warning("No active notebook found");
                    AccessibilityMod.Speak("Cannot read statistics - notebook not active", true);
                    return;
                }

                var announcement = new StringBuilder();
                announcement.Append("Statistics. ");

                // Try to find NotebookStats (only exists on page 1)
                var statsComponents = UnityEngine.Object.FindObjectsOfType<NotebookStats>();
                NotebookStats stats = null;

                if (statsComponents != null && statsComponents.Count > 0)
                {
                    foreach (var component in statsComponents)
                    {
                        if (component != null && component.gameObject.activeInHierarchy)
                        {
                            stats = component;
                            break;
                        }
                    }
                }

                // Only announce general stats if on page 1 (where NotebookStats exists)
                if (stats != null)
                {
                    if (stats.StatQuestPoints != null && !string.IsNullOrEmpty(stats.StatQuestPoints.text))
                        announcement.Append($"Quest points: {stats.StatQuestPoints.text}. ");

                    if (stats.StatScenesVisited != null && !string.IsNullOrEmpty(stats.StatScenesVisited.text))
                        announcement.Append($"Scenes visited: {stats.StatScenesVisited.text}. ");

                    if (stats.StatAchievementsUnlocked != null && !string.IsNullOrEmpty(stats.StatAchievementsUnlocked.text))
                        announcement.Append($"Achievements unlocked: {stats.StatAchievementsUnlocked.text}. ");

                    if (stats.StatCodexFound != null && !string.IsNullOrEmpty(stats.StatCodexFound.text))
                        announcement.Append($"Codex entries found: {stats.StatCodexFound.text}. ");

                    if (stats.PersonalInfosFound != null && !string.IsNullOrEmpty(stats.PersonalInfosFound.text))
                        announcement.Append($"Personal information found: {stats.PersonalInfosFound.text}. ");

                    if (stats.GalleryEntriesUnlocked != null && !string.IsNullOrEmpty(stats.GalleryEntriesUnlocked.text))
                        announcement.Append($"Gallery entries unlocked: {stats.GalleryEntriesUnlocked.text}. ");
                }

                // Find all StatsQuestioningCard components globally
                // Filter to only those that are actually visible (active in hierarchy and unlocked)
                var allCards = UnityEngine.Object.FindObjectsOfType<StatsQuestioningCard>();
                var visibleCards = new System.Collections.Generic.List<StatsQuestioningCard>();

                if (allCards != null && allCards.Length > 0)
                {
                    foreach (var card in allCards)
                    {
                        // A card is visible if:
                        // 1. The card GameObject is active
                        // 2. The unlocked container is active (not locked)
                        if (card != null &&
                            card.gameObject.activeInHierarchy &&
                            card.unlockedContainer != null &&
                            card.unlockedContainer.activeInHierarchy)
                        {
                            visibleCards.Add(card);
                        }
                    }
                }

                if (visibleCards.Count > 0)
                {
                    announcement.Append("Interrogation reports: ");
                    int reportCount = 0;

                        foreach (var card in visibleCards)
                        {
                            // Get character name
                            string characterName = "Unknown";
                            if (card.NameText != null && !string.IsNullOrEmpty(card.NameText.text))
                            {
                                characterName = card.NameText.text;
                            }
                            else if (!string.IsNullOrEmpty(card.character))
                            {
                                characterName = card.character;
                            }

                            // Each star has 2 Image components: FilledStar (always enabled) and EmptyStar (overlay)
                            // Star is FILLED when EmptyStar.enabled = false
                            // Star is EMPTY when EmptyStar.enabled = true (overlay covers filled star)
                            int starCount = 0;
                            if (card.Stars != null && card.Stars.Length > 0)
                            {
                                foreach (var starObj in card.Stars)
                                {
                                    if (starObj == null) continue;

                                    // Get both Image components (FilledStar and EmptyStar overlay)
                                    var images = starObj.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                                    if (images != null && images.Length >= 2)
                                    {
                                        // Second image component is the EmptyStar overlay
                                        var emptyStarOverlay = images[1];

                                        // If EmptyStar overlay is disabled, the star appears filled
                                        if (emptyStarOverlay != null && !emptyStarOverlay.enabled)
                                        {
                                            starCount++;
                                        }
                                    }
                                }
                            }

                            // Get stamp status (completion rank)
                            string stampStatus = "";
                            if (card.Stamp != null && card.Stamp.Length > 0)
                            {
                                foreach (var stampObj in card.Stamp)
                                {
                                    if (stampObj != null && stampObj.activeInHierarchy)
                                    {
                                        string stampName = stampObj.name;

                                        // Map stamp numbers to rank names
                                        if (stampName.Contains("1"))
                                            stampStatus = "Greenhorn";
                                        else if (stampName.Contains("2"))
                                            stampStatus = "New Guy";
                                        else if (stampName.Contains("3"))
                                            stampStatus = "Gumshoe";
                                        else if (stampName.Contains("4"))
                                            stampStatus = "Hard-Boiled";
                                        else if (stampName.Contains("5"))
                                            stampStatus = "Legend";
                                        else
                                            stampStatus = stampName.Replace("Stamp", "").Replace("stamp", "").Trim();

                                        break;
                                    }
                                }
                            }

                                // Get questions asked
                                string questionsAsked = "";
                                if (card.QuestionsAskedText != null && !string.IsNullOrEmpty(card.QuestionsAskedText.text))
                                {
                                    questionsAsked = card.QuestionsAskedText.text;
                                }

                                // Get focus accuracy
                                string focusAccuracy = "";
                                if (card.FocusAccuracyText != null && !string.IsNullOrEmpty(card.FocusAccuracyText.text))
                                {
                                    focusAccuracy = card.FocusAccuracyText.text;
                                }

                                // Build announcement for this interrogation
                                if (reportCount > 0)
                                {
                                    announcement.Append(", ");
                                }
                                announcement.Append($"{characterName}");

                                // Add stamp status first (most important info)
                                if (!string.IsNullOrEmpty(stampStatus))
                                {
                                    announcement.Append($", {stampStatus}");
                                }

                                // Add star rating
                                announcement.Append($", {starCount} stars");

                                // Add questions asked
                                if (!string.IsNullOrEmpty(questionsAsked))
                                {
                                    announcement.Append($", {questionsAsked} questions");
                                }

                                // Add focus accuracy
                                if (!string.IsNullOrEmpty(focusAccuracy))
                                {
                                    announcement.Append($", {focusAccuracy} accuracy");
                                }

                        reportCount++;
                    }

                    if (reportCount > 0)
                    {
                        announcement.Append(". ");
                    }
                }
                else
                {
                    announcement.Append("Interrogation reports: None on this page. ");
                }

                // Try to determine current rank (only if stats component exists)
                if (stats != null && stats.ranks != null && stats.ranks.Length > 0)
                {
                    // Check each rank to see which one is active (usually indicated by visibility or alpha)
                    foreach (var rank in stats.ranks)
                    {
                        if (rank != null && rank.gameObject.activeInHierarchy)
                        {
                            string rankName = GetRankName(rank.StatRankType);
                            if (!string.IsNullOrEmpty(rankName))
                            {
                                announcement.Append($"Current rank: {rankName}. ");
                                break;
                            }
                        }
                    }
                }

                if (announcement.Length > "Statistics. ".Length)
                {
                    AccessibilityMod.Speak(announcement.ToString(), true);
                }
                else
                {
                    MelonLogger.Warning("Announcement is empty - no stats to announce");
                    AccessibilityMod.Speak("No statistics to display", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error reading stats: {ex}");
                AccessibilityMod.Speak("Error reading statistics", true);
            }
        }

        /// <summary>
        /// Converts rank type enum to human-readable name.
        /// </summary>
        private static string GetRankName(NotebookStats.NotebookStatRankType rankType)
        {
            switch (rankType)
            {
                case NotebookStats.NotebookStatRankType.GREENHORN:
                    return "Greenhorn";
                case NotebookStats.NotebookStatRankType.NEWGUY:
                    return "New Guy";
                case NotebookStats.NotebookStatRankType.GUMSHOE:
                    return "Gumshoe";
                case NotebookStats.NotebookStatRankType.HARDBOILED:
                    return "Hard-Boiled";
                case NotebookStats.NotebookStatRankType.LEGEND:
                    return "Legend";
                default:
                    return "Unknown Rank";
            }
        }

        /// <summary>
        /// Announces the current notebook category and page when changed.
        /// </summary>
        private static void AnnounceNotebookState(Notebook notebook)
        {
            if (notebook == null) return;

            try
            {
                var currentCategory = notebook.currentCategory;
                int currentPageIdx = notebook.currentPageIdx;
                int currentCharacterIdx = notebook.characterSelectionIdx;

                // Only announce if something changed
                if (currentCategory == lastAnnouncedCategory &&
                    currentPageIdx == lastAnnouncedPageIdx &&
                    currentCharacterIdx == lastAnnouncedCharacterIdx)
                    return;

                var announcement = new StringBuilder();

                // Announce category if it changed
                if (currentCategory != lastAnnouncedCategory)
                {
                    string categoryName = GetCategoryName(currentCategory);
                    announcement.Append(categoryName);

                    // Add hint for stats page
                    if (currentCategory == Notebook.PageType.STATS)
                    {
                        announcement.Append(". Press R to read all statistics");
                    }

                    lastAnnouncedCategory = currentCategory;
                }

                // If in PEOPLEDETAIL mode, announce the character name when it changes
                if (currentCategory == Notebook.PageType.PEOPLEDETAIL &&
                    currentCharacterIdx != lastAnnouncedCharacterIdx)
                {
                    try
                    {
                        if (notebook.selectableCharacters != null &&
                            currentCharacterIdx >= 0 &&
                            currentCharacterIdx < notebook.selectableCharacters.Count)
                        {
                            var character = notebook.selectableCharacters[currentCharacterIdx];
                            if (character != null && !string.IsNullOrEmpty(character.character))
                            {
                                if (announcement.Length > 0)
                                    announcement.Append(": ");
                                announcement.Append(character.character);
                                lastAnnouncedCharacterIdx = currentCharacterIdx;
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"Could not announce character name: {ex.Message}");
                    }
                }

                // Announce page number if available
                if (notebook.unlockedPages != null && 
                    notebook.unlockedPages.ContainsKey(currentCategory))
                {
                    var pages = notebook.unlockedPages[currentCategory];
                    if (pages != null && pages.Count > 0)
                    {
                        int pageNumber = currentPageIdx + 1;
                        int totalPages = pages.Count;
                        
                        if (announcement.Length > 0)
                            announcement.Append(", ");
                        
                        announcement.Append($"page {pageNumber} of {totalPages}");
                    }
                }

                lastAnnouncedPageIdx = currentPageIdx;

                if (announcement.Length > 0)
                {
                    AccessibilityMod.Speak(announcement.ToString(), true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error announcing notebook state: {ex}");
            }
        }

        /// <summary>
        /// Converts PageType enum to human-readable category name.
        /// </summary>
        private static string GetCategoryName(Notebook.PageType pageType)
        {
            switch (pageType)
            {
                case Notebook.PageType.CLUES:
                    return "Clues";
                case Notebook.PageType.PEOPLE:
                    return "People";
                case Notebook.PageType.PEOPLEDETAIL:
                    return "Person Details";
                case Notebook.PageType.PLACES:
                    return "Places";
                case Notebook.PageType.CODEX:
                    return "Codex";
                case Notebook.PageType.STATS:
                    return "Statistics";
                default:
                    return "Unknown Category";
            }
        }

        /// <summary>
        /// Resets tracking when notebook is closed or disabled.
        /// </summary>
        private static void ResetTracking()
        {
            lastAnnouncedCategory = (Notebook.PageType)(-1);
            lastAnnouncedPageIdx = -1;
            lastAnnouncedCharacterIdx = -1;
        }

        #region Harmony Patches

        /// <summary>
        /// Patch for when notebook is enabled/opened.
        /// Announces the initial state.
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "OnEnable")]
        public class Notebook_OnEnable_Patch
        {
            static void Postfix(Notebook __instance)
            {
                try
                {
                    ResetTracking();
                    AccessibilityMod.Speak("Notebook opened", true);
                    AnnounceNotebookState(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.OnEnable patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for when notebook is disabled/closed.
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "OnDisable")]
        public class Notebook_OnDisable_Patch
        {
            static void Prefix()
            {
                try
                {
                    AccessibilityMod.Speak("Notebook closed", true);
                    ResetTracking();
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.OnDisable patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for page navigation (left/right).
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ChangePage")]
        public class Notebook_ChangePage_Patch
        {
            static void Postfix(Notebook __instance)
            {
                try
                {
                    AnnounceNotebookState(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ChangePage patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for category changes (up/down through bookmarks).
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ChangeCategory")]
        public class Notebook_ChangeCategory_Patch
        {
            static void Postfix(Notebook __instance)
            {
                try
                {
                    AnnounceNotebookState(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ChangeCategory patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for notebook page setup.
        /// Announces content when pages are displayed.
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ShowPages")]
        public class Notebook_ShowPages_Patch
        {
            static void Postfix(Notebook __instance)
            {
                try
                {
                    AnnounceNotebookState(__instance);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ShowPages patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for escape/back button in notebook.
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "OnKeyEsc")]
        public class Notebook_OnKeyEsc_Patch
        {
            static void Prefix()
            {
                try
                {
                    AccessibilityMod.Speak("Closing notebook", true);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.OnKeyEsc patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for character selection in people page.
        /// Only announces characters that are not locked (UNKNOWN state).
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ManagePeopleSelection")]
        public class Notebook_ManagePeopleSelection_Patch
        {
            static void Postfix(Notebook __instance)
            {
                try
                {
                    if (__instance.selectableCharacters != null &&
                        __instance.characterSelectionIdx >= 0 &&
                        __instance.characterSelectionIdx < __instance.selectableCharacters.Count)
                    {
                        var character = __instance.selectableCharacters[__instance.characterSelectionIdx];
                        if (character != null && !string.IsNullOrEmpty(character.character))
                        {
                            // Only announce if character is not locked (UNKNOWN = 0)
                            if (character.characterState == NotebookCharacter.CharacterState.UNKNOWN)
                            {
                                // Character is locked (shown as "?" to sighted players)
                                AccessibilityMod.Speak("Locked character", true);
                                return;
                            }

                            int position = __instance.characterSelectionIdx + 1;
                            int total = __instance.selectableCharacters.Count;
                            AccessibilityMod.Speak($"{character.character}, {position} of {total}", true);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ManagePeopleSelection patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for location selection in places page.
        /// Only announces locations that are not locked (UNKNOWN state).
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ManagePlacesSelection")]
        public class Notebook_ManagePlacesSelection_Patch
        {
            static void Postfix(Notebook __instance)
            {
                try
                {
                    if (__instance.selectableLocations != null &&
                        __instance.locationSelectionIdx >= 0 &&
                        __instance.locationSelectionIdx < __instance.selectableLocations.Count)
                    {
                        var location = __instance.selectableLocations[__instance.locationSelectionIdx];
                        if (location != null && !string.IsNullOrEmpty(location.LocationGroupName))
                        {
                            // Only announce if location is not locked (UNKNOWN = 0)
                            if (location.locationState == NotebookLocation.LocationState.UNKNOWN)
                            {
                                // Location is locked (shown as "?" to sighted players)
                                AccessibilityMod.Speak("Locked location", true);
                                return;
                            }

                            int position = __instance.locationSelectionIdx + 1;
                            int total = __instance.selectableLocations.Count;
                            AccessibilityMod.Speak($"{location.LocationGroupName}, {position} of {total}", true);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ManagePlacesSelection patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for clue selection in clues page.
        /// Handles both left and right pages in the notebook spread.
        /// The notebook displays two pages at once: currentPageIdx (left) and currentPageIdx+1 (right).
        /// selectionId indicates the clue across both pages.
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ManageClueSelection")]
        public class Notebook_ManageClueSelection_Patch
        {
            static void Postfix(Notebook __instance, bool shouldDeselect, bool modifyVisualSelection, int selectionId)
            {
                try
                {
                    // Don't announce when deselecting
                    if (shouldDeselect)
                        return;

                    if (__instance.selectableClues == null)
                        return;

                    // The selectableClues array is rebuilt for each spread and contains the current spread's pages.
                    // It always has the left page at index 0 and right page at index 1 (if it exists).
                    // currentPageIdx is a global page number, not an index into selectableClues.
                    // So we always start searching from index 0.

                    if (__instance.selectableClues.Count > 0)
                    {
                        var leftPageClues = __instance.selectableClues[0];

                        // If selectionId is within left page bounds, use left page
                        if (leftPageClues != null && selectionId < leftPageClues.Count)
                        {
                            var clue = leftPageClues[selectionId];
                            if (clue != null && !string.IsNullOrEmpty(clue.clue))
                            {
                                int position = selectionId + 1;
                                int total = leftPageClues.Count;
                                AccessibilityMod.Speak($"{clue.clue}, {position} of {total}", true);
                            }
                        }
                        // Otherwise, check the right page (index 1)
                        else if (__instance.selectableClues.Count > 1)
                        {
                            var rightPageClues = __instance.selectableClues[1];
                            int rightClueIdx = selectionId - (leftPageClues?.Count ?? 0);

                            if (rightPageClues != null &&
                                rightClueIdx >= 0 &&
                                rightClueIdx < rightPageClues.Count)
                            {
                                var clue = rightPageClues[rightClueIdx];
                                if (clue != null && !string.IsNullOrEmpty(clue.clue))
                                {
                                    int position = rightClueIdx + 1;
                                    int total = rightPageClues.Count;
                                    AccessibilityMod.Speak($"{clue.clue}, {position} of {total}", true);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ManageClueSelection patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for codex entry selection.
        /// Handles both left and right pages in the notebook spread.
        /// The notebook displays two pages at once: currentPageIdx (left) and currentPageIdx+1 (right).
        /// selectionId indicates the codex entry across both pages.
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ManageCodexSelection")]
        public class Notebook_ManageCodexSelection_Patch
        {
            static void Postfix(Notebook __instance, bool shouldDeselect, bool modifyVisualSelection, int selectionId)
            {
                try
                {
                    // Don't announce when deselecting
                    if (shouldDeselect)
                        return;

                    if (__instance.selectableCodex == null)
                        return;

                    // The selectableCodex array is rebuilt for each spread and contains the current spread's pages.
                    // It always has the left page at index 0 and right page at index 1 (if it exists).
                    // currentPageIdx is a global page number, not an index into selectableCodex.
                    // So we always start searching from index 0.

                    if (__instance.selectableCodex.Count > 0)
                    {
                        var leftPageCodex = __instance.selectableCodex[0];

                        // If selectionId is within left page bounds, use left page
                        if (leftPageCodex != null && selectionId < leftPageCodex.Count)
                        {
                            var codex = leftPageCodex[selectionId];
                            if (codex != null && !string.IsNullOrEmpty(codex.codex))
                            {
                                int position = selectionId + 1;
                                int total = leftPageCodex.Count;
                                AccessibilityMod.Speak($"{codex.codex}, {position} of {total}", true);
                            }
                        }
                        // Otherwise, check the right page (index 1)
                        else if (__instance.selectableCodex.Count > 1)
                        {
                            var rightPageCodex = __instance.selectableCodex[1];
                            int rightCodexIdx = selectionId - (leftPageCodex?.Count ?? 0);

                            if (rightPageCodex != null &&
                                rightCodexIdx >= 0 &&
                                rightCodexIdx < rightPageCodex.Count)
                            {
                                var codex = rightPageCodex[rightCodexIdx];
                                if (codex != null && !string.IsNullOrEmpty(codex.codex))
                                {
                                    int position = rightCodexIdx + 1;
                                    int total = rightPageCodex.Count;
                                    AccessibilityMod.Speak($"{codex.codex}, {position} of {total}", true);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ManageCodexSelection patch: {ex}");
                }
            }
        }

        /// <summary>
        /// Patch for personal info selection in people detail page.
        /// Handles both left and right pages in the notebook spread.
        /// The notebook displays two pages at once: currentPageIdx (left) and currentPageIdx+1 (right).
        /// selectionId indicates the personal info item across both pages.
        /// </summary>
        [HarmonyPatch(typeof(Notebook), "ManagePeopleDetailSelection")]
        public class Notebook_ManagePeopleDetailSelection_Patch
        {
            static void Postfix(Notebook __instance, bool shouldDeselect, bool modifyVisualSelection, int selectionId)
            {
                try
                {
                    // Don't announce when deselecting
                    if (shouldDeselect)
                        return;

                    if (__instance.selectablePersonalInfos == null)
                        return;

                    // The selectablePersonalInfos array is rebuilt for each spread and contains the current spread's pages.
                    // It always has the left page at index 0 and right page at index 1 (if it exists).
                    // currentPageIdx is a global page number, not an index into selectablePersonalInfos.
                    // So we always start searching from index 0.

                    if (__instance.selectablePersonalInfos.Count > 0)
                    {
                        var leftPageInfos = __instance.selectablePersonalInfos[0];

                        // If selectionId is within left page bounds, use left page
                        if (leftPageInfos != null && selectionId < leftPageInfos.Count)
                        {
                            var info = leftPageInfos[selectionId];
                            if (info != null)
                            {
                                int position = selectionId + 1;
                                int total = leftPageInfos.Count;

                                // Check if this is a card (character description) or regular personal info
                                string announcement = "";
                                if (info.is_card)
                                {
                                    // This is the character description card
                                    // Try to get the character name and description
                                    if (__instance.selectableCharacters != null &&
                                        __instance.characterSelectionIdx >= 0 &&
                                        __instance.characterSelectionIdx < __instance.selectableCharacters.Count)
                                    {
                                        var character = __instance.selectableCharacters[__instance.characterSelectionIdx];
                                        if (character != null && !string.IsNullOrEmpty(character.character))
                                        {
                                            announcement = $"{character.character} description";
                                        }
                                    }

                                    if (string.IsNullOrEmpty(announcement))
                                    {
                                        announcement = "Character description";
                                    }
                                }
                                else if (!string.IsNullOrEmpty(info.personal_info))
                                {
                                    announcement = info.personal_info;
                                }

                                if (!string.IsNullOrEmpty(announcement))
                                {
                                    AccessibilityMod.Speak($"{announcement}, {position} of {total}", true);
                                }
                            }
                        }
                        // Otherwise, check the right page (index 1)
                        else if (__instance.selectablePersonalInfos.Count > 1)
                        {
                            var rightPageInfos = __instance.selectablePersonalInfos[1];
                            int rightInfoIdx = selectionId - (leftPageInfos?.Count ?? 0);

                            if (rightPageInfos != null &&
                                rightInfoIdx >= 0 &&
                                rightInfoIdx < rightPageInfos.Count)
                            {
                                var info = rightPageInfos[rightInfoIdx];
                                if (info != null)
                                {
                                    int position = rightInfoIdx + 1;
                                    int total = rightPageInfos.Count;

                                    // Check if this is a card (character description) or regular personal info
                                    string announcement = "";
                                    if (info.is_card)
                                    {
                                        // This is the character description card
                                        // Try to get the character name and description
                                        if (__instance.selectableCharacters != null &&
                                            __instance.characterSelectionIdx >= 0 &&
                                            __instance.characterSelectionIdx < __instance.selectableCharacters.Count)
                                        {
                                            var character = __instance.selectableCharacters[__instance.characterSelectionIdx];
                                            if (character != null && !string.IsNullOrEmpty(character.character))
                                            {
                                                announcement = $"{character.character} description";
                                            }
                                        }

                                        if (string.IsNullOrEmpty(announcement))
                                        {
                                            announcement = "Character description";
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(info.personal_info))
                                    {
                                        announcement = info.personal_info;
                                    }

                                    if (!string.IsNullOrEmpty(announcement))
                                    {
                                        AccessibilityMod.Speak($"{announcement}, {position} of {total}", true);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"Error in Notebook.ManagePeopleDetailSelection patch: {ex}");
                }
            }
        }

        #endregion

        #region Keyboard Navigation (Polling-based)

        private static Notebook activeNotebook = null;

        /// <summary>
        /// Handles keyboard input for notebook navigation.
        /// Called from AccessibilityMod.OnUpdate()
        /// Q/E = Change category (triggers), A/D = Change page (bumpers), Enter = Select
        /// </summary>
        public static void HandleKeyboardInput()
        {
            try
            {
                // Find active notebook
                var notebooks = UnityEngine.Object.FindObjectsOfType<Notebook>();
                activeNotebook = null;

                if (notebooks != null && notebooks.Count > 0)
                {
                    foreach (var notebook in notebooks)
                    {
                        if (notebook != null && notebook.gameObject.activeInHierarchy && notebook.enabled)
                        {
                            activeNotebook = notebook;
                            break;
                        }
                    }
                }

                if (activeNotebook == null)
                    return;

                // R - Read all statistics (only on STATS page)
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.R))
                {
                    if (activeNotebook.currentCategory == Notebook.PageType.STATS)
                    {
                        ReadAllStats();
                        return; // Don't process other input
                    }
                }

                // Escape - Go back / close detail panel (same as controller B button)
                // Note: Backspace is handled by InteractableNavigator for world items,
                // so we only use Escape here to avoid conflicts
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                {
                    try
                    {
                        activeNotebook.OnKeyEsc();
                        AccessibilityMod.Speak("Back", true);
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"Error calling OnKeyEsc: {ex.Message}");
                    }
                }

                // Q - Next category (like left trigger)
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Q))
                {
                    activeNotebook.ChangeCategory(true); // true = up/next
                }

                // E - Previous category (like right trigger)
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.E))
                {
                    activeNotebook.ChangeCategory(false); // false = down/previous
                }

                // A - Previous page (like left bumper)
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.A))
                {
                    activeNotebook.ChangePage(false); // false = backward
                }

                // D - Next page (like right bumper)
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.D))
                {
                    activeNotebook.ChangePage(true); // true = forward
                }

                // Enter/Return - Open/read details of currently focused element
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Return) ||
                    UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.KeypadEnter))
                {
                    try
                    {
                        var currentCategory = activeNotebook.currentCategory;
                        bool success = false;

                        switch (currentCategory)
                        {
                            case Notebook.PageType.PEOPLE:
                                // Get current person and invoke its click action to open details
                                if (activeNotebook.selectableCharacters != null &&
                                    activeNotebook.characterSelectionIdx >= 0 &&
                                    activeNotebook.characterSelectionIdx < activeNotebook.selectableCharacters.Count)
                                {
                                    var character = activeNotebook.selectableCharacters[activeNotebook.characterSelectionIdx];
                                    if (character != null && character.onCharacterClick != null)
                                    {
                                        character.onCharacterClick.Invoke();
                                        success = true;
                                    }
                                }
                                break;

                            case Notebook.PageType.CLUES:
                                // Get current clue and invoke its click action
                                if (activeNotebook.selectableClues != null && activeNotebook.selectableClues.Count > 0)
                                {
                                    int clueIdx = activeNotebook.clueSelectionIdx;

                                    // Check left page
                                    if (activeNotebook.selectableClues.Count > 0)
                                    {
                                        var leftPageClues = activeNotebook.selectableClues[0];
                                        if (leftPageClues != null && clueIdx < leftPageClues.Count)
                                        {
                                            var clue = leftPageClues[clueIdx];
                                            if (clue != null && clue.onClueClick != null)
                                            {
                                                clue.onClueClick.Invoke();
                                                success = true;
                                            }
                                        }
                                        // Check right page
                                        else if (activeNotebook.selectableClues.Count > 1)
                                        {
                                            var rightPageClues = activeNotebook.selectableClues[1];
                                            int rightIdx = clueIdx - (leftPageClues?.Count ?? 0);
                                            if (rightPageClues != null && rightIdx >= 0 && rightIdx < rightPageClues.Count)
                                            {
                                                var clue = rightPageClues[rightIdx];
                                                if (clue != null && clue.onClueClick != null)
                                                {
                                                    clue.onClueClick.Invoke();
                                                    success = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                break;

                            case Notebook.PageType.CODEX:
                                // Get current codex entry and invoke its click action
                                if (activeNotebook.selectableCodex != null && activeNotebook.selectableCodex.Count > 0)
                                {
                                    int codexIdx = activeNotebook.codexSelectionIdx;

                                    // Check left page
                                    if (activeNotebook.selectableCodex.Count > 0)
                                    {
                                        var leftPageCodex = activeNotebook.selectableCodex[0];
                                        if (leftPageCodex != null && codexIdx < leftPageCodex.Count)
                                        {
                                            var codex = leftPageCodex[codexIdx];
                                            if (codex != null && codex.onCodexClick != null)
                                            {
                                                codex.onCodexClick.Invoke();
                                                success = true;
                                            }
                                        }
                                        // Check right page
                                        else if (activeNotebook.selectableCodex.Count > 1)
                                        {
                                            var rightPageCodex = activeNotebook.selectableCodex[1];
                                            int rightIdx = codexIdx - (leftPageCodex?.Count ?? 0);
                                            if (rightPageCodex != null && rightIdx >= 0 && rightIdx < rightPageCodex.Count)
                                            {
                                                var codex = rightPageCodex[rightIdx];
                                                if (codex != null && codex.onCodexClick != null)
                                                {
                                                    codex.onCodexClick.Invoke();
                                                    success = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                break;

                            case Notebook.PageType.PEOPLEDETAIL:
                                // Get current personal info and invoke its click action
                                if (activeNotebook.selectablePersonalInfos != null && activeNotebook.selectablePersonalInfos.Count > 0)
                                {
                                    int infoIdx = activeNotebook.personalInfoSelectionIdx;

                                    // Check left page
                                    if (activeNotebook.selectablePersonalInfos.Count > 0)
                                    {
                                        var leftPageInfos = activeNotebook.selectablePersonalInfos[0];
                                        if (leftPageInfos != null && infoIdx < leftPageInfos.Count)
                                        {
                                            var info = leftPageInfos[infoIdx];
                                            if (info != null && info.onPersonalInfoClick != null)
                                            {
                                                info.onPersonalInfoClick.Invoke();
                                                success = true;
                                            }
                                        }
                                        // Check right page
                                        else if (activeNotebook.selectablePersonalInfos.Count > 1)
                                        {
                                            var rightPageInfos = activeNotebook.selectablePersonalInfos[1];
                                            int rightIdx = infoIdx - (leftPageInfos?.Count ?? 0);
                                            if (rightPageInfos != null && rightIdx >= 0 && rightIdx < rightPageInfos.Count)
                                            {
                                                var info = rightPageInfos[rightIdx];
                                                if (info != null && info.onPersonalInfoClick != null)
                                                {
                                                    info.onPersonalInfoClick.Invoke();
                                                    success = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                break;

                            case Notebook.PageType.PLACES:
                                // Get current location and invoke its click action
                                if (activeNotebook.selectableLocations != null &&
                                    activeNotebook.locationSelectionIdx >= 0 &&
                                    activeNotebook.locationSelectionIdx < activeNotebook.selectableLocations.Count)
                                {
                                    var location = activeNotebook.selectableLocations[activeNotebook.locationSelectionIdx];
                                    if (location != null && location.onLocationClick != null)
                                    {
                                        location.onLocationClick.Invoke();
                                        success = true;
                                    }
                                }
                                break;
                        }

                        if (!success)
                        {
                            MelonLogger.Warning($"Could not open details for category: {currentCategory}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Error($"Error opening details: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in NotebookAccessibility.HandleKeyboardInput: {ex}");
            }
        }

        #endregion
    }
}