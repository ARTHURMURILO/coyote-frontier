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
using Content.Shared.Access.Components;
using Content.Shared.Chat;
using Content.Shared.Damage;
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
using Content.Shared.Silicons.Laws;
using Content.Shared.UserInterface;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
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


    private readonly Dictionary<string, Queue<ChatEntry>> _histories = new();
    private readonly ConcurrentQueue<PendingRequest> _pendingRequests = new();
    private readonly ConcurrentQueue<PendingResponse> _pendingResponses = new();
    private readonly Dictionary<string, TimeSpan> _coreDelays = new();
    private readonly Dictionary<(string, int), TimeSpan> _pulseEndTimes = new();
    private readonly HashSet<(EntityUid, string)> _recentlyProcessed = new();
    private readonly HashSet<string> _busyCores = new();
    private readonly Dictionary<string, List<ChatEntry>> _pendingBatches = new();
    private readonly Dictionary<string, int> _autoContinueCounts = new();
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
        SubscribeLocalEvent<CoyoteAICoreComponent, BoundUserInterfaceMessageAttempt>(OnBuiAttempt);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIToggleLockMessage>(OnToggleLock);
        SubscribeLocalEvent<CoyoteAICoreComponent, CoyoteAIUnclaimMessage>(OnUnclaim);
        SubscribeLocalEvent<CoyoteAICoreComponent, InteractUsingEvent>(OnInteractUsing);
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

        // Sound only plays when a new LLM call actually starts, not during batch accumulation
        if (_timing.CurTime >= core.NextSound)
        {
            core.NextSound = _timing.CurTime + core.SoundCooldown;
            _audio.PlayPvs(core.PromptSound, uid);
        }

        _busyCores.Add(core.CoreId);

        var shiftDuration = FormatTime(_timing.CurTime);
        var timeSinceLast = GetTimeSinceLastResponse(core.CoreId);
        var manifest = _manifest.GetCrewManifest();
        var speciesIds = _manifest.GetSpeciesOnStation();
        var speciesLoreBlock = _speciesLore.BuildLoreBlock(speciesIds);
        var lawBlock = BuildLawBlock(core.LawSet);
        var visionBlock = BuildVisionBlock(uid, core);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(core, manifest, speciesLoreBlock, lawBlock, visionBlock, shiftDuration, timeSinceLast, out _);
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
            return;
        }
        if (!response.Response.ShouldRespond)
        {
            Log.Debug($"CoyoteAI: Inject skipped, ShouldRespond=false for core {response.CoreId}");
            return;
        }

        if (!Exists(response.CoreUid))
        {
            Log.Error($"CoyoteAI: Core entity {response.CoreUid} no longer exists");
            return;
        }

        if (!TryComp<CoyoteAICoreComponent>(response.CoreUid, out var core))
        {
            Log.Error($"CoyoteAI: Core component missing on {response.CoreUid}");
            return;
        }

        var message = response.Response.Message;
        if (string.IsNullOrEmpty(message))
        {
            Log.Debug($"CoyoteAI: Empty message from LLM for core {response.CoreId}");
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

        if (!string.IsNullOrEmpty(response.Response.Action) && !string.IsNullOrEmpty(response.Response.ActionChannel))
        {
            Log.Debug($"CoyoteAI: Action triggered on channel '{response.Response.ActionChannel}': {response.Response.Action}");
            var action = response.Response.Action.ToLowerInvariant();
            var channelName = response.Response.ActionChannel.ToLowerInvariant();
            var channelIdx = Array.FindIndex(CoyoteAICoreComponent.LogicChannelNames, n => n == channelName);
            if (channelIdx >= 0)
            {
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
                var signal = action == "off" ? false : true;
                _deviceLink.SendSignal(response.CoreUid, portName, signal);
            }
        }

        if (!string.IsNullOrEmpty(response.Response.PointAt))
        {
            PointAtEntity(response.CoreUid, response.Response.PointAt, core.VisionRange);
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
            case "ShowPeople": ent.Comp.ShowPeople = args.Value; break;
            case "ShowMachines": ent.Comp.ShowMachines = args.Value; break;
            case "ShowMachinesDetail": ent.Comp.ShowMachinesDetail = args.Value; break;
            case "ShowItems": ent.Comp.ShowItems = args.Value; break;
            case "ShowItemsDetail": ent.Comp.ShowItemsDetail = args.Value; break;
        }
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
                var visionBlock = BuildVisionBlock(uid, core);
                var history = _histories.TryGetValue(coreId, out var h) ? h : new Queue<ChatEntry>();
                var userPrompt = batch.Count == 1
                    ? _promptBuilder.BuildUserPrompt(history, batch[0], shiftDuration, null)
                    : _promptBuilder.BuildBatchPrompt(history, batch, shiftDuration);
                var systemPrompt = _promptBuilder.BuildSystemPrompt(core, manifest, speciesLoreBlock, lawBlock, visionBlock, shiftDuration, GetTimeSinceLastResponse(coreId), out _);

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

        if (!ent.Comp.IsClaimed)
        {
            ent.Comp.OwnerId = swiperName;
            ent.Comp.OwnerName = swiperName;
            ent.Comp.IsLocked = false;
            Dirty(ent);
            UpdateOwnerDescription(ent);
            UpdateConfigUi(ent);
            return;
        }

        if (ent.Comp.IsLocked && ent.Comp.OwnerName != swiperName)
            return;

        if (ent.Comp.OwnerName == swiperName)
        {
            ent.Comp.IsLocked = !ent.Comp.IsLocked;
            Dirty(ent);
            UpdateOwnerDescription(ent);
            UpdateConfigUi(ent);
        }
    }

    private void UpdateOwnerDescription(Entity<CoyoteAICoreComponent> ent)
    {
        var desc = "A VIGIL (Vessel Intelligence & General Integration Layer) core unit. An experimental AI core based on the LLM API system (OpenAI compatible API) by the now bankrupt 'ClosedAI' and their partner 'MicroSloopy', this core gives the LLM all the tools to be able to interact with the world with features such as pointing, speaking, radio, logic channels, vision, descriptions, crew manifest, Story/politic books and other features allowing for your lonely ship to be less lonely! (quality will vary with LLM model used)";
        if (ent.Comp.IsClaimed)
        {
            desc += $"\n\nRegistered to: {ent.Comp.OwnerName}";
            desc += $"\nStatus: {(ent.Comp.IsLocked ? "Locked" : "Unlocked")}";
        }
        _metaData.SetEntityDescription(ent, desc);
    }

    private void OnBuiAttempt(Entity<CoyoteAICoreComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (args.Message is CoyoteAIToggleLockMessage or CoyoteAIUnclaimMessage)
        {
            if (!ent.Comp.IsClaimed || !HasOwnerIdCard(args.Actor, ent.Comp.OwnerName))
                args.Cancel();
        }
        if (args.Message is CoyoteAIUnclaimMessage && ent.Comp.IsLocked)
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
        if (ent.Comp.IsLocked) return;
        ent.Comp.OwnerId = string.Empty;
        ent.Comp.OwnerName = string.Empty;
        ent.Comp.IsLocked = false;
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
        var visionBlock = BuildVisionBlock(ent.Owner, ent.Comp);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(ent.Comp, manifest, speciesLoreBlock, lawBlock, visionBlock, shiftDuration, "N/A", out var counts);
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
            ownerName: ent.Comp.OwnerName,
            isLocked: ent.Comp.IsLocked,
            isClaimed: ent.Comp.IsClaimed,
            lockedView: ent.Comp.IsLocked && ent.Comp.IsClaimed,
            showPeople: ent.Comp.ShowPeople,
            showMachines: ent.Comp.ShowMachines,
            showMachinesDetail: ent.Comp.ShowMachinesDetail,
            showItems: ent.Comp.ShowItems,
            showItemsDetail: ent.Comp.ShowItemsDetail,
            visionRange: ent.Comp.VisionRange,
            tokenSystem: counts.System / 4,
            tokenPersonLore: counts.PersonLore / 4,
            tokenCrewXeno: counts.CrewXeno / 4,
            tokenVision: counts.Vision / 4,
            tokenHistory: totalChars / 4,
            tokenContext: 0,
            cooldownBase: ent.Comp.CooldownBase,
            cooldownCharFactor: ent.Comp.CooldownCharFactor,
            cooldownMax: ent.Comp.CooldownMax,
            autoContinue: ent.Comp.AutoContinue,
            autoContinueThreshold: ent.Comp.AutoContinueThreshold,
            autoContinueMax: ent.Comp.AutoContinueMax
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
    private string BuildVisionBlock(EntityUid coreUid, CoyoteAICoreComponent core)
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

        if (core.ShowPeople)
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
                bioParts.Add($"at {dist:F0}m");
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

        if (core.ShowMachines)
        {
            var machineData = new Dictionary<string, (int count, List<float> dists, Vector2 firstPos, string desc, bool powered)>();

            var query = EntityQueryEnumerator<ActivatableUIComponent, TransformComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out var ui, out var xform, out var meta))
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

                var entry = machineData[name];
                entry.count++;
                entry.dists.Add(dist);
                machineData[name] = entry;
            }

            foreach (var (name, (count, dists, firstPos, desc, powered)) in machineData)
            {
                var sortedDists = dists.Distinct().OrderBy(d => d).ToList();
                var distStr = string.Join(", ", sortedDists.Select(d => $"{d:F0}m"));
                var line = $"[MACHINE] {name} | at {distStr}";

                if (core.ShowMachinesDetail)
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

        if (core.ShowItems)
        {
            var itemData = new Dictionary<string, (int count, List<float> dists, Vector2 firstPos, string desc, int contraband, int stackAmount)>();

            var query = EntityQueryEnumerator<ItemComponent, TransformComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out var item, out var xform, out var meta))
            {
                if (string.IsNullOrEmpty(meta.EntityName)) continue;

                var pos = _xforms.GetWorldPosition(xform);
                var dist = (pos - corePos).Length();
                if (dist > range) continue;
                if (IsOrganItem(meta.EntityName)) continue;
                if (TryComp<VisibilityComponent>(uid, out var iVis) && (iVis.Layer & coreVisMask) == 0)
                    continue;
                if (!_examine.InRangeUnOccluded(coreUid, uid, range))
                    continue;

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

                if (core.ShowItemsDetail)
                {
                    var itemDetails = new List<string>();
                    var header = $"[ITEM] {name}{countStr} | at {distStr}";
                    if (count == 1)
                    {
                        var dir = GetDirection(corePos, firstPos);
                        header += $" [{dir}]";
                    }
                    itemDetails.Add(header);
                    if (!string.IsNullOrEmpty(desc))
                        itemDetails.Add($"  desc: \"{desc}\"");
                    itemDetails.Add($"  legality: {contraband}");
                    if (stackAmount > 1)
                        itemDetails.Add($"  amount: {stackAmount}");
                    itemEntries.Add(string.Join("\n", itemDetails));
                }
                else
                {
                    itemEntries.Add($"[ITEM] {name}{countStr} | at {distStr}");
                }
            }
        }

        var sections = new List<string>();
        if (mobEntries.Count > 0)
            sections.Add("── CREW/MOBS ──\n" + string.Join("\n---\n", mobEntries));
        if (machineEntries.Count > 0)
            sections.Add("── MACHINES/COMPUTERS ──\n" + string.Join("\n---\n", machineEntries));
        if (itemEntries.Count > 0)
            sections.Add("── ITEMS ──\n" + string.Join("\n---\n", itemEntries));
        if (sections.Count == 0) return string.Empty;
        return "── NEARBY ──\n" + string.Join("\n\n", sections) + "\n";
    }

    private static string GetDirection(Vector2 from, Vector2 to)
    {
        var diff = to - from;
        var angle = Math.Atan2(diff.Y, diff.X) * (180.0 / Math.PI);
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
}
