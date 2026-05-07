using System.Collections.Frozen;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Interaction;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Physics;
using Content.Shared.Whitelist;
using Content.Shared._Zona14.SniperZones;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage.Systems;

namespace Content.Server._Stalker.NPCs;

public sealed partial class STNPCSniperSystem : EntitySystem
{
    [Dependency] private readonly IConsoleHost _consoleHost = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    private FrozenDictionary<MapCoordinates, Entity<STNPCSniperComponent>> _hashedCoords = new Dictionary<MapCoordinates, Entity<STNPCSniperComponent>>().ToFrozenDictionary();

    public override void Initialize()
    {
        base.Initialize();

        InitializeCommands();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);

        // Zona14: cancel decision moved to SharedSniperZoneSystem so client and server agree.
        // We only consume the post-cancel server-side side-effect event.
        SubscribeLocalEvent<SniperZoneTriggeredEvent>(OnZoneTriggered);
    }

    private void OnRoundStarted(RoundStartedEvent @event)
    {
        RegenerateMap();
    }

    // Zona14: cancel decision was made in SharedSniperZoneSystem; this only runs the
    //         server-only retaliation (damage, audio, IC chat).
    private void OnZoneTriggered(ref SniperZoneTriggeredEvent ev)
    {
        var mapId = Transform(ev.User).MapID;
        var mapCoords = new MapCoordinates(new Vector2(ev.Coord.X, ev.Coord.Y), mapId);

        if (!_hashedCoords.TryGetValue(mapCoords, out var entity))
            return;

        if (HasComp<PacifiedComponent>(ev.User))
            return;

        if (_whitelistSystem.IsWhitelistPass(entity.Comp.AttackerWhitelist, ev.User))
            return;

        if (entity.Comp.SoundGunshot is not null)
            _audio.PlayPvs(entity.Comp.SoundGunshot, Transform(entity).Coordinates);

        if (entity.Comp.Damage is not null)
            _damageable.TryChangeDamage(ev.User, entity.Comp.Damage, ignoreResistances: true);

        if (entity.Comp.MessageShoot.Count > 0)
            _chat.TrySendInGameICMessage(entity, Loc.GetString(_random.Pick(entity.Comp.MessageShoot).Id), InGameICChatType.Speak, false);
    }

    private void RegenerateMap()
    {
        Log.Info("Regenerating snipers map...");
        var coords = new Dictionary<MapCoordinates, Entity<STNPCSniperComponent>>();
        var perMap = new Dictionary<MapId, List<Vector2i>>(); // Zona14: replicate to clients

        var query = EntityQueryEnumerator<STNPCSniperComponent, TransformComponent>();
        while (query.MoveNext(out var entityUid, out var sniperComponent, out var transformComponent))
        {
            var position = _transform.GetWorldPosition(transformComponent);

            // Zona14: floor-round to match SharedSniperZoneSystem.WorldToTile (negatives-correct).
            var anchor = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
            var size = sniperComponent.Range;
            var box2 = new Box2i(anchor.X - size, anchor.Y - size, anchor.X + size + 1, anchor.Y + size);

            for (var x = box2.Left; x < box2.Right; x++)
            {
                for (var y = box2.Bottom; y < box2.Top; y++)
                {
                    var mapCoords = new MapCoordinates(x, y, transformComponent.MapID);
                    if (!_interaction.InRangeUnobstructed(entityUid, mapCoords, 0f, CollisionGroup.InteractImpassable, uid => uid == entityUid))
                        continue;

                    coords.TryAdd(mapCoords, (entityUid, sniperComponent));

                    // Zona14: also accumulate the tile coord for the replicated component.
                    if (!perMap.TryGetValue(transformComponent.MapID, out var list))
                        perMap[transformComponent.MapID] = list = new List<Vector2i>();
                    list.Add(new Vector2i(x, y));
                }
            }
        }

        _hashedCoords = coords.ToFrozenDictionary();

        // Zona14: push the per-map sets into replicated components so the client sees them.
        foreach (var (mapId, list) in perMap)
        {
            var mapUid = _mapManager.GetMapEntityId(mapId);
            if (!mapUid.IsValid())
                continue;
            var comp = EnsureComp<SniperZonesComponent>(mapUid);
            comp.ForbiddenCoords = list;
            comp.LookupCache = null;
            Dirty(mapUid, comp);
        }

        Log.Info($"Sniper map regenerated: {_hashedCoords.Count} coords across {perMap.Count} maps");
    }
}
