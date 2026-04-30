using System.IO;
using System.Text.Json;
using Content.Shared._CS.AICore;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;

namespace Content.Client._CS.AICore;

public sealed class CoyoteAICoreSystem : EntitySystem
{
    private readonly IFileDialogManager _dialogManager = IoCManager.Resolve<IFileDialogManager>();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CoyoteAIExportResponseEvent>(OnExportResponse);
    }

    private async void OnExportResponse(CoyoteAIExportResponseEvent args)
    {
        var file = await _dialogManager.SaveFile(new FileDialogFilters(new FileDialogFilters.Group("json")));
        if (file == null) return;
        await using var writer = new StreamWriter(file.Value.fileStream);
        await writer.WriteAsync(args.Yaml);
    }
}
