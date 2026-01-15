# Field Navigation & Pathfinding Implementation

## Overview

Enable blind players to navigate the game world by:
1. Scanning for nearby entities (NPCs, chests, exits, etc.)
2. Cycling through entities with hotkeys
3. Filtering entities by category
4. Getting pathfinding directions to targets
5. Announcing current map location

---

## Entity Types

### NavigableEntity Hierarchy
```csharp
public abstract class NavigableEntity
{
    public Vector3 Position { get; }
    public string Name { get; }
    public EntityCategory Category { get; }
    public abstract string GetDescription();
}

public class TreasureChestEntity : NavigableEntity
{
    public bool IsOpen { get; }
    public bool IsHidden { get; }
}

public class NPCEntity : NavigableEntity
{
    public bool IsShop { get; }
    public string ShopType { get; }  // Weapon, Armor, Item, Magic, Inn
}

public class MapExitEntity : NavigableEntity
{
    public string DestinationName { get; }
}

public class SavePointEntity : NavigableEntity { }

public class EventEntity : NavigableEntity
{
    public string EventType { get; }
}

public class VehicleEntity : NavigableEntity
{
    public VehicleType Type { get; }  // Ship, Canoe, Airship
}
```

### EntityCategory Enum
```csharp
public enum EntityCategory
{
    All,
    Chests,
    NPCs,
    MapExits,
    SavePoints,
    Events,
    Vehicles
}
```

---

## FF1 Game Classes

### Field Map Structure
- `FieldMap` - Root scene component
- `FieldController` - Central game state, implements `IEntityAccessor`
- `MapManager` - Map data management
- `MapModel` - Current map data, contains `allEntityGroupList`

### Entity Classes
| Entity Type | FF1 Class | Key Properties |
|-------------|-----------|----------------|
| Base | `FieldEntity` | `Position`, `Property`, `IsMoving` |
| NPCs | `FieldNonPlayer` | `CanAction`, `InteractiveIcon` |
| Chests | `FieldTresureBox` | `isOpen`, `isHiddenItem`, `tresureBoxProperty` |
| Exits | `FieldOpenTriggerEntity` | Via `PropertyGotoMap` |
| Player | `FieldPlayer` | `moveState`, position |

### Property Classes
| Property | Purpose |
|----------|---------|
| `PropertyEntity` | Base - `TilePosition`, `Pos`, `Name`, `ObjectType` |
| `PropertyNpc` | NPC metadata |
| `PropertyTresureBox` | Chest contents |
| `PropertyGotoMap` | Exit destination info |
| `PropertyTelepoPoint` | Teleport destinations |
| `PropertyEvent` | Event triggers |
| `PropertyTransportation` | Vehicle data |

---

## Entity Scanner

### EntityScanner.cs
```csharp
public class EntityScanner
{
    private List<NavigableEntity> entities = new();
    private int currentIndex = -1;
    private EntityCategory currentCategory = EntityCategory.All;
    private List<IEntityFilter> filters = new();

    private DateTime lastScanTime = DateTime.MinValue;
    private const float ScanIntervalSeconds = 5f;

    public void ScanEntities()
    {
        if ((DateTime.Now - lastScanTime).TotalSeconds < ScanIntervalSeconds)
            return;

        lastScanTime = DateTime.Now;
        entities.Clear();

        var fieldController = GetFieldController();
        if (fieldController == null) return;

        var mapModel = GetMapModel(fieldController);
        if (mapModel == null) return;

        // Scan all entity groups
        ScanTreasureChests(mapModel);
        ScanNPCs(fieldController);
        ScanMapExits(mapModel);
        ScanSavePoints(mapModel);
        ScanEvents(mapModel);
        ScanVehicles(fieldController);

        // Sort by distance from player
        SortByDistance();
    }

    public void ForceScan()
    {
        lastScanTime = DateTime.MinValue;
        ScanEntities();
    }

    public NavigableEntity NextEntity()
    {
        if (entities.Count == 0) return null;

        var filtered = GetFilteredEntities();
        if (filtered.Count == 0) return null;

        currentIndex = (currentIndex + 1) % filtered.Count;
        return filtered[currentIndex];
    }

    public NavigableEntity PreviousEntity()
    {
        if (entities.Count == 0) return null;

        var filtered = GetFilteredEntities();
        if (filtered.Count == 0) return null;

        currentIndex--;
        if (currentIndex < 0) currentIndex = filtered.Count - 1;
        return filtered[currentIndex];
    }

    public NavigableEntity CurrentEntity()
    {
        var filtered = GetFilteredEntities();
        if (currentIndex < 0 || currentIndex >= filtered.Count) return null;
        return filtered[currentIndex];
    }

    private List<NavigableEntity> GetFilteredEntities()
    {
        var result = entities.AsEnumerable();

        // Apply category filter
        if (currentCategory != EntityCategory.All)
        {
            result = result.Where(e => e.Category == currentCategory);
        }

        // Apply additional filters
        foreach (var filter in filters)
        {
            result = result.Where(e => filter.ShouldInclude(e));
        }

        return result.ToList();
    }
}
```

