# System: Coyote (`_CS`) & Palmtree (`_PS`) Systems

## Purpose

`_CS` is Coyote Sector's own gameplay layer (roleplay economy, needs, ERP-adjacent flavor, custom
species/jobs/ships). `_PS` is the current Palmtree Station prefix and is small.

## Location

`Content.Server/_CS`, `Content.Shared/_CS`, `Content.Client/_CS`,
`Resources/Prototypes/_CS`, `Resources/Locale/en-US/_CS`, `Resources/Textures/_CS`, `Resources/Maps/_CS`;
same pattern for `_PS` (prototypes/locale/textures only where noted).

## `_CS` systems

| System | Location | Purpose |
|---|---|---|
| Roleplay Incentive (RPI) | `Content.Server/_CS/RolePlayIncentiveServer/RoleplayIncentiveSystem.cs:43`; shared `_CS/RolePlayIncentiveShared/` (`RPIncentiveComponent`, `RpiTaxBracketPrototype`, `RpiAuraPrototype`, `RpiChatActionPrototype`, `RpiContinuousProxyActionPrototype`, `RpiJobModifierPrototype`); YAML `Resources/Prototypes/_CS/RPI stuff/` | Pays players via `_NF` `BankSystem` for chat/emotes, discrete actions, continuous/proximity actions; tax brackets; auras; job modifiers; design notes in `Content.Server/_CS/myThoughts.txt` |
| Needs | `Content.Server/_CS/Needs/NeedSystem.cs:15`; shared `_CS/Needs/*`; YAML `_CS/Needs/` | Per-species hunger/thirst/other needs with examine output and slowdown prototypes |
| Scent / sniff | `Content.Shared/_CS/SniffAndSmell/{ScentSystem,ScentPrototype,ScentComponent,SmellerComponent}`; injection `Content.Server/_CS/AddCompsOnActorize/AddCompsOnActorizeSystem.cs:19-25` | Passive proximity smells + Smell verb; consent-gated for lewd scents |
| Horny examine quirks | `Content.Shared/_CS/HornyQuirks/*`, YAML `_CS/hornyExamines.yml` | Temperament/body-type examine text, suppressible by trait |
| Aphro laced visibility | `Content.Server/_CS/AphroLacedVisibility/*`, `Content.Client/_CS/AphroLacedVisibility`, `Content.Shared/_CS/Helpers/ServerAphrodisiacChecker.cs` | Detects aphrodisiac reagents (bowls/containers), status icon/examine note, consent-gated |
| Custom species/markings | `Content.Shared/_CS/Humanoid/{CoyoteMarkingSystem,MarkingPrototype,LegDisplacementPrototype}`, YAML `_CS/Species/anthro.yml`, `_CS/Entities/Mobs/Species/anthro.yml` | `Anthromorph` species (`kind: BasicHumanlike+BasicFurry`), `kindAllowance` marking sharing, leg displacement |
| Size manipulation | `Content.Server/_CS/Body/Systems/{SizeManipulationSystem,SizeReverterSystem}`, shared `_CS/Body/Components/*`, `_CS/Weapons/Ranged/Systems/SizeManipulatorSystem.cs`, client visuals | Grow/shrink mechanics and size gun (consent-gated) |
| Space janitor | `Content.Server/_CS/SpaceJanitor/SpaceJanitorSystem.cs:17` | Deletes long-abandoned items in space |
| Shuttle IFF crew status | `Content.Server/_CS/ShuttleCrewStatus/ShuttleCrewStatusSystem.cs:19` | Greys IFF labels for crewless player shuttles |
| Radio static | `Content.Shared/_CS/RadioNoises/RadioStaticSystem.cs:13` | Radio squelch/static sounds, volume/squelch verbs |
| Misc shared | `_CS/{Throwing/CatchableSystem,MobDice/MobDiceSystem,UnContrabandSystem}` | Catchable thrown items, mob dice, un-contraband |
| Misc server | `_CS/{HealingBank (WIP), Conveyor/ConveyorCleanupSystem, Bed/BlanketSystem, BlipCartridge/BlipCartridgeSystem, EventResponseReagentCondition, Item/HeldItemOrbiterSystem, Lathe/Systems/BiogeneratorBufferSystem, Cargo/Systems/PricingSystem, Salvage/ExpeditionMedicalSOSSystem}` | Various feature ports/overrides |
| Client | `Content.Client/_CS/{ClientNeedsSystem, Lathe/UI/LatheMenu.xaml.cs, Helpers/*, AphroLacedVisibility}` | Needs UI, custom lathe menu partial, text helpers |
| Content | `Resources/Prototypes/_CS/*` | Career jobs (Companion, Therapist, Massage Therapist, Secretary, Nurse, Journalist, Pirate roles), loadouts, shipyard catalog (biodomes, Scrap, BlackMarket, NFSD), maps `Resources/Maps/_CS/Shuttles/`, reagents, research, RCD, sounds, voice, `ass.txt` |

