// SPDX-License-Identifier: MIT
using System.Reflection;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Stalker.NPCs;
using Content.Shared._Zona14.SniperZones;
using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Zona14.Prediction;

public sealed class SniperZonePredictionSymmetryTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId Pistol = "WeaponPistolMk58";
    private static readonly EntProtoId SniperZoneSource = "STNPCSniper";

    [Test]
    public async Task ShotInsideZoneIsCancelledOnBothSides()
    {
        await AddAtmosphere();
        var pistolNet = await PlaceInHands(Pistol);
        var pistolEnt = ToServer(pistolNet);

        await Pair.RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            var playerXform = SEntMan.GetComponent<TransformComponent>(SPlayer);
            SEntMan.SpawnAtPosition(SniperZoneSource, playerXform.Coordinates);

            var sniperSys = Server.System<STNPCSniperSystem>();
            sniperSys.GetType()
                .GetMethod("RegenerateMap", BindingFlags.NonPublic | BindingFlags.Instance)?
                .Invoke(sniperSys, null);
        });

        await Pair.RunSeconds(1f);

        await Client.WaitAssertion(() =>
        {
            var mapUid = CEntMan.GetComponent<TransformComponent>(CPlayer).MapUid;
            Assert.That(mapUid, Is.Not.Null, "client player must be on a map");
            Assert.That(CEntMan.HasComponent<SniperZonesComponent>(mapUid!.Value), Is.True,
                "client must receive the replicated SniperZonesComponent — symmetry contract");
        });

        var gunSys = SEntMan.System<SharedGunSystem>();
        var startAmmo = gunSys.GetAmmoCount(pistolEnt);

        await AttemptShoot(TargetCoords, false);
        await RunTicks(4);

        Assert.That(gunSys.GetAmmoCount(pistolEnt), Is.EqualTo(startAmmo),
            "shot inside sniper zone must be cancelled — ammo count unchanged");

        await Server.WaitAssertion(() =>
        {
            var dmg = SEntMan.GetComponent<DamageableComponent>(SPlayer);
            Assert.That(dmg.TotalDamage.Value, Is.GreaterThan(0),
                "STNPCSniperSystem retaliation damage should apply server-side");
        });
    }

}
