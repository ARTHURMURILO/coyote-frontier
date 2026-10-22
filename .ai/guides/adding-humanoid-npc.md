# Guide: Adding a Humanoid Enemy NPC with a Preset Appearance

> Answer to: "How can one add a new type of humanoid enemy NPC with a preset appearance (e.g. a new
> Syndicate Agent enemy mob)?"
> Facts from source; inferred items marked. Line numbers valid for branch `palm3` commit `4bfdad813c`.

## TL;DR

Use a **static mob entity prototype** that parents the hostile-humanoid bases, with
`HumanoidAppearance { species, initial: <humanoidProfile> }` for a fixed look, `Loadout` for clothes/gear,
`NpcFactionMember` for hostility, and the hostile AI packages for behavior. The `RandomHumanoidSpawner`
marker path is for *randomized* looks and has no fixed-profile field in this fork.

## Spawner layers (pick the right one)

| Path | What it gives | When to use |
|---|---|---|
| **A. Static NPC mob prototype** | Fixed species/profile + loadout + AI, spawnable by ID | Preset-appearance enemy (your case) |
| **B. `RandomHumanoidSpawner` + `randomHumanoidSettings`** | Random `HumanoidCharacterProfile` on a species; optional extra components | ERT/death squad-style random humans |
| **C. `HumanoidProfilePrototype`** | Full preset `HumanoidCharacterProfile` (`profile:`) referenced via `HumanoidAppearance.initial` | Fixed hair/markings/skin/name |
| **D. Antag/gamerule spawners** (`AntagSpawner`, `GhostRoleMobSpawner`, `GhostTakeoverAvailable`) | Player/ghost-controlled mobs | Antags, ghost roles — not plain NPC enemies |

Important: `RandomHumanoidAppearanceSystem` skips randomization when `HumanoidAppearance.Initial` is
set (`Content.Server/Humanoid/Systems/RandomHumanoidAppearanceSystem.cs:22`), so `initial:` wins even on
bases that also have `RandomHumanoidAppearance`. `RandomHumanoidSpawner` (B) always generates a random
profile (`Content.Server/Humanoid/Systems/RandomHumanoidSystem.cs:29-62`) and its settings prototype has
only `randomizeName`, `speciesBlacklist`, `components` in this fork
(`Content.Shared/Humanoid/Prototypes/RandomHumanoidSettingsPrototype.cs:24-38`) — no `loadouts:` field.

## Traced example: `_NF` Syndicate naval mobs

- `Resources/Prototypes/_NF/Entities/Mobs/NPCs/mob_hostile_syndicate.yml`
  - `MobSyndicateNavalBase` (:2-43): parents `NFMobNoEquipmentOnGib`, `MobStaminaFodder`,
    `MobMovementSpeedModifierRanged`, `MobHumanoidHostileBase`, `MobHumanoidInvetory`,
    `MobHumanoidHostileAISimpleRanged`; `NpcFactionMember { NFSyndicate }` (:18-20);
    `RandomHumanoidAppearance { randomizeName: false }` (:21-22); `RandomMetadata` name segments (:23-27).
  - `MobSyndicateNavalCaptainA` (:47-71): `Loadout { prototypes: [SyndicateNavalCaptainGearA] }` (:54-56)
    and its own `Gun` + `BasicEntityAmmoProvider` + `RechargeBasicEntityAmmo` (:57-71).
- Gear: `Resources/Prototypes/_NF/Roles/Jobs/Hostile/syndicate_naval_forces.yml:1-16` (`startingGear`).
- Markers: `Resources/Prototypes/_NF/Entities/Markers/Spawners/Random/mobs_hostile_syndicate.yml:1-34`.
- Expedition table: `Resources/Prototypes/_NF/Procedural/salvage_factions.yml:224-249`.
- Hostile bases/AI: `Resources/Prototypes/_NF/Entities/Mobs/NPCs/mob_hostile_base.yml`
  (inventory templates :355-370, AI packages :373-423, full hostile base :428-483).
- Upstream simple example: `Resources/Prototypes/Entities/Mobs/NPCs/human.yml:50-65` (`MobSyndicateFootsoldier`).

