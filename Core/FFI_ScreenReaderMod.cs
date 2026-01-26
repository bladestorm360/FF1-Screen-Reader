using MelonLoader;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Field;
using UnityEngine;
using HarmonyLib;
using System;
using System.Reflection;
using GameCursor = Il2CppLast.UI.Cursor;

[assembly: MelonInfo(typeof(FFI_ScreenReader.Core.FFI_ScreenReaderMod), "FFI Screen Reader", "1.0.0", "Author")]
[assembly: MelonGame("SQUARE ENIX, Inc.", "FINAL FANTASY")]

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Entity category for filtering navigation targets.
    /// </summary>
    public enum EntityCategory
    {
        All = 0,
        Chests = 1,
        NPCs = 2,
        MapExits = 3,
        Events = 4,
        Vehicles = 5
    }

    /// <summary>
    /// Main mod class for FFI Screen Reader.
    /// Provides screen reader accessibility support for Final Fantasy I Pixel Remaster.
    /// </summary>
    public class FFI_ScreenReaderMod : MelonMod
    {
        private static TolkWrapper tolk;
        private static FFI_ScreenReaderMod instance;
        private InputManager inputManager;
        private EntityScanner entityScanner;

        /// <summary>
        /// Static instance accessor for patches to access mod state.
        /// </summary>
        public static FFI_ScreenReaderMod Instance => instance;

        // Track last scanned map ID to detect map changes
        private int lastScannedMapId = -1;

        // Category count derived from enum for safe cycling
        private static readonly int CategoryCount = System.Enum.GetValues(typeof(EntityCategory)).Length;

        // Current category
        private EntityCategory currentCategory = EntityCategory.All;

        // Pathfinding filter toggle
        private bool filterByPathfinding = false;

        // Map exit filter toggle
        private bool filterMapExits = false;

        // Audio feedback toggles
        private bool enableWallTones = false;
        private bool enableFootsteps = false;
        private bool enableAudioBeacons = false;

        // Timer-based audio beacon
        private float lastBeaconTime = 0f;
        private const float BEACON_INTERVAL = 2.0f;  // Steady 2-second ping

        // Timer-based wall tones (continuous while near walls)
        private float lastWallToneLoopTime = 0f;
        private const float WALL_TONE_LOOP_INTERVAL = 0.1f;  // 100ms between tone loops

        // Map transition suppression for wall tones
        private int wallToneMapId = -1;
        private float wallToneSuppressedUntil = 0f;

        // Preferences
        private static MelonPreferences_Category prefsCategory;
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;
        private static MelonPreferences_Entry<bool> prefWallTones;
        private static MelonPreferences_Entry<bool> prefFootsteps;
        private static MelonPreferences_Entry<bool> prefAudioBeacons;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("FFI Screen Reader Mod loaded!");

            // Store static reference for callbacks from patches
            instance = this;

            // Subscribe to scene load events for automatic component caching
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize preferences
            prefsCategory = MelonPreferences.CreateCategory("FFI_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination, showing only the closest one");
            prefWallTones = prefsCategory.CreateEntry<bool>("WallTones", false, "Wall Tones", "Play directional tones when approaching walls");
            prefFootsteps = prefsCategory.CreateEntry<bool>("Footsteps", false, "Footsteps", "Play click sound on each tile movement");
            prefAudioBeacons = prefsCategory.CreateEntry<bool>("AudioBeacons", false, "Audio Beacons", "Play ping toward selected entity");

            // Load saved preferences
            filterByPathfinding = prefPathfindingFilter.Value;
            filterMapExits = prefMapExitFilter.Value;
            enableWallTones = prefWallTones.Value;
            enableFootsteps = prefFootsteps.Value;
            enableAudioBeacons = prefAudioBeacons.Value;

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize entity translator for Japanese -> English name translation
            EntityTranslator.Initialize();

            // Initialize external sound player for distinct audio feedback (wall bumps, etc.)
            SoundPlayer.Initialize();

            // Initialize input manager
            inputManager = new InputManager(this);

            // Initialize entity scanner for field navigation
            entityScanner = new EntityScanner();
            entityScanner.FilterByPathfinding = filterByPathfinding;

            // Try manual patching with error handling
            TryManualPatching();
        }

        /// <summary>
        /// Attempts to manually apply Harmony patches with detailed error logging.
        /// </summary>
        private void TryManualPatching()
        {
            LoggerInstance.Msg("Attempting manual Harmony patching...");

            var harmony = new HarmonyLib.Harmony("com.ffi.screenreader.manual");

            // Patch cursor navigation methods (menus)
            TryPatchCursorNavigation(harmony);

            // Character creation handled by NewGamePatches.cs (not CharacterCreationPatches which has wrong grid math)

            // Patch job/class selection screen (FF1-specific job list)
            JobSelectionPatches.ApplyPatches(harmony);

            // Config menu patches use HarmonyPatch attributes - auto-applied by MelonLoader

            // Dialogue patches (intro text, NPC dialogue, system messages)
            ScrollMessagePatches.ApplyPatches(harmony);
            MessageWindowPatches.ApplyPatches(harmony);

            // Shop patches (item list, command menu, quantity)
            ShopPatches.ApplyPatches(harmony);

            // Main menu patches (Item, Equipment, Magic, Status)
            MagicMenuPatches.ApplyPatches(harmony);
            StatusMenuPatches.ApplyPatches(harmony);
            StatusDetailsPatches.ApplyPatches(harmony);
            CharacterSelectTransitionPatches.ApplyPatches(harmony);

            // Battle patches
            BattleMessagePatches.ApplyPatches(harmony);
            BattleCommandPatches.ApplyPatches(harmony);
            BattleItemPatches.ApplyPatches(harmony);
            BattleMagicPatches.ApplyPatches(harmony);
            BattleResultPatches.ApplyPatches(harmony);
            BattleStartPatches.ApplyPatches(harmony);
            BattleControllerPatches.ApplyPatches(harmony); // Clear states on battle end (guarded by IsInBattle)
            MainMenuControllerPatches.ApplyPatches(harmony); // Clear battle states when returning to field

            // Vehicle/movement patches (ship, canoe, airship landing detection)
            // NOTE: Ported from FF3 - may require debugging for FF1-specific classes
            VehicleLandingPatches.ApplyPatches(harmony);
            MovementSpeechPatches.ApplyPatches(harmony);

            // Popup patches (confirmations, save/load, battle pause, game over)
            PopupPatches.ApplyPatches(harmony);
            BattlePausePatches.ApplyPatches(harmony);
            SaveLoadPatches.ApplyPatches(harmony);

            // New game character creation/naming
            NewGamePatches.ApplyPatches(harmony);

            // Map transition fade detection (suppress wall tones during screen fades)
            MapTransitionPatches.ApplyPatches(harmony);

            LoggerInstance.Msg("Manual patching complete");
        }

        /// <summary>
        /// Patches cursor navigation methods for menu reading.
        /// </summary>
        private void TryPatchCursorNavigation(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Use typeof() directly - much faster than assembly scanning
                Type cursorType = typeof(GameCursor);
                LoggerInstance.Msg($"Found Cursor type: {cursorType.FullName}");

                // Get postfix method
                var cursorPostfix = typeof(ManualPatches).GetMethod("CursorNavigation_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (cursorPostfix == null)
                {
                    LoggerInstance.Error("Could not find CursorNavigation_Postfix method");
                    return;
                }

                // Patch NextIndex
                var nextIndexMethod = cursorType.GetMethod("NextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (nextIndexMethod != null)
                {
                    harmony.Patch(nextIndexMethod, postfix: new HarmonyMethod(cursorPostfix));
                    LoggerInstance.Msg("Patched NextIndex");
                }

                // Patch PrevIndex
                var prevIndexMethod = cursorType.GetMethod("PrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (prevIndexMethod != null)
                {
                    harmony.Patch(prevIndexMethod, postfix: new HarmonyMethod(cursorPostfix));
                    LoggerInstance.Msg("Patched PrevIndex");
                }

                // Patch SkipNextIndex
                var skipNextMethod = cursorType.GetMethod("SkipNextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipNextMethod != null)
                {
                    harmony.Patch(skipNextMethod, postfix: new HarmonyMethod(cursorPostfix));
                    LoggerInstance.Msg("Patched SkipNextIndex");
                }

                // Patch SkipPrevIndex
                var skipPrevMethod = cursorType.GetMethod("SkipPrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipPrevMethod != null)
                {
                    harmony.Patch(skipPrevMethod, postfix: new HarmonyMethod(cursorPostfix));
                    LoggerInstance.Msg("Patched SkipPrevIndex");
                }

                LoggerInstance.Msg("Cursor navigation patches applied successfully");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error patching cursor navigation: {ex.Message}");
                LoggerInstance.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        public override void OnDeinitializeMelon()
        {
            // Unsubscribe from scene load events
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            CoroutineManager.CleanupAll();
            tolk?.Unload();
        }

        // Track previous scene for battle exit detection
        private string previousSceneName = "";

        /// <summary>
        /// Called when a new scene is loaded.
        /// Automatically caches commonly-used Unity components to avoid expensive FindObjectOfType calls.
        /// Also clears battle states when leaving battle.
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                LoggerInstance.Msg($"[ComponentCache] Scene loaded: {scene.name}");

                // Check if we're leaving a battle scene
                bool wasInBattle = previousSceneName.Contains("Battle");
                bool isInBattle = scene.name.Contains("Battle");

                if (wasInBattle && !isInBattle)
                {
                    // Left battle - clear all battle states
                    LoggerInstance.Msg("[Scene] Left battle scene - clearing all battle states");
                    BattleStateHelper.TryClearOnBattleEnd();
                    ClearAllMenuStates();
                    BattleCommandPatches.ClearCachedTargetController();
                }

                // Update previous scene name
                previousSceneName = scene.name;

                // Clear old cache
                GameObjectCache.ClearAll();

                // Suppress wall tones during scene transition
                wallToneSuppressedUntil = Time.time + 1.0f;
                SoundPlayer.StopWallTone();

                // Reset location message tracker (but NOT lastAnnouncedMapId —
                // battle transitions change scenes without changing maps)
                LocationMessageTracker.Reset();

                // Delay entity scan to allow scene to fully initialize
                CoroutineManager.StartManaged(DelayedInitialScan());
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"[ComponentCache] Error in OnSceneLoaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that delays entity scanning to allow scene to fully initialize.
        /// </summary>
        private System.Collections.IEnumerator DelayedInitialScan()
        {
            // Wait 0.5 seconds for scene to fully initialize and entities to spawn
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Scan for field entities
            try
            {
                entityScanner?.ScanEntities();
                LoggerInstance.Msg("[EntityScanner] Delayed scan complete");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[EntityScanner] Error during delayed scan: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            // Handle all input
            inputManager?.Update();

            // Timer-based audio beacon (independent of movement)
            if (enableAudioBeacons)
            {
                UpdateAudioBeacon();
            }

            // Continuous wall tones (play while near walls, independent of movement)
            if (enableWallTones)
            {
                UpdateWallTones();
            }
        }

        /// <summary>
        /// Updates the audio beacon on a steady 2-second interval.
        /// Independent of player movement for consistent pacing.
        /// </summary>
        private void UpdateAudioBeacon()
        {
            float currentTime = Time.time;
            if (currentTime - lastBeaconTime < BEACON_INTERVAL)
                return;

            lastBeaconTime = currentTime;

            var entity = entityScanner?.CurrentEntity;
            if (entity == null) return;

            var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController?.fieldPlayer == null) return;

            Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
            Vector3 entityPos = entity.Position;

            // Calculate distance and volume (louder when closer)
            float distance = Vector3.Distance(playerPos, entityPos);
            float maxDist = 500f;  // ~31 tiles
            float volumeScale = Mathf.Clamp(1f - (distance / maxDist), 0.15f, 0.60f);

            // Calculate pan (0.0 = left, 0.5 = center, 1.0 = right)
            float deltaX = entityPos.x - playerPos.x;
            float pan = Mathf.Clamp(deltaX / 100f, -1f, 1f) * 0.5f + 0.5f;

            // Check if entity is south of player (use lower pitch)
            bool isSouth = entityPos.y < playerPos.y - 8f;

            SoundPlayer.PlayBeacon(isSouth, pan, volumeScale);
        }

        /// <summary>
        /// Updates wall tones on a continuous interval.
        /// Detects adjacent walls and plays continuous looping tones via hardware looping.
        /// Only restarts the loop when detected directions change.
        /// </summary>
        private void UpdateWallTones()
        {
            float currentTime = Time.time;
            if (currentTime - lastWallToneLoopTime < WALL_TONE_LOOP_INTERVAL)
                return;

            lastWallToneLoopTime = currentTime;

            // Detect sub-map transitions (same scene, different map ID) and suppress tones briefly
            int currentMapId = GetCurrentMapId();
            if (currentMapId > 0 && wallToneMapId > 0 && currentMapId != wallToneMapId)
            {
                wallToneSuppressedUntil = currentTime + 1.0f;
                SoundPlayer.StopWallTone();
            }
            if (currentMapId > 0)
                wallToneMapId = currentMapId;

            if (currentTime < wallToneSuppressedUntil)
            {
                SoundPlayer.StopWallTone();
                return;
            }

            if (MapTransitionPatches.IsScreenFading)
            {
                SoundPlayer.StopWallTone();
                return;
            }

            var player = GetFieldPlayer();
            if (player == null)
            {
                SoundPlayer.StopWallTone();
                return;
            }

            try
            {
                var walls = FieldNavigationHelper.GetNearbyWallsWithDistance(player);

                // Get map exit positions to suppress false wall tones at exits/doors/stairs
                var mapExitPositions = entityScanner?.GetMapExitPositions();
                Vector3 playerPos = player.transform.localPosition;

                // Collect adjacent wall directions, skipping directions that lead to map exits
                var directions = new System.Collections.Generic.List<SoundPlayer.Direction>();

                if (walls.NorthDist == 0 &&
                    !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(0, 16, 0), mapExitPositions))
                    directions.Add(SoundPlayer.Direction.North);

                if (walls.SouthDist == 0 &&
                    !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(0, -16, 0), mapExitPositions))
                    directions.Add(SoundPlayer.Direction.South);

                if (walls.EastDist == 0 &&
                    !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(16, 0, 0), mapExitPositions))
                    directions.Add(SoundPlayer.Direction.East);

                if (walls.WestDist == 0 &&
                    !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, new Vector3(-16, 0, 0), mapExitPositions))
                    directions.Add(SoundPlayer.Direction.West);

                // PlayWallTonesLooped handles start/stop/update:
                // - No walls: stops the loop
                // - Same directions: no-op (loop continues uninterrupted)
                // - Different directions: restarts loop with new mix
                SoundPlayer.PlayWallTonesLooped(directions.ToArray());
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[WallTones] Error: {ex.Message}");
            }
        }

        #region Entity Navigation

        /// <summary>
        /// Gets the current map ID from FieldMapProvisionInformation.
        /// Returns -1 if unable to retrieve.
        /// </summary>
        private int GetCurrentMapId()
        {
            try
            {
                var fieldMapInfo = Il2CppLast.Map.FieldMapProvisionInformation.Instance;
                if (fieldMapInfo != null)
                {
                    return fieldMapInfo.CurrentMapId;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Refreshes entities if needed - called on user input (J/K/L keys).
        /// Event-driven: only rescans on map change or empty entity list.
        /// Map changes detected by comparing current map ID to last scanned.
        /// </summary>
        private void RefreshEntitiesIfNeeded()
        {
            if (entityScanner == null)
                return;

            int currentMapId = GetCurrentMapId();

            // Check if map changed since last scan (catches FF1 sub-map transitions like entering shops)
            bool mapChanged = (currentMapId != lastScannedMapId && currentMapId > 0 && lastScannedMapId > 0);

            if (mapChanged)
            {
                MelonLogger.Msg($"[RefreshEntities] Map changed: {lastScannedMapId} -> {currentMapId}, clearing cache and rescanning");

                // Clear FieldPlayerController cache to get fresh mapHandle for pathfinding
                GameObjectCache.Clear<Il2CppLast.Map.FieldPlayerController>();

                // Force rescan
                entityScanner.ScanEntities();
                lastScannedMapId = currentMapId;
            }
            else if (entityScanner.Entities.Count == 0)
            {
                // Only rescan if entity list is empty
                MelonLogger.Msg("[RefreshEntities] Empty entity list, rescanning");
                entityScanner.ScanEntities();

                // Update tracked map ID if not set
                if (lastScannedMapId <= 0 && currentMapId > 0)
                {
                    lastScannedMapId = currentMapId;
                }
            }
        }

        internal void AnnounceCurrentEntity()
        {
            // Refresh entities if map changed or time interval passed
            RefreshEntitiesIfNeeded();

            var entity = entityScanner?.CurrentEntity;
            if (entity == null)
            {
                SpeakText("No entity selected");
                return;
            }

            // Get player position for distance/direction
            var context = new FilterContext();

            if (context.PlayerPosition == Vector3.zero)
            {
                MelonLogger.Msg("[AnnounceEntity] Player position is zero - controller not found");
                SpeakText("Cannot determine directions");
                return;
            }

            // Get pathfinding info
            var pathInfo = FieldNavigationHelper.FindPathTo(
                context.PlayerPosition,
                entity.Position,
                context.MapHandle,
                context.FieldPlayer
            );

            // Only announce directions, not entity name (user requested)
            string announcement;
            if (pathInfo.Success && !string.IsNullOrEmpty(pathInfo.Description))
            {
                announcement = pathInfo.Description;
            }
            else
            {
                // Pathfinding failed - say "No path" instead of straight-line distance
                announcement = "No path";
            }

            SpeakText(announcement);
        }

        internal void CycleNext()
        {
            if (entityScanner == null)
            {
                SpeakText("Entity scanner not available");
                return;
            }

            // Refresh entities if map changed or time interval passed
            RefreshEntitiesIfNeeded();

            entityScanner.NextEntity();

            // Check if pathfinding filter is on but no entities are reachable
            if (entityScanner.NoReachableEntities())
            {
                SpeakText("No reachable entities");
                return;
            }

            AnnounceEntityOnly();
        }

        internal void CyclePrevious()
        {
            if (entityScanner == null)
            {
                SpeakText("Entity scanner not available");
                return;
            }

            // Refresh entities if map changed or time interval passed
            RefreshEntitiesIfNeeded();

            entityScanner.PreviousEntity();

            // Check if pathfinding filter is on but no entities are reachable
            if (entityScanner.NoReachableEntities())
            {
                SpeakText("No reachable entities");
                return;
            }

            AnnounceEntityOnly();
        }

        internal void AnnounceEntityOnly()
        {
            // Refresh entities if map changed or time interval passed
            RefreshEntitiesIfNeeded();

            var entity = entityScanner?.CurrentEntity;
            if (entity == null)
            {
                string categoryName = GetCategoryName(currentCategory);
                int count = entityScanner?.Entities?.Count ?? 0;
                if (count == 0)
                {
                    SpeakText($"No {categoryName} found");
                }
                else
                {
                    SpeakText("No entity selected");
                }
                return;
            }

            // Get player position for distance/direction
            var context = new FilterContext();
            string announcement = entity.FormatDescription(context.PlayerPosition);

            // Add index info
            int index = entityScanner.CurrentIndex + 1;
            int total = entityScanner.Entities.Count;
            announcement += $" ({index} of {total})";

            MelonLoader.MelonLogger.Msg($"[Entity] {announcement}");
            SpeakText(announcement);
        }

        #endregion

        #region Category Navigation

        internal void CycleNextCategory()
        {
            // Cycle to next category
            int nextCategory = ((int)currentCategory + 1) % CategoryCount;
            currentCategory = (EntityCategory)nextCategory;
            if (entityScanner != null)
                entityScanner.CurrentCategory = currentCategory;

            AnnounceCategoryChange();
        }

        internal void CyclePreviousCategory()
        {
            // Cycle to previous category
            int prevCategory = (int)currentCategory - 1;
            if (prevCategory < 0)
                prevCategory = CategoryCount - 1;

            currentCategory = (EntityCategory)prevCategory;
            if (entityScanner != null)
                entityScanner.CurrentCategory = currentCategory;

            AnnounceCategoryChange();
        }

        internal void ResetToAllCategory()
        {
            if (currentCategory == EntityCategory.All)
            {
                SpeakText("Already in All category");
                return;
            }

            currentCategory = EntityCategory.All;
            if (entityScanner != null)
                entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        private void AnnounceCategoryChange()
        {
            string categoryName = GetCategoryName(currentCategory);
            string announcement = $"Category: {categoryName}";
            SpeakText(announcement);
        }

        public static string GetCategoryName(EntityCategory category)
        {
            switch (category)
            {
                case EntityCategory.All: return "All";
                case EntityCategory.Chests: return "Treasure Chests";
                case EntityCategory.NPCs: return "NPCs";
                case EntityCategory.MapExits: return "Map Exits";
                case EntityCategory.Events: return "Events";
                case EntityCategory.Vehicles: return "Vehicles";
                default: return "Unknown";
            }
        }

        #endregion

        #region Filter Toggles

        internal void TogglePathfindingFilter()
        {
            filterByPathfinding = !filterByPathfinding;

            if (entityScanner != null)
                entityScanner.FilterByPathfinding = filterByPathfinding;

            // Save to preferences
            prefPathfindingFilter.Value = filterByPathfinding;
            prefsCategory.SaveToFile(false);

            string status = filterByPathfinding ? "on" : "off";
            SpeakText($"Pathfinding filter {status}");
        }

        internal void ToggleMapExitFilter()
        {
            filterMapExits = !filterMapExits;

            // Save to preferences
            prefMapExitFilter.Value = filterMapExits;
            prefsCategory.SaveToFile(false);

            string status = filterMapExits ? "on" : "off";
            SpeakText($"Map exit filter {status}");
        }

        internal void ToggleWallTones()
        {
            enableWallTones = !enableWallTones;

            // Stop any playing wall tone loop immediately when toggling off
            if (!enableWallTones)
                SoundPlayer.StopWallTone();

            // Save to preferences
            prefWallTones.Value = enableWallTones;
            prefsCategory.SaveToFile(false);

            string status = enableWallTones ? "on" : "off";
            SpeakText($"Wall tones {status}");
        }

        internal void ToggleFootsteps()
        {
            enableFootsteps = !enableFootsteps;

            // Save to preferences
            prefFootsteps.Value = enableFootsteps;
            prefsCategory.SaveToFile(false);

            string status = enableFootsteps ? "on" : "off";
            SpeakText($"Footsteps {status}");
        }

        internal void ToggleAudioBeacons()
        {
            enableAudioBeacons = !enableAudioBeacons;

            // Save to preferences
            prefAudioBeacons.Value = enableAudioBeacons;
            prefsCategory.SaveToFile(false);

            string status = enableAudioBeacons ? "on" : "off";
            SpeakText($"Audio beacons {status}");
        }

        // Accessors for audio feedback state (used by MovementSoundPatches)
        internal bool IsWallTonesEnabled() => enableWallTones;
        internal bool IsFootstepsEnabled() => enableFootsteps;
        internal bool IsAudioBeaconsEnabled() => enableAudioBeacons;

        /// <summary>
        /// Gets the currently selected entity for audio beacon tracking.
        /// </summary>
        internal NavigableEntity GetSelectedEntity() => entityScanner?.CurrentEntity;

        #endregion

        #region Teleportation

        /// <summary>
        /// Gets the FieldPlayer from the FieldPlayerController.
        /// Uses direct IL2CPP access (not reflection, which doesn't work on IL2CPP types).
        /// </summary>
        private Il2CppLast.Entity.Field.FieldPlayer GetFieldPlayer()
        {
            try
            {
                // Use FieldPlayerController - same pattern used in GetPlayerPosition() and pathfinding
                var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                {
                    return playerController.fieldPlayer;
                }

                // Fallback: try to find and cache if cache returned null (e.g., after scene transition)
                playerController = UnityEngine.Object.FindObjectOfType<Il2CppLast.Map.FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                {
                    return playerController.fieldPlayer;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting field player: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Teleports the player to a position relative to the currently selected entity.
        /// </summary>
        /// <param name="offset">The offset from the entity position (16 units = 1 tile)</param>
        internal void TeleportInDirection(Vector2 offset)
        {
            try
            {
                var player = GetFieldPlayer();
                if (player == null)
                {
                    SpeakText("Not on field map", true);
                    return;
                }

                // Get the currently selected entity
                var entity = entityScanner.CurrentEntity;
                if (entity == null)
                {
                    SpeakText("No entity selected", true);
                    return;
                }

                // Calculate target position: entity position + offset
                Vector3 entityPos = entity.Position;
                Vector3 targetPos = entityPos + new Vector3(offset.x, offset.y, 0);

                // Teleport player to target position
                player.transform.localPosition = targetPos;

                // Announce with direction relative to entity and entity name
                string direction = GetDirectionFromOffset(offset);
                string entityName = entity.Name;
                SpeakText($"Teleported to {direction} of {entityName}", true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error teleporting: {ex.Message}");
                SpeakText("Teleport failed", true);
            }
        }

        /// <summary>
        /// Converts an offset vector to a cardinal direction string.
        /// </summary>
        private string GetDirectionFromOffset(Vector2 offset)
        {
            if (Math.Abs(offset.x) > Math.Abs(offset.y))
            {
                return offset.x > 0 ? "east" : "west";
            }
            else
            {
                return offset.y > 0 ? "north" : "south";
            }
        }

        #endregion

        #region Game Information

        internal void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();
                if (userDataManager != null)
                {
                    int gil = userDataManager.OwendGil;
                    SpeakText($"{gil} Gil", true);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[AnnounceGilAmount] Error: {ex.Message}");
            }
            SpeakText("Gil not available", true);
        }

        internal void AnnounceCurrentMap()
        {
            try
            {
                string mapName = Field.MapNameResolver.GetCurrentMapName();
                SpeakText(mapName, true);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[AnnounceCurrentMap] Error: {ex.Message}");
                SpeakText("Unknown location", true);
            }
        }

        internal void AnnounceCharacterStatus()
        {
            try
            {
                // H key only works in battle
                if (!Patches.BattleStateHelper.IsInBattle)
                {
                    SpeakText("Party status only available in battle", true);
                    return;
                }

                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();
                if (userDataManager == null)
                {
                    SpeakText("Character data not available", true);
                    return;
                }

                // Get party characters using GetOwnedCharactersClone
                var partyList = userDataManager.GetOwnedCharactersClone(false);
                if (partyList == null || partyList.Count == 0)
                {
                    SpeakText("No party members", true);
                    return;
                }

                var sb = new System.Text.StringBuilder();
                foreach (var charData in partyList)
                {
                    try
                    {
                        if (charData != null)
                        {
                            string name = charData.Name;
                            var param = charData.Parameter;
                            if (param != null)
                            {
                                int currentHp = param.CurrentHP;
                                int maxHp = param.ConfirmedMaxHp();

                                // FF1 uses spell charges per level, not MP
                                // Just report HP for now as that's most useful in battle
                                sb.AppendLine($"{name}: {currentHp}/{maxHp} HP");
                            }
                        }
                    }
                    catch { }
                }

                string status = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(status))
                {
                    SpeakText(status, true);
                }
                else
                {
                    SpeakText("No character status available", true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[AnnounceCharacterStatus] Error: {ex.Message}");
                SpeakText("Character status not available", true);
            }
        }

        #endregion

        /// <summary>
        /// Speak text through the screen reader.
        /// Thread-safe: TolkWrapper uses locking to prevent concurrent native calls.
        /// </summary>
        /// <param name="text">Text to speak.</param>
        /// <param name="interrupt">Whether to interrupt current speech (true for user actions, false for game events).</param>
        public static void SpeakText(string text, bool interrupt = true)
        {
            tolk?.Speak(text, interrupt);
        }

        /// <summary>
        /// Clears all menu states except the specified one.
        /// Called by patches when a menu activates to ensure only one menu is active at a time.
        /// </summary>
        public static void ClearOtherMenuStates(string exceptMenu)
        {
            ManualPatches.ClearOtherMenuStates(exceptMenu);
        }

        /// <summary>
        /// Clears all menu state flags.
        /// </summary>
        public static void ClearAllMenuStates()
        {
            ManualPatches.ClearAllMenuStates();
        }
    }

    /// <summary>
    /// Manual patch methods for Harmony.
    /// </summary>
    public static class ManualPatches
    {
        /// <summary>
        /// Postfix for cursor navigation methods (NextIndex, PrevIndex, etc.)
        /// Reads the menu text at the current cursor position.
        /// Only suppressed when a specific patch is actively handling that menu.
        /// </summary>
        public static void CursorNavigation_Postfix(object __instance)
        {
            try
            {
                // Cast to the actual Cursor type
                var cursor = __instance as GameCursor;
                if (cursor == null)
                {
                    return;
                }

                // === BUILD CURSOR PATH FOR DETECTION ===
                // Build path with up to 3 levels: grandparent/parent/cursor
                string cursorPath = "";
                try
                {
                    var t = cursor.transform;
                    cursorPath = t?.name ?? "null";
                    if (t?.parent != null) cursorPath = t.parent.name + "/" + cursorPath;
                    if (t?.parent?.parent != null) cursorPath = t.parent.parent.name + "/" + cursorPath;
                }
                catch { cursorPath = "error"; }

                // === BATTLE PAUSE MENU SPECIAL CASE ===
                // Must be checked BEFORE suppression because battle states would suppress it.
                // Cursor path contains "curosr_parent" (game typo) when in pause menu.
                if (cursorPath.Contains("curosr_parent"))
                {
                    MelonLogger.Msg($"[Cursor Nav] Battle pause menu detected (path={cursorPath}) - reading directly");
                    CoroutineManager.StartManaged(
                        MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false)
                    );
                    return;
                }

                // Skip if new game or job selection is handling cursor events
                if (NewGamePatches.IsHandlingCursor || JobSelectionPatches.IsHandlingCursor)
                {
                    // Clear flags to prevent them getting stuck if user navigates away
                    NewGamePatches.ClearHandlingFlag();
                    JobSelectionPatches.ClearHandlingFlag();
                    return;
                }

                // === ACTIVE STATE CHECKS ===
                // Only suppress when specific patches are actively handling announcements
                // ShouldSuppress() validates controller is still active (auto-resets stuck flags)

                // Popup with buttons - read button text via PopupPatches
                if (PopupState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(cursor);
                    return;
                }

                // Save/load confirmation - also uses PopupState for button reading
                if (SaveLoadMenuState.ShouldSuppress())
                {
                    PopupPatches.ReadCurrentButton(cursor);
                    return;
                }

                // Config menu - needs current setting values
                if (ConfigMenuState.ShouldSuppress()) return;

                // Battle command menu - SetCursor patch handles command announcements
                if (BattleCommandState.ShouldSuppress())
                {
                    MelonLogger.Msg("[Cursor Nav] SUPPRESSED by BattleCommandState");
                    return;
                }

                // Battle target selection - needs target HP/status
                if (BattleTargetState.ShouldSuppress())
                {
                    MelonLogger.Msg("[Cursor Nav] SUPPRESSED by BattleTargetState");
                    return;
                }

                // Battle item menu - needs item data in battle
                if (BattleItemMenuState.ShouldSuppress()) return;

                // Battle magic menu - needs spell data in battle
                if (BattleMagicMenuState.ShouldSuppress()) return;

                // Shop menu - needs item price/stats
                if (ShopMenuTracker.ValidateState()) return;

                // Item menu - needs item data in main menu
                if (ItemMenuState.ShouldSuppress()) return;

                // Equipment menu - needs equipment slot/item data
                if (EquipMenuState.ShouldSuppress()) return;

                // Magic menu - needs spell data and charges
                if (MagicMenuState.ShouldSuppress()) return;

                // Status menu (character selection) - needs character status data
                if (StatusMenuState.ShouldSuppress()) return;

                // Status details (individual character stats) - has its own navigation
                if (StatusDetailsState.ShouldSuppress()) return;

                // === DEFAULT: Read via MenuTextDiscovery ===
                // Start coroutine to read after one frame (allows UI to update)
                CoroutineManager.StartManaged(
                    MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false)
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CursorNavigation_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all menu state flags.
        /// </summary>
        public static void ClearAllMenuStates()
        {
            ConfigMenuState.ResetState();
            BattleCommandState.ClearState();
            BattleTargetState.ClearState();
            BattleItemMenuState.Reset();
            BattleMagicMenuState.Reset();
            ShopMenuTracker.ClearState();
            ItemMenuState.ResetState();
            EquipMenuState.ResetState();
            MagicMenuState.ResetState();
            StatusMenuState.ResetState();
            StatusDetailsState.ResetState();
            PopupState.Clear();
            SaveLoadMenuState.ResetState();
            BattlePausePatches.Reset();
        }

        /// <summary>
        /// Clears all menu states except the specified one. Called when a menu activates.
        /// </summary>
        public static void ClearOtherMenuStates(string exceptMenu)
        {
            if (exceptMenu != "Config") ConfigMenuState.ResetState();
            if (exceptMenu != "BattleCommand") BattleCommandState.ClearState();
            if (exceptMenu != "BattleTarget") BattleTargetState.ClearState();
            if (exceptMenu != "BattleItem") BattleItemMenuState.Reset();
            if (exceptMenu != "BattleMagic") BattleMagicMenuState.Reset();
            if (exceptMenu != "Shop") ShopMenuTracker.ClearState();
            if (exceptMenu != "Item") ItemMenuState.ResetState();
            if (exceptMenu != "Equip") EquipMenuState.ResetState();
            if (exceptMenu != "Magic") MagicMenuState.ResetState();
            if (exceptMenu != "Status") StatusMenuState.ResetState();
            if (exceptMenu != "StatusDetails") StatusDetailsState.ResetState();
        }
    }
}
