# Guide: Adding a Custom UI (Bound User Interface)

> Answer to: "How can one add a new UI — a window with two panels side by side (John and Cindy with
> their sprites) and a button list below ('Kiss', 'Hug') that John can click to act on Cindy?"
> Facts from source; inferred items marked. Line numbers valid for branch `palm3` commit `4bfdad813c`.

## TL;DR

This is a **BUI (Bound User Interface)** opened on the *target* entity (Cindy) with the actor (John)
as the registered user. Model it on the stripping UI: shared UI key/state/message, a server system that
opens the UI from a verb and handles messages, and a client `BoundUserInterface` + XAML window. Render
both players' live sprites with `SpriteView.SetEntity(...)`.

## Architecture summary

| Piece | Where | Notes |
|---|---|---|
| `UserInterfaceComponent` | `RobustToolbox/Robust.Shared/GameObjects/Components/UserInterface/UserInterfaceComponent.cs` | `Interfaces` dictionary (`InterfaceData`: `ClientType`, `InteractionRange`, `RequireInputValidation`), `Actors`, `States`; `BoundUserInterfaceState`/`Message`/`BoundUIWrapMessage` |
| `SharedUserInterfaceSystem` | `RobustToolbox/Robust.Shared/GameObjects/Systems/SharedUserInterfaceSystem.cs` | `TryOpenUi :645`, `OpenUi :670`, `SetUiState :727`, `CloseUi :583`; message entry `OnMessageReceived :93-147` (validates + raises `RaiseLocalEvent(uid, message, true)` on the UI owner); `EnsureClientBui :514-547` constructs the client class named by `ClientType` |
| Client BUI base | `RobustToolbox/Robust.Shared/GameObjects/Components/UserInterface/BoundUserInterface.cs` | `Owner`, `UiKey`, `UpdateState`, `SendMessage`, `SendPredictedMessage`; `PlayerManager.LocalEntity` available |
| Window helper | `RobustToolbox/Robust.Client/UserInterface/BoundUserInterfaceExt.cs:22-36` | `this.CreateWindow<T>()` wires close/position |
| `ActivatableUIComponent`/System | `Content.Shared/UserInterface/ActivatableUIComponent.cs`, `ActivatableUISystem.cs:180-248` | Standard open path with attempt events; `VerbOnly` makes it a verb |
| Input validation | `Content.Shared/Interaction/SharedInteractionSystem.cs:160-197` | Range/`CanInteract`/`SingleUser` checks when `RequireInputValidation` (default true) |
| BUI prediction | `Content.Client/UserInterface/BuiPredictionState.cs:27` | For predicted messages |

`InterfaceData.ClientType` must be the exact client class name; the class must have a
`(EntityUid owner, Enum uiKey)` constructor and be marked `[UsedImplicitly]` so it survives release builds.

## Traced examples

### A. Strippable — open-on-other via verb (best model for John→Cindy)
- YAML attached to all humanoids: `Resources/Prototypes/Entities/Mobs/Species/base.yml:196-202`.
- Shared component/key/messages/do-after: `Content.Shared/Strip/Components/StrippableComponent.cs`
  (component :7-16, `StrippingUiKey` :18-22, `StrippingSlotButtonPressed` :24-29, `StrippableDoAfterEvent` :79-94).
- Shared system `Content.Shared/Strip/SharedStrippableSystem.cs`:
  `GetVerbsEvent<Verb>` (:59-73) → `TryOpenStrippingUi` (:654-664) calls
  `_ui.OpenUi(target.Owner, StrippingUiKey.Key, user)` — **UI opens on the target, actor is the user**;
  message handler `SubscribeLocalEvent<StrippableComponent, StrippingSlotButtonPressed>` (:47) uses
  `args.Actor`; do-after with `AttemptFrequency.EveryTick` re-validation (:571-592).
- Client BUI `Content.Client/Inventory/StrippableBoundUserInterface.cs` (window :68,
  `SendPredictedMessage` :144); window `Content.Client/Strip/StrippingMenu.cs`.

### B. HumanoidMarkingModifier — admin verb + `SetUiState` on open
`Content.Server/Humanoid/Systems/HumanoidAppearanceSystem.Modifier.cs:16-46`: verb → admin gate →
`_uiSystem.OpenUi(uid, HumanoidMarkingModifierKey.Key, actor.PlayerSession)` then `SetUiState` (:35-43);
handlers validate `message.Actor` (:48-103). Key/messages/state:
`Content.Shared/Humanoid/SharedHumanoidMarkingModifierSystem.cs`.

