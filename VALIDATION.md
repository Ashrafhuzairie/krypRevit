# krypRevit 0.6.0 validation

Executed on Linux on 2026-10-30.

- Release .NET Framework 4.8 build with RevitAPI/RevitAPIUI 24.0: passed with zero warnings/errors. Metadata checks verify native creation/modification/deletion/rollback, opening/profile-sketch and solid-intersection API references plus the chat entry point and four embedded icons. Repeat DLL builds have identical SHA-256.
- Chat harness: **20 checks passed**, including the reported numeric wall-ID deletion conversation (direct lookup then proposal, never name search or automatic apply), OpenAI model-access advice and opening-rule/planner tool flow. Production protocol/provider code is exercised using canned provider responses and fake model dispatch; these are not live LLM or Revit model tests.
- Modelling/input harness: **26 checks passed**, including full 64-bit ID preservation above floating-point precision, invalid IDs, deletion-only batches, dependency-set equality/caps and existing geometry/input validation. It uses production Revit-independent code; native Document.GetElement/Delete/RollBack are not executed.
- Opening-rule harness: **19 checks passed**, covering cable tray/trunking clearances, asymmetric centres, all five requested circle mappings, the 200 mm cap, physical-size fit, exact/next-larger matching, tolerance, trunking classification, disabled duct defaults and invalid/nonfinite rules.
- MCP/HTTP suite: **31 tests passed**, including root browser status, numeric ID forwarding, ambiguous-reference rejection, opening-rule/planner routing, payload transport, method/Host/origin checks and native production TCP transport hosted under .NET 8 with stubbed endpoints.

Total: **96 automated protocol/input/rule/transport checks passed**. No native geometry or Windows runtime behavior is certified by those results.

NOT RUN: Windows UI/DPAPI/rule persistence, actual provider accounts, native element lookup/deletion previews and restoration, protected dependency handling, native geometry/units/constraints/worksharing, opening detection for active/linked services, all four opening host/shape combinations, profile winding, actual centreline verification, transaction-group rollback and Undo. Revit 2024/Windows is unavailable here. Complete REVIT-VALIDATION.md and OPENING-RULES.md on a saved disposable model.

Original Microsoft SDK SHA-512 matched release metadata. NuGet signature verification exited 0 without disabling checks, with NU3018/NU3028 warnings because certificate-revocation endpoints were unavailable; revocation status is unconfirmed. Build reference assemblies are not shipped. No user API keys or local key/rule files are included in the package.
