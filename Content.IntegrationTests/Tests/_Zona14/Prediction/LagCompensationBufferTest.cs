// SPDX-License-Identifier: MIT
using System.Linq;
using System.Numerics;
using Content.Server.Movement.Components;
using Content.Server.Movement.Systems;
using Content.Shared.Coordinates;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Zona14.Prediction;

[TestFixture]
public sealed class LagCompensationBufferTest
{
    private static readonly EntProtoId DummyMob = "MobHuman";

    [Test]
    public async Task PositionQueueGrowsAndPrunes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var lagSys = pair.Server.System<LagCompensationSystem>();
        var xformSys = pair.Server.System<SharedTransformSystem>();

        EntityUid mob = default;
        Vector2 origin = default;
        await pair.Server.WaitAssertion(() =>
        {
            var player = pair.Server.PlayerMan.Sessions.First();
            if (player.AttachedEntity is not { } ent)
            {
                Assert.Fail("no attached");
            }
            else
            {
                mob = entMan.SpawnAtPosition(DummyMob, ent.ToCoordinates());
                entMan.EnsureComponent<LagCompensationComponent>(mob);
                origin = xformSys.GetWorldPosition(mob);
            }
        });

        for (var i = 0; i < 20; i++)
        {
            await pair.Server.WaitAssertion(() =>
            {
                xformSys.SetWorldPosition(mob, origin + new Vector2(i * 0.05f, 0));
            });
            await pair.RunTicksSync(2);
        }

        await pair.Server.WaitAssertion(() =>
        {
            var lag = entMan.GetComponent<LagCompensationComponent>(mob);
            Assert.That(lag.Positions.Count, Is.GreaterThanOrEqualTo(5),
                "expected several positions recorded after moving the mob");
        });

        await pair.Server.WaitAssertion(() =>
        {
            lagSys.BufferTime = TimeSpan.FromMilliseconds(50);
        });

        await pair.RunSeconds(1);

        await pair.Server.WaitAssertion(() =>
        {
            var lag = entMan.GetComponent<LagCompensationComponent>(mob);
            Assert.That(lag.Positions.Count, Is.LessThan(5),
                "queue should be pruned to recent positions only after lowering BufferTime");
        });

        await pair.CleanReturnAsync();
    }
}
