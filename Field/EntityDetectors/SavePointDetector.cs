namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects save points by name or type.
    /// Priority 80.
    /// </summary>
    public class SavePointDetector : IEntityDetector
    {
        public int Priority => 80;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            if (context.GameObjectNameLower.Contains("save") || context.TypeName.Contains("Save"))
                return DetectionResult.Detected(
                    new SavePointEntity(context.FieldEntity, context.Position, "Save Point"));
            return DetectionResult.NotHandled;
        }
    }
}
