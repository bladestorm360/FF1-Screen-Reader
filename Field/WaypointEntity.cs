using System;
using UnityEngine;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Field
{
    /// <summary>
    /// Categories for waypoints to allow filtering.
    /// </summary>
    internal enum WaypointCategory
    {
        All = 0,
        Landmarks = 1,
        Docks = 2,
        Miscellaneous = 3
    }

    /// <summary>
    /// Represents a user-defined waypoint for navigation.
    /// Standalone class — not part of the entity navigation system.
    /// </summary>
    internal class WaypointEntity
    {
        private readonly string waypointId;
        private readonly string waypointName;
        private readonly Vector3 position;
        private readonly WaypointCategory waypointCategory;
        private readonly string mapId;

        public string WaypointId => waypointId;
        public string Name => waypointName;
        public WaypointCategory WaypointCategoryType => waypointCategory;
        public string MapId => mapId;
        public Vector3 Position => position;

        public WaypointEntity(string id, string name, Vector3 pos, string mapId, WaypointCategory category)
        {
            this.waypointId = id;
            this.waypointName = name;
            this.position = pos;
            this.mapId = mapId;
            this.waypointCategory = category;
        }

        public static string GetCategoryDisplayName(WaypointCategory category)
        {
            switch (category)
            {
                case WaypointCategory.Landmarks: return T("Landmark");
                case WaypointCategory.Docks: return T("Dock");
                case WaypointCategory.Miscellaneous: return T("Waypoint");
                default: return T("Waypoint");
            }
        }

        public static string[] GetCategoryNames()
        {
            return new string[] { T("All"), T("Landmarks"), T("Docks"), T("Miscellaneous") };
        }

        /// <summary>
        /// Formats this waypoint for screen reader announcement.
        /// Uses FieldNavigationHelper for direction/distance.
        /// </summary>
        public string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = FieldNavigationHelper.GetDirection(playerPos, Position);
            float steps = FieldNavigationHelper.DistanceToSteps(distance);
            string stepLabel = Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";
            string categoryName = GetCategoryDisplayName(waypointCategory);
            return $"{waypointName} ({categoryName}) ({steps:F1} {stepLabel} {direction})";
        }
    }
}
