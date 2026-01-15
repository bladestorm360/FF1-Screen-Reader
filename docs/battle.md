# Battle Accessibility Implementation

## Overview

Make turn-based battles fully accessible by announcing:
1. Whose turn it is
2. Available commands
3. Target selection
4. Action execution ("X uses Y on Z")
5. Damage/healing numbers
6. Status effects
7. Battle results

---

## Battle Flow

```
Battle Start
    ↓
Command Selection (per character)
    ↓
Target Selection (if needed)
    ↓
Action Execution
    ↓
Damage/Effect Display
    ↓
Next Turn / Battle End
    ↓
Result Screen (victory/defeat)
```

---

## FF1 Game Classes

### Core Battle Classes
| Class | Purpose |
|-------|---------|
| `Battle` | Scene/subscene entry |
| `BattleController` | Main battle state machine |
| `BattlePlayController` | Turn/action management |
| `BattleProgress` | Turn order (base class) |
| `BattleProgressTurn` | Turn-based implementation |

### Command System
| Class | Purpose |
|-------|---------|
| `BattleCommandSelectController` | Command menu control |
| `BattleCommandSelectContentController` | Individual command items |
| `Command` (MasterBase) | Command data/definition |
| `BattleCommandInputController` | Input handling |

### Target System
| Class | Purpose |
|-------|---------|
| `BattleTargetSelectController` | Target selection UI |
| `BattleTargetSelectContentController` | Individual targets |
| `BattleTargetInfomationController` | Target info display |
| `BattleTargetSelectUtility` | Selection helpers |

### Action System
| Class | Purpose |
|-------|---------|
| `BattleActData` | Action data structure |
| `BattleActExection` | Action execution |
| `BattleBaseFunction` | Effect calculation base |
| `BattleBasicFunction` | Common effects |

### Unit Data
| Class | Purpose |
|-------|---------|
| `BattleUnitData` | Base unit data |
| `BattlePlayerData` | Player character data |
| `BattleEnemyData` | Enemy data |
| `BattleFightStatus` | HP/MP/conditions |

### Results
| Class | Purpose |
|-------|---------|
| `BattleResultData` | Victory rewards |
| `ResultController` | Result screen |
| `ResultMenuController` | Result UI |

### Messages/Display
| Class | Purpose |
|-------|---------|
| `DamageViewUIManager` | Damage numbers |
| `BattleSystemMessageProvider` | System messages |
| `ViewDamageEntity` | Individual damage display |

---

## Command Selection

### BattleCommandPatches.cs
```csharp
[HarmonyPatch]
public static class BattleCommandPatches
{
    private static string lastAnnouncedCharacter = "";
    private static bool suppressCommandAnnouncement = false;

    // Announce when character's turn begins
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattleCommandSelectController), "SetCommandData")]
    public static void SetCommandData_Postfix(object __0)
    {
        try
        {
            // __0 is BattleUnitData for active character
            string charName = GetCharacterName(__0);

            if (charName != lastAnnouncedCharacter)
            {
                lastAnnouncedCharacter = charName;
                TolkWrapper.Speak($"{charName}'s turn", interrupt: true);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Error in SetCommandData: {ex.Message}");
        }
    }

    // Announce command on cursor move
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattleCommandSelectController), /* cursor method */)]
    public static void CommandCursor_Postfix(BattleCommandSelectController __instance)
    {
        if (suppressCommandAnnouncement) return;

        try
        {
            int index = GetCurrentIndex(__instance);
            string commandName = GetCommandName(__instance, index);

            if (!string.IsNullOrEmpty(commandName))
            {
                TolkWrapper.Speak(commandName, interrupt: true);
            }
        }
        catch { }
    }

    public static void SuppressCommands(bool suppress)
    {
        suppressCommandAnnouncement = suppress;
    }
}
```

### FF1 Commands
- **Attack** - Physical attack
- **Magic** - Opens spell list (White/Black)
- **Item** - Use item from inventory
- **Defend** - Reduce damage taken
- **Run** - Attempt to flee battle

---

## Target Selection

