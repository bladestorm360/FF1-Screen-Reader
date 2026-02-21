namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects doors, stairs, ladders, and entrances as secondary map exits.
    /// Priority 100.
    /// </summary>
    public class DoorStairsDetector : IEntityDetector
    {
        public int Priority => 100;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            string lower = context.GameObjectNameLower;
            if (lower.Contains("door") || lower.Contains("stairs") ||
                lower.Contains("ladder") || lower.Contains("entrance"))
            {
                int destMapId = EntityDetectionHelpers.GetGotoMapDestinationId(context.FieldEntity);
                string destName = EntityDetectionHelpers.ResolveMapName(destMapId);
                return DetectionResult.Detected(
                    new MapExitEntity(context.FieldEntity, context.Position, "Exit", destMapId, destName));
            }
            return DetectionResult.NotHandled;
        }
    }
}
