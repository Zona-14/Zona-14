using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Stalker.Bands.Components;
using Content.Server.Database;
using Content.Server.Mind;
using Content.Shared._Stalker.Bands;
using Content.Shared.Database;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Content.Shared.Actions; // Added
using Content.Shared.Mobs.Systems; // Added
using Content.Shared.StatusIcon; // Added
using Content.Shared.StatusIcon.Components; // Added
using Robust.Shared.Player; // Added for ICommonSession
using Content.Server.Players.JobWhitelist; // Added for JobWhitelistManager
using Robust.Shared.Log; // Added for Logger

namespace Content.Server._Stalker.Bands
{
    public sealed class BandsSystem : SharedBandsSystem
    {
        [Dependency] private readonly IServerDbManager _dbManager = default!;
        [Dependency] private readonly MindSystem _mindSystem = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
        [Dependency] private readonly SharedActionsSystem _actions = default!; // Added
        [Dependency] private readonly MobStateSystem _mobState = default!; // Added
        [Dependency] private readonly JobWhitelistManager _jobWhitelistManager = default!; // Added based on new info

        // Define a server-side representation combining prototype and potentially DB info
        private sealed record ServerBandInfo(STBandPrototype Prototype, StalkerBand? DbBand = null);


        public override void Initialize()
        {
            base.Initialize();
            // SubscribeLocalEvent<BandsManagingComponent, BoundUIOpenedEvent>(OnUiOpen); // Replaced by UpdateUiState triggered by base event
            SubscribeLocalEvent<BandsManagingComponent, BoundUIOpenedEvent>(HandleBoundUIOpen); // New handler to call UpdateUiState
            SubscribeLocalEvent<BandsManagingComponent, ComponentStartup>(SubscribeUpdateUiState); // Add startup subscription
            SubscribeLocalEvent<BandsManagingComponent, BandsManagingAddMemberMessage>(OnAddMember);
            SubscribeLocalEvent<BandsManagingComponent, BandsManagingRemoveMemberMessage>(OnRemoveMember);

            // Added subscriptions from SharedBandsSystem
            SubscribeLocalEvent<BandsComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<BandsComponent, ComponentRemove>(OnRemove);
            SubscribeLocalEvent<BandsComponent, ToggleBandsEvent>(OnToggle);
            SubscribeLocalEvent<BandsComponent, ChangeBandEvent>(OnChange);
        }

        // --- UI Update Subscription & Handling ---
        private void SubscribeUpdateUiState<T>(Entity<BandsManagingComponent> ent, ref T ev) where T : notnull
        {
             // Call UpdateUiState, passing null for actor as ComponentStartup doesn't have one readily available.
             // UpdateUiState will attempt to find a session from the UI itself.
            UpdateUiState(ent);
        }

        // Explicit handler for BoundUIOpenedEvent to pass the actor correctly
        private void HandleBoundUIOpen(Entity<BandsManagingComponent> ent, ref BoundUIOpenedEvent args)
        {
            UpdateUiState(ent, args.Actor);
        }


