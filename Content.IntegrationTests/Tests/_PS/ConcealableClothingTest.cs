using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared._PS.Clothing;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Tests the <see cref="ConcealableClothingComponent"/> implant interaction: injecting the concealment
/// implant while clothing is already equipped must grant the toggle action without re-equipping it.
/// </summary>
[TestFixture]
public sealed class ConcealableClothingTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  name: ConcealableClothingDummy
  id: ConcealableClothingDummy
  components:
  - type: Inventory
  - type: ContainerContainer
  - type: Actions

- type: playTimeTracker
  id: PSLoadoutPlayTimeTracker

- type: roleLoadout
  id: JobPSLoadoutTester
  groups:
  - ContractorImplanter

- type: job
  id: PSLoadoutTester
  playTimeTracker: PSLoadoutPlayTimeTracker
";

    [Test]
    public async Task ImplantRefreshesAlreadyEquippedClothingActions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var inventory = server.System<InventorySystem>();
        var actions = server.System<SharedActionsSystem>();
        var implants = server.System<SharedSubdermalImplantSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        EntityUid user = default;
        EntityUid implant = default;

        await server.WaitAssertion(() =>
        {
            user = entMan.SpawnEntity("ConcealableClothingDummy", testMap.GridCoords);
            var backpack = entMan.SpawnEntity("ClothingBackpack", testMap.GridCoords);
            Assert.That(inventory.TryEquip(user, backpack, "back"));

            // Equipping concealable clothing without the implant must not grant the toggle action.
            Assert.That(HasConcealmentAction(entMan, actions, user), Is.False);

            // Implanting must grant the action to the already equipped backpack.
            implant = entMan.SpawnEntity("ClothingConcealmentImplantBackpack", testMap.GridCoords);
            var implantComp = entMan.GetComponent<SubdermalImplantComponent>(implant);
            implants.ForceImplant(user, implant, implantComp);
            Assert.That(HasConcealmentAction(entMan, actions, user), Is.True);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            // Removing the implant must take the action away again.
            implants.ForceRemove(user, implant);
            Assert.That(HasConcealmentAction(entMan, actions, user), Is.False);

            mapSystem.DeleteMap(testMap.MapId);
        });

        await pair.CleanReturnAsync();
    }

    private static bool HasConcealmentAction(IEntityManager entMan, SharedActionsSystem actions, EntityUid user)
    {
        var query = entMan.GetEntityQuery<InstantActionComponent>();
        return actions.GetActions(user).Any(action => query.CompOrNull(action.Owner)?.Event is ToggleClothingConcealmentEvent);
    }

    [Test]
    public async Task LoadoutProvidesConcealmentImplanter()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var stationSpawning = server.System<StationSpawningSystem>();
        var mapSystem = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            // The shared contractor implant group must offer the concealment implanter.
            var group = protoMan.Index<LoadoutGroupPrototype>("ContractorImplanter");
            Assert.That(group.Loadouts.Contains("ContractorClothingConcealmentBackpackImplanter"), Is.True);

            var loadout = protoMan.Index<LoadoutPrototype>("ContractorClothingConcealmentBackpackImplanter");
            Assert.That(loadout.Implants.Contains("ClothingConcealmentBackpackImplanter"), Is.True);

            // Spawning with it selected must actually implant the character.
            var profile = new HumanoidCharacterProfile();
            var roleLoadout = new RoleLoadout("JobPSLoadoutTester");
            roleLoadout.SelectedLoadouts["ContractorImplanter"] =
            [
                new Loadout { Prototype = "ContractorClothingConcealmentBackpackImplanter" },
            ];
            profile.SetLoadout(roleLoadout);

            var tester = stationSpawning.SpawnPlayerMob(testMap.GridCoords, job: "PSLoadoutTester", profile, station: null);
            Assert.That(entMan.HasComponent<ConcealableClothingUserComponent>(tester), Is.True);

            mapSystem.DeleteMap(testMap.MapId);
        });

        await pair.CleanReturnAsync();
    }
}
