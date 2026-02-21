namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Result of a detector's attempt to classify a field entity.
    /// Three states: Skip (exclude entirely), NotHandled (pass to next), Detected (use this entity).
    /// </summary>
    public struct DetectionResult
    {
        private bool _skip;

        public bool ShouldSkip => _skip;
        public NavigableEntity Entity { get; private set; }
        public bool IsHandled => _skip || Entity != null;

        public static readonly DetectionResult Skip = new DetectionResult { _skip = true };
        public static readonly DetectionResult NotHandled = new DetectionResult();

        public static DetectionResult Detected(NavigableEntity entity)
        {
            return new DetectionResult { Entity = entity };
        }
    }

    /// <summary>
    /// Interface for entity detectors in the chain of responsibility.
    /// Each detector examines a field entity and either detects it, skips it, or passes.
    /// </summary>
    public interface IEntityDetector
    {
        /// <summary>
        /// Priority determines ordering in the chain (lower = earlier).
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Attempts to detect and classify the entity.
        /// </summary>
        DetectionResult TryDetect(EntityDetectionContext context);
    }
}
