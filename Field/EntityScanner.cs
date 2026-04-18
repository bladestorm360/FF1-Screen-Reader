using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Core.Filters;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Field.EntityDetectors;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using Il2CppLast.Management;
using FieldMap = Il2Cpp.FieldMap;
using FieldEntity = Il2CppLast.Entity.Field.FieldEntity;

namespace FFI_ScreenReader.Field
{
    /// <summary>
    /// Scans the field map for navigable entities and maintains a filtered, sorted list.
    /// Uses a chain of responsibility pattern for entity detection.
    /// </summary>
    public class EntityScanner
    {
        private List<NavigableEntity> entities = new List<NavigableEntity>();
        private int currentIndex = 0;
        private EntityCategory currentCategory = EntityCategory.All;
        private List<NavigableEntity> filteredEntities = new List<NavigableEntity>();
        private PathfindingFilter pathfindingFilter = new PathfindingFilter();
        private ToLayerFilter toLayerFilter = new ToLayerFilter();
        private int lastScannedMapId = -1;

        // Incremental scanning: map FieldEntity to its NavigableEntity conversion
        private Dictionary<FieldEntity, NavigableEntity> entityMap = new Dictionary<FieldEntity, NavigableEntity>();

        // Track selected entity by identifier to maintain focus across re-sorts
        private Vector3? selectedEntityPosition = null;
        private EntityCategory? selectedEntityCategory = null;
        private string selectedEntityName = null;

        // Detector chain (sorted by priority)
        private readonly List<IEntityDetector> detectors;

        public EntityScanner()
        {
            detectors = new List<IEntityDetector>
            {
                new PlayerFilter(),
                new VehicleDetector(),
                new VisualEffectFilter(),
                new GotoMapDetector(),
                new TreasureChestDetector(),
                new NPCDetector(),
                new SavePointDetector(),
                new TransportDetector(),
                new DoorStairsDetector(),
                new ToLayerDetector(),
                new EventTriggerDetector(),
                new InteractiveEntityDetector()
            };
            detectors.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// Whether to filter entities by pathfinding accessibility.
        /// </summary>
        public bool FilterByPathfinding
        {
            get => pathfindingFilter.IsEnabled;
            set => pathfindingFilter.IsEnabled = value;
        }

        /// <summary>
        /// Whether to filter out ToLayer (layer transition) entities.
        /// </summary>
        public bool FilterToLayer
        {
            get => toLayerFilter.IsEnabled;
            set
            {
                if (toLayerFilter.IsEnabled != value)
                {
                    toLayerFilter.IsEnabled = value;
                    ApplyFilter();
                }
            }
        }

        /// <summary>
        /// Current list of entities (filtered by category).
        /// </summary>
        public List<NavigableEntity> Entities => filteredEntities;

        /// <summary>
        /// Returns positions of all MapExitEntity instances from the unfiltered entity list.
        /// Used by wall tone suppression to avoid false positives at map exits/doors/stairs.
        /// </summary>
        public List<Vector3> GetMapExitPositions()
        {
            var positions = new List<Vector3>();
            foreach (var entity in entities)
            {
                if (entity is MapExitEntity)
                    positions.Add(entity.Position);
            }
            return positions;
        }

        /// <summary>
        /// Current entity index.
        /// </summary>
        public int CurrentIndex
        {
            get => currentIndex;
            set
            {
                currentIndex = value;
                SaveSelectedEntityIdentifier();
            }
        }

        /// <summary>
        /// Current category filter.
        /// </summary>
        public EntityCategory CurrentCategory
        {
            get => currentCategory;
            set
            {
                if (currentCategory != value)
                {
                    currentCategory = value;
                    ClearSelectedEntityIdentifier();
                    ApplyFilter();
                    currentIndex = 0;
                }
            }
        }

        /// <summary>
        /// Currently selected entity.
        /// </summary>
        public NavigableEntity CurrentEntity
        {
            get
            {
                EnsureCorrectMap();
                if (filteredEntities.Count == 0 || currentIndex < 0 || currentIndex >= filteredEntities.Count)
                    return null;
                return filteredEntities[currentIndex];
            }
        }

        #region Scanning

        /// <summary>
        /// Scans the field for all navigable entities using incremental scanning.
        /// Only converts new entities, keeping existing conversions.
        /// </summary>
        public void ScanEntities()
        {
            try
            {
                int currentMapId = GetCurrentMapId();
                string currentMapAsset = MapNameResolver.GetCurrentMapAssetName();

                var fieldEntities = FieldNavigationHelper.GetAllFieldEntities();
                var currentSet = new HashSet<FieldEntity>(fieldEntities);

                // Remove entities that no longer exist in the game's entity list
                var toRemove = entityMap.Keys.Where(k => !currentSet.Contains(k)).ToList();
                foreach (var key in toRemove)
                    entityMap.Remove(key);

                // Prune entities that are deactivated/destroyed in the scene
                var dead = entityMap.Where(kv => !kv.Value.IsAlive).Select(kv => kv.Key).ToList();
                foreach (var key in dead)
                    entityMap.Remove(key);

                // Only process NEW entities (ones not already in the map)
                foreach (var fieldEntity in fieldEntities)
                {
                    if (!string.IsNullOrEmpty(currentMapAsset) &&
                        !EntityDetectionHelpers.IsEntityOnCurrentMap(fieldEntity, currentMapAsset))
                        continue;

                    if (!entityMap.ContainsKey(fieldEntity))
                    {
                        try
                        {
                            var navigable = ConvertToNavigableEntity(fieldEntity);
                            if (navigable != null)
                                entityMap[fieldEntity] = navigable;
                        }
                        catch { } // Entity may be destroyed or unavailable
                    }
                }

                entities = entityMap.Values.ToList();
                lastScannedMapId = currentMapId;
                ApplyFilter();

                if (currentIndex >= filteredEntities.Count)
                    currentIndex = 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityScanner] Error scanning entities: {ex.Message}");
            }
        }

