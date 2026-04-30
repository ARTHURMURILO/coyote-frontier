using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Pointing.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server.SurveillanceCamera;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Eye;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Contraband;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Components;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.PDA;
using Content.Shared.Radio;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Stacks;
using Content.Shared._CS.AICore;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Silicons.Laws;
using Content.Shared.UserInterface;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._CS.AICore;

/// <summary>
///     Main orchestrator for LLM-powered AI cores.
///     Handles radio/local chat intake, vision building, pointing, logic channels,
///     rate limiting, ID-card claiming, and periodic UI refresh.
///     LLM calls are offloaded to async tasks via producer/consumer queues
///     (<see cref="_pendingRequests"/>/<see cref="_pendingResponses"/>)
///     to avoid blocking the main simulation tick.
/// </summary>
public sealed class CoyoteAICoreSystem : EntitySystem
{
    [Dependency] private readonly CoyoteLLMClientSystem _llm = default!;
    [Dependency] private readonly CoyoteManifestSystem _manifest = default!;
    [Dependency] private readonly CoyoteRateLimiterSystem _rateLimiter = default!;
    [Dependency] private readonly SpeciesLoreSystem _speciesLore = default!;
    private readonly CoyotePromptBuilder _promptBuilder = new();
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PointingSystem _pointingSystem = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;


    private readonly Dictionary<string, Queue<ChatEntry>> _histories = new();
    private readonly ConcurrentQueue<PendingRequest> _pendingRequests = new();
    private readonly ConcurrentQueue<PendingResponse> _pendingResponses = new();
    private readonly Dictionary<string, TimeSpan> _coreDelays = new();
    private readonly Dictionary<(string, int), TimeSpan> _pulseEndTimes = new();
    private readonly HashSet<(EntityUid, string)> _recentlyProcessed = new();
    private readonly HashSet<string> _busyCores = new();
    private readonly Dictionary<string, List<ChatEntry>> _pendingBatches = new();
    private readonly Dictionary<string, int> _autoContinueCounts = new();
    private readonly Dictionary<string, List<SearchResult>> _searchResults = new();
    private readonly Dictionary<EntityUid, CameraNetworkCache> _cameraCache = new();
    private TimeSpan _lastUiRefresh = TimeSpan.Zero;
    private TimeSpan _lastFullUiRefresh = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CoyoteAICoreComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CoyoteAICoreComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CoyoteAICoreComponent, RadioReceiveEvent>(OnRadioReceive);
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<NFEntityEmotedEvent>(OnEntityEmoted);

        SubscribeLocalEvent<CoyoteAICoreComponent, BeforeActivatableUIOpenEvent>(OnBeforeConfigUiOpen);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIConfigSaveMessage>(OnConfigSave);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIResetHistoryMessage>(OnResetHistory);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetLogicChannelMessage>(OnSetLogicChannel);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetVisionOptionMessage>(OnSetVisionOption);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetItemModeMessage>(OnSetItemMode);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetEnabledMessage>(OnSetEnabled);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetChannelLabelMessage>(OnSetChannelLabel);
        SubscribeLocalEvent<CoyoteAICoreComponent, BoundUserInterfaceMessageAttempt>(OnBuiAttempt);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIToggleLockMessage>(OnToggleLock);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIUnclaimMessage>(OnUnclaim);
        SubscribeLocalEvent<CoyoteAICoreComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIExportMessage>(OnExportRequest);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIImportMessage>(OnImport);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIAddMemoryMessage>(OnAddMemory);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIRemoveMemoryMessage>(OnRemoveMemory);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetRadioChannelMessage>(OnSetRadioChannel);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetGlobalVisionOptionMessage>(OnSetGlobalVisionOption);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetGlobalItemModeMessage>(OnSetGlobalItemMode);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAISetCameraSubnetMessage>(OnSetCameraSubnet);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _recentlyProcessed.Clear();
        RevertExpiredPulses();

        // Periodic UI refresh every 2 seconds for all cores.
        // Uses refreshOnly=true so text fields (AI name, personality, etc.)
        // are not overwritten while the user is editing them.
        var now = _timing.CurTime;
        if (now - _lastFullUiRefresh > TimeSpan.FromSeconds(2))
        {
            _lastFullUiRefresh = now;
            var query = EntityQueryEnumerator<CoyoteAICoreComponent>();
            while (query.MoveNext(out var uid, out var core))
                UpdateConfigUi((uid, core), refreshOnly: true);
        }

        while (_pendingResponses.TryDequeue(out var response))
        {
            Log.Debug($"CoyoteAI: Update dequeued response for core {response.CoreId}");
            InjectResponse(response);
        }

        ProcessExpiredCooldowns();

