using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects treasure chests via FieldTresureBox cast and name-based fallback.
    /// Priority 60.
    /// </summary>
    public class TreasureChestDetector : IEntityDetector
    {
        public int Priority => 60;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            // Try explicit cast to FieldTresureBox (note: game uses "Tresure" spelling)
            var treasureBox = context.FieldEntity.TryCast<FieldTresureBox>();
            if (treasureBox != null)
                return DetectionResult.Detected(CreateTreasure(context));

            // Fallback: name-based treasure detection
            string lower = context.GameObjectNameLower;
            if (lower.Contains("treasure") || lower.Contains("tresure") ||
                lower.Contains("chest") || context.TypeName.Contains("Treasure") ||
                context.TypeName.Contains("Tresure"))
                return DetectionResult.Detected(CreateTreasure(context));

            return DetectionResult.NotHandled;
        }

        private static NavigableEntity CreateTreasure(EntityDetectionContext context)
        {
            bool isOpened = EntityDetectionHelpers.CheckIfTreasureOpened(context.FieldEntity);
            string contents = EntityDetectionHelpers.GetTreasureContents(context.PropertyObject);
            string name = !string.IsNullOrEmpty(contents) ? contents : "Treasure Chest";
            return new TreasureChestEntity(context.FieldEntity, context.Position, name, isOpened);
        }
    }
}
