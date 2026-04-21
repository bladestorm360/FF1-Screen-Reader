using System;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Field;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Handles waypoint operations: cycling, pathfinding, add/rename/remove.
    /// </summary>
    internal class WaypointController
    {
        private readonly EntityNavigationManager navManager;
        private readonly WaypointManager waypointManager;
        private readonly WaypointNavigator waypointNavigator;

        public WaypointController(EntityNavigationManager navManager, WaypointManager manager, WaypointNavigator navigator)
        {
            this.navManager = navManager;
            this.waypointManager = manager;
            this.waypointNavigator = navigator;
        }

        public void CycleNext()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFI_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
                return;
            }

            waypointNavigator.CycleNext();
            NavigationTargetTracker.MarkWaypoint();
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CyclePrevious()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFI_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
                return;
            }

            waypointNavigator.CyclePrevious();
            NavigationTargetTracker.MarkWaypoint();
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CycleNextCategory()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            waypointNavigator.CycleNextCategory(mapId);
            if (waypointNavigator.Count > 0)
                NavigationTargetTracker.MarkWaypoint();
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void CyclePreviousCategory()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            waypointNavigator.CyclePreviousCategory(mapId);
            if (waypointNavigator.Count > 0)
                NavigationTargetTracker.MarkWaypoint();
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void PathfindToCurrentWaypoint()
        {
            if (!navManager.EnsureFieldContext()) return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFI_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            NavigationTargetTracker.MarkWaypoint();

            // Beacon mode: just restart the beacon to re-ping toward this waypoint.
            if (FFI_ScreenReaderMod.AudioBeaconsEnabled)
            {
                FFI_ScreenReaderMod.Instance?.RestartBeacon();
                return;
            }

            // Turn-by-turn mode: A* path + descriptive route (matches entity pathfinding).
            var context = new FilterContext();
            if (context.PlayerPosition == Vector3.zero)
            {
                FFI_ScreenReaderMod.SpeakText(T("Cannot determine directions"));
                return;
            }

            var pathInfo = FieldNavigationHelper.FindPathTo(
                context.PlayerPosition, waypoint.Position, context.MapHandle, context.FieldPlayer);

            string announcement = (pathInfo.Success && !string.IsNullOrEmpty(pathInfo.Description))
                ? pathInfo.Description
                : T("No path");

            FFI_ScreenReaderMod.SpeakText(announcement);
        }

        public void AddNewWaypointWithNaming()
        {
            if (!navManager.EnsureFieldContext()) return;

            if (waypointNavigator.CurrentCategory == WaypointCategory.All)
            {
                FFI_ScreenReaderMod.SpeakText(T("Please select a category"));
                return;
            }

            var player = FFI_ScreenReaderMod.GetFieldPlayer();
            if (player == null)
            {
                FFI_ScreenReaderMod.SpeakText(T("Cannot get player position"));
                return;
            }

            Vector3 playerPos = player.transform.localPosition;
            string mapId = GetMapIdString();
            WaypointCategory category = waypointNavigator.CurrentCategory;

            TextInputWindow.Open(
                T("Enter waypoint name"),
                "",
                onConfirm: (name) =>
                {
                    waypointManager.AddWaypoint(name, playerPos, mapId, category);
                    waypointNavigator.RefreshList(mapId);
                    FFI_ScreenReaderMod.SpeakText(string.Format(T("Waypoint added: {0}"), name));
                },
                onCancel: () => { }
            );
        }

        public void RenameCurrentWaypoint()
        {
            if (!navManager.EnsureFieldContext()) return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFI_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            string mapId = GetMapIdString();

            TextInputWindow.Open(
                T("Enter new waypoint name"),
                waypoint.Name,
                onConfirm: (newName) =>
                {
                    if (waypointManager.RenameWaypoint(waypoint.WaypointId, newName))
                    {
                        waypointNavigator.RefreshList(mapId);
                        FFI_ScreenReaderMod.SpeakText(string.Format(T("Waypoint renamed to: {0}"), newName));
                    }
                    else
                    {
                        FFI_ScreenReaderMod.SpeakText(T("Failed to rename waypoint"));
                    }
                },
                onCancel: () => { }
            );
        }

        public void RemoveCurrentWaypoint()
        {
            if (!navManager.EnsureFieldContext()) return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFI_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            string mapId = GetMapIdString();
            string waypointName = waypoint.Name;

            ConfirmationDialog.Open(
                string.Format(T("Delete waypoint {0}?"), waypointName),
                onYes: () =>
                {
                    if (waypointManager.RemoveWaypoint(waypoint.WaypointId))
                    {
                        waypointNavigator.RefreshList(mapId);
                        waypointNavigator.ClearSelection();
                        FFI_ScreenReaderMod.SpeakText(string.Format(T("Waypoint deleted: {0}"), waypointName));
                    }
                    else
                    {
                        FFI_ScreenReaderMod.SpeakText(T("Failed to delete waypoint"));
                    }
                },
                onNo: () => { }
            );
        }

        public void ClearAllWaypointsForMap()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            int count = waypointManager.GetWaypointCountForMap(mapId);

            if (count == 0)
            {
                FFI_ScreenReaderMod.SpeakText(T("No waypoints to clear on this map"));
                return;
            }

            string plural = count == 1 ? T("waypoint") : T("waypoints");

            ConfirmationDialog.Open(
                string.Format(T("Clear all {0} {1} from this map?"), count, plural),
                onYes: () =>
                {
                    int cleared = waypointManager.ClearMapWaypoints(mapId);
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();
                    FFI_ScreenReaderMod.SpeakText(string.Format(T("Cleared {0} {1}"), cleared, plural));
                },
                onNo: () => { }
            );
        }

        private string GetMapIdString()
        {
            return FFI_ScreenReaderMod.GetCurrentMapId().ToString();
        }
    }
}
