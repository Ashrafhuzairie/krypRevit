# krypRevit 0.4.0 validation

Executed on Linux on 2026-10-30.

- .NET Framework 4.8 Release build against RevitAPI/RevitAPIUI 24.0: passed, zero warnings/errors.
- Chat harness: **16 checks passed**. Includes provider-native request/response protocols, rejected unregistered apply tools, schema/backend operation parity, cancellation, failure recovery and a canned-provider metric prompt flow that stages four walls and one floor after inspection/unit conversion. Production chat/provider code is linked with a test JSON adapter and fake HTTP/Revit dispatch. This is not an actual language-model response or Revit model test.
- Modelling harness: **17 checks passed**, using the production Revit-independent DTO and geometry/input rules. Covers all ten action shapes, both polygon windings, closing-vertex normalization, self-intersections/touches, horizontal boundaries, short edges, degenerate geometry, nonfinite numbers, required IDs, zero level elevations and invalid operations. No Revit API execution is simulated or claimed.
- MCP/transport suite: **26 tests passed**. Includes a real MCP stdio session and the production TCP/HTTP listener under .NET 8 with substituted endpoints. New search/type-scope and unit-conversion routes were checked for transport/payload handling; the unit endpoint implementation uses real Revit UnitUtils in production and was not run in these tests.
- Release metadata checks: built-in chat entry point, framework/API versions, four embedded icon hashes and native modelling API references. Repeat build: identical DLL SHA-256.
- Original SDK SHA-512 matched Microsoft release metadata. NuGet signature verification exited 0 without disabling verification; certificate-revocation endpoints were unavailable (NU3018/NU3028), leaving revocation status unconfirmed. Build references are not shipped.

Total: **59 automated protocol/transport/input checks passed**. They do not certify actual Revit geometry, constraints or user-interface behavior.

NOT EXECUTED: Windows GUI/DPAPI, actual model-provider accounts, Revit model inspection/unit conversion/creation/modification, transaction warning rollback, Show in 3D, worksharing and Undo. Revit 2024/Windows is unavailable here. The package therefore requires the local acceptance steps in REVIT-VALIDATION.md on a disposable project before production use.

Important corrected behavior: native commit results are distinguished from notification/read-back problems. Creation/modification results are read from Revit after a successful commit. A later Undo or edit can invalidate that historical snapshot; Show in 3D resolves surviving actual IDs again.
