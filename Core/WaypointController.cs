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
                FFI_ScreenReaderMod.SpeakText(T("No waypoint selected"));
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
                FFI_ScreenReaderMod.SpeakText(T("Cannot get player position"));
                return;
            }

            Vector3 playerPos = player.transform.localPosition;
            string mapId = GetMapIdString();

            TextInputWindow.Open(
                T("Enter waypoint name"),
                "",
                onConfirm: (name) =>
                {
                    waypointManager.AddWaypoint(name, playerPos, mapId);
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
