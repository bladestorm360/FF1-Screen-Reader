# Dialogue & Text Accessibility Implementation

## Priority Order

1. Scrolling Intro Text (highest - first thing players encounter)
2. Message Windows (NPC dialogue)
3. Speaker Names
4. System Messages
5. Choice/Selection Windows

---

## Scrolling Intro Text

### Overview
The opening crawl text that plays when starting a new game. Must be readable and skippable.

### Classes
- `FadeMessageManager` (Last.Message) - Fade-in message display
- `LineFadeMessageManager` - Line-by-line fade
- `ScrollMessageManager` - Scrolling text
- `MessageParentControler` - Parent control

### Workaround Required
**Must use manual patching** - These managers handle string content.

```csharp
// ScrollMessagePatches.cs
public static void ApplyPatches(Harmony harmony)
{
    // FadeMessageManager.Play
    Type fadeType = FindType("Il2CppLast.Message.FadeMessageManager");
    if (fadeType != null)
    {
        var playMethod = AccessTools.Method(fadeType, "Play");
        harmony.Patch(playMethod, postfix: new HarmonyMethod(
            typeof(ScrollMessagePatches), "FadeManagerPlay_Postfix"));
    }

    // LineFadeMessageManager.Play
    Type lineType = FindType("Il2CppLast.Message.LineFadeMessageManager");
    if (lineType != null)
    {
        var playMethod = AccessTools.Method(lineType, "Play");
        harmony.Patch(playMethod, postfix: new HarmonyMethod(
            typeof(ScrollMessagePatches), "LineFadeManagerPlay_Postfix"));
    }

    // ScrollMessageManager
    Type scrollType = FindType("Il2CppLast.Message.ScrollMessageManager");
    if (scrollType != null)
    {
        var playMethod = AccessTools.Method(scrollType, "Play");
        harmony.Patch(playMethod, postfix: new HarmonyMethod(
            typeof(ScrollMessagePatches), "ScrollManagerPlay_Postfix"));
    }
}
```

### Extracting Text Content

```csharp
// Use positional parameters to avoid string crashes
public static void FadeManagerPlay_Postfix(object __instance)
{
    try
    {
        // Access content via reflection
        var contentField = AccessTools.Field(__instance.GetType(), "content");
        var contentProp = AccessTools.Property(__instance.GetType(), "content");

        object content = contentProp?.GetValue(__instance)
                      ?? contentField?.GetValue(__instance);

        if (content != null)
        {
            string text = ExtractTextFromContent(content);
            if (!string.IsNullOrEmpty(text))
            {
                TolkWrapper.Speak(TextUtils.StripIcons(text), interrupt: false);
            }
        }
    }
    catch (Exception ex)
    {
        MelonLogger.Warning($"Error reading scroll text: {ex.Message}");
    }
}

private static string ExtractTextFromContent(object content)
{
    // Content may be List<BaseContent> or similar
    // Walk the structure to find text strings
    if (content is IEnumerable enumerable)
    {
        var sb = new StringBuilder();
        foreach (var item in enumerable)
        {
            var textProp = AccessTools.Property(item.GetType(), "text");
            var textField = AccessTools.Field(item.GetType(), "text");
            string text = textProp?.GetValue(item)?.ToString()
                       ?? textField?.GetValue(item)?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine(text);
            }
        }
        return sb.ToString();
    }
    return content.ToString();
}
```

### FF1 Intro Content
The opening scroll explains:
- The world falling into darkness
- The four crystals losing power
- The prophecy of the Light Warriors
- The quest beginning

---

## Message Windows (NPC Dialogue)

### Classes
- `MessageWindowManager` (Singleton) - Main dialogue system
- `MessageMultipleWindowManager` - Multiple concurrent windows
- `MessageSelectWindowManager` - Selection dialogs
- `MessageChoiceWindowManager` - Choice prompts
- `MessageWindowController` - Window control

### Key Methods (MessageWindowManager)
```csharp
public class MessageWindowManager : Singleton
{
    // Content methods - TARGET FOR PATCHING
    public void SetContent(List<BaseContent> content);
    public void Play(bool isAuto);

    // Speaker methods
    public void SetSpeker(string name);       // Note: typo in original
    public void SetSpekerColor(Color32 color);
    public void SetSpeakerAsset(string asset);

    // State queries
    public bool IsOpen();
    public bool IsEndWaiting();
    public bool IsCompleteMessage();
    public bool IsNewPageInputWaiting();

    // Window control
    public void ShowWindow(bool show);
    public void Close();
    public void Finish();
}
```

### Workaround Required
**Must use manual patching** for `SetContent` and speaker methods.

```csharp
// MessageWindowPatches.cs (called from TryManualPatching)
public static void ApplyPatches(Harmony harmony)
{
    Type mwmType = FindType("MessageWindowManager");
    if (mwmType == null) return;

    // Patch SetContent(List<BaseContent>)
    var setContentMethod = AccessTools.Method(mwmType, "SetContent");
    if (setContentMethod != null)
    {
        harmony.Patch(setContentMethod, postfix: new HarmonyMethod(
            typeof(MessageWindowPatches), "SetContent_Postfix"));
    }

    // Patch Play(bool)
    var playMethod = AccessTools.Method(mwmType, "Play");
    if (playMethod != null)
    {
        harmony.Patch(playMethod, postfix: new HarmonyMethod(
            typeof(MessageWindowPatches), "Play_Postfix"));
    }
}
```

### Implementation

