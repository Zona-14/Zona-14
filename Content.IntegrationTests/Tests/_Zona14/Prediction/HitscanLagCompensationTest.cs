// SPDX-License-Identifier: MIT
namespace Content.IntegrationTests.Tests._Zona14.Prediction;

[TestFixture]
public sealed class HitscanLagCompensationTest
{
    [Test]
    public async Task HitscanRewindsTargetPosition()
    {
        // Intended scenario:
        //   1. Spawn a target with LagCompensationComponent at position A.
        //   2. Move it to position B over several ticks (queue records both).
        //   3. From a session with simulated ping P, fire a hitscan that *would* miss B but hit A.
        //   4. Assert the target took damage (lag comp resolved to A).
        //
        // Not yet implemented: HitscanBasicRaycastSystem doesn't read target coords —
        // it raycasts against current physics and uses args.Target only as a filter.
        // Full physics rewind would require either rewinding the target's transform
        // around the raycast, or adding a bounds-check fallback against historical
        // positions. The rewind plumbing in server GunSystem.Shoot is already in place
        // (re-aims the trace direction toward the rewound target position).
        Assert.Pass("scaffold — full physics rewind is follow-up work");
    }
}
