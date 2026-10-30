# Download krypRevit 0.4.0 — prompt-driven modelling

- [Compiled copy-only ZIP](releases/krypRevit-NoAdmin-v0.4.0.zip)
- [Complete source ZIP](releases/krypRevit-source-v0.4.0.zip)

Open a ZIP on GitHub and choose **Download raw file**. Adjacent SHA-256 files verify integrity.

Close Revit and replace the DLL/PDB in your existing folder:

```text
C:\Users\md.ashraf\AppData\Roaming\Autodesk\Revit\Addins\2024\krypRevit
```

Keep your current manifest if its full DLL path is correct. Do not add another manifest. Restart Revit > krypRevit > AI Chat, select a provider and enter its API key. Python/external MCP are optional for the built-in chat.

This build stages native model actions for walls, floors, levels, loaded unhosted one-level families, instance parameters, 3D moves/rotations/copies, valid type changes and straight-wall reshaping. It inspects real geometry/types and uses Revit unit conversion. Click **Review & apply** and confirm to execute the batch; **Show in 3D** selects/zooms to physical results in an existing isometric view.

[Example prompts](MODELLING-EXAMPLES.txt) and [native acceptance steps](REVIT-VALIDATION.md) explain the supported scope and validation. Roofs/stairs/general MEP, hosted/adaptive/structural family creation, arbitrary code, deletion and type-definition creation are not implemented.

Build: zero warnings/errors; repeat DLL hashes matched. **59 protocol/input/transport checks passed**. Actual Revit geometry, unit conversion, constraints, Windows UI/provider accounts and Undo remain untested here. Validate in a saved disposable project before working models.