## `_CS` conventions / caveats

- Own CVars: `Content.Shared/_CS/CCVar/CSCCVars.cs` (currently `conveyor.*` only).
- RPI directly depends on `_NF` `BankSystem` (Coupling across fork modules).
- `RadiotStatic`/other systems extend upstream radio/chat via shared events.
- `Resources/Prototypes/_CS/ass.txt` is plain text, referenced by nothing.
- `_CS` patches core files (e.g. `LatheSystem`, `SalvageSystem.Expeditions.cs`, preferences bank balance)
  in addition to its own folder.

## `_PS` contents (exact)

| Area | Files |
|---|---|
| Server | `Content.Server/_PS/Clothing/ServerConcealableClothingSystem.cs:8` |
| Shared | `_PS/Clothing/SharedConcealableClothingSystem.cs:18`, `Components/{ConcealableClothingComponent,ConcealableClothingImplantComponent,ConcealableClothingUserComponent}.cs` |
| Client | `Content.Client/_PS/Clothing/ClientConcealableClothingSystem.cs` |
| Empty dirs | `Content.Shared/_PS/{Interaction,Interactions}`, `Content.Client/_PS/{Interactions,UI}`, `Resources/Prototypes/_PS/{InteractionVerbs,Recipes}` |
| Prototypes | `Resources/Prototypes/_PS/` — concealable action, effects, genital/breast markings (`PSGenitalBreasts0..N`), subdermal implants/implanters, weapons (batteries, launchers, grenades, hitscan, projectiles, turrets), strobe lighting, `lobby.yml`, `Voice/speech_emotes.yml`, `RCD/rcd.yml`, sound collections, shipyard cauterizer |
| Locale/Textures | `Resources/Locale/en-US/_PS/{actions,emotes,genitals.ftl}`, `Resources/Textures/_PS/...` |

Feature: `ConcealableClothing` — a subdermal implant grants a toggle action that hides granted clothing
categories; server system validates implant removal and cleans up the user component.

## Data ownership

- RPI paywards/tax state (runtime), needs values (component), scent components (runtime), size
  components/scale (component + appearance), concealable clothing category grants (component).
- `_PS` genital markings are prototype/appearance data persisted with profiles (markings JSON).

## Events / hooks

- Consumes `ConsentSystem.HasConsent` (size, scent, aphro), `ExaminedEvent` (quirks/scent/custom examine
  ordering), `PlayEmoteMessage`/emote events (custom emotes), `AddCompsOnActorizeEvent` (scent smeller).
- Hooks upstream systems: RPI listens to chat/actions, radio static intercepts radio receive,
  pricing overrides cargo pricing.

## Dependencies

`_NF` bank/market/cargo/shipyard, upstream consent/examine/markings/height-adjust/radio/chat, job and
loadout prototypes.

## Depended on by

Live Coyote gameplay (jobs, ships, economy), consent-gated systems, character customization.

## Unknowns

- `_PS` intent for empty directories / whether more features are planned.
- Reachability of some `_CS` minor systems (healing bank is marked WIP).
- `_CS` "Fuzzy's stuff" and various orphan prototypes were not individually assessed.
