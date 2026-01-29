using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for the job/class selection menu in FF1's New Game screen.
    /// Announces the job name and description when navigating between classes
    /// (Fighter, Thief, Black Mage, White Mage, Red Mage, Monk).
    /// </summary>
    public static class JobSelectionPatches
    {

        /// <summary>
        /// Flag indicating job selection is currently active and handling cursor events.
        /// Used to prevent generic cursor navigation from also reading.
        /// </summary>
        public static bool IsHandlingCursor { get; private set; } = false;

        /// <summary>
        /// Applies job selection patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("Applying job selection patches...");

                // Find JobContentListController type (KeyInput version) - this handles the job selection list
                Type listControllerType = FindType("Il2CppLast.UI.KeyInput.JobContentListController");
                if (listControllerType == null)
                {
                    MelonLogger.Warning("JobContentListController (KeyInput) not found - trying Touch version");
                    listControllerType = FindType("Il2CppLast.UI.Touch.JobContentListController");
                }

                if (listControllerType == null)
                {
                    MelonLogger.Warning("JobContentListController type not found - job selection patches disabled");
                    return;
                }

                MelonLogger.Msg($"Found JobContentListController: {listControllerType.FullName}");

                // Patch SelectContent - called when cursor moves to a new item
                var selectMethod = AccessTools.Method(listControllerType, "SelectContent");
                if (selectMethod != null)
                {
                    var postfix = typeof(JobSelectionPatches).GetMethod("SelectContent_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(selectMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched JobContentListController.SelectContent");
                }
                else
                {
                    MelonLogger.Warning("SelectContent method not found");
                }

                MelonLogger.Msg("Job selection patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying job selection patches: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Postfix for JobContentListController.SelectContent - called when cursor moves.
        /// Uses Component hierarchy to find job names since IL2CPP private fields are inaccessible.
        /// </summary>
        public static void SelectContent_Postfix(object __instance, object targetCursor)
        {
            // Set flag to prevent generic cursor navigation from also reading
            IsHandlingCursor = true;

            try
            {
                if (targetCursor == null) return;

                // Get current index from cursor
                var indexProp = AccessTools.Property(targetCursor.GetType(), "Index");
                if (indexProp == null)
                {
                    MelonLogger.Msg("[JobSelection] Index property not found on cursor");
                    return;
                }

                int currentIndex = (int)indexProp.GetValue(targetCursor);
                MelonLogger.Msg($"[JobSelection] SelectContent index: {currentIndex}");

                // Use central deduplicator - skip if same index
                if (!AnnouncementDeduplicator.ShouldAnnounce("JobSelect.Index", currentIndex))
                    return;

                // Cast to Component to access Unity hierarchy
                var controllerComponent = __instance as Component;
                if (controllerComponent == null)
                {
                    MelonLogger.Msg("[JobSelection] Could not cast to Component");
                    return;
                }

                // Get job name immediately (this doesn't need delay)
                string jobName = GetJobNameViaHierarchy(controllerComponent, currentIndex);

                if (string.IsNullOrEmpty(jobName))
                {
                    MelonLogger.Msg("[JobSelection] Could not get job name");
                    return;
                }

                // Use central deduplicator - skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce("JobSelect.Name", jobName))
                    return;

                // Use coroutine to wait one frame before reading description (UI needs to update)
                CoroutineManager.StartManaged(AnnounceJobWithDelayedDescription(controllerComponent, jobName));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that announces job name immediately, then waits a frame and announces description.
        /// </summary>
        private static IEnumerator AnnounceJobWithDelayedDescription(Component controller, string jobName)
        {
            // Wait one frame for UI to update description text
            yield return null;

            try
            {
                string description = GetDescriptionViaHierarchy(controller);

                string announcement;
                if (!string.IsNullOrEmpty(description))
                {
                    announcement = $"{jobName}. {description}";
                }
                else
                {
                    announcement = jobName;
                }

                MelonLogger.Msg($"[Job Selection] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in delayed job announcement: {ex.Message}");
                // Still announce the job name at least
                FFI_ScreenReaderMod.SpeakText(jobName);
            }
        }

        /// <summary>
        /// Gets the job name by traversing the Unity component hierarchy.
        /// Finds JobContentView components and extracts the JobNameText.
        /// </summary>
        private static string GetJobNameViaHierarchy(Component listController, int index)
        {
            try
            {
                // Find all JobContentView components (these contain the job name text)
                // First, get all child transforms and look for JobContentView or Text components
                var transform = listController.transform;

                // Look for a child named "view" or similar that contains the job list
                // Try to find JobContentController components first
                var allChildren = listController.GetComponentsInChildren<Component>(true);

                // Log component types for debugging (only once)
                LogComponentTypes(allChildren);

                // Strategy 1: Find all Text components and look for job names
                var textComponents = listController.GetComponentsInChildren<Text>(true);
                MelonLogger.Msg($"[JobSelection] Found {textComponents.Length} Text components");

                // FF1 job names
                string[] jobNames = { "Warrior", "Thief", "Monk", "Red Mage", "White Mage", "Black Mage",
                                     "Knight", "Ninja", "Master", "Red Wizard", "White Wizard", "Black Wizard" };

                // Find all text that matches job names
                var jobTexts = new System.Collections.Generic.List<Text>();
                foreach (var text in textComponents)
                {
                    if (text == null || string.IsNullOrWhiteSpace(text.text)) continue;

                    string textValue = text.text.Trim();
                    foreach (var jobName in jobNames)
                    {
                        if (textValue.Equals(jobName, StringComparison.OrdinalIgnoreCase))
                        {
                            jobTexts.Add(text);
                            MelonLogger.Msg($"[JobSelection] Found job text: '{textValue}' at {text.gameObject.name}");
                            break;
                        }
                    }
                }

                MelonLogger.Msg($"[JobSelection] Found {jobTexts.Count} job name texts, looking for index {index}");

                // Return the text at the given index
                if (index >= 0 && index < jobTexts.Count)
                {
                    return jobTexts[index].text.Trim();
                }

                // Strategy 2: Try to find JobContentView by type name
                foreach (var comp in allChildren)
                {
                    if (comp == null) continue;
                    string typeName = comp.GetType().Name;

                    if (typeName.Contains("JobContentView"))
                    {
                        // Try to get JobNameText property
                        var jobNameTextProp = AccessTools.Property(comp.GetType(), "JobNameText");
                        if (jobNameTextProp != null)
                        {
                            var jobNameText = jobNameTextProp.GetValue(comp) as Text;
                            if (jobNameText != null && !string.IsNullOrWhiteSpace(jobNameText.text))
                            {
                                MelonLogger.Msg($"[JobSelection] Found JobNameText via property: '{jobNameText.text}'");
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in GetJobNameViaHierarchy: {ex.Message}");
                return null;
            }
        }

        private static bool typesLogged = false;

        /// <summary>
        /// Logs component types for debugging (only once).
        /// </summary>
        private static void LogComponentTypes(Component[] components)
        {
            if (typesLogged) return;
            typesLogged = true;

            MelonLogger.Msg($"[JobSelection] Component types in hierarchy ({components.Length} total):");
            var typeNames = new System.Collections.Generic.HashSet<string>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                if (typeNames.Add(typeName))
                {
                    MelonLogger.Msg($"  - {typeName}");
                }
            }
        }

        /// <summary>
        /// Gets the description text from the JobContentListController.
        /// Searches all Text components and finds the description (longer text that's not a job name).
        /// </summary>
        private static string GetDescriptionViaHierarchy(Component listController)
        {
            try
            {
                // FF1 job names to exclude
                string[] jobNames = { "Warrior", "Thief", "Monk", "Red Mage", "White Mage", "Black Mage",
                                     "Knight", "Ninja", "Master", "Red Wizard", "White Wizard", "Black Wizard" };

                // Get all Text components
                var textComponents = listController.GetComponentsInChildren<Text>(true);

                // Find the description: longest text that's not a job name
                string bestDescription = null;
                int bestLength = 0;

                foreach (var text in textComponents)
                {
                    if (text == null || string.IsNullOrWhiteSpace(text.text)) continue;

                    string textValue = text.text.Trim();

                    // Skip job names
                    bool isJobName = false;
                    foreach (var jobName in jobNames)
                    {
                        if (textValue.Equals(jobName, StringComparison.OrdinalIgnoreCase))
                        {
                            isJobName = true;
                            break;
                        }
                    }
                    if (isJobName) continue;

                    // Skip very short text (labels, etc.)
                    if (textValue.Length < 10) continue;

                    // Prefer longer text (descriptions are sentences)
                    if (textValue.Length > bestLength)
                    {
                        bestLength = textValue.Length;
                        bestDescription = textValue;
                    }
                }

                if (!string.IsNullOrEmpty(bestDescription))
                {
                    MelonLogger.Msg($"[JobSelection] Found description: '{bestDescription}'");
                    return bestDescription;
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting description: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resets state when leaving job selection.
        /// </summary>
        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset("JobSelect.Index", "JobSelect.Name");
            typesLogged = false;
            IsHandlingCursor = false;
        }

        /// <summary>
        /// Clears the handling flag. Called after a delay to allow coroutine reads to be skipped.
        /// </summary>
        public static void ClearHandlingFlag()
        {
            IsHandlingCursor = false;
        }
    }
}
