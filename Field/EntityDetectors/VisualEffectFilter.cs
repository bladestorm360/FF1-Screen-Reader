namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Filters out party members, visual effects, and inactive entities.
    /// Priority 20: runs after VehicleDetector (vehicles also use ResidentCharaEntity).
    /// </summary>
    public class VisualEffectFilter : IEntityDetector
    {
        public int Priority => 20;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            string lower = context.GameObjectNameLower;

            // Skip party member entities (following characters)
            if (lower.Contains("residentchara") || lower.Contains("resident"))
                return DetectionResult.Skip;

            // Skip visual effects and non-interactive elements (unless EventTrigger)
            if (!context.IsEventTrigger)
            {
                if (lower.Contains("fieldeffect") || lower.Contains("scrolldummy") ||
                    lower.Contains("scroll") || lower.Contains("tileanim") ||
                    lower.Contains("pointin") || lower.Contains("opentrigger") ||
                    (lower.Contains("effect") && !lower.Contains("object")))
                    return DetectionResult.Skip;
            }

            // Skip inactive objects
            if (!context.IsActive)
                return DetectionResult.Skip;

            return DetectionResult.NotHandled;
        }
    }
}
