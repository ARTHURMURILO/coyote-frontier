# Guide: Adding a New Species

> Answer to: "How can one add a new species?"
> Facts from source; inferred items marked. Line numbers valid for branch `palm3` commit `4bfdad813c`.

## TL;DR

A playable species is three YAML layers: a `species` prototype (`SpeciesPrototype`), a base mob
prototype (sprites + `HumanoidAppearance`), and a player mob prototype, plus a doll mob for the
character editor, a base-sprite set (`speciesBaseSprites`), marking points, an RSI with the expected
states, and a locale name key. Easiest path: copy the `_CS` Anthromorph files and rename.

## Data model

`SpeciesPrototype` (`Content.Shared/Humanoid/Prototypes/SpeciesPrototype.cs`):

| Field | Notes |
|---|---|
| `id`, `name` (loc key), `roundStart` | `roundStart: true` makes it appear in the character editor; false resets profiles to Human on validation (`HumanoidCharacterProfile.cs:609-613`) |
| `prototype` | Mob to spawn (required) |
| `dollPrototype` | Character-editor preview mob (required) |
| `sprites` | `speciesBaseSprites` prototype |
| `markingLimits` | `markingPoints` prototype |
| `skinColoration` | `HumanToned`, `Hues`, `VoxFeathers`, `TintedHues`, `ShelegToned`, `AnimalFur` |
| `kind` | Species tags matched by marking `kindAllowance` (e.g. `BasicHumanlike`, `BasicFurry`) |
| `customName` | Enables the custom-species-name field |
| `sexes`, ages, `min/default/maxHeight/Width` | Editor clamps |
| `defaultLegStyle`, `allowDigilegDisplacement` | `_CS` digitigrade support |
| naming datasets (`maleFirstNames`/`femaleFirstNames`/`lastNames`, `naming`) | Default `NamesFirstMale`/`NamesFirstFemale`/`NamesLast` |
| `forcedMarkingColor` | Used when a marking/layer forces color |
| `Descriptor`, `guideBookIcon` | **Dead fields** (not read) |

Supporting prototypes:
- `speciesBaseSprites` → `HumanoidSpeciesBaseSpritesPrototype`
  (`Content.Shared/Humanoid/Prototypes/HumanoidSpritePrototypes.cs:11-25`): maps `HumanoidVisualLayers`
  to `humanoidBaseSprite` IDs. Omitted layer = unused.
- `humanoidBaseSprite` → `HumanoidSpeciesSpriteLayer` (:32-89): `baseSprite`, `layerAlpha`,
  `allowsMarkings`, `matchSkin`, `forcedColoring`, `altSprites` (digitigrade swaps).
- `markingPoints` → `MarkingPointsPrototype` (`Content.Shared/Humanoid/Markings/MarkingPoints.cs:47-62`);
  `required` is effectively unused (only cloned, :37).
- Layer enum `HumanoidVisualLayers` (`Content.Shared/Humanoid/HumanoidVisualLayers.cs:11-51`) with an
  "how to add a layer" comment (:54-88).
- Marking compatibility: `MarkingPrototype.speciesRestriction` / `kindAllowance`; `MarkingManager`
  (`Content.Shared/Humanoid/Markings/MarkingManager.cs:170-185`) allows a marking if it has no
  restriction, lists the species, or its `kindAllowance` intersects `SpeciesPrototype.Kind`.

## Traced example: `_CS` Anthromorph (self-contained)

1. `Resources/Prototypes/_CS/Species/anthro.yml`
   - `species` :1-13 (`roundStart: true`, `prototype: MobAnthromorph`, `dollPrototype: MobAnthromorphDummy`,
     `sprites: MobAnthromorphSprites`, `markingLimits: MobAnthromorphMarkingLimits`, `skinColoration: Hues`,
     `kind: [BasicHumanlike, BasicFurry]`).
   - `speciesBaseSprites` :15-46 (`Head`, `Chest`, `Eyes`, limbs, plus accessory layers → `MobHumanoidAnyMarking`).
   - `markingPoints` :48-137 (all categories `points: 999`).
   - `humanoidBaseSprite` :139-221 (RSI `_CS/Mobs/Species/Anthromorph/parts.rsi`).