### BattleTargetPatches.cs
```csharp
[HarmonyPatch]
public static class BattleTargetPatches
{
    private static string lastAnnouncedTarget = "";

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattleTargetSelectController), /* target change method */)]
    public static void TargetChanged_Postfix(BattleTargetSelectController __instance)
    {
        try
        {
            // Suppress command announcements during targeting
            BattleCommandPatches.SuppressCommands(true);

            var target = GetCurrentTarget(__instance);
            string announcement = FormatTargetAnnouncement(target);

            if (announcement != lastAnnouncedTarget)
            {
                lastAnnouncedTarget = announcement;
                TolkWrapper.Speak(announcement, interrupt: true);
            }
        }
        catch { }
    }

    private static string FormatTargetAnnouncement(BattleUnitData target)
    {
        string name = GetUnitName(target);
        int currentHP = target.CurrentHP;
        int maxHP = target.MaxHP;

        return $"{name}, {currentHP}/{maxHP} HP";
    }

    // Handle enemy name deduplication (Goblin A, Goblin B, etc.)
    private static string GetUnitName(BattleUnitData unit)
    {
        if (unit is BattleEnemyData enemy)
        {
            return GetDeduplicatedEnemyName(enemy);
        }
        return unit.Name;
    }

    private static string GetDeduplicatedEnemyName(BattleEnemyData enemy)
    {
        // Game may already suffix with A/B/C
        // If not, track and add suffix
        string baseName = enemy.Name;
        int index = GetEnemyIndex(enemy);

        if (index > 0)
        {
            char suffix = (char)('A' + index);
            return $"{baseName} {suffix}";
        }
        return baseName;
    }
}
```

### Target Types (BattleActData.TargetType)
- `SingleMember` - One party member
- `AllMember` - All party members
- `SingleEnemy` - One enemy
- `AllEnemy` - All enemies
- `All` - Everyone
- `Self` - Caster only

---

## Action Announcements

### BattleMessagePatches.cs
```csharp
[HarmonyPatch]
public static class BattleMessagePatches
{
    // Track to prevent duplicate announcements
    private static readonly HashSet<string> recentAnnouncements = new();
    private static DateTime lastCleanup = DateTime.Now;

    // Announce action execution
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattleActExection), "UpdateExection")]
    public static void ActionExecuted_Postfix(BattleActExection __instance)
    {
        try
        {
            var actData = GetCurrentActData(__instance);
            if (actData == null) return;

            string announcement = FormatActionAnnouncement(actData);
            if (TryAnnounce(announcement))
            {
                TolkWrapper.Speak(announcement, interrupt: false);
            }
        }
        catch { }
    }

    private static string FormatActionAnnouncement(BattleActData actData)
    {
        string actor = GetActorName(actData.AttackUnitData);
        string action = GetActionName(actData.Command);
        string target = GetTargetNames(actData.TargetUnitDatas);

        if (string.IsNullOrEmpty(target))
            return $"{actor} uses {action}";

        return $"{actor} uses {action} on {target}";
    }

    private static bool TryAnnounce(string message)
    {
        // Cleanup old entries periodically
        if ((DateTime.Now - lastCleanup).TotalSeconds > 2)
        {
            recentAnnouncements.Clear();
            lastCleanup = DateTime.Now;
        }

        if (recentAnnouncements.Contains(message))
            return false;

        recentAnnouncements.Add(message);
        return true;
    }
}
```

### Action Message Examples
- "Fighter uses Attack on Goblin A"
- "Black Mage uses Fire on All Enemies"
- "White Mage uses Cure on Fighter"
- "Thief uses Item Potion on Black Mage"

---

## Damage/Healing Output

### DamagePatches.cs
```csharp
[HarmonyPatch]
public static class DamagePatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DamageViewUIManager), /* display method */)]
    public static void DamageDisplayed_Postfix(object __instance)
    {
        try
        {
            var damageEntities = GetVisibleDamageEntities(__instance);

            foreach (var entity in damageEntities)
            {
                string target = GetTargetName(entity);
                int amount = GetDamageAmount(entity);
                bool isHealing = IsHealingEffect(entity);

                string message = isHealing
                    ? $"{target}: {amount} healed"
                    : $"{target}: {amount} damage";

                TolkWrapper.Speak(message, interrupt: false);
            }
        }
        catch { }
    }
}
```

### Damage Types
- Physical damage
- Magical damage (elemental)
- Healing
- Miss / Ineffective
- Critical hit

---

## Status Effects

### StatusPatches.cs
```csharp
[HarmonyPatch]
public static class StatusPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattleStatusControl), /* apply condition method */)]
    public static void ConditionApplied_Postfix(object __0, object __1)
    {
        try
        {
            string unitName = GetUnitName(__0);
            string condition = GetConditionName(__1);

            TolkWrapper.Speak($"{unitName}: {condition}", interrupt: false);
        }
        catch { }
    }
}
```

### FF1 Status Conditions
**Negative:**
- Poison
- Blind (Dark)
- Sleep
- Paralysis
- Silence
- Confusion
- Stone (Petrify)
- Death

**Positive:**
- Haste
- Protect (NulShock, etc.)
- Regen (if applicable in PR)

---

## Battle Results