### C. CharacterDetailWindow — client-only target window with sprite + network events
`Content.Client/Examine/CharacterExamineSystem.cs:29-73` (client-exclusive verb, clothing init :57-59,
`window.SetPreviewEntity(uid)` :61, sends `RequestCharacterInfoEvent` :72); server reply
`Content.Server/Examine/CharacterInfoSystem.cs`; XAML `CharacterDetailWindow.xaml:54-61` (`SpriteView`).
This is a **non-BUI** alternative when only the local player needs the window.

### D. Borg menu — entity sprite inside a BUI
`Content.Client/Silicons/Borgs/BorgBoundUserInterface.cs:23` (`_menu.SetEntity(Owner)`),
`BorgMenu.xaml.cs:59-76`, shared state `Content.Shared/Silicons/Borgs/BorgUI.cs`.

There is **no existing BUI that shows two live player sprites side by side** — the pieces above prove
both halves independently.

## Recipe — "John & Cindy" window (fork `_PS`)

### File layout

```
Content.Shared/_PS/Social/SocialUi.cs                       # key, state, messages, do-after event
Content.Shared/_PS/Social/SocialTargetComponent.cs          # marker component on interactable mobs
Content.Server/_PS/Social/SocialSystem.cs                   # verbs, open, message handling, do-after
Content.Client/_PS/Social/SocialBoundUserInterface.cs       # BUI class
Content.Client/_PS/Social/SocialWindow.xaml(.cs)            # two panels + button row
Resources/Locale/en-US/_PS/social/social.ftl                # strings
```

### Step 1 — shared UI key/state/messages

- `[Serializable, NetSerializable] public enum SocialUiKey : byte { Key }`
- `SocialUiState : BoundUserInterfaceState` — `NetEntity User, Target; string UserName, TargetName;`
  (names via `Identity.Name`). Never put `EntityUid` in state; use `NetEntity`.
- `[Serializable, NetSerializable] public enum SocialAction : byte { Kiss, Hug }`
- `SocialVerbMessage : BoundUserInterfaceMessage { SocialAction Action; }`
- `SocialDoAfterEvent : DoAfterEvent` with `[DataField] SocialAction Action`, implement `Clone()`; the
  target comes from `Args.Target` (`DoAfterEvent.cs:32-35`).

### Step 2 — server system

Subscriptions:
```csharp
SubscribeLocalEvent<SocialTargetComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
SubscribeLocalEvent<SocialTargetComponent, SocialVerbMessage>(OnVerbMessage);
SubscribeLocalEvent<SocialDoAfterEvent>(OnDoAfterFinished);
```
- `OnGetVerbs`: require `args.Target != args.User`, `CanAccess`, `CanInteract`, hands; add a `Verb`
  (`Category = VerbCategory.Interaction`) whose `Act` calls `OpenWindow(uid, args.User)`
  (pattern `SharedStrippableSystem.cs:59-73`).
- `OpenWindow`: `_ui.SetUiState(uid, SocialUiKey.Key, BuildState(uid, user));`
  `_ui.OpenUi(uid, SocialUiKey.Key, user);` (order per `HumanoidAppearanceSystem.Modifier.cs:35-43`).
- `OnVerbMessage(Entity<SocialTargetComponent> target, ref SocialVerbMessage args)`:
  actor = `args.Actor`; target = `target.Owner`; validate interact/alive/consent/cooldown; start
  `DoAfterArgs` (`BreakOnDamage`, `BreakOnMove`, `NeedHand = false`) with `SocialDoAfterEvent`; use
  `AttemptFrequency.EveryTick` + `DoAfterAttemptEvent<SocialDoAfterEvent>` if you need continuous checks.
- `OnDoAfterFinished`: re-check range/consent, then `PopupPredicted`/`PopupEntity` and optionally
  `_chat.TryEmoteWithChat(actor, "Kiss", ChatTransmitRange.Normal)`
  (`Content.Server/Chat/Systems/ChatSystem.Emote.cs:60-95`).
- Consent (this fork): `ConsentSystem.HasConsent` (server, `Content.Server/Consent/ConsentSystem.cs:49`);
  reuse `NSFWDescriptions` or add a new toggle in `Resources/Prototypes/consent.yml`. See
  `.ai/systems/consent-and-erp.md`.

### Step 3 — client BUI

```csharp
[UsedImplicitly]
public sealed class SocialBoundUserInterface : BoundUserInterface
{
    private SocialWindow? _window;
    public SocialBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }
    protected override void Open()
    {
        _window = this.CreateWindow<SocialWindow>();
        _window.SetTarget(Owner);                     // Cindy
        _window.SetUser(PlayerManager.LocalEntity);   // John
        _window.KissButton.OnPressed += _ => SendMessage(new SocialVerbMessage(SocialAction.Kiss));
        _window.HugButton.OnPressed  += _ => SendMessage(new SocialVerbMessage(SocialAction.Hug));
    }
    protected override void UpdateState(BoundUserInterfaceState state) { /* update names/counters */ }
}
```
- `SpriteView.SetEntity(EntityUid)` renders the live client entity; call
  `ClientClothingSystem.InitClothing` first for other players' clothing (see
  `CharacterExamineSystem.cs:57-59`). No dummy entity needed; appearance changes update automatically.
