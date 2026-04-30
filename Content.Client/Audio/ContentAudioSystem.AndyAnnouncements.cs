using Content.Shared.CCVar;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Client.Audio;

public sealed partial class ContentAudioSystem
{
    private const string PocketSizedAndyFolderSegment = "/PocketSizedAndy/";
    private const string AndyAnnouncementFallbackPath = "/Audio/Announcements/announce.ogg";
    private const float AndyAnnouncementMaxVolume = 0f;
    // Options sliders display whole percents. Treat <= 1% as muted so "0%" cannot leak quiet playback.
    private const float AndyAnnouncementMuteThreshold = 0.01f;

    private float _andyAnnouncementVolume;
    private bool _andyAnnouncementsEnabled = true;
    private bool _andyAnnouncementsMuted;
    private readonly Dictionary<EntityUid, float> _andyAnnouncementBaseVolumes = new();
    private readonly HashSet<EntityUid> _andyAnnouncementFallbackPlayed = new();
    private readonly HashSet<EntityUid> _andyAnnouncementDisabledHandled = new();
    private bool _andyAnnouncementsInitialized;

    private void InitializeAndyAnnouncements()
    {
        if (_andyAnnouncementsInitialized)
            return;

        _andyAnnouncementsInitialized = true;

        // Prime from current config immediately so startup audio cannot use stale defaults.
        _andyAnnouncementsEnabled = _configManager.GetCVar(CCVars.AndyAnnouncementsEnabled);
        var currentVolume = _configManager.GetCVar(CCVars.AndyAnnouncementVolume);
        _andyAnnouncementsMuted = currentVolume <= AndyAnnouncementMuteThreshold;
        _andyAnnouncementVolume = SharedAudioSystem.GainToVolume(currentVolume);

        Subs.CVar(_configManager, CCVars.AndyAnnouncementsEnabled, AndyAnnouncementEnabledChanged, true);
        Subs.CVar(_configManager, CCVars.AndyAnnouncementVolume, AndyAnnouncementVolumeChanged, true);
        TrySubscribeAndyAudioEvents();
    }

    private void TrySubscribeAndyAudioEvents()
    {
        try
        {
            SubscribeLocalEvent<AudioComponent, ComponentStartup>(OnAudioStartup);
            SubscribeLocalEvent<AudioComponent, ComponentShutdown>(OnAudioShutdown);
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains("Duplicate Subscriptions", StringComparison.Ordinal))
        {
            // Can happen when reconnecting without a full process restart.
        }
    }

    private void OnAudioStartup(EntityUid uid, AudioComponent component, ComponentStartup args)
    {
        // Apply toggle/volume immediately so short one-shot announcements can't slip through.
        UpdateAndyAnnouncementVolume(uid, component);
    }

    private void OnAudioShutdown(EntityUid uid, AudioComponent component, ComponentShutdown args)
    {
        _andyAnnouncementBaseVolumes.Remove(uid);
        _andyAnnouncementFallbackPlayed.Remove(uid);
        _andyAnnouncementDisabledHandled.Remove(uid);
    }

    private void AndyAnnouncementEnabledChanged(bool enabled)
    {
        _andyAnnouncementsEnabled = enabled;

        if (enabled)
        {
            _andyAnnouncementDisabledHandled.Clear();
            _andyAnnouncementFallbackPlayed.Clear();
        }

        UpdateAndyAnnouncementVolumes();
    }

    private void AndyAnnouncementVolumeChanged(float volume)
    {
        _andyAnnouncementsMuted = volume <= AndyAnnouncementMuteThreshold;
        _andyAnnouncementVolume = SharedAudioSystem.GainToVolume(volume);

        UpdateAndyAnnouncementVolumes();
    }

    private void UpdateAndyAnnouncementVolumes()
    {
        var snapshot = new List<EntityUid>();
        var query = EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            snapshot.Add(uid);
        }

        foreach (var uid in snapshot)
        {
            if (!TryComp(uid, out AudioComponent? component))
                continue;

            UpdateAndyAnnouncementVolume(uid, component);
        }
    }

    private void UpdateAndyAnnouncementVolume(EntityUid uid, AudioComponent component)
    {
        var isAndy = IsAndyAnnouncement(component.FileName);
        if (!isAndy)
            return;

        if (!_andyAnnouncementsEnabled && _andyAnnouncementDisabledHandled.Contains(uid))
            return;

        if (!_andyAnnouncementsEnabled)
        {
            _andyAnnouncementDisabledHandled.Add(uid);

            if (_andyAnnouncementFallbackPlayed.Add(uid))
            {
                _audio.PlayGlobal(new ResolvedPathSpecifier(AndyAnnouncementFallbackPath), Filter.Local(), false, component.Params);
            }

            // Stop the original Andy clip so only the replacement announcement is heard.
            _audio.Stop(uid, component);
            return;
        }

        if (_andyAnnouncementsMuted)
        {
            _audio.Stop(uid, component);
            return;
        }

        if (!_andyAnnouncementBaseVolumes.TryGetValue(uid, out var baseVolume))
        {
            baseVolume = component.Params.Volume;
            _andyAnnouncementBaseVolumes[uid] = baseVolume;
        }

        var expected = MathF.Min(baseVolume + _andyAnnouncementVolume, AndyAnnouncementMaxVolume);

        if (MathF.Abs(component.Volume - expected) < 0.001f)
            return;

        _audio.SetVolume(uid, expected, component);
    }

    private static bool IsAndyAnnouncement(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var normalized = fileName.Replace('\\', '/').Trim();

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

        // Match any resource path that plays from a PocketSizedAndy folder
        // (e.g. /Audio/_NF/Announcements/PocketSizedAndy/* or /Audio/_NF/Ambience/PocketSizedAndy/*).
        if (!normalized.Contains(PocketSizedAndyFolderSegment, StringComparison.OrdinalIgnoreCase))
            return false;

        return normalized.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
    }
}