# Download krypRevit 0.6.0 — openings, numeric IDs and built-in chat

- [Compiled copy-only ZIP](releases/krypRevit-NoAdmin-v0.6.0.zip)
- [Complete source ZIP](releases/krypRevit-source-v0.6.0.zip)

Open a ZIP on GitHub and choose **Download raw file**. Close Revit and replace the DLL/PDB in your existing per-user krypRevit folder. Keep the existing manifest if its full DLL path is correct; do not add a duplicate. Restart and check the AI Chat window title shows 0.6.0.

**Opening rules** inside AI Chat configures the clearances and table. Cable tray defaults: 50 mm left/right and 25 mm top/bottom. Trunking: 25 mm all sides, identified as cable tray family/type containing trunking. Circular service->opening mm: 25->50, 50->80, 80->100, 100->150, 150->200, with a 200 mm cap. Exact matching is default; next-larger matching is an explicit setting.

Select services (and optionally hosts), then ask the assistant to read the rules and plan openings. Linked services may be addressed using link_id and service_ids. Review dimensions/skipped candidates and click **Review & apply**. Rectangular/circular cuts are implemented for supported walls/slabs; see [scope, methods and native tests](OPENING-RULES.md). Round wall cuts edit the profile sketch; other supported cases create native Opening elements. Angled/curved/merged cases are excluded.

Numeric IDs such as 697693 now resolve directly in the active project, without a text search or manual UniqueId lookup. Deletion requires a rolled-back full-dependency preview (max 50) and human confirmation, with exact actual-set checking before commit.

The optional external bridge now uses **HTTP http://127.0.0.1:8765/**, not HTTPS or the old browser-blocked port. The root returns basic service/version JSON. Built-in AI Chat uses direct Revit dispatch and needs neither this listener nor Python. An unavailable OpenAI model still requires choosing one enabled for the API project; no automatic fallback is used.

Release build: zero warnings/errors; repeat DLL hashes matched. **96 protocol/input/rule/transport checks passed. Native Revit cuts/deletion, sketch rollback, Windows UI, rule persistence and Undo have not been run here.** Test on a saved disposable project using REVIT-VALIDATION.md before working models.
