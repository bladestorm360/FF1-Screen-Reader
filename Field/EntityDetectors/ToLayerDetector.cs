using Il2CppLast.Entity.Field;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects layer transition entities (SwitchLayerEventEntity).
    /// Created as EventEntity with "ToLayer" type for filtering via ToLayerFilter.
    /// Priority 110.
    /// </summary>
    public class ToLayerDetector : IEntityDetector
    {
        public int Priority => 110;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            bool isLayerEntity = false;
            try { isLayerEntity = context.FieldEntity.TryCast<SwitchLayerEventEntity>() != null; }
            catch { } // IL2CPP cast can throw on destroyed entities

            if (!isLayerEntity && context.ObjectType == 4) // MapConstants.ObjectType.ToLayer
                isLayerEntity = true;

            if (!isLayerEntity)
            {
                string lower = context.GameObjectNameLower;
                if (lower == "toupper" || lower == "tobottom" || lower.StartsWith("tolayer"))
                    isLayerEntity = true;
            }

            if (!isLayerEntity)
                return DetectionResult.NotHandled;

            string entityName = EntityDetectionHelpers.GetEntityNameFromProperty(context.FieldEntity);
            if (string.IsNullOrEmpty(entityName))
                entityName = context.GameObjectName;

            return DetectionResult.Detected(
                new EventEntity(context.FieldEntity, context.Position, entityName, "ToLayer"));
        }
    }
}
