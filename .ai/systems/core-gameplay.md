# System: Core Gameplay Subsystems

> Upstream-inherited gameplay that is still active unless noted. Each entry: purpose, location,
> key classes, dependencies. Physics itself is engine-side.
>
> How-to: asteroid/salvage dungeons `.ai/guides/adding-dungeons.md`; humanoid enemy NPCs
> `.ai/guides/adding-humanoid-npc.md`.

## Interactions / hands / inventory / storage

- **Purpose**: world interaction pipeline, hands, equipment slots, containers, item size, verbs.
- **Location**: `Content.Shared/Interaction/SharedInteractionSystem.cs:54`; `Content.Shared/Hands/EntitySystems/SharedHandsSystem.cs:16` (+ `.Pickup/.Drop/.Interactions/.Relay/.AI`), server `Content.Server/Hands/Systems/HandsSystem.cs:33`; `Content.Shared/Inventory/InventorySystem.Equip.cs:24`; `Content.Shared/Storage/EntitySystems/SharedStorageSystem.cs:51` / server `Content.Server/Storage/EntitySystems/StorageSystem.cs:19`; `Content.Shared/Item/SharedItemSystem.cs:16`; interaction verbs `Content.Shared/InteractionVerbs/SharedInteractionVerbsSystem.cs:27`; classic verbs `Content.Shared/Verbs/SharedVerbSystem.cs:10`.
- **Dependencies**: DoAfter, containers, pulling, action blockers, UI storage.
- **Depended on by**: nearly everything item-related (weapons, medical, construction, lathes, cargo, NPC operators).

## Physics / body / damage / medical / stamina / blood

- **Physics** is engine-side (`RobustToolbox/Robust.Shared/Physics/Systems/SharedPhysicsSystem.cs:19`); content adds collision groups, prevent-collide, conveyors (`Content.Shared/Physics/...`).
- **Body**: `Content.Shared/Body/Systems/SharedBodySystem.cs:10` (+ `.Parts/.Organs/.Body`), server `Content.Server/Body/Systems/BodySystem.cs:20`; components `BodyComponent`, `OrganComponent`, `BodyPartComponent`; prototypes in `Resources/Prototypes/Body`.
- **Damage**: `Content.Shared/Damage/Systems/DamageableSystem.cs:22`, `DamageSpecifier`, `DamageModifierSet`; armor `Content.Shared/Armor/SharedArmorSystem.cs:13` via inventory relay; stamina `SharedStaminaSystem` / server `StaminaSystem`; bloodstream `SharedBloodstreamSystem` / `BloodstreamSystem`.
- **Metabolism/organs**: server `MetabolizerSystem`, `StomachSystem`, `LungSystem`, `BrainSystem`, `RespiratorSystem`, `InternalsSystem`, `ThermalRegulatorSystem` (`Content.Server/Body/Systems/`).
- **Medical items**: `Content.Server/Medical/{HealthAnalyzerSystem,DefibrillatorSystem,CryoPodSystem,MedicalScannerSystem}`; healing `Content.Shared/Medical/Healing/HealingSystem.cs:21`.
- **Stun/knockdown**: `Content.Shared/Stunnable/SharedStunSystem.cs:27`, server `StunSystem`.
- **Surgery**: **not present** (only TODOs in `Content.Server/Implants/ImplanterSystem.cs`).
- **Depended on by**: combat, atmos barotrauma, chemistry effects, medical, NPC combat, stuns.

## Atmos / gases / piping

- **Location**: `Content.Server/Atmos/EntitySystems/AtmosphereSystem.cs:26` with partials (LINDA, Monstermos, Processing, Hotspot, Gases, Superconductivity, etc.); data `Content.Shared/Atmos/GasMixture.cs`, `Resources/Prototypes/Atmospherics`; pipes via `Content.Server/NodeContainer/EntitySystems/{NodeContainerSystem,NodeGroupSystem}.cs` and `NodeGroups/PipeNet.cs`; effects (flammable, barotrauma, gas tanks, filters, heat exchangers, miners, alerts); gas reactions prototypes.
- **Dependencies**: physics, maps/tiles, containers, puddles, `GameTicker` (env resets).
- **Active** on ships/stations.

## Power / device networks / wires

- **Location**: `Content.Server/Power/EntitySystems/{PowerNetSystem,ApcSystem,BatterySystem,ChargerSystem,CableSystem,ExtensionCableSystem}.cs`; solver `Content.Server/Power/Pow3r/*`; device linking `Content.Server/DeviceLinking/Systems/*`; device network `Content.Server/DeviceNetwork/Systems/*`; wires `Content.Server/Wires/WiresSystem.cs:22`, shared `Content.Shared/Wires/SharedWiresSystem.cs:11`; generators/TEG/SMES.
- **Notes**: `Pow3r/` at repo root is a standalone visualizer, not the runtime.
- **Dependencies**: NodeContainer/NodeGroup; consumed by doors, vending, cryo, conveyors, triggers, shuttles.

## Chemistry / reagents / solutions / metabolism / entity effects

- **Location**: `Content.Shared/Chemistry/EntitySystems/{SharedSolutionContainerSystem,SharedInjectorSystem,SharedHypospraySystem}.cs`; `Content.Shared/Chemistry/Reaction/ChemicalReactionSystem.cs:16`, `ReactiveSystem`; `Content.Server/Chemistry/...` (chem master, dispensers, tile reactions, vapor); `Content.Shared/Chemistry/Reagent/ReagentPrototype.cs:26`; metabolism server `MetabolizerSystem`; entity effects `Content.Server/EntityEffects/EntityEffectSystem.cs:49` + `Content.Shared/EntityEffects/**`.
- **Depended on by**: body/medical, botany, ERP conditions (`ConsentCondition`), reactions.

