# Menu Accessibility Implementation

## Priority Order

1. Title Screen
2. Config Menu
3. Save/Load Slots
4. Main Menu Navigation
5. Shops (Buy/Sell)
6. Equipment Menu
7. Item Menu
8. Magic/Ability Menu
9. Status Menu
10. New Game Naming

---

## Title Screen

### Classes
- `TitleWindowController` (Last.UI.Touch) - Main title logic
- `TitleMenuCommandController` (Last.UI.Touch) - Command selection
- `TitleCommandContentView` - Individual command display
- `TitleCommandId` - Enum for commands

### Patch Points
```csharp
// TitleMenuCommandController
- SetCursor(int index)           // Cursor moved
- SelectInit()                   // Menu initialized
- onTouch (Action<TitleCommandId>) // Command selected
```

### Implementation
```csharp
// TitleMenuPatches.cs - Use attribute patching (no string params)
[HarmonyPatch]
public static class TitleMenuPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TitleMenuCommandController), "SetCursor")]
    public static void SetCursor_Postfix(TitleMenuCommandController __instance, int index)
    {
        // Read command at index
        var commands = __instance.GetComponentsInChildren<TitleCommandContentView>();
        if (index >= 0 && index < commands.Length)
        {
            string text = commands[index].GetComponentInChildren<Text>()?.text;
            TolkWrapper.Speak(text, interrupt: true);
        }
    }
}
```

### Commands to Announce
- New Game
- Load Game
- Config
- (Any DLC options)

---

## Config Menu

### Classes
- `ConfigController` - Main config logic
- `ConfigMenuCommandController` - Command list
- `ConfigDetailsController` - Setting values
- `ConfigActualDetailsControllerBase` - Value display

### Patch Points
```csharp
// ConfigMenuCommandController
- Cursor navigation methods

// ConfigDetailsController
- Value change methods
- Option selection
```

### Implementation
Port `ConfigMenuPatches.cs` from FF3:
- Announce option name on cursor move
- Announce current value
- Announce new value on change
- Handle toggle, slider, dropdown types

### Hotkey
- `I` - Read current option description/tooltip

---

## Save/Load Slots

### Classes
- `SaveWindowController` - Save window
- `LoadWindowController` - Load window
- `SaveListController` - Slot list management
- `SaveContentController` - Individual slot display
- `SaveSlotData` - Slot data structure

### Key Properties (SaveSlotData)
```csharp
// Access via SaveContentController.SetData(SaveSlotData)
- Character names
- Play time
- Gil amount
- Location/map name
- Save date/time
```

### Implementation
```csharp
// SaveSlotReader.cs
public static string ReadSlotInfo(SaveContentController controller)
{
    if (!controller.IsExistData)
        return "Empty Slot";

    // Extract slot data via reflection
    var data = GetSlotData(controller);
    return $"{data.LeaderName}, Level {data.Level}, {data.PlayTime}, {data.Location}";
}
```

### Patch Points
```csharp
// SaveListController
- Cursor index changes
- SetLatestIndex(bool)

// SaveContentController
- SetFocus(bool)
- SetData(SaveSlotData)
```

---

## Main Menu

### Classes
- `MenuManager` (Singleton) - Menu lifecycle
- `MainMenuController` - Main menu logic
- `CommandMenuController` - Command list
- `CommandMenuContentController` - Individual commands

### Patch Points
```csharp
// MainMenuController via Cursor
- Cursor.PrevIndex()
- Cursor.NextIndex()
- Cursor.SkipPrevIndex()
- Cursor.SkipNextIndex()
```

### Implementation
Port `CursorNavigationPatches.cs`:
- Patch Cursor navigation methods
- Delay one frame for UI update
- Use MenuTextDiscovery to find text

### MenuTextDiscovery Strategies
1. Check for save slot context
2. Check for character selection context
3. Walk GameObject hierarchy for Text components
4. Search ContentList by index
5. Fallback component search

---

## Shops

### Classes
- `ShopUIManager` (Singleton) - Shop lifecycle
- `ShopController` - Main shop logic
- `ShopCommandMenuController` - Buy/Sell/Exit commands
- `ShopListController` - Item list
- `ShopListItemContentController` - Individual items
- `ShopItemInfoController` - Item details
- `ShopTradeWindowController` - Transaction confirmation
- `ShopPartyController` - Party member selection

### FF1 Shop Types
- Weapon shops
- Armor shops
- Item shops
- Magic shops (spell purchase)
- Inn (HP/MP restore)

### Patch Points
```csharp
// ShopCommandMenuController
- Command selection (Buy/Sell/Exit)

// ShopListController
- AsyncSelected() - Item selected
- Cursor navigation

// ShopListItemContentController
- Item name, price, quantity

// ShopTradeWindowController
- Quantity selection
- Confirm/cancel
```

### Implementation
```csharp
// ShopMenuPatches.cs
[HarmonyPatch]
public static class ShopMenuPatches
{
    // Announce shop command selection
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ShopCommandMenuController), /* cursor method */)]
    public static void CommandCursor_Postfix(/* params */)
    {
        // Read Buy/Sell/Exit
    }

    // Announce item in list
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ShopListController), /* selection method */)]
    public static void ItemSelection_Postfix(/* params */)
    {
        // Read: "Iron Sword, 500 Gil, Owned: 2"
    }
}

// ShopReader.cs
public static string ReadShopItem(ShopListItemContentController item)
{
    string name = item.GetItemName();
    int price = item.GetPrice();
    int owned = item.GetOwnedCount();
    return $"{name}, {price} Gil, Owned: {owned}";
}
```

