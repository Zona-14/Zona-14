// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Zona14.Weapons.Ranged.Prediction;
using Content.Shared.Coordinates;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Zona14.Prediction;

[TestFixture]
public sealed class OffPvsFiringTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId Gun = "WeaponSniperMosin";

    // Off-PVS regression: firing toward off-screen coords (with no target entity)
    // should still spawn a server projectile, and the prediction-aware overload
    // should still tag it. Locks in the "doesn't behave properly off screen" symptom
    // against the server-side regression class.
    //
    // Note: the `InteractionTest` harness's `AttemptShoot` helper calls `SGun.AttemptShoot`
    // directly on the server, bypassing the client prediction tick. The full client→server
    // prediction roundtrip is exercised manually; we only cover the server contract here.
    [Test]
    public async Task FiringTowardsOffPvsCoordsStillSpawnsServerProjectile()
    {
        await PlaceInHands(Gun);
        await Pair.RunSeconds(2f);
        await UseInHand();
        await SetCombatMode(true);

        EntityUid gunUid = default;
        GunComponent gunComp = default!;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SGun.TryGetGun(SPlayer, out gunUid, out gunComp!), Is.True);
        });

        // Sanity-check fire (with default target=null) to clear any pickup state.
        await Server.WaitAssertion(() =>
        {
            var firedOk = SGun.AttemptShoot(SPlayer, gunUid, gunComp,
                SEntMan.GetCoordinates(TargetCoords));
            Assert.That(firedOk, Is.True, "Sanity-check: gun should fire");
        });
        await Pair.RunSeconds(2f);

        // Now aim far away — 50 tiles offset from the player. No target entity is set.
        var offCoords = SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(50f, 0f));
        await Server.WaitAssertion(() =>
        {
#pragma warning disable RA0002
            gunComp.ShootCoordinates = offCoords;
            gunComp.Target = null;
            gunComp.ShotCounter = 0;
#pragma warning restore RA0002
            var predictedIds = new List<int> { 7 };
            var spawned = SGun.AttemptShoot(SPlayer, gunUid, gunComp, predictedIds, ServerSession);
            Assert.That(spawned, Is.Not.Null.And.Not.Empty,
                "Firing toward off-PVS coords should still spawn a projectile");

            var firstProjectile = spawned![0];
            Assert.That(SEntMan.TryGetComponent(firstProjectile, out PredictedProjectileServerComponent? comp), Is.True,
                "Off-PVS-aimed projectile should still receive PredictedProjectileServerComponent");
            Assert.That(comp!.ClientId, Is.EqualTo(7));
        });
    }
}