        // --- UI State Update Method ---
        private async void UpdateUiState(Entity<BandsManagingComponent> ent, EntityUid? actor = null)
        {
            var (uid, component) = ent;

            NetUserId? userId = null;
            ICommonSession? session = null; // Keep session for potential future use

            // Prioritize actor if provided (e.g., from BoundUIOpenedEvent)
            if (actor != null && _mindSystem.TryGetSession(actor.Value, out session))
            {
                userId = session.UserId;
            }
            else
            {
                // Attempt to get any player session associated with the UI if actor is null (e.g., from ComponentStartup)
                if (_uiSystem.TryGetUi(uid, component.Key, out var ui) && ui.SubscribedSessions.FirstOrDefault() is { } firstSession)
                {
                     userId = firstSession.UserId;
                     session = firstSession; // Store the found session
                }
                 else
                 {
                    // Cannot determine user context, cannot update UI meaningfully.
                    Logger.WarningS("bands", $"Could not determine user context for BandsManaging UI update on {ToPrettyString(uid)}");
                    // Send an empty/default state
                    _uiSystem.SetUiState(uid, component.Key, new BandsManagingBoundUserInterfaceState(null, 0, new(), false));
                    return;
                 }
            }

            // If userId is still null after checks, we cannot proceed.
            if (userId == null)
            {
                 Logger.ErrorS("bands", $"Failed to obtain NetUserId for BandsManaging UI update on {ToPrettyString(uid)}.");
                _uiSystem.SetUiState(uid, component.Key, new BandsManagingBoundUserInterfaceState(null, 0, new(), false));
                 return;
            }


            var bandInfo = await GetPlayerBandInfoAsync(userId.Value);
            // Don't return early if bandInfo is null, send state indicating no band or not manageable.
            List<BandMemberInfo> members = new(); // Use BandMemberInfo directly
            string? bandName = null;
            int maxMembers = 0;
            bool canManage = false;

            if (bandInfo != null)
            {
                members = await GetBandMembersAsync(bandInfo.Prototype); // This now returns List<BandMemberInfo>
                bandName = bandInfo.Prototype.Name;
                maxMembers = bandInfo.Prototype.MaxMembers;
                canManage = await CanPlayerManageBandAsync(userId.Value);
            }

            // No longer need to convert, members is already List<BandMemberInfo>
            // var memberInfos = members.Select(m => new BandMemberInfo(m.UserId, m.LastSeenUserName, m.PrimaryRoleId)).ToList();
            var state = new BandsManagingBoundUserInterfaceState(bandName, maxMembers, members, canManage); // Use members directly

            // Use the correct SetUiState overload - no session needed here.
            _uiSystem.SetUiState(uid, component.Key, state);
        }


        // --- Action Handlers ---
        // OnAddMember and OnRemoveMember remain largely the same, but call the new UpdateUiState
        private async void OnAddMember(EntityUid uid, BandsManagingComponent component, BandsManagingAddMemberMessage msg)
        {
            // Use the requested pattern for getting session from the message actor
            if (msg.Actor is not { } actor || !_mindSystem.TryGetSession(actor, out var session))
                return;

            var leaderUserId = session.UserId;

            if (!await CanPlayerManageBandAsync(leaderUserId)) // Use helper
                return;

            var targetPlayerRecord = await _dbManager.GetPlayerRecordByUserName(msg.PlayerName);
            if (targetPlayerRecord == null)
            {
                // TODO: Send feedback to the user that the player was not found
                return;
            }

            var targetUserId = targetPlayerRecord.UserId;

            // Prevent adding self
            if (leaderUserId == targetUserId)

            var leaderBandInfo = await GetPlayerBandInfoAsync(leaderUserId);
            if (leaderBandInfo == null)
                return; // Leader isn't in a band? Should not happen if they can manage.

            var currentMembers = await GetBandMembersAsync(leaderBandInfo.Prototype);
            if (currentMembers.Count >= leaderBandInfo.Prototype.MaxMembers)
            {
                 // TODO: Send feedback to the user that the band is full
                return;
            }

            if (currentMembers.Any(m => m.UserId == targetUserId))
            {
                // TODO: Send feedback to the user that the player is already in the band
                return;
            }

            var targetBandInfo = await GetPlayerBandInfoAsync(targetUserId);
            if (targetBandInfo != null)
            {
                 // TODO: Send feedback to the user that the target player is already in another band
                return;
            }

            // Find the lowest rank role ID from the band's hierarchy prototype
            var lowestRankRoleId = leaderBandInfo.Prototype.Hierarchy.Count > 0
                ? leaderBandInfo.Prototype.Hierarchy.OrderBy(kvp => kvp.Key).FirstOrDefault().Value?.ID // Use Role ID from prototype
                : null; // Handle case where hierarchy might be empty

            if (string.IsNullOrEmpty(lowestRankRoleId))
            {
                Logger.ErrorS("bands", $"Band {leaderBandInfo.Prototype.Name} (ProtoID: {leaderBandInfo.Prototype.ID}) has no roles defined in hierarchy for adding members.");
                return;
            }

            // Add the lowest rank role to the target player using JobWhitelistManager or DBManager
            // Assuming AddRoleWhitelistAsync is the correct method
            await _dbManager.AddRoleWhitelistAsync(targetUserId, lowestRankRoleId);

            // Update UI state after modification
            UpdateUiState((uid, component), actor);
        }

