using System.Text.Json.Serialization;
using Robust.Shared.Serialization;

namespace Content.Shared._CS.AICore;

[Serializable, NetSerializable]
public enum CoyoteAICoreUiKey : byte
{
    Config,
    Logic,
}

[Serializable, NetSerializable]
public enum LogicChannelMode : byte
{
    Off,
    On,
    Pulse,
}

[Serializable, NetSerializable]
public enum ReasoningLevel : byte
{
    Off,
    Low,
    Medium,
    High,
    Max,
}

[Serializable, NetSerializable]
public enum ItemVisionMode : byte
{
    FeedAll,
    SmartSummary,
    SearchEngine,
}

[Serializable, NetSerializable]
public enum MemoryPriority : byte
{
    Low,
    Normal,
    High,
    Critical,
}

[Serializable, NetSerializable]
public sealed class CoyoteAIConfigBuiState : BoundUserInterfaceState
{
    public string AiName;
    public string PersonalityPrompt;
    public string ApiEndpoint;
    public string ModelName;
    public float Temperature;
    public bool HasApiKey;
    public ReasoningLevel ReasoningLevelData;
    public string LawSet;
    public string[] AvailableLawSets;
    public int MaxHistory;
    public int MaxTokens;
    public bool Enabled;
    public int HistoryLength;
    public int EstimatedTokens;
    public int TotalEstimatedTokens;

    public LogicChannelMode[] ChannelStates;
    public string OwnerName;
    public bool IsLocked;
    public bool IsClaimed;
    public bool LockedView;
    public bool RefreshOnly;

    public bool ShowPeople = true;
    public bool ShowMachines = true;
    public bool ShowMachinesDetail;
    public bool ShowItems = true;
    public bool ShowItemsDetail;
    public ItemVisionMode ItemMode = ItemVisionMode.SearchEngine;

    public float VisionRange;

    public int TokenSystem;
    public int TokenPersonLore;
    public int TokenCrewXeno;
    public int TokenVision;
    public int TokenHistory;
    public int TokenContext;

    public float CooldownBase;
    public float CooldownCharFactor;
    public float CooldownMax;
    public bool AutoContinue;
    public int AutoContinueThreshold;
    public int AutoContinueMax;

    // Ship & Load awareness
    public string CurrentShipName = string.Empty;
    public string OriginalShipName = string.Empty;
    public string ConstructionDate = string.Empty;
    public int LoadCount;
    public string LoadTimestampsDisplay = string.Empty;

    // Ownership history
    public List<OwnershipRecord> OwnershipHistory = new();

    // AI Self-lock
    public bool AiLocked;

    // Memories
    public List<AICoreMemory> Memories = new();

    // Export
    public string ExportYaml = string.Empty;

    public CoyoteAIConfigBuiState(string aiName, string personalityPrompt, string apiEndpoint,
        string modelName, float temperature, bool hasApiKey,
        ReasoningLevel reasoningLevel, string lawSet, string[] availableLawSets,
        int maxHistory, int maxTokens,
        bool enabled, int historyLength, int estimatedTokens, int totalEstimatedTokens,
        LogicChannelMode[]? channelStates = null,
        string ownerName = "", bool isLocked = false, bool isClaimed = false,
        bool lockedView = false,
        bool showPeople = true, bool showMachines = true, bool showMachinesDetail = false,
        bool showItems = true, bool showItemsDetail = false,
        ItemVisionMode itemMode = ItemVisionMode.SearchEngine,
        float visionRange = 15f,
        int tokenSystem = 0, int tokenPersonLore = 0, int tokenCrewXeno = 0,
        int tokenVision = 0, int tokenHistory = 0, int tokenContext = 0,
        float cooldownBase = 0.3f, float cooldownCharFactor = 0.02f, float cooldownMax = 4f,
        bool autoContinue = false, int autoContinueThreshold = 400, int autoContinueMax = 2,
        string currentShipName = "", string originalShipName = "", string constructionDate = "",
        int loadCount = 0, string loadTimestampsDisplay = "",
        List<OwnershipRecord>? ownershipHistory = null,
        bool aiLocked = false,
        List<AICoreMemory>? memories = null,
        string exportYaml = "")
    {
        AiName = aiName;
        PersonalityPrompt = personalityPrompt;
        ApiEndpoint = apiEndpoint;
        ModelName = modelName;
        Temperature = temperature;
        HasApiKey = hasApiKey;
        ReasoningLevelData = reasoningLevel;
        LawSet = lawSet;
        AvailableLawSets = availableLawSets;
        MaxHistory = maxHistory;
        MaxTokens = maxTokens;
        Enabled = enabled;
        HistoryLength = historyLength;
        EstimatedTokens = estimatedTokens;
        TotalEstimatedTokens = totalEstimatedTokens;
        ChannelStates = channelStates ?? new LogicChannelMode[10];
        OwnerName = ownerName;
        IsLocked = isLocked;
        IsClaimed = isClaimed;
        LockedView = lockedView;
        ShowPeople = showPeople;
        ShowMachines = showMachines;
        ShowMachinesDetail = showMachinesDetail;
        ShowItems = showItems;
        ShowItemsDetail = showItemsDetail;
        ItemMode = itemMode;
        VisionRange = visionRange;
        TokenSystem = tokenSystem;
        TokenPersonLore = tokenPersonLore;
        TokenCrewXeno = tokenCrewXeno;
        TokenVision = tokenVision;
        TokenHistory = tokenHistory;
        TokenContext = tokenContext;
        CooldownBase = cooldownBase;
        CooldownCharFactor = cooldownCharFactor;
        CooldownMax = cooldownMax;
        AutoContinue = autoContinue;
        AutoContinueThreshold = autoContinueThreshold;
        AutoContinueMax = autoContinueMax;
        CurrentShipName = currentShipName;
        OriginalShipName = originalShipName;
        ConstructionDate = constructionDate;
        LoadCount = loadCount;
        LoadTimestampsDisplay = loadTimestampsDisplay;
        OwnershipHistory = ownershipHistory ?? new();
        AiLocked = aiLocked;
        Memories = memories ?? new();
        ExportYaml = exportYaml;
        RefreshOnly = false;
    }
}

