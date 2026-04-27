using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client.Audio;

public sealed class ClientGlobalSoundSystem : SharedGlobalSoundSystem
{
    private const string PocketSizedAndyFolderSegment = "/PocketSizedAndy/";
    private static readonly ResolvedPathSpecifier AndyAnnouncementFallback = new("/Audio/Announcements/announce.ogg");

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    // Admin music
    private bool _adminAudioEnabled = true;
    private List<EntityUid?> _adminAudio = new(1);

    // Event sounds (e.g. nuke timer)
    private bool _eventAudioEnabled = true;
    private Dictionary<StationEventMusicType, EntityUid?> _eventAudio = new(1);

    // Andy announcements
    private bool _andyAudioEnabled = true;
    private List<EntityUid?> _andyAudio = new(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeNetworkEvent<AdminSoundEvent>(PlayAdminSound);
        Subs.CVar(_cfg, CCVars.AdminSoundsEnabled, ToggleAdminSound, true);

        SubscribeNetworkEvent<StationEventMusicEvent>(PlayStationEventMusic);
        SubscribeNetworkEvent<StopStationEventMusic>(StopStationEventMusic);
        Subs.CVar(_cfg, CCVars.EventMusicEnabled, ToggleStationEventMusic, true);

        Subs.CVar(_cfg, CCVars.AndyAnnouncementsEnabled, ToggleAndyAnnouncements, true);

        SubscribeNetworkEvent<GameGlobalSoundEvent>(PlayGameSound);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        ClearAudio();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ClearAudio();
    }

    private void ClearAudio()
    {
        foreach (var stream in _adminAudio)
        {
            _audio.Stop(stream);
        }
        _adminAudio.Clear();

        foreach (var stream in _eventAudio.Values)
        {
            _audio.Stop(stream);
        }

        _eventAudio.Clear();

        foreach (var stream in _andyAudio)
        {
            _audio.Stop(stream);
        }

        _andyAudio.Clear();
    }

    private void PlayAdminSound(AdminSoundEvent soundEvent)
    {
        if(!_adminAudioEnabled) return;

        var stream = _audio.PlayGlobal(soundEvent.Specifier, Filter.Local(), false, soundEvent.AudioParams);
        _adminAudio.Add(stream?.Entity);
    }

    private void PlayStationEventMusic(StationEventMusicEvent soundEvent)
    {
        // Either the cvar is disabled or it's already playing
        if(!_eventAudioEnabled || _eventAudio.ContainsKey(soundEvent.Type)) return;

        var stream = _audio.PlayGlobal(soundEvent.Specifier, Filter.Local(), false, soundEvent.AudioParams);
        _eventAudio.Add(soundEvent.Type, stream?.Entity);
    }

    private void PlayGameSound(GameGlobalSoundEvent soundEvent)
    {
        if (IsAndyAnnouncement(soundEvent.Specifier))
        {
            if (!_andyAudioEnabled)
            {
                _audio.PlayGlobal(AndyAnnouncementFallback, Filter.Local(), false, soundEvent.AudioParams);
                return;
            }

            var stream = _audio.PlayGlobal(soundEvent.Specifier, Filter.Local(), false, soundEvent.AudioParams);
            _andyAudio.Add(stream?.Entity);
            return;
        }

        _audio.PlayGlobal(soundEvent.Specifier, Filter.Local(), false, soundEvent.AudioParams);
    }

    private void StopStationEventMusic(StopStationEventMusic soundEvent)
    {
        if (!_eventAudio.TryGetValue(soundEvent.Type, out var stream))
            return;

        _audio.Stop(stream);
        _eventAudio.Remove(soundEvent.Type);
    }

    private void ToggleAdminSound(bool enabled)
    {
        _adminAudioEnabled = enabled;
        if (_adminAudioEnabled) return;
        foreach (var stream in _adminAudio)
        {
            _audio.Stop(stream);
        }
        _adminAudio.Clear();
    }

    private void ToggleStationEventMusic(bool enabled)
    {
        _eventAudioEnabled = enabled;
        if (_eventAudioEnabled) return;
        foreach (var stream in _eventAudio)
        {
            _audio.Stop(stream.Value);
        }
        _eventAudio.Clear();
    }

    private void ToggleAndyAnnouncements(bool enabled)
    {
        _andyAudioEnabled = enabled;

        if (_andyAudioEnabled)
            return;

        foreach (var stream in _andyAudio)
        {
            _audio.Stop(stream);
        }

        _andyAudio.Clear();
    }

    private static bool IsAndyAnnouncement(ResolvedSoundSpecifier specifier)
    {
        if (specifier is not ResolvedPathSpecifier pathSpecifier)
            return false;

        var normalized = pathSpecifier.Path.ToString().Replace('\\', '/').Trim();

        if (normalized.StartsWith("SoundPathSpecifier(", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith(")", StringComparison.Ordinal))
        {
            normalized = normalized["SoundPathSpecifier(".Length..^1];
        }

        if (normalized.StartsWith("ResolvedPathSpecifier(", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith(")", StringComparison.Ordinal))
        {
            normalized = normalized["ResolvedPathSpecifier(".Length..^1];
        }

        normalized = normalized.Trim();
        if (!normalized.StartsWith('/'))
            normalized = $"/{normalized}";

        if (normalized.EndsWith(")", StringComparison.Ordinal))
            normalized = normalized[..^1];

        return normalized.Contains(PocketSizedAndyFolderSegment, StringComparison.OrdinalIgnoreCase)
               && normalized.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
    }

}