2. `Resources/Prototypes/_CS/Entities/Mobs/Species/anthro.yml`
   - `BaseMobAnthromorph` :1-48: `parent: BaseMobSpeciesOrganic`, `abstract: true`, `save: false`,
     `HumanoidAppearance species: Anthromorph`, fixtures, needs, icon.
   - `MobAnthromorphDummy` :51-60: `parent: MobHumanDummy`, `HumanoidAppearance species: Anthromorph`.
3. `Resources/Prototypes/_CS/Entities/Mobs/Player/anthro.yml:1-5`: `MobAnthromorph`.
4. Locale: `Resources/Locale/en-US/_CS/species/species.ftl` (`species-name-anthromorph`).
5. Guidebook: `Resources/Prototypes/Guidebook/species.yml:99-102` + `Resources/ServerInfo/Guidebook/Mobs/_CS/Anthromorph.xml`.
6. RSI: `Resources/Textures/_CS/Mobs/Species/Anthromorph/parts.rsi/`.

Inheritance: `BaseMobSpeciesOrganic` supplies `Body prototype: Human`, `Damageable`, `MindContainer`,
`Speech`/`Vocal` defaults (`Resources/Prototypes/Entities/Mobs/Species/base.yml:233-302,152-154,65-66,183,204-211`),
so a new species needs **no body/damage override**.

Variants: Felinid adds a custom body + damage set (`Nyanotrasen/Species/felinid.yml`,
`Nyanotrasen/Damage/modifier_sets.yml:8-13`); Harpy adds a custom sprite layer `RArmExtension`
(`_DV/Species/harpy.yml:19-51,98-147`, mob `_DV/Entities/Mobs/Species/harpy.yml:34-94,109-112`);
Resomi adds custom naming datasets and eyes/hair base sprites (`_Starlight/Species/resomi.yml:9-12,74-87`).

## Minimum-viable recipe — `PSExample`

> The traced Anthromorph example lives under `_CS`; copy it as a template, but per the maintainer
> convention all new Palmtree content belongs in `_PS` folders (`Content.*/_PS`, `Resources/**/_PS`).
> Prototype kinds are global, so `_PS` YAML can reference `_CS`/`_NF` prototypes freely.

1. **RSI**: `Resources/Textures/_PS/Mobs/Species/PSExample/parts.rsi/` with `meta.json` and states
   `full`, `head_m`, `head_f`, `torso_m`, `torso_f`, `l_arm`, `r_arm`, `l_hand`, `r_hand`,
   `l_leg`, `r_leg`, `l_foot`, `r_foot` (copy Anthromorph and recolor).
2. **Mobs** — `Resources/Prototypes/_PS/Entities/Mobs/Species/ps_example.yml`:
   - `BaseMobPSExample`: `parent: BaseMobSpeciesOrganic`, `abstract: true`, `save: false`,
     `Icon` (parts.rsi `full`), `HumanoidAppearance species: PSExample`, fixtures
     (circle radius .35, density 185) — copy `BaseMobAnthromorph`.
   - `MobPSExampleDummy`: `parent: MobHumanDummy`, `categories: [HideSpawnMenu]`,
     `HumanoidAppearance species: PSExample`.
   - `Resources/Prototypes/_PS/Entities/Mobs/Player/ps_example.yml`: `MobPSExample`,
     `parent: BaseMobPSExample`, `save: false`.
