using MelonLoader;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Field;
using UnityEngine;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameCursor = Il2CppLast.UI.Cursor;
using static FFI_ScreenReader.Utils.ModTextTranslator;

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

        // Extracted managers
        private AudioLoopManager audioLoopManager;
        private EntityNavigationManager entityNav;
        private CategoryManager categories;

        // Waypoint system
        private WaypointManager waypointManager;
        private WaypointNavigator waypointNavigator;
        private WaypointController waypointController;

        /// <summary>
        /// Static instance accessor for patches to access mod state.
        /// </summary>
        public static FFI_ScreenReaderMod Instance => instance;

        // Pathfinding filter toggle
        private bool filterByPathfinding = false;

        // Map exit filter toggle
        private bool filterMapExits = false;

        // ToLayer (layer transition) filter toggle
        private bool filterToLayer = false;

        // Audio feedback toggles
        private bool enableWallTones = false;
        private bool enableFootsteps = false;
        private bool enableAudioBeacons = false;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("FFI Screen Reader Mod loaded!");

            // Store static reference for callbacks from patches
            instance = this;

            // Subscribe to scene load events for automatic component caching
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize mod text translator for localization
            ModTextTranslator.Initialize();

            // Initialize preferences
            PreferencesManager.Initialize();

            // Load saved preferences
            filterByPathfinding = PreferencesManager.PathfindingFilterDefault;
            filterMapExits = PreferencesManager.MapExitFilterDefault;
            filterToLayer = PreferencesManager.ToLayerFilterDefault;
            enableWallTones = PreferencesManager.WallTonesDefault;
            enableFootsteps = PreferencesManager.FootstepsDefault;
            enableAudioBeacons = PreferencesManager.AudioBeaconsDefault;

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize entity translator for Japanese -> English name translation
            EntityTranslator.Initialize();

            // Initialize external sound player for distinct audio feedback (wall bumps, etc.)
            SoundPlayer.Initialize();

            // Initialize SDL3 gamepad support
            GamepadManager.Initialize();

            // Initialize entity scanner for field navigation
            entityScanner = new EntityScanner();
            entityScanner.FilterByPathfinding = filterByPathfinding;
            entityScanner.FilterToLayer = filterToLayer;

            // Initialize managers (order matters: entityNav before categories)
            entityNav = new EntityNavigationManager(entityScanner, () => categories?.CurrentCategory ?? EntityCategory.All);
            categories = new CategoryManager(entityScanner, entityNav);
            audioLoopManager = new AudioLoopManager(this, entityScanner);

            // Initialize waypoint system
            waypointManager = new WaypointManager();
            waypointNavigator = new WaypointNavigator(waypointManager);
            waypointController = new WaypointController(entityNav, waypointManager, waypointNavigator);
            Handlers.WaypointHandler.Initialize(waypointController);

            // Initialize input manager
            inputManager = new InputManager(this);

            // Initialize mod menu
            ModMenu.Initialize();

            // Try manual patching with error handling
            TryManualPatching();

            // NOTE: Audio loops (wall tones, beacons) are NOT started here.
            // They start from DelayedInitialScan() after scene loads, or from user toggle.
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

            // Equipment menu patches (command bar announcements)
            EquipMenuPatches.ApplyPatches(harmony);

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
            VehicleLandingPatches.ApplyPatches(harmony);
            MovementSpeechPatches.ApplyPatches(harmony);
            DashFlagPatches.ApplyPatches(harmony);

            // Popup patches (confirmations, save/load, battle pause, game over)
            PopupPatches.ApplyPatches(harmony);
            BattlePausePatches.ApplyPatches(harmony);
            SaveLoadPatches.ApplyPatches(harmony);

            // New game character creation/naming
            NewGamePatches.ApplyPatches(harmony);

            // Map transition fade detection (suppress wall tones during screen fades)
            MapTransitionPatches.ApplyPatches(harmony);

            // Config menu controls reading
            ConfigMenuPatches.ApplyPatches(harmony);

            // Game state patches (config menu bestiary dispatch)
            GameStatePatches.ApplyPatches(harmony);

            // Extras menu patches (bestiary, music player, gallery)
            try { BestiaryManualPatches.ApplyPatches(harmony); }
            catch (Exception ex) { LoggerInstance.Error($"[Bestiary] Fatal error loading patches: {ex}"); }

            try { MusicPlayerManualPatches.ApplyPatches(harmony); }
            catch (Exception ex) { LoggerInstance.Error($"[MusicPlayer] Fatal error loading patches: {ex}"); }

            try { GalleryManualPatches.ApplyPatches(harmony); }
            catch (Exception ex) { LoggerInstance.Error($"[Gallery] Fatal error loading patches: {ex}"); }

            // SDL input passthrough — injects SDL controller state into InputSystemManager
            // and suppresses game input when mod is consuming
            InputPassthroughPatches.ApplyPatches(harmony);

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

            // Stop audio loops
            audioLoopManager?.StopAll();

            // Shutdown sound player (closes waveOut handles, frees unmanaged memory)
            SoundPlayer.Shutdown();

            CoroutineManager.CleanupAll();
            ControllerRouter.Reset();
            GamepadManager.Shutdown();
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
                    AnnouncementDeduplicator.ResetAll();
                }

                // Update previous scene name
                previousSceneName = scene.name;

                // Clear old cache
                GameObjectCache.ClearAll();

                // Stop audio loops during scene transition and suppress briefly
                audioLoopManager?.StopAll();
                audioLoopManager?.SuppressBriefly(1.0f);

                // Reset movement state for new map
                MovementSoundPatches.ResetState();

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
        private IEnumerator DelayedInitialScan()
        {
            // Wait 0.5 seconds for scene to fully initialize and entities to spawn
            yield return new WaitForSeconds(0.5f);

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

            // Only restart audio loops on valid field scenes (not title, boot, splash, etc.)
            var fieldPlayerController = GameObjectCache.Refresh<Il2CppLast.Map.FieldPlayerController>();
            if (fieldPlayerController != null)
            {
                audioLoopManager?.StartIfEnabled();
            }
        }

        public override void OnUpdate()
        {
            inputManager?.Update();

        }

        #region Forwarding Methods for InputManager

        // Entity navigation
        internal void CycleNext() => entityNav?.CycleNext();
        internal void CyclePrevious() => entityNav?.CyclePrevious();
        internal void AnnounceCurrentEntity() => entityNav?.AnnounceCurrentEntity();
        internal void AnnounceEntityOnly() => entityNav?.AnnounceEntityOnly();

        // Category navigation
        internal void CycleNextCategory() => categories?.CycleNext();
        internal void CyclePreviousCategory() => categories?.CyclePrevious();
        internal void ResetToAllCategory() => categories?.ResetToAll();

        #endregion

        #region Filter Toggles

        private void SaveAndAnnounce(string featureName, bool value)
        {
            SpeakText($"{featureName} {(value ? T("on") : T("off"))}");
        }

        internal void TogglePathfindingFilter()
        {
            filterByPathfinding = !filterByPathfinding;
            if (entityScanner != null)
                entityScanner.FilterByPathfinding = filterByPathfinding;
            PreferencesManager.SavePathfindingFilter(filterByPathfinding);
            SaveAndAnnounce(T("Pathfinding filter"), filterByPathfinding);
        }

        internal void ToggleMapExitFilter()
        {
            filterMapExits = !filterMapExits;
            PreferencesManager.SaveMapExitFilter(filterMapExits);
            entityScanner?.ReapplyFilter();
            SaveAndAnnounce(T("Map exit filter"), filterMapExits);
        }

        internal void ToggleToLayerFilter()
        {
            filterToLayer = !filterToLayer;
            if (entityScanner != null)
                entityScanner.FilterToLayer = filterToLayer;
            PreferencesManager.SaveToLayerFilter(filterToLayer);
            SaveAndAnnounce(T("Layer transition filter"), filterToLayer);
        }

        internal void ToggleWallTones()
        {
            enableWallTones = !enableWallTones;
            if (enableWallTones) audioLoopManager?.StartWallToneLoop(); else audioLoopManager?.StopWallToneLoop();
            PreferencesManager.SaveWallTones(enableWallTones);
            SaveAndAnnounce(T("Wall tones"), enableWallTones);
        }

        internal void ToggleFootsteps()
        {
            enableFootsteps = !enableFootsteps;
            PreferencesManager.SaveFootsteps(enableFootsteps);
            SaveAndAnnounce(T("Footsteps"), enableFootsteps);
        }

        internal void ToggleAudioBeacons()
        {
            enableAudioBeacons = !enableAudioBeacons;
            if (enableAudioBeacons) audioLoopManager?.StartBeaconLoop(); else audioLoopManager?.StopBeaconLoop();
            PreferencesManager.SaveAudioBeacons(enableAudioBeacons);
            SaveAndAnnounce(T("Audio beacons"), enableAudioBeacons);
        }

        // Accessors for audio feedback state (used by AudioLoopManager and MovementSoundPatches)
        internal bool IsWallTonesEnabled() => enableWallTones;
        internal bool IsFootstepsEnabled() => enableFootsteps;
        internal bool IsAudioBeaconsEnabled() => enableAudioBeacons;

        // Public static accessors for filter/toggle settings (used by ModMenu)
        public static bool PathfindingFilterEnabled => instance?.filterByPathfinding ?? false;
        public static bool MapExitFilterEnabled => instance?.filterMapExits ?? false;
        public static bool ToLayerFilterEnabled => instance?.filterToLayer ?? false;
        public static bool WallTonesEnabled => instance?.enableWallTones ?? false;
        public static bool FootstepsEnabled => instance?.enableFootsteps ?? false;
        public static bool AudioBeaconsEnabled => instance?.enableAudioBeacons ?? false;

        /// <summary>
        /// Gets the currently selected entity for audio beacon tracking.
        /// </summary>
        internal NavigableEntity GetSelectedEntity() => entityScanner?.CurrentEntity;

        #endregion

        #region Shared Utility Methods

        /// <summary>
        /// Gets the current map ID from FieldMapProvisionInformation.
        /// Returns -1 if unable to retrieve.
        /// </summary>
        internal static int GetCurrentMapId()
        {
            try
            {
                var fieldMapInfo = Il2CppLast.Map.FieldMapProvisionInformation.Instance;
                if (fieldMapInfo != null)
                    return fieldMapInfo.CurrentMapId;
            }
            catch { } // Map info unavailable during scene transitions
            return -1;
        }

        /// <summary>
        /// Gets the FieldPlayer from the FieldPlayerController.
        /// Uses GameObjectCache with refresh fallback.
        /// </summary>
        internal static Il2CppLast.Entity.Field.FieldPlayer GetFieldPlayer()
        {
            try
            {
                var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                    return playerController.fieldPlayer;

                playerController = GameObjectCache.Refresh<Il2CppLast.Map.FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                    return playerController.fieldPlayer;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting field player: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Teleportation

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
                    SpeakText(T("Not on field map"), true);
                    return;
                }

                // Get the currently selected entity
                var entity = entityScanner.CurrentEntity;
                if (entity == null)
                {
                    SpeakText(T("No entity selected"), true);
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
                SpeakText(string.Format(T("Teleported to {0} of {1}"), direction, entityName), true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error teleporting: {ex.Message}");
                SpeakText(T("Teleport failed"), true);
            }
        }

        private string GetDirectionFromOffset(Vector2 offset)
        {
            if (Math.Abs(offset.x) > Math.Abs(offset.y))
                return offset.x > 0 ? T("east") : T("west");
            else
                return offset.y > 0 ? T("north") : T("south");
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
                    SpeakText(string.Format(T("{0} Gil"), gil), true);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[AnnounceGilAmount] Error: {ex.Message}");
            }
            SpeakText(T("Gil not available"), true);
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
                SpeakText(T("Unknown location"), true);
            }
        }

        internal void AnnounceCharacterStatus()
        {
            try
            {
                // H key only works in battle
                if (!Patches.BattleStateHelper.IsInBattle)
                {
                    SpeakText(T("Party status only available in battle"), true);
                    return;
                }

                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();
                if (userDataManager == null)
                {
                    SpeakText(T("Character data not available"), true);
                    return;
                }

                // Get party characters using GetOwnedCharactersClone
                var partyList = userDataManager.GetOwnedCharactersClone(false);
                if (partyList == null || partyList.Count == 0)
                {
                    SpeakText(T("No party members"), true);
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
                                string hpLine = string.Format(T("{0}: {1}/{2} HP"), name, currentHp, maxHp);

                                // Append active status effects
                                try
                                {
                                    var conditionList = param.CurrentConditionList;
                                    if (conditionList != null && conditionList.Count > 0)
                                    {
                                        var statusNames = new System.Collections.Generic.List<string>();
                                        foreach (var condition in conditionList)
                                        {
                                            string condName = MagicMenuState.GetConditionName(condition);
                                            if (!string.IsNullOrWhiteSpace(condName))
                                                statusNames.Add(condName);
                                        }
                                        if (statusNames.Count > 0)
                                            hpLine += " " + string.Join(", ", statusNames);
                                    }
                                }
                                catch { } // Status effect reading is non-critical

                                sb.AppendLine(hpLine);
                            }
                        }
                    }
                    catch { } // Individual character data may be invalid
                }

                string status = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(status))
                    SpeakText(status, true);
                else
                    SpeakText(T("No character status available"), true);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[AnnounceCharacterStatus] Error: {ex.Message}");
                SpeakText(T("Character status not available"), true);
            }
        }

        #endregion

        /// <summary>
        /// Speak text through the screen reader.
        /// Thread-safe: TolkWrapper uses locking to prevent concurrent native calls.
        /// </summary>
        public static void SpeakText(string text, bool interrupt = true)
        {
            MelonLoader.MelonLogger.Msg($"[TTS] {text}");
            tolk?.Speak(text, interrupt);
        }

        /// <summary>
        /// Silences current speech immediately. Used by controller navigation
        /// to interrupt ongoing announcements (e.g., pathfinding directions)
        /// since NVDA doesn't see controller input as key events.
        /// </summary>
        public static void InterruptSpeech()
        {
            tolk?.Silence();
        }

        /// <summary>
        /// Clears all menu states except the specified one.
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
            AnnouncementDeduplicator.ResetAll();
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
                catch { cursorPath = "error"; } // Transform hierarchy may be disposed

                // === BATTLE PAUSE MENU SPECIAL CASE ===
                // Must be checked BEFORE suppression because battle states would suppress it.
                // Cursor path contains "curosr_parent" (game typo) when in pause menu.
                if (cursorPath.Contains("curosr_parent"))
                {
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
                    return;
                }

                // Battle target selection - needs target HP/status
                if (BattleTargetState.ShouldSuppress())
                {
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

                // Extras menus - bestiary, music player, gallery have their own patches
                if (BestiaryStateTracker.IsInBestiary) return;
                if (MusicPlayerStateTracker.IsInMusicPlayer) return;
                if (GalleryStateTracker.IsInGallery) return;

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
            MenuStateRegistry.ResetAll();
            ShopMenuTracker.ClearState();
            BattlePausePatches.Reset();
        }

        /// <summary>
        /// Clears all menu states except the specified one. Called when a menu activates.
        /// </summary>
        // Maps caller-provided exceptMenu strings to MenuStateRegistry keys
        private static readonly Dictionary<string, string> menuKeyMap = new Dictionary<string, string>
        {
            { "Config", MenuStateRegistry.CONFIG_MENU },
            { "BattleCommand", MenuStateRegistry.BATTLE_COMMAND },
            { "BattleTarget", MenuStateRegistry.BATTLE_TARGET },
            { "BattleItem", MenuStateRegistry.BATTLE_ITEM },
            { "BattleMagic", MenuStateRegistry.BATTLE_MAGIC },
            { "Item", MenuStateRegistry.ITEM_MENU },
            { "Equip", MenuStateRegistry.EQUIP_MENU },
            { "Magic", MenuStateRegistry.MAGIC_MENU },
            { "Status", MenuStateRegistry.STATUS_MENU },
            { "StatusDetails", MenuStateRegistry.STATUS_DETAILS },
        };

        public static void ClearOtherMenuStates(string exceptMenu)
        {
            // Reset all registry states except the one being activated
            // AND keep MAIN_MENU active — it's the parent container for all sub-menus.
            // Clearing it creates a gap where no menu is active (audio/controls break).
            if (menuKeyMap.TryGetValue(exceptMenu, out var exceptKey))
            {
                var keys = new List<string>(MenuStateRegistry.GetActiveStates());
                foreach (var key in keys)
                {
                    if (key != exceptKey && key != MenuStateRegistry.MAIN_MENU)
                        MenuStateRegistry.SetActive(key, false);
                }
            }
            else
                MenuStateRegistry.ResetAll();

            // Shop is not in the registry — clear separately
            if (exceptMenu != "Shop") ShopMenuTracker.ClearState();
        }
    }
}