### Scanning Methods

```csharp
private void ScanTreasureChests(MapModel mapModel)
{
    // Access via FieldController.entityList or MapModel groups
    foreach (var entity in GetEntitiesOfType<FieldTresureBox>())
    {
        var chest = new TreasureChestEntity
        {
            Position = entity.transform.position,
            IsOpen = entity.isOpen,
            IsHidden = entity.isHiddenItem,
            Name = GetChestName(entity)
        };
        entities.Add(chest);
    }
}

private void ScanNPCs(FieldController controller)
{
    // Access via residentCharaEntityList
    var npcDict = GetResidentCharaEntityList(controller);
    foreach (var kvp in npcDict)
    {
        var npc = kvp.Value;
        if (!npc.CanAction) continue;

        var npcEntity = new NPCEntity
        {
            Position = npc.transform.position,
            Name = GetNPCName(npc),
            IsShop = IsShopNPC(npc),
            ShopType = GetShopType(npc)
        };
        entities.Add(npcEntity);
    }
}

private void ScanMapExits(MapModel mapModel)
{
    // Access via PropertyGotoMap in entityPropertyList
    foreach (var prop in GetPropertiesOfType<PropertyGotoMap>(mapModel))
    {
        var exit = new MapExitEntity
        {
            Position = prop.Pos,
            DestinationName = GetDestinationName(prop)
        };
        entities.Add(exit);
    }
}
```

---

## Filtering System

### IEntityFilter Interface
```csharp
public interface IEntityFilter
{
    bool ShouldInclude(NavigableEntity entity);
    string Name { get; }
    bool IsEnabled { get; set; }
}
```

### CategoryFilter
```csharp
public class CategoryFilter : IEntityFilter
{
    public EntityCategory Category { get; set; } = EntityCategory.All;
    public string Name => "Category";
    public bool IsEnabled { get; set; } = true;

    public bool ShouldInclude(NavigableEntity entity)
    {
        if (!IsEnabled || Category == EntityCategory.All)
            return true;
        return entity.Category == Category;
    }
}
```

### PathfindingFilter
```csharp
public class PathfindingFilter : IEntityFilter
{
    public string Name => "Reachable";
    public bool IsEnabled { get; set; } = false;

    public bool ShouldInclude(NavigableEntity entity)
    {
        if (!IsEnabled) return true;

        // Check if path exists to entity
        var path = FieldNavigationHelper.GetPathToPosition(entity.Position);
        return path != null && path.Count > 0;
    }
}
```

### MapExitFilter (Deduplication)
```csharp
public class MapExitFilter : IEntityFilter
{
    private HashSet<string> seenDestinations = new();

    public string Name => "Unique Exits";
    public bool IsEnabled { get; set; } = false;

    public void Reset() => seenDestinations.Clear();

    public bool ShouldInclude(NavigableEntity entity)
    {
        if (!IsEnabled) return true;
        if (entity is not MapExitEntity exit) return true;

        if (seenDestinations.Contains(exit.DestinationName))
            return false;

        seenDestinations.Add(exit.DestinationName);
        return true;
    }
}
```

---

## Pathfinding

