using System;
using System.Collections.Generic;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Data.Master;
using Il2CppLast.Map;

namespace FFI_ScreenReader.Field
{
    /// <summary>
    /// Resolves map IDs to human-readable localized area names.
    /// Uses the game's MasterManager and MessageManager to convert map IDs
    /// to localized display names (e.g., "Chaos Shrine 1F").
    /// </summary>
    public static class MapNameResolver
    {
        // Cache resolved names to avoid repeated lookups
        private static Dictionary<int, string> mapNameCache = new Dictionary<int, string>();

        /// <summary>
        /// Gets the name of the current map the player is on.
        /// Uses FieldMapProvisionInformation (preferred) or UserDataManager (fallback).
        /// </summary>
        /// <returns>Localized map name, or "Unknown" if unable to determine</returns>
        public static string GetCurrentMapName()
        {
            try
            {
                int currentMapId = GetCurrentMapIdInternal();
                if (currentMapId <= 0)
                    return "Unknown";

                string resolvedName = TryResolveMapNameById(currentMapId);

                if (!string.IsNullOrEmpty(resolvedName))
                    return resolvedName;

                return $"Map {currentMapId}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapNameResolver] Error getting current map name: {ex.Message}");
                return "Unknown";
            }
        }

        /// <summary>
        /// Gets the current map ID from FieldMapProvisionInformation (preferred) or UserDataManager (fallback).
        /// </summary>
        private static int GetCurrentMapIdInternal()
        {
            // Try FieldMapProvisionInformation first - this tracks the actual displayed map
            try
            {
                var fieldMapInfo = FieldMapProvisionInformation.Instance;
                if (fieldMapInfo != null)
                {
                    int mapId = fieldMapInfo.CurrentMapId;
                    if (mapId > 0)
                    {
                        return mapId;
                    }
                }
            }
            catch { }

            // Fallback to UserDataManager
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                {
                    return userDataManager.CurrentMapId;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Gets a human-readable name for a map exit destination by its map ID.
        /// This resolves the DESTINATION map name, not the current map.
        /// </summary>
        /// <param name="destMapId">The destination map ID from the exit's property (e.g., TmeId, MapId)</param>
        /// <returns>Localized destination map name, or formatted map ID if resolution fails</returns>
        public static string GetMapExitName(int destMapId)
        {
            if (destMapId <= 0)
                return "";

            // Check cache first
            if (mapNameCache.TryGetValue(destMapId, out string cachedName))
                return cachedName;

            // Try to resolve using the destination MapId with Map and Area master data
            string resolvedName = TryResolveMapNameById(destMapId);
            if (!string.IsNullOrEmpty(resolvedName))
            {
                mapNameCache[destMapId] = resolvedName;
                return resolvedName;
            }

            // Fallback: Just show the map ID
            string fallback = $"Map {destMapId}";
            mapNameCache[destMapId] = fallback;
            return fallback;
        }

        /// <summary>
        /// Attempts to resolve a map ID to a localized area name using Map and Area master data.
        /// </summary>
        /// <param name="mapId">The map ID to resolve</param>
        /// <returns>Localized area name with floor/title, or null if resolution fails</returns>
        public static string TryResolveMapNameById(int mapId)
        {
            try
            {
                // Get MasterManager instance
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                {
                    MelonLogger.Warning("[MapNameResolver] MasterManager.Instance is null");
                    return null;
                }

                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                {
                    MelonLogger.Warning("[MapNameResolver] MessageManager.Instance is null");
                    return null;
                }

                // Get the Map master data (contains AreaId and MapTitle)
                var mapList = masterManager.GetList<Map>();
                if (mapList == null)
                {
                    MelonLogger.Warning("[MapNameResolver] Could not get Map list from MasterManager");
                    return null;
                }

                if (!mapList.ContainsKey(mapId))
                {
                    MelonLogger.Msg($"[MapNameResolver] Map ID {mapId} not found in master data");
                    return null;
                }

                var map = mapList[mapId];
                if (map == null)
                {
                    return null;
                }

                // Get the area ID from the map
                int areaId = map.AreaId;

                // Get the Area master data
                var areaList = masterManager.GetList<Area>();
                if (areaList == null || !areaList.ContainsKey(areaId))
                {
                    MelonLogger.Msg($"[MapNameResolver] Area ID {areaId} not found for map {mapId}");
                    return null;
                }

                var area = areaList[areaId];
                if (area == null)
                {
                    return null;
                }

                // Get localized area name (e.g., "Chaos Shrine", "Cornelia")
                string areaNameKey = area.AreaName;
                string areaName = null;
                if (!string.IsNullOrEmpty(areaNameKey))
                {
                    areaName = messageManager.GetMessage(areaNameKey, false);
                }

                // Get localized map title (e.g., "1F", "B1", "Entrance")
                string mapTitleKey = map.MapTitle;
                string mapTitle = null;
                if (!string.IsNullOrEmpty(mapTitleKey) && mapTitleKey != "None" && mapTitleKey.ToLower() != "none")
                {
                    mapTitle = messageManager.GetMessage(mapTitleKey, false);
                }

                // If no map title from message, try using Floor field directly
                if (string.IsNullOrEmpty(mapTitle))
                {
                    int floor = map.Floor;
                    if (floor > 0)
                    {
                        mapTitle = $"{floor}F";
                    }
                    else if (floor < 0)
                    {
                        mapTitle = $"B{-floor}";
                    }
                }

                // Combine area name and map title
                if (!string.IsNullOrEmpty(areaName) && !string.IsNullOrEmpty(mapTitle))
                {
                    return $"{areaName} {mapTitle}";
                }
                else if (!string.IsNullOrEmpty(areaName))
                {
                    return areaName;
                }
                else if (!string.IsNullOrEmpty(mapTitle))
                {
                    return mapTitle;
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapNameResolver] Error resolving map ID {mapId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the asset name for the current map (e.g., "Map_20020" for Cornelia).
        /// Used for filtering entities by their parent hierarchy.
        /// </summary>
        public static string GetCurrentMapAssetName()
        {
            try
            {
                int currentMapId = GetCurrentMapIdInternal();
                if (currentMapId <= 0)
                    return null;

                return GetMapAssetName(currentMapId);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapNameResolver] Error getting current map asset name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the asset name for a specific map ID.
        /// </summary>
        public static string GetMapAssetName(int mapId)
        {
            if (mapId <= 0)
                return null;

            try
            {
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                var mapList = masterManager.GetList<Map>();
                if (mapList == null || !mapList.ContainsKey(mapId))
                    return null;

                var map = mapList[mapId];
                return map?.AssetName;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapNameResolver] Error getting asset name for map {mapId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clears the map name cache. Call this when changing game state significantly.
        /// </summary>
        public static void ClearCache()
        {
            mapNameCache.Clear();
        }
    }
}
