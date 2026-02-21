using UnityEngine;
using Il2CppLast.Entity.Field;
using EventTriggerEntity = Il2CppLast.Entity.Field.EventTriggerEntity;

namespace FFI_ScreenReader.Field.EntityDetectors
{
    /// <summary>
    /// Pre-computed shared data for a single entity detection pass.
    /// Avoids repeated reflection and casts across multiple detectors.
    /// PropertyObject and ObjectType are lazy-evaluated since most detector chains
    /// short-circuit before needing them.
    /// </summary>
    public class EntityDetectionContext
    {
        public FieldEntity FieldEntity { get; }
        public Vector3 Position { get; }
        public string TypeName { get; }
        public string GameObjectName { get; }
        public string GameObjectNameLower { get; }
        public bool IsActive { get; }
        public bool IsEventTrigger { get; }

        // Lazy property object (uses reflection)
        private object _propertyObj;
        private bool _propertyResolved;

        public object PropertyObject
        {
            get
            {
                if (!_propertyResolved)
                {
                    _propertyObj = EntityDetectionHelpers.GetEntityProperty(FieldEntity);
                    _propertyResolved = true;
                }
                return _propertyObj;
            }
        }

        // Lazy object type (depends on PropertyObject)
        private int _objectType = -2;

        public int ObjectType
        {
            get
            {
                if (_objectType == -2)
                    _objectType = EntityDetectionHelpers.GetObjectType(PropertyObject);
                return _objectType;
            }
        }

        public EntityDetectionContext(FieldEntity fieldEntity)
        {
            FieldEntity = fieldEntity;
            Position = fieldEntity.transform.localPosition;
            TypeName = fieldEntity.GetType().Name;

            try { GameObjectName = fieldEntity.gameObject.name ?? ""; }
            catch { GameObjectName = ""; } // GameObject may be destroyed

            GameObjectNameLower = GameObjectName.ToLower();

            try { IsActive = fieldEntity.gameObject.activeInHierarchy; }
            catch { IsActive = true; } // Assume active if hierarchy check fails

            try { IsEventTrigger = fieldEntity.TryCast<EventTriggerEntity>() != null; }
            catch { IsEventTrigger = false; } // IL2CPP cast may fail
        }
    }
}
