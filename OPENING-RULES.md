# krypRevit 0.6.0 — service penetrations and size rules

Open **AI Chat > Opening rules**. Values are in millimetres and persist for your Windows user under `%LOCALAPPDATA%\krypRevit\OpeningRules.json`. Rules are independent of provider API keys. Changing/saving rules invalidates a pending opening proposal; existing cuts are not resized.

## Default rectangular rules

| Service | Left | Right | Top | Bottom |
| --- | ---: | ---: | ---: | ---: |
| Cable tray | 50 | 50 | 25 | 25 |
| Trunking | 25 | 25 | 25 | 25 |

Trunking is a cable tray whose family/type name contains **trunking**, case-insensitively. The marker is editable. Each clearance can be changed independently; asymmetric sides shift the opening centre accordingly. A rectangular duct row is provided but disabled until you explicitly configure/enable it.

- Cable tray 300 x 100 mm becomes a 400 x 150 mm opening.
- Trunking 100 x 50 mm becomes a 150 x 100 mm opening.

X/Y are the opening's host-local directions: wall X follows its baseline and Y is up; slab X/Y follow the project's internal plan axes. These are not necessarily the current view's screen axes. Rolled rectangular profiles are projected into these axes before adding clearances.

## Default circular table

| Nominal service diameter | Opening diameter |
| ---: | ---: |
| 25 mm | 50 mm |
| 50 mm | 80 mm |
| 80 mm | 100 mm |
| 100 mm | 150 mm |
| 150 mm | 200 mm |

The opening diameter is capped at **200 mm**. Default matching requires a table entry within **0.5 mm**; unmatched sizes stop for configuration. You can explicitly select **Next larger service row** (for example 75 mm then uses the 80 -> 100 row). The tolerance is editable; overlapping matching ranges are rejected. The mapped opening must also fit the available physical/outer diameter. No output above 200 mm is accepted. Profiles do not automatically include separate insulation, linings or covers; account for required additional clearance in your rules.

## Plan and apply

1. On a disposable project, select straight MEP services in Revit. Optionally select the intended wall/slab hosts too.
2. Ask: “Read my opening rules and plan openings for the selected services through the walls and slabs. Report skipped services.”
3. Alternatively provide numeric/UniqueId service IDs and host IDs. For services in a loaded Revit link, provide the link instance ID plus optional service IDs from that linked document. Hosts always belong to the editable active document. The ordinary get_element_by_id tool addresses the active document; linked service IDs are resolved by the opening planner using link_id.
4. Review each planned size, host/service identity, source link, local axes and rule. No cut is committed at this stage.
5. Click **Review & apply**, inspect the native confirmation and approve. The entire opening batch is one Undo step. A failed cut/verification rolls back the group.
6. Click **Show in 3D** to inspect affected hosts. Opening results include the cut method and IDs, with a native centreline-passage check before keeping the group.

Example prompt for linked services: “Using loaded link [actual link instance ID], plan service openings for service IDs [actual linked IDs] through host wall/slab IDs [actual active-document IDs], with shape auto and my saved rules.”

## Actual cutting methods

- Rectangular wall: native wall Opening element.
- Rectangular/circular slab: native profile Opening element.
- Circular wall: a closed circular inner loop in the wall's editable profile using SketchEditScope. This is an actual profile void, not an Opening-family marker. It is reported as a wall-profile cut with its sketch-curve IDs; edit/remove it through the wall profile, not as a standalone Opening element.

The wall-profile method requires a straight vertical basic wall that supports profile-sketch editing. It preserves existing profile curves and cancels the edit scope on failure. Openings use a TransactionGroup so intermediate operations can be rolled back and successful operations become one Undo record. A centreline still blocked by host material, or loss of all host material, causes rejection.

## Supported scope and limits

- Straight cable trays/trunking, pipes, round/rectangular ducts and conduits.
- Services perpendicular to straight vertical basic walls or horizontal planar slab faces.
- Active-document services or services in one explicitly supplied loaded Revit link.
- At most 100 services, 1,000 hosts and 10 cuts per proposal. Narrow the IDs/selection for larger models. Bounds are cached before detailed intersection checks.
- Skipped/unmatched/unsupported candidates are reported in the plan; they are not silently treated as completed.
- Angled/sloped/curved services, slanted/curved walls, nonhorizontal slab faces, oval services, supports/fittings/custom-family services and merged multi-service openings are not implemented. Services must extend beyond both host faces. Profiles reaching host edges/existing voids are rejected conservatively.
- No structural design/rebar coordination or automatic insulation allowance is performed. Existing openings are detected through the absence of intersecting host material; nearby/overlapping planned cuts may be rejected rather than merged.

## Validation status

Pure rule tests cover all requested clearances/table rows, asymmetric sizing, matching policies, type-name classification and cap enforcement. The C# source compiles against the real Revit 2024 API, including native opening/profile/solid-intersection APIs. **The native detection/cuts, links, sketch winding, rollback, editable rule dialog and centreline verification have not been executed in Revit here.** Test all four host/shape combinations in a saved disposable project and verify dimensions, side clearances, actual voids, unrelated geometry and Undo before working models.
