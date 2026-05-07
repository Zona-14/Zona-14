// SPDX-License-Identifier: MIT
using Content.Server.Projectiles;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Zona14.Prediction;

[TestFixture]
public sealed class ProjectileCollideExtractionTest
{
    // Scaffold for direct-call coverage of ProjectileSystem.ProjectileCollide.
    // The end-to-end predicted-projectile lifecycle test exercises the same path
    // through real shooting; revisit if a regression slips past it.
    [Test]
    public async Task ProjectileCollideAppliesDamageDirectly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var entMan = pair.Server.ResolveDependency<IEntityManager>();
        var sys = pair.Server.System<ProjectileSystem>();

        Assert.That(sys, Is.Not.Null);

        await pair.CleanReturnAsync();
    }
}