        private async void OnRemoveMember(EntityUid uid, BandsManagingComponent component, BandsManagingRemoveMemberMessage msg)
        {
            // Use the requested pattern for getting session from the message actor
            if (msg.Actor is not { } actor || !_mindSystem.TryGetSession(actor, out var session))
                return;

            var leaderUserId = session.UserId;

            if (!await CanPlayerManageBandAsync(leaderUserId)) // Use helper
                return;

            // Prevent leader from removing themselves via this UI (they should leave the band differently)
            if (leaderUserId.UserId == msg.PlayerUserId)
                 return;

            var leaderBandInfo = await GetPlayerBandInfoAsync(leaderUserId);
            if (leaderBandInfo == null)
                return; // Leader isn't in a band?

            // Construct a set of all role IDs associated with this band prototype's hierarchy
            var bandRoleIds = leaderBandInfo.Prototype.Hierarchy.Values
                                .Select(p => p.ID) // Get Role IDs from prototype hierarchy
                                .ToHashSet();
            // Also include the main band prototype ID if it represents the leader/base role
            bandRoleIds.Add(leaderBandInfo.Prototype.ID);


            // Get the roles the target member has that are part of this band
            var memberRolesInBand = (await _dbManager.GetRoleWhitelistsAsync(msg.PlayerUserId))
                .Where(rw => bandRoleIds.Contains(rw.RoleId))
                .ToList();

            if (!memberRolesInBand.Any())
                return; // Target player is not in this band (or has no roles from it)

            // Remove all band-related roles from the target member
            foreach (var roleWhitelist in memberRolesInBand)
            {
                await _dbManager.RemoveRoleWhitelistAsync(msg.PlayerUserId, roleWhitelist.RoleId);
            }

            // Update UI state after modification
            UpdateUiState((uid, component), actor);
        }

        // --- Methods from SharedBandsSystem ---

        private void OnInit(EntityUid uid, BandsComponent component, ComponentInit args)
        {
            EnsureComp<StatusIconComponent>(uid);

            if (component is { AltBand: not null, CanChange: true })
                _actions.AddAction(uid, ref component.ActionChangeEntity, component.ActionChange, uid);

            _actions.AddAction(uid, ref component.ActionEntity, component.Action, uid);
        }

        private void OnRemove(EntityUid uid, BandsComponent component, ComponentRemove args)
        {
            _actions.RemoveAction(uid, component.ActionEntity);
            if (component.ActionChangeEntity != null)
                _actions.RemoveAction(uid, component.ActionChangeEntity);
        }

        private void OnToggle(EntityUid uid, BandsComponent component, ToggleBandsEvent args)
        {
            if (args.Handled || !_mobState.IsAlive(uid)) // Check Handled flag
                return;

            component.Enabled = !component.Enabled;
            Dirty(uid, component); // Ensure component state is synced

            args.Handled = true;
        }

        private void OnChange(Entity<BandsComponent> entity, ref ChangeBandEvent args)
        {
             if (args.Handled) // Check Handled flag
                return;

            var comp = entity.Comp;
            if (comp.AltBand == null || !comp.CanChange)
                return;

            (comp.BandStatusIcon, comp.AltBand) = (comp.AltBand, comp.BandStatusIcon);
            Dirty(entity); // Ensure component state is synced
            args.Handled = true;
        }