## Construction / lathe / materials / research

- **Location**: `Content.Server/Construction/ConstructionSystem.cs:16` (graphs, guided, machine frames, flatpacks); `Content.Server/Lathe/LatheSystem.cs:45`; materials `Content.Server/Materials/MaterialStorageSystem.cs:23`, `OreSiloSystem`, `MaterialReclaimerSystem`; research `Content.Server/Research/Systems/ResearchSystem.cs:16`.
- **Fork patches**: `_CS` Lathe partial, `_NF` blueprint lathe/random blueprints, `_CS` pricing override.

## Actions / abilities / spells

- **Location**: `Content.Shared/Actions/SharedActionsSystem.cs:22`, `ActionContainerSystem`, grant/upgrade systems; server `Content.Server/Actions/ActionsSystem.cs:7`; magic `Content.Shared/Magic/SharedMagicSystem.cs:43`; abilities folder is small.
- **Active framework**; wizard content inherited but preset disabled.

## Movement / gravity / buckle / climbing / jetpacks

- **Location**: `Content.Shared/Movement/Systems/SharedMoverController.cs:34` (+ `.Input/.Relay`), pulling `PullingSystem:40`, jetpacks `SharedJetpackSystem`, gravity `Content.Shared/Gravity/SharedGravitySystem.cs:12` + server generators, buckle `SharedBuckleSystem.cs:18`, climbing `ClimbSystem:30`, standing/laying `StandingStateSystem`, `SharedLayingDownSystem`.
- **Dependencies**: engine physics/input, body/movement modifiers, hands.

## Spawning / entity tables / station systems

- **Location**: `Content.Server/Spawners/EntitySystems/*`; `Content.Shared/EntityTable/EntityTableSystem.cs:9`; station spawning `Content.Server/Station/Systems/StationSpawningSystem.cs:46`, jobs `StationJobsSystem.cs:28`, `StationSystem.cs:31`.
- **Fork**: `_NF` spawners/POIs, `StationDeedSpawnerComponent`.

## NPC / AI

- **Location**: `Content.Server/NPC/Systems/{NPCSystem,HTN/HTNSystem,Pathfinding/PathfindingSystem,NPCSteeringSystem,NPCCombatSystem,NPCPerceptionSystem,NPCRetaliationSystem}.cs`; HTN operators in `NPC/HTN/PrimitiveTasks/Operators`; shared steering/pathfinding bases.
- **Dependencies**: hands/inventory/interaction, weapons, pulling, containers, grid tiles, mob state.

## Game rules / antags / objectives

- **Location**: `Content.Server/GameTicking/Rules/*` (Secret, Traitor, Nukeops, Revolutionary, Zombie, Thief, Dragon, DeathMatch, Survivor, ParadoxClone, Sandbox, SubGamemodes, variation passes); `Content.Server/Antag/AntagSelectionSystem.cs:40`; objectives `Content.Server/Objectives/ObjectivesSystem.cs:25` + condition systems; station events `Content.Server/StationEvents/*`.
- **Status**: framework active; upstream antag presets disabled (`rules: []`); NF uses antag selection for pirates.

## Shuttles / grids / FTL

- **Location**: `Content.Server/Shuttles/Systems/ShuttleSystem.cs:37` (+ `.FasterThanLight`, `.Impact`, `.IFF`, `.GridFill`), `ShuttleConsoleSystem` (+ `.FTL/.Drone`), `DockingSystem.cs:23`, `EmergencyShuttleSystem.cs:44`, `ArrivalsSystem.cs:46`, `ThrusterSystem.cs:29`, `StationAnchorSystem`, `RadarConsoleSystem`; shared components `FTLComponent`, `DockingComponent`, `ShuttleComponent`.
- **Dependencies**: engine physics/grids, power, device linking, station system, GameTicker round end.
- **Fork**: `_NF` FTL restrictions, drydock, NF drone consoles.

## Cargo / economy (upstream)

- **Location**: `Content.Server/Cargo/Systems/CargoSystem.cs:26` (+ Orders/Bounty/Shuttle/Funds/Telepad), `PricingSystem.cs:30`.
- **Status**: dormant on live map (`StandardFrontierStation` uses `_NF` cargo); active for maps using `BaseStationCargo`.

## Damage / explosions / triggers

- **Location**: `Content.Server/Explosion/EntitySystems/ExplosionSystem.cs:35` (+ Processing/TileFill/GridMap/Airtight/Visuals/CVars/Flood), shared `SharedExplosionSystem`; triggers `TriggerSystem.cs:80` (+ OnUse/Signal/Proximity/Voice/Mobstate/TimedCollide).
- **Dependencies**: physics, map/tiles, atmos, damage, destructible, device linking.

## Alerts / status effects

- **Location**: `Content.Shared/Alert/AlertsSystem.cs:9`, server `ServerAlertsSystem`; `Content.Shared/StatusEffect/StatusEffectsSystem`.
- Used by nearly every condition (hunger, bleeding, stun, etc.).

## Known status summary

| Subsystem | Status |
|---|---|
| Interactions/hands/inventory, body/damage, atmos, power, chemistry, construction, movement, spawning, NPC, shuttles, explosions | Active |
| Actions/magic | Framework active; most spell content not reachable via presets |
| Antags/objectives | Framework active; upstream antag rules dormant; NF pirates active |
| Upstream cargo | Dormant on live map; active for maps using upstream cargo components |
| Surgery | Not implemented in this repo |
