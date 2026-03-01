using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using System.Collections.Generic;

namespace ChickenPoliceAccessibility
{
    /// <summary>
    /// Provides custom accessibility descriptions for specific inventory items.
    /// Add item names and their descriptions to the dictionary below.
    /// </summary>
    public static class ItemDescriptions
    {
        // Dictionary of item IDs to custom descriptions
        // IMPORTANT: Use item IDs (like "loveletter"), NOT display names (like "Love letter")
        // Item IDs are language-independent and can be found in the log when you use an item
        private static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>()
        {
            // Love letter item
            { "loveletter", @"I–
I know I don't exist.
I don't exist because you don't see me.
But… I'm not what you think I am.
You don't think about me, do you? Never. No.
I think of you. Every day! Every minute! Always!
In my dreams, I have become one with him.
One soul…
You know who I am thinking about, right?
Can you feel it? Can you feel that I'm there too?
With you… Do you feel it, right?

I can't hold it in myself for long. Forgive me!
Forgive me! Forgive me, please. I'm sorry. But
I can't help it!
The world is crashing down. The whole
world just rotting and rotting… everything
is rotting around me.
I have to get out of here! To become one
with my destiny.
And one with you.
Forgive me!" },

            // Letter item
            { "letter", @"I know Molly very well.
Please note this when deciding whether or not to accept my assignment.
Miss Ibanez is a trusted friend!
Treat her as a gentleman.
– N" },

            // Wessler's photo
            { "wesslers_photo", @"The framed photograph captures a composed feline woman standing between two tall rat-headed men, all three dressed in formal suits. The woman, centered and wearing a crisp white outfit, appears to be the focus of the portrait, while the two rat men flank her like solemn attendants. Their straight postures and the staged quality of the shot suggest an important moment preserved on film, hinting at a shared past or a bond defined by status, protection, or significance known only to those in the picture." },

            // Asylum flyer
            { "flyer", @"LET THERE BE PEACE FOREVER
Mental institution for ill and damaged minds

WE ARE WAITING FOR YOU!
Call us! From Clawville – 555-966
Clawville state. Just follow the Asylum road east. Bush-Marsh 966" },

            // Add more items here as needed
            // To find an item's ID: use the item and check the log for:
            //   "[Item Description] No custom description for item: [ITEM_ID]"
            // Then add an entry like:
            //   { "itemid", @"Your description text here..." },
        };

        // Dictionary of world object IDs to custom descriptions
        // These are for interactable objects in the game world (ButtonType.OBJECT)
        // IMPORTANT: Use object IDs from locationObject.id, NOT display names
        // Object IDs can be found in the log when you interact with an object
        private static readonly Dictionary<string, string> objectDescriptions = new Dictionary<string, string>()
        {
            // Police Department objects
            { "220:13", @"A ribbon runs on the lower side of the crest with the following words: ""United in everlasting peace"". On the left side stands an antropomorphic lion and a bird like a stork. On the right side stands a fox and a sheep. Between the animals is a crest devided into four sections. Each section depicts a gauntleted hand holding a different item such as a dagger, a white flag, some kind of a branch and something that looks like a chicken leg. The stork and sheep are noticeably smaller than the lion and the fox figures." },

            // Add more objects here as needed
            // To find an object's ID: interact with the object and check the log for:
            //   "[Object Description] No custom description for object: [OBJECT_ID]"
            // Then add an entry like:
            //   { "objectid", @"Your description text here..." },
        };

        /// <summary>
        /// Checks if an item has a custom description and announces it.
        /// Returns true if a description was found and announced.
        /// </summary>
        public static bool TryAnnounceDescription(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return false;

            if (descriptions.TryGetValue(itemName, out string description))
            {
                MelonLogger.Msg($"Reading custom description for: {itemName}");
                AccessibilityMod.Speak(description, true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds or updates a custom description for an item.
        /// </summary>
        public static void AddDescription(string itemName, string description)
        {
            if (string.IsNullOrEmpty(itemName))
                return;

            descriptions[itemName] = description;
            MelonLogger.Msg($"Added/updated custom description for: {itemName}");
        }

        /// <summary>
        /// Checks if an item has a custom description.
        /// </summary>
        public static bool HasDescription(string itemName)
        {
            return !string.IsNullOrEmpty(itemName) && descriptions.ContainsKey(itemName);
        }

        /// <summary>
        /// Checks if a world object has a custom description and announces it.
        /// Returns true if a description was found and announced.
        /// </summary>
        public static bool TryAnnounceObjectDescription(string objectId)
        {
            if (string.IsNullOrEmpty(objectId))
                return false;

            if (objectDescriptions.TryGetValue(objectId, out string description))
            {
                MelonLogger.Msg($"Reading custom description for object: {objectId}");
                AccessibilityMod.Speak(description, true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds or updates a custom description for a world object.
        /// </summary>
        public static void AddObjectDescription(string objectId, string description)
        {
            if (string.IsNullOrEmpty(objectId))
                return;

            objectDescriptions[objectId] = description;
            MelonLogger.Msg($"Added/updated custom description for object: {objectId}");
        }

        /// <summary>
        /// Checks if a world object has a custom description.
        /// </summary>
        public static bool HasObjectDescription(string objectId)
        {
            return !string.IsNullOrEmpty(objectId) && objectDescriptions.ContainsKey(objectId);
        }
    }

    /// <summary>
    /// Harmony patch to announce custom descriptions when items are used.
    /// </summary>
    [HarmonyPatch(typeof(InventoryItem), "Execute")]
    public class InventoryItem_Execute_CustomDescription_Patch
    {
        static void Prefix(InventoryItem __instance)
        {
            try
            {
                if (__instance == null || string.IsNullOrEmpty(__instance.item))
                    return;

                string itemId = __instance.item;
                string gameObjectName = __instance.gameObject?.name ?? "";

                // Check if this item has a custom description
                bool hasDescription = ItemDescriptions.HasDescription(itemId);

                // If not found, try GameObject name as fallback
                string keyToUse = itemId;
                if (!hasDescription && !string.IsNullOrEmpty(gameObjectName))
                {
                    hasDescription = ItemDescriptions.HasDescription(gameObjectName);
                    if (hasDescription)
                    {
                        keyToUse = gameObjectName;
                    }
                }

                if (hasDescription)
                {
                    MelonLogger.Msg($"[Item Description] Reading custom description for item: {itemId}");

                    // Announce using the item first
                    AccessibilityMod.Speak($"Using {itemId}", true);

                    // Wait a moment, then read the description
                    string finalKey = keyToUse;
                    System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                    {
                        ItemDescriptions.TryAnnounceDescription(finalKey);
                    });
                }
                // Log when an item is used that doesn't have a custom description
                // This helps identify items that might need descriptions added
                else
                {
                    MelonLogger.Msg($"[Item Description] No custom description for item: {itemId} (GameObject: {gameObjectName})");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error in custom description patch: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