        /// <summary>
        /// Forces a full rescan by clearing the entity cache.
        /// </summary>
        public void ForceRescan()
        {
            entityMap.Clear();
            ScanEntities();
        }

        #endregion

        #region Entity Detection (Chain of Responsibility)

        /// <summary>
        /// Converts a FieldEntity to a NavigableEntity using the detector chain.
        /// Each detector is tried in priority order until one handles the entity.
        /// </summary>
        private NavigableEntity ConvertToNavigableEntity(FieldEntity fieldEntity)
        {
            if (fieldEntity == null)
                return null;

            var context = new EntityDetectionContext(fieldEntity);

            foreach (var detector in detectors)
            {
                var result = detector.TryDetect(context);
                if (result.ShouldSkip)
                    return null;
                if (result.Entity != null)
                    return result.Entity;
            }

            return null;
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Moves to the next entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void NextEntity()
        {
            EnsureCorrectMap();

            if (filteredEntities.Count == 0)
            {
                ScanEntities();
                if (filteredEntities.Count == 0)
                    return;
            }

            if (!pathfindingFilter.IsEnabled)
            {
                currentIndex = (currentIndex + 1) % filteredEntities.Count;
                SaveSelectedEntityIdentifier();
                return;
            }

            var context = new FilterContext();
            int attempts = 0;
            int startIndex = currentIndex;

            while (attempts < filteredEntities.Count)
            {
                currentIndex = (currentIndex + 1) % filteredEntities.Count;
                attempts++;

                var entity = filteredEntities[currentIndex];
                if (pathfindingFilter.PassesFilter(entity, context))
                {
                    SaveSelectedEntityIdentifier();
                    return;
                }
            }

            currentIndex = startIndex;
        }

        /// <summary>
        /// Returns true if pathfinding filter is enabled and no entities have valid paths.
        /// </summary>
        public bool NoReachableEntities()
        {
            if (!pathfindingFilter.IsEnabled || filteredEntities.Count == 0)
                return false;

            var context = new FilterContext();
            foreach (var entity in filteredEntities)
            {
                if (pathfindingFilter.PassesFilter(entity, context))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Moves to the previous entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void PreviousEntity()
        {
            EnsureCorrectMap();

            if (filteredEntities.Count == 0)
            {
                ScanEntities();
                if (filteredEntities.Count == 0)
                    return;
            }

            if (!pathfindingFilter.IsEnabled)
            {
                currentIndex = (currentIndex - 1 + filteredEntities.Count) % filteredEntities.Count;
                SaveSelectedEntityIdentifier();
                return;
            }

            var context = new FilterContext();
            int attempts = 0;
            int startIndex = currentIndex;

            while (attempts < filteredEntities.Count)
            {
                currentIndex = (currentIndex - 1 + filteredEntities.Count) % filteredEntities.Count;
                attempts++;

                var entity = filteredEntities[currentIndex];
                if (pathfindingFilter.PassesFilter(entity, context))
                {
                    SaveSelectedEntityIdentifier();
                    return;
                }
            }

            currentIndex = startIndex;
        }

        #endregion

        #region Filtering

        /// <summary>
        /// Applies the current category filter, sorts by distance, and restores selection.
        /// </summary>
        private void ApplyFilter()
        {
            if (currentCategory == EntityCategory.All)
            {
                filteredEntities = new List<NavigableEntity>(entities);
            }
            else
            {
                filteredEntities = entities.Where(e => e.Category == currentCategory).ToList();
            }

            var playerPos = GetPlayerPosition();
            if (playerPos.HasValue)
            {
                filteredEntities = filteredEntities.OrderBy(e => Vector3.Distance(e.Position, playerPos.Value)).ToList();
            }

            if (FFI_ScreenReaderMod.MapExitFilterEnabled)
            {
                filteredEntities = DeduplicateMapExits(filteredEntities);
            }

            if (toLayerFilter.IsEnabled)
            {
                filteredEntities = filteredEntities.Where(e => toLayerFilter.PassesFilter(e, null)).ToList();
            }

            int restoredIndex = FindEntityByIdentifier();
            if (restoredIndex >= 0)
            {
                currentIndex = restoredIndex;
            }
        }

        /// <summary>
        /// Groups map exits by destination map ID, keeping only the closest of each.
        /// List must already be sorted by distance (closest first).
        /// Exits with unresolved destinations (ID &lt;= 0) are kept individually.
        /// </summary>
        private List<NavigableEntity> DeduplicateMapExits(List<NavigableEntity> source)
        {
            var result = new List<NavigableEntity>();
            var seenDestinations = new HashSet<int>();

            foreach (var entity in source)
            {
                if (entity is MapExitEntity mapExit && mapExit.DestinationMapId > 0)
                {
                    if (!seenDestinations.Add(mapExit.DestinationMapId))
                        continue;
                }
                result.Add(entity);
            }
            return result;
        }

        /// <summary>
        /// Re-applies filters without rescanning. Used when filter toggles change.
        /// </summary>
        public void ReapplyFilter()
        {
            ApplyFilter();
            if (currentIndex >= filteredEntities.Count)
                currentIndex = 0;
        }

        #endregion

        #region Entity Identity Tracking

        private void SaveSelectedEntityIdentifier()
        {
            var entity = CurrentEntity;
            if (entity != null)
            {
                selectedEntityPosition = entity.Position;
                selectedEntityCategory = entity.Category;
                selectedEntityName = entity.Name;
            }
        }

        public void ClearSelectedEntityIdentifier()
        {
            selectedEntityPosition = null;
            selectedEntityCategory = null;
            selectedEntityName = null;
        }

        private int FindEntityByIdentifier()
        {
            if (!selectedEntityPosition.HasValue || !selectedEntityCategory.HasValue)
                return -1;

            for (int i = 0; i < filteredEntities.Count; i++)
            {
                var entity = filteredEntities[i];
                if (entity.Category == selectedEntityCategory.Value &&
                    Vector3.Distance(entity.Position, selectedEntityPosition.Value) < 0.5f)
                {
                    return i;
                }
            }

            if (!string.IsNullOrEmpty(selectedEntityName))
            {
                for (int i = 0; i < filteredEntities.Count; i++)
                {
                    var entity = filteredEntities[i];
                    if (entity.Category == selectedEntityCategory.Value &&
                        entity.Name == selectedEntityName)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        #endregion

        #region Map & Player Helpers

        private void EnsureCorrectMap()
        {
            try
            {
                int currentMapId = GetCurrentMapId();
                if (currentMapId > 0 && currentMapId != lastScannedMapId)
                    ForceRescan();
            }
            catch { } // Map ID read may fail during transitions
        }

        private Vector3? GetPlayerPosition()
        {
            try
            {
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                    return playerController.fieldPlayer.transform.localPosition;
            }
            catch { } // Player may not exist on current map
            return null;
        }

        private int GetCurrentMapId()
        {
            try
            {
                var fieldMapInfo = FieldMapProvisionInformation.Instance;
                if (fieldMapInfo != null)
                {
                    int mapId = fieldMapInfo.CurrentMapId;
                    if (mapId > 0)
                        return mapId;
                }
            }
            catch { } // Map info may not be initialized yet

            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                    return userDataManager.CurrentMapId;
            }
            catch { } // UserDataManager may not be initialized
            return -1;
        }

        #endregion
    }
}