[Serializable, NetSerializable]
public sealed class CoyoteAIConfigSaveMessage : BoundUserInterfaceMessage
{
    public string AiName;
    public string PersonalityPrompt;
    public string ApiEndpoint;
    public string ModelName;
    public string ApiKey;
    public float Temperature;
    public ReasoningLevel ReasoningLevelData;
    public string LawSet;
    public int MaxHistory;
    public int MaxTokens;
    public bool Enabled;
    public float VisionRange;
    public float CooldownBase;
    public float CooldownCharFactor;
    public float CooldownMax;
    public bool AutoContinue;
    public int AutoContinueThreshold;
    public int AutoContinueMax;
    public List<AICoreMemory>? Memories;
    public ItemVisionMode ItemMode = ItemVisionMode.SearchEngine;

    public CoyoteAIConfigSaveMessage(string aiName, string personalityPrompt, string apiEndpoint,
        string modelName, string apiKey, float temperature, ReasoningLevel reasoningLevel,
        string lawSet, int maxHistory, int maxTokens, bool enabled, float visionRange = 15f,
        float cooldownBase = 0.3f, float cooldownCharFactor = 0.02f, float cooldownMax = 4f,
        bool autoContinue = false, int autoContinueThreshold = 400, int autoContinueMax = 2,
        List<AICoreMemory>? memories = null,
        ItemVisionMode itemMode = ItemVisionMode.SearchEngine)
    {
        AiName = aiName;
        PersonalityPrompt = personalityPrompt;
        ApiEndpoint = apiEndpoint;
        ModelName = modelName;
        ApiKey = apiKey;
        Temperature = temperature;
        ReasoningLevelData = reasoningLevel;
        LawSet = lawSet;
        MaxHistory = maxHistory;
        MaxTokens = maxTokens;
        Enabled = enabled;
        VisionRange = visionRange;
        CooldownBase = cooldownBase;
        CooldownCharFactor = cooldownCharFactor;
        CooldownMax = cooldownMax;
        AutoContinue = autoContinue;
        AutoContinueThreshold = autoContinueThreshold;
        AutoContinueMax = autoContinueMax;
        Memories = memories;
        ItemMode = itemMode;
    }
}

[Serializable, NetSerializable]
public sealed class CoyoteAIResetHistoryMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CoyoteAIToggleLockMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CoyoteAIUnclaimMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CoyoteAISetVisionOptionMessage : BoundUserInterfaceMessage
{
    public string Option;
    public bool Value;

    public CoyoteAISetVisionOptionMessage(string option, bool value)
    {
        Option = option;
        Value = value;
    }
}

[Serializable, NetSerializable]
public sealed class CoyoteAISetItemModeMessage : BoundUserInterfaceMessage
{
    public ItemVisionMode Mode;

    public CoyoteAISetItemModeMessage(ItemVisionMode mode)
    {
        Mode = mode;
    }
}

[Serializable, NetSerializable]
public sealed class CoyoteAISetLogicChannelMessage : BoundUserInterfaceMessage
{
    public int ChannelIndex;
    public LogicChannelMode Mode;

    public CoyoteAISetLogicChannelMessage(int index, LogicChannelMode mode)
    {
        ChannelIndex = index;
        Mode = mode;
    }
}

