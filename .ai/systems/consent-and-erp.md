# System: Consent & ERP-Adjacent Content

> Coyote/Palmtree is explicitly an adult-RP-permitting server (maintainer context). This document records
> what the code actually enforces — not what policy may intend.

## Purpose

Account/character consent settings (imported from Floofstation), consent checks in content systems, and
the ERP-adjacent feature set added by `_Floof`, `_CS` and `_PS`.

## Consent architecture

| Piece | Path |
|---|---|
| Shared data | `Content.Shared/Consent/PlayerConsentSettings.cs:9`, `ConsentTogglePrototype.cs:9`, `SharedConsentSystem.cs:12`, `MsgUpdateConsent.cs:12` |
| Server manager | `Content.Server/Consent/ServerConsentManager.cs:17`, `ConsentSystem.cs:49` |
| Client manager/UI | `Content.Client/Consent/ClientConsentManager.cs:11`, `Content.Client/Consent/UI/Windows/ConsentWindow.xaml(.cs)` |
| Toggle prototypes | `Resources/Prototypes/consent.yml` |
| Locale | `Resources/Locale/en-US/Blep/consent.ftl` |
| DB | `Content.Server.Database/Model.cs:450,459` (+ `Profile.CharacterConsentFreetext:416`) |
| Persistence code | `ServerDbBase.cs:1320-1429` |
| CVar | `consent.freetext_max_length` (`Content.Shared/Floofstation/FSCVars.cs:17`) |

Data model:
- Account-scope `Freetext` + toggles; character-scope `CharacterFreetext` (per profile slot).
- Toggles (`Resources/Prototypes/consent.yml`): `Vore`, `Digestion`, `Hypno` (unused), `NoClone` (unused),
  `NSFWDescriptions`, `CanSmellLewdScents`, `GenitalMarkings`, `Aphrodisiacs`, `AphrodisiacsVisibility`,
  `SizeManipulation`, `Transformation`; `Cum`/`NaturalLubricant` commented out.
- Only "on" values are stored; unknown/off toggles are dropped by `EnsureValid`.
- Client only ever holds its own settings; `GetConsent()` throws before load.

Flow: client window → `MsgUpdateConsent` → server requires pre-loaded data, validates, admin-logs
(`LogType.Consent`, Medium), persists for static user IDs, echoes back. `ReloadCharacterConsent` fires
on character-slot switch and profile save. Unknown/disconnected users get empty settings (all off).

## Enforcement points (`ConsentSystem.HasConsent` callers)

| Toggle | System | Location | Behavior |
|---|---|---|---|
| `Vore` | `VoreSystem` | `Content.Server/FloofStation/VoreSystem.cs:85-108` | Devour verb requires **both** user and target consent |
| `Digestion` | `VoreSystem` | `:153,469` | Digest verb/tick blocked without consent |
| `GenitalMarkings` | `ModifyUndiesSystem` | `Content.Server/_Floof/ModifyUndies/ModifyUndiesSystem.cs:68-79` | Others toggling your genital markings; self always allowed |
| `SizeManipulation` | `SizeManipulationSystem` | `Content.Server/_CS/Body/Systems/SizeManipulationSystem.cs:72-79` | Size gun hit blocked with popup; reverter bypasses |
| `Transformation` | `TransformationToolSystem` | `Content.Server/Tools/TransformationToolSystem.cs:166-170` | Polymorph tool blocked |
| `NSFWDescriptions` | `CustomExamineSystem` | `Content.Shared/_Floof/Examine/CustomExamineSystem.cs:18,48-66` | Hidden text until examiner consents |
| `CanSmellLewdScents` | `ScentSystem` | `Content.Shared/_CS/SniffAndSmell/ScentSystem.cs:840-848` | Admin ghosts bypass |
| `Aphrodisiacs` | `ConsentCondition` | `Content.Shared/EntityEffects/EffectConditions/ConsentCondition.cs:20-24` | Reagent effects gated |
| `AphrodisiacsVisibility` | `AphroLacedVisibilitySystem` (client) | `Content.Client/_CS/AphroLacedVisibility/AphroLacedVisibilitySystem.cs:19-71` | Shows "laced" icon/note |
| `Hypno`, `NoClone` | — | — | **No callers; no effect** |

For building a consent-gated interaction UI (e.g. kiss/hug buttons), see `.ai/guides/adding-custom-ui.md`.

Semantics gotchas:
- `SharedConsentSystem.HasConsent` returns **false on the client**; only server enforcement is meaningful.
- Mindless/NPC entities consent to **everything** (`ConsentSystem.cs:54`).
- No consent checks exist in stripping, clothing removal, interaction verbs, or generic examine.
- `CharacterInfoSystem` returns flavor text **and both consent freetexts** for any requested entity with
  no range check — treat as public data (see `.ai/HAZARDS.md`).

