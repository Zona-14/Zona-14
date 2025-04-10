using System;
using Content.Server._Stalker.WarZone;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared._Stalker.WarZone;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Console;
using Content.Server.Administration;

namespace Content.Server._Stalker.WarZone.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class WarZoneAdminCommand : IConsoleCommand
{
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly IServerDbManager _dbManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    public string Command => "warzoneadmin";
    public string Description => "Admin command to modify warzone points and ownership.";
    public string Help => "Usage:\n" +
                          "warzoneadmin setpoints band <dbid> <points>\n" +
                          "warzoneadmin setpoints faction <dbid> <points>\n" +
                          "warzoneadmin setowner <zoneProtoId> band <dbid>\n" +
                          "warzoneadmin setowner <zoneProtoId> faction <dbid>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 4)
        {
            shell.WriteLine(Help);
            return;
        }

        var warZoneSystem = _entityManager.System<WarZoneSystem>();

        if (args[0] == "setpoints")
        {
            if (args.Length != 4)
            {
                shell.WriteLine(Help);
                return;
            }

            var target = args[1];
            if (!int.TryParse(args[2], out var dbid))
            {
                shell.WriteLine("Invalid dbid.");
                return;
            }

            if (!int.TryParse(args[3], out var points))
            {
                shell.WriteLine("Invalid points value.");
                return;
            }

            if (target == "band")
            {
                warZoneSystem.SetBandPoints(dbid, points);
                foreach (var bandProto in _prototypeManager.EnumeratePrototypes<Content.Shared._Stalker.Bands.STBandPrototype>())
                {
                    if (bandProto.DatabaseId == dbid)
                    {
                        await _dbManager.SetStalkerBandAsync(bandProto.ID, points);
                        shell.WriteLine($"Set band {dbid} points to {points}");
                        return;
                    }
                }
                shell.WriteLine($"Band with dbid {dbid} not found.");
            }
            else if (target == "faction")
            {
                warZoneSystem.SetFactionPoints(dbid, points);
                foreach (var factionProto in _prototypeManager.EnumeratePrototypes<Content.Shared.NPC.Prototypes.NpcFactionPrototype>())
                {
                    if (factionProto.DatabaseId == dbid)
                    {
                        await _dbManager.SetStalkerFactionAsync(factionProto.ID, points);
                        shell.WriteLine($"Set faction {dbid} points to {points}");
                        return;
                    }
                }
                shell.WriteLine($"Faction with dbid {dbid} not found.");
            }
            else
            {
                shell.WriteLine(Help);
            }

            return;
        }

        if (args[0] == "setpoints")
        {
            if (args.Length != 3)
            {
                shell.WriteLine(Help);
                return;
            }

            var target = args[1];
            if (!int.TryParse(args[2], out var dbid))
            {
                shell.WriteLine("Invalid dbid.");
                return;
            }

            if (!int.TryParse(args[3], out var points))
            {
                shell.WriteLine("Invalid points value.");
                return;
            }

            if (target == "band")
            {
                warZoneSystem.SetBandPoints(dbid, points);
                foreach (var bandProto in _prototypeManager.EnumeratePrototypes<Content.Shared._Stalker.Bands.STBandPrototype>())
                {
                    if (bandProto.DatabaseId == dbid)
                    {
                        await _dbManager.SetStalkerBandAsync(bandProto.ID, points);
                        shell.WriteLine($"Set band {dbid} points to {points}");
                        return;
                    }
                }
                shell.WriteLine($"Band with dbid {dbid} not found.");
            }
            else if (target == "faction")
            {
                warZoneSystem.SetFactionPoints(dbid, points);
                foreach (var factionProto in _prototypeManager.EnumeratePrototypes<Content.Shared.NPC.Prototypes.NpcFactionPrototype>())
                {
                    if (factionProto.DatabaseId == dbid)
                    {
                        await _dbManager.SetStalkerFactionAsync(factionProto.ID, points);
                        shell.WriteLine($"Set faction {dbid} points to {points}");
                        return;
                    }
                }
                shell.WriteLine($"Faction with dbid {dbid} not found.");
            }
            else
            {
                shell.WriteLine(Help);
            }

            return;
        }

        if (args[0] == "setowner")
        {
            if (args.Length != 4)
            {
                shell.WriteLine(Help);
                return;
            }

            var zoneProtoId = args[1];
            var target = args[2];

            if (!int.TryParse(args[3], out var dbid))
            {
                shell.WriteLine("Invalid dbid.");
                return;
            }

            WarZoneComponent? foundComp = null;
            foreach (var (_, wzComp) in warZoneSystem.GetAllWarZones())
            {
                if (wzComp.ZoneProto == zoneProtoId)
                {
                    foundComp = wzComp;
                    break;
                }
            }

            if (foundComp == null)
            {
                shell.WriteLine($"Warzone with proto id {zoneProtoId} not found.");
                return;
            }

            ProtoId<Content.Shared._Stalker.Bands.STBandPrototype>? bandProtoId = null;
            ProtoId<Content.Shared.NPC.Prototypes.NpcFactionPrototype>? factionProtoId = null;

            if (target == "band")
            {
                foundComp.DefendingBandId = dbid;
                foundComp.DefendingFactionId = null;

                foreach (var bandProto in _prototypeManager.EnumeratePrototypes<Content.Shared._Stalker.Bands.STBandPrototype>())
                {
                    if (bandProto.DatabaseId == dbid)
                    {
                        bandProtoId = bandProto.ID;
                        break;
                    }
                }

                if (bandProtoId == null)
                {
                    shell.WriteLine($"Band with dbid {dbid} not found.");
                    return;
                }
            }
            else if (target == "faction")
            {
                foundComp.DefendingBandId = null;
                foundComp.DefendingFactionId = dbid;

                foreach (var factionProto in _prototypeManager.EnumeratePrototypes<Content.Shared.NPC.Prototypes.NpcFactionPrototype>())
                {
                    if (factionProto.DatabaseId == dbid)
                    {
                        factionProtoId = factionProto.ID;
                        break;
                    }
                }

                if (factionProtoId == null)
                {
                    shell.WriteLine($"Faction with dbid {dbid} not found.");
                    return;
                }
            }
            else
            {
                shell.WriteLine(Help);
                return;
            }

            await _dbManager.SetStalkerZoneOwnershipAsync(zoneProtoId, bandProtoId, factionProtoId);
            shell.WriteLine($"Set owner of warzone {zoneProtoId} to {target} {dbid}");
            return;
        }

        shell.WriteLine(Help);
    }
}