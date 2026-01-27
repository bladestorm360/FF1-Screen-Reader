using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Core.Filters;
using FFI_ScreenReader.Utils;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using Il2CppLast.Management;
using FieldMap = Il2Cpp.FieldMap;
using GotoMapEventEntity = Il2CppLast.Entity.Field.GotoMapEventEntity;
using PropertyGotoMap = Il2CppLast.Map.PropertyGotoMap;
using PropertyEntity = Il2CppLast.Map.PropertyEntity;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;
using FieldMapObjectDefault = Il2CppLast.Entity.Field.FieldMapObjectDefault;
using FieldEntity = Il2CppLast.Entity.Field.FieldEntity;
using FieldNonPlayer = Il2CppLast.Entity.Field.FieldNonPlayer;
using PropertyTransportation = Il2CppLast.Map.PropertyTransportation;
using PropertyTelepoPoint = Il2CppLast.Map.PropertyTelepoPoint;
using EventTriggerEntity = Il2CppLast.Entity.Field.EventTriggerEntity;
using ContentUtitlity = Il2CppLast.Systems.ContentUtitlity;
using MessageManager = Il2CppLast.Management.MessageManager;

namespace FFI_ScreenReader.Field
{
    /// <summary>
    /// Scans the field map for navigable entities and maintains a list of them.
    /// </summary>
    public class EntityScanner
    {
        private List<NavigableEntity> entities = new List<NavigableEntity>();
        private int currentIndex = 0;
        private EntityCategory currentCategory = EntityCategory.All;
        private List<NavigableEntity> filteredEntities = new List<NavigableEntity>();
        private PathfindingFilter pathfindingFilter = new PathfindingFilter();
        private int lastScannedMapId = -1;

        // Incremental scanning: map FieldEntity to its NavigableEntity conversion
        // This avoids re-converting the same entities every scan
        private Dictionary<FieldEntity, NavigableEntity> entityMap = new Dictionary<FieldEntity, NavigableEntity>();

        // Track selected entity by identifier to maintain focus across re-sorts
        private Vector3? selectedEntityPosition = null;
        private EntityCategory? selectedEntityCategory = null;
        private string selectedEntityName = null;

        /// <summary>
        /// Whether to filter entities by pathfinding accessibility.
        /// When enabled, only entities with a valid path from the player are shown.
        /// </summary>
        public bool FilterByPathfinding
        {
            get => pathfindingFilter.IsEnabled;
            set => pathfindingFilter.IsEnabled = value;
        }

        /// <summary>
        /// Current list of entities (filtered by category)
        /// </summary>
        public List<NavigableEntity> Entities => filteredEntities;

        /// <summary>
        /// Returns positions of all MapExitEntity instances from the unfiltered entity list.
        /// Used by wall tone suppression to avoid false positives at map exits/doors/stairs.
        /// Reads from the unfiltered list so it works regardless of the active category filter.
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
        /// Current entity index
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
        /// Saves the current entity's identifier for focus restoration after re-sorting.
        /// </summary>
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

        /// <summary>
        /// Clears the saved entity identifier (used when explicitly resetting selection).
        /// </summary>
        public void ClearSelectedEntityIdentifier()
        {
            selectedEntityPosition = null;
            selectedEntityCategory = null;
            selectedEntityName = null;
        }

        /// <summary>
        /// Finds the index of an entity matching the saved identifier.
        /// Returns -1 if not found.
        /// </summary>
        private int FindEntityByIdentifier()
        {
            if (!selectedEntityPosition.HasValue || !selectedEntityCategory.HasValue)
                return -1;

            for (int i = 0; i < filteredEntities.Count; i++)
            {
                var entity = filteredEntities[i];
                // Match by position (with small tolerance) and category
                if (entity.Category == selectedEntityCategory.Value &&
                    Vector3.Distance(entity.Position, selectedEntityPosition.Value) < 0.5f)
                {
                    return i;
                }
            }

            // Fallback: try matching by name if position changed slightly
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

        /// <summary>
        /// Current category filter
        /// </summary>
        public EntityCategory CurrentCategory
        {
            get => currentCategory;
            set
            {
                if (currentCategory != value)
                {
                    currentCategory = value;
                    ClearSelectedEntityIdentifier(); // Clear since we're changing category
                    ApplyFilter();
                    currentIndex = 0;
                }
            }
        }

        /// <summary>
        /// Currently selected entity
        /// </summary>
        public NavigableEntity CurrentEntity
        {
            get
            {
                // Check if map changed and rescan if needed
                EnsureCorrectMap();

                if (filteredEntities.Count == 0 || currentIndex < 0 || currentIndex >= filteredEntities.Count)
                    return null;
                return filteredEntities[currentIndex];
            }
        }

        /// <summary>
        /// Scans the field for all navigable entities using incremental scanning.
        /// Only converts new entities, keeping existing conversions to improve performance.
        /// </summary>
        public void ScanEntities()
        {
            try
            {
                int currentMapId = GetCurrentMapId();
                string currentMapAsset = MapNameResolver.GetCurrentMapAssetName();

                var fieldEntities = FieldNavigationHelper.GetAllFieldEntities();
                var currentSet = new HashSet<FieldEntity>(fieldEntities);

                // Remove entities that no longer exist
                var toRemove = entityMap.Keys.Where(k => !currentSet.Contains(k)).ToList();
                foreach (var key in toRemove)
                    entityMap.Remove(key);

                // Only process NEW entities (ones not already in the map)
                foreach (var fieldEntity in fieldEntities)
                {
                    // Filter by parent hierarchy - only include entities on current map
                    if (!string.IsNullOrEmpty(currentMapAsset) && !IsEntityOnCurrentMap(fieldEntity, currentMapAsset))
                        continue;

                    if (!entityMap.ContainsKey(fieldEntity))
                    {
                        try
                        {
                            var navigable = ConvertToNavigableEntity(fieldEntity);
                            if (navigable != null)
                                entityMap[fieldEntity] = navigable;
                        }
                        catch { }  // Silently skip entities that fail to convert
                    }
                }

                // Update the entities list from the map
                entities = entityMap.Values.ToList();

                // Store the map ID we scanned for
                lastScannedMapId = currentMapId;

                // Re-apply filter after scanning
                ApplyFilter();

                // Reset index if out of bounds
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
        /// Use this when changing maps or when entities may have changed state.
        /// </summary>
        public void ForceRescan()
        {
            entityMap.Clear();
            ScanEntities();
        }

        /// <summary>
        /// Checks if the current map has changed since the last scan.
        /// If so, triggers a full rescan to get entities for the new map.
        /// </summary>
        private void EnsureCorrectMap()
        {
            try
            {
                int currentMapId = GetCurrentMapId();
                if (currentMapId > 0 && currentMapId != lastScannedMapId)
                {
                    ForceRescan();
                }
            }
            catch { }
        }

        /// <summary>
        /// Applies the current category filter.
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

            // Sort by distance from player
            var playerPos = GetPlayerPosition();
            if (playerPos.HasValue)
            {
                filteredEntities = filteredEntities.OrderBy(e => Vector3.Distance(e.Position, playerPos.Value)).ToList();
            }

            // Restore focus to previously selected entity after re-sorting
            int restoredIndex = FindEntityByIdentifier();
            if (restoredIndex >= 0)
            {
                currentIndex = restoredIndex;
            }
        }

        /// <summary>
        /// Gets the current player position.
        /// </summary>
        private Vector3? GetPlayerPosition()
        {
            try
            {
                // Use FieldPlayerController for direct access
                var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                {
                    // Use localPosition for pathfinding
                    return playerController.fieldPlayer.transform.localPosition;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets the current map ID from FieldMapProvisionInformation (preferred) or UserDataManager (fallback).
        /// FieldMapProvisionInformation tracks the actual displayed map, which updates on map transitions.
        /// </summary>
        private int GetCurrentMapId()
        {
            // Try FieldMapProvisionInformation first - this tracks the actual displayed map
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
            catch { }

            // Fallback to UserDataManager
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                    return userDataManager.CurrentMapId;
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Gets the FieldPlayer from the FieldController using reflection.
        /// The player field is private in the main game's FieldController.
        /// </summary>
        private FieldPlayer GetFieldPlayer()
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController == null)
                    return null;

                // Access private 'player' field using reflection
                var fieldType = fieldMap.fieldController.GetType();
                var playerField = fieldType.GetField("player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (playerField != null)
                {
                    return playerField.GetValue(fieldMap.fieldController) as FieldPlayer;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Moves to the next entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void NextEntity()
        {
            // Check if map changed and rescan if needed
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

            // With pathfinding filter, find next entity with valid path
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
                    return; // Found a valid entity
                }
            }

            // No reachable entities found - stay at current position
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
                    return false; // At least one reachable
            }
            return true; // None reachable
        }

        /// <summary>
        /// Moves to the previous entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void PreviousEntity()
        {
            // Check if map changed and rescan if needed
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

            // With pathfinding filter, find previous entity with valid path
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
                    return; // Found a valid entity
                }
            }

            // No reachable entities found - stay at current position
            currentIndex = startIndex;
        }

        /// <summary>
        /// Converts a FieldEntity to a NavigableEntity.
        /// </summary>
        private NavigableEntity ConvertToNavigableEntity(FieldEntity fieldEntity)
        {
            if (fieldEntity == null)
                return null;

            // Use localPosition for pathfinding compatibility
            Vector3 position = fieldEntity.transform.localPosition;
            string typeName = fieldEntity.GetType().Name;
            string goName = "";

            try
            {
                goName = fieldEntity.gameObject.name ?? "";
            }
            catch { }

            string goNameLower = goName.ToLower();

            bool isActive = true;
            try { isActive = fieldEntity.gameObject.activeInHierarchy; } catch { }

            bool isEventTriggerEarly = false;
            try { isEventTriggerEarly = fieldEntity.TryCast<EventTriggerEntity>() != null; } catch { }

            // Skip the player entity
            if (typeName.Contains("FieldPlayer") || goNameLower.Contains("player"))
                return null;

            // ===== VEHICLE DETECTION (must come before residentchara filter) =====
            // Check VehicleTypeMap first - vehicles use ResidentCharaEntity GameObjects
            if (FieldNavigationHelper.VehicleTypeMap.TryGetValue(fieldEntity, out int vehicleType))
            {
                string vehicleName = VehicleEntity.GetVehicleName(vehicleType);
                return new VehicleEntity(fieldEntity, position, vehicleName, vehicleType);
            }

            // Skip party member entities (following characters)
            // This must come AFTER vehicle check since vehicles also use ResidentCharaEntity
            if (goNameLower.Contains("residentchara") || goNameLower.Contains("resident"))
                return null;

            // EventTriggerEntity (warp tiles, map exits) should NOT be filtered
            // FieldScrollDummyEntity extends FieldEntity directly, NOT EventTriggerEntity, so still gets filtered
            bool isEventTrigger = isEventTriggerEarly;

            // Skip visual effects and non-interactive elements (including scroll/content that can be detected as vehicles)
            if (!isEventTrigger)
            {
                if (goNameLower.Contains("fieldeffect") || goNameLower.Contains("scrolldummy") ||
                    goNameLower.Contains("scroll") || goNameLower.Contains("tileanim") ||
                    goNameLower.Contains("pointin") || goNameLower.Contains("opentrigger") ||
                    (goNameLower.Contains("effect") && !goNameLower.Contains("object")))
                    return null;
            }

            // Skip inactive objects
            if (!isActive)
                return null;

            // Try to get the Property object which determines entity type
            object propertyObj = GetEntityProperty(fieldEntity);
            int objectType = GetObjectType(propertyObj);

            // ===== MAP EXIT DETECTION =====
            // Try casting to GotoMapEventEntity first (most reliable)
            var gotoMapEvent = fieldEntity.TryCast<GotoMapEventEntity>();
            if (gotoMapEvent != null)
            {
                int destMapId = GetGotoMapDestinationId(fieldEntity);
                string destName = ResolveMapName(destMapId);
                string exitName = !string.IsNullOrEmpty(destName) ? $"Exit to {destName}" : "Exit";
                return new MapExitEntity(fieldEntity, position, exitName, destMapId, destName);
            }

            // Fallback: GotoMap in game object name OR ObjectType 3
            if (goNameLower.Contains("gotomap") || objectType == 3)
            {
                int destMapId = GetGotoMapDestinationId(fieldEntity);
                string destName = ResolveMapName(destMapId);
                string exitName = !string.IsNullOrEmpty(destName) ? $"Exit to {destName}" : "Exit";
                return new MapExitEntity(fieldEntity, position, exitName, destMapId, destName);
            }

            // ===== TELEPORT POINT DETECTION =====
            // PropertyTelepoPoint = same-map warp tiles (e.g., Citadel of Trials puzzle)
            try
            {
                if (propertyObj is PropertyEntity prop)
                {
                    var telepoProperty = prop.TryCast<PropertyTelepoPoint>();
                    if (telepoProperty != null)
                    {
                        string name = GetEntityNameFromProperty(fieldEntity);
                        if (string.IsNullOrEmpty(name))
                            name = "Warp Tile";
                        return new EventEntity(fieldEntity, position, name, "Warp Tile");
                    }
                }
            }
            catch { }

            // ===== TREASURE CHEST DETECTION =====
            // Try explicit cast to FieldTresureBox first (note: game uses "Tresure" spelling)
            var treasureBox = fieldEntity.TryCast<FieldTresureBox>();
            if (treasureBox != null)
            {
                bool isOpened = CheckIfTreasureOpened(fieldEntity);
                string contents = GetTreasureContents(propertyObj);
                string name = !string.IsNullOrEmpty(contents) ? contents : "Treasure Chest";
                return new TreasureChestEntity(fieldEntity, position, name, isOpened);
            }

            // Fallback: name-based treasure detection
            if (goNameLower.Contains("treasure") || goNameLower.Contains("tresure") ||
                goNameLower.Contains("chest") || typeName.Contains("Treasure") || typeName.Contains("Tresure"))
            {
                bool isOpened = CheckIfTreasureOpened(fieldEntity);
                string contents = GetTreasureContents(propertyObj);
                string name = !string.IsNullOrEmpty(contents) ? contents : "Treasure Chest";
                return new TreasureChestEntity(fieldEntity, position, name, isOpened);
            }

            // ===== NPC DETECTION (type-based) =====
            var fieldNonPlayer = fieldEntity.TryCast<FieldNonPlayer>();
            if (fieldNonPlayer != null)
            {
                // Check if NPC is actionable (can be interacted with)
                try
                {
                    if (!fieldNonPlayer.CanAction)
                        return null; // Skip non-interactive NPCs
                }
                catch { } // CanAction may not exist - include NPC if check fails

                string npcName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(npcName) || npcName == "NPC")
                    npcName = CleanObjectName(goName, "NPC");
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                return new NPCEntity(fieldEntity, position, npcName, "", isShop);
            }

            // ===== SAVE POINT DETECTION =====
            if (goNameLower.Contains("save") || typeName.Contains("Save"))
                return new SavePointEntity(fieldEntity, position, "Save Point");

            // ===== VEHICLE/TRANSPORT DETECTION (fallback layers) =====
            // Note: Layer 1 (VehicleTypeMap) is checked earlier, before residentchara filter

            // Layer 2: Check for vehicles via PropertyTransportation
            try
            {
                var property = fieldEntity.Property;
                if (property != null)
                {
                    var transportProperty = property.TryCast<PropertyTransportation>();
                    if (transportProperty != null)
                    {
                        string vehicleName = GetVehicleNameFromProperty(goName, typeName);
                        int vehicleTypeFromName = GetVehicleTypeFromName(vehicleName);
                        // Skip unknown vehicle types (Type 0) - prevents duplicates
                        if (vehicleTypeFromName == 0)
                            return null;
                        return new VehicleEntity(fieldEntity, position, vehicleName, vehicleTypeFromName);
                    }
                }
            }
            catch { }

            // Layer 3: String-based fallback (least reliable)
            if (typeName.Contains("Transport") || goNameLower.Contains("ship") ||
                goNameLower.Contains("canoe") || goNameLower.Contains("airship"))
            {
                string vehicleName = GetVehicleNameFromProperty(goName, typeName);
                int vehicleTypeFromName = GetVehicleTypeFromName(vehicleName);
                // Skip unknown types
                if (vehicleTypeFromName == 0)
                    return null;
                return new VehicleEntity(fieldEntity, position, vehicleName, vehicleTypeFromName);
            }

            // ===== DOOR/STAIRS (secondary exit detection) =====
            if (goNameLower.Contains("door") || goNameLower.Contains("stairs") ||
                goNameLower.Contains("ladder") || goNameLower.Contains("entrance"))
            {
                int destMapId = GetGotoMapDestinationId(fieldEntity);
                string destName = ResolveMapName(destMapId);
                return new MapExitEntity(fieldEntity, position, "Exit", destMapId, destName);
            }

            // ===== SKIP ELEVATION/LAYER-CHANGE TRIGGERS =====
            // ToUpper/ToBottom move the player between layers on the same map (e.g., stairs)
            // These are not useful navigation targets — warp tiles use PropertyTelepoPoint instead
            if (goNameLower == "toupper" || goNameLower == "tobottom")
                return null;

            // ===== EVENT TRIGGER CATCH-ALL =====
            // EventTriggerEntity doesn't implement IInteractiveEntity, so without this
            // it falls through all detection and returns null
            if (isEventTrigger)
            {
                string entityName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(entityName))
                    entityName = CleanObjectName(goName, "Event Trigger");
                return new EventEntity(fieldEntity, position, entityName, "Event");
            }

            // ===== FIELDMAPOBJECTDEFAULT DETECTION =====
            // These are generic interactive objects (buildings, misc objects)
            var mapObjectDefault = fieldEntity.TryCast<FieldMapObjectDefault>();
            if (mapObjectDefault != null)
            {
                // Skip if can't interact
                if (!mapObjectDefault.CanAction)
                    return null;

                // Try to get a meaningful name from the property
                string name = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(name))
                    name = GetInteractiveObjectName(propertyObj, goName);
                return new EventEntity(fieldEntity, position, name, "Interactive Object");
            }

            // ===== GENERIC INTERACTIVE OBJECTS =====
            var interactiveEntity = fieldEntity.TryCast<IInteractiveEntity>();
            if (interactiveEntity != null)
            {
                // ObjectType 0 seems to be PointIn (entry points - not useful to navigate to)
                if (objectType == 0 && goNameLower.Contains("pointin"))
                    return null;

                string name = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(name))
                    name = CleanObjectName(goName, "Object");
                return new EventEntity(fieldEntity, position, name, "Interactive");
            }

            return null;
        }

        /// <summary>
        /// Checks if an entity belongs to the current map by examining its parent hierarchy.
        /// Entities are children of their map container (e.g., "Map_20020" for Cornelia).
        /// </summary>
        private bool IsEntityOnCurrentMap(FieldEntity fieldEntity, string currentMapAsset)
        {
            if (fieldEntity == null || string.IsNullOrEmpty(currentMapAsset))
                return true; // Allow if we can't determine

            try
            {
                var transform = fieldEntity.gameObject.transform;
                var parent = transform.parent;
                int depth = 0;
                string assetLower = currentMapAsset.ToLower();
                bool foundAnyMapContainer = false;

                while (parent != null && depth < 10)
                {
                    string parentName = parent.name;
                    string parentLower = parentName.ToLower();

                    // Check if this parent is a map container (starts with "map_")
                    if (parentLower.StartsWith("map_"))
                    {
                        foundAnyMapContainer = true;
                        // If it matches current map, include it
                        if (parentLower.Contains(assetLower))
                        {
                            return true;
                        }
                        // If it's a different map container, exclude it
                        return false;
                    }

                    parent = parent.parent;
                    depth++;
                }

                // No map container found in hierarchy - entity is at root level
                // Include root-level entities (they're shared objects like FieldMapObjectDefault)
                return true;
            }
            catch
            {
                return true; // Allow on error
            }
        }

        /// <summary>
        /// Gets the Property object from a FieldEntity.
        /// </summary>
        private object GetEntityProperty(FieldEntity fieldEntity)
        {
            try
            {
                var entityType = fieldEntity.GetType();

                // Try "Property" property first
                var propProp = entityType.GetProperty("Property",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (propProp != null)
                {
                    return propProp.GetValue(fieldEntity);
                }

                // Try "property" field
                var propField = entityType.GetField("property",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (propField != null)
                {
                    return propField.GetValue(fieldEntity);
                }

                // Try "_property"
                propField = entityType.GetField("_property",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (propField != null)
                {
                    return propField.GetValue(fieldEntity);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets the ObjectType from a property object.
        /// ObjectType 3 = Map Exit (GotoMap), ObjectType 0 = PointIn (entry points)
        /// </summary>
        private int GetObjectType(object propertyObj)
        {
            if (propertyObj == null) return -1;

            try
            {
                var propType = propertyObj.GetType();

                // Try "ObjectType" property first
                var prop = propType.GetProperty("ObjectType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(propertyObj);
                    if (value != null)
                    {
                        return Convert.ToInt32(value);
                    }
                }

                // Try "objectType" field (lowercase)
                var field = propType.GetField("objectType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(propertyObj);
                    if (value != null)
                    {
                        return Convert.ToInt32(value);
                    }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Gets the destination map ID from a FieldEntity by accessing its Property as PropertyGotoMap.
        /// This is the correct way to get destination map ID - PropertyGotoMap.MapId contains the DESTINATION.
        /// </summary>
        private int GetGotoMapDestinationId(FieldEntity fieldEntity)
        {
            try
            {
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                    return -1;

                // Try to cast to PropertyGotoMap which has MapId (the DESTINATION map)
                var gotoMapProperty = property.TryCast<PropertyGotoMap>();
                if (gotoMapProperty != null)
                {
                    int mapId = gotoMapProperty.MapId;
                    if (mapId > 0)
                        return mapId;
                }
                else
                {
                    // Fallback: try TmeId from PropertyEntity
                    int tmeId = property.TmeId;
                    if (tmeId > 0)
                        return tmeId;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Gets the destination map ID from a property object (legacy reflection method).
        /// </summary>
        private int GetDestinationMapId(object propertyObj)
        {
            if (propertyObj == null) return -1;

            try
            {
                var propType = propertyObj.GetType();

                // Try common property names for destination map ID
                string[] propNames = { "TmeId", "MapId", "DestinationMapId", "MoveMapId", "ToMapId", "targetMapId" };
                foreach (var name in propNames)
                {
                    var prop = propType.GetProperty(name);
                    if (prop != null)
                    {
                        var value = prop.GetValue(propertyObj);
                        if (value != null)
                        {
                            return Convert.ToInt32(value);
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Resolves a destination map ID to its localized name.
        /// Uses MapNameResolver to properly look up the destination map's area and floor.
        /// </summary>
        private string ResolveMapName(int destMapId)
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
        private string GetTreasureContents(object propertyObj)
        {
            if (propertyObj == null) return "";

            try
            {
                var propType = propertyObj.GetType();

                // Check for Gil first (special case)
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

                // Try common property names for content/item IDs
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
                                // Fallback if resolution fails
                                return $"Item {contentId}";
                            }
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Resolves a content ID to a localized item name.
        /// </summary>
        private string ResolveItemName(int contentId)
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
            catch { }

            return null;
        }

        /// <summary>
        /// Gets the NPC name from a property object.
        /// </summary>
        private string GetNpcName(object propertyObj)
        {
            if (propertyObj == null) return "NPC";

            try
            {
                var propType = propertyObj.GetType();

                // Try common property names for NPC name
                string[] propNames = { "Name", "CharacterName", "NpcName", "DisplayName" };
                foreach (var name in propNames)
                {
                    var prop = propType.GetProperty(name);
                    if (prop != null)
                    {
                        var value = prop.GetValue(propertyObj);
                        if (value != null && !string.IsNullOrEmpty(value.ToString()))
                        {
                            return value.ToString();
                        }
                    }
                }
            }
            catch { }
            return "NPC";
        }

        /// <summary>
        /// Gets the entity name from PropertyEntity.Name for NPCs and interactive entities.
        /// Falls back to trying to resolve via MessageManager if name looks like a message ID.
        /// Japanese names are translated via EntityTranslator.
        /// </summary>
        private string GetEntityNameFromProperty(FieldEntity fieldEntity)
        {
            try
            {
                // Access Property directly on the IL2CPP object
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                    return null;

                // Get the Name property
                string name = property.Name;

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                // Check if name looks like a message ID (e.g., starts with "mes_" or similar patterns)
                if (name.StartsWith("mes_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("sys_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("field_", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to resolve via MessageManager
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(name, false);
                        if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != name)
                            return EntityTranslator.Translate(localizedName);
                    }
                }

                // Treat generic/non-descriptive names as empty so callers fall back to other sources
                string nameLower = name.ToLower();
                if (nameLower == "event" || nameLower == "eventtrigger" || nameLower == "event trigger" || nameLower == "pointin")
                    return null;

                // Translate in case it's Japanese
                return EntityTranslator.Translate(name);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Cleans up an object name for display.
        /// </summary>
        private string CleanObjectName(string name, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return defaultName;

            // Remove common suffixes
            name = name.Replace("(Clone)", "").Trim();

            // If name is just numbers or very short, use default
            if (name.Length < 2 || name.All(c => char.IsDigit(c) || c == '_'))
                return defaultName;

            // If name starts with underscore, use default
            if (name.StartsWith("_"))
                return defaultName;

            return name;
        }

        /// <summary>
        /// Gets a meaningful name for an interactive object, trying various sources.
        /// </summary>
        private string GetInteractiveObjectName(object propertyObj, string goName)
        {
            // Try to get name from property
            if (propertyObj != null)
            {
                try
                {
                    // Try ObjectId which might map to a known object type
                    var objectIdProp = propertyObj.GetType().GetProperty("ObjectId");
                    if (objectIdProp != null)
                    {
                        var objectId = objectIdProp.GetValue(propertyObj);
                        if (objectId != null && (int)objectId > 0)
                        {
                            return $"Object {objectId}";
                        }
                    }
                }
                catch { }
            }

            // Clean up game object name
            string cleaned = CleanObjectName(goName, "");
            if (!string.IsNullOrEmpty(cleaned) && cleaned != "FieldMapObjectDefault")
            {
                return cleaned;
            }

            return "Interactive Object";
        }

        /// <summary>
        /// Checks if a treasure entity has been opened.
        /// </summary>
        private bool CheckIfTreasureOpened(FieldEntity fieldEntity)
        {
            try
            {
                // Try to cast to FieldTresureBox first (the actual type)
                var treasureBox = fieldEntity.TryCast<FieldTresureBox>();
                if (treasureBox != null)
                {
                    // Try "isOpen" field (FF1 uses this - private bool isOpen at offset 0x159)
                    var isOpenField = treasureBox.GetType().GetField("isOpen",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (isOpenField != null)
                    {
                        return (bool)isOpenField.GetValue(treasureBox);
                    }

                    // Try "isOpen" property
                    var isOpenProp = treasureBox.GetType().GetProperty("isOpen");
                    if (isOpenProp != null)
                    {
                        return (bool)isOpenProp.GetValue(treasureBox);
                    }
                }

                // Fallback: try generic property checks on FieldEntity
                var prop = fieldEntity.GetType().GetProperty("isOpened") ??
                           fieldEntity.GetType().GetProperty("isOpen");
                if (prop != null)
                {
                    return (bool)prop.GetValue(fieldEntity);
                }

                var field = fieldEntity.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ??
                    fieldEntity.GetType().GetField("isOpened",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    return (bool)field.GetValue(fieldEntity);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Gets the destination name for a map exit.
        /// Tries to resolve map ID to localized name via MessageManager.
        /// </summary>
        private string GetMapExitDestination(FieldEntity fieldEntity)
        {
            int destMapId = -1;

            try
            {
                var entityType = fieldEntity.GetType();

                // Try moveMapId property first
                var moveMapIdProp = entityType.GetProperty("moveMapId");
                if (moveMapIdProp != null)
                {
                    destMapId = (int)moveMapIdProp.GetValue(fieldEntity);
                }

                // Try TmeId from Property object (alternative structure)
                if (destMapId <= 0)
                {
                    var propertyProp = entityType.GetProperty("Property");
                    if (propertyProp != null)
                    {
                        var propertyObj = propertyProp.GetValue(fieldEntity);
                        if (propertyObj != null)
                        {
                            var tmeIdProp = propertyObj.GetType().GetProperty("TmeId");
                            if (tmeIdProp != null)
                            {
                                destMapId = Convert.ToInt32(tmeIdProp.GetValue(propertyObj));
                            }
                        }
                    }
                }

                // Resolve map ID to name using MessageManager
                if (destMapId > 0)
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string[] keyFormats = {
                            $"MSG_MAP_NAME_{destMapId:D2}",
                            $"MSG_MAP_NAME_{destMapId}",
                            $"map_name_{destMapId:D4}",
                            $"map_name_{destMapId}"
                        };

                        foreach (var keyFormat in keyFormats)
                        {
                            string mapName = messageManager.GetMessage(keyFormat, true);
                            if (!string.IsNullOrEmpty(mapName))
                                return mapName;
                        }
                    }

                    return $"Map {destMapId}";
                }
            }
            catch { }

            return "";
        }

        /// <summary>
        /// Gets a display name for a vehicle entity from property/GO name.
        /// </summary>
        private string GetVehicleNameFromProperty(string goName, string typeName)
        {
            string nameLower = goName.ToLower();

            if (nameLower.Contains("airship") || typeName.Contains("AirShip"))
                return "Airship";
            if (nameLower.Contains("canoe"))
                return "Canoe";
            if (nameLower.Contains("ship") || nameLower.Contains("boat"))
                return "Ship";

            return "Vehicle";
        }

        /// <summary>
        /// Gets the TransportationType enum value from vehicle name.
        /// </summary>
        private int GetVehicleTypeFromName(string vehicleName)
        {
            string nameLower = vehicleName.ToLower();

            // FF1 TransportationType values (from docs/debug.md)
            if (nameLower.Contains("airship"))
                return 3; // Airship
            if (nameLower.Contains("canoe"))
                return 5; // Canoe
            if (nameLower.Contains("ship") || nameLower.Contains("boat"))
                return 2; // Ship

            return 0; // Unknown - will be filtered out
        }
    }
}