```csharp
private static string lastSpokenMessage = "";
private static string currentSpeaker = "";

public static void SetContent_Postfix(object __instance, object __0)
{
    try
    {
        // __0 is List<BaseContent>
        string text = ExtractDialogueText(__0);
        if (string.IsNullOrEmpty(text)) return;

        // Deduplicate
        if (text == lastSpokenMessage) return;
        lastSpokenMessage = text;

        // Speak with speaker prefix if available
        string announcement = string.IsNullOrEmpty(currentSpeaker)
            ? text
            : $"{currentSpeaker}: {text}";

        TolkWrapper.Speak(TextUtils.StripIcons(announcement), interrupt: false);
    }
    catch (Exception ex)
    {
        MelonLogger.Warning($"Error in SetContent patch: {ex.Message}");
    }
}

public static void Play_Postfix(object __instance)
{
    try
    {
        // Extract speaker name from instance
        var speakerField = AccessTools.Field(__instance.GetType(), "spekerValue");
        var speakerProp = AccessTools.Property(__instance.GetType(), "spekerValue");

        string speaker = speakerProp?.GetValue(__instance)?.ToString()
                      ?? speakerField?.GetValue(__instance)?.ToString();

        if (!string.IsNullOrEmpty(speaker) && speaker != currentSpeaker)
        {
            currentSpeaker = speaker;
            // Optionally announce speaker change
            // TolkWrapper.Speak(speaker, interrupt: false);
        }
    }
    catch { }
}
```

### Deduplication Logic
Prevent same message from being spoken multiple times:
```csharp
private static string lastMessage = "";
private static DateTime lastMessageTime = DateTime.MinValue;

private static bool ShouldSpeak(string message)
{
    if (message == lastMessage &&
        (DateTime.Now - lastMessageTime).TotalMilliseconds < 500)
    {
        return false;
    }
    lastMessage = message;
    lastMessageTime = DateTime.Now;
    return true;
}
```

---

## Speaker Names

### Extraction Methods

```csharp
// From MessageWindowManager
public static string GetCurrentSpeaker(object manager)
{
    // Try spekerValue field (note typo in game code)
    var field = AccessTools.Field(manager.GetType(), "spekerValue");
    if (field != null)
    {
        return field.GetValue(manager)?.ToString();
    }

    // Try property
    var prop = AccessTools.Property(manager.GetType(), "spekerValue");
    return prop?.GetValue(manager)?.ToString();
}
```

### FF1 Speaker Names
- Named NPCs (King, Princess Sarah, Garland, etc.)
- Generic NPCs (Townsperson, Merchant, Soldier)
- Party members in cutscenes

---

## System Messages

### Classes
- `SystemMessageWindowManager` (Singleton) - System prompts
- `CommonPopup` - Generic popup dialogs

### Common System Messages
- "Received [Item]!"
- "Not enough Gil"
- "Inventory is full"
- "Cannot equip this item"
- "Save complete"
- "Load complete"

### Implementation
```csharp
// Patch SystemMessageWindowManager similar to MessageWindowManager
// OR hook into CommonPopup

[HarmonyPatch(typeof(CommonPopup))]
public static class PopupPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("Open")]
    public static void Open_Postfix(CommonPopup __instance)
    {
        string title = __instance.Title;
        string message = __instance.Message;

        string announcement = string.IsNullOrEmpty(title)
            ? message
            : $"{title}: {message}";

        TolkWrapper.Speak(announcement, interrupt: true);
    }
}
```

---

## Choice/Selection Windows

### Classes
- `MessageSelectWindowManager` - Yes/No and multi-choice
- `MessageChoiceWindowManager` - Choice display

### Patch Points
```csharp
// Selection cursor movement
// Choice confirmation
// Choice cancellation
```

### Implementation
```csharp
// Announce choices as cursor moves
public static void ChoiceCursor_Postfix(int index, /* choice data */)
{
    string choice = GetChoiceText(index);
    TolkWrapper.Speak(choice, interrupt: true);
}
```

### Common Choices in FF1
- Yes / No
- Buy / Sell / Exit (shops)
- Save slot selection
- Target selection (items/magic)

---

## Text Processing Utilities

### TextUtils.cs
```csharp
public static class TextUtils
{
    // Strip icon markup from game text
    // Format: <icon=name> or similar
    private static readonly Regex IconRegex = new Regex(
        @"<[^>]+>",
        RegexOptions.Compiled);

    public static string StripIcons(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return IconRegex.Replace(text, "").Trim();
    }

    // Clean up whitespace
    public static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    // Remove line breaks for single-line speech
    public static string SingleLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace("\n", " ").Replace("\r", "");
    }
}
```

---

## Speech Queue Management

### SpeechHelper.cs
```csharp
public static class SpeechHelper
{
    public static IEnumerator DelayedSpeech(string text, bool interrupt, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }

        if (!string.IsNullOrEmpty(text))
        {
            TolkWrapper.Speak(text, interrupt);
        }
    }

    public static void SpeakDelayed(string text, bool interrupt = true)
    {
        CoroutineManager.Start(DelayedSpeech(text, interrupt));
    }
}
```

### When to Interrupt vs Queue
- **Interrupt (true):** User actions (menu navigation, hotkeys)
- **Queue (false):** Game events (dialogue, battle messages, system notifications)

---

## Localization Notes

### MessageManager
The game uses `MessageManager` for localized text lookup:
```csharp
// Get localized string by ID
string text = MessageManager.GetMessage(messageId);
```

### Language Support
FF1 PR supports:
- English, Japanese, French, German, Italian, Spanish
- Portuguese, Russian, Korean, Chinese (Simplified/Traditional), Thai

Screen reader output uses the game's current language setting automatically.
