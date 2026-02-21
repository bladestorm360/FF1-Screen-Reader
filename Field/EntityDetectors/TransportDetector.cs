using PropertyTransportation = Il2CppLast.Map.PropertyTransportation;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Detects vehicles via PropertyTransportation and string-based fallback.
    /// This is the secondary vehicle detection (VehicleDetector handles VehicleTypeMap).
    /// Skips unknown vehicle types (Type 0) to prevent duplicates.
    /// Priority 90.
    /// </summary>
    public class TransportDetector : IEntityDetector
    {
        public int Priority => 90;

        public DetectionResult TryDetect(EntityDetectionContext context)
        {
            // Layer 2: Check for vehicles via PropertyTransportation
            try
            {
                var property = context.FieldEntity.Property;
                if (property != null)
                {
                    var transportProperty = property.TryCast<PropertyTransportation>();
                    if (transportProperty != null)
                        return TryCreateVehicle(context);
                }
            }
            catch { } // Property access may throw on disposed entity

            // Layer 3: String-based fallback (least reliable)
            if (context.TypeName.Contains("Transport") || context.GameObjectNameLower.Contains("ship") ||
                context.GameObjectNameLower.Contains("canoe") || context.GameObjectNameLower.Contains("airship"))
                return TryCreateVehicle(context);

            return DetectionResult.NotHandled;
        }

        private static DetectionResult TryCreateVehicle(EntityDetectionContext context)
        {
            string vehicleName = EntityDetectionHelpers.GetVehicleNameFromProperty(
                context.GameObjectName, context.TypeName);
            int vehicleType = EntityDetectionHelpers.GetVehicleTypeFromName(vehicleName);
            if (vehicleType == 0)
                return DetectionResult.Skip;
            return DetectionResult.Detected(
                new VehicleEntity(context.FieldEntity, context.Position, vehicleName, vehicleType));
        }
    }
}
