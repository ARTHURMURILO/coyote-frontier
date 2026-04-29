using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._CS.AICore;

/// <summary>
///     Core component for the LLM-powered AI.
///     Server-authoritative: ApiKey and ApiEndpoint are never sent to clients.
///     Fields marked AutoNetworkedField are sent to the client for UI display.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CoyoteAICoreComponent : Component
{
    [DataField][AutoNetworkedField] public string CoreId = string.Empty;
    [DataField][AutoNetworkedField] public string AiName = "Cortana";
    [DataField][AutoNetworkedField] public string PersonalityPrompt = string.Empty;
    [DataField] public string ApiEndpoint = "http://localhost:1234/v1/chat/completions";
    [DataField] public string ModelName = string.Empty;
    [DataField] public string ApiKey = string.Empty;
    [DataField][AutoNetworkedField] public float Temperature = 0.7f;
    [DataField][AutoNetworkedField] public ReasoningLevel ReasoningLevel = ReasoningLevel.Max;
    [DataField][AutoNetworkedField] public string LawSet = string.Empty;
    [DataField][AutoNetworkedField] public bool Enabled = true;
    [DataField] public float LocalHearRange = 12f;
    [DataField][AutoNetworkedField] public float VisionRange = 15f;
    [DataField][AutoNetworkedField] public HashSet<string> RadioChannels = new()
    {
        "Common", "Command", "Engineering", "Medical", "Science", "Security", "Service", "Supply"
    };
    [DataField][AutoNetworkedField] public int MaxHistoryLength = 30;
    [DataField][AutoNetworkedField] public int MaxTokens = 128000;
    [DataField][AutoNetworkedField] public LogicChannelMode[] ChannelStates = new LogicChannelMode[10];
    [DataField][AutoNetworkedField] public string OwnerId = string.Empty;
    [DataField][AutoNetworkedField] public string OwnerName = string.Empty;
    [DataField][AutoNetworkedField] public bool IsLocked;

    [DataField][AutoNetworkedField] public bool ShowPeople = true;
    [DataField][AutoNetworkedField] public bool ShowMachines = true;
    [DataField][AutoNetworkedField] public bool ShowMachinesDetail;
    [DataField][AutoNetworkedField] public bool ShowItems = true;
    [DataField][AutoNetworkedField] public bool ShowItemsDetail;

    [DataField] public string LoreNotes = string.Empty;

    [DataField] public SoundSpecifier SaveSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
    [DataField][AutoNetworkedField] public float CooldownBase = 0.3f;
    [DataField][AutoNetworkedField] public float CooldownCharFactor = 0.02f;
    [DataField][AutoNetworkedField] public float CooldownMax = 4.0f;

    [DataField][AutoNetworkedField] public bool AutoContinue = false;
    [DataField][AutoNetworkedField] public int AutoContinueThreshold = 400;
    [DataField][AutoNetworkedField] public int AutoContinueMax = 2;

    [ViewVariables] public bool HasApiKeyConfigured => !string.IsNullOrEmpty(ApiKey);
    [ViewVariables] public bool IsClaimed => !string.IsNullOrEmpty(OwnerId);

    public static readonly string[] LogicChannelNames = { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
}