3. **Species** — `Resources/Prototypes/_PS/Species/ps_example.yml`:
   - `species` block as above (`id: PSExample`, `roundStart: true`, `prototype: MobPSExample`,
     `dollPrototype: MobPSExampleDummy`, `sprites: MobPSExampleSprites`,
     `markingLimits: MobPSExampleMarkingLimits`, `skinColoration: Hues`, `kind: [...]`).
   - `speciesBaseSprites` mapping layers to sprites. **Head/Chest need `...HeadMale`, `...HeadFemale`,
     `...TorsoMale`, `...TorsoFemale` variants** — `GetSexMorph` appends the sex suffix
     (`Content.Shared/Humanoid/HumanoidVisualLayersExtension.cs:18-24`).
   - `markingPoints` for the categories you want (copy Anthromorph's 999 block).
   - `humanoidBaseSprite` entries pointing at your RSI.
4. **Locale** — `Resources/Locale/en-US/_PS/species/species.ftl`: `species-name-psexample = PS Example`.
5. **Test**: build, `spawn MobPSExample`, open the character editor (species appears automatically via
   `roundStart: true`, `HumanoidProfileEditor.xaml.cs:857-884`), check the doll preview and markings tab.

## Optional extras

- **Body/organs**: override `Body: prototype: X` + a `body` prototype (`Resources/Prototypes/Body/Prototypes/`,
  examples `_DV/Body/Prototypes/harpy.yml`). Missing referenced parts/organs = prototype load error.
- **Damage**: `Damageable damageModifierSet` + set prototype (`_DV/Damage/modifier_sets.yml:6-11`).
- **Custom name**: `customName: true`.
- **Naming**: override name datasets + `naming`; locale `Resources/Locale/en-US/species/namepreset.ftl`.
- **Sexes**: `sexes: [Unsexed]` (IPC pattern `_EinsteinEngines/Species/ipc.yml:19-20`).
- **Leg style**: `defaultLegStyle: Digitigrade` + `altSprites` + `allowDigilegDisplacement`.
- **Markings**: easiest via `kind` + `kindAllowance`; each marking needs a `marking-<id>` locale key
  (e.g. `Resources/Locale/en-US/_CS/markings.ftl`).
- **Speech/voice/emotes**: override `Speech`/`Vocal` (Felinid pattern).
- **Random species weighting**: `Resources/Prototypes/Species/species_weights.yml` (used by
  `GameTicker.Spawning.cs:194-216` when `ic.random_species_weights` is set).
- **Guidebook**: `guideEntry` in `Resources/Prototypes/Guidebook/species.yml` + XML under
  `Resources/ServerInfo/Guidebook/Mobs/<prefix>/`.

## Testing / validation

| Action | How |
|---|---|
| Spawn mob | admin `spawn MobPSExample` |
| Editor preview | lobby character editor (species list) |
| Guidebook text | `Content.IntegrationTests/Tests/Guidebook/GuideEntryPrototypeTests.cs` parses all entries |
| Name datasets | `Content.IntegrationTests/Tests/Localization/LocalizedDatasetPrototypeTest.cs` |
| Prototype errors | `dotnet run --project Content.YAMLLinter` |

There is no integration test specifically validating `SpeciesPrototype` fields.

## Pitfalls

1. Missing/mismatched `dollPrototype` → wrong preview (a wrong-species doll silently renders human).
2. Missing `sprites`/`markingLimits` → loads, but throws when appearance is applied
   (`Content.Client/Humanoid/HumanoidAppearanceSystem.cs:84`, `SharedHumanoidAppearanceSystem.cs:355-363`).
3. Missing sex-morph variants for Head/Chest → invisible or wrong sprite.
4. New custom layer must be added to the mob's and dummy's `Sprite.layers` (Harpy pattern).
5. `roundStart: false` → hidden from editor; profiles are silently reset to Human.
6. Category absent from `markingPoints` → markings can still be added but points UI is hidden and
   defaults won't apply.
7. `required: true` marking points is unused — don't rely on it.
8. Invisible markings: check `speciesRestriction`/`kindAllowance` and that `bodyPart` exists in
   `speciesBaseSprites`.
9. Wrong `Body.prototype`/missing organs → prototype load error; not overriding is safest.
10. Missing locale → raw keys in UI (no crash); wrong dataset prefix/count fails tests.
11. `forcedMarkingColor` only applies when the layer's `humanoidBaseSprite.forcedColoring` is true.

## Unknowns

- `Descriptor`/`guideBookIcon` are dead fields; intent unknown.
- Exact organ/damage behavior when overriding `species` on a non-matching mob base (verify in-game).

## Source anchors

`Content.Shared/Humanoid/{Prototypes/*,Markings/*,SharedHumanoidAppearanceSystem.cs,HumanoidAppearanceComponent.cs}`,
`Content.Client/Humanoid/HumanoidAppearanceSystem.cs`, `Content.Client/Lobby/{LobbyUIController.cs,UI/HumanoidProfileEditor.xaml.cs}`,
`Resources/Prototypes/{Species,Entities/Mobs/Species,Entities/Mobs/Player}`, fork species folders.
