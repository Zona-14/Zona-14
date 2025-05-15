using Content.Shared._DZ.FarGunshot;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._DZ.FarGunshot;

public sealed class FarGunshotSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FarGunshotComponent, FargunshotEvent>(OnFarGunshot);

    }

    public void OnFarGunshot(EntityUid uid, FarGunshotComponent component, FargunshotEvent args)
    {

        if (uid == EntityUid.Invalid || component.Range <= 14f || component.IsIntegredSilencer || args.gunUid != uid)
            return;

        var shootPos = _transform.GetMapCoordinates(uid);

        // Create a filter for players who are far enough to hear the distant gunshot,
        // excluding those within close range (14)
        var farSoundFilter = Filter.Empty()
            .AddInRange(shootPos, component.Range)
            .RemoveInRange(shootPos, 14f);

        // Play the distant gunshot sound globally:
        // - Enabled for replay recording
        // - Not toggleable by the player (forced playback)
        _audio.PlayGlobal(
            component.Sound,
            farSoundFilter,
            recordReplay: true,
            AudioParams.Default
        );
    }

}
