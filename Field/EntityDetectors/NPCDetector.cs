using FieldNonPlayer = Il2CppLast.Entity.Field.FieldNonPlayer;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects NPCs via FieldNonPlayer cast.
    /// Skips non-interactive NPCs (CanAction == false).
    /// Priority 70.
    /// </summary>
    public class NPCDetector : IEntityDetector
    {
        public int Priority => 70;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            var fieldNonPlayer = context.FieldEntity.TryCast<FieldNonPlayer>();
            if (fieldNonPlayer == null)
                return DetectionResult.NotHandled;

            // Check if NPC is actionable (can be interacted with)
            try
            {
                if (!fieldNonPlayer.CanAction)
                    return DetectionResult.Skip;
            }
            catch { } // CanAction may not exist - include NPC if check fails

            string npcName = EntityDetectionHelpers.GetEntityNameFromProperty(context.FieldEntity);
            if (string.IsNullOrEmpty(npcName) || npcName == "NPC")
                npcName = EntityDetectionHelpers.CleanObjectName(context.GameObjectName, "NPC");
            bool isShop = context.GameObjectNameLower.Contains("shop") ||
                          context.GameObjectNameLower.Contains("merchant");
            return DetectionResult.Detected(
                new NPCEntity(context.FieldEntity, context.Position, npcName, "", isShop));
        }
    }
}
