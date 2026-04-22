namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Catch-all for EventTriggerEntity instances not handled by earlier detectors.
    /// Priority 120.
    /// </summary>
    public class EventTriggerDetector : IEntityDetector
    {
        public int Priority => 120;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            if (context.IsEventTrigger)
            {
                string entityName = EntityDetectionHelpers.GetEntityNameFromProperty(context.FieldEntity);
                if (string.IsNullOrEmpty(entityName) || entityName == "GeneralEventObject")
                {
                    entityName = EntityDetectionHelpers.ClassifyByDialogue(context.FieldEntity)
                        ?? EntityDetectionHelpers.CleanObjectName(context.GameObjectName, "Event Trigger");
                }
                return DetectionResult.Detected(
                    new EventEntity(context.FieldEntity, context.Position, entityName, "Event"));
            }
            return DetectionResult.NotHandled;
        }
    }
}
