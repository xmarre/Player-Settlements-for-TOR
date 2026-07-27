# Runtime acceptance tests

## Installation and load

1. Delete the existing `Modules/PlayerSettlement` and `Modules/PlayerSettlement_TOR` folders before installing the candidate.
2. Start Bannerlord 1.3.15 with The Old Realms 1.16 and load an existing save containing a player-built town or castle.
3. Confirm the settlement keeps its current owner. Repeat with a custom fortification that was legitimately conquered or transferred.
4. Confirm the log contains `Strategic neighbour integration` and reports at least one resulting neighbour for every reachable player-built fortification.

## Placement and save flow

5. Start settlement placement and move the cursor before pressing and releasing the left mouse button. Confirm the settlement commits at the release-position frame and normal map selection does not receive the click.
6. Enter gate placement. Confirm the phase remains usable with no gate-marker prefab present, accepts only a valid land position, and advances exactly once on left-button release.
7. When port placement is available, confirm the phase remains usable with no port-marker prefab present, rejects land, accepts a valid water position, and commits exactly once on left-button release.
8. Press Escape independently during settlement, gate, and port placement. Confirm each phase returns to the intended previous state without retaining stale frames or committing the placement.
9. Complete a settlement and save. Confirm the save file is written, the localised success notification appears, and no in-process campaign reload occurs.
10. Force or simulate a failed save and confirm the localised failure notification appears and a later placement save can start normally.
11. Suppress the save-completion callback in a diagnostic test, or end the campaign session while a placement save is pending. Confirm pending state is cleared. For the callback-suppression case, confirm the five-minute monotonic timeout unlocks later save attempts and records the timeout in `save_transition.log`.

## Village and strategic AI

12. Start, interrupt, complete, and save/reload a raid on a generated village. Confirm ownership, bound settlement, trade-bound settlement, and village party state remain valid.
13. Declare war and run campaign time for several native AI decision cycles.
14. Confirm hostile lords or armies can select, approach, begin, maintain, abandon, and later reselect the custom fortification under normal native strategy rules.
15. Build a new town and castle and repeat the strategic-target check without restarting the campaign.

## Performance and regression checks

16. Confirm there is no campaign-tick settlement scan. The siege repair should run only after save load and settlement creation.
17. Confirm the application-tick save timeout path performs only its constant-time inactive guard when no placement save is pending.
18. Verify ordinary native settlements, transferred custom settlements, caravan routing, village raids, settlement visuals, and normal campaign saving retain their previous behaviour.
