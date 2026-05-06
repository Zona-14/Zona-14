using Content.Shared._Stalker.Bands;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;
using Content.Shared._ES.Viewcone;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Stalker.Bands;
/// <summary>
/// Applies status icons for specified band
/// </summary>
public sealed class BandsSystem : SharedBandsSystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!; //Zona-14
    [Dependency] private readonly TransformSystem _transform = default!; //Zona-14
    [Dependency] private readonly IGameTiming _timing = default!; //Zona-14

    private const float MaxDistanceForBandPatch = 5f; //Zona-14
    private const float RecognitionDelay = 2f; //Zona-14
    private const float KnownDuration = 30f; //Zona-14

    private readonly Dictionary<EntityUid, RecognitionData> _recognitionData = new(); //Zona-14

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BandsComponent, GetStatusIconsEvent>(OnGetStatusIcon);
    }

    //Zona-14-start
    /// <summary>
    /// Updates recognition state for all tracked entities.
    /// Handles the transition between Unknown -> Recognizing (2s) -> Known (30s) -> Expired -> Recognizing.
    /// Once in Known state, the patch remains visible indefinitely while in range (>5 tiles).
    /// Only when out of range does the 30-second timer start counting down.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player == null)
            return;

        var playerPos = _transform.GetWorldPosition(player.Value);
        var currentTime = _timing.RealTime;

        var toRemove = new List<EntityUid>();

        foreach (var (uid, data) in _recognitionData)
        {
            if (!EntityManager.EntityExists(uid))
            {
                toRemove.Add(uid);
                continue;
            }

            var entityPos = _transform.GetWorldPosition(uid);
            var distance = (playerPos - entityPos).Length();

            if (distance > MaxDistanceForBandPatch)
            {
                // Out of range
                switch (data.State)
                {
                    case RecognitionState.Unknown:
                        // Stay unknown
                        break;

                    case RecognitionState.Recognizing:
                        // Reset to unknown
                        data.State = RecognitionState.Unknown;
                        data.RecognitionStartTime = null;
                        break;

                    case RecognitionState.Known:
                        // Start countdown if not already started
                        if (!data.RecognizedTime.HasValue)
                        {
                            data.RecognizedTime = currentTime;
                        }
                        else if ((currentTime - data.RecognizedTime.Value).TotalSeconds >= KnownDuration)
                        {
                            // Timer expired
                            data.State = RecognitionState.Expired;
                            data.RecognizedTime = null;
                        }
                        break;

                    case RecognitionState.Expired:
                        // Stay expired
                        break;
                }
            }
            else
            {
                // In range
                switch (data.State)
                {
                    case RecognitionState.Unknown:
                        // Start recognition
                        data.State = RecognitionState.Recognizing;
                        data.RecognitionStartTime = currentTime;
                        break;

                    case RecognitionState.Recognizing:
                        // Check if recognition delay passed
                        if (data.RecognitionStartTime.HasValue &&
                            (currentTime - data.RecognitionStartTime.Value).TotalSeconds >= RecognitionDelay)
                        {
                            data.State = RecognitionState.Known;
                            data.RecognizedTime = null; // No timer while in range
                        }
                        break;

                    case RecognitionState.Known:
                        // Keep known, reset timer to stay visible
                        data.RecognizedTime = null;
                        break;

                    case RecognitionState.Expired:
                        // Start recognition again
                        data.State = RecognitionState.Recognizing;
                        data.RecognitionStartTime = currentTime;
                        break;
                }
            }
        }

        foreach (var uid in toRemove)
        {
            _recognitionData.Remove(uid);
        }
    }
    //Zona-14-end

    /// <summary>
    /// Determines which band patch icon to display based on distance and recognition state.
    /// </summary>
    private void OnGetStatusIcon(EntityUid uid, BandsComponent component, ref GetStatusIconsEvent args)
    {
        if (EntityManager.TryGetComponent<ESViewconeOccludableComponent>(uid, out var occ) && occ.IsHidden)
            return;

        //Zona-14-start
        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player == null)
        {
            args.StatusIcons.Add(_proto.Index<JobIconPrototype>(component.BandStatusIcon));
            return;
        }

        // Always show own patch immediately
        if (uid == player.Value)
        {
            args.StatusIcons.Add(_proto.Index<JobIconPrototype>(component.BandStatusIcon));
            return;
        }

        var playerPos = _transform.GetWorldPosition(player.Value);
        var entityPos = _transform.GetWorldPosition(uid);
        var distance = (playerPos - entityPos).Length();

        // Get or create recognition data
        if (!_recognitionData.TryGetValue(uid, out var data))
        {
            data = new RecognitionData();
            _recognitionData[uid] = data;
        }

        string iconId;

        if (distance > MaxDistanceForBandPatch)
        {
            // Out of range - show patch only if in Known state
            iconId = data.State == RecognitionState.Known ? component.BandStatusIcon : "nodata";
        }
        else
        {
            // In range
            iconId = data.State switch
            {
                RecognitionState.Unknown => "nodata",
                RecognitionState.Recognizing => "nodata",
                RecognitionState.Known => component.BandStatusIcon,
                RecognitionState.Expired => "nodata",
                _ => "nodata"
            };
        }
        //Zona-14-end

        if (!_proto.HasIndex<JobIconPrototype>(iconId))
        {
            args.StatusIcons.Add(_proto.Index<JobIconPrototype>(component.BandStatusIcon));
            return;
        }

        args.StatusIcons.Add(_proto.Index<JobIconPrototype>(iconId));
    }

    //Zona-14-start
    /// <summary>
    /// Stores recognition timing data for a single entity.
    /// </summary>
    private class RecognitionData
    {
        public RecognitionState State = RecognitionState.Unknown;
        public TimeSpan? RecognitionStartTime;
        public TimeSpan? RecognizedTime;
    }

    /// <summary>
    /// Represents the current recognition state of a band patch.
    /// </summary>
    private enum RecognitionState : byte
    {
        Unknown,
        Recognizing,
        Known,
        Expired
    }
    //Zona-14-end
}
