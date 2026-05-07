// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using Content.Shared._Zona14.Weapons.Ranged.Prediction;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Zona14.Prediction;

[TestFixture]
public sealed class PredictionComponentRegistrationTest
{
    [Test]
    public async Task PredictionComponentsRegistered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var compFactory = pair.Server.ResolveDependency<IComponentFactory>();

        Assert.Multiple(() =>
        {
            Assert.That(compFactory.GetRegistration<IgnorePredictionHideComponent>(), Is.Not.Null);
            Assert.That(compFactory.GetRegistration<IgnorePredictionHitComponent>(), Is.Not.Null);
            Assert.That(compFactory.GetRegistration<GunIgnorePredictionComponent>(), Is.Not.Null);
            Assert.That(compFactory.GetRegistration<PredictedProjectileServerComponent>(), Is.Not.Null);
            Assert.That(compFactory.GetRegistration<PredictedProjectileClientComponent>(), Is.Not.Null);
            Assert.That(compFactory.GetRegistration<PredictedProjectileHitComponent>(), Is.Not.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void RequestShootEventCarriesShotAndTick()
    {
        var ev = new RequestShootEvent
        {
            Shot = new List<int> { 1, 2, 3 },
            LastRealTick = new GameTick(42),
        };

        Assert.That(ev.Shot, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(ev.LastRealTick, Is.EqualTo(new GameTick(42)));
    }
}
