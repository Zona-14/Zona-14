// SPDX-License-Identifier: MIT
using Robust.Shared.GameStates;

namespace Content.Shared._Zona14.SniperZones;

/// <summary>
///     Marker on entities subject to the sniper-zone cancel check. Attached via
///     YAML on a base mob prototype. Lets <see cref="SharedSniperZoneSystem"/>
///     subscribe component-targeted, so we never depend on <c>broadcast: true</c>
///     on the action-attempt raise sites — preventing the prediction asymmetry
///     bomb from re-assembling itself in future PRs.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedSniperZoneSystem))]
public sealed partial class SniperZoneCheckComponent : Component;
