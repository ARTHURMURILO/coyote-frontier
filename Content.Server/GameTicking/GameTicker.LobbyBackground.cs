using Content.Server.GameTicking.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [ViewVariables]
    public string? LobbyBackground { get; private set; }

    [ViewVariables]
    private List<ResPath>? _lobbyBackgrounds;

    private static readonly string[] WhitelistedBackgroundExtensions = ["png", "jpg", "jpeg", "webp"];

    private void InitializeLobbyBackground()
    {
        _lobbyBackgrounds = _prototypeManager.EnumeratePrototypes<LobbyBackgroundPrototype>()
            .Select(x => x.Background)
            .Where(x => WhitelistedBackgroundExtensions.Contains(x.Extension))
            .ToList();
        Timer.SpawnRepeating(30000, CycleLobbyBackground, System.Threading.CancellationToken.None);
        RandomizeLobbyBackground();
    }

    private void CycleLobbyBackground()
    {
        if (_lobbyBackgrounds == null || _lobbyBackgrounds.Count == 0)
            return;

        RandomizeLobbyBackground();
        SendStatusToAll();
    }

    private void RandomizeLobbyBackground()
    {
        LobbyBackground = _lobbyBackgrounds!.Count != 0 ? _robustRandom.Pick(_lobbyBackgrounds!).ToString() : null;
    }
}