### BattleResultPatches.cs
```csharp
[HarmonyPatch]
public static class BattleResultPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ResultController), /* init method */)]
    public static void ResultInit_Postfix(object __instance)
    {
        try
        {
            var resultData = GetResultData(__instance);
            AnnounceVictory(resultData);
        }
        catch { }
    }

    private static void AnnounceVictory(BattleResultData data)
    {
        var sb = new StringBuilder();
        sb.Append("Victory! ");

        // Experience
        sb.Append($"{data.GetExp} experience. ");

        // Gil
        sb.Append($"{data.GetGil} gil. ");

        // Items
        if (data.ItemList?.Count > 0)
        {
            sb.Append("Items: ");
            foreach (var item in data.ItemList)
            {
                sb.Append($"{item.Name}, ");
            }
        }

        TolkWrapper.Speak(sb.ToString(), interrupt: true);
    }

    // Level up announcements
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ResultController), /* level up method */)]
    public static void LevelUp_Postfix(object __0)
    {
        try
        {
            var charData = __0 as BattleResultData.BattleResultCharacterData;
            if (charData?.IsLevelUp == true)
            {
                string name = charData.AfterData.Name;
                int level = charData.AfterData.Level;
                TolkWrapper.Speak($"{name} reached level {level}!", interrupt: false);
            }
        }
        catch { }
    }
}
```

### Result Screen Elements (TESTED 2026-01-14)
1. **Gil announcement** - "Gained X gil"
2. **Per-character XP** - "[Name] gained X XP" (uses game's per-character calculation, respects KO status)
3. **Item drops** - "Found [item name]" or "Found [item name] xN"
4. **Level ups** - "[Name] leveled up to [level], HP +X, Strength +Y, Stamina +Z, Intellect +W, Agility +V, Luck +U"

### Stat Names (match in-game display)
- Strength (not Power)
- Agility
- Stamina (not Vitality)
- Intellect (not Intelligence)
- Luck
- HP

### Implementation Notes
- Uses `ResultMenuController` (KeyInput namespace) with `targetData` field
- Patches `ShowPointsInit` for gil and per-character XP
- Patches `ShowGetItemsInit` for item drops
- Patches `ShowStatusUpInit` for level ups
- CharacterList iteration uses index-based access (foreach crashes IL2CPP)
- Item names resolved via `ContentUtitlity.GetMesIdItemName()` + `MessageManager`
- Stat gains calculated from `BeforData.Parameter` vs `AfterData.Parameter`

---

## Defeat Handling

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(BattleController), /* game over check */)]
public static void GameOver_Postfix(bool __result)
{
    if (__result)
    {
        TolkWrapper.Speak("Game Over", interrupt: true);
    }
}
```

---

## Battle State Tracking

### GlobalBattleState.cs
```csharp
public static class GlobalBattleState
{
    public static bool InBattle { get; private set; }
    public static bool InCommandSelect { get; private set; }
    public static bool InTargetSelect { get; private set; }
    public static BattleUnitData CurrentActor { get; private set; }

    public static void EnterBattle()
    {
        InBattle = true;
        TolkWrapper.Speak("Battle start", interrupt: true);
    }

    public static void ExitBattle()
    {
        InBattle = false;
        InCommandSelect = false;
        InTargetSelect = false;
        CurrentActor = null;
    }

    public static void EnterCommandSelect(BattleUnitData actor)
    {
        InCommandSelect = true;
        InTargetSelect = false;
        CurrentActor = actor;
    }

    public static void EnterTargetSelect()
    {
        InTargetSelect = true;
    }

    public static void ExitTargetSelect()
    {
        InTargetSelect = false;
    }
}
```

---

## FF1-Specific Battle Notes

### Party Configuration
- Fixed 4-character party
- No mid-battle party changes
- No summons or job abilities (unlike FF3)

### Magic System
- Spell slots per level (not MP)
- 8 spell levels
- Each level has limited charges
- Announce: "Fire, 2 charges remaining"

### Class Abilities
After class change:
- Knight can use low-level White Magic
- Ninja can use low-level Black Magic
- etc.

### Enemy Encounters
- Random encounters on field/dungeon
- Boss battles (scripted)
- Ambush/preemptive possible

### Escape
- Run command may fail
- Some battles don't allow escape (boss)

---

## Implementation Priority

1. **Character turn announcement** - Know whose turn it is
2. **Command selection** - Navigate Attack/Magic/Item/Defend/Run
3. **Target selection** - Enemy names with HP
4. **Action execution** - "X uses Y on Z"
5. **Damage output** - Numbers for each hit
6. **Victory/defeat** - Battle outcome
7. **Results screen** - EXP, Gil, items, levels
8. **Status effects** - Conditions applied/removed
9. **Magic charges** - Remaining spell uses
