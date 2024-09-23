using Content.Server._Stalker.AddCustomComponent;
using Content.Server._Stalker.StationEvents.Components;
using Content.Shared.Alert;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Stalker.AddOrDelOnCollideSafeZone;

/// <summary>
/// This handles...
/// </summary>
public sealed class AddOrDelOnCollideSafeZoneSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alertsSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AddOrDelOnCollideSafeZoneComponent, StartCollideEvent>(OnCollide);
    }

    private void OnCollide(EntityUid uid, AddOrDelOnCollideSafeZoneComponent component, ref StartCollideEvent args)
    {
        if (!TryComp(args.OtherEntity, out ActorComponent? actor))
            return;

        if (actor.PlayerSession.AttachedEntity != null)
        {
            _prototype.TryIndex<AlertPrototype>("StalkerSafeZone", out var stalkerSafeZoneAlert);
            if (stalkerSafeZoneAlert == null)
                return;
            if (component.MustAdd == true)
            {
                Logger.Debug("component.MustAdd==true");
                EnsureComp<StalkerSafeZoneComponent>(actor.PlayerSession.AttachedEntity.Value);
                _alertsSystem.ShowAlert(actor.PlayerSession.AttachedEntity.Value, stalkerSafeZoneAlert);
            }
            else
            {
                Logger.Debug("component.MustAdd==false");
                RemComp<StalkerSafeZoneComponent>(actor.PlayerSession.AttachedEntity.Value);
                _alertsSystem.ClearAlert(actor.PlayerSession.AttachedEntity.Value, stalkerSafeZoneAlert);
            }
        }

    }
}
