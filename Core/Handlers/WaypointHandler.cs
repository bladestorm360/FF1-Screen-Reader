namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Waypoint action methods for key binding registration.
    /// Delegates to the WaypointController instance.
    /// </summary>
    internal static class WaypointHandler
    {
        private static WaypointController controller;

        internal static void Initialize(WaypointController ctrl)
        {
            controller = ctrl;
        }

        internal static void CyclePrevious() => controller?.CyclePrevious();
        internal static void CycleNext() => controller?.CycleNext();
        internal static void CyclePreviousCategory() => controller?.CyclePreviousCategory();
        internal static void CycleNextCategory() => controller?.CycleNextCategory();
        internal static void PathfindToCurrentWaypoint() => controller?.PathfindToCurrentWaypoint();
        internal static void AddNewWaypointWithNaming() => controller?.AddNewWaypointWithNaming();
        internal static void RenameCurrentWaypoint() => controller?.RenameCurrentWaypoint();
        internal static void RemoveCurrentWaypoint() => controller?.RemoveCurrentWaypoint();
        internal static void ClearAllWaypointsForMap() => controller?.ClearAllWaypointsForMap();
    }
}