## Hostility / factions

- `NpcFactionMemberComponent` holds the faction set; `NpcFactionSystem` derives hostile sets from
  `npcFaction` prototypes and drives HTN combat (`Content.Shared/NPC/Systems/NpcFactionSystem.cs:40-71,178-195`).
- Faction graphs: `Resources/Prototypes/ai_factions.yml` (`NanoTrasen` :25-48, `SimpleHostile` :72-98)
  and `Resources/Prototypes/_NF/ai_factions.yml` (`NFSyndicate` :54-87, `NFPirate` :155-186, etc.).
- A child `NpcFactionMember.factions:` **replaces** the inherited `NanoTrasen` set — this is how the
  syndicate base becomes hostile to crew.
- Crew species are `NanoTrasen` (`Resources/Prototypes/Entities/Mobs/Species/base.yml:192-194`).

## Recipe — "Syndicate Agent" with preset look (species Human)

### Step 1 (optional, for a truly fixed look): humanoid profile

`Resources/Prototypes/_PS/humanoidProfiles/syndicate_agent.yml`:

```yaml
- type: humanoidProfile
  id: SyndicateAgentPreset
  profile:
    name: Syndicate Agent
    species: Human
    age: 30
    appearance:
      hair: HairBald
      facialHair: FacialHairNone
      skinColor: "#C0967F"
      eyeColor: "#8B0000"
      # markings: [ ... ]   # HumanoidCharacterAppearance fields
```

Fields mirror `HumanoidCharacterProfile` (`Content.Shared/Preferences/HumanoidCharacterProfile.cs:82-126`)
and `HumanoidCharacterAppearance`; example file `Resources/Prototypes/SimpleStation14/humanoidProfiles/felinid.yml`.

### Step 2: gear prototype

`Resources/Prototypes/_PS/Roles/Jobs/Hostile/syndicate_agent.yml`:

```yaml
- type: startingGear
  id: SyndicateAgentGear
  equipment:
    jumpsuit: ClothingUniformJumpsuitSyndieFormalNF
    shoes: ClothingShoesBootsCombat
    outerClothing: ClothingOuterArmorBasic
    mask: ClothingMaskGasSyndicate
  inhand:
    - CombatKnife
```

### Step 3: mob entity

`Resources/Prototypes/_PS/Entities/Mobs/NPCs/mob_hostile_syndicate_agent.yml`:

```yaml
- type: entity
  name: syndicate agent
  parent:
  - MobMovementSpeedModifierRanged
  - MobHumanoidHostileBase
  - MobHumanoidInvetory
  - MobHumanoidHostileAISimpleRanged
  id: MobSyndicateAgentPreset
  description: A stealthy syndicate operative.
  components:
  - type: HumanoidAppearance          # fixed look; blocks RandomHumanoidAppearance
    species: Human
    initial: SyndicateAgentPreset
  - type: NpcFactionMember
    factions: [ NFSyndicate ]
  - type: Loadout
    prototypes: [ SyndicateAgentGear ]
  - type: RandomMetadata
    nameSegments: [ NamesSyndicatePrefix, NamesSyndicateNormal ]
  - type: RechargeBasicEntityAmmo
    rechargeCooldown: 1
    rechargeSound: { path: /Audio/_NF/Effects/silence.ogg }
  - type: BasicEntityAmmoProvider
    proto: NFCartridgePistol35
    capacity: 4
    count: 4
  - type: Gun
    showExamineText: false
    fireRate: 3
    selectedMode: FullAuto
    availableModes: [ FullAuto ]
    soundGunshot: { path: /Audio/Weapons/Guns/Gunshots/pistol.ogg }
```

Notes:
- Parent list copied from `MobSyndicateNavalBase`; alternatively parent that base directly and override
  `HumanoidAppearance` with `initial:`.
