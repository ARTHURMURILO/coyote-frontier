using System.IO;
using Content.Shared._CS.AICore;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;

namespace Content.Client._CS.AICore;

public sealed class CoyoteAIConfigBoundUserInterface : BoundUserInterface
{
    private readonly IFileDialogManager _dialogManager = IoCManager.Resolve<IFileDialogManager>();

    private CoyoteAIConfigMenu? _menu;

    public CoyoteAIConfigBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _menu = new();
        _menu.OnSave += (name, personality, endpoint, model, apiKey, temperature, reasoningLevel, lawSet, maxHistory, maxTokens, enabled, visionRange, cooldownBase, cooldownCharFactor, cooldownMax, autoContinue, autoContinueThreshold, autoContinueMax, memories, localItemMode, channelLabels) =>
        {
            SendMessage(new CoyoteAIConfigSaveMessage(
                name, personality, endpoint, model, apiKey, temperature, reasoningLevel, lawSet, maxHistory, maxTokens, enabled, visionRange,
                cooldownBase, cooldownCharFactor, cooldownMax, autoContinue, autoContinueThreshold, autoContinueMax,
                memories, localItemMode, channelLabels));
        };
        _menu.OnResetHistory += () =>
        {
            SendMessage(new CoyoteAIResetHistoryMessage());
        };
        _menu.OnSetChannel += (index, mode) =>
        {
            SendMessage(new CoyoteAISetLogicChannelMessage(index, mode));
        };
        _menu.OnSetChannelLabel += (index, label) =>
        {
            SendMessage(new CoyoteAISetChannelLabelMessage(index, label));
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
        _menu.OnSetItemMode += (mode) =>
        {
            SendMessage(new CoyoteAISetItemModeMessage(mode));
        };
        _menu.OnSetGlobalVisionOption += (option, value) =>
        {
            SendMessage(new CoyoteAISetGlobalVisionOptionMessage(option, value));
        };
        _menu.OnSetGlobalItemMode += (mode) =>
        {
            SendMessage(new CoyoteAISetGlobalItemModeMessage(mode));
        };
        _menu.OnSetCameraSubnet += (subnetId, enabled) =>
        {
            SendMessage(new CoyoteAISetCameraSubnetMessage(subnetId, enabled));
        };
        _menu.OnSetEnabled += (enabled) =>
        {
            SendMessage(new CoyoteAISetEnabledMessage { Enabled = enabled });
        };
        _menu.OnSetRadioChannel += (channelId, enabled) =>
        {
            SendMessage(new CoyoteAISetRadioChannelMessage(channelId, enabled));
        };
        _menu.OnExport += () =>
        {
            SendMessage(new CoyoteAIExportMessage());
        };
        _menu.OnImport += async (_) =>
        {
            await using var file = await _dialogManager.OpenFile(new FileDialogFilters(new FileDialogFilters.Group("json")));
            if (file == null) return;
            using var reader = new StreamReader(file);
            var json = await reader.ReadToEndAsync();
            SendMessage(new CoyoteAIImportMessage { DataJson = json });
        };
        _menu.OnAddMemory += (content, priority, tags) =>
        {
            SendMessage(new CoyoteAIAddMemoryMessage { Content = content, Priority = priority, Tags = tags });
        };
        _menu.OnRemoveMemory += (memoryId) =>
        {
            SendMessage(new CoyoteAIRemoveMemoryMessage { MemoryId = memoryId });
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