// Export / Import
[Serializable, NetSerializable]
public sealed class CoyoteAIExportMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CoyoteAIImportMessage : BoundUserInterfaceMessage
{
    public string DataJson = string.Empty;
}

[Serializable, NetSerializable]
public sealed class CoyoteAISetEnabledMessage : BoundUserInterfaceMessage
{
    public bool Enabled;
}

// Memory CRUD
[Serializable, NetSerializable]
public sealed class CoyoteAIAddMemoryMessage : BoundUserInterfaceMessage
{
    public string Content = string.Empty;
    public MemoryPriority Priority;
    public List<string> Tags = new();
}

[Serializable, NetSerializable]
public sealed class CoyoteAIRemoveMemoryMessage : BoundUserInterfaceMessage
{
    public string MemoryId = string.Empty;
}

public sealed class ChatEntry
{
    public string Type = string.Empty;
    public string? Channel;
    public string SpeakerName = string.Empty;
    public string SpeakerSpecies = string.Empty;
    public string SpeakerJob = string.Empty;
    public int SpeakerAge;
    public string Message = string.Empty;
    public TimeSpan Timestamp;
    public string SpeakerContext = string.Empty;
}

public sealed class LLMResponse
{
    [JsonPropertyName("should_respond")]
    public bool ShouldRespond;
    [JsonPropertyName("channel")]
    public string? Channel;
    [JsonPropertyName("message")]
    public string Message = string.Empty;
    [JsonPropertyName("continue")]
    public bool Continue;
    [JsonPropertyName("delay")]
    public int Delay;
    [JsonPropertyName("action")]
    public string? Action;
    [JsonPropertyName("action_channel")]
    public string? ActionChannel;
    [JsonPropertyName("point_at")]
    public string? PointAt;
    [JsonPropertyName("target")]
    public string? Target;
    [JsonPropertyName("memory_add")]
    public string? MemoryAdd;
    [JsonPropertyName("memory_add_priority")]
    public string? MemoryAddPriority;
    [JsonPropertyName("memory_add_tags")]
    public List<string>? MemoryAddTags;
    [JsonPropertyName("memory_remove")]
    public string? MemoryRemove;
    [JsonPropertyName("memory_clear")]
    public bool MemoryClear;
    [JsonPropertyName("memory_clear_confirm")]
    public bool MemoryClearConfirm;
    [JsonPropertyName("query_entity")]
    public string? QueryEntity;
    [JsonPropertyName("search_entity")]
    public string? SearchEntity;
    [JsonPropertyName("select_entity")]
    public string? SelectEntity;
}

[Serializable, NetSerializable]
public sealed class OwnershipRecord
{
    public string OwnerName = string.Empty;
    public string ClaimedAt = string.Empty;
    public string? UnclaimedAt;
}

[Serializable, NetSerializable]
public sealed class AICoreMemory
{
    public string Id = string.Empty;
    public string Content = string.Empty;
    public MemoryPriority Priority = MemoryPriority.Normal;
    public List<string> Tags = new();
    public string CreatedAt = string.Empty;
    public string LastAccessedAt = string.Empty;
}

public sealed class AICoreExportData
{
    public int Version = 1;
    public string ForkId = string.Empty;
    public string CoreId = string.Empty;
    public string AiName = string.Empty;
    public string PersonalityPrompt = string.Empty;
    public string LoreNotes = string.Empty;
    public float Temperature = 0.7f;
    public ReasoningLevel ReasoningLevel = ReasoningLevel.Max;
    public string LawSet = string.Empty;
    public bool Enabled = true;
    public int MaxHistoryLength = 200;
    public int MaxTokens = 128000;
    public bool ShowPeople = true;
    public bool ShowMachines = true;
    public bool ShowMachinesDetail;
    public bool ShowItems = true;
    public bool ShowItemsDetail;
    public float VisionRange = 15f;
    public float CooldownBase = 0.3f;
    public float CooldownCharFactor = 0.02f;
    public float CooldownMax = 4.0f;
    public bool AutoContinue;
    public int AutoContinueThreshold = 400;
    public int AutoContinueMax = 2;
    public int LoadCount;
    public List<string> LoadTimestamps = new();
    public string OriginalShipName = string.Empty;
    public string ConstructionDate = string.Empty;
    public List<OwnershipRecord> OwnershipHistory = new();
    public List<ChatEntry> ConversationHistory = new();
    public List<AICoreMemory> Memories = new();
}

// Server→Client event for export response
[Serializable, NetSerializable]
public sealed class CoyoteAIExportResponseEvent : EntityEventArgs
{
    public string Yaml = string.Empty;
}