- For an Anthromorph version, use `species: Anthromorph` and a matching profile; the mob prototype must
  use a species body compatible with `Body`/organs (safest: parent `BaseMobAnthromorph` chain or the
  species' mob base).
- **Weapon semantics**: ranged HTN AI fires the mob's own `Gun` + ammo components. A gun item in
  `back`/`inhand` is cosmetic/loot (see the naval captain). For melee-only, use
  `MobHumanoidHostileAISimpleMelee` and a `MeleeWeapon` component.

### Step 4 (optional): make it spawnable

- Marker: copy `mobs_hostile_syndicate.yml:1-34` (`MarkerBase` + `RandomSpawner { prototypes: [...] }`).
- Expedition: add to a `salvageFaction` entry (`salvage_factions.yml:225-249`).
- Gamerule/station event: `RandomSpawnRule`/`SpaceSpawnRule` with the marker/mob, add via
  `addgamerule`.
- Ghost takeover: add `GhostTakeoverAvailable` + `GhostRole`, or wrap in
  `GhostRoleMobSpawner { prototype: MobSyndicateAgentPreset }` (see `.ai/guides/adding-gamemodes.md`).

## Testing

| Action | Command |
|---|---|
| Spawn | `spawn MobSyndicateAgentPreset` (server/admin console) |
| Inspect | `vv <uid>` to confirm `HumanoidAppearance.Initial`, factions, loadout |
| Compare with known-good | `spawn MobSyndicateNavalDeckhandA` |
| Reload YAML | In Tools builds the client prototype watcher reloads on window refocus (CVar `res.prototype_reload_watch`); `loadprototype` pushes YAML in any build. There is no server `reloadprototypes` command in this fork. |
| Rules | `listgamerules`, `addgamerule`, `endgamerule` |

## Pitfalls

- **Random vs fixed**: only the static-mob + `initial:` route pins hair/markings/species. A
  `RandomHumanoidSpawner` will randomize regardless.
- **No loadout field in `randomHumanoidSettings`** in this fork — put a `Loadout` component inside
  `components` if needed.
- **Markers self-delete on `MapInit`** (`RandomHumanoidSystem.cs:31`) — one-shot.
- **Inventory template matters**: `MobHumanoidInvetory` supports all loadout slots;
  `MobHumanoidInvetorySimplified` only pockets/belt/back and is paired with a hand-drawn sprite overlay.
  If clothes don't show, check the template and any `Sprite` override.
- **Faction override**: children replace inherited factions; if the mob doesn't fight, check both sides
  of the relationship in `ai_factions.yml`/`_NF/ai_factions.yml`.
- **Protected grids**: `HostileNPCDeletionSystem` gibs `NanoTrasen`-hostile NPCs on grids with
  `ProtectedGrid.KillHostileMobs` (default false) — spawning there deletes the mob
  (`Content.Server/_NF/NPC/Systems/HostileNPCDeletionSystem.cs:37-56`).
- **`NFBaseMobRestrictions`** adds despawn/pacify behavior on death/leave-grid; use a cleaner base if
  unwanted (`mob_hostile_base.yml:89-112`).
- **`categories: [ HideSpawnMenu ]`** hides it from the admin spawn menu (the `spawn` command still works).
- **`HumanoidAppearance` does not set the entity name** — use `RandomMetadata`/`MetaData`.
- **Species/body mismatch**: overriding `species` without a compatible body prototype affects
  organs/damage; safest is to use the species' own mob base.

## Unknowns

- Exact organ/damage behavior when overriding `species` on a non-matching base (verify in-game).
- Whether `HideSpawnMenu` affects fork-specific admin UIs beyond the standard menu.
- Whether a `GhostRole` on a mob that is already sentient needs `MakeSentient` (default true).

## Source anchors

`Content.Server/Humanoid/Systems/{RandomHumanoidSystem,RandomHumanoidAppearanceSystem}.cs`,
`Content.Shared/Humanoid/Prototypes/{RandomHumanoidSettingsPrototype,HumanoidProfilePrototype}.cs`,
`Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs`, `Content.Shared/Clothing/LoadoutSystem.cs`,
`Content.Shared/Station/SharedStationSpawningSystem.cs`, `Content.Shared/NPC/Systems/NpcFactionSystem.cs`,
`Content.Server/Antag/AntagSelectionSystem.cs`, `Content.Server/Ghost/Roles/GhostRoleSystem.cs`.
