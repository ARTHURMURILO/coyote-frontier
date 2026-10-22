# Guide: Adding an Admin Command

> Answer to: "How can one add a new admin command?"
> Facts from source; inferred items marked. Line numbers valid for branch `palm3` commit `4bfdad813c`.

## TL;DR

Create a class implementing `IConsoleCommand` (or `LocalizedCommands`) with a parameterless constructor
and `[Dependency]` fields, put `[AdminCommand(AdminFlags.X)]` on the **class**, and add loc strings.
`ConsoleHost` auto-discovers it — no registration needed. Omit the attribute and the command becomes
server-console-only.

## Infrastructure

| Piece | Path | Notes |
|---|---|---|
| `IConsoleCommand` | `RobustToolbox/Robust.Shared/Console/IConsoleCommand.cs:12-85` | `Command`, `Description`, `Help`, `Execute(IConsoleShell, string argStr, string[] args)`, `GetCompletion` |
| `LocalizedCommands` | `RobustToolbox/Robust.Shared/Console/LocalizedCommands.cs:9-39` | Injects localization; auto keys `cmd-<command>-desc` / `-help` |
| `LocalizedEntityCommands` | same file `:53-57` | Adds `EntityManager`; registered only while entity systems are active |
| Discovery | `RobustToolbox/Robust.Shared/Console/ConsoleHost.cs:62-82` | Reflection over `IConsoleCommand`; abstract types skipped; duplicate names throw at startup |
| `[AdminCommand]` | `Content.Server/Administration/AdminCommandAttribute.cs:14-24` | Class or method; multiple attributes = any matching flag |
| `[AnyCommand]` | `Content.Shared/Administration/AnyCommandAttribute.cs:10-15` | Anyone may run; wins over `[AdminCommand]` |
| `AdminFlags` | `Content.Shared/Administration/AdminFlags.cs:7-136` | `Admin, Ban, Debug, Fun, Permissions, Server, Spawn, VarEdit, Mapping, Logs, Round, Query, Adminhelp, ViewNotes, EditNotes, MassBan, Stealth, Adminchat, Pii, Moderator, AdminWho, NameColor, Whitelist, Host` |
| Permission cache | `Content.Server/Administration/Managers/AdminManager.cs:250-309,611-634` | Built once in `Initialize()` from discovered commands; no attribute → `isAvail=false` → server console only |
| Enforcement | `RobustToolbox/Robust.Server/Console/ServerConsoleHost.cs:154-157` | `ShellCanExecute` → `AdminManager.CanCommand` |
| Rank flags | `Content.Server.Database/Model.cs:676-702`; edited via `PermissionsEui` (`permissions` command) | Rank flags + per-admin positive/negative overrides |
| Toolshed | `RobustToolbox/Robust.Shared/Toolshed/Attributes.cs:13,23`, `ToolshedEnvironment.cs:66-96` | `[ToolshedCommand]` + `[CommandImplementation]`; class-level attribute only (cache keys by class name) |

## Traced examples

### A. Auto-discovered classic command — `SetGamePresetCommand`
`Content.Server/GameTicking/Commands/SetGamePresetCommand.cs:11-66`: `[AdminCommand(AdminFlags.Round)]`
on the class, `[Dependency] IEntityManager, IPrototypeManager`, `Execute` validates `args.Length`,
`shell.WriteError(Loc.GetString(...))`, resolves `GameTicker` via `_entity.System<GameTicker>()`,
success via `shell.WriteLine`; `GetCompletion` returns `CompletionResult.FromHintOptions(...)`.

### B. Inline method-registered command — `addgamerule`
`Content.Server/GameTicking/GameTicker.GameRule.cs`: registration in `Initialize()`
(`_consoleHost.RegisterCommand("addgamerule", "", "addgamerule <rules>", AddGameRuleCommand, AddGameRuleCompletions)` :29-33),
method-level `[AdminCommand(AdminFlags.Fun)]` (:330-331), `NetEntity.TryParse` for entity args (:368-395).
Registration must happen in `Initialize()` (not later) because `AdminManager` builds its permission
cache once, after entity systems initialize.

### C. Fork-local example — `coyoteappraisegrid`
`Content.Server/_CS/Cargo/Systems/PricingSystem.cs:26-33`: inline `RegisterCommand` with method-level
`[AdminCommand(AdminFlags.Debug)]`.

## Recipe — `psgrant <player> <amount>`

1. Create `Content.Server/_PS/Administration/Commands/PsGrantCommand.cs`, namespace
   `Content.Server._PS.Administration.Commands`.
