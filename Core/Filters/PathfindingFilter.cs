using FFI_ScreenReader.Field;
using UnityEngine;

namespace FFI_ScreenReader.Core.Filters
{
    /// <summary>
    /// Filters entities by whether they have a valid path from the player.
    /// This is an expensive filter as it runs pathfinding for each entity.
    /// </summary>
    public class PathfindingFilter : IEntityFilter
    {
        private bool isEnabled = false;

        /// <summary>
        /// Whether this filter is enabled.
        /// </summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (value != isEnabled)
                {
                    isEnabled = value;
                    if (value)
                        OnEnabled();
                    else
                        OnDisabled();
                }
            }
        }

        /// <summary>
        /// Display name for this filter.
        /// </summary>
        public string Name => "Pathfinding Filter";

        /// <summary>
        /// Pathfinding filter runs at cycle time since paths change as player/entities move.
        /// </summary>
        public FilterTiming Timing => FilterTiming.OnCycle;

        /// <summary>
        /// Checks if an entity has a valid path from the player.
        /// </summary>
        public bool PassesFilter(NavigableEntity entity, FilterContext context)
        {
            if (!IsEntityValid(entity))
                return false;

            if (context.PlayerController == null)
                return false;

            // Use localPosition for pathfinding
            Vector3 playerPos = context.PlayerPosition;
            Vector3 targetPos = entity.Position;

            var pathInfo = FieldNavigationHelper.FindPathTo(
                playerPos,
                targetPos,
                context.MapHandle,
                context.FieldPlayer
            );

            return pathInfo.Success;
        }

        /// <summary>
        /// Called when filter is enabled.
        /// </summary>
        public void OnEnabled()
        {
        }

        /// <summary>
        /// Called when filter is disabled.
        /// </summary>
        public void OnDisabled()
        {
        }

        private bool IsEntityValid(NavigableEntity entity) => entity != null && entity.IsAlive;
    }
}
