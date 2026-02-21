namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Filters out the player entity from the navigation list.
    /// Priority 0: must run first before any detection.
    /// </summary>
    public class PlayerFilter : IEntityDetector
    {
        public int Priority => 0;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            if (context.TypeName.Contains("FieldPlayer") || context.GameObjectNameLower.Contains("player"))
                return DetectionResult.Skip;
            return DetectionResult.NotHandled;
        }
    }
}