2. Class skeleton:
```csharp
[AdminCommand(AdminFlags.Admin)]
public sealed class PsGrantCommand : LocalizedCommands
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public override string Command => "psgrant";
    // Description/Help default to cmd-psgrant-desc / cmd-psgrant-help from LocalizedCommands

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2) { shell.WriteError(Loc.GetString("shell-wrong-arguments-number")); return; }
        if (!int.TryParse(args[1], out var amount)) { shell.WriteError(Loc.GetString("cmd-psgrant-invalid")); return; }
        if (!_playerManager.TryGetSessionByUsername(args[0], out var session))
        { shell.WriteError(Loc.GetString("parse-session-fail", ("username", args[0]))); return; }
        // ... apply the grant via your manager/system ...
        shell.WriteLine(Loc.GetString("cmd-psgrant-success", ("player", session.Name), ("amount", amount)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        => args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                    CompletionHelper.SessionNames(players: _playerManager), Loc.GetString("cmd-psgrant-arg-player")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-psgrant-arg-amount")),
            _ => CompletionResult.Empty,
        };
}
```
3. Localization: `Resources/Locale/en-US/_PS/commands/psgrant.ftl` with `cmd-psgrant-desc`,
   `cmd-psgrant-help`, arg hints, success/error strings. Reuse generic keys from
   `Resources/Locale/en-US/shell.ftl` (`shell-command-success`, `shell-wrong-arguments-number`,
   `parse-session-fail`). Example: `Resources/Locale/en-US/administration/commands/announce-command.ftl`.
4. Registration: none needed for class-based commands. For inline registration, call
   `_consoleHost.RegisterCommand(...)` in an `EntitySystem.Initialize()` and `UnregisterCommand` in
   `Shutdown()`.
5. Permissions: reuse an existing `AdminFlags`; add a new bit only if you also want it in rank UI.
6. Test:
   - Server console: run `psgrant ...` (no player session ⇒ permission checks bypassed).
   - In-game: promote via `promotehost`, `permissions` EUI, or a rank; `reloadadmin` if needed.
   - Integration tests: `server.ExecuteCommand("psgrant ...")` (`TestPair.Helpers.cs:94`) or
     `consoleHost.GetSessionShell(session).ExecuteCommand(...)` (`Tests/Commands/SuicideCommandTests.cs:95`).

## Argument patterns

- **NetEntity**: `NetEntity.TryParse(args[i], out var net)` + `IEntityManager.TryGetEntity(net, out var uid)`
  (`EndGameRuleCommand`, `MakeGhostRoleCommand`).
- **Player session**: `IPlayerManager.TryGetSessionByUsername` + `CompletionHelper.SessionNames`.
- **Prototype ID**: `IPrototypeManager.HasIndex`/`TryIndex` + `CompletionHelper.PrototypeIDs<T>()`.
- **EntityUid**: possible but only safe for local/server input; prefer `NetEntity`.

## Pitfalls

- **Parameterless constructor required** (reflection instantiation); all dependencies via fields.
- **Duplicate command name throws at startup** — pick a unique name.
- **No attribute ⇒ server-console-only**; silently absent from player permission caches.
- **Inline registration after `AdminManager.Initialize()`** won't be permission-cached (cache is built
  once); register in `Initialize()`.
- **Attribute placement**: class-level for `IConsoleCommand`/toolshed; method-level only works for
  `RegisterCommand` callbacks.
- **`[AdminCommand]` lives in `Content.Server`** — shared/client commands can only use `[AnyCommand]`.
- **Toolshed**: class name must end in `Command` (or set `Name`); needs ≥1 `[CommandImplementation]`;
  no per-subcommand permissions.
- **`shell.Player` is null for server console** — check before using a player.
- **Descriptions are broadcast to all clients** — no sensitive data.
- **Entity args**: prefer `NetEntity`; completion helpers `CompletionHelper.NetEntities`,
  `.Components<T>`, `.PrototypeIDs<T>`, `.SessionNames`.

## Unknowns

- No in-repo guide beyond XML comments; permission behavior tests are minimal
  (`Content.IntegrationTests/Tests/Toolshed/ToolshedTest.cs`).
- Toolshed is server-only in content (`ToolshedManager.Permissions.cs:38-41` throws on client).

## Source anchors

`RobustToolbox/Robust.Shared/Console/{IConsoleCommand,LocalizedCommands,ConsoleHost}.cs`,
`Content.Server/Administration/AdminCommandAttribute.cs`,
`Content.Shared/Administration/{AdminFlags,AnyCommandAttribute}.cs`,
`Content.Server/Administration/Managers/AdminManager.cs`,
`Content.Server/Administration/Commands/`, `Content.Server/_CS/Cargo/Systems/PricingSystem.cs`.
