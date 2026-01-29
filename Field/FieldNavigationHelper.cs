using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using FFI_ScreenReader.Utils;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using FieldMap = Il2Cpp.FieldMap;
using TransportationInfo = Il2CppLast.Map.TransportationInfo;
using EventTriggerEntity = Il2CppLast.Entity.Field.EventTriggerEntity;
using MapRouteSearcher = Il2Cpp.MapRouteSearcher;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

namespace FFI_ScreenReader.Field
{
    /// <summary>
    /// Result of a pathfinding operation
    /// </summary>
    public class PathInfo
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int StepCount { get; set; }
        public string Description { get; set; }
        public List<Vector3> WorldPath { get; set; }

        public PathInfo()
        {
            Success = false;
            WorldPath = new List<Vector3>();
        }
    }

    /// <summary>
    /// Helper for field navigation and pathfinding.
    /// Uses the game's native pathfinding system.
    /// </summary>
    public static class FieldNavigationHelper
    {
        /// <summary>
        /// Maps FieldEntity to their vehicle type ID (populated from Transportation.ModelList)
        /// </summary>
        public static Dictionary<FieldEntity, int> VehicleTypeMap { get; } = new Dictionary<FieldEntity, int>();

        /// <summary>
        /// Clears the vehicle type map. Call on map transitions.
        /// </summary>
        public static void ResetTransportationDebug()
        {
            VehicleTypeMap.Clear();
        }

        /// <summary>
        /// Gets all field entities in the current map.
        /// </summary>
        public static List<FieldEntity> GetAllFieldEntities()
        {
            var results = new List<FieldEntity>();

            try
            {
                // Try cache first, then refresh (FindObjectOfType)
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap == null)
                {
                    fieldMap = GameObjectCache.Refresh<FieldMap>();
                }

                if (fieldMap == null)
                {
                    MelonLogger.Msg("[FieldNav] FieldMap not found");
                    return results;
                }

                if (fieldMap.fieldController == null)
                {
                    MelonLogger.Msg("[FieldNav] fieldController is null");
                    return results;
                }

                // Direct property access (IL2CppInterop wrapper - like FF3)
                var entityList = fieldMap.fieldController.entityList;
                if (entityList != null)
                {
                    foreach (var fieldEntity in entityList)
                    {
                        if (fieldEntity != null)
                            results.Add(fieldEntity);
                    }
                }

                // Also check for transportation entities via NeedInteractiveList
                if (fieldMap.fieldController.transportation != null)
                {
                    try
                    {
                        var transportationEntities = fieldMap.fieldController.transportation.NeedInteractiveList();
                        if (transportationEntities != null)
                        {
                            foreach (var interactiveEntity in transportationEntities)
                            {
                                if (interactiveEntity == null) continue;

                                var fieldEntity = interactiveEntity.TryCast<FieldEntity>();
                                if (fieldEntity != null && !results.Contains(fieldEntity))
                                {
                                    results.Add(fieldEntity);
                                }
                            }
                        }
                    }
                    catch { /* NeedInteractiveList may not be available */ }

                    // Access Transportation.ModelList dictionary via pointer offsets
                    // This gives us the actual vehicle type information
                    try
                    {
                        unsafe
                        {
                            IntPtr transportControllerPtr = fieldMap.fieldController.transportation.Pointer;
                            if (transportControllerPtr != IntPtr.Zero)
                            {
                                // Get infoData (Transportation) at offset 0x18
                                IntPtr infoDataPtr = *(IntPtr*)(transportControllerPtr + 0x18);
                                if (infoDataPtr != IntPtr.Zero)
                                {
                                    // Get modelList (Dictionary) at offset 0x18 in Transportation
                                    IntPtr modelListPtr = *(IntPtr*)(infoDataPtr + 0x18);
                                    if (modelListPtr != IntPtr.Zero)
                                    {
                                        var modelListObj = new Il2CppSystem.Object(modelListPtr);
                                        var modelDict = modelListObj.TryCast<Il2CppSystem.Collections.Generic.Dictionary<int, TransportationInfo>>();

                                        if (modelDict != null)
                                        {
                                            foreach (var kvp in modelDict)
                                            {
                                                var transportInfo = kvp.Value;
                                                if (transportInfo == null) continue;

                                                bool enabled = transportInfo.Enable;
                                                int transportType = transportInfo.Type;

                                                // Skip non-vehicle types and disabled vehicles
                                                // Type 0 = None, Type 1 = Player; Types 2,3,5,7 are valid vehicles in FF1
                                                if (transportType == 0 || transportType == 1 || !enabled) continue;

                                                var mapObject = transportInfo.MapObject;
                                                if (mapObject != null && !results.Contains(mapObject))
                                                {
                                                    results.Add(mapObject);
                                                    VehicleTypeMap[mapObject] = transportType;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { /* Vehicle scan via pointer offsets may fail */ }
                }

                // Access FootEvent.stepOnTriggerList for step-on event triggers
                // These are stored separately from the main entityList (e.g., airship-raising tile)
                try
                {
                    unsafe
                    {
                        IntPtr fieldControllerPtr = fieldMap.fieldController.Pointer;
                        if (fieldControllerPtr != IntPtr.Zero)
                        {
                            // FootEvent at offset 0x120 on FieldController
                            IntPtr footEventPtr = *(IntPtr*)(fieldControllerPtr + 0x120);
                            if (footEventPtr == IntPtr.Zero)
                            {
                                MelonLogger.Msg("[FootEvent] footEventPtr is Zero — FootEvent not yet initialized");
                            }
                            else
                            {
                                // stepOnTriggerList (Dictionary<int, EventTriggerEntity>) at offset 0x10
                                IntPtr dictPtr = *(IntPtr*)(footEventPtr + 0x10);
                                if (dictPtr == IntPtr.Zero)
                                {
                                    MelonLogger.Msg("[FootEvent] dictPtr is Zero — offset 0x10 may be wrong");
                                }
                                else
                                {
                                    var dictObj = new Il2CppSystem.Object(dictPtr);
                                    var triggerDict = dictObj.TryCast<Il2CppSystem.Collections.Generic.Dictionary<int, EventTriggerEntity>>();
                                    if (triggerDict == null)
                                    {
                                        MelonLogger.Msg("[FootEvent] TryCast<Dictionary> returned null — wrong type assumption");
                                    }
                                    else
                                    {
                                        int added = 0;
                                        foreach (var kvp in triggerDict)
                                        {
                                            var triggerEntity = kvp.Value;
                                            if (triggerEntity == null) continue;
                                            var fieldEntity = triggerEntity.TryCast<FieldEntity>();
                                            if (fieldEntity == null) continue;
                                            if (!results.Contains(fieldEntity))
                                            {
                                                results.Add(fieldEntity);
                                                added++;
                                            }
                                        }
                                        if (added > 0)
                                            MelonLogger.Msg($"[FootEvent] Added {added} entities from stepOnTriggerList");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[FootEvent] Exception: {ex.GetType().Name}: {ex.Message}");
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting field entities: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Checks if a direction from the player position leads to a map exit entity.
        /// Used to suppress wall tones at map exits, doors, and stairs where
        /// MapRouteSearcher.Search() reports blocked but the tile is actually accessible.
        /// </summary>
        /// <param name="playerPos">Player's local position.</param>
        /// <param name="direction">Direction vector (e.g., new Vector3(0, 16, 0) for North).</param>
        /// <param name="mapExitPositions">Positions of all map exit entities.</param>
        /// <param name="tolerance">Max 2D distance to match (default 12.0 = 3/4 tile).</param>
        /// <returns>True if a map exit is near the adjacent tile in this direction.</returns>
        public static bool IsDirectionNearMapExit(Vector3 playerPos, Vector3 direction,
            List<Vector3> mapExitPositions, float tolerance = 12.0f)
        {
            if (mapExitPositions == null || mapExitPositions.Count == 0)
                return false;

            Vector3 adjacentTilePos = playerPos + direction;

            foreach (var exitPos in mapExitPositions)
            {
                // 2D distance only (X/Y), ignoring Z (collision layer)
                float dx = adjacentTilePos.x - exitPos.x;
                float dy = adjacentTilePos.y - exitPos.y;
                float dist2D = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist2D <= tolerance)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets walkable directions from the current position.
        /// </summary>
        public static string GetWalkableDirections(FieldPlayer player, IMapAccessor mapHandle)
        {
            if (player == null || mapHandle == null)
                return "Cannot check directions";

            Vector3 currentPos = player.transform.position;
            float stepSize = 16f;

            var directions = new List<string>();

            Vector3 northPos = currentPos + new Vector3(0, stepSize, 0);
            if (CheckPositionWalkable(player, northPos))
                directions.Add("North");

            Vector3 southPos = currentPos + new Vector3(0, -stepSize, 0);
            if (CheckPositionWalkable(player, southPos))
                directions.Add("South");

            Vector3 eastPos = currentPos + new Vector3(stepSize, 0, 0);
            if (CheckPositionWalkable(player, eastPos))
                directions.Add("East");

            Vector3 westPos = currentPos + new Vector3(-stepSize, 0, 0);
            if (CheckPositionWalkable(player, westPos))
                directions.Add("West");

            if (directions.Count == 0)
                return "STUCK - No walkable directions!";

            return string.Join(", ", directions);
        }

        /// <summary>
        /// Result structure for wall proximity detection.
        /// Distance values: -1 = no wall within range, 0 = adjacent/blocked
        /// </summary>
        public struct WallDistances
        {
            public int NorthDist;
            public int SouthDist;
            public int EastDist;
            public int WestDist;

            public WallDistances(int north, int south, int east, int west)
            {
                NorthDist = north;
                SouthDist = south;
                EastDist = east;
                WestDist = west;
            }
        }

        /// <summary>
        /// Gets distance to nearest wall in each cardinal direction (in tiles).
        /// Returns -1 for a direction if no wall adjacent (1 tile away).
        /// </summary>
        public static WallDistances GetNearbyWallsWithDistance(FieldPlayer player)
        {
            if (player == null || player.transform == null)
                return new WallDistances(-1, -1, -1, -1);

            Vector3 pos = player.transform.localPosition;

            return new WallDistances(
                GetWallDistance(player, pos, new Vector3(0, 16, 0)),
                GetWallDistance(player, pos, new Vector3(0, -16, 0)),
                GetWallDistance(player, pos, new Vector3(16, 0, 0)),
                GetWallDistance(player, pos, new Vector3(-16, 0, 0))
            );
        }

        /// <summary>
        /// Gets the distance to a wall in a given direction using pathfinding.
        /// Returns: 0 = adjacent/blocked, -1 = no wall adjacent
        /// Only checks the immediately adjacent tile to reduce confusion from distant walls.
        /// </summary>
        private static int GetWallDistance(FieldPlayer player, Vector3 pos, Vector3 step)
        {
            // Check 1 tile away - if blocked, wall is adjacent (distance 0)
            if (IsAdjacentTileBlocked(player, pos, step))
                return 0;

            // No wall adjacent
            return -1;
        }

        /// <summary>
        /// Checks if an adjacent tile is blocked using pathfinding.
        /// More reliable than IsCanMoveToDestPosition for predictive checks.
        /// </summary>
        private static bool IsAdjacentTileBlocked(FieldPlayer player, Vector3 playerPos, Vector3 direction)
        {
            try
            {
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                if (playerController == null)
                {
                    playerController = GameObjectCache.Refresh<FieldPlayerController>();
                    if (playerController == null)
                        return false;
                }

                var mapHandle = playerController.mapHandle;
                if (mapHandle == null)
                    return false;

                int mapWidth = mapHandle.GetCollisionLayerWidth();
                int mapHeight = mapHandle.GetCollisionLayerHeight();

                // Validate map dimensions
                if (mapWidth <= 0 || mapHeight <= 0 || mapWidth > 10000 || mapHeight > 10000)
                    return false;

                // Convert world position to cell coordinates
                Vector3 startCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + playerPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - playerPos.y * 0.0625f),
                    player.gameObject.layer - 9
                );

                Vector3 targetPos = playerPos + direction;
                Vector3 destCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + targetPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - targetPos.y * 0.0625f),
                    startCell.z
                );

                // Get player collision state
                bool playerCollisionState = player._IsOnCollision_k__BackingField;

                // If pathfinding returns no path, tile is blocked
                var pathPoints = MapRouteSearcher.Search(mapHandle, startCell, destCell, playerCollisionState);
                return pathPoints == null || pathPoints.Count == 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[WallTones] IsAdjacentTileBlocked error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a position is walkable for the player.
        /// Made internal for wall proximity detection.
        /// Note: This uses IsCanMoveToDestPosition which may not work reliably for predictive checks.
        /// Use IsAdjacentTileBlocked for wall detection instead.
        /// </summary>
        internal static bool CheckPositionWalkable(FieldPlayer player, Vector3 position)
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController != null)
                {
                    return fieldMap.fieldController.IsCanMoveToDestPosition(player, ref position);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds a path from the player position to the target position.
        /// Uses the game's MapRouteSearcher for collision-aware pathfinding.
        /// </summary>
        public static PathInfo FindPathTo(Vector3 playerWorldPos, Vector3 targetWorldPos, IMapAccessor mapHandle, FieldPlayer player = null)
        {
            var pathInfo = new PathInfo { Success = false };

            if (mapHandle == null)
            {
                pathInfo.ErrorMessage = "Map handle not available";
                return pathInfo;
            }

            try
            {
                int mapWidth = mapHandle.GetCollisionLayerWidth();
                int mapHeight = mapHandle.GetCollisionLayerHeight();

                // Validation: check for unreasonable dimensions (likely stale mapHandle)
                if (mapWidth <= 0 || mapHeight <= 0 || mapWidth > 10000 || mapHeight > 10000)
                {
                    MelonLogger.Warning($"[Pathfinding] Invalid map dimensions: {mapWidth}x{mapHeight} - mapHandle may be stale");
                    pathInfo.ErrorMessage = "Invalid map dimensions - try rescanning";
                    return pathInfo;
                }

                // Convert world coordinates to cell coordinates
                Vector3 startCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + playerWorldPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - playerWorldPos.y * 0.0625f),
                    0
                );

                Vector3 destCell = new Vector3(
                    Mathf.FloorToInt(mapWidth * 0.5f + targetWorldPos.x * 0.0625f),
                    Mathf.FloorToInt(mapHeight * 0.5f - targetWorldPos.y * 0.0625f),
                    0
                );

                if (player != null)
                {
                    float layerZ = player.gameObject.layer - 9;
                    startCell.z = layerZ;
                }

                Il2CppSystem.Collections.Generic.List<Vector3> pathPoints = null;

                if (player != null)
                {
                    bool playerCollisionState = player._IsOnCollision_k__BackingField;

                    // Try pathfinding with different destination layers until one succeeds
                    for (int tryDestZ = 2; tryDestZ >= 0; tryDestZ--)
                    {
                        destCell.z = tryDestZ;
                        pathPoints = MapRouteSearcher.Search(mapHandle, startCell, destCell, playerCollisionState);

                        if (pathPoints != null && pathPoints.Count > 0)
                        {
                            break;
                        }
                    }

                    // If direct path failed, try adjacent tiles
                    if (pathPoints == null || pathPoints.Count == 0)
                    {
                        Vector3[] adjacentOffsets = new Vector3[] {
                            new Vector3(0, 16, 0),    // north
                            new Vector3(16, 0, 0),    // east
                            new Vector3(0, -16, 0),   // south
                            new Vector3(-16, 0, 0),   // west
                            new Vector3(16, 16, 0),   // northeast
                            new Vector3(16, -16, 0),  // southeast
                            new Vector3(-16, -16, 0), // southwest
                            new Vector3(-16, 16, 0)   // northwest
                        };

                        foreach (var offset in adjacentOffsets)
                        {
                            Vector3 adjacentTargetWorld = targetWorldPos + offset;

                            Vector3 adjacentDestCell = new Vector3(
                                Mathf.FloorToInt(mapWidth * 0.5f + adjacentTargetWorld.x * 0.0625f),
                                Mathf.FloorToInt(mapHeight * 0.5f - adjacentTargetWorld.y * 0.0625f),
                                0
                            );

                            for (int tryDestZ = 2; tryDestZ >= 0; tryDestZ--)
                            {
                                adjacentDestCell.z = tryDestZ;
                                pathPoints = MapRouteSearcher.Search(mapHandle, startCell, adjacentDestCell, playerCollisionState);

                                if (pathPoints != null && pathPoints.Count > 0)
                                {
                                    break;
                                }
                            }

                            if (pathPoints != null && pathPoints.Count > 0)
                                break;
                        }
                    }
                }
                else
                {
                    pathPoints = MapRouteSearcher.SearchSimple(mapHandle, startCell, destCell);
                }

                if (pathPoints == null || pathPoints.Count == 0)
                {
                    pathInfo.ErrorMessage = "No path found";
                    return pathInfo;
                }

                pathInfo.WorldPath = new List<Vector3>();
                for (int i = 0; i < pathPoints.Count; i++)
                {
                    pathInfo.WorldPath.Add(pathPoints[i]);
                }

                pathInfo.Success = true;
                pathInfo.StepCount = pathPoints.Count > 0 ? pathPoints.Count - 1 : 0;
                pathInfo.Description = DescribePath(pathInfo.WorldPath);

                return pathInfo;
            }
            catch (Exception ex)
            {
                pathInfo.ErrorMessage = $"Pathfinding error: {ex.Message}";
                return pathInfo;
            }
        }

        /// <summary>
        /// Creates a human-readable description of a path.
        /// </summary>
        private static string DescribePath(List<Vector3> worldPath)
        {
            if (worldPath == null || worldPath.Count < 2)
                return "No movement needed";

            var segments = new List<string>();
            Vector3 currentDir = Vector3.zero;
            int stepCount = 0;

            for (int i = 1; i < worldPath.Count; i++)
            {
                Vector3 dir = worldPath[i] - worldPath[i - 1];
                dir.Normalize();

                if (Vector3.Distance(dir, currentDir) < 0.1f)
                {
                    stepCount++;
                }
                else
                {
                    if (stepCount > 0)
                    {
                        string dirName = GetCardinalDirectionName(currentDir);
                        segments.Add($"{dirName} {stepCount}");
                    }

                    currentDir = dir;
                    stepCount = 1;
                }
            }

            if (stepCount > 0)
            {
                string dirName = GetCardinalDirectionName(currentDir);
                segments.Add($"{dirName} {stepCount}");
            }

            return string.Join(", ", segments);
        }

        /// <summary>
        /// Gets the cardinal direction name from a direction vector.
        /// </summary>
        private static string GetCardinalDirectionName(Vector3 dir)
        {
            if (Mathf.Abs(dir.x) > 0.4f && Mathf.Abs(dir.y) > 0.4f)
            {
                if (dir.y > 0 && dir.x > 0) return "Northeast";
                if (dir.y > 0 && dir.x < 0) return "Northwest";
                if (dir.y < 0 && dir.x > 0) return "Southeast";
                if (dir.y < 0 && dir.x < 0) return "Southwest";
            }

            if (Mathf.Abs(dir.y) > Mathf.Abs(dir.x))
            {
                return dir.y > 0 ? "North" : "South";
            }
            else if (Mathf.Abs(dir.x) > 0.1f)
            {
                return dir.x > 0 ? "East" : "West";
            }

            return "Unknown";
        }

        /// <summary>
        /// Gets the cardinal/intercardinal direction from source to target.
        /// </summary>
        public static string GetDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to cardinal/intercardinal directions
            if (angle >= 337.5 || angle < 22.5) return "North";
            else if (angle >= 22.5 && angle < 67.5) return "Northeast";
            else if (angle >= 67.5 && angle < 112.5) return "East";
            else if (angle >= 112.5 && angle < 157.5) return "Southeast";
            else if (angle >= 157.5 && angle < 202.5) return "South";
            else if (angle >= 202.5 && angle < 247.5) return "Southwest";
            else if (angle >= 247.5 && angle < 292.5) return "West";
            else if (angle >= 292.5 && angle < 337.5) return "Northwest";
            else return "Unknown";
        }

        /// <summary>
        /// Calculates the distance between two positions in game units.
        /// </summary>
        public static float GetDistance(Vector3 from, Vector3 to)
        {
            return Vector3.Distance(from, to);
        }

        /// <summary>
        /// Converts game distance units to steps.
        /// One step = 16 game units.
        /// </summary>
        public static float DistanceToSteps(float distance)
        {
            return distance / 16f;
        }

        /// <summary>
        /// Gets a simple path description with direction and step count.
        /// </summary>
        public static string GetSimplePathDescription(Vector3 from, Vector3 to)
        {
            float distance = GetDistance(from, to);
            string direction = GetDirection(from, to);
            float steps = DistanceToSteps(distance);
            string stepLabel = Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";

            return $"{steps:F0} {stepLabel} {direction}";
        }
    }
}