## ERP-adjacent systems

| Feature | Location | Purpose | Consent interaction |
|---|---|---|---|
| CustomExamine | `Content.Shared/_Floof/Examine/*`, client window | Player-authored public/subtle examine text (range 20/2, expiry, markup sanitized, in-heat/rut flags) | `NSFWDescriptions` on examiner |
| ModifyUndies | `Content.Server/_Floof/ModifyUndies/*` | Show/hide undergarment/genital markings via verb + do-after | `GenitalMarkings` on target for others |
| Vore | `Content.Server/FloofStation/VoreSystem.cs` | Devour/stomach/digestion | `Vore` both sides, `Digestion` prey |
| AphroLaced | `Content.Shared/_CS/AphroLacedVisibility/*`, `Helpers/ServerAphrodisiacChecker.cs` | Marks containers with aphrodisiac reagents | Viewer's `AphrodisiacsVisibility` |
| HornyExamineQuirks | `Content.Shared/_CS/HornyQuirks/*`, `Resources/Prototypes/_CS/hornyExamines.yml` | Temperament/body-type examine text; suppression trait | none directly |
| ScentSystem | `Content.Shared/_CS/SniffAndSmell/*` | Passive proximity smells + "Smell" verb, cooldowns, popups | `CanSmellLewdScents` for lewd scents |
| SizeManipulation / HeightAdjust | `Content.Server/_CS/Body/Systems/*`, `Content.Shared/HeightAdjust/*` | Grow/shrink via gun/effects | `SizeManipulation` on target |
| Genital markings (`_PS`) | `Resources/Prototypes/_PS/Entities/Mobs/Customization/Markings/genitals.yml` | Breast/genital sprites, draw order | Gated via ModifyUndies/draw logic |
| `kindAllowance` markings | `Content.Shared/Humanoid/Markings/MarkingPrototype.cs:24`, `MarkingManager.cs:170-210` | Share markings across species kinds (`_CS` addition) | none |
| Leg displacement / height-width | `HumanoidAppearanceComponent.cs:144,175-186`, `_CS/LegDisplacementPrototype.cs` | Digitigrade legs, per-profile size | none |
| `_PS` ConcealableClothing | `Content.Shared/_PS/Clothing/*` + server/client systems | Implant toggles clothing visibility | none |
| Body-type/horny traits | `Content.Server/Traits/TraitSystem.cs:63-84`, `_CS/body_type_traits.yml`, `_CS/Traits.yml` | Traits add quirk/scent components at spawn | none |

Note: `FlarpiSettingsPrototype` is **RPI economy**, not ERP. `_CS/ass.txt` is an orphaned body-descriptor
word list referenced by nothing.

## Examine pipeline (where most ERP text lands)

1. Client examine request → server validates range/occlusion (`ExamineSystemShared.CanExamine`).
2. `ExamineSystemShared.GetExamineText` (`:256-291`) builds text from metadata + `ExaminedEvent`
   subscribers, sorted by group/priority; then `ExamineCompletedEvent`.
3. ERP-adjacent contributors: CustomExamine (group priority -1), HornyExamineQuirks
   (group `"DanIsCool"`, body priority 10, temperaments 0), ScentSystem (priority 20, colored).
4. Client displays server-built text; character detail window is a separate network path.

## Gating

- Only `consent.freetext_max_length` is live. `floof.consent_rules` is defined but never read.
- No ERP/NSFW CVar or admin-permission gate exists.
- Admin bypasses: AdminGhost skips lewd-scent block; `CanChangeExamine` allows self or admin.

## Persistence

- Account: `ConsentSettings` + `ConsentToggle`; character freetext: `Profile.CharacterConsentFreetext`.
- Export/import: character YAML includes `CharacterConsentFreetext`, `AccountConsentFreetext`,
  `ConsentToggles`; importing can update live consent (`HumanoidProfileEditor.xaml.cs:2046-2094`).
- Character freetext saves only when consent is saved (`ServerDbBase.cs:1374-1382`).

## Dependencies

Preferences/DB, mind/session lookup, examine system, humanoid appearance/markings, `_Floof`/`_CS` systems,
reagents/entity effects.

## Unknowns

- Intended behavior of unused toggles (`Hypno`, `NoClone`) and gating config (`floof.consent_rules`).
- Whether the CharacterInfo exposure is intended.
- Whether stripped/removed features (old consent examine verb) should be restored.
