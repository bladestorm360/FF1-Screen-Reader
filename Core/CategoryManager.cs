using FFI_ScreenReader.Field;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Manages entity category filtering and cycling.
    /// </summary>
    public class CategoryManager
    {
        private readonly EntityScanner entityScanner;
        private readonly EntityNavigationManager entityNav;
        private EntityCategory currentCategory = EntityCategory.All;
        private static readonly int CategoryCount = System.Enum.GetValues(typeof(EntityCategory)).Length;

        /// <summary>
        /// The currently active entity category filter.
        /// </summary>
        public EntityCategory CurrentCategory => currentCategory;

        public CategoryManager(EntityScanner scanner, EntityNavigationManager entityNav)
        {
            this.entityScanner = scanner;
            this.entityNav = entityNav;
        }

        /// <summary>
        /// Cycles to the next entity category.
        /// </summary>
        public void CycleNext()
        {
            if (!entityNav.EnsureFieldContext()) return;

            int nextCategory = ((int)currentCategory + 1) % CategoryCount;
            currentCategory = (EntityCategory)nextCategory;
            if (entityScanner != null)
                entityScanner.CurrentCategory = currentCategory;

            AnnounceCategoryChange();
        }

        /// <summary>
        /// Cycles to the previous entity category.
        /// </summary>
        public void CyclePrevious()
        {
            if (!entityNav.EnsureFieldContext()) return;

            int prevCategory = (int)currentCategory - 1;
            if (prevCategory < 0)
                prevCategory = CategoryCount - 1;

            currentCategory = (EntityCategory)prevCategory;
            if (entityScanner != null)
                entityScanner.CurrentCategory = currentCategory;

            AnnounceCategoryChange();
        }

        /// <summary>
        /// Resets to the All category.
        /// </summary>
        public void ResetToAll()
        {
            if (!entityNav.EnsureFieldContext()) return;

            if (currentCategory == EntityCategory.All)
            {
                FFI_ScreenReaderMod.SpeakText(T("Already in All category"));
                return;
            }

            currentCategory = EntityCategory.All;
            if (entityScanner != null)
                entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        private void AnnounceCategoryChange()
        {
            string categoryText = string.Format(T("Category: {0}"), GetCategoryName(currentCategory));

            // Refresh so the new category's nearest entity is the current selection.
            entityNav.RefreshEntitiesIfNeeded();

            // Obey the pathfinding filter: select the nearest reachable entity. If none are reachable
            // (or the category is empty), treat the category as empty — announce only its name (the
            // "No reachable entities" message is reserved for entity cycling).
            if (entityScanner == null || !entityScanner.SelectFirstReachable())
            {
                FFI_ScreenReaderMod.SpeakText(categoryText);
                return;
            }

            string entityDescription = entityNav.FormatCurrentEntity();

            if (entityDescription == null)
            {
                // Empty category: announce the category name only (silent on the entity).
                FFI_ScreenReaderMod.SpeakText(categoryText);
                return;
            }

            // Category has an entity: announce the category and its first entity, and make it
            // the active navigation target (matches AnnounceEntityOnly behavior).
            NavigationTargetTracker.MarkEntity();
            FFI_ScreenReaderMod.SpeakText($"{categoryText}, {entityDescription}");
        }

        /// <summary>
        /// Returns the display name for a category.
        /// </summary>
        public static string GetCategoryName(EntityCategory category)
        {
            switch (category)
            {
                case EntityCategory.All: return T("All");
                case EntityCategory.Chests: return T("Treasure Chests");
                case EntityCategory.NPCs: return T("NPCs");
                case EntityCategory.MapExits: return T("Map Exits");
                case EntityCategory.Events: return T("Events");
                case EntityCategory.Vehicles: return T("Vehicles");
                default: return T("Unknown");
            }
        }
    }
}
