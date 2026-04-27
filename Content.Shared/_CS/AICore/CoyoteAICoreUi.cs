using System.Text.Json.Serialization;
using Robust.Shared.Serialization;

namespace Content.Shared._CS.AICore;

/// <summary>
///     UI keys for the AI core config and logic panels.
///     Config is the main full/locked BUI view; Logic reserved for future use.
/// </summary>
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

/// <summary>
///     BUI state sent from server to client on every UI refresh.
///     <see cref="RefreshOnly"/> controls whether the client overwrites
///     text-field contents — set to true for periodic/checkbox updates
///     so the user's unsaved edits are preserved.
/// </summary>
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
        float visionRange = 15f,
        int tokenSystem = 0, int tokenPersonLore = 0, int tokenCrewXeno = 0,
        int tokenVision = 0, int tokenHistory = 0, int tokenContext = 0,
        float cooldownBase = 0.3f, float cooldownCharFactor = 0.02f, float cooldownMax = 4f,
        bool autoContinue = false, int autoContinueThreshold = 400, int autoContinueMax = 2)
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
        RefreshOnly = false;
    }
}

/// <summary>
///     Save-message sent from client when the user clicks "Save Configuration".
///     ApiKey is only written server-side if non-empty (empty = preserve existing key).
/// </summary>
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

    public CoyoteAIConfigSaveMessage(string aiName, string personalityPrompt, string apiEndpoint,
        string modelName, string apiKey, float temperature, ReasoningLevel reasoningLevel,
        string lawSet, int maxHistory, int maxTokens, bool enabled, float visionRange = 15f,
        float cooldownBase = 0.3f, float cooldownCharFactor = 0.02f, float cooldownMax = 4f,
        bool autoContinue = false, int autoContinueThreshold = 400, int autoContinueMax = 2)
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

/// <summary>
///     Toggles a vision-filter option (ShowPeople, ShowMachines, etc.).
///     Sent on checkbox change; processed server-side with refreshOnly=true
///     so unsaved text fields are not overwritten.
/// </summary>
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

/// <summary>
///     Sets a logic channel to Off/On/Pulse.
///     Triggered by the three-button row in the UI.
/// </summary>
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

/// <summary>
///     A single entry in the conversation history.
///     Type is one of: "radio", "local", "emote", "followup".
///     SpeakerContext describes what the speaker is doing/holding.
/// </summary>
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

/// <summary>
///     Deserialized LLM JSON response.
///     All fields use snake_case JsonPropertyName to match OpenAI-compatible API output.
/// </summary>
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