- Use `SendMessage` for authoritative-only actions; `SendPredictedMessage` only if the handler is
  prediction-safe (guard shared handlers with `if (_net.IsClient) return;`).

### Step 4 — XAML window

`FancyWindow` root (see `BorgMenu.xaml.cs`): `BoxContainer Vertical` containing a horizontal
`BoxContainer` with two vertical panels (`Label` + `SpriteView` per side, e.g. `Scale="4 4"`), then a
horizontal button row (`KissButton`, `HugButton`). `[GenerateTypedNameReferences]` generates fields;
`RobustXamlLoader.Load(this)` in the ctor. XAML is auto-included by `Content.Client.csproj:33`.

### Step 5 — YAML attachment

Attach to humanoid base (`Resources/Prototypes/Entities/Mobs/Species/base.yml`, near :196-202):
```yaml
- type: SocialTarget
- type: UserInterface
  interfaces:
    enum.SocialUiKey.Key:
      type: SocialBoundUserInterface
      interactionRange: -1        # optional: no auto-close while socializing
```
Alternatives:
- Add `ActivatableUI { key: enum.SocialUiKey.Key, verbOnly: true, verbText: social-verb-open }` and let
  `ActivatableUISystem` open it; set the state on `BeforeActivatableUIOpenEvent` (pattern
  `PaperSystem.cs:92-96`). Do **not** set `singleUser: true` for a social window.
- EE InteractionVerbs alternative: give mobs `InteractionVerbs { allowedVerbs: [OpenSocialUI] }`, define
  an `Interaction` prototype (see `Resources/Prototypes/Interactions/mood_interactions.yml` for
  `Hug`/`Pet`), and write a server `InteractionAction` that calls `_ui.OpenUi(args.Target, ...)`.

### Step 6 — locale

`Resources/Locale/en-US/_PS/social/social.ftl`: window title, verb text, button labels, popup strings.
Use `Loc.GetString` in both XAML (`{Loc key}`) and code.

## Validation checklist / pitfalls

- **Actor ≠ owner**: messages arrive at the UI owner (Cindy); always use `args.Actor` as the clicker.
- **Range**: default `InteractionRange = 2f`, auto-closes out of range; `<= 0` disables auto-close but
  falls back to `IsAccessible` checks. Override with `BoundUserInterfaceCheckRangeEvent` if needed.
- **`singleUser`** blocks a second user — leave false for social windows.
- **UI key collisions**: use a distinct enum type; declare the `UserInterface` entry once per entity
  (base humanoid vs inventory templates can otherwise double-declare).
- **ClientType lookup** requires the exact class name and `[UsedImplicitly]`.
- **State**: `SetUiState` skips equal states; only `[Serializable, NetSerializable]` data; `NetEntity`
  only, resolved with `GetEntity` on receipt.
- **Prediction**: `SendPredictedMessage` also runs locally; guard mutations.
- **Sprites**: live `SpriteView` requires `SpriteComponent` + `TransformComponent` client-side; clothing
  needs `ClientClothingSystem.InitClothing`; if using dummy entities, delete them in `Dispose`.
- **PVS**: the server UI system expands PVS to the UI entity, so the actor can see the target.
- **Window lifecycle**: `CreateWindow<T>()` makes closing call `BUI.Close()`; clean up handlers in `Dispose`.
- **Target deletion/SSD**: UI closes automatically on shutdown; check mind/session for SSD messaging.
- **Consent**: add/check a consent toggle before kiss/hug effects.

## Unknowns

- No precedent for two live player sprites in one BUI; the combination is inferred from
  `CharacterDetailWindow` + `BorgMenu`.
- No kiss/hug consent toggle exists; you must reuse `NSFWDescriptions` or add one.
- Policy for moving apart/SSD/consent-denied is a design choice.

## Source anchors

`RobustToolbox/Robust.Shared/GameObjects/Components/UserInterface/*`,
`RobustToolbox/Robust.Shared/GameObjects/Systems/SharedUserInterfaceSystem.cs`,
`Content.Shared/UserInterface/ActivatableUI*.cs`, `Content.Shared/Strip/*`,
`Content.Client/Inventory/StrippableBoundUserInterface.cs`, `Content.Client/Strip/StrippingMenu.cs`,
`Content.Client/Examine/CharacterExamineSystem.cs`, `Content.Client/Silicons/Borgs/BorgMenu.xaml.cs`,
`Content.Shared/InteractionVerbs/*`.
