using UnityEngine;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Handles field-specific input: teleport, entity navigation, pathfinding.
    /// </summary>
    internal static class FieldInputHandler
    {
        internal static void HandleInput(FFI_ScreenReaderMod mod)
        {
            // Ctrl+Arrow to teleport
            if (InputHelper.IsCtrlHeld())
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, 16));
                    return;
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, -16));
                    return;
                }
                else if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    mod.TeleportInDirection(new Vector2(-16, 0));
                    return;
                }
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    mod.TeleportInDirection(new Vector2(16, 0));
                    return;
                }
            }

            // J or [ to cycle backward (Shift for categories)
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                if (InputHelper.IsShiftHeld())
                {
                    mod.CyclePreviousCategory();
                }
                else
                {
                    mod.CyclePrevious();
                }
            }

            // K to repeat current entity
            if (Input.GetKeyDown(KeyCode.K))
            {
                mod.AnnounceEntityOnly();
            }

            // L or ] to cycle forward (Shift for categories)
            if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                if (InputHelper.IsShiftHeld())
                {
                    mod.CycleNextCategory();
                }
                else
                {
                    mod.CycleNext();
                }
            }

            // P or \ to pathfind (Ctrl+\ for layer filter, Shift for pathfinding filter)
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Backslash))
            {
                if (Input.GetKeyDown(KeyCode.Backslash) && InputHelper.IsCtrlHeld())
                {
                    mod.ToggleToLayerFilter();
                }
                else if (InputHelper.IsShiftHeld())
                {
                    mod.TogglePathfindingFilter();
                }
                else
                {
                    mod.AnnounceCurrentEntity();
                }
            }
        }
    }
}