        // --- Helper Methods (Refined based on new info) ---

        /// <summary>
        /// Gets the band prototype and potentially related DB info for a player based on their primary role.
        /// </summary>
        private async Task<ServerBandInfo?> GetPlayerBandInfoAsync(NetUserId userId)
        {
            // 1. Get player's primary role using JobWhitelistManager
            var primaryRole = await _jobWhitelistManager.GetPrimaryJobAsync(userId); // Assuming such a method exists
            if (primaryRole == null)
                return null; // Player has no primary role assigned

            // 2. Find the STBandPrototype whose ID matches the primary role ID
            // This assumes the STBandPrototype ID *is* the RoleId. Adjust if mapping is different.
            if (!_prototypeManager.TryIndex<STBandPrototype>(primaryRole.RoleId, out var bandProto))
            {
                // It's possible not every role corresponds to a band leader role
                // Check if the player belongs to *any* band role defined in *any* band prototype hierarchy
                foreach (var proto in _prototypeManager.EnumeratePrototypes<STBandPrototype>())
                {
                    var playerRoles = await _dbManager.GetRoleWhitelistsAsync(userId);
                    var bandRoleIds = proto.Hierarchy.Values.Select(p => p.ID).ToHashSet();
                    bandRoleIds.Add(proto.ID); // Include base role

                    if (playerRoles.Any(r => bandRoleIds.Contains(r.RoleId)))
                    {
                        // Player is part of this band, find the corresponding DB entry if needed
                        var dbBand = await _dbManager.GetStalkerBandByProtoIdAsync(proto.ID); // Need this DB method
                        return new ServerBandInfo(proto, dbBand);
                    }
                }
                return null; // Player is not part of any known band
            }


            // 3. Player's primary role IS a band leader role. Find the corresponding DB entry if needed.
            var bandDbInfo = await _dbManager.GetStalkerBandByProtoIdAsync(bandProto.ID); // Need this DB method

            return new ServerBandInfo(bandProto, bandDbInfo);
        }


        /// <summary>
        /// Gets members of a band defined by the given prototype, returning BandMemberInfo objects.
        /// </summary>
        private async Task<List<BandMemberInfo>> GetBandMembersAsync(STBandPrototype bandProto) // Return List<BandMemberInfo>
        {
            var members = new List<BandMemberInfo>(); // Create List<BandMemberInfo>
            var bandRoleIds = bandProto.Hierarchy.Values.Select(p => p.ID).ToHashSet();
            bandRoleIds.Add(bandProto.ID); // Include the base/leader role

            // Find all players whitelisted for any role in this band's hierarchy
            // This likely requires a DB method like GetPlayersWithAnyRoleWhitelistAsync(roleIds)
            var memberRecords = await _dbManager.GetPlayersWithAnyRoleWhitelistAsync(bandRoleIds); // Need this DB method

            foreach (var record in memberRecords)
            {
                // Determine the primary role *within the band context* if needed, or just use overall primary
                var primaryRole = await _jobWhitelistManager.GetPrimaryJobAsync(record.UserId);
                // Create BandMemberInfo directly
                members.Add(new BandMemberInfo(record.UserId, record.LastSeenUserName, primaryRole?.RoleId ?? "Unknown"));
            }

            return members;
        }

        /// <summary>
        /// Checks if a player can manage their band (delegated to DB Manager).
        /// </summary>
        private async Task<bool> CanPlayerManageBandAsync(NetUserId userId)
        {
            // Use the existing DB method as indicated by the original code structure
            return await _dbManager.CanPlayerManageBandAsync(userId);
        }
    }

    // --- Helper Structs/Classes (Ensure these match definitions elsewhere) ---

    // BandMemberInfo is defined in Content.Shared._Stalker.Bands.SharedBandsSystem.cs

    // Assuming StalkerBand is defined in Content.Server.Database.Model
    // public sealed class StalkerBand { ... }

}