### Announcements
- Shop type on entry (Weapon Shop, Magic Shop, etc.)
- Command selection (Buy, Sell, Exit)
- Item name + price + owned count
- Quantity during purchase
- "Not enough Gil" / "Inventory full" errors
- Transaction confirmation

---

## Equipment Menu

### Classes
- `EquipmentWindowController` - Main window
- `EquipmentCommandController` - Equip/Remove/Optimal
- `EquipmentSelectWindowController` - Item selection
- `EquipmentStatusContentController` - Stat changes
- `EquipmentInfoWindowController` - Item info

### Patch Points
```csharp
// EquipmentCommandController
- Cursor navigation

// EquipmentSelectWindowController
- AsyncActiveCursor() - Selection active
- Cursor navigation

// Stat comparison display
- Before/after stats
```

### Implementation
Port `EquipMenuPatches.cs`:
- Announce equipment slot (Weapon, Shield, Helmet, etc.)
- Announce item name
- Announce stat changes (ATK +5, DEF -2, etc.)

---

## Item Menu

### Classes
- `ItemWindowController` - Main item window
- `ItemCommandController` - Use/Sort commands
- `ItemListController` - Item list
- `ItemListContentController` - Individual items
- `ItemTargetSelectListController` - Target selection

### Patch Points
```csharp
// ItemListController
- Cursor navigation
- SortPerformance()

// ItemListContentController
- Item selection

// ItemTargetSelectListController
- Target selection for usable items
```

---

## Magic/Ability Menu

### Classes
- `AbilityWindowController` - Main ability window
- `AbilityCommandController` - Magic type selection
- `AbilityContentListController` - Spell list
- `AbilityUseContentListController` - Usage target

### FF1 Magic Notes
- Spell slots per level (1-8 charges per level)
- White Magic / Black Magic separation
- Announce remaining charges

---

## Status Menu

### Classes
- `StatusWindowController` - Main status
- `StatusWindowContentController` - Character info
- `StatusDetailsController` - Detailed stats

### Announcements
- Character name + job
- HP / Max HP
- Level + Experience
- Stats (STR, AGI, INT, VIT, LCK)
- Equipped items
- Known spells

---

## New Game Naming

### Classes
- Type discovered at runtime: `NewGameWindowController` (FF1 variant)
- Character name input
- Keyboard/controller letter selection

### Workaround Required
**Must use manual patching** - IL2CPP string parameter crash.

```csharp
// NewGameNamingPatches.cs
public static void ApplyPatches(Harmony harmony)
{
    Type controllerType = FindType("Il2CppSerial.FF1.UI.KeyInput.NewGameWindowController");
    // OR check for FF1-specific namespace

    var initMethod = AccessTools.Method(controllerType, "InitNameSelect");
    harmony.Patch(initMethod, postfix: new HarmonyMethod(...));
}
```

### FF1 Specifics
- Four characters to name (Light Warriors)
- Each has a job class displayed
- Default names (varies by version)

### Announcements
- Current character being named + job
- Selected letter
- Current name so far
- Confirm/cancel

---

## Cursor System (Shared)

### Cursor Class Methods
```csharp
public class Cursor : MonoBehaviour
{
    public int Index { get; }
    public bool IsFocus { get; }

    public void PrevIndex(Action<int> callback, int max, bool wrap);
    public void NextIndex(Action<int> callback, int max, bool wrap);
    public void SkipPrevIndex(Action<int> callback, int max, int skip, bool wrap, bool sound);
    public void SkipNextIndex(Action<int> callback, int max, int skip, bool wrap, bool sound);

    public void SetFocus(bool focus);
}
```

### Universal Cursor Patch
```csharp
// CursorNavigationPatches.cs
[HarmonyPatch(typeof(Cursor))]
public static class CursorNavigationPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("NextIndex")]
    public static void NextIndex_Postfix(Cursor __instance)
    {
        CoroutineManager.Start(MenuTextDiscovery.WaitAndReadCursor(__instance));
    }

    // Same for PrevIndex, SkipNextIndex, SkipPrevIndex
}
```

---

## Text Discovery Strategies

### MenuTextDiscovery.cs
```csharp
public static class MenuTextDiscovery
{
    public static IEnumerator WaitAndReadCursor(Cursor cursor)
    {
        yield return null; // Wait one frame

        string text = TryAllStrategies(cursor);
        if (!string.IsNullOrEmpty(text))
        {
            TolkWrapper.Speak(TextUtils.StripIcons(text), interrupt: true);
        }
    }

    private static string TryAllStrategies(Cursor cursor)
    {
        // 1. Save slot context
        if (TrySaveSlotStrategy(cursor, out string result)) return result;

        // 2. Character selection context
        if (TryCharacterSelectionStrategy(cursor, out result)) return result;

        // 3. Shop context
        if (TryShopStrategy(cursor, out result)) return result;

        // 4. Direct hierarchy walk
        if (TryHierarchyWalk(cursor, out result)) return result;

        // 5. ContentList indexed search
        if (TryContentListStrategy(cursor, out result)) return result;

        // 6. Fallback
        return TryFallbackStrategy(cursor);
    }
}
```
