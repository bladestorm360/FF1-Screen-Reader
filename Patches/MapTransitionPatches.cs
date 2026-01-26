using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Field;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Suppresses wall tones during map transitions by polling FadeManager state.
    /// Uses cached reflection to call IsFadeFinish() —
    /// no Harmony patches on FadeManager (avoids IL2CPP trampoline issues with Nullable params).
    /// Polled every 100ms from UpdateWallTones().
    /// </summary>
    public static class MapTransitionPatches
    {
        private static bool isInitialized = false;

        // Cached reflection members
        private static PropertyInfo instanceProperty;
        private static MethodInfo isFadeFinishMethod;

        // Map transition announcement tracking
        private static int lastAnnouncedMapId = -1;

        // Field states from SubSceneManagerMainGame.State enum
        private const int STATE_CHANGE_MAP = 1;
        private const int STATE_FIELD_READY = 2;
        private const int STATE_PLAYER = 3;

        /// <summary>
        /// True while the screen is fading (fade not finished).
        /// Checked by UpdateWallTones() to suppress tones during transitions.
        /// Polls FadeManager.IsFadeFinish() via cached reflection each time it's read.
        /// </summary>
        public static bool IsScreenFading
        {
            get
            {
                if (!isInitialized) return false;
                try
                {
                    object instance = instanceProperty.GetValue(null);
                    if (instance == null) return false;

                    bool isFadeFinish = (bool)isFadeFinishMethod.Invoke(instance, null);
                    return !isFadeFinish;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Initializes cached reflection for FadeManager polling.
        /// Harmony parameter kept for API compatibility but is not used.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isInitialized)
                return;

            Initialize();
            ApplyMapTransitionPatch(harmony);
        }

        private static void Initialize()
        {
            try
            {
                Type fadeManagerType = FindFadeManagerType();
                if (fadeManagerType == null)
                {
                    MelonLogger.Warning("[MapTransition] FadeManager type not found");
                    return;
                }

                MelonLogger.Msg($"[MapTransition] Found FadeManager: {fadeManagerType.FullName}");

                // Cache Instance property (inherited from SingletonMonoBehaviour<T>)
                instanceProperty = AccessTools.Property(fadeManagerType, "Instance");
                if (instanceProperty == null)
                {
                    // Fallback: search base type hierarchy with FlattenHierarchy
                    instanceProperty = fadeManagerType.BaseType?.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                }

                bool hasInstance = instanceProperty != null;
                MelonLogger.Msg($"[MapTransition] Instance property: {(hasInstance ? "found" : "NOT FOUND")}");

                if (!hasInstance)
                {
                    MelonLogger.Warning("[MapTransition] Cannot poll FadeManager without Instance property");
                    return;
                }

                // Cache IsFadeFinish method
                isFadeFinishMethod = AccessTools.Method(fadeManagerType, "IsFadeFinish");
                bool hasFadeFinish = isFadeFinishMethod != null;
                MelonLogger.Msg($"[MapTransition] IsFadeFinish method: {(hasFadeFinish ? "found" : "NOT FOUND")}");

                if (!hasFadeFinish)
                {
                    MelonLogger.Warning("[MapTransition] IsFadeFinish not found — fade detection disabled");
                    return;
                }

                isInitialized = true;

                // Log initial state
                try
                {
                    object instance = instanceProperty.GetValue(null);
                    bool initialState = instance != null && (bool)isFadeFinishMethod.Invoke(instance, null);
                    MelonLogger.Msg($"[MapTransition] Cached reflection initialized — IsFadeFinish={initialState}");
                }
                catch
                {
                    MelonLogger.Msg("[MapTransition] Cached reflection initialized — IsFadeFinish=(no instance yet)");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapTransition] Error initializing cached reflection: {ex.Message}");
            }
        }

        /// <summary>
        /// Find the FadeManager type via assembly scanning.
        /// The System.Fade namespace maps to Il2CppSystem.Fade in unhollowed assemblies.
        /// </summary>
        private static Type FindFadeManagerType()
        {
            string[] typeNames = new[]
            {
                "Il2CppSystem.Fade.FadeManager",
                "System.Fade.FadeManager"
            };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var name in typeNames)
                    {
                        var type = asm.GetType(name);
                        if (type != null)
                        {
                            MelonLogger.Msg($"[MapTransition] Found FadeManager in {asm.GetName().Name} as {name}");
                            return type;
                        }
                    }
                }
                catch { }
            }

            // Broader search: look for any type named FadeManager
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name == "FadeManager" && !type.IsNested)
                        {
                            MelonLogger.Msg($"[MapTransition] Found FadeManager via broad search: {type.FullName} in {asm.GetName().Name}");
                            return type;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Applies Harmony patch on SubSceneManagerMainGame.ChangeState(State)
        /// to detect actual game state transitions and announce map changes.
        /// Unlike set_CurrentMapId (which fires during map provisioning/loading),
        /// ChangeState only fires on real player-driven state transitions.
        /// </summary>
        private static void ApplyMapTransitionPatch(HarmonyLib.Harmony harmony)
        {
            try
            {
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerMainGame),
                    "ChangeState",
                    new Type[] { typeof(SubSceneManagerMainGame.State) }
                );

                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MapTransitionPatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MapTransition] Patched SubSceneManagerMainGame.ChangeState (event-driven map transitions)");
                }
                else
                {
                    MelonLogger.Warning("[MapTransition] Could not find SubSceneManagerMainGame.ChangeState — map transition announcements disabled");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapTransition] Error patching map transition: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SubSceneManagerMainGame.ChangeState.
        /// Only fires on actual game state transitions (field, battle, menu),
        /// never during map data loading/provisioning.
        /// </summary>
        public static void ChangeState_Postfix(SubSceneManagerMainGame.State state)
        {
            try
            {
                int stateValue = (int)state;

                // Only check map transitions on field-related states
                if (stateValue == STATE_CHANGE_MAP || stateValue == STATE_FIELD_READY || stateValue == STATE_PLAYER)
                {
                    CheckMapTransition();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapTransition] Error in ChangeState_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the current map ID from FieldMapProvisionInformation and announces
        /// map changes. Announces on every transition including load-game.
        /// </summary>
        private static void CheckMapTransition()
        {
            try
            {
                var fieldMapInfo = Il2CppLast.Map.FieldMapProvisionInformation.Instance;
                if (fieldMapInfo == null)
                    return;

                int currentMapId = fieldMapInfo.CurrentMapId;
                if (currentMapId <= 0)
                    return;

                // Same map — no announcement
                if (currentMapId == lastAnnouncedMapId)
                    return;

                lastAnnouncedMapId = currentMapId;

                // Resolve map name
                string mapName = MapNameResolver.GetCurrentMapName();
                if (string.IsNullOrEmpty(mapName) || mapName == "Unknown")
                {
                    MelonLogger.Msg($"[MapTransition] Map ID {currentMapId} could not be resolved to a name");
                    return;
                }

                string announcement = $"Entering {mapName}";

                // Record for dedup (suppresses bare name from FadeMessageManager.Play)
                LocationMessageTracker.SetLastMapTransition(announcement);

                MelonLogger.Msg($"[MapTransition] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapTransition] Error in CheckMapTransition: {ex.Message}");
            }
        }

        /// <summary>
        /// Full reset of map tracking state. Does NOT reset lastAnnouncedMapId
        /// since battle transitions change scenes without changing maps.
        /// </summary>
        public static void ResetMapTracking()
        {
            LocationMessageTracker.Reset();
        }
    }
}
