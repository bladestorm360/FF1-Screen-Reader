using System;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Field;

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
                FFI_ScreenReaderMod.SpeakText("No waypoints on this map");
                return;
            }

            waypointNavigator.CycleNext();
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CyclePrevious()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFI_ScreenReaderMod.SpeakText("No waypoints on this map");
                return;
            }

            waypointNavigator.CyclePrevious();
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CycleNextCategory()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            waypointNavigator.CycleNextCategory(mapId);
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void CyclePreviousCategory()
        {
            if (!navManager.EnsureFieldContext()) return;

            string mapId = GetMapIdString();
            waypointNavigator.CyclePreviousCategory(mapId);
            FFI_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void PathfindToCurrentWaypoint()
        {
            if (!navManager.EnsureFieldContext()) return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFI_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            var player = FFI_ScreenReaderMod.GetFieldPlayer();
            if (player == null)
            {
                FFI_ScreenReaderMod.SpeakText(waypoint.Name);
                return;
            }

            Vector3 playerPos = player.transform.localPosition;
            string pathDescription = FieldNavigationHelper.GetSimplePathDescription(playerPos, waypoint.Position);
            FFI_ScreenReaderMod.SpeakText(pathDescription);
        }

        public void AddNewWaypointWithNaming()
        {
            if (!navManager.EnsureFieldContext()) return;

            var player = FFI_ScreenReaderMod.GetFieldPlayer();
            if (player == null)
            {
                FFI_ScreenReaderMod.SpeakText("Cannot get player position");
                return;
            }

            Vector3 playerPos = player.transform.localPosition;
            string mapId = GetMapIdString();

            TextInputWindow.Open(
                "Enter waypoint name",
                "",
                onConfirm: (name) =>
                {
                    waypointManager.AddWaypoint(name, playerPos, mapId);
                    waypointNavigator.RefreshList(mapId);
                    FFI_ScreenReaderMod.SpeakText($"Waypoint added: {name}");
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
                FFI_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            string mapId = GetMapIdString();

            TextInputWindow.Open(
                "Enter new waypoint name",
                waypoint.Name,
                onConfirm: (newName) =>
                {
                    if (waypointManager.RenameWaypoint(waypoint.WaypointId, newName))
                    {
                        waypointNavigator.RefreshList(mapId);
                        FFI_ScreenReaderMod.SpeakText($"Waypoint renamed to: {newName}");
                    }
                    else
                    {
                        FFI_ScreenReaderMod.SpeakText("Failed to rename waypoint");
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
                FFI_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            string mapId = GetMapIdString();
            string waypointName = waypoint.Name;

            ConfirmationDialog.Open(
                $"Delete waypoint {waypointName}?",
                onYes: () =>
                {
                    if (waypointManager.RemoveWaypoint(waypoint.WaypointId))
                    {
                        waypointNavigator.RefreshList(mapId);
                        waypointNavigator.ClearSelection();
                        FFI_ScreenReaderMod.SpeakText($"Waypoint deleted: {waypointName}");
                    }
                    else
                    {
                        FFI_ScreenReaderMod.SpeakText("Failed to delete waypoint");
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
                FFI_ScreenReaderMod.SpeakText("No waypoints to clear on this map");
                return;
            }

            string plural = count == 1 ? "waypoint" : "waypoints";

            ConfirmationDialog.Open(
                $"Clear all {count} {plural} from this map?",
                onYes: () =>
                {
                    int cleared = waypointManager.ClearMapWaypoints(mapId);
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();
                    FFI_ScreenReaderMod.SpeakText($"Cleared {cleared} {plural}");
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