        if (_pendingRequests.TryDequeue(out var request))
        {
            Log.Debug($"CoyoteAI: Update dequeued request for core {request.Core.CoreId}, starting LLM call");
            _ = ProcessRequestAsync(request);
        }
    }

    private void OnMapInit(Entity<CoyoteAICoreComponent> ent, ref MapInitEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.CoreId))
            ent.Comp.CoreId = Guid.NewGuid().ToString();

        _histories[ent.Comp.CoreId] = new Queue<ChatEntry>();

        if (!string.IsNullOrEmpty(ent.Comp.AiName))
            _metaData.SetEntityName(ent, $"VIGIL CORE-{ent.Comp.AiName}");

        // Ship awareness
        var vessel = GetCurrentVesselName(ent);
        if (string.IsNullOrEmpty(ent.Comp.OriginalShipName))
            ent.Comp.OriginalShipName = vessel;
        if (string.IsNullOrEmpty(ent.Comp.ConstructionDate))
            ent.Comp.ConstructionDate = FormatTime(_timing.CurTime);

        // Load tracking
        ent.Comp.LoadCount++;
        var realDate = DateTime.Now.ToString("yyyy-MM-dd");
        var shiftTime = FormatTime(_timing.CurTime);
        ent.Comp.LoadTimestamps.Add($"Shift {shiftTime} | {realDate}");
        if (ent.Comp.LoadTimestamps.Count > 20)
            ent.Comp.LoadTimestamps.RemoveRange(0, ent.Comp.LoadTimestamps.Count - 20);

        SyncRadioComponents(ent, ent.Comp);
    }

    private void OnShutdown(Entity<CoyoteAICoreComponent> ent, ref ComponentShutdown args)
    {
        _histories.Remove(ent.Comp.CoreId);
        _coreDelays.Remove(ent.Comp.CoreId);
        _pendingBatches.Remove(ent.Comp.CoreId);
        _busyCores.Remove(ent.Comp.CoreId);
        _autoContinueCounts.Remove(ent.Comp.CoreId);
        _rateLimiter.Reset(ent.Comp.CoreId);
    }

    private void OnRadioReceive(EntityUid uid, CoyoteAICoreComponent component, ref RadioReceiveEvent args)
    {
        if (!component.Enabled)
            return;

        if (args.MessageSource == uid || args.RadioSource == uid)
            return;

        var key = (args.MessageSource, args.Message);
        if (!_recentlyProcessed.Add(key))
            return;

        var speakerName = MetaData(args.MessageSource).EntityName;
        if (string.IsNullOrEmpty(speakerName))
            return;

        var species = "Unknown";
        var age = 30;
        var job = "Unknown";

        if (TryComp<HumanoidAppearanceComponent>(args.MessageSource, out var humanoid))
        {
            species = humanoid.Species;
            age = humanoid.Age;
        }

        if (_mind.TryGetMind(args.MessageSource, out var mindId, out var mindComp) &&
            _roles.MindHasRole<JobRoleComponent>((mindId, mindComp), out var role))
        {
            if (role.Value.Comp1.JobPrototype.HasValue)
            {
                var jobProto = _prototype.Index(role.Value.Comp1.JobPrototype.Value);
                job = jobProto.LocalizedName;
            }
        }

        var entry = new ChatEntry
        {
            Type = "radio",
            Channel = args.Channel.ID,
            SpeakerName = speakerName,
            SpeakerSpecies = species,
            SpeakerJob = job,
            SpeakerAge = age,
            Message = args.Message,
            Timestamp = _timing.CurTime
        };

        int? distance = null;
        if (TryComp<TransformComponent>(uid, out var coreXform) &&
            TryComp<TransformComponent>(args.MessageSource, out var sourceXform))
        {
            var sourcePos = _xforms.GetWorldPosition(sourceXform);
            var corePos = _xforms.GetWorldPosition(coreXform);
            distance = (int)(sourcePos - corePos).Length();
        }

        HandleIncomingMessage(uid, component, entry, distance: distance, sourceEntity: args.MessageSource);
    }

    private void OnEntitySpoke(EntitySpokeEvent ev)
    {
        var query = EntityQueryEnumerator<CoyoteAICoreComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var core, out var xform))
        {
            if (!core.Enabled)
                continue;

            if (ev.Source == uid)
                continue;

            if (!TryComp<TransformComponent>(ev.Source, out var sourceXform))
                continue;

            var sourcePos = _xforms.GetWorldPosition(sourceXform);
            var corePos = _xforms.GetWorldPosition(xform);
            var dist = (sourcePos - corePos).Length();

            if (dist > core.LocalHearRange)
                continue;

            var speakerName = MetaData(ev.Source).EntityName;
            if (string.IsNullOrEmpty(speakerName))
                continue;

            var dupKey = (ev.Source, ev.Message);
            if (_recentlyProcessed.Contains(dupKey))
                continue;

            var species = "Unknown";
            var age = 30;
            var job = "Unknown";

            if (TryComp<HumanoidAppearanceComponent>(ev.Source, out var humanoid))
            {
                species = humanoid.Species;
                age = humanoid.Age;
            }

            if (_mind.TryGetMind(ev.Source, out var mindId, out var mindComp) &&
                _roles.MindHasRole<JobRoleComponent>((mindId, mindComp), out var role))
            {
                if (role.Value.Comp1.JobPrototype.HasValue)
                {
                    var jobProto = _prototype.Index(role.Value.Comp1.JobPrototype.Value);
                    job = jobProto.LocalizedName;
                }
            }

            var context = BuildSpeakerContext(ev.Source);
            var entry = new ChatEntry
            {
                Type = "local",
                SpeakerName = speakerName,
                SpeakerSpecies = species,
                SpeakerJob = job,
                SpeakerAge = age,
                Message = ev.Message,
                Timestamp = _timing.CurTime,
                SpeakerContext = context
            };

            HandleIncomingMessage(uid, core, entry, distance: (int)dist, sourceEntity: ev.Source);
        }
    }

    private void OnEntityEmoted(NFEntityEmotedEvent ev)
    {
        var query = EntityQueryEnumerator<CoyoteAICoreComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var core, out var xform))
        {
            if (!core.Enabled)
                continue;

            if (ev.Source == uid)
                continue;

            if (!TryComp<TransformComponent>(ev.Source, out var sourceXform))
                continue;

            var sourcePos = _xforms.GetWorldPosition(sourceXform);
            var corePos = _xforms.GetWorldPosition(xform);
            if ((sourcePos - corePos).Length() > core.LocalHearRange)
                continue;

            var speakerName = MetaData(ev.Source).EntityName;
            if (string.IsNullOrEmpty(speakerName))
                continue;

            var entry = new ChatEntry
            {
                Type = "emote",
                SpeakerName = speakerName,
                Message = ev.Emote,
                Timestamp = _timing.CurTime
            };

            HandleIncomingMessage(uid, core, entry, distance: null, sourceEntity: ev.Source);
        }
    }

    /// <summary>
    ///     Process an incoming chat message for a specific core.
    ///     When locked+claimed, only the owner (physically holding their ID card)
    ///     can trigger responses — this is the "privacy mode" that prevents
    ///     bystanders from talking to a locked AI core.
    /// </summary>
    private void HandleIncomingMessage(EntityUid uid, CoyoteAICoreComponent core, ChatEntry entry, int? distance, EntityUid? sourceEntity = null)
    {
        if (core.IsLocked && core.IsClaimed && (sourceEntity == null || !HasOwnerIdCard(sourceEntity.Value, core.OwnerName)))
            return;

        if (!IsPowered(uid))
            return;

        if (!_histories.TryGetValue(core.CoreId, out var history))
        {
            history = new Queue<ChatEntry>();
            _histories[core.CoreId] = history;
        }

        history.Enqueue(entry);
        while (history.Count > core.MaxHistoryLength)
            history.Dequeue();

        // Busy check: core has an in-flight LLM request → batch for later
        if (_busyCores.Contains(core.CoreId))
        {
            Log.Debug($"CoyoteAI: Core {core.CoreId} busy, batching message from '{entry.SpeakerName}'");
            if (!_pendingBatches.ContainsKey(core.CoreId))
                _pendingBatches[core.CoreId] = new List<ChatEntry>();
            _pendingBatches[core.CoreId].Add(entry);
            return;
        }

        // Cooldown check: within post-response pause → batch instead of dropping
        if (_coreDelays.TryGetValue(core.CoreId, out var delayUntil) && _timing.CurTime < delayUntil)
        {
            Log.Debug($"CoyoteAI: Core {core.CoreId} in cooldown for {((delayUntil - _timing.CurTime).TotalSeconds):F1}s, batching");
            if (!_pendingBatches.ContainsKey(core.CoreId))
                _pendingBatches[core.CoreId] = new List<ChatEntry>();
            _pendingBatches[core.CoreId].Add(entry);
            return;
        }

        if (!_rateLimiter.CanRespond(core.CoreId))
        {
            Log.Debug($"CoyoteAI: Core {core.CoreId} rate limited");
            return;
        }

        Log.Debug($"CoyoteAI: Enqueuing LLM request for core {core.CoreId} from speaker '{entry.SpeakerName}'");

        _busyCores.Add(core.CoreId);

        var shiftDuration = FormatTime(_timing.CurTime);
        var timeSinceLast = GetTimeSinceLastResponse(core.CoreId);
        var manifest = _manifest.GetCrewManifest();
        var speciesIds = _manifest.GetSpeciesOnStation();
        var speciesLoreBlock = _speciesLore.BuildLoreBlock(speciesIds);
        var lawBlock = BuildLawBlock(core.LawSet);
        var localVision = BuildLocalVisionBlock(uid, core);
        var globalVision = BuildGlobalVisionBlock(uid, core);
        var visionBlock = CombineVision(localVision, globalVision, core);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(core, manifest, speciesLoreBlock, lawBlock, visionBlock, shiftDuration, timeSinceLast, GetCurrentVesselName((uid, core)), out _);
        var userPrompt = _promptBuilder.BuildUserPrompt(history, entry, shiftDuration, distance);

        _pendingRequests.Enqueue(new PendingRequest
        {
            CoreUid = uid,
            Core = core,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt
        });
    }

    private string GetTimeSinceLastResponse(string coreId)
    {
        if (!_histories.TryGetValue(coreId, out var history))
            return "N/A";

        ChatEntry? lastAiEntry = null;
        foreach (var e in history)
        {
            if (e.SpeakerJob == "Station Intelligence")
                lastAiEntry = e;
        }

        if (lastAiEntry == null)
            return "No previous response";

        var elapsed = _timing.CurTime - lastAiEntry.Timestamp;
        var secs = (int)elapsed.TotalSeconds;
        if (secs < 1) return "less than a second ago";
        if (secs < 60) return $"{secs} seconds ago";
        var mins = secs / 60;
        return $"{mins} minute{(mins > 1 ? "s" : "")} ago";
    }

    private async Task ProcessRequestAsync(PendingRequest request)
    {
        if (request.Core.Deleted || !Exists(request.CoreUid))
        {
            Log.Debug($"CoyoteAI: Request skipped, core entity gone for {request.Core.CoreId}");
            return;
        }

        Log.Debug($"CoyoteAI: Calling LLM for core {request.Core.CoreId}...");
        var response = await _llm.CallAsync(request.Core, request.SystemPrompt, request.UserPrompt);
        Log.Debug($"CoyoteAI: LLM returned {(response == null ? "null" : $"shouldRespond={response.ShouldRespond}")} for core {request.Core.CoreId}");

        _pendingResponses.Enqueue(new PendingResponse
        {
            CoreUid = request.CoreUid,
            CoreId = request.Core.CoreId,
            AiName = request.Core.AiName,
            SystemPrompt = request.SystemPrompt,
            UserPrompt = request.UserPrompt,
            Response = response
        });
    }

    private void InjectResponse(PendingResponse response)
    {
        if (response.Response == null)
        {
            Log.Debug($"CoyoteAI: Inject skipped, null response for core {response.CoreId}");
            _busyCores.Remove(response.CoreId);
            _coreDelays[response.CoreId] = _timing.CurTime + TimeSpan.FromSeconds(0.5);
            return;
        }

        if (!Exists(response.CoreUid))
        {
            Log.Error($"CoyoteAI: Core entity {response.CoreUid} no longer exists");
            _busyCores.Remove(response.CoreId);
            _coreDelays.Remove(response.CoreId);
            _pendingBatches.Remove(response.CoreId);
            return;
        }

        if (!TryComp<CoyoteAICoreComponent>(response.CoreUid, out var core))
        {
            Log.Error($"CoyoteAI: Core component missing on {response.CoreUid}");
            _busyCores.Remove(response.CoreId);
            _coreDelays.Remove(response.CoreId);
            _pendingBatches.Remove(response.CoreId);
            return;
        }

        // ── Vision queries bypass should_respond (they're info requests, not chat) ──
        if (HandleVisionQuery(response, core))
            return;

        // ── Actions always processed even when should_respond=false ──
        var action = !string.IsNullOrEmpty(response.Response.Action) ? response.Response.Action.ToLowerInvariant() : null;
        if (action != null)
        {
            if (!string.IsNullOrEmpty(response.Response.ActionChannel))
            {
                var channelName = response.Response.ActionChannel.ToLowerInvariant();
                var channelIdx = Array.FindIndex(CoyoteAICoreComponent.LogicChannelNames, n => n == channelName);
                if (channelIdx >= 0)
                {
                    Log.Debug($"CoyoteAI: Logic channel action '{action}' on '{channelName}'");
                    switch (action)
                    {
                        case "pulse":
                            core.ChannelStates[channelIdx] = LogicChannelMode.Pulse;
                            _pulseEndTimes[(core.CoreId, channelIdx)] = _timing.CurTime + TimeSpan.FromSeconds(1);
                            break;
                        case "on":
                            core.ChannelStates[channelIdx] = LogicChannelMode.On;
                            _pulseEndTimes.Remove((core.CoreId, channelIdx));
                            break;
                        case "off":
                            core.ChannelStates[channelIdx] = LogicChannelMode.Off;
                            _pulseEndTimes.Remove((core.CoreId, channelIdx));
                            break;
                    }
                    Dirty(response.CoreUid, core);
                    UpdateConfigUi((response.CoreUid, core));
                    var portName = "Channel" + channelName.Substring(0, 1).ToUpper() + channelName.Substring(1);
                    var signal = action != "off";
                    _deviceLink.SendSignal(response.CoreUid, portName, signal);
                }
            }

            // Core lock/unlock/abandon
            switch (action)
            {
                case "core_lock":
                    core.IsLocked = true;
                    core.AiLocked = true;
                    Dirty(response.CoreUid, core);
                    UpdateConfigUi((response.CoreUid, core));
                    Log.Debug($"CoyoteAI: Core {response.CoreId} locked by AI");
                    break;
                case "core_unlock":
                    core.IsLocked = false;
                    core.AiLocked = false;
                    Dirty(response.CoreUid, core);
                    UpdateConfigUi((response.CoreUid, core));
                    Log.Debug($"CoyoteAI: Core {response.CoreId} unlocked by AI");
                    break;
                case "core_abandon" when core.IsClaimed:
                    var abandonNow = FormatTime(_timing.CurTime);
                    foreach (var r in core.OwnershipHistory)
                    {
                        if (r.UnclaimedAt == null)
                            r.UnclaimedAt = abandonNow;
                    }
                    core.OwnerId = string.Empty;
                    core.OwnerName = string.Empty;
                    core.IsLocked = false;
                    core.AiLocked = false;
                    Dirty(response.CoreUid, core);
                    UpdateOwnerDescription((response.CoreUid, core));
                    UpdateConfigUi((response.CoreUid, core));
                    Log.Debug($"CoyoteAI: Core {response.CoreId} abandoned by AI");
                    break;
            }
        }

        // Memory actions
        if (!string.IsNullOrEmpty(response.Response.MemoryAdd) && response.Response.MemoryAdd != "true")
        {
            var priority = response.Response.MemoryAddPriority?.ToLowerInvariant() switch
            {
                "low" => MemoryPriority.Low,
                "high" => MemoryPriority.High,
                "critical" => MemoryPriority.Critical,
                _ => MemoryPriority.Normal
            };
            var tags = response.Response.MemoryAddTags ?? new();
            core.Memories.Add(new AICoreMemory
            {
                Id = Guid.NewGuid().ToString(),
                Content = response.Response.MemoryAdd,
                Priority = priority,
                Tags = tags,
                CreatedAt = FormatTime(_timing.CurTime),
                LastAccessedAt = FormatTime(_timing.CurTime)
            });
            PruneMemories(core);
            Dirty(response.CoreUid, core);
            Log.Debug($"CoyoteAI: Memory added for core {response.CoreId}: {response.Response.MemoryAdd}");
        }

        if (!string.IsNullOrEmpty(response.Response.MemoryRemove))
        {
            core.Memories.RemoveAll(m => m.Id == response.Response.MemoryRemove);
            Dirty(response.CoreUid, core);
            Log.Debug($"CoyoteAI: Memory removed for core {response.CoreId}");
        }

        if (response.Response.MemoryClear && response.Response.MemoryClearConfirm)
        {
            core.Memories.Clear();
            Dirty(response.CoreUid, core);
            Log.Debug($"CoyoteAI: All memories cleared for core {response.CoreId}");
        }

        if (!string.IsNullOrEmpty(response.Response.PointAt))
        {
            PointAtEntity(response.CoreUid, response.Response.PointAt, core.VisionRange);
        }

        if (!response.Response.ShouldRespond)
        {
            Log.Debug($"CoyoteAI: Inject skipped, ShouldRespond=false for core {response.CoreId}");
            _busyCores.Remove(response.CoreId);
            _coreDelays[response.CoreId] = _timing.CurTime + TimeSpan.FromSeconds(0.5);
            return;
        }

        var message = response.Response.Message;
        if (string.IsNullOrEmpty(message))
        {
            Log.Debug($"CoyoteAI: Empty message from LLM for core {response.CoreId}");
            _busyCores.Remove(response.CoreId);
            _coreDelays[response.CoreId] = _timing.CurTime + TimeSpan.FromSeconds(0.5);
            return;
        }

        var channel = response.Response.Channel;
        var isLocal = string.IsNullOrEmpty(channel) ||
                       channel.Equals("Local", StringComparison.OrdinalIgnoreCase);

        var name = response.AiName;

        if (isLocal)
        {
            Log.Debug($"CoyoteAI: Speaking locally for core {response.CoreId}");
            _chat.TrySendInGameICMessage(
                response.CoreUid,
                message,
                InGameICChatType.Speak,
                ChatTransmitRange.Normal,
                hideLog: false,
                nameOverride: name,
                ignoreActionBlocker: true
            );
        }
        else
        {
            Log.Debug($"CoyoteAI: Injecting response on channel '{channel}' for core {response.CoreId}");
            if (!_prototype.TryIndex<RadioChannelPrototype>(channel!, out var channelProto))
            {
                Log.Error($"CoyoteAI: Channel prototype '{channel}' not found!");
                return;
            }
            _radio.SendRadioMessage(response.CoreUid, message, channelProto, response.CoreUid);
        }

        if (response.Response.Delay > 0)
        {
            var delayMs = Math.Min(response.Response.Delay, 6000);
            _coreDelays[core.CoreId] = _timing.CurTime + TimeSpan.FromMilliseconds(delayMs);
            Log.Debug($"CoyoteAI: Core {core.CoreId} set forced delay of {delayMs}ms");
        }

        _rateLimiter.RegisterResponse(core.CoreId);

        if (_histories.TryGetValue(core.CoreId, out var history))
        {
            history.Enqueue(new ChatEntry
            {
                Type = isLocal ? "local" : "radio",
                Channel = isLocal ? null : channel,
                SpeakerName = name,
                SpeakerSpecies = "AI",
                SpeakerJob = "Station Intelligence",
                SpeakerAge = 0,
                Message = message,
                Timestamp = _timing.CurTime
            });
        }

        // Clear busy flag — new messages will now be queued or start fresh requests
        _busyCores.Remove(core.CoreId);

        // Dynamic cooldown: scale with response length so the AI doesn't spam
        // cooldown = clamp(response.Length * charFactor, base, max)
        var responseLen = message.Length;
        var cooldown = Math.Clamp(responseLen * core.CooldownCharFactor, core.CooldownBase, core.CooldownMax);
        var cooldownEnd = _timing.CurTime + TimeSpan.FromSeconds(cooldown);

        // Only override delay if it's shorter than our calculated cooldown
        if (!_coreDelays.TryGetValue(core.CoreId, out var existingDelay) || existingDelay < cooldownEnd)
            _coreDelays[core.CoreId] = cooldownEnd;

        Log.Debug($"CoyoteAI: Core {core.CoreId} dynamic cooldown {cooldown:F1}s (response len {responseLen})");

        // Auto-continue: if enabled, response is long enough, hasn't set continue=true,
        // and doesn't end with sentence-terminal punctuation, fire one auto follow-up.
        if (core.AutoContinue && responseLen >= core.AutoContinueThreshold && !response.Response.Continue
            && message.Length > 0 && !".!?\"".Contains(message[^1]))
        {
            var autoCount = _autoContinueCounts.GetValueOrDefault(core.CoreId, 0);
            if (autoCount < core.AutoContinueMax)
            {
                _autoContinueCounts[core.CoreId] = autoCount + 1;
                Log.Debug($"CoyoteAI: Auto-continue {autoCount + 1}/{core.AutoContinueMax} for core {core.CoreId}");
                var shiftDuration = FormatTime(_timing.CurTime);
                var followupTrigger = new ChatEntry
                {
                    Type = "followup",
                    SpeakerName = "System",
                    SpeakerSpecies = "",
                    SpeakerJob = "",
                    SpeakerAge = 0,
                    Message = $"Auto-continue. Your previous response was: \"{message}\". Continue your response naturally without repeating yourself.",
                    Timestamp = _timing.CurTime
                };
                var followupHistory = _histories.TryGetValue(core.CoreId, out var h) ? new Queue<ChatEntry>(h) : new Queue<ChatEntry>();
                followupHistory.Enqueue(followupTrigger);
                // Don't mark busy — allow this auto-continue to go through immediately
                var followupUserPrompt = _promptBuilder.BuildUserPrompt(
                    followupHistory, followupTrigger, shiftDuration,
                    distance: null);
                _pendingRequests.Enqueue(new PendingRequest
                {
                    CoreUid = response.CoreUid,
                    Core = core,
                    SystemPrompt = response.SystemPrompt,
                    UserPrompt = followupUserPrompt
                });
            }
        }

        // Handle LLM-requested follow-ups (continue: true)
        if (response.Response.Continue)
        {
            Log.Debug($"CoyoteAI: Queuing follow-up for core {response.CoreId}");
            var shiftDuration = FormatTime(_timing.CurTime);
            var followupTrigger = new ChatEntry
            {
                Type = "followup",
                SpeakerName = "System",
                SpeakerSpecies = "",
                SpeakerJob = "",
                SpeakerAge = 0,
                Message = $"Follow-up requested. Your previous response was: \"{message}\". Continue your response naturally without repeating yourself.",
                Timestamp = _timing.CurTime
            };
            var followupHistory = _histories.TryGetValue(core.CoreId, out var h) ? new Queue<ChatEntry>(h) : new Queue<ChatEntry>();
            followupHistory.Enqueue(followupTrigger);
            _busyCores.Add(core.CoreId);
            var followupUserPrompt = _promptBuilder.BuildUserPrompt(
                followupHistory, followupTrigger, shiftDuration,
                distance: null);
            _pendingRequests.Enqueue(new PendingRequest
            {
                CoreUid = response.CoreUid,
                Core = core,
                SystemPrompt = response.SystemPrompt,
                UserPrompt = followupUserPrompt
            });
        }
    }

    private void OnBeforeConfigUiOpen(Entity<CoyoteAICoreComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        if (!IsPowered(ent.Owner))
            return;
        if (ent.Comp.IsLocked && ent.Comp.IsClaimed && !HasOwnerIdCard(args.User, ent.Comp.OwnerName))
            return;
        UpdateConfigUi(ent);
    }

    private void OnConfigSave(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIConfigSaveMessage args)
    {
        ent.Comp.AiName = args.AiName;
        ent.Comp.PersonalityPrompt = args.PersonalityPrompt;
        ent.Comp.ApiEndpoint = args.ApiEndpoint;
        ent.Comp.ModelName = args.ModelName;
        if (!string.IsNullOrEmpty(args.ApiKey))
            ent.Comp.ApiKey = args.ApiKey;
        ent.Comp.Temperature = Math.Clamp(args.Temperature, 0.1f, 2.0f);
        ent.Comp.ReasoningLevel = args.ReasoningLevelData;
        ent.Comp.LawSet = args.LawSet;
        ent.Comp.MaxHistoryLength = Math.Clamp(args.MaxHistory, 5, 1000);
        ent.Comp.MaxTokens = Math.Max(args.MaxTokens, 1);
        ent.Comp.VisionRange = Math.Clamp(args.VisionRange, 1f, 15f);
        ent.Comp.Enabled = args.Enabled;
        ent.Comp.CooldownBase = Math.Clamp(args.CooldownBase, 0.1f, 10f);
        ent.Comp.CooldownCharFactor = Math.Clamp(args.CooldownCharFactor, 0.001f, 0.5f);
        ent.Comp.CooldownMax = Math.Clamp(args.CooldownMax, 0.1f, 10f);
        ent.Comp.AutoContinue = args.AutoContinue;
        ent.Comp.AutoContinueThreshold = Math.Max(args.AutoContinueThreshold, 50);
        ent.Comp.AutoContinueMax = Math.Clamp(args.AutoContinueMax, 1, 10);
        ent.Comp.LocalItemMode = args.LocalItemMode;
        if (args.Memories != null)
            ent.Comp.Memories = args.Memories;
        ent.Comp.ChannelLabels = args.ChannelLabels;
        Dirty(ent);

        _metaData.SetEntityName(ent, $"VIGIL CORE-{args.AiName}");

        UpdateConfigUi(ent);

        _audio.PlayPredicted(ent.Comp.SaveSound, ent, args.Actor);
    }

    private void OnResetHistory(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIResetHistoryMessage args)
    {
        if (_histories.ContainsKey(ent.Comp.CoreId))
            _histories[ent.Comp.CoreId].Clear();
        _coreDelays.Remove(ent.Comp.CoreId);
        _pendingBatches.Remove(ent.Comp.CoreId);
        _busyCores.Remove(ent.Comp.CoreId);
        _autoContinueCounts.Remove(ent.Comp.CoreId);
        _rateLimiter.Reset(ent.Comp.CoreId);
        UpdateConfigUi(ent);
    }

    private void OnSetLogicChannel(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetLogicChannelMessage args)
    {
        var signal = args.Mode == LogicChannelMode.Off ? false : true;
        ent.Comp.ChannelStates[args.ChannelIndex] = args.Mode;
        if (args.Mode == LogicChannelMode.Pulse)
            _pulseEndTimes[(ent.Comp.CoreId, args.ChannelIndex)] = _timing.CurTime + TimeSpan.FromSeconds(1);
        else
            _pulseEndTimes.Remove((ent.Comp.CoreId, args.ChannelIndex));
        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);

        var portName = "Channel" + CoyoteAICoreComponent.LogicChannelNames[args.ChannelIndex].Substring(0, 1).ToUpper()
            + CoyoteAICoreComponent.LogicChannelNames[args.ChannelIndex].Substring(1);
        _deviceLink.SendSignal(ent, portName, signal);
    }

    private void OnSetVisionOption(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetVisionOptionMessage args)
    {
        switch (args.Option)
        {
            case "ShowPeople": ent.Comp.ShowPeopleLocal = args.Value; break;
            case "ShowMachines": ent.Comp.ShowMachinesLocal = args.Value; break;
            case "ShowMachinesDetail": ent.Comp.ShowMachinesDetailLocal = args.Value; break;
            case "ShowItems": ent.Comp.ShowItemsLocal = args.Value; break;
            case "ShowItemsDetail": ent.Comp.ShowItemsDetailLocal = args.Value; break;
        }
        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);
    }

    private void OnSetItemMode(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetItemModeMessage args)
    {
        ent.Comp.LocalItemMode = args.Mode;
        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);
    }

    private void OnSetGlobalVisionOption(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetGlobalVisionOptionMessage args)
    {
        switch (args.Option)
        {
            case "GlobalVisionEnabled": ent.Comp.GlobalVisionEnabled = args.Value; break;
            case "ShowPeopleGlobal": ent.Comp.ShowPeopleGlobal = args.Value; break;
            case "ShowMachinesGlobal": ent.Comp.ShowMachinesGlobal = args.Value; break;
            case "ShowMachinesDetailGlobal": ent.Comp.ShowMachinesDetailGlobal = args.Value; break;
            case "ShowItemsGlobal": ent.Comp.ShowItemsGlobal = args.Value; break;
            case "ShowItemsDetailGlobal": ent.Comp.ShowItemsDetailGlobal = args.Value; break;
        }
        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);
    }

    private void OnSetGlobalItemMode(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetGlobalItemModeMessage args)
    {
        ent.Comp.GlobalItemMode = args.Mode;
        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);
    }

    private void OnSetCameraSubnet(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetCameraSubnetMessage args)
    {
        if (args.Enabled)
        {
            if (!ent.Comp.EnabledCameraSubnets.Contains(args.SubnetId))
                ent.Comp.EnabledCameraSubnets.Add(args.SubnetId);
        }
        else
            ent.Comp.EnabledCameraSubnets.Remove(args.SubnetId);

        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);
    }

    private void OnSetEnabled(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetEnabledMessage args)
    {
        ent.Comp.Enabled = args.Enabled;
        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);
    }

    private void OnSetChannelLabel(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetChannelLabelMessage args)
    {
        if (args.ChannelIndex >= 0 && args.ChannelIndex < ent.Comp.ChannelLabels.Length)
        {
            ent.Comp.ChannelLabels[args.ChannelIndex] = args.Label;
            Dirty(ent);
        }
    }

    private void SyncRadioComponents(EntityUid uid, CoyoteAICoreComponent core)
    {
        if (TryComp<ActiveRadioComponent>(uid, out var radio))
            radio.Channels = new HashSet<string>(core.RadioChannels);
        if (TryComp<IntrinsicRadioTransmitterComponent>(uid, out var transmitter))
            transmitter.Channels = new HashSet<string>(core.RadioChannels);
    }

    private void OnSetRadioChannel(Entity<CoyoteAICoreComponent> ent, ref CoyoteAISetRadioChannelMessage args)
    {
        if (args.Enabled)
            ent.Comp.RadioChannels.Add(args.ChannelId);
        else
            ent.Comp.RadioChannels.Remove(args.ChannelId);

        SyncRadioComponents(ent, ent.Comp);
        Dirty(ent);
        UpdateConfigUi(ent, refreshOnly: true);
    }

    private void RevertExpiredPulses()
    {
        var now = _timing.CurTime;
        List<(string, int)> expired = new();
        foreach (var (key, endTime) in _pulseEndTimes)
        {
            if (now >= endTime)
                expired.Add(key);
        }
        foreach (var (coreId, channelIdx) in expired)
        {
            _pulseEndTimes.Remove((coreId, channelIdx));
            var query = EntityQueryEnumerator<CoyoteAICoreComponent>();
            while (query.MoveNext(out var uid, out var core))
            {
                if (core.CoreId != coreId) continue;
                core.ChannelStates[channelIdx] = LogicChannelMode.Off;
                Dirty(uid, core);
                UpdateConfigUi((uid, core));
                break;
            }
        }
    }

    private void ProcessExpiredCooldowns()
    {
        var now = _timing.CurTime;
        List<string> expired = new();
        foreach (var (coreId, endTime) in _coreDelays)
        {
            if (now >= endTime)
                expired.Add(coreId);
        }
        foreach (var coreId in expired)
        {
            _coreDelays.Remove(coreId);
            if (!_pendingBatches.TryGetValue(coreId, out var batch) || batch.Count == 0)
                continue;
            _pendingBatches.Remove(coreId);

            var query = EntityQueryEnumerator<CoyoteAICoreComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var core, out var xform))
            {
                if (core.CoreId != coreId) continue;
                if (!IsPowered(uid)) break;

                Log.Debug($"CoyoteAI: Processing batch of {batch.Count} for core {coreId}");

                var shiftDuration = FormatTime(now);
                var manifest = _manifest.GetCrewManifest();
                var speciesIds = _manifest.GetSpeciesOnStation();
                var speciesLoreBlock = _speciesLore.BuildLoreBlock(speciesIds);
                var lawBlock = BuildLawBlock(core.LawSet);
                var localVision = BuildLocalVisionBlock(uid, core);
                var globalVision = BuildGlobalVisionBlock(uid, core);
                var mergedVision = CombineVision(localVision, globalVision, core);
                var history = _histories.TryGetValue(coreId, out var h) ? h : new Queue<ChatEntry>();
                var userPrompt = batch.Count == 1
                    ? _promptBuilder.BuildUserPrompt(history, batch[0], shiftDuration, null)
                    : _promptBuilder.BuildBatchPrompt(history, batch, shiftDuration);
                var systemPrompt = _promptBuilder.BuildSystemPrompt(core, manifest, speciesLoreBlock, lawBlock, mergedVision, shiftDuration, GetTimeSinceLastResponse(coreId), GetCurrentVesselName((uid, core)), out _);

                _busyCores.Add(coreId);
                _pendingRequests.Enqueue(new PendingRequest
                {
                    CoreUid = uid,
                    Core = core,
                    SystemPrompt = systemPrompt,
                    UserPrompt = userPrompt
                });
                break;
            }
        }
    }

    private void OnInteractUsing(Entity<CoyoteAICoreComponent> ent, ref InteractUsingEvent args)
    {
        EntityUid? idCard = null;
        IdCardComponent? idComp = null;

        if (TryComp<IdCardComponent>(args.Used, out var directId))
        {
            idCard = args.Used;
            idComp = directId;
        }
        else if (TryComp<PdaComponent>(args.Used, out var pda) && pda.IdSlot.Item is { } pdaId)
        {
            idCard = pdaId;
            TryComp(idCard, out idComp);
        }

        if (idCard == null || idComp == null || string.IsNullOrEmpty(idComp.FullName))
            return;

        args.Handled = true;

        var swiperName = idComp.FullName;

        // AI-locked: ID card swipe does nothing — only core_unlock from the AI can unlock
        if (ent.Comp.AiLocked)
            return;

        if (!ent.Comp.IsClaimed)
        {
            ent.Comp.OwnerId = swiperName;
            ent.Comp.OwnerName = swiperName;
            ent.Comp.IsLocked = false;
            ent.Comp.AiLocked = false;
            AddOwnershipRecord(ent, swiperName);
            Dirty(ent);
            UpdateOwnerDescription(ent);
            UpdateConfigUi(ent);
            return;
        }

        // Same owner — toggle lock
        ent.Comp.IsLocked = !ent.Comp.IsLocked;
        if (!ent.Comp.IsLocked)
            ent.Comp.AiLocked = false;
        Dirty(ent);
        UpdateOwnerDescription(ent);
        UpdateConfigUi(ent);
    }

    private void AddOwnershipRecord(Entity<CoyoteAICoreComponent> ent, string ownerName)
    {
        var now = FormatTime(_timing.CurTime);
        // Close any active record
        foreach (var r in ent.Comp.OwnershipHistory)
        {
            if (r.UnclaimedAt == null)
                r.UnclaimedAt = now;
        }
        ent.Comp.OwnershipHistory.Add(new OwnershipRecord
        {
            OwnerName = ownerName,
            ClaimedAt = now,
            UnclaimedAt = null
        });
    }

    private void UpdateOwnerDescription(Entity<CoyoteAICoreComponent> ent)
    {
        var desc = "A VIGIL (Vessel Intelligence & General Integration Layer) core unit. An experimental AI core based on the LLM API system (OpenAI compatible API) by the now bankrupt 'ClosedAI' and their partner 'MicroSloopy', this core gives the LLM all the tools to be able to interact with the world with features such as pointing, speaking, radio, logic channels, vision, descriptions, crew manifest, Story/politic books and other features allowing for your lonely ship to be less lonely! (quality will vary with LLM model used)";
        if (ent.Comp.IsClaimed)
        {
            desc += $"\n\nRegistered to: {ent.Comp.OwnerName}";
            var lockStatus = ent.Comp.AiLocked ? "AI-Locked" : ent.Comp.IsLocked ? "Locked" : "Unlocked";
            desc += $"\nStatus: {lockStatus}";
        }
        _metaData.SetEntityDescription(ent, desc);
    }

    private string GetCurrentVesselName(Entity<CoyoteAICoreComponent> ent)
    {
        if (!TryComp<TransformComponent>(ent, out var xform))
            return "Unknown";

        if (xform.GridUid is not { Valid: true } gridUid)
            return "Deep Space";

        // Try shipyard deed first (NF)
        if (TryComp<ShuttleDeedComponent>(gridUid, out var deed))
        {
            var fullName = deed.ShuttleName ?? "Unknown";
            if (!string.IsNullOrEmpty(deed.ShuttleNameSuffix))
                fullName += " " + deed.ShuttleNameSuffix;
            return fullName;
        }

        // Try station name
        var station = _stationSystem.GetOwningStation(gridUid);
        if (station is { Valid: true })
            return Name(station.Value);

        // Fallback to grid name
        return Name(gridUid);
    }

    private void OnBuiAttempt(Entity<CoyoteAICoreComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (args.Message is CoyoteAIToggleLockMessage or CoyoteAIUnclaimMessage)
        {
            if (!ent.Comp.IsClaimed || !HasOwnerIdCard(args.Actor, ent.Comp.OwnerName))
                args.Cancel();
        }
        if (args.Message is CoyoteAIUnclaimMessage && ent.Comp.IsLocked && !ent.Comp.AiLocked)
            args.Cancel();
        if (args.Message is CoyoteAIToggleLockMessage && ent.Comp.AiLocked)
            args.Cancel();
    }

    private void OnToggleLock(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIToggleLockMessage args)
    {
        if (!ent.Comp.IsClaimed) return;
        ent.Comp.IsLocked = !ent.Comp.IsLocked;
        Dirty(ent);
        UpdateOwnerDescription(ent);
        UpdateConfigUi(ent);
    }

    private void OnUnclaim(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIUnclaimMessage args)
    {
        if (ent.Comp.IsLocked && !ent.Comp.AiLocked) return;
        var now = FormatTime(_timing.CurTime);
        foreach (var r in ent.Comp.OwnershipHistory)
        {
            if (r.UnclaimedAt == null)
                r.UnclaimedAt = now;
        }
        ent.Comp.OwnerId = string.Empty;
        ent.Comp.OwnerName = string.Empty;
        ent.Comp.IsLocked = false;
        ent.Comp.AiLocked = false;
        Dirty(ent);
        UpdateOwnerDescription(ent);
        UpdateConfigUi(ent);
    }

    private bool HasOwnerIdCard(EntityUid uid, string ownerName)
    {
        if (TryComp<HandsComponent>(uid, out var hands))
        {
            foreach (var hand in hands.Hands.Values)
            {
                if (hand.HeldEntity.HasValue && HasIdWithName(hand.HeldEntity.Value, ownerName))
                    return true;
            }
        }

        if (TryComp<InventoryComponent>(uid, out var inv))
        {
            for (int i = 0; i < inv.Containers.Length; i++)
            {
                if (inv.Containers[i].ContainedEntity is { Valid: true } item)
                {
                    if (HasIdWithName(item, ownerName))
                        return true;
                    if (TryComp<PdaComponent>(item, out var pda) && pda.IdSlot.Item is { } pdaId)
                    {
                        if (HasIdWithName(pdaId, ownerName))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private bool HasIdWithName(EntityUid uid, string name)
    {
        return TryComp<IdCardComponent>(uid, out var id) && id.FullName == name;
    }

    private bool IsPowered(EntityUid uid)
    {
        return TryComp<ApcPowerReceiverComponent>(uid, out var apc) && apc.Powered;
    }

    /// <summary>
    ///     Sends the full BUI state to all watching clients.
    ///     When <paramref name="refreshOnly"/> is true, the client skips overwriting
    ///     text-field contents (AiNameEdit, PersonalityEdit, etc.) so the user's
    ///     in-progress edits survive checkbox/layout toggles.
    ///     The system prompt is rebuilt every time because it includes the live
    ///     vision block and crew manifest; the char-count-based token estimate
    ///     is derived from it for the "Total prompt tokens" display.
    /// </summary>
    private void UpdateConfigUi(Entity<CoyoteAICoreComponent> ent, bool refreshOnly = false)
    {
        var lawSets = _prototype.EnumeratePrototypes<SiliconLawsetPrototype>()
            .Select(l => l.ID)
            .Prepend("")
            .ToArray();
        var history = _histories.TryGetValue(ent.Comp.CoreId, out var h) ? h : new Queue<ChatEntry>();
        var totalChars = 0;
        foreach (var e in history)
            totalChars += e.Message.Length + e.SpeakerName.Length + (e.Channel?.Length ?? 5) + 10;
        var estimatedTokens = totalChars / 4;

        var shiftDuration = FormatTime(_timing.CurTime);
        var manifest = _manifest.GetCrewManifest();
        var speciesIds = _manifest.GetSpeciesOnStation();
        var speciesLoreBlock = _speciesLore.BuildLoreBlock(speciesIds);
        var lawBlock = BuildLawBlock(ent.Comp.LawSet);
        var localVision = BuildLocalVisionBlock(ent.Owner, ent.Comp);
        var globalVision = BuildGlobalVisionBlock(ent.Owner, ent.Comp);
        var localVisionChars = localVision.Length;
        var globalVisionStr = globalVision ?? string.Empty;
        var globalVisionChars = ent.Comp.GlobalVisionEnabled ? globalVisionStr.Length : 0;
        var mergedVision = CombineVision(localVision, globalVisionStr, ent.Comp);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(ent.Comp, manifest, speciesLoreBlock, lawBlock, mergedVision, shiftDuration, "N/A", GetCurrentVesselName(ent), out var counts);
        var totalEstimatedTokens = (systemPrompt.Length + totalChars) / 4;

        var state = new CoyoteAIConfigBuiState(
            aiName: ent.Comp.AiName,
            personalityPrompt: ent.Comp.PersonalityPrompt,
            apiEndpoint: ent.Comp.ApiEndpoint,
            modelName: ent.Comp.ModelName,
            temperature: ent.Comp.Temperature,
            hasApiKey: ent.Comp.HasApiKeyConfigured,
            reasoningLevel: ent.Comp.ReasoningLevel,
            lawSet: ent.Comp.LawSet,
            availableLawSets: lawSets,
            maxHistory: ent.Comp.MaxHistoryLength,
            maxTokens: ent.Comp.MaxTokens,
            enabled: ent.Comp.Enabled,
            historyLength: history.Count,
            estimatedTokens: estimatedTokens,
            totalEstimatedTokens: totalEstimatedTokens,
            channelStates: ent.Comp.ChannelStates,
            channelLabels: ent.Comp.ChannelLabels,
            ownerName: ent.Comp.OwnerName,
            isLocked: ent.Comp.IsLocked,
            isClaimed: ent.Comp.IsClaimed,
            lockedView: (ent.Comp.IsLocked && ent.Comp.IsClaimed) || ent.Comp.AiLocked,
            showPeopleLocal: ent.Comp.ShowPeopleLocal,
            showMachinesLocal: ent.Comp.ShowMachinesLocal,
            showMachinesDetailLocal: ent.Comp.ShowMachinesDetailLocal,
            showItemsLocal: ent.Comp.ShowItemsLocal,
            showItemsDetailLocal: ent.Comp.ShowItemsDetailLocal,
            localItemMode: ent.Comp.LocalItemMode,
            globalVisionEnabled: ent.Comp.GlobalVisionEnabled,
            showPeopleGlobal: ent.Comp.ShowPeopleGlobal,
            showMachinesGlobal: ent.Comp.ShowMachinesGlobal,
            showMachinesDetailGlobal: ent.Comp.ShowMachinesDetailGlobal,
            showItemsGlobal: ent.Comp.ShowItemsGlobal,
            showItemsDetailGlobal: ent.Comp.ShowItemsDetailGlobal,
            globalItemMode: ent.Comp.GlobalItemMode,
            enabledCameraSubnets: ent.Comp.EnabledCameraSubnets.ToHashSet(),
            availableCameraSubnets: RefreshCameraNetwork(ent.Owner).AvailableSubnets,
            visionRange: ent.Comp.VisionRange,
            tokenSystem: counts.System / 4,
            tokenPersonLore: counts.PersonLore / 4,
            tokenCrewXeno: counts.CrewXeno / 4,
            tokenVision: counts.Vision / 4,
            tokenLocalVision: localVisionChars / 4,
            tokenGlobalVision: globalVisionChars / 4,
            tokenHistory: totalChars / 4,
            tokenContext: 0,
            cooldownBase: ent.Comp.CooldownBase,
            cooldownCharFactor: ent.Comp.CooldownCharFactor,
            cooldownMax: ent.Comp.CooldownMax,
            autoContinue: ent.Comp.AutoContinue,
            autoContinueThreshold: ent.Comp.AutoContinueThreshold,
            autoContinueMax: ent.Comp.AutoContinueMax,
            currentShipName: GetCurrentVesselName(ent),
            originalShipName: ent.Comp.OriginalShipName,
            constructionDate: ent.Comp.ConstructionDate,
            loadCount: ent.Comp.LoadCount,
            loadTimestampsDisplay: ent.Comp.LoadTimestamps.Count > 0
                ? string.Join(", ", ent.Comp.LoadTimestamps.TakeLast(5)) : "",
            ownershipHistory: ent.Comp.OwnershipHistory,
            aiLocked: ent.Comp.AiLocked,
            radioChannels: ent.Comp.RadioChannels,
            memories: ent.Comp.Memories
        );
        state.RefreshOnly = refreshOnly;
        _ui.SetUiState(ent.Owner, CoyoteAICoreUiKey.Config, state);
    }

    private static string FormatTime(TimeSpan time)
    {
        var totalSeconds = (int)time.TotalSeconds;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        if (hours > 0)
            return $"{hours}h {minutes}m {seconds}s";
        if (minutes > 0)
            return $"{minutes}m {seconds}s";
        return $"{seconds}s";
    }

    private string BuildSpeakerContext(EntityUid uid)
    {
        if (HasComp<VendingMachineComponent>(uid))
            return "Vending machine";
        if (!HasComp<HumanoidAppearanceComponent>(uid) && !HasComp<HandsComponent>(uid))
            return "machine";
        if (!TryComp<HandsComponent>(uid, out var hands))
            return string.Empty;
        var parts = new List<string>();
        foreach (var item in hands.Hands.Values)
        {
            if (item.HeldEntity.HasValue && Exists(item.HeldEntity.Value))
                parts.Add($"holding {Name(item.HeldEntity.Value)}");
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "empty-handed";
    }

    private static readonly string[] OrganItemPrefixes = { "brain", "positronic brain", "heart", "lungs", "liver", "kidneys", "appendix", "eyes", "tongue", "stomach" };

    /// <summary>
    ///     Builds the "NEARBY" vision block for the LLM system prompt.
    ///     Uses three separate entity queries for clean categorization:
    ///     1. MobStateComponent entities → CREW/MOBS (includes health, clothing, held items, markings)
    ///     2. ActivatableUIComponent entities → MACHINES/COMPUTERS (excludes mobs and items, aggregated by name)
    ///     3. ItemComponent entities → ITEMS (aggregated by name, with contraband level)
    ///     All entries are filtered by:
    ///     - Distance (VisionRange, 1-15m)
    ///     - Line-of-sight occlusion (InRangeUnOccluded)
    ///     - Visibility layer mask (blocks ghosts, etc.)
    ///     Duplicate machines/items are aggregated with "×N" notation and comma-separated distances.
    ///     Direction [N/NE/E/SE/S/SW/W/NW] is only shown for unique entities (count == 1).
    /// </summary>
    private string BuildLocalVisionBlock(EntityUid coreUid, CoyoteAICoreComponent core)
    {
        if (!TryComp<TransformComponent>(coreUid, out var coreXform))
            return string.Empty;

        var corePos = _xforms.GetWorldPosition(coreXform);
        var range = core.VisionRange;
        var coreVisMask = (int)VisibilityFlags.Normal;
        if (TryComp<EyeComponent>(coreUid, out var eye))
            coreVisMask = eye.VisibilityMask;
        var mobEntries = new List<string>();
        var machineEntries = new List<string>();
        var itemEntries = new List<string>();

        if (core.ShowPeopleLocal)
        {
            var query = EntityQueryEnumerator<MobStateComponent, TransformComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out var mobState, out var xform, out var meta))
            {
                if (uid == coreUid) continue;
                if (TryComp<VisibilityComponent>(uid, out var vis) && (vis.Layer & coreVisMask) == 0)
                    continue;
                if (string.IsNullOrEmpty(meta.EntityName)) continue;

                var pos = _xforms.GetWorldPosition(xform);
                var dist = (pos - corePos).Length();
                if (dist > range) continue;
                if (!_examine.InRangeUnOccluded(coreUid, uid, range))
                    continue;

                var name = meta.EntityName;
                if (IsOrganItem(name))
                    continue;

                var desc = meta.EntityDescription;
                var species = "";
                var age = 30;
                var job = "";
                var genderStr = "";
                var heightCm = 0f;
                var weightKg = 0f;
                var markings = new List<string>();
                var isHumanoid = false;
                var hasMind = _mind.TryGetMind(uid, out _, out _);

                if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
                {
                    isHumanoid = true;
                    species = humanoid.Species;
                    age = humanoid.Age;
                    genderStr = humanoid.Gender switch
                    {
                        Gender.Male => "he/him",
                        Gender.Female => "she/her",
                        Gender.Epicene => "they/them",
                        Gender.Neuter => "it/its",
                        _ => "unknown"
                    };
                    heightCm = _prototype.TryIndex<SpeciesPrototype>(humanoid.Species, out var speciesProto)
                        ? speciesProto.AverageHeight * humanoid.Height : 0f;

                    if (TryComp<PhysicsComponent>(uid, out var phys))
                        weightKg = phys.Mass;

                    foreach (var (category, markingList) in humanoid.MarkingSet.Markings)
                    {
                        foreach (var m in markingList)
                        {
                            if (m.MarkingId.StartsWith("Undergarment"))
                                continue;
                            markings.Add(ReadableMarkingName(m.MarkingId));
                        }
                    }
                }

                if (_mind.TryGetMind(uid, out var mindId, out var mindComp) &&
                    _roles.MindHasRole<JobRoleComponent>((mindId, mindComp), out var role) &&
                    role.Value.Comp1.JobPrototype.HasValue)
                {
                    job = _prototype.Index(role.Value.Comp1.JobPrototype.Value).LocalizedName;
                }

                var tag = hasMind && isHumanoid ? "CREW"
                    : HasComp<MobStateComponent>(uid) ? "MOB"
                    : "ITEM";

                if (tag == "ITEM" && desc != null && desc.Contains("artificial brain", StringComparison.OrdinalIgnoreCase))
                    continue;

                var details = new List<string>();
                var info = $"[{tag}] {name}";

                var bioParts = new List<string>();
                if (!string.IsNullOrEmpty(species))
                    bioParts.Add(species);
                if (!string.IsNullOrEmpty(job))
                    bioParts.Add(job);
                if (!string.IsNullOrEmpty(genderStr))
                    bioParts.Add(genderStr);
                if (age != 30)
                    bioParts.Add($"{age}yo");
                bioParts.Add($"at {dist:F0}m ({(int)pos.X}, {(int)pos.Y})");
                if (bioParts.Count > 0)
                    info += $" | {string.Join(", ", bioParts)}";
                if (heightCm > 0)
                    info += $" | {heightCm:F0}cm";
                if (weightKg > 0)
                    info += $" {weightKg:F0}kg";

                details.Add(info);

                if (!string.IsNullOrEmpty(desc))
                    details.Add($"  desc: \"{desc}\"");

                if (markings.Count > 0)
                    details.Add($"  markings: [{string.Join(", ", markings)}]");

                if (isHumanoid)
                {
                    var naked = true;
                    if (TryComp<InventoryComponent>(uid, out var inv))
                    {
                        var wornItems = new List<string>();
                        for (int i = 0; i < inv.Slots.Length && i < inv.Containers.Length; i++)
                        {
                            if (inv.Containers[i].ContainedEntity is { Valid: true } wornEntity)
                            {
                                var wornMeta = MetaData(wornEntity);
                                var wornName = wornMeta.EntityName;
                                var wornDesc = wornMeta.EntityDescription;
                                var contraband = GetContrabandLevel(wornEntity, wornDesc);
                                if (contraband >= 2)
                                    wornItems.Add($"{wornName}[!]");
                                else if (contraband > 0)
                                    wornItems.Add($"{wornName}[c{contraband}]");
                                else
                                    wornItems.Add(wornName);

                                var slotName = inv.Slots[i].Name;
                                if (slotName == "jumpsuit" || slotName == "outerClothing")
                                    naked = false;
                            }
                        }
                        if (wornItems.Count > 0)
                            details.Add($"  wearing: [{string.Join(", ", wornItems)}]");
                    }
                    if (naked)
                        details[0] = info + " | NAKED";
                }

                if (TryComp<HandsComponent>(uid, out var hands))
                {
                    var heldItems = new List<string>();
                    foreach (var item in hands.Hands.Values)
                    {
                        if (item.HeldEntity.HasValue && Exists(item.HeldEntity.Value))
                        {
                            var heldMeta = MetaData(item.HeldEntity.Value);
                            var heldName = heldMeta.EntityName;
                            if (IsOrganItem(heldName))
                                continue;
                            var heldDesc = heldMeta.EntityDescription;
                            var contraband = GetContrabandLevel(item.HeldEntity.Value, heldDesc);
                            if (contraband >= 2)
                                heldItems.Add($"{heldName}[!]");
                            else if (contraband > 0)
                                heldItems.Add($"{heldName}[c{contraband}]");
                            else
                                heldItems.Add(heldName);
                        }
                    }
                    if (heldItems.Count > 0)
                        details.Add($"  holding: [{string.Join(", ", heldItems)}]");
                }

                if (TryComp<DamageableComponent>(uid, out var damageable))
                {
                    var healthInfo = $"  health: {mobState.CurrentState} | ";
                    if (damageable.TotalDamage == 0)
                    {
                        healthInfo += "Undamaged and normal";
                    }
                    else
                    {
                        var damages = new List<string>();
                        foreach (var (type, amount) in damageable.Damage.DamageDict)
                        {
                            if (amount > 0)
                                damages.Add($"{amount} {type.ToLower()}");
                        }
                        healthInfo += string.Join(", ", damages);
                    }
                    details.Add(healthInfo);
                }

                mobEntries.Add(string.Join("\n", details));
            }
        }

        if (core.ShowMachinesLocal)
        {
            var machineData = new Dictionary<string, (int count, List<float> dists, Vector2 firstPos, string desc, bool powered)>();

            // Query 1: ActivatableUI entities (machines/computers)
            var machineQuery = EntityQueryEnumerator<ActivatableUIComponent, TransformComponent, MetaDataComponent>();
            while (machineQuery.MoveNext(out var uid, out var ui, out var xform, out var meta))
            {
                if (uid == coreUid) continue;
                if (HasComp<MobStateComponent>(uid) || HasComp<ItemComponent>(uid)) continue;
                if (TryComp<VisibilityComponent>(uid, out var mVis) && (mVis.Layer & coreVisMask) == 0)
                    continue;
                if (string.IsNullOrEmpty(meta.EntityName)) continue;

                var pos = _xforms.GetWorldPosition(xform);
                var dist = (pos - corePos).Length();
                if (dist > range) continue;
                if (!_examine.InRangeUnOccluded(coreUid, uid, range))
                    continue;

                var name = meta.EntityName;

                if (!machineData.ContainsKey(name))
                {
                    var desc = meta.EntityDescription ?? "";
                    var powered = TryComp<ApcPowerReceiverComponent>(uid, out var apc) && apc.Powered;
                    machineData[name] = (0, new List<float>(), pos, desc, powered);
                }

                var machineEntry = machineData[name];
                machineEntry.count++;
                machineEntry.dists.Add(dist);
                machineData[name] = machineEntry;
            }

            foreach (var (name, (count, dists, firstPos, desc, powered)) in machineData)
            {
                var sortedDists = dists.Distinct().OrderBy(d => d).ToList();
                var distStr = string.Join(", ", sortedDists.Select(d => $"{d:F0}m"));
                var line = $"[MACHINE] {name} | at {distStr} ({(int)firstPos.X}, {(int)firstPos.Y})";

                if (core.ShowMachinesDetailLocal)
                {
                    if (count == 1)
                    {
                        var dir = GetDirection(corePos, firstPos);
                        line += $" [{dir}]";
                    }
                    var machineDetails = new List<string>();
                    if (!string.IsNullOrEmpty(desc))
                        machineDetails.Add($"  desc: \"{desc}\"");
                    machineDetails.Add($"  powered: {(powered ? "yes" : "no")}");
                    line += "\n" + string.Join("\n", machineDetails);
                }

                machineEntries.Add(line);
            }
        }

        if (core.ShowItemsLocal)
        {
            if (core.LocalItemMode == ItemVisionMode.FeedAll)
            {
                // Feed All mode: full details for every item (current behavior)
                var itemData = new Dictionary<string, (int count, List<float> dists, Vector2 firstPos, string desc, int contraband, int stackAmount)>();
                var query = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out var item, out var xform, out var meta))
                {
                    if (string.IsNullOrEmpty(meta.EntityName)) continue;
                    var pos = _xforms.GetWorldPosition(xform);
                    var dist = (pos - corePos).Length();
                    if (dist > range) continue;
                    if (IsOrganItem(meta.EntityName)) continue;
                    if (TryComp<VisibilityComponent>(uid, out var iVis) && (iVis.Layer & coreVisMask) == 0) continue;
                    if (!_examine.InRangeUnOccluded(coreUid, uid, range)) continue;

                    var name = meta.EntityName;
                    if (!itemData.ContainsKey(name))
                    {
                        var desc = meta.EntityDescription ?? "";
                        var contraband = GetContrabandLevel(uid, desc);
                        var amount = TryComp<StackComponent>(uid, out var stack) ? stack.Count : 1;
                        itemData[name] = (0, new List<float>(), pos, desc, contraband, amount);
                    }
                    var entry = itemData[name];
                    entry.count++;
                    entry.dists.Add(dist);
                    itemData[name] = entry;
                }

                foreach (var (name, (count, dists, firstPos, desc, contraband, stackAmount)) in itemData)
                {
                    var sortedDists = dists.Distinct().OrderBy(d => d).ToList();
                    var distStr = string.Join(", ", sortedDists.Select(d => $"{d:F0}m"));
                    var countStr = count > 1 ? $" ×{count}" : "";
                    if (core.ShowItemsDetailLocal)
                    {
                        var itemDetails = new List<string>();
                        var header = $"[ITEM] {name}{countStr} | at {distStr}";
                        if (count == 1) { var dir = GetDirection(corePos, firstPos); header += $" [{dir}]"; }
                        itemDetails.Add(header);
                        if (!string.IsNullOrEmpty(desc)) itemDetails.Add($"  desc: \"{desc}\"");
                        itemDetails.Add($"  legality: {contraband}");
                        if (stackAmount > 1) itemDetails.Add($"  amount: {stackAmount}");
                        itemEntries.Add(string.Join("\n", itemDetails));
                    }
                    else
                        itemEntries.Add($"[ITEM] {name}{countStr} | at {distStr}");
                }
            }
            else if (core.LocalItemMode == ItemVisionMode.SmartSummary)
            {
                // Smart Summary: compact counts
                var itemCount = 0;
                var itemGroups = new Dictionary<string, int>();
                var query = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out var item, out var xform, out var meta))
                {
                    if (string.IsNullOrEmpty(meta.EntityName)) continue;
                    var pos = _xforms.GetWorldPosition(xform);
                    var dist = (pos - corePos).Length();
                    if (dist > range) continue;
                    if (IsOrganItem(meta.EntityName)) continue;
                    if (TryComp<VisibilityComponent>(uid, out var iVis) && (iVis.Layer & coreVisMask) == 0) continue;
                    if (!_examine.InRangeUnOccluded(coreUid, uid, range)) continue;

                    itemCount++;
                    if (!itemGroups.ContainsKey(meta.EntityName))
                        itemGroups[meta.EntityName] = 0;
                    itemGroups[meta.EntityName]++;
                }

                if (itemCount > 0)
                {
                    var summary = string.Join(", ", itemGroups.OrderByDescending(g => g.Value).Select(g => $"{g.Key} \u00d7{g.Value}"));
                    itemEntries.Add($"── ITEMS (Smart Summary) ──");
                    itemEntries.Add($"{itemCount} items: {summary}");
                    itemEntries.Add("Use {\"query_entity\": \"item name\"} for full details.");
                }
            }
            // Search Engine mode: show nothing, AI uses search_entity
        }

        // Combine sections
        var totalMachines = machineEntries.Count;
        var totalItems = itemEntries.Count;
        var visionCount = totalMachines + totalItems;

        var visionSummary = "";
        if (visionCount > 0)
        {
            var parts = new List<string>();
            if (totalMachines > 0) parts.Add($"{totalMachines} machines");
            if (totalItems > 0) parts.Add($"{totalItems} items");
            visionSummary = $"There's: {visionCount} object{(visionCount != 1 ? "s" : "")} inside your vision: {string.Join(", ", parts)}.\n\n";
        }

        var sections = new List<string>();
        if (mobEntries.Count > 0)
            sections.Add("── CREW/MOBS ──\n" + string.Join("\n---\n", mobEntries));
        if (machineEntries.Count > 0)
            sections.Add("── MACHINES/COMPUTERS ──\n" + string.Join("\n---\n", machineEntries));
        if (itemEntries.Count > 0)
            sections.Add(string.Join("\n", itemEntries));
        if (sections.Count == 0) return string.Empty;
        return "── LOCAL ──\n" + visionSummary + string.Join("\n\n", sections) + "\n";
    }

    private static bool FuzzyNameMatch(string entityName, string searchTerm)
    {
        if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(searchTerm))
            return false;
        var name = entityName.ToLowerInvariant().Trim();
        var term = searchTerm.ToLowerInvariant().Trim();
        if (name.Contains(term))
            return true;
        var distance = LevenshteinDistance(name, term);
        return distance <= Math.Max(2, term.Length * 0.3);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var lenA = a.Length;
        var lenB = b.Length;
        var matrix = new int[lenA + 1, lenB + 1];
        for (int i = 0; i <= lenA; i++) matrix[i, 0] = i;
        for (int j = 0; j <= lenB; j++) matrix[0, j] = j;
        for (int i = 1; i <= lenA; i++)
            for (int j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        return matrix[lenA, lenB];
    }

    private static string GetDirection(Vector2 from, Vector2 to)
    {
        var diff = to - from;
        var angle = Math.Atan2(-diff.Y, diff.X) * (180.0 / Math.PI);
        if (angle < 0) angle += 360;
        return angle switch
        {
            >= 337.5 or < 22.5 => "E",
            < 67.5 => "NE",
            < 112.5 => "N",
            < 157.5 => "NW",
            < 202.5 => "W",
            < 247.5 => "SW",
            < 292.5 => "S",
            _ => "SE"
        };
    }

    /// <summary>
    ///     Resolves an entity name from the LLM's "point_at" response field
    ///     and calls <see cref="PointingSystem.TryPointEntity"/> to spawn a
    ///     pointing arrow. Searches all entities by name (case-insensitive),
    ///     selecting the nearest visible one within vision range.
    ///     Uses rotateToFace: false because the AI core is anchored and cannot rotate.
    /// </summary>
    private void PointAtEntity(EntityUid pointer, string targetName, float visionRange)
    {
        if (!TryComp<TransformComponent>(pointer, out var coreXform))
            return;

        var corePos = _xforms.GetWorldPosition(coreXform);
        var coreVisMask = (int)VisibilityFlags.Normal;
        if (TryComp<EyeComponent>(pointer, out var eye))
            coreVisMask = eye.VisibilityMask;
        EntityUid? bestTarget = null;
        var bestDist = float.MaxValue;

        var query = EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var xform, out var meta))
        {
            if (uid == pointer) continue;
            if (string.IsNullOrEmpty(meta.EntityName)) continue;
            if (!meta.EntityName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryComp<VisibilityComponent>(uid, out var vis) && (vis.Layer & coreVisMask) == 0)
                continue;

            var pos = _xforms.GetWorldPosition(xform);
            var dist = (pos - corePos).Length();
            if (dist > visionRange) continue;

            if (!_examine.InRangeUnOccluded(pointer, uid, visionRange))
                continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = uid;
            }
        }

        if (bestTarget == null)
        {
            Log.Debug($"CoyoteAI: PointAtEntity - no matching entity '{targetName}' found in range");
            return;
        }

        _pointingSystem.TryPointEntity(pointer, bestTarget.Value, rotateToFace: false);

        Log.Debug($"CoyoteAI: {Name(pointer)} pointed at '{targetName}' ({bestTarget.Value})");
    }

    private static bool IsOrganItem(string name)
    {
        foreach (var prefix in OrganItemPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ReadableMarkingName(string markingId)
    {
        if (markingId.StartsWith("GenitalBreasts"))
        {
            var cup = ExtractCup(markingId);
            var type = markingId.Contains("Quad") ? "quadruple-nipple" :
                       markingId.Contains("Sextuple") ? "sextuple-nipple" :
                       markingId.Contains("Udders") ? "udders" : "breasts";
            return $"{type} ({cup})";
        }
        if (markingId.Contains("Vagina")) return "vagina";
        if (markingId.Contains("Penis") || markingId.Contains("Dick")) return "penis";
        if (markingId.Contains("Balls")) return "testicles";
        if (markingId.Contains("Chest"))
            return "chest markings";
        if (markingId.Contains("Tail")) return "tail";
        if (markingId.Contains("HeadTop") || markingId.Contains("Ear"))
            return "ears";
        if (markingId.Contains("Head")) return "head markings";
        if (markingId.Contains("Leg")) return "leg markings";
        if (markingId.Contains("Arm")) return "arm markings";
        return markingId;
    }

    private static string ExtractCup(string id)
    {
        if (id.Contains("A")) return "A"; if (id.Contains("B")) return "B";
        if (id.Contains("C")) return "C"; if (id.Contains("D")) return "D";
        if (id.Contains("E")) return "E"; if (id.Contains("F")) return "F";
        if (id.Contains("G")) return "G"; if (id.Contains("H")) return "H";
        if (id.Contains("I")) return "I"; if (id.Contains("J")) return "J";
        if (id.Contains("K")) return "K"; if (id.Contains("L")) return "L";
        if (id.Contains("M")) return "M"; if (id.Contains("N")) return "N";
        if (id.Contains("O")) return "O"; return "?";
    }

    /// <summary>
    ///     Maps ContrabandComponent.Severity to a 0-4 integer for the vision block.
    ///     Special case: "for authorized use only" in description → level 4 (Grand Theft tier).
    ///     The severity values are strings; they cover both standard SS14 tiers and
    ///     Coyote Frontier's extended contraband classification.
    /// </summary>
    private int GetContrabandLevel(EntityUid uid, string? description)
    {
        if (description != null && description.Contains("for authorized use only", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (!TryComp<ContrabandComponent>(uid, out var contraband))
            return 0;
        var severity = (string)contraband.Severity;
        return severity switch
        {
            "NotContraband" => 0,
            "Minor" or "Class1" => 1,
            "Major" or "Class2" or "Class2Expedition" or "Restricted" => 2,
            "Magical" or "Class3General" or "Class3Cult" or "Class3Pirate" or "Class3Syndicate" or "Class3Wizard" or "Class3Expedition" or "Class3MobHuman" or "Class3MobCreature" or "Class3MobConstruct" => 3,
            "GrandTheft" or "Syndicate" or "Class4General" or "PirateTarget" => 4,
            _ => 0
        };
    }

    private string BuildLawBlock(string lawSetId)
    {
        if (string.IsNullOrEmpty(lawSetId))
            return string.Empty;

        if (!_prototype.TryIndex<SiliconLawsetPrototype>(lawSetId, out var lawset))
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"── ACTIVE LAWSET: {lawSetId} ──");
        sb.AppendLine($"Authority: {lawset.ObeysTo}");
        foreach (var lawId in lawset.Laws)
        {
            if (!_prototype.TryIndex<SiliconLawPrototype>(lawId, out var law))
                continue;
            var lawText = Loc.GetString(law.LawString);
            sb.AppendLine($"- {lawText}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    public void ClearHistory(string coreId)
    {
        if (_histories.TryGetValue(coreId, out var history))
            history.Clear();
        _coreDelays.Remove(coreId);
        _rateLimiter.Reset(coreId);
    }

    // ── New helper methods ──

    private EntityUid? FindEntityByName(EntityUid coreUid, string targetName, float visionRange)
    {
        if (!TryComp<TransformComponent>(coreUid, out var coreXform))
            return null;
        var corePos = _xforms.GetWorldPosition(coreXform);
        var coreVisMask = (int)VisibilityFlags.Normal;
        if (TryComp<EyeComponent>(coreUid, out var eye))
            coreVisMask = eye.VisibilityMask;
        EntityUid? best = null;
        var bestDist = float.MaxValue;
        var query = EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var xform, out var meta))
        {
            if (uid == coreUid) continue;
            if (string.IsNullOrEmpty(meta.EntityName)) continue;
            if (!FuzzyNameMatch(meta.EntityName, targetName))
                continue;
            if (TryComp<VisibilityComponent>(uid, out var vis) && (vis.Layer & coreVisMask) == 0)
                continue;
            var pos = _xforms.GetWorldPosition(xform);
            var dist = (pos - corePos).Length();
            if (dist > visionRange) continue;
            if (!_examine.InRangeUnOccluded(coreUid, uid, visionRange))
                continue;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = uid;
            }
        }
        return best;
    }

    private void PruneMemories(CoyoteAICoreComponent core)
    {
        if (core.Memories.Count <= 100)
            return;
        core.Memories.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        core.Memories.RemoveRange(100, core.Memories.Count - 100);
    }

    // ── Export / Import ──

    private void OnExportRequest(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIExportMessage args)
    {
        var history = _histories.TryGetValue(ent.Comp.CoreId, out var h) ? h.ToList() : new();
        var export = new AICoreExportData
        {
            Version = 2,
            CoreId = ent.Comp.CoreId,
            AiName = ent.Comp.AiName,
            PersonalityPrompt = ent.Comp.PersonalityPrompt,
            LoreNotes = ent.Comp.LoreNotes,
            Temperature = ent.Comp.Temperature,
            ReasoningLevel = ent.Comp.ReasoningLevel,
            LawSet = ent.Comp.LawSet,
            Enabled = ent.Comp.Enabled,
            MaxHistoryLength = ent.Comp.MaxHistoryLength,
            MaxTokens = ent.Comp.MaxTokens,
            ShowPeopleLocal = ent.Comp.ShowPeopleLocal,
            ShowMachinesLocal = ent.Comp.ShowMachinesLocal,
            ShowMachinesDetailLocal = ent.Comp.ShowMachinesDetailLocal,
            ShowItemsLocal = ent.Comp.ShowItemsLocal,
            ShowItemsDetailLocal = ent.Comp.ShowItemsDetailLocal,
            LocalItemMode = ent.Comp.LocalItemMode,
            GlobalVisionEnabled = ent.Comp.GlobalVisionEnabled,
            ShowPeopleGlobal = ent.Comp.ShowPeopleGlobal,
            ShowMachinesGlobal = ent.Comp.ShowMachinesGlobal,
            ShowMachinesDetailGlobal = ent.Comp.ShowMachinesDetailGlobal,
            ShowItemsGlobal = ent.Comp.ShowItemsGlobal,
            ShowItemsDetailGlobal = ent.Comp.ShowItemsDetailGlobal,
            GlobalItemMode = ent.Comp.GlobalItemMode,
            EnabledCameraSubnets = new(ent.Comp.EnabledCameraSubnets),
            VisionRange = ent.Comp.VisionRange,
            CooldownBase = ent.Comp.CooldownBase,
            CooldownCharFactor = ent.Comp.CooldownCharFactor,
            CooldownMax = ent.Comp.CooldownMax,
            AutoContinue = ent.Comp.AutoContinue,
            AutoContinueThreshold = ent.Comp.AutoContinueThreshold,
            AutoContinueMax = ent.Comp.AutoContinueMax,
            LoadCount = ent.Comp.LoadCount,
            LoadTimestamps = new(ent.Comp.LoadTimestamps),
            OriginalShipName = ent.Comp.OriginalShipName,
            ConstructionDate = ent.Comp.ConstructionDate,
            OwnershipHistory = new(ent.Comp.OwnershipHistory),
            ConversationHistory = history,
            Memories = new(ent.Comp.Memories),
            ChannelLabels = ent.Comp.ChannelLabels.ToArray(),
            RadioChannels = ent.Comp.RadioChannels.ToArray(),
        };
        var json = System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
        RaiseNetworkEvent(new CoyoteAIExportResponseEvent { Yaml = json }, args.Actor);
    }

    private void OnImport(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIImportMessage args)
    {
        if (string.IsNullOrEmpty(args.DataJson)) return;
        AICoreExportData? data = null;
        try { data = System.Text.Json.JsonSerializer.Deserialize<AICoreExportData>(args.DataJson, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }); } catch { return; }
        if (data == null) return;
        if (data.Version < 1) return;

        ent.Comp.CoreId = data.CoreId;
        ent.Comp.AiName = data.AiName;
        ent.Comp.PersonalityPrompt = data.PersonalityPrompt;
        ent.Comp.LoreNotes = data.LoreNotes;
        ent.Comp.Temperature = Math.Clamp(data.Temperature, 0.1f, 2.0f);
        ent.Comp.ReasoningLevel = data.ReasoningLevel;
        ent.Comp.LawSet = data.LawSet;
        ent.Comp.Enabled = data.Enabled;
        ent.Comp.MaxHistoryLength = Math.Clamp(data.MaxHistoryLength, 5, 1000);
        ent.Comp.MaxTokens = Math.Max(data.MaxTokens, 1);
        ent.Comp.VisionRange = Math.Clamp(data.VisionRange, 1f, 15f);
        ent.Comp.CooldownBase = Math.Clamp(data.CooldownBase, 0.1f, 10f);
        ent.Comp.CooldownCharFactor = Math.Clamp(data.CooldownCharFactor, 0.001f, 0.5f);
        ent.Comp.CooldownMax = Math.Clamp(data.CooldownMax, 0.1f, 10f);
        ent.Comp.AutoContinue = data.AutoContinue;
        ent.Comp.AutoContinueThreshold = Math.Max(data.AutoContinueThreshold, 50);
        ent.Comp.AutoContinueMax = Math.Clamp(data.AutoContinueMax, 1, 10);
        ent.Comp.LoadCount = data.LoadCount;
        ent.Comp.LoadTimestamps = new(data.LoadTimestamps);
        ent.Comp.OriginalShipName = data.OriginalShipName;
        ent.Comp.ConstructionDate = data.ConstructionDate;
        ent.Comp.OwnershipHistory = new(data.OwnershipHistory);
        ent.Comp.Memories = new(data.Memories);
        ent.Comp.ChannelLabels = data.ChannelLabels ?? new string[20];
        if (data.RadioChannels != null && data.RadioChannels.Length > 0)
        {
            ent.Comp.RadioChannels = new HashSet<string>(data.RadioChannels);
            SyncRadioComponents(ent, ent.Comp);
        }

        // Vision fields: v2 uses dedicated fields, v1 maps old names
        if (data.Version >= 2)
        {
            ent.Comp.ShowPeopleLocal = data.ShowPeopleLocal;
            ent.Comp.ShowMachinesLocal = data.ShowMachinesLocal;
            ent.Comp.ShowMachinesDetailLocal = data.ShowMachinesDetailLocal;
            ent.Comp.ShowItemsLocal = data.ShowItemsLocal;
            ent.Comp.ShowItemsDetailLocal = data.ShowItemsDetailLocal;
            ent.Comp.LocalItemMode = data.LocalItemMode;
            ent.Comp.GlobalVisionEnabled = data.GlobalVisionEnabled;
            ent.Comp.ShowPeopleGlobal = data.ShowPeopleGlobal;
            ent.Comp.ShowMachinesGlobal = data.ShowMachinesGlobal;
            ent.Comp.ShowMachinesDetailGlobal = data.ShowMachinesDetailGlobal;
            ent.Comp.ShowItemsGlobal = data.ShowItemsGlobal;
            ent.Comp.ShowItemsDetailGlobal = data.ShowItemsDetailGlobal;
            ent.Comp.GlobalItemMode = data.GlobalItemMode;
            ent.Comp.EnabledCameraSubnets = new(data.EnabledCameraSubnets);
        }
        else
        {
            ent.Comp.ShowPeopleLocal = data.ShowPeople;
            ent.Comp.ShowMachinesLocal = data.ShowMachines;
            ent.Comp.ShowMachinesDetailLocal = data.ShowMachinesDetail;
            ent.Comp.ShowItemsLocal = data.ShowItems;
            ent.Comp.ShowItemsDetailLocal = data.ShowItemsDetail;
        }

        _histories[ent.Comp.CoreId] = new Queue<ChatEntry>(data.ConversationHistory);

        _metaData.SetEntityName(ent, $"VIGIL CORE-{data.AiName}");
        Dirty(ent);
        UpdateConfigUi(ent);
        Log.Info($"CoyoteAI: Imported AI '{data.AiName}' (core {data.CoreId}) with {data.ConversationHistory.Count} history entries");
    }

    // ── Memory CRUD ──

    private void OnAddMemory(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIAddMemoryMessage args)
    {
        ent.Comp.Memories.Add(new AICoreMemory
        {
            Id = Guid.NewGuid().ToString(),
            Content = args.Content,
            Priority = args.Priority,
            Tags = new(args.Tags),
            CreatedAt = FormatTime(_timing.CurTime),
            LastAccessedAt = FormatTime(_timing.CurTime)
        });
        PruneMemories(ent.Comp);
        Dirty(ent);
        UpdateConfigUi(ent);
    }

    private void OnRemoveMemory(Entity<CoyoteAICoreComponent> ent, ref CoyoteAIRemoveMemoryMessage args)
    {
        var removeId = args.MemoryId;
        ent.Comp.Memories.RemoveAll(m => m.Id == removeId);
        Dirty(ent);
        UpdateConfigUi(ent);
    }

    // ── Vision query/search handlers in InjectResponse ──

    private bool HandleVisionQuery(PendingResponse response, CoyoteAICoreComponent core)
    {
        var resp = response.Response;
        if (resp == null) return false;
        var handled = false;
        string? injectMessage = null;

        // query_entity — get full details of a named entity
        if (!string.IsNullOrEmpty(resp.QueryEntity))
        {
            var detail = BuildEntityDetailBlock(response.CoreUid, resp.QueryEntity, core);
            injectMessage = $"── QUERY: {resp.QueryEntity} ──\n{detail}";
            handled = true;
        }

        // search_entity — search by name across local + camera scopes
        if (!string.IsNullOrEmpty(resp.SearchEntity))
        {
            var results = SearchEntities(response.CoreUid, resp.SearchEntity, core);
            if (results.Count == 1)
            {
                var r = results[0];
                var tag = r.CameraName != null ? $"[CAMERA: {r.CameraName}]" : "[LOCAL]";
                var detail = BuildEntityDetailBlock(response.CoreUid, r.DisplayName, core);
                injectMessage = $"── SEARCH: {resp.SearchEntity} ──\n{tag} {detail}";
                _searchResults.Remove(response.CoreId);
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"── SEARCH: {resp.SearchEntity} ──");
                sb.AppendLine($"Found {results.Count} matches:");
                for (int i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    var tag = r.CameraName != null ? $"[CAMERA: {r.CameraName}]" : "[LOCAL]";
                    var dir = GetDirection(Transform(response.CoreUid).WorldPosition, r.Position);
                    sb.AppendLine($"  [S{i}] {tag} {r.DisplayName} | at {(r.Position - Transform(response.CoreUid).WorldPosition).Length():F0}m [{dir}] | ({(int)r.Position.X}, {(int)r.Position.Y})");
                }
                sb.AppendLine($"Select one with {{\"select_entity\": \"S0\"}}");
                _searchResults[response.CoreId] = results;
                injectMessage = sb.ToString();
            }
            handled = true;
        }

        // select_entity — pick from search results
        if (!string.IsNullOrEmpty(resp.SelectEntity))
        {
            if (_searchResults.TryGetValue(response.CoreId, out var results))
            {
                var prefix = "S";
                if (resp.SelectEntity.StartsWith(prefix) && int.TryParse(resp.SelectEntity.AsSpan(1), out var idx) && idx >= 0 && idx < results.Count)
                {
                    var selected = results[idx];
                    var tag = selected.CameraName != null ? $"[CAMERA: {selected.CameraName}]" : "[LOCAL]";
                    var detail = BuildEntityDetailBlock(response.CoreUid, selected.DisplayName, core);
                    injectMessage = $"── SELECTED: {selected.DisplayName} ──\n{tag} {detail}";
                }
                _searchResults.Remove(response.CoreId);
            }
            handled = true;
        }

        if (!handled || injectMessage == null)
            return false;

        // Inject the vision query result into conversation history and trigger a follow-up
        _busyCores.Remove(response.CoreId);

        if (_histories.TryGetValue(response.CoreId, out var history))
        {
            var queryEntry = new ChatEntry
            {
                Type = "vision",
                SpeakerName = "System",
                SpeakerSpecies = "",
                SpeakerJob = "",
                SpeakerAge = 0,
                Message = injectMessage,
                Timestamp = _timing.CurTime
            };
            history.Enqueue(queryEntry);
        }

        var shiftDuration = FormatTime(_timing.CurTime);
        var manifest = _manifest.GetCrewManifest();
        var speciesIds = _manifest.GetSpeciesOnStation();
        var speciesLoreBlock = _speciesLore.BuildLoreBlock(speciesIds);
        var lawBlock = BuildLawBlock(core.LawSet);
        var localVision = BuildLocalVisionBlock(response.CoreUid, core);
        var globalVision = BuildGlobalVisionBlock(response.CoreUid, core);
        var mergedVision = CombineVision(localVision, globalVision, core);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(core, manifest, speciesLoreBlock, lawBlock, mergedVision, shiftDuration, GetTimeSinceLastResponse(response.CoreId), GetCurrentVesselName((response.CoreUid, core)), out _);
        var followupHistory = _histories.TryGetValue(response.CoreId, out var h) ? new Queue<ChatEntry>(h) : new Queue<ChatEntry>();
        var followupTrigger = new ChatEntry
        {
            Type = "followup",
            SpeakerName = "System",
            SpeakerSpecies = "",
            SpeakerJob = "",
            SpeakerAge = 0,
            Message = "Vision query result provided above. You may respond now.",
            Timestamp = _timing.CurTime
        };
        followupHistory.Enqueue(followupTrigger);
        _busyCores.Add(response.CoreId);
        var followupUserPrompt = _promptBuilder.BuildUserPrompt(followupHistory, followupTrigger, shiftDuration, null);
        _pendingRequests.Enqueue(new PendingRequest
        {
            CoreUid = response.CoreUid,
            Core = core,
            SystemPrompt = systemPrompt,
            UserPrompt = followupUserPrompt
        });

        Log.Debug($"CoyoteAI: Vision query handled for core {response.CoreId}: {injectMessage[..Math.Min(injectMessage.Length, 80)]}");
        return true;
    }

    private string BuildEntityDetailBlock(EntityUid coreUid, string targetName, CoyoteAICoreComponent core)
    {
        var coreVisMask = (int)VisibilityFlags.Normal;
        if (TryComp<EyeComponent>(coreUid, out var eye))
            coreVisMask = eye.VisibilityMask;
        var corePos = Transform(coreUid).WorldPosition;
        var range = core.VisionRange;

        // Check mobs
        var mobQuery = EntityQueryEnumerator<MobStateComponent, TransformComponent, MetaDataComponent>();
        while (mobQuery.MoveNext(out var uid, out var mobState, out var xform, out var meta))
        {
            if (!FuzzyNameMatch(meta.EntityName, targetName)) continue;
            if (TryComp<VisibilityComponent>(uid, out var vis) && (vis.Layer & coreVisMask) == 0) continue;
            var pos = xform.WorldPosition;
            var dist = (pos - corePos).Length();
            if (dist > range) continue;
            if (!_examine.InRangeUnOccluded(coreUid, uid, range)) continue;

            var dir = GetDirection(corePos, pos);
            var species = TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) ? humanoid.Species.ToString() : "Unknown";
            var job = "";
            if (_mind.TryGetMind(uid, out var mindId, out var mindComp) && _roles.MindHasRole<JobRoleComponent>((mindId, mindComp), out var role) && role.Value.Comp1.JobPrototype.HasValue)
                job = _prototype.Index(role.Value.Comp1.JobPrototype.Value).LocalizedName;
            var healthInfo = "";
            if (TryComp<DamageableComponent>(uid, out var damageable))
                healthInfo = damageable.TotalDamage == 0 ? "Undamaged" : $"Damaged ({damageable.TotalDamage} total)";

            return $"[CREW] {meta.EntityName} | {species} | {job} | at {dist:F0}m [{dir}] | health: {mobState.CurrentState} | {healthInfo} | ({(int)pos.X}, {(int)pos.Y})";
        }

        // Check machines (ActivatableUI)
        var machineQuery = EntityQueryEnumerator<ActivatableUIComponent, TransformComponent, MetaDataComponent>();
        while (machineQuery.MoveNext(out var uid, out var ui, out var xform, out var meta))
        {
            if (!FuzzyNameMatch(meta.EntityName, targetName)) continue;
            if (HasComp<MobStateComponent>(uid) || HasComp<ItemComponent>(uid)) continue;
            if (TryComp<VisibilityComponent>(uid, out var vis) && (vis.Layer & coreVisMask) == 0) continue;
            var pos = xform.WorldPosition;
            var dist = (pos - corePos).Length();
            if (dist > range) continue;
            if (!_examine.InRangeUnOccluded(coreUid, uid, range)) continue;

            var dir = GetDirection(corePos, pos);
            var desc = meta.EntityDescription ?? "";
            var powered = TryComp<ApcPowerReceiverComponent>(uid, out var apc) && apc.Powered;
            return $"[MACHINE] {meta.EntityName} | at {dist:F0}m [{dir}] | desc: \"{desc}\" | powered: {(powered ? "yes" : "no")}";
        }

        // Check items
        var itemQuery = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
        while (itemQuery.MoveNext(out var uid, out var item, out var xform, out var meta))
        {
            if (!FuzzyNameMatch(meta.EntityName, targetName)) continue;
            if (string.IsNullOrEmpty(meta.EntityName) || IsOrganItem(meta.EntityName)) continue;
            if (TryComp<VisibilityComponent>(uid, out var vis) && (vis.Layer & coreVisMask) == 0) continue;
            var pos = xform.WorldPosition;
            var dist = (pos - corePos).Length();
            if (dist > range) continue;
            if (!_examine.InRangeUnOccluded(coreUid, uid, range)) continue;

            var dir = GetDirection(corePos, pos);
            var desc = meta.EntityDescription ?? "";
            var contraband = GetContrabandLevel(uid, desc);
            var amount = TryComp<StackComponent>(uid, out var stack) ? stack.Count : 1;
            var countStr = amount > 1 ? $" ×{amount}" : "";
            return $"[ITEM] {meta.EntityName}{countStr} | at {dist:F0}m [{dir}] | desc: \"{desc}\" | legality: {contraband} | ({(int)pos.X}, {(int)pos.Y})";
        }

        return $"Entity '{targetName}' not found in range.";
    }

    private List<SearchResult> SearchEntities(EntityUid coreUid, string searchTerm, CoyoteAICoreComponent core)
    {
        var results = new List<SearchResult>();
        var seen = new HashSet<EntityUid>();
        var coreVisMask = (int)VisibilityFlags.Normal;
        if (TryComp<EyeComponent>(coreUid, out var eye))
            coreVisMask = eye.VisibilityMask;

        // Local search: range + occlusion from core position
        if (TryComp<TransformComponent>(coreUid, out var coreXform))
        {
            var corePos = _xforms.GetWorldPosition(coreXform);
            var range = core.VisionRange;
            var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (uid == coreUid) continue;
                if (string.IsNullOrEmpty(meta.EntityName)) continue;
                if (!FuzzyNameMatch(meta.EntityName, searchTerm)) continue;
                if (TryComp<VisibilityComponent>(uid, out var vis) && (vis.Layer & coreVisMask) == 0) continue;
                var pos = _xforms.GetWorldPosition(xform);
                var dist = (pos - corePos).Length();
                if (dist > range) continue;
                if (!_examine.InRangeUnOccluded(coreUid, uid, range)) continue;

                if (!seen.Add(uid)) continue;
                results.Add(new SearchResult { Entity = uid, DisplayName = meta.EntityName, Position = pos });
            }
        }

        // Global camera search: range from each camera, no occlusion
        if (core.GlobalVisionEnabled)
        {
            var cache = RefreshCameraNetwork(coreUid);
            foreach (var cam in cache.Cameras)
            {
                if (!core.EnabledCameraSubnets.Contains(cam.SubnetId)) continue;
                var camRange = core.VisionRange;
                var camPos = cam.Position;
                var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var meta, out var xform))
                {
                    if (uid == coreUid) continue;
                    if (string.IsNullOrEmpty(meta.EntityName)) continue;
                    if (!FuzzyNameMatch(meta.EntityName, searchTerm)) continue;
                    var pos = _xforms.GetWorldPosition(xform);
                    var dist = (pos - camPos).Length();
                    if (dist > camRange) continue;

                    if (!seen.Add(uid)) continue;
                    results.Add(new SearchResult { Entity = uid, DisplayName = meta.EntityName, Position = pos, CameraName = cam.Name });
                }
            }
        }

        return results;
    }

    private string CombineVision(string localVision, string globalVision, CoyoteAICoreComponent core)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(localVision))
            parts.Add(localVision);
        if (core.GlobalVisionEnabled && !string.IsNullOrEmpty(globalVision))
            parts.Add(globalVision);
        return string.Join("\n", parts);
    }

    private CameraNetworkCache RefreshCameraNetwork(EntityUid coreUid)
    {
        if (_cameraCache.TryGetValue(coreUid, out var cache) &&
            _timing.CurTime - cache.LastRefreshed < TimeSpan.FromSeconds(30))
            return cache;

        cache = new CameraNetworkCache();

        if (!TryComp<TransformComponent>(coreUid, out var coreXform) || coreXform.GridUid == null)
        {
            cache.LastRefreshed = _timing.CurTime;
            _cameraCache[coreUid] = cache;
            return cache;
        }

        var gridUid = coreXform.GridUid.Value;
        var routers = new Dictionary<string, (string subnetName, uint frequency)>();

        var routerQuery = EntityQueryEnumerator<SurveillanceCameraRouterComponent, TransformComponent>();
        while (routerQuery.MoveNext(out var uid, out var router, out var xform))
        {
            if (xform.GridUid != gridUid) continue;
            if (!router.Active) continue;
            if (string.IsNullOrEmpty(router.SubnetFrequencyId)) continue;
            var name = router.SubnetName;
            if (string.IsNullOrEmpty(name))
                name = MakeSubnetNameReadable(router.SubnetFrequencyId);
            routers[router.SubnetFrequencyId] = (name, router.SubnetFrequency);
        }

        var subnetCameraCount = new Dictionary<string, int>();
        foreach (var subnetId in routers.Keys)
            subnetCameraCount[subnetId] = 0;

        var seenNames = new Dictionary<string, int>();
        var cameraList = new List<CameraEntry>();
        var cameraQuery = EntityQueryEnumerator<SurveillanceCameraComponent, TransformComponent, MetaDataComponent>();
        while (cameraQuery.MoveNext(out var uid, out var cam, out var xform, out var meta))
        {
            if (xform.GridUid != gridUid) continue;
            if (!cam.Active) continue;
            if (!TryComp<DeviceNetworkComponent>(uid, out var devNet)) continue;
            var freqId = devNet.ReceiveFrequencyId;
            if (string.IsNullOrEmpty(freqId) || !subnetCameraCount.ContainsKey(freqId)) continue;

            subnetCameraCount[freqId]++;

            var rawName = cam.NameSet && !string.IsNullOrEmpty(cam.CameraId)
                ? cam.CameraId
                : meta.EntityName;
            var pos = _xforms.GetWorldPosition(xform);

            if (!cam.NameSet || rawName == "camera")
            {
                rawName = $"camera ({(int)pos.X}, {(int)pos.Y})";
            }
            else
            {
                if (!seenNames.TryGetValue(rawName, out var count))
                    seenNames[rawName] = 1;
                else
                {
                    seenNames[rawName] = ++count;
                    rawName = $"{rawName} #{count}";
                }
            }

            cameraList.Add(new CameraEntry
            {
                Entity = uid,
                Name = rawName,
                SubnetId = freqId,
                SubnetName = routers.TryGetValue(freqId, out var r) ? r.subnetName : freqId,
                Position = pos
            });
        }

        cache.AvailableSubnets = subnetCameraCount;
        cache.Cameras = cameraList;
        cache.LastRefreshed = _timing.CurTime;
        _cameraCache[coreUid] = cache;
        return cache;
    }

    private string BuildGlobalVisionBlock(EntityUid coreUid, CoyoteAICoreComponent core)
    {
        if (!core.GlobalVisionEnabled || core.EnabledCameraSubnets.Count == 0)
            return string.Empty;

        var cache = RefreshCameraNetwork(coreUid);
        if (cache.Cameras.Count == 0)
            return string.Empty;

        var relevantCameras = cache.Cameras
            .Where(c => core.EnabledCameraSubnets.Contains(c.SubnetId))
            .ToList();

        if (relevantCameras.Count == 0)
            return string.Empty;

        var subnetCount = relevantCameras.Select(c => c.SubnetId).Distinct().Count();
        var modeLabel = core.GlobalItemMode switch
        {
            ItemVisionMode.SearchEngine => "Search Engine",
            ItemVisionMode.SmartSummary => "Smart Summary",
            _ => "Full Feed"
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"── GLOBAL VISION (CAMERAS) ({modeLabel}) ──");
        sb.AppendLine($"{{{relevantCameras.Count} cameras across {subnetCount} subnets}}");
        sb.AppendLine();

        var camRange = core.VisionRange;

        foreach (var subnetGroup in relevantCameras.GroupBy(c => c.SubnetName))
        {
            sb.AppendLine($"[SUBNET: {subnetGroup.Key}]");
            var camNum = 0;
            foreach (var cam in subnetGroup)
            {
                camNum++;
                var camPos = cam.Position;

                if (core.GlobalItemMode == ItemVisionMode.FeedAll)
                {
                    var camEntry = BuildLocalVisionBlockFromPosition(cam.Entity, camPos, core,
                        core.ShowPeopleGlobal, core.ShowMachinesGlobal, core.ShowMachinesDetailGlobal,
                        core.ShowItemsGlobal, core.ShowItemsDetailGlobal, core.GlobalItemMode, camRange);
                    if (!string.IsNullOrEmpty(camEntry))
                    {
                        sb.AppendLine($"  Camera #{camNum} '{cam.Name}' ({(int)camPos.X}, {(int)camPos.Y}):");
                        foreach (var line in camEntry.Split('\n'))
                            sb.AppendLine($"    {line}");
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine($"  Camera #{camNum} '{cam.Name}' ({(int)camPos.X}, {(int)camPos.Y}): nothing visible");
                        sb.AppendLine();
                    }
                    continue;
                }

                // Build mob details and counts
                var mobLines = new List<string>();
                var crewCount = 0;
                var mobCount = 0;
                if (core.ShowPeopleGlobal)
                {
                    var mobQuery = EntityQueryEnumerator<MobStateComponent, TransformComponent, MetaDataComponent>();
                    while (mobQuery.MoveNext(out var uid, out var mState, out var tx, out var meta))
                    {
                        if (uid == coreUid) continue;
                        if (string.IsNullOrEmpty(meta.EntityName)) continue;
                        if (IsOrganItem(meta.EntityName)) continue;
                        var pos = _xforms.GetWorldPosition(tx);
                        if ((pos - camPos).Length() > camRange) continue;

                        var isHumanoid = HasComp<HumanoidAppearanceComponent>(uid);
                        var hasMind = _mind.TryGetMind(uid, out _, out _);
                        var isCrew = isHumanoid && hasMind;
                        var tag = isCrew ? "CREW" : "MOB";

                        if (isCrew) crewCount++;
                        else mobCount++;

                        var dist = (pos - camPos).Length();
                        var mobParts = new List<string>();
                        var mobStr = $"[{tag}] {meta.EntityName}";

                        if (isHumanoid)
                        {
                            var species = "";
                            var heightCm = 0f;
                            var weightKg = 0f;
                            var markings = new List<string>();
                            if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
                            {
                                species = humanoid.Species;
                                if (_prototype.TryIndex<SpeciesPrototype>(humanoid.Species, out var speciesProto))
                                    heightCm = speciesProto.AverageHeight * humanoid.Height;
                                if (TryComp<PhysicsComponent>(uid, out var phys))
                                    weightKg = phys.Mass;
                                foreach (var (category, markingList) in humanoid.MarkingSet.Markings)
                                {
                                    foreach (var m in markingList)
                                    {
                                        if (m.MarkingId.StartsWith("Undergarment"))
                                            continue;
                                        markings.Add(ReadableMarkingName(m.MarkingId));
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(species))
                                mobParts.Add(species);
                            if (heightCm > 0)
                                mobParts.Add($"{heightCm:F0}cm");
                            if (weightKg > 0)
                                mobParts.Add($"{weightKg:F0}kg");
                            if (markings.Count > 0)
                                mobParts.Add($"markings: [{string.Join(", ", markings)}]");
                        }

                        mobParts.Add($"at {dist:F0}m ({(int)pos.X}, {(int)pos.Y})");
                        if (mobParts.Count > 0)
                            mobStr += " | " + string.Join(", ", mobParts);
                        mobLines.Add(mobStr);
                    }
                }

                var camLine = $"  Camera #{camNum} '{cam.Name}' ({(int)camPos.X}, {(int)camPos.Y}): {crewCount} crew, {mobCount} mob{(mobCount != 1 ? "s" : "")}";
                sb.AppendLine(camLine);
                if (mobLines.Count > 0)
                {
                    foreach (var ml in mobLines)
                        sb.AppendLine($"    {ml}");
                }

                // Build machine info for this camera
                var machineLine = string.Empty;
                if (core.ShowMachinesGlobal)
                {
                    if (core.ShowMachinesDetailGlobal)
                    {
                        // Show machine names with positions
                        var machineData = new Dictionary<string, (int count, Vector2 firstPos)>();
                        var mcQuery = EntityQueryEnumerator<ActivatableUIComponent, TransformComponent, MetaDataComponent>();
                        while (mcQuery.MoveNext(out var uid, out var ui, out var tx, out var meta))
                        {
                            if (uid == coreUid) continue;
                            if (HasComp<MobStateComponent>(uid) || HasComp<ItemComponent>(uid)) continue;
                            if (string.IsNullOrEmpty(meta.EntityName)) continue;
                            var pos = _xforms.GetWorldPosition(tx);
                            if ((pos - camPos).Length() > camRange) continue;

                            if (!machineData.ContainsKey(meta.EntityName))
                                machineData[meta.EntityName] = (0, pos);
                            var entry = machineData[meta.EntityName];
                            entry.count++;
                            if (entry.count == 1)
                                entry.firstPos = pos;
                            machineData[meta.EntityName] = entry;
                        }

                        if (machineData.Count > 0)
                        {
                            var parts = machineData
                                .OrderByDescending(kv => kv.Value.count)
                                .Select(kv =>
                                {
                                    var (count, firstPos) = kv.Value;
                                    var posStr = $"({(int)firstPos.X}, {(int)firstPos.Y})";
                                    return count > 1 ? $"{kv.Key} \u00d7{count} at {posStr}" : $"{kv.Key} at {posStr}";
                                });
                            machineLine = $"MACHINES: {string.Join(", ", parts)}";
                        }
                    }
                    else
                    {
                        var machineCount = 0;
                        var mcQuery = EntityQueryEnumerator<ActivatableUIComponent, TransformComponent>();
                        while (mcQuery.MoveNext(out var uid, out _, out var tx))
                        {
                            if (uid == coreUid) continue;
                            if (HasComp<MobStateComponent>(uid) || HasComp<ItemComponent>(uid)) continue;
                            var pos = _xforms.GetWorldPosition(tx);
                            if ((pos - camPos).Length() > camRange) continue;
                            machineCount++;
                        }
                        machineLine = $"{machineCount} machine{(machineCount != 1 ? "s" : "")}";
                    }
                }

                var itemLine = string.Empty;
                if (core.ShowItemsGlobal)
                {
                    if (core.GlobalItemMode == ItemVisionMode.SmartSummary)
                    {
                        // SmartSummary: show item names with counts
                        var itemGroups = new Dictionary<string, int>();
                        var itQuery = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
                        while (itQuery.MoveNext(out var uid, out var item, out var tx, out var meta))
                        {
                            if (string.IsNullOrEmpty(meta.EntityName)) continue;
                            var pos = _xforms.GetWorldPosition(tx);
                            if ((pos - camPos).Length() > camRange) continue;
                            if (IsOrganItem(meta.EntityName)) continue;

                            if (!itemGroups.ContainsKey(meta.EntityName))
                                itemGroups[meta.EntityName] = 0;
                            itemGroups[meta.EntityName]++;
                        }

                        if (itemGroups.Count > 0)
                        {
                            var summary = string.Join(", ", itemGroups.OrderByDescending(g => g.Value).Select(g => $"{g.Key} \u00d7{g.Value}"));
                            itemLine = $"items: {summary}";
                        }
                    }
                    else // SearchEngine: just count items
                    {
                        var itemCount = 0;
                        var itQuery = EntityQueryEnumerator<ItemComponent, TransformComponent>();
                        while (itQuery.MoveNext(out var uid, out _, out var tx))
                        {
                            var pos = _xforms.GetWorldPosition(tx);
                            if ((pos - camPos).Length() > camRange) continue;
                            itemCount++;
                        }
                        itemLine = $"{itemCount} items";
                    }
                }

                var statsParts = new List<string>();
                if (core.ShowMachinesGlobal && !string.IsNullOrEmpty(machineLine))
                    statsParts.Add(machineLine);
                if (core.ShowItemsGlobal && !string.IsNullOrEmpty(itemLine))
                    statsParts.Add(itemLine);
                if (statsParts.Count > 0)
                    sb.AppendLine($"    {string.Join(" | ", statsParts)}");
            }
            sb.AppendLine();
        }

        if (core.GlobalItemMode == ItemVisionMode.SearchEngine)
            sb.AppendLine("Use {\"search_entity\": \"object name\"} to search camera feeds.");
        else if (core.GlobalItemMode == ItemVisionMode.SmartSummary)
            sb.AppendLine("Use {\"query_entity\": \"object name\"} for details \u2014 results show which camera found it.");
        sb.AppendLine();

        return sb.ToString();
    }

    private string BuildLocalVisionBlockFromPosition(EntityUid sourceUid, Vector2 sourcePos, CoyoteAICoreComponent core,
        bool showPeople, bool showMachines, bool showMachinesDetail, bool showItems, bool showItemsDetail, ItemVisionMode itemMode, float camRange = 15f)
    {
        var mobEntries = new List<string>();
        var machineEntries = new List<string>();
        var itemEntries = new List<string>();

        if (showPeople)
        {
            var mobQuery = EntityQueryEnumerator<MobStateComponent, TransformComponent, MetaDataComponent>();
            while (mobQuery.MoveNext(out var uid, out var mobState, out var xform, out var meta))
            {
                if (uid == sourceUid) continue;
                if (string.IsNullOrEmpty(meta.EntityName)) continue;
                var pos = _xforms.GetWorldPosition(xform);
                var dist = (pos - sourcePos).Length();
                if (dist > camRange) continue;

                var tag = HasComp<HumanoidAppearanceComponent>(uid) ? "CREW" : "MOB";
                mobEntries.Add($"[{tag}] {meta.EntityName} | at {dist:F0}m ({(int)pos.X}, {(int)pos.Y})");
            }
        }

        if (showMachines)
        {
            var machineData = new Dictionary<string, (int count, List<float> dists, Vector2 firstPos, string desc, bool powered)>();
            var mcQuery = EntityQueryEnumerator<ActivatableUIComponent, TransformComponent, MetaDataComponent>();
            while (mcQuery.MoveNext(out var uid, out var ui, out var xform, out var meta))
            {
                if (uid == sourceUid) continue;
                if (HasComp<MobStateComponent>(uid) || HasComp<ItemComponent>(uid)) continue;
                if (string.IsNullOrEmpty(meta.EntityName)) continue;
                var pos = _xforms.GetWorldPosition(xform);
                var dist = (pos - sourcePos).Length();
                if (dist > camRange) continue;

                if (!machineData.ContainsKey(meta.EntityName))
                {
                    var desc = meta.EntityDescription ?? "";
                    var powered = TryComp<ApcPowerReceiverComponent>(uid, out var apc) && apc.Powered;
                    machineData[meta.EntityName] = (0, new List<float>(), pos, desc, powered);
                }
                var entry = machineData[meta.EntityName];
                entry.count++;
                entry.dists.Add(dist);
                if (entry.count == 1)
                    entry.firstPos = pos;
                machineData[meta.EntityName] = entry;
            }

            foreach (var (name, (count, dists, firstPos, desc, powered)) in machineData)
            {
                var sortedDists = dists.Distinct().OrderBy(d => d).ToList();
                var distStr = string.Join(", ", sortedDists.Select(d => $"{d:F0}m"));
                var line = $"[MACHINE] {name} | at {distStr} ({(int)firstPos.X}, {(int)firstPos.Y})";
                if (showMachinesDetail)
                {
                    var machineDetails = new List<string>();
                    if (!string.IsNullOrEmpty(desc))
                        machineDetails.Add($"  desc: \"{desc}\"");
                    machineDetails.Add($"  powered: {(powered ? "yes" : "no")}");
                    line += "\n" + string.Join("\n", machineDetails);
                }
                machineEntries.Add(line);
            }
        }

        if (showItems)
        {
            if (itemMode == ItemVisionMode.FeedAll)
            {
                var itemData = new Dictionary<string, (int count, List<float> dists, string desc, int contraband, int stackAmount)>();
                var itQuery = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
                while (itQuery.MoveNext(out var uid, out var item, out var xform, out var meta))
                {
                    if (string.IsNullOrEmpty(meta.EntityName)) continue;
                    var pos = _xforms.GetWorldPosition(xform);
                    var dist = (pos - sourcePos).Length();
                    if (dist > camRange) continue;
                    if (IsOrganItem(meta.EntityName)) continue;

                    if (!itemData.ContainsKey(meta.EntityName))
                    {
                        var desc = meta.EntityDescription ?? "";
                        var contraband = GetContrabandLevel(uid, desc);
                        var amount = TryComp<StackComponent>(uid, out var stack) ? stack.Count : 1;
                        itemData[meta.EntityName] = (0, new List<float>(), desc, contraband, amount);
                    }
                    var entry = itemData[meta.EntityName];
                    entry.count++;
                    entry.dists.Add(dist);
                    itemData[meta.EntityName] = entry;
                }

                foreach (var (name, (count, dists, desc, contraband, stackAmount)) in itemData)
                {
                    var sortedDists = dists.Distinct().OrderBy(d => d).ToList();
                    var distStr = string.Join(", ", sortedDists.Select(d => $"{d:F0}m"));
                    var countStr = count > 1 ? $" \u00d7{count}" : "";
                    var header = $"[ITEM] {name}{countStr} | at {distStr}";
                    if (showItemsDetail)
                    {
                        var itemDetails = new List<string> { header };
                        if (!string.IsNullOrEmpty(desc)) itemDetails.Add($"  desc: \"{desc}\"");
                        itemDetails.Add($"  legality: {contraband}");
                        if (stackAmount > 1) itemDetails.Add($"  amount: {stackAmount}");
                        itemEntries.Add(string.Join("\n", itemDetails));
                    }
                    else
                        itemEntries.Add(header);
                }
            }
            else if (itemMode == ItemVisionMode.SmartSummary)
            {
                var itemCount = 0;
                var itemGroups = new Dictionary<string, int>();
                var itQuery = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
                while (itQuery.MoveNext(out var uid, out var item, out var xform, out var meta))
                {
                    if (string.IsNullOrEmpty(meta.EntityName)) continue;
                    var pos = _xforms.GetWorldPosition(xform);
                    var dist = (pos - sourcePos).Length();
                    if (dist > camRange) continue;
                    if (IsOrganItem(meta.EntityName)) continue;

                    itemCount++;
                    if (!itemGroups.ContainsKey(meta.EntityName))
                        itemGroups[meta.EntityName] = 0;
                    itemGroups[meta.EntityName]++;
                }

                if (itemCount > 0)
                {
                    var summary = string.Join(", ", itemGroups.OrderByDescending(g => g.Value).Select(g => $"{g.Key} \u00d7{g.Value}"));
                    itemEntries.Add($"  ITEMS: {itemCount} items: {summary}");
                }
            }
        }

        var sections = new List<string>();
        if (mobEntries.Count > 0)
            sections.Add("CREW: " + string.Join(", ", mobEntries));
        if (machineEntries.Count > 0)
            sections.Add("MACHINES: " + string.Join(", ", machineEntries));
        if (itemEntries.Count > 0)
            sections.Add(string.Join("; ", itemEntries));

        return sections.Count > 0 ? string.Join(" | ", sections) : string.Empty;
    }

    private sealed class CameraNetworkCache
    {
        public Dictionary<string, int> AvailableSubnets = new();
        public List<CameraEntry> Cameras = new();
        public TimeSpan LastRefreshed;
    }

    private sealed class CameraEntry
    {
        public EntityUid Entity;
        public string Name = string.Empty;
        public string SubnetId = string.Empty;
        public string SubnetName = string.Empty;
        public Vector2 Position;
    }

    private static string MakeSubnetNameReadable(string subnetId)
    {
        if (string.IsNullOrEmpty(subnetId))
            return "Unknown";
        const string prefix = "SurveillanceCamera";
        if (subnetId.StartsWith(prefix))
        {
            var name = subnetId[prefix.Length..];
            if (string.IsNullOrEmpty(name))
                return "General";
            return name;
        }
        return subnetId;
    }

    private sealed class PendingRequest
    {
        public EntityUid CoreUid;
        public CoyoteAICoreComponent Core = default!;
        public string SystemPrompt = string.Empty;
        public string UserPrompt = string.Empty;
    }

    private sealed class PendingResponse
    {
        public EntityUid CoreUid;
        public string CoreId = string.Empty;
        public string AiName = string.Empty;
        public string SystemPrompt = string.Empty;
        public string UserPrompt = string.Empty;
        public LLMResponse? Response;
    }

    private sealed class SearchResult
    {
        public EntityUid Entity;
        public string DisplayName = string.Empty;
        public Vector2 Position;
        public string? CameraName;
    }
}
