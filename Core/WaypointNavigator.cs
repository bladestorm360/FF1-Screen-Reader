using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MelonLoader;
using FFI_ScreenReader.Field;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Handles waypoint cycling and category filtering.
    /// Separate from EntityNavigator since waypoints have their own category system.
    /// </summary>
    internal class WaypointNavigator
    {
        private readonly WaypointManager waypointManager;
        private List<WaypointEntity> currentList = new List<WaypointEntity>();
        private int currentIndex = -1;
        private WaypointCategory currentCategory = WaypointCategory.All;

        private static readonly string[] CategoryNames = WaypointEntity.GetCategoryNames();
        private static readonly int CategoryCount = Enum.GetValues(typeof(WaypointCategory)).Length;

        public WaypointEntity SelectedWaypoint =>
            (currentIndex >= 0 && currentIndex < currentList.Count)
                ? currentList[currentIndex] : null;

        public int Count => currentList.Count;

        public WaypointNavigator(WaypointManager manager)
        {
            waypointManager = manager;
        }

        public void RefreshList(string mapId)
        {
            try
            {
                string previousSelectionId = SelectedWaypoint?.WaypointId;

                if (currentCategory == WaypointCategory.All)
                    currentList = waypointManager.GetWaypointsForMap(mapId);
                else
                    currentList = waypointManager.GetWaypointsForCategory(mapId, currentCategory);

                SortByDistancePreservingSelection(previousSelectionId);

                if (currentList.Count == 0)
                    currentIndex = -1;
                else if (currentIndex >= currentList.Count)
                    currentIndex = currentList.Count - 1;
                else if (currentIndex < 0)
                    currentIndex = 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Waypoints] Error refreshing list: {ex.Message}");
                currentList = new List<WaypointEntity>();
                currentIndex = -1;
            }
        }

        public WaypointEntity CycleNext()
        {
            if (currentList.Count == 0) return null;
            SortByDistance();
            currentIndex = (currentIndex + 1) % currentList.Count;
            return SelectedWaypoint;
        }

        public WaypointEntity CyclePrevious()
        {
            if (currentList.Count == 0) return null;
            SortByDistance();
            currentIndex = (currentIndex - 1 + currentList.Count) % currentList.Count;
            return SelectedWaypoint;
        }

        public string CycleNextCategory(string mapId)
        {
            int nextVal = ((int)currentCategory + 1) % CategoryCount;
            currentCategory = (WaypointCategory)nextVal;
            RefreshList(mapId);
            return CategoryNames[(int)currentCategory];
        }

        public string CyclePreviousCategory(string mapId)
        {
            int prevVal = ((int)currentCategory - 1 + CategoryCount) % CategoryCount;
            currentCategory = (WaypointCategory)prevVal;
            RefreshList(mapId);
            return CategoryNames[(int)currentCategory];
        }

        public string FormatCurrentWaypoint()
        {
            var waypoint = SelectedWaypoint;
            if (waypoint == null) return "No waypoints";

            Vector3 playerPos = GetPlayerPosition();
            string description = waypoint.FormatDescription(playerPos);

            if (currentList.Count > 1)
                description += $", {currentIndex + 1} of {currentList.Count}";

            return description;
        }

        public string GetCategoryAnnouncement()
        {
            string categoryName = CategoryNames[(int)currentCategory];
            int count = currentList.Count;
            string plural = count == 1 ? "waypoint" : "waypoints";
            return $"{categoryName}: {count} {plural}";
        }

        public void ClearSelection()
        {
            currentIndex = currentList.Count > 0 ? 0 : -1;
        }

        private void SortByDistance()
        {
            if (currentList.Count <= 1) return;

            var currentSelection = SelectedWaypoint;
            Vector3 playerPos = GetPlayerPosition();
            currentList = currentList.OrderBy(w => Vector3.Distance(playerPos, w.Position)).ToList();

            if (currentSelection != null)
            {
                int newIndex = currentList.IndexOf(currentSelection);
                if (newIndex >= 0) currentIndex = newIndex;
            }
        }

        private void SortByDistancePreservingSelection(string waypointIdToPreserve)
        {
            if (currentList.Count <= 1) return;

            Vector3 playerPos = GetPlayerPosition();
            currentList = currentList.OrderBy(w => Vector3.Distance(playerPos, w.Position)).ToList();

            if (!string.IsNullOrEmpty(waypointIdToPreserve))
            {
                int newIndex = currentList.FindIndex(w => w.WaypointId == waypointIdToPreserve);
                if (newIndex >= 0)
                {
                    currentIndex = newIndex;
                    return;
                }
            }
            currentIndex = 0;
        }

        private Vector3 GetPlayerPosition()
        {
            var player = FFI_ScreenReaderMod.GetFieldPlayer();
            if (player != null)
                return player.transform.localPosition;
            return Vector3.zero;
        }
    }
}
