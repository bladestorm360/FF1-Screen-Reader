using Il2CppLast.Entity.Field;
using FieldMapObjectDefault = Il2CppLast.Entity.Field.FieldMapObjectDefault;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects FieldMapObjectDefault and generic IInteractiveEntity instances.
    /// Last in the chain - catches anything interactive not handled by earlier detectors.
    /// Priority 130.
    /// </summary>
    public class InteractiveEntityDetector : IEntityDetector
    {
        public int Priority => 130;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            // FieldMapObjectDefault - generic interactive objects (buildings, misc objects)
            var mapObjectDefault = context.FieldEntity.TryCast<FieldMapObjectDefault>();
            if (mapObjectDefault != null)
            {
                if (!mapObjectDefault.CanAction)
                    return DetectionResult.Skip;

                string name = EntityDetectionHelpers.GetEntityNameFromProperty(context.FieldEntity);
                if (string.IsNullOrEmpty(name) || name == "GeneralEventObject")
                {
                    name = EntityDetectionHelpers.ClassifyByDialogue(context.FieldEntity)
                        ?? EntityDetectionHelpers.GetInteractiveObjectName(context.PropertyObject, context.GameObjectName);
                }
                return DetectionResult.Detected(
                    new EventEntity(context.FieldEntity, context.Position, name, "Interactive Object"));
            }

            // Generic IInteractiveEntity catch-all
            var interactiveEntity = context.FieldEntity.TryCast<IInteractiveEntity>();
            if (interactiveEntity != null)
            {
                // ObjectType 0 + PointIn = entry points (not useful to navigate to)
                if (context.ObjectType == 0 && context.GameObjectNameLower.Contains("pointin"))
                    return DetectionResult.Skip;

                string name = EntityDetectionHelpers.GetEntityNameFromProperty(context.FieldEntity);
                if (string.IsNullOrEmpty(name))
                    name = EntityDetectionHelpers.CleanObjectName(context.GameObjectName, "Object");
                return DetectionResult.Detected(
                    new EventEntity(context.FieldEntity, context.Position, name, "Interactive"));
            }

            return DetectionResult.NotHandled;
        }
    }
}
