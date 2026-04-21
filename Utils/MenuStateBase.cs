using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Base class for menu state tracking singletons.
    /// Provides the common IsActive/ShouldSuppress/ClearState pattern
    /// shared by most menu state classes.
    ///
    /// Subclasses provide RegistryKey, then expose static facades that delegate
    /// to the singleton instance.
    /// </summary>
    public abstract class MenuStateBase
    {
        /// <summary>
        /// The MenuStateRegistry key for this state (e.g., MenuStateRegistry.BATTLE_ITEM).
        /// </summary>
        protected abstract string RegistryKey { get; }

        /// <summary>
        /// Whether this menu state is currently active.
        /// </summary>
        public bool IsActive
        {
            get => MenuStateRegistry.IsActive(RegistryKey);
            set => MenuStateRegistry.SetActive(RegistryKey, value);
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Default: suppress when active. Override for state-machine-based validation.
        /// </summary>
        public virtual bool ShouldSuppress() => IsActive;

        /// <summary>
        /// Clears the active state.
        /// </summary>
        public virtual void ClearState() => IsActive = false;

        /// <summary>
        /// Resets the active state. Override to clear additional fields.
        /// </summary>
        public virtual void ResetState() => ClearState();

        /// <summary>
        /// Called by MenuStateRegistry reset handler. Override to add cleanup.
        /// </summary>
        protected virtual void OnReset() { }

        /// <summary>
        /// Registers this state's reset handler with MenuStateRegistry.
        /// Call from the static constructor of the facade class.
        /// </summary>
        public void RegisterResetHandler()
        {
            MenuStateRegistry.RegisterResetHandler(RegistryKey, OnReset);
        }
    }
}
