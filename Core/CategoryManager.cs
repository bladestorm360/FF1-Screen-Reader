using FFI_ScreenReader.Field;

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
                FFI_ScreenReaderMod.SpeakText("Already in All category");
                return;
            }

            currentCategory = EntityCategory.All;
            if (entityScanner != null)
                entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        private void AnnounceCategoryChange()
        {
            string categoryName = GetCategoryName(currentCategory);
            FFI_ScreenReaderMod.SpeakText($"Category: {categoryName}");
        }

        /// <summary>
        /// Returns the display name for a category.
        /// </summary>
        public static string GetCategoryName(EntityCategory category)
        {
            switch (category)
            {
                case EntityCategory.All: return "All";
                case EntityCategory.Chests: return "Treasure Chests";
                case EntityCategory.NPCs: return "NPCs";
                case EntityCategory.MapExits: return "Map Exits";
                case EntityCategory.Events: return "Events";
                case EntityCategory.Vehicles: return "Vehicles";
                default: return "Unknown";
            }
        }
    }
}
