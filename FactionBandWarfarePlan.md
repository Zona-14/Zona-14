# Faction & Band Warfare System - Design and Implementation

## Overview
This document outlines the design and current implementation status of the Faction and Band Warfare system for Space Station 14.

---

## Design Plan

### Core Concepts
- **War Zones:** Areas that can be captured, generate periodic rewards, and have requirements.
- **Factions:** NPC groups that can own zones and accumulate points.
- **Bands:** Player groups aligned with factions, can own zones and accumulate points.
- **Ownership:** A zone is owned by either a Band or a Faction, tracked persistently.
- **Capture Mechanics:** Players contest zones, triggering a timer to capture.
- **Rewards:** Owners gain points periodically for held zones.

### Database Schema
- `stalker_bands`: Band info and points.
- `stalker_factions`: Faction info and points.
- `stalker_zone_ownerships`: Zone ownership state.

### Planned Features
- Capture logic with contestation and timers.
- Ownership persistence.
- Reward distribution.
- UI for status and management.
- Admin commands.

---

## Implementation Summary

### Prototypes and Components
- `STWarZonePrototype` defines zone properties, reward rates, and requirements.
- `STBandPrototype` defines player bands and their faction alignment.
- `WarZoneComponent` links entities to zone prototypes.
- `BandsComponent` tracks player band membership.

### War Zone System
- Implemented as `WarZoneSystem` in server code.
- Subscribes to player collision events to detect presence in zones.
- Tracks which Bands and Factions are present in each zone.
- Handles contestation: resets timer if multiple groups are present.
- Starts capture timer when a single group is uncontested.
- Transfers ownership after timer completes.
- Announces captures via server logs.

### Persistence
- Integrates with `IServerDbManager`.
- Calls `SetStalkerZoneOwnershipAsync()` to update ownership in the database.
- Loads ownership state on startup (future work).

### Reward Distribution
- Periodically checks if reward interval elapsed.
- Adds `RewardPointsPerPeriod` to the owning Band or Faction.
- Updates points in the database via `SetStalkerBandAsync()` or `SetStalkerFactionAsync()`.

### Notes
- Capture time currently uses `RewardPeriod` as a placeholder.
- UI integration and admin commands are future work.
- System is modular and ready for extension.

---

## Next Steps
- Add UI feedback for capture progress and ownership.
- Implement admin commands for zone control.
- Expand reward usage and gameplay integration.
- Refine requirements and consequences logic.