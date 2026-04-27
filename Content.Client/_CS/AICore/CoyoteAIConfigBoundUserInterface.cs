using Content.Shared._CS.AICore;
using Robust.Client.GameObjects;

namespace Content.Client._CS.AICore;

/// <summary>
///     BUI controller for the AI core config window.
///     Routes save/reset/lock/channel/vision messages between the menu and server.
///     One instance per open AI core config window.
/// </summary>
public sealed class CoyoteAIConfigBoundUserInterface : BoundUserInterface
{
    private CoyoteAIConfigMenu? _menu;

    public CoyoteAIConfigBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _menu = new();
        _menu.OnSave += (name, personality, endpoint, model, apiKey, temperature, reasoningLevel, lawSet, maxHistory, maxTokens, enabled, visionRange, cooldownBase, cooldownCharFactor, cooldownMax, autoContinue, autoContinueThreshold, autoContinueMax) =>
        {
            SendMessage(new CoyoteAIConfigSaveMessage(
                name, personality, endpoint, model, apiKey, temperature, reasoningLevel, lawSet, maxHistory, maxTokens, enabled, visionRange,
                cooldownBase, cooldownCharFactor, cooldownMax, autoContinue, autoContinueThreshold, autoContinueMax));
        };
        _menu.OnResetHistory += () =>
        {
            SendMessage(new CoyoteAIResetHistoryMessage());
        };
        _menu.OnSetChannel += (index, mode) =>
        {
            SendMessage(new CoyoteAISetLogicChannelMessage(index, mode));
        };
        _menu.OnToggleLock += () =>
        {
            SendMessage(new CoyoteAIToggleLockMessage());
        };
        _menu.OnUnclaim += () =>
        {
            SendMessage(new CoyoteAIUnclaimMessage());
        };
        _menu.OnSetVisionOption += (option, value) =>
        {
            SendMessage(new CoyoteAISetVisionOptionMessage(option, value));
        };
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _menu?.Close();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not CoyoteAIConfigBuiState cast) return;
        _menu?.UpdateState(cast);
    }
}
