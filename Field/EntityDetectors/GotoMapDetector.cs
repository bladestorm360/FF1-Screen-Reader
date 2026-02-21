using Il2CppLast.Map;
using GotoMapEventEntity = Il2CppLast.Entity.Field.GotoMapEventEntity;
using PropertyEntity = Il2CppLast.Map.PropertyEntity;
using PropertyTelepoPoint = Il2CppLast.Map.PropertyTelepoPoint;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects map exits (GotoMapEventEntity, ObjectType 3) and same-map warp tiles (PropertyTelepoPoint).
    /// Priority 50: runs after all exclusion filters.
    /// </summary>
    public class GotoMapDetector : IEntityDetector
    {
        public int Priority => 50;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            // Try casting to GotoMapEventEntity first (most reliable)
            var gotoMapEvent = context.FieldEntity.TryCast<GotoMapEventEntity>();
            if (gotoMapEvent != null)
                return DetectionResult.Detected(CreateMapExit(context));

            // Fallback: GotoMap in game object name OR ObjectType 3
            if (context.GameObjectNameLower.Contains("gotomap") || context.ObjectType == 3)
                return DetectionResult.Detected(CreateMapExit(context));

            // PropertyTelepoPoint = same-map warp tiles (e.g., Citadel of Trials puzzle)
            try
            {
                if (context.PropertyObject is PropertyEntity prop)
                {
                    var telepoProperty = prop.TryCast<PropertyTelepoPoint>();
                    if (telepoProperty != null)
                    {
                        string name = EntityDetectionHelpers.GetEntityNameFromProperty(context.FieldEntity);
                        if (string.IsNullOrEmpty(name))
                            name = "Warp Tile";
                        return DetectionResult.Detected(
                            new EventEntity(context.FieldEntity, context.Position, name, "Warp Tile"));
                    }
                }
            }
            catch { } // IL2CPP cast may fail on invalid property

            return DetectionResult.NotHandled;
        }

        private static NavigableEntity CreateMapExit(EntityDetectionContext context)
        {
            int destMapId = EntityDetectionHelpers.GetGotoMapDestinationId(context.FieldEntity);
            string destName = EntityDetectionHelpers.ResolveMapName(destMapId);
            string exitName = !string.IsNullOrEmpty(destName) ? $"Exit to {destName}" : "Exit";
            return new MapExitEntity(context.FieldEntity, context.Position, exitName, destMapId, destName);
        }
    }
}
