using System;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Shared helper for reading state machine values from IL2CPP controllers.
    /// Consolidates duplicate GetCurrentState() methods from menu patches.
    /// </summary>
    public static class StateMachineHelper
    {
        // Common offsets within state machine objects - use centralized offsets
        private const int OFFSET_STATE_MACHINE_CURRENT = IL2CppOffsets.StateMachine.Current;
        private const int OFFSET_STATE_TAG = IL2CppOffsets.StateMachine.StateTag;

        /// <summary>
        /// Reads the current state from a controller's state machine.
        /// </summary>
        /// <param name="controllerPtr">Pointer to the IL2CPP controller object</param>
        /// <param name="stateMachineOffset">Offset of the stateMachine field within the controller</param>
        /// <returns>The current state tag value, or -1 if unable to read</returns>
        public static int ReadState(IntPtr controllerPtr, int stateMachineOffset)
        {
            if (controllerPtr == IntPtr.Zero)
                return -1;

            try
            {
                unsafe
                {
                    // Read stateMachine pointer at the specified offset
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + stateMachineOffset);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    // Read current State<T> pointer at offset 0x10
                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    // Read Tag (int) at offset 0x10
                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
        }
    }
}
