# Character Creation Screen - Implementation Plan

## Problem Analysis

**Current buggy behavior:**
- Left/right arrows: reads wrong cached values from previous selections
- Up/down arrows: reads stale name/class from unrelated fields

**Root cause:** The existing `NewGameNamingPatches.cs` patches the wrong methods. It patches `InitSelect/UpdateSelect` on `NewGameWindowController`, but navigation within the character selection grid is handled by `CharacterContentListController.SelectContent`.

## Expected Behavior

### Navigation Layout
```
Row 1: [LW1: Name] [LW1: Class] | [LW2: Name] [LW2: Class]
Row 2: [LW3: Name] [LW3: Class] | [LW4: Name] [LW4: Class]
```

### Left/Right Arrows
- When on **Name field**: Navigate between Light Warriors 1-4
  - "Light Warrior 1: Name: Rei" → "Light Warrior 2: Name: Barret"
- When on **Class field**: Navigate between Light Warriors 1-4
  - "Light Warrior 1: Class: Monk" → "Light Warrior 2: Class: Warrior"

### Up/Down Arrows
- Navigate between Name and Class fields **for the same character**
- Also switch between rows (warriors 1-2 vs 3-4)
  - Example: Down from LW1 Name → "Light Warrior 3: Name: Maria"
  - Example: Down from LW1 Name (if at Class) → "Class: White Mage"

## Technical Discovery

### Key Classes (from dump.cs)

**CharacterSelectCommnadId enum (line 301636):**
```csharp
public enum CharacterSelectCommnadId {
    Non = 0,
    Job = 1,        // Class field
    CharaName = 2,  // Name field
    AllDecision = 3
}
```

**CharacterContentListController (KeyInput, line 453370):**
- `contentList` - List<CharacterContentController> (4 warriors)
- `commandList` - List<CharacterSelectCommandContentView> (all selectable fields)
- `targetIndex` - current selection index
- `OnSelect(CharacterSelectCommnadId, int)` - fires when selection changes
- `SelectContent(Cursor targetCursor)` - called when cursor moves to new field

**CharacterContentController (KeyInput, line 453284):**
- `CharacterName` - the character's name string
- `Data` - NewGameSelectData with character info
- `CommandList` - List<CharacterSelectCommandContentView> (Name/Class fields)
- `SetForcus(CharacterSelectCommnadId)` - sets focus to specific field

**NewGameWindowController (KeyInput, line 279470):**
- `selectedIndex` - which Light Warrior (0-3)
- `selectedCommandId` - CharaName(2) or Job(1)
- `SelectedDataList` - List<NewGameSelectData> for all 4 warriors
- `CurrentData` - NewGameSelectData for selected warrior
- `charaListController` - CharacterContentListController reference

## Implementation Plan

### Step 1: Create CharacterCreationPatches.cs

Replace the existing approach with a new patch that:

1. **Patches `CharacterContentListController.SelectContent`** (KeyInput version)
   - Called whenever cursor moves within the character selection grid
   - Receives `targetCursor` with position information

2. **Tracks state:**
   - `lastCharacterIndex` - which Light Warrior (0-3)
   - `lastCommandId` - Name or Class field
   - `lastAnnouncedText` - for deduplication

3. **Reads correct data:**
   - Get character index from cursor position or controller state
   - Get command ID from the focused field
   - Get character name from `SelectedDataList[index].CharacterName`
   - Get job name from `CharacterContentController.Data` or by walking hierarchy

### Step 2: Data Access Strategy

```csharp
// 1. Get the CharacterContentListController instance
Component controller = __instance as Component;

// 2. Get contentList to access individual character controllers
var contentListField = AccessTools.Field(controller.GetType(), "contentList");
var contentList = contentListField.GetValue(controller) as IList;

// 3. Determine which character and field from cursor
// The cursor's Index maps to: characterIndex * 2 + commandOffset
// Or we can track which CharacterContentController has focus

// 4. Get character name
var charController = contentList[characterIndex];
var nameProp = AccessTools.Property(charController.GetType(), "CharacterName");
string name = nameProp.GetValue(charController) as string;

// 5. Get job name by finding focused CommandContentView
// Each CharacterContentController.CommandList has Name and Job views
// Check which one has focus via Data.Id property
```

