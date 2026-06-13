using FFI_ScreenReader.Utils;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects vehicles via the VehicleTypeMap (populated from Transportation.ModelList).
    /// Priority 10: must run before VisualEffectFilter since vehicles use ResidentCharaEntity GameObjects.
    /// </summary>
    public class VehicleDetector : IEntityDetector
    {
        public int Priority => 10;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            if (FieldNavigationHelper.VehicleTypeMap.TryGetValue(context.FieldEntity, out int vehicleType))
            {
                // The canoe is an auto-used key item (summoned at canals), not a walk-to
                // vehicle — its map object sits out of bounds, so keep it out of navigation.
                if (vehicleType == FF1Constants.TransportTypes.CANOE)
                    return DetectionResult.Skip;

                string vehicleName = VehicleEntity.GetVehicleName(vehicleType);
                return DetectionResult.Detected(
                    new VehicleEntity(context.FieldEntity, context.Position, vehicleName, vehicleType));
            }
            return DetectionResult.NotHandled;
        }
    }
}
