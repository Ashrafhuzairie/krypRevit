# Windows/Revit 2024 validation — runtime checks NOT EXECUTED HERE

Use a disposable saved project copy and an approved provider account. Record the exact Revit build and any failure before wider deployment.

1. Close Revit and replace the previous DLL/PDB. Keep only one manifest with the preserved AddInId, pointing to the full DLL path. Start as a standard Windows user without an HTTP.sys reservation. Verify the AI Assistant panel shows AI Chat (speech bubble), Review & apply (checklist), MCP Status (plug). Check icons in large/small layouts and dark/light themes.
2. Open AI Chat, select each supported provider, enter an approved API key and valid model, and send a simple question. Verify Send/Ctrl+Enter, Stop, New chat, reopening/minimizing the window and provider/model changes. No Python or MCP client should be needed.
3. Test missing/invalid keys, invalid models, quota failures, no network and company proxy behavior. Confirm errors contain no API key. Save a key, restart, reload it under the same Windows user, remove it with Forget key, and verify no plaintext key file exists. The key storage/UI behavior needs actual Windows testing.
4. Select a wall. Ask to inspect it and propose changing Comments. Verify model facts and actual IDs, then cancel review and confirm nothing changed. Ask for a new proposal, approve it through the native dialog, verify the parameter changed, check status applied and undo the whole batch once. Repeat using the ribbon Review & apply button. A stopped request must never automatically apply edits.
5. Test integer, yes/no and double parameters with known unit conversions. Reject read-only, ElementId, malformed/nonfinite values and missing IDs. Verify different numeric specs use the correct internal units.
6. Test known moves, rotations and copies individually. Inspect resulting geometry, returned created IDs, dependent elements and Undo. Reject pinned/grouped/type/linked elements. Test basic-wall creation with valid type/level/endpoints/height and invalid inputs.
7. Force an approved multi-action batch to fail at a later action or at commit. Verify no partial changes persist and errors/warnings roll back the entire batch. Check worksharing ownership, design options and constraints.
8. Propose then edit/undo/redo the document; the plan must become stale. Switch documents and close/reopen the source document; plans must not apply to the wrong document. Verify ten-minute expiration and pending-plan overwrite protection. A new document context must not reuse the prior chat's model facts.
9. Keep Revit busy beyond a queued request timeout, then return to idle; an expired queued request must not execute later. Close chat and Revit with work queued; check no crashes, orphaned modal dialogs or unexpected edits.
10. Run another local process on port 8765 before Revit: the optional external MCP listener should report unavailable while AI Chat remains usable through direct Revit dispatch. Stop the conflicting process and restart Revit for external MCP.

Live provider availability, Windows UI/key protection and Revit model behavior cannot be inferred from the Linux mock/protocol results. Automatic SEM opening detection was not implemented in this source release.

## Native modelling acceptance checks for 0.4.0 — not run here

1. On a saved disposable project with known basic wall/floor types and level, issue the metric room prompt in MODELLING-EXAMPLES.txt. Inspect the proposed centerline rectangle and elevations before applying. After commit verify exactly four walls and one floor, native dimensions/type thickness and the reported created IDs. Show in 3D should select/zoom to the physical results in an existing isometric view. One Undo must remove the entire batch.
2. Create a new named level at 3,000 mm. Verify elevation with native Revit properties, then inspect its actual ID and use it in a separate wall/floor proposal. No plan view should be claimed as automatically created.
3. Place a known loaded unhosted one-level furniture/generic-model family at stated internal XYZ coordinates and a known level. Verify position and offset in Revit, and Undo. Reject structural, hosted, work-plane, in-place, adaptive and two-level symbols.
4. Change a selected eligible instance to a type listed in valid_types, then reshape a straight basic wall while preserving base Z. Verify Revit's actual resulting IDs/type/geometry; a replacement instance must be reported under its new ID.
5. Move/rotate/copy physical elements with known 3D displacements/axes. Edit a numeric instance parameter after matching its spec and converting units. Compare reported parameter values with native properties, including a no-op set to the existing value.
6. Try an invalid/self-crossing floor and a too-short wall; reject before proposal. Force a later approved action to fail (constraints/ownership) and confirm no partial model changes persist and the real Revit failure is shown.
7. After a successful commit, close/move a view, undo or delete the created results, then query status and Show in 3D. The status is a commit record; no surviving-result claim should be made when elements no longer exist.

Record actual results/screenshots. These native acceptance checks are the remaining prerequisite to claiming end-to-end modelling readiness.

## Numeric IDs, deletion and HTTP status — not run in Revit here

- Enter “697693 this is the wall please delete it” using an actual current-document wall ID. Verify direct lookup returns the numeric ID, canonical UniqueId and correct document/category, with no text search or request to copy a UniqueId from Properties. Repeat with a missing ID, a different document, and a linked-model ID; no substitute target may be used.
- For deletion, inspect the complete target/hosted/annotation/sketch dependency list. Cancel and verify the preview transaction restored everything with no committed deletion. Approve a fresh plan and verify exactly that set disappears. One Undo must restore the batch. Test protected/pinned/grouped dependencies and the 50-element cap. Source execution compares the actual removal set before commit; this must be checked natively.
- With no proposal, Review must be disabled; after a deletion-only result Show in 3D must be disabled. Change the document or expire a plan and verify stale plans cannot execute.
- With Revit running, visit http://127.0.0.1:8765/ (HTTP, no HTTPS): basic bridge/version JSON should appear. Other model routes must reject unmarked browser requests. Built-in chat must remain usable if the optional listener cannot bind (another process/instance). Check an external MCP client after updating any old explicit port override.

## Service openings / user rules — not run in Revit here

Follow OPENING-RULES.md. Test rectangular and circular openings in both walls and slabs, active and transformed linked services, exact nominal diameter matches, matching tolerance, the 200 mm cap and physical outer-diameter fit. Check cable tray 300x100 -> 400x150 and trunking 100x50 -> 150x100 mm, plus asymmetric margins and local-axis orientation. Test the circular 25->50, 50->80, 80->100, 100->150, 150->200 table in native units.

Verify actual profile voids for round walls (not markers), native Opening elements for other supported combinations, unchanged unrelated sketch loops/geometry, complete through passages, native dimensions and one Undo for the whole group. Cancel/error in sketch mode must close/cancel the edit scope and roll back the entire group. Test unsupported slanted/curved/angled/oval/edge-crossing cases, missing rules, scopes over limits, duplicate service IDs and overlapping penetrations. Confirm skipped candidates are disclosed in the review. Save changed rules, reload the window and verify pending opening plans become stale while existing cuts are not resized.
