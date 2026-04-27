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

    public float VisionRange;

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
        float visionRange = 15f)
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
        VisionRange = visionRange;
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

    public CoyoteAIConfigSaveMessage(string aiName, string personalityPrompt, string apiEndpoint,
        string modelName, string apiKey, float temperature, ReasoningLevel reasoningLevel,
        string lawSet, int maxHistory, int maxTokens, bool enabled, float visionRange = 15f)
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
}
