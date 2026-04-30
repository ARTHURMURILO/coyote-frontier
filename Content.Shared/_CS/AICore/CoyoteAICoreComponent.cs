using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._CS.AICore;

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
        "Common", "Engineering", "Medical", "Science", "Security", "Service", "Supply",
        "Handheld", "Binary", "Freelance", "Traffic"
    };

    public static readonly string[] AvailableRadioChannels =
    {
        "Common", "Command", "Engineering", "Medical", "Science", "Security",
        "Service", "Supply", "Handheld", "Binary", "Freelance", "Traffic", "Nfsd"
    };
    [DataField][AutoNetworkedField] public int MaxHistoryLength = 200;
    [DataField][AutoNetworkedField] public int MaxTokens = 128000;
    [DataField][AutoNetworkedField] public LogicChannelMode[] ChannelStates = new LogicChannelMode[20];
    [DataField] public string[] ChannelLabels = new string[20];
    [DataField][AutoNetworkedField] public string OwnerId = string.Empty;
    [DataField][AutoNetworkedField] public string OwnerName = string.Empty;
    [DataField][AutoNetworkedField] public bool IsLocked;

    [DataField][AutoNetworkedField] public bool ShowPeopleLocal = true;
    [DataField][AutoNetworkedField] public bool ShowMachinesLocal = true;
    [DataField][AutoNetworkedField] public bool ShowMachinesDetailLocal;
    [DataField][AutoNetworkedField] public bool ShowItemsLocal = true;
    [DataField][AutoNetworkedField] public bool ShowItemsDetailLocal;

    [DataField][AutoNetworkedField] public ItemVisionMode LocalItemMode = ItemVisionMode.SearchEngine;

    // Global (Camera) Vision
    [DataField][AutoNetworkedField] public bool GlobalVisionEnabled;
    [DataField][AutoNetworkedField] public bool ShowPeopleGlobal = true;
    [DataField][AutoNetworkedField] public bool ShowMachinesGlobal = true;
    [DataField][AutoNetworkedField] public bool ShowMachinesDetailGlobal;
    [DataField][AutoNetworkedField] public bool ShowItemsGlobal = true;
    [DataField][AutoNetworkedField] public bool ShowItemsDetailGlobal;
    [DataField][AutoNetworkedField] public ItemVisionMode GlobalItemMode = ItemVisionMode.SearchEngine;
    [DataField][AutoNetworkedField] public List<string> EnabledCameraSubnets = new();

    [DataField] public string LoreNotes = string.Empty;

    [DataField] public SoundSpecifier SaveSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
    [DataField][AutoNetworkedField] public float CooldownBase = 0.3f;
    [DataField][AutoNetworkedField] public float CooldownCharFactor = 0.02f;
    [DataField][AutoNetworkedField] public float CooldownMax = 4.0f;

    [DataField][AutoNetworkedField] public bool AutoContinue = false;
    [DataField][AutoNetworkedField] public int AutoContinueThreshold = 400;
    [DataField][AutoNetworkedField] public int AutoContinueMax = 2;

    // Ship Awareness
    [DataField] public string OriginalShipName = string.Empty;
    [DataField] public string ConstructionDate = string.Empty;

    // Load Tracking
    [DataField] public int LoadCount;
    [DataField] public List<string> LoadTimestamps = new();

    // Ownership History
    [DataField] public List<OwnershipRecord> OwnershipHistory = new();

    // AI Self-Lock
    [DataField][AutoNetworkedField] public bool AiLocked;

    // Memory System
    [DataField] public List<AICoreMemory> Memories = new();

    [ViewVariables] public bool HasApiKeyConfigured => !string.IsNullOrEmpty(ApiKey);
    [ViewVariables] public bool IsClaimed => !string.IsNullOrEmpty(OwnerId);

    public static readonly string[] LogicChannelNames = { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" };
}
