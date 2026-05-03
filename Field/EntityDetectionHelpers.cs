using System;
using System.Linq;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using FFI_ScreenReader.Utils;
using PropertyEntity = Il2CppLast.Map.PropertyEntity;
using PropertyGotoMap = Il2CppLast.Map.PropertyGotoMap;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;
using ContentUtitlity = Il2CppLast.Systems.ContentUtitlity;
using MessageManager = Il2CppLast.Management.MessageManager;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Field
{
    /// <summary>
    /// Static helper methods for entity detection and property access.
    /// Extracted from EntityScanner for use by individual detector classes.
    /// </summary>
    public static class EntityDetectionHelpers
    {
        /// <summary>
        /// Gets the Property object from a FieldEntity via reflection.
        /// </summary>
        public static object GetEntityProperty(FieldEntity fieldEntity)
        {
            try
            {
                var entityType = fieldEntity.GetType();

                var propProp = entityType.GetProperty("Property",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (propProp != null)
                    return propProp.GetValue(fieldEntity);

                var propField = entityType.GetField("property",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (propField != null)
                    return propField.GetValue(fieldEntity);

                propField = entityType.GetField("_property",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (propField != null)
                    return propField.GetValue(fieldEntity);
            }
            catch { } // IL2CPP property/field may not exist
            return null;
        }

        /// <summary>
        /// Gets the ObjectType from a property object.
        /// ObjectType 3 = Map Exit (GotoMap), ObjectType 0 = PointIn (entry points).
        /// </summary>
        public static int GetObjectType(object propertyObj)
        {
            if (propertyObj == null) return -1;

            try
            {
                var propType = propertyObj.GetType();

                var prop = propType.GetProperty("ObjectType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(propertyObj);
                    if (value != null)
                        return Convert.ToInt32(value);
                }

                var field = propType.GetField("objectType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(propertyObj);
                    if (value != null)
                        return Convert.ToInt32(value);
                }
            }
            catch { } // IL2CPP field may not exist on type
            return -1;
        }

        /// <summary>
        /// Gets the destination map ID from a FieldEntity by accessing its Property as PropertyGotoMap.
        /// </summary>
        public static int GetGotoMapDestinationId(FieldEntity fieldEntity)
        {
            try
            {
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                    return -1;

                var gotoMapProperty = property.TryCast<PropertyGotoMap>();
                if (gotoMapProperty != null)
                {
                    int mapId = gotoMapProperty.MapId;
                    if (mapId > 0)
                        return mapId;
                }
                else
                {
                    int tmeId = property.TmeId;
                    if (tmeId > 0)
                        return tmeId;
                }
            }
            catch { } // Property cast may fail in IL2CPP
            return -1;
        }

        /// <summary>
        /// Resolves a destination map ID to its localized name.
        /// </summary>
        public static string ResolveMapName(int destMapId)
        {
            if (destMapId <= 0) return "";

            try
            {
                return MapNameResolver.GetMapExitName(destMapId);
            }
            catch
            {
                return $"Map {destMapId}";
            }
        }

        /// <summary>
        /// Gets the contents description for a treasure chest.
        /// </summary>
        public static string GetTreasureContents(object propertyObj)
        {
            if (propertyObj == null) return "";

            try
            {
                var propType = propertyObj.GetType();

                var gilProp = propType.GetProperty("GilAmount");
                if (gilProp != null)
                {
                    var gilValue = gilProp.GetValue(propertyObj);
                    if (gilValue != null)
                    {
                        int gilAmount = Convert.ToInt32(gilValue);
                        if (gilAmount > 0)
                            return $"{gilAmount} Gil";
                    }
                }

                string[] propNames = { "ContentId", "ItemId", "ContentsId", "RewardId", "itemId" };
                foreach (var name in propNames)
                {
                    var prop = propType.GetProperty(name);
                    if (prop != null)
                    {
                        var value = prop.GetValue(propertyObj);
                        if (value != null)
                        {
                            int contentId = Convert.ToInt32(value);
                            if (contentId > 0)
                            {
                                string itemName = ResolveItemName(contentId);
                                if (!string.IsNullOrEmpty(itemName))
                                    return itemName;
                                return $"Item {contentId}";
                            }
                        }
                    }
                }
            }
            catch { } // IL2CPP field may not exist on type
            return "";
        }

        /// <summary>
        /// Resolves a content ID to a localized item name.
        /// </summary>
        public static string ResolveItemName(int contentId)
        {
            if (contentId <= 0) return null;

            try
            {
                string mesId = ContentUtitlity.GetMesIdItemName(contentId);
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string name = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrEmpty(name))
                            return TextUtils.StripIconMarkup(name);
                    }
                }
            }
            catch { } // IL2CPP message resolution may fail

            return null;
        }

        /// <summary>
        /// Gets the entity name from PropertyEntity.Name for NPCs and interactive entities.
        /// Falls back to MessageManager resolution. Japanese names translated via EntityTranslator.
        /// </summary>
        public static string GetEntityNameFromProperty(FieldEntity fieldEntity)
        {
            try
            {
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                    return null;

                string name = property.Name;

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                if (name.StartsWith("mes_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("sys_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("field_", StringComparison.OrdinalIgnoreCase))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(name, false);
                        if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != name)
                            return EntityTranslator.Translate(localizedName);
                    }
                }

                string nameLower = name.ToLower();
                if (nameLower == "event" || nameLower == "eventtrigger" || nameLower == "event trigger" || nameLower == "pointin")
                    return null;

                return EntityTranslator.Translate(name);
            }
            catch { } // IL2CPP property access may fail
            return null;
        }

        /// <summary>
        /// Cleans up an object name for display.
        /// Translates Japanese GameObject names (e.g. "1:村人(男性)") so the fallback path used
        /// when PropertyEntity.Name is null/empty still produces a localized announcement.
        /// </summary>
        public static string CleanObjectName(string name, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return defaultName;

            name = name.Replace("(Clone)", "").Trim();

            if (name.Length < 2 || name.All(c => char.IsDigit(c) || c == '_'))
                return defaultName;

            if (name.StartsWith("_"))
                return defaultName;

            return EntityTranslator.Translate(name);
        }

        // Keyword → display class. Order matters: more-specific keywords first so they win.
        // Dialogue text for examined objects in FF1 follows patterns like
        //   English: "It's a gravestone.", "There's a sign here."
        //   Japanese: "墓石だ", "墓標だ"
        // A substring match is enough to classify. Both EN and JA forms are listed so the
        // classifier works regardless of the game's current language setting.
        private static readonly (string Keyword, string Label)[] DialogueClassKeywords = new[]
        {
            // English keywords — more specific multi-word forms before single-word generics
            ("grave marker", "Grave Marker"),
            ("gravestone", "Gravestone"),
            ("tombstone", "Tombstone"),
            ("tomb", "Tomb"),
            ("signpost", "Sign"),
            ("sign", "Sign"),
            ("bookcase", "Bookcase"),
            ("bookshelf", "Bookshelf"),
            ("altar", "Altar"),
            ("statue", "Statue"),
            ("throne", "Throne"),
            ("boulder", "Boulder"),
            ("rock", "Rock"),
            ("crystal", "Crystal"),
            ("orb", "Orb"),
            ("pedestal", "Pedestal"),
            ("plate", "Plate"),
            ("barrel", "Barrel"),
            ("crate", "Crate"),
            ("pot", "Pot"),
            ("vase", "Vase"),
            ("jar", "Jar"),
            ("bed", "Bed"),
            ("table", "Table"),
            ("chair", "Chair"),
            ("piano", "Piano"),
            ("painting", "Painting"),
            ("mirror", "Mirror"),
            ("clock", "Clock"),
            ("lever", "Lever"),
            ("switch", "Switch"),
            ("button", "Button"),
            ("chest", "Chest"),
            ("gate", "Gate"),
            ("ladder", "Ladder"),
            ("stairs", "Stairs"),
            ("well", "Well"),
            ("fountain", "Fountain"),
            // Japanese keywords — for users playing in Japanese where dialogue is in JA.
            // Same labels as the EN matches; mod_text/translation handles the user-facing
            // localization at speak time.
            ("墓標", "Grave Marker"),
            ("墓石", "Gravestone"),
            ("看板", "Sign"),
            ("立て札", "Sign"),
            ("本棚", "Bookshelf"),
            ("祭壇", "Altar"),
            ("像", "Statue"),
            ("玉座", "Throne"),
            ("岩", "Rock"),
            ("クリスタル", "Crystal"),
            ("井戸", "Well"),
            ("噴水", "Fountain"),
            ("樽", "Barrel"),
            ("壺", "Pot"),
            ("ベッド", "Bed"),
        };

        private static readonly System.Collections.Generic.HashSet<string> loggedUnclassifiedKeys =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// Reads the MessageKey from a property (via IL2CPP TryCast to the subclasses that
        /// expose it), looks up the resolved dialogue, and matches it against a keyword list to
        /// classify the object (e.g. "Gravestone", "Sign"). Returns null if nothing matched, in
        /// which case caller falls back to "Interactive Object". Logs the unclassified key+text
        /// once per unique key so the keyword list can be extended without a code change.
        /// </summary>
        public static string ClassifyByDialogue(FieldEntity fieldEntity)
        {
            if (fieldEntity == null) return null;
            try
            {
                PropertyEntity property = fieldEntity.Property;
                if (property == null) return null;

                // PropertyDefaultEntity inherits PropertyTalk → we get MessageKey through it.
                string messageKey = null;
                var def = property.TryCast<Il2CppLast.Map.PropertyDefaultEntity>();
                if (def != null) messageKey = def.MessageKey;
                if (string.IsNullOrEmpty(messageKey)) return null;

                var mgr = MessageManager.Instance;
                if (mgr == null) return null;
                string text = mgr.GetMessage(messageKey, false);
                if (string.IsNullOrWhiteSpace(text) || text == messageKey) return null;

                string normalized = TextUtils.StripIconMarkup(text).ToLowerInvariant();
                foreach (var (kw, label) in DialogueClassKeywords)
                {
                    if (normalized.Contains(kw)) return label;
                }

                // No keyword matched — log once per key so we can extend the list later.
                if (loggedUnclassifiedKeys.Add(messageKey))
                {
                    string snippet = text.Replace('\n', ' ').Trim();
                    if (snippet.Length > 80) snippet = snippet.Substring(0, 80) + "…";
                    MelonLoader.MelonLogger.Msg($"[Classify] unmatched key={messageKey} text=\"{snippet}\"");
                }
                return null;
            }
            catch { return null; } // IL2CPP cast / message lookup may fail
        }

        /// <summary>
        /// Gets a meaningful name for an interactive object, trying various sources.
        /// </summary>
        public static string GetInteractiveObjectName(object propertyObj, string goName)
        {
            if (propertyObj != null)
            {
                try
                {
                    var objectIdProp = propertyObj.GetType().GetProperty("ObjectId");
                    if (objectIdProp != null)
                    {
                        var objectId = objectIdProp.GetValue(propertyObj);
                        if (objectId != null && (int)objectId > 0)
                            return $"Object {objectId}";
                    }
                }
                catch { } // IL2CPP field may not exist on type
            }

            string cleaned = CleanObjectName(goName, "");
            if (!string.IsNullOrEmpty(cleaned) && cleaned != "FieldMapObjectDefault")
                return cleaned;

            return "Interactive Object";
        }

        /// <summary>
        /// Checks if a treasure entity has been opened.
        /// </summary>
        public static bool CheckIfTreasureOpened(FieldEntity fieldEntity)
        {
            try
            {
                var treasureBox = fieldEntity.TryCast<FieldTresureBox>();
                if (treasureBox != null)
                {
                    var isOpenField = treasureBox.GetType().GetField("isOpen",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (isOpenField != null)
                        return (bool)isOpenField.GetValue(treasureBox);

                    var isOpenProp = treasureBox.GetType().GetProperty("isOpen");
                    if (isOpenProp != null)
                        return (bool)isOpenProp.GetValue(treasureBox);
                }

                var prop = fieldEntity.GetType().GetProperty("isOpened") ??
                           fieldEntity.GetType().GetProperty("isOpen");
                if (prop != null)
                    return (bool)prop.GetValue(fieldEntity);

                var field = fieldEntity.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ??
                    fieldEntity.GetType().GetField("isOpened",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                    return (bool)field.GetValue(fieldEntity);
            }
            catch { } // IL2CPP field may not exist on type
            return false;
        }

        /// <summary>
        /// Gets a display name for a vehicle entity from property/GO name.
        /// </summary>
        public static string GetVehicleNameFromProperty(string goName, string typeName)
        {
            string nameLower = goName.ToLower();

            if (nameLower.Contains("airship") || typeName.Contains("AirShip"))
                return T("Airship");
            if (nameLower.Contains("canoe"))
                return T("Canoe");
            if (nameLower.Contains("ship") || nameLower.Contains("boat"))
                return T("Ship");

            return T("Vehicle");
        }

        /// <summary>
        /// Gets the TransportationType enum value from vehicle name.
        /// Returns 0 for unknown types (filtered out by callers).
        /// </summary>
        public static int GetVehicleTypeFromName(string vehicleName)
        {
            string nameLower = vehicleName.ToLower();

            if (nameLower.Contains("airship"))
                return 3;
            if (nameLower.Contains("canoe"))
                return 5;
            if (nameLower.Contains("ship") || nameLower.Contains("boat"))
                return 2;

            return 0;
        }

        /// <summary>
        /// Checks if an entity belongs to the current map by examining its parent hierarchy.
        /// </summary>
        public static bool IsEntityOnCurrentMap(FieldEntity fieldEntity, string currentMapAsset)
        {
            if (fieldEntity == null || string.IsNullOrEmpty(currentMapAsset))
                return true;

            try
            {
                var transform = fieldEntity.gameObject.transform;
                var parent = transform.parent;
                int depth = 0;
                string assetLower = currentMapAsset.ToLower();

                while (parent != null && depth < 10)
                {
                    string parentName = parent.name;
                    string parentLower = parentName.ToLower();

                    if (parentLower.StartsWith("map_"))
                    {
                        if (parentLower.Contains(assetLower))
                            return true;
                        return false;
                    }

                    parent = parent.parent;
                    depth++;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