### Step 3: Announcement Logic

```csharp
// Format varies based on what changed:

// Character changed (left/right or up/down between rows):
// "Light Warrior {n}: Name: {name}"
// "Light Warrior {n}: Class: {jobName}"

// Only field changed (up/down within same character):
// "Name: {name}"
// "Class: {jobName}"

// To detect: compare lastCharacterIndex with currentCharacterIndex
```

### Step 4: Update FFI_ScreenReaderMod.cs

- Register new patches in ApplyPatches()
- Remove or disable conflicting patches from NewGameNamingPatches

### Step 5: Prevent MenuTextDiscovery Interference

- Add `IsHandlingCursor` flag like JobSelectionPatches
- Check flag in MenuTextDiscovery.WaitAndReadCursor()

## File Changes

### New File: `Patches/CharacterCreationPatches.cs`

```csharp
public static class CharacterCreationPatches
{
    private static int lastCharacterIndex = -1;
    private static int lastCommandId = -1;  // 1=Job, 2=Name
    private static string lastAnnouncement = "";

    public static bool IsHandlingCursor { get; private set; } = false;

    public static void ApplyPatches(Harmony harmony)
    {
        // Patch CharacterContentListController.SelectContent (KeyInput)
        Type type = FindType("Il2CppLast.UI.KeyInput.CharacterContentListController");
        var method = AccessTools.Method(type, "SelectContent");
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(CharacterCreationPatches), "SelectContent_Postfix"));
    }

    public static void SelectContent_Postfix(object __instance, object targetCursor)
    {
        IsHandlingCursor = true;
        try
        {
            // Extract current position
            // Determine characterIndex (0-3) and commandId (1=Job, 2=Name)
            // Read appropriate data
            // Announce based on what changed
        }
        finally
        {
            CoroutineManager.StartManaged(ClearFlagAfterFrame());
        }
    }

    private static string GetAnnouncementForField(object controller, int charIndex, int commandId)
    {
        // Get SelectedDataList from NewGameWindowController parent
        // Or get from CharacterContentController.Data
        // Format: "Light Warrior {n}: {Field}: {Value}"
    }
}
```

### Modify: `Patches/NewGameNamingPatches.cs`

- Remove or disable `InitSelect_Postfix` and `UpdateSelect_Postfix` patches
- Keep `InitNameSelect`, `UpdateNameSelect`, `InitNameInput` for name selection/input states
- These handle the name cycling (NameSelect state) not character grid selection (Select state)

### Modify: `Menus/MenuTextDiscovery.cs`

- Add check for `CharacterCreationPatches.IsHandlingCursor` alongside `JobSelectionPatches.IsHandlingCursor`

## Testing Checklist

1. **Left/Right in Name field:**
   - [ ] "Light Warrior 1: Name: Rei"
   - [ ] Press Right → "Light Warrior 2: Name: Barret"
   - [ ] Press Right → "Light Warrior 3: Name: Maria" (or 2 depending on layout)

2. **Left/Right in Class field:**
   - [ ] "Light Warrior 1: Class: Warrior"
   - [ ] Press Right → "Light Warrior 2: Class: Monk"

3. **Up/Down between Name and Class:**
   - [ ] On "Light Warrior 1: Name: Rei"
   - [ ] Press Down → "Class: Warrior" (same character, field changed)

4. **Up/Down between rows:**
   - [ ] On "Light Warrior 1: Name: Rei"
   - [ ] Press Down (past class) → "Light Warrior 3: Name: Maria"

5. **No duplicate announcements:**
   - [ ] Pressing same direction repeatedly doesn't re-announce if nothing changed

## Risk Mitigation

- **Fallback:** If new patches fail, keep old behavior working via try/catch
- **Logging:** Add detailed MelonLogger output for debugging cursor positions
- **Graceful degradation:** If character/job name can't be read, announce "Unknown"
