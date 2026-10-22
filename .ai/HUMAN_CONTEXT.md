# HUMAN-PROVIDED CONTEXT

The following information was provided by the project maintainer.

Treat it as **context and guidance**, not as proof of implementation.

If this information conflicts with the source code:

* The source code determines what the project currently does.
* The human-provided context describes intended architecture, historical decisions, terminology, or information that may not be obvious from the source.
* Record meaningful discrepancies in `.ai/UNKNOWN.md` or `.ai/HAZARDS.md`.

## Project Context

This is a Space Station 14 codebase. The game has different servers each having their own codebase, or using someone else's codebase.

Our server uses Coyote Frontier (Coyote Station). The modular folders for it are thus prefixed _CS.

Our server is named Palmtree Station, and thus we've chosen _PS as our prefix.

Coyote uses code from several other codebases such as Einstein Engines (EE), DeltaV, Estação Pirata, Floof Station, Goob Station, Corvax, etc.

Space Station 14 (SS14) is a remake of Space Station 13 (SS13)

Space Station is a round-based game where the players each with their own characters fill a role in the station. There's also antagonists who are rolled each round (at the start, and sometimes during the round).

The station wins if all antagonist fails, therefore it's a crew victory. Not all antagonists and their objectives are lethal to the station. The famous antagonist example is nukeops, and those are obviously the most lethal and hostile to the station and crew, second to the Malfunctioning AI.

In Coyote's case, there's no such thing as station-based gameplay, since it's based off Frontier Station, which makes it so the station is reduced to being just a hub where players meet, trade, buy and sell ships to explore the cosmos, making money while doing so. Players are able to take on mining, exploration, etc. There are, of course, dangers, in the forms of mobs that attack you in dungeons and asteroids. There's also pirates, but we don't use them in Coyote in actual gameplay, since we focus on PvE.

## Terminology

SS14 means Space Station 14. SS13 means Space Station 13.

The SS14 official maintainers are referred to as Wizard's Den or simply "Wizden". Thus if you see mentions of wizden or wizden code, that's what it refers to.

RobustToolbox is the game's engine.

RP stands for Roleplay. ERP stands for Erotic Roleplay, which is allowed in Coyote Station and Palmtree.

BYOND is the platform/engine of the original game, SS13. It's only historically relevant, as RobustToolbox is the engine of our project, a remake of SS13.

LRP stands for Low-Roleplay, and MRP and HRP for Medium and High RP respectively. These refer to the level of roleplay of a server. Where as LRP is basically no roleplay involved at all, just mechanical gameplay. And HRP is almost exclusively roleplay focused, putting things aside such as objectives or antagonism in favor of character writing and development.

"emag" is often a nickname to the Syndicate's Cryptographic Sequencer, the famous antagonist tool of hacking devices into doing stuff.

NanoTrasen (NT) is usually the corporation associated with the player's station in Space Station 13/14. In Palmtree and Coyote, they are not the main station anymore, and the universe expands beyond just a corporation station. The players therefore live on **Nash Station** with **NFSD** as their law enforcement.

NSFD stands for New Frontier Sherif's Department. They're the local police.

Syndicate is the rival corporation of NT. They're usually the ones sending nukeops and other antagonists to the station in normal rounds of the game.

## Architecture Known to the Maintainer

The actual game code is split between ``Content.Client`` for the client code and ``Content.Server`` for the server side code. These are focus of adding any gameplay features.

The other projects such as ``Content.Replay`` etc, are hardly ever touched.

The game uses ECS. Everything in the game is an entity, and those entities have components. Components do not contain behaviour. Only data. Systems therefore act upon said components.

A collection of components that make up an entity or a thing is called a Prototype. For example ``Resources/Prototypes/Entities/Objects/Tools/toolbox.yml`` contains a definition for a toolbox.
``ToolboxBase`` is therefore a prototype. ``ToolboxMechanical`` is a child of ``ToolboxBase``, and is a separate entity that can be spawned in. Prototypes are like Prefabs in engines like Unity.

A prototype may also define things aside from game entities, such as loading screen music and backgrounds.

The ``Resources`` folder is where all sounds, prototypes and textures reside.

The game uses its own image format known as .rsi which is basically just a folder, with spritesheets inside, corresponding to a state, and a json file defining this state. This is a very similar concept to SS13's .dmi format (dream maker image) for the BYOND engine.

## Historical Context

The game's engine name, Robust Toolbox, is also a funny reference to SS13, where the controls were so bad in the original game and hard to master that anyone capable of being lethal in PvP encounters and actually good at it were called robust. And the toolbox was a common strong weapon.

SS13 codebases usually suffer from technical debt. In fact SS13 is pretty much the definition of technical debt. SS14 however tries to be a bit cleaner. But the coders of the code we use, Coyote, are not the brightest. Finding misplaced files, misnamed resources or straight up inconsistency is not unexpected.

I've always been challeged by this codebase, and doing things with such a verbose and spread out codebase always feels like a herculean  task. Despite this, I've taken on the challenge.

As of 2026, Coyote abandoned the SS14 scene, leaving this code entirely frozen in time and up to us (Palmtree Station and you, the LLM) to modify or maintain.

## Important Constraints

Our additions must be placed under _PS folders, much like the rest of the codebases do. This allows for modularity and organization between what code is ours and what isn't ours.

The engine RobustToolbox is never to be modified.

## Things That Are Intentionally Weird

Nobody but Wizden can modify the engine. And nobody but them understands it well.

Documenting the engine and learning how to modify or work with it might be a herculean task which is best suited for an LLM, at a later time.
