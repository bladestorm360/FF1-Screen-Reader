using System;
using UnityEngine;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Field;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Manages entity cycling and pathfinding announcements on the field map.
    /// </summary>
    public class EntityNavigationManager
    {
        private readonly EntityScanner entityScanner;
        private readonly Func<EntityCategory> getCategory;
        private int lastScannedMapId = -1;

        public EntityNavigationManager(EntityScanner scanner, Func<EntityCategory> getCategory)
        {
            this.entityScanner = scanner;
            this.getCategory = getCategory;
        }

        /// <summary>
        /// Checks if player is on an active field map.
        /// Returns true if ready for entity navigation, false otherwise.
        /// </summary>
        public bool EnsureFieldContext()
        {
            if (MenuStateRegistry.AnyActive())
                return false;

            var fieldMap = GameObjectCache.Get<Il2Cpp.FieldMap>();
            if (fieldMap == null)
                fieldMap = GameObjectCache.Refresh<Il2Cpp.FieldMap>();

            if (fieldMap == null || !fieldMap.gameObject.activeInHierarchy)
            {
                FFI_ScreenReader.Core.FFI_ScreenReaderMod.SpeakText(T("Not on map"));
                return false;
            }

            var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
            if (playerController == null)
                playerController = GameObjectCache.Refresh<Il2CppLast.Map.FieldPlayerController>();

            if (playerController?.fieldPlayer == null)
            {
                FFI_ScreenReader.Core.FFI_ScreenReaderMod.SpeakText(T("Not on map"));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Refreshes entities on user input (J/K/L keys).
        /// Always runs incremental scan to detect new/removed entities and update live state.
        /// Full rescan only on map transitions.
        /// </summary>
        public void RefreshEntitiesIfNeeded()
        {
            if (entityScanner == null) return;

            int currentMapId = FFI_ScreenReaderMod.GetCurrentMapId();
            bool mapChanged = (currentMapId != lastScannedMapId && currentMapId > 0 && lastScannedMapId > 0);

            if (mapChanged)
            {
                GameObjectCache.Clear<Il2CppLast.Map.FieldPlayerController>();
                entityScanner.ForceRescan();
                lastScannedMapId = currentMapId;
                NavigationTargetTracker.Clear();
            }
            else
            {
                entityScanner.ScanEntities();
                if (lastScannedMapId <= 0 && currentMapId > 0)
                    lastScannedMapId = currentMapId;
            }
        }

        /// <summary>
        /// Announces pathfinding directions to the currently selected entity.
        /// </summary>
        public void AnnounceCurrentEntity()
        {
            RefreshEntitiesIfNeeded();

            var entity = entityScanner?.CurrentEntity;
            if (entity == null)
            {
                FFI_ScreenReaderMod.SpeakText(T("No entity selected"));
                return;
            }

            NavigationTargetTracker.MarkEntity();

            var context = new FilterContext();
            if (context.PlayerPosition == Vector3.zero)
            {
                FFI_ScreenReaderMod.SpeakText(T("Cannot determine directions"));
                return;
            }

            var pathInfo = FieldNavigationHelper.FindPathTo(
                context.PlayerPosition, entity.Position, context.MapHandle, context.FieldPlayer);

            string announcement;
            if (pathInfo.Success && !string.IsNullOrEmpty(pathInfo.Description))
                announcement = pathInfo.Description;
            else
                announcement = T("No path");

            FFI_ScreenReaderMod.SpeakText(announcement);
        }

        /// <summary>
        /// Cycles to the next entity and announces it.
        /// </summary>
        public void CycleNext()
        {
            if (!EnsureFieldContext()) return;

            if (entityScanner == null)
            {
                FFI_ScreenReaderMod.SpeakText(T("Entity scanner not available"));
                return;
            }

            RefreshEntitiesIfNeeded();
            entityScanner.NextEntity();

            if (entityScanner.NoReachableEntities())
            {
                FFI_ScreenReaderMod.SpeakText(T("No reachable entities"));
                return;
            }

            NavigationTargetTracker.MarkEntity();
            AnnounceEntityOnly();
        }

        /// <summary>
        /// Cycles to the previous entity and announces it.
        /// </summary>
        public void CyclePrevious()
        {
            if (!EnsureFieldContext()) return;

            if (entityScanner == null)
            {
                FFI_ScreenReaderMod.SpeakText(T("Entity scanner not available"));
                return;
            }

            RefreshEntitiesIfNeeded();
            entityScanner.PreviousEntity();

            if (entityScanner.NoReachableEntities())
            {
                FFI_ScreenReaderMod.SpeakText(T("No reachable entities"));
                return;
            }

            NavigationTargetTracker.MarkEntity();
            AnnounceEntityOnly();
        }

        /// <summary>
        /// Announces the currently selected entity's name, direction, and index.
        /// </summary>
        public void AnnounceEntityOnly()
        {
            if (!EnsureFieldContext()) return;

            RefreshEntitiesIfNeeded();

            var entity = entityScanner?.CurrentEntity;
            if (entity == null)
            {
                string categoryName = CategoryManager.GetCategoryName(getCategory());
                int count = entityScanner?.Entities?.Count ?? 0;
                if (count == 0)
                    FFI_ScreenReaderMod.SpeakText(string.Format(T("No {0} found"), categoryName));
                else
                    FFI_ScreenReaderMod.SpeakText(T("No entity selected"));
                return;
            }

            NavigationTargetTracker.MarkEntity();

            var context = new FilterContext();
            string announcement = entity.FormatDescription(context.PlayerPosition);

            int index = entityScanner.CurrentIndex + 1;
            int total = entityScanner.Entities.Count;
            announcement += " " + string.Format(T("({0} of {1})"), index, total);

            FFI_ScreenReaderMod.SpeakText(announcement);
        }
    }
}
