namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects layer transition entities (ToUpper/ToBottom).
    /// Created as EventEntity with "ToLayer" type for filtering via ToLayerFilter.
    /// Priority 110.
    /// </summary>
    public class ToLayerDetector : IEntityDetector
    {
        public int Priority => 110;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            string lower = context.GameObjectNameLower;
            if (lower == "toupper" || lower == "tobottom")
            {
                string entityName = EntityDetectionHelpers.GetEntityNameFromProperty(context.FieldEntity);
                if (string.IsNullOrEmpty(entityName))
                    entityName = context.GameObjectName;
                return DetectionResult.Detected(
                    new EventEntity(context.FieldEntity, context.Position, entityName, "ToLayer"));
            }
            return DetectionResult.NotHandled;
        }
    }
}