### MapRouteSearcher Usage
```csharp
public static class FieldNavigationHelper
{
    public static List<Vector3> GetPathToEntity(NavigableEntity entity)
    {
        return GetPathToPosition(entity.Position);
    }

    public static List<Vector3> GetPathToPosition(Vector3 destination)
    {
        var fieldController = GameObjectCache.Get<FieldController>();
        if (fieldController == null) return null;

        var player = GetPlayer(fieldController);
        if (player == null) return null;

        var mapAccessor = fieldController as IMapAccessor;

        // Convert world positions to cell positions
        Vector3 startCell = MapRouteSearcher.EntityWorldPositionToCellPosition(
            mapAccessor, player);
        Vector3 destCell = WorldToCellPosition(destination);

        // Search for path
        try
        {
            var path = MapRouteSearcher.Search(
                mapAccessor, startCell, destCell, collisionEnabled: true);
            return path;
        }
        catch (MapRouteSearchException)
        {
            // No path found - try adjacent cells
            return TryAdjacentCells(mapAccessor, startCell, destCell);
        }
    }

    private static List<Vector3> TryAdjacentCells(
        IMapAccessor map, Vector3 start, Vector3 dest)
    {
        // Try cells adjacent to destination
        Vector3[] offsets = {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0)
        };

        foreach (var offset in offsets)
        {
            try
            {
                var path = MapRouteSearcher.Search(
                    map, start, dest + offset, true);
                if (path != null && path.Count > 0)
                    return path;
            }
            catch { }
        }
        return null;
    }

    public static string GetDirectionDescription(List<Vector3> path)
    {
        if (path == null || path.Count < 2)
            return "No path found";

        // Calculate direction from current position
        var start = path[0];
        var end = path[path.Count - 1];

        float dx = end.x - start.x;
        float dy = end.y - start.y;

        var parts = new List<string>();

        if (Math.Abs(dy) > 0.1f)
        {
            string ns = dy > 0 ? "North" : "South";
            int steps = (int)Math.Abs(dy);
            parts.Add($"{ns} {steps}");
        }

        if (Math.Abs(dx) > 0.1f)
        {
            string ew = dx > 0 ? "East" : "West";
            int steps = (int)Math.Abs(dx);
            parts.Add($"{ew} {steps}");
        }

        return string.Join(", ", parts);
    }
}
```

---

## Input Manager

### Hotkey Mapping
```csharp
public static class InputManager
{
    public static void Update()
    {
        // Entity navigation
        if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket))
            OnPreviousEntity();

        if (Input.GetKeyDown(KeyCode.K))
            OnRepeatEntity();

        if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket))
            OnNextEntity();

        // Category cycling
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            if (Input.GetKeyDown(KeyCode.J))
                OnPreviousCategory();
            if (Input.GetKeyDown(KeyCode.L))
                OnNextCategory();
            if (Input.GetKeyDown(KeyCode.K))
                OnResetCategory();
        }

        // Pathfinding
        if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Backslash))
        {
            if (Input.GetKey(KeyCode.LeftShift))
                OnTogglePathfindingFilter();
            else
                OnAnnounceWithPath();
        }

        // Category shortcuts
        if (Input.GetKeyDown(KeyCode.Alpha0))
            OnResetCategory();
        if (Input.GetKeyDown(KeyCode.Equals))
            OnNextCategory();
        if (Input.GetKeyDown(KeyCode.Minus))
            OnPreviousCategory();

        // Info hotkeys
        if (Input.GetKeyDown(KeyCode.M))
        {
            if (Input.GetKey(KeyCode.LeftShift))
                OnToggleMapExitFilter();
            else
                OnAnnounceMapName();
        }

        if (Input.GetKeyDown(KeyCode.H))
            OnAnnouncePartyStatus();

        if (Input.GetKeyDown(KeyCode.G))
            OnAnnounceGil();

        // Teleport (debug/accessibility)
        if (Input.GetKey(KeyCode.LeftControl))
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
                OnTeleport(Vector2.up);
            if (Input.GetKeyDown(KeyCode.DownArrow))
                OnTeleport(Vector2.down);
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                OnTeleport(Vector2.left);
            if (Input.GetKeyDown(KeyCode.RightArrow))
                OnTeleport(Vector2.right);
        }
    }
}
```

### Hotkey Reference
| Key | Function |
|-----|----------|
| J / [ | Previous entity |
| K | Repeat current entity |
| L / ] | Next entity |
| Shift+J | Previous category |
| Shift+L | Next category |
| Shift+K / 0 | Reset to All |
| = | Next category |
| - | Previous category |
| P / \ | Announce with pathfinding |
| Shift+P | Toggle pathfinding filter |
| M | Announce map name |
| Shift+M | Toggle map exit filter |
| H | Party HP/MP status |
| G | Current gil |
| Ctrl+Arrow | Teleport to direction of selected entity |

---

## Announcements

### Entity Announcement Format
```csharp
public static string FormatEntityAnnouncement(NavigableEntity entity, bool includePath)
{
    var parts = new List<string>();

    // Entity description
    parts.Add(entity.GetDescription());

    // Path info if requested
    if (includePath)
    {
        var path = FieldNavigationHelper.GetPathToEntity(entity);
        string direction = FieldNavigationHelper.GetDirectionDescription(path);
        parts.Add(direction);
    }

    return string.Join(". ", parts);
}
```

