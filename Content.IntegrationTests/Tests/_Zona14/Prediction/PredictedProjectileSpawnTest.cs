// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Zona14.Weapons.Ranged.Prediction;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Zona14.Prediction;

[TestFixture]
public sealed class PredictedProjectileSpawnTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId Mob = "MobHuman";
    // Use a weapon that's known to fire from the existing WeaponTests harness (pre-loaded chamber).
    private static readonly EntProtoId Gun = "WeaponSniperMosin";

    // Server-side prediction-tagging contract: when `AttemptShoot` is invoked with a
    // `predictedProjectiles` list and `userSession`, every spawned projectile gets a
    // `PredictedProjectileServerComponent` whose `ClientId` matches the list and whose
    // `Shooter` equals the supplied session.
    //
    // Note: the `InteractionTest` harness's `AttemptShoot` helper calls `SGun.AttemptShoot`
    // directly on the server, bypassing the client prediction tick. The full client→server
    // prediction roundtrip is exercised manually; we only cover the server contract here.
    [Test]
    public async Task ServerTagsPredictedProjectileWhenShootRequestedWithPredictedIds()
    {
        await AddAtmosphere();
        var target = await SpawnTarget(Mob);
        await PlaceInHands(Gun);
        await Pair.RunSeconds(2f);

        // Mosin requires wielding before it fires.
        await UseInHand();

        EntityUid gunUid = default;
        GunComponent gunComp = default!;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SGun.TryGetGun(SPlayer, out gunUid, out gunComp!), Is.True, "Player not holding a gun");
        });

        await SetCombatMode(true);

        // Sanity-check the upstream bool overload first; it sets ShootCoordinates internally.
        await Server.WaitAssertion(() =>
        {
            var firedOk = SGun.AttemptShoot(SPlayer, gunUid, gunComp,
                SEntMan.GetCoordinates(TargetCoords), ToServer(target));
            Assert.That(firedOk, Is.True, "Sanity-check: pistol should fire at all (bool overload)");
        });
        await RunTicks(1);

        // Now exercise the prediction-aware overload. Wait long enough to clear the SemiAuto
        // ShotCounter (the gun must drop below NextFire and ShotCounter must reset).
        await Pair.RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
#pragma warning disable RA0002 // ShootCoordinates is normally written from inside SharedGunSystem only.
            gunComp.ShootCoordinates = SEntMan.GetCoordinates(TargetCoords);
            gunComp.Target = ToServer(target);
            gunComp.ShotCounter = 0; // SemiAuto refuses if ShotCounter > 0; reset to allow another shot.
#pragma warning restore RA0002
            var predictedIds = new List<int> { 42 };
            var spawned = SGun.AttemptShoot(SPlayer, gunUid, gunComp, predictedIds, ServerSession);
            Assert.That(spawned, Is.Not.Null.And.Not.Empty, "Expected at least one projectile spawned");

            // Inspect the spawned projectile in the same tick — high-velocity ammo can
            // collide with the target on the next physics step and queue-delete itself.
            var firstProjectile = spawned![0];
            Assert.That(SEntMan.TryGetComponent(firstProjectile, out PredictedProjectileServerComponent? comp), Is.True,
                "Expected the spawned projectile to have PredictedProjectileServerComponent attached");
            Assert.That(comp!.ClientId, Is.EqualTo(42), "ClientId should match the predictedProjectiles list");
            Assert.That(comp.Shooter, Is.Not.Null, "Shooter session should be set");
        });
    }
}
