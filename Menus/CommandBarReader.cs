using FFI_ScreenReader.Core;
using static FFI_ScreenReader.Utils.ModTextTranslator;

// Command-id enums (each command bar uses its own). Shared FFPR types — values match across games.
using ShopCommandId = Il2CppLast.Defaine.ShopCommandId;
using ItemCommandId = Il2CppLast.Defaine.UI.ItemCommandId;
using EquipmentCommandId = Il2CppLast.UI.EquipmentCommandId;
using MenuCommandId = Il2CppLast.Defaine.MenuCommandId;
using AbilityCommandId = Il2CppLast.Defaine.UI.AbilityCommandId;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Command-bar name maps + a shared announce. Each command bar's open-trigger (CommandBarPatches)
    /// reads the focused command's ID ENUM — placeholder-proof, unlike the async-localized on-screen
    /// Text — and announces it via <see cref="Announce"/>. No dedup/gate lives here; the per-bar arm
    /// flag in CommandBarPatches guarantees one announce per open.
    ///
    /// Ported from FF2 to be iterated on in FF1. Names are FF1's own wording via T() (translation.json,
    /// falling back to the literal for English); refine against FF1's exact UI as needed.
    /// </summary>
    public static class CommandBarReader
    {
        /// <summary>Speaks a resolved command name (no-op if empty).</summary>
        public static void Announce(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
                return;
            FFI_ScreenReaderMod.SpeakText(commandName, interrupt: true);
        }

        /// <summary>Speaks a command name with a positional "(n of N)" suffix (index is ZERO-BASED).</summary>
        public static void Announce(string commandName, int index, int count)
        {
            Announce(FFI_ScreenReader.Utils.MenuPosition.Format(commandName, index, count));
        }

        public static string GetShopCommandName(ShopCommandId id) => id switch
        {
            ShopCommandId.Buy => T("Buy"),
            ShopCommandId.Sell => T("Sell"),
            ShopCommandId.Equipment => T("Equipment"),
            ShopCommandId.Back => T("Back"),
            _ => null
        };

        public static string GetItemCommandName(ItemCommandId id) => id switch
        {
            ItemCommandId.Use => T("Use"),
            ItemCommandId.Organize => T("Sort"),
            ItemCommandId.Important => T("Key Items"),
            _ => null
        };

        public static string GetEquipmentCommandName(EquipmentCommandId id) => id switch
        {
            EquipmentCommandId.Equip => T("Equip"),
            EquipmentCommandId.Strongest => T("Optimal"),
            EquipmentCommandId.RemoveEverything => T("Remove All"),
            _ => null
        };

        public static string GetAbilityCommandName(AbilityCommandId id) => id switch
        {
            AbilityCommandId.Use => T("Use"),
            AbilityCommandId.Forget => T("Forget"),
            AbilityCommandId.Memorize => T("Memorize"),
            AbilityCommandId.Remove => T("Remove"),
            AbilityCommandId.Exchange => T("Exchange"),
            _ => null
        };

        public static string GetMenuCommandName(MenuCommandId id) => id switch
        {
            MenuCommandId.Item => T("Item"),
            MenuCommandId.Magic => T("Magic"),
            MenuCommandId.Equipment => T("Equipment"),
            MenuCommandId.Status => T("Status"),
            MenuCommandId.Sort => T("Sort"),
            MenuCommandId.Words => T("Words"),
            MenuCommandId.Config => T("Config"),
            MenuCommandId.Interruption => T("Interruption"),
            MenuCommandId.Save => T("Save"),
            MenuCommandId.Back => T("Back"),
            MenuCommandId.Job => T("Job"),
            MenuCommandId.Ability => T("Ability"),
            MenuCommandId.Load => T("Load"),
            _ => null
        };
    }
}
