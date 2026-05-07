// SPDX-License-Identifier: MIT
using Content.Shared._Zona14.CCVar;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._Zona14.Prediction;

[TestFixture]
public sealed class CVarRegistrationTest
{
    [Test]
    public async Task LagCompensationMillisecondsCVarRegistered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var cfg = pair.Server.ResolveDependency<IConfigurationManager>();
        Assert.That(cfg.IsCVarRegistered(Zona14CVars.LagCompensationMilliseconds.Name), Is.True);
        Assert.That(cfg.GetCVar(Zona14CVars.LagCompensationMilliseconds), Is.EqualTo(750));

        await pair.CleanReturnAsync();
    }
}