### Entity Descriptions
```csharp
// TreasureChestEntity
public override string GetDescription()
{
    string state = IsOpen ? "Opened" : "Unopened";
    string hidden = IsHidden ? "Hidden " : "";
    return $"{hidden}{state} Chest";
}

// NPCEntity
public override string GetDescription()
{
    if (IsShop)
        return $"{ShopType} Shop";
    return Name ?? "NPC";
}

// MapExitEntity
public override string GetDescription()
{
    return $"Exit to {DestinationName}";
}

// VehicleEntity
public override string GetDescription()
{
    return Type switch
    {
        VehicleType.Ship => "Ship",
        VehicleType.Canoe => "Canoe",
        VehicleType.Airship => "Airship",
        _ => "Vehicle"
    };
}
```

### Map Name Announcement
```csharp
private static void OnAnnounceMapName()
{
    var mapModel = GetCurrentMapModel();
    if (mapModel == null) return;

    string mapName = mapModel.GetMapName();
    // Or use MessageManager for localized name
    string localizedName = MessageManager.GetMessage(mapModel.GetTitleId());

    TolkWrapper.Speak(localizedName ?? mapName, interrupt: true);
}
```

### Party Status Announcement
```csharp
private static void OnAnnouncePartyStatus()
{
    var party = UserDataManager.Instance.GetPartyMembers();
    var sb = new StringBuilder();

    foreach (var member in party)
    {
        sb.Append($"{member.Name}: {member.CurrentHP}/{member.MaxHP} HP");
        if (member.MaxMP > 0)
            sb.Append($", {member.CurrentMP}/{member.MaxMP} MP");
        sb.Append(". ");
    }

    TolkWrapper.Speak(sb.ToString(), interrupt: true);
}
```

---

## Focus Preservation System

When the player moves, entity lists are re-sorted by distance. To prevent losing track of the selected entity, the EntityScanner maintains focus across re-sorts.

### Implementation
```csharp
// Track selected entity by identifier
private Vector3? selectedEntityPosition = null;
private EntityCategory? selectedEntityCategory = null;
private string selectedEntityName = null;

// Save identifier when index changes
public int CurrentIndex
{
    set
    {
        currentIndex = value;
        SaveSelectedEntityIdentifier();
    }
}

// Restore focus after re-sorting in ApplyFilter()
int restoredIndex = FindEntityByIdentifier();
if (restoredIndex >= 0)
{
    currentIndex = restoredIndex;
}
```

### Matching Logic
1. First, match by **position** (within 0.5 unit tolerance) + **category**
2. Fallback: match by **name** + **category** if position changed slightly

This ensures that when selecting an NPC and walking toward them, the scanner keeps that NPC selected even as the distance-sorted list changes.

---

## Treasure Chest State Detection

Treasure chests display their opened/unopened state to help players find uncollected items.

### Implementation
The `CheckIfTreasureOpened()` method checks the `FieldTresureBox.isOpen` field:
```csharp
var treasureBox = fieldEntity.TryCast<FieldTresureBox>();
if (treasureBox != null)
{
    var isOpenField = treasureBox.GetType().GetField("isOpen",
        BindingFlags.NonPublic | BindingFlags.Instance);
    if (isOpenField != null)
    {
        return (bool)isOpenField.GetValue(treasureBox);
    }
}
```

### Display Format
- "Unopened [Item Name]" - Chest has not been opened
- "Opened [Item Name]" - Chest has been opened

---

## Movement Sound Patches

### MovementSoundPatches.cs
Provide audio feedback when player moves:
```csharp
[HarmonyPatch(typeof(FieldPlayer))]
public static class MovementSoundPatches
{
    private static Vector3 lastPosition;

    [HarmonyPostfix]
    [HarmonyPatch("UpdateEntity")]
    public static void UpdateEntity_Postfix(FieldPlayer __instance)
    {
        if (__instance.IsMoving) return;

        var currentPos = __instance.transform.position;
        if (currentPos != lastPosition)
        {
            lastPosition = currentPos;
            // Optionally announce new position or play sound
            // Useful for confirming movement completed
        }
    }
}
```

---

## FF1 Specific Locations

### World Map Areas
- Cornelia region (starting area)
- Pravoka coast
- Elfheim forest
- Melmond marshlands
- Crescent Lake
- Mount Gulg area
- Ice Cavern region
- Floating Castle
- Temple of Chaos

### Vehicles
- **Ship** - Obtained after defeating pirates
- **Canoe** - From Crescent Lake sage
- **Airship** - Found in desert after class change

### Notable Entity Types
- Crystals (key story items)
- Locked doors (require keys)
- Levers/switches (dungeon puzzles)
