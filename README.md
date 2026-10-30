# krypRevit 0.3.0 — AI assistant inside Revit 2024

This version restores a **built-in AI Chat window**. The earlier 0.2.x rebuild contained only the external MCP bridge; it did not include chat. Version 0.3.0 adds native chat with OpenAI, Claude and Gemini, while retaining reviewed model editing and the optional external MCP connection.

## Use inside Revit

The **krypRevit > AI Assistant** panel has three different command icons:

- **AI Chat** — speech bubble. Opens the modeless assistant window inside Revit.
- **Review actions** — checklist/checkmark. Reviews and confirms staged model edits.
- **MCP Status** — connection/plug. Checks the optional external MCP listener.

Click AI Chat, select a provider, enter an API key and use a model enabled for that account. The model field is editable. Defaults are `gpt-4.1-mini`, `claude-sonnet-4-5` and `gemini-2.5-flash`; availability and billing depend on the provider account. A ChatGPT/Claude/Gemini web subscription is not itself an API key.

Type a question and click Send or press Ctrl+Enter. Stop cancels a request. New chat clears the conversation, but does not cancel a pending model-edit proposal. Review actions is available both inside the chat and on the ribbon.

**Python and an external MCP client are not required for the built-in chat.** The chat calls the selected provider over HTTPS and dispatches Revit tools directly through the add-in's ExternalEvent bridge. A failed optional localhost listener does not prevent the built-in chat from opening.

Saving a key is optional. **Save key** encrypts it for the current Windows user using DPAPI under `%LOCALAPPDATA%\krypRevit\Chat`; **Forget key** removes the saved key for the selected provider. Conversation history stays in memory and is discarded when the window closes. Old RevitSem key files are left untouched and are not migrated. Enter your provider key again in this version. Messages and requested model facts are sent to the selected provider; use company-approved provider access and models.

## Supported model actions

Inspect selected elements (or a bounded model list), parameters, walls/floors, levels and basic wall types. Propose text/integer/double instance-parameter edits, moves, rotations, copies and basic straight nonstructural walls. The assistant uses actual IDs from inspection. Coordinates/heights use Revit internal feet, angles radians, and double parameters the internal units of their particular spec.

**The AI cannot approve or apply edits.** A proposal stages 1–10 actions with at most one action per existing element. Click Review actions, inspect the native Revit confirmation, and approve or cancel. Approval uses one transaction and one Undo step; any commit warning/error rolls the batch back. Proposals expire after ten minutes or any document change and cannot silently overwrite a pending proposal. The dialog checks the originating document and revalidates inputs. Revit may adjust connected/dependent geometry, so validate on a disposable copy first.

Not implemented: arbitrary code execution, deletion, type edits, element-reference parameter changes, linked/pinned/grouped edits, general family/MEP placement, floors, or automatic SEM opening detection. The newer uploaded DLL's SEM opening-planner source was not provided; this release implements the listed general editing tools from the older source.

## Copy-only upgrade (no administrator installer)

1. Close Revit 2024.
2. Download `releases/krypRevit-NoAdmin-v0.3.0.zip` from GitHub using Download raw file, then extract it.
3. For an existing working krypRevit installation, replace the DLL and PDB in:

   ```text
   C:\Users\md.ashraf\AppData\Roaming\Autodesk\Revit\Addins\2024\krypRevit
   ```

   Keep the existing manifest if its Assembly path is correct. Do not add another manifest.
4. For a fresh installation, copy both entries inside the ZIP's Revit-Addins-2024 directory into `%APPDATA%\Autodesk\Revit\Addins\2024`: `krypRevit.addin` and the `krypRevit` folder.
5. Verify that `<Assembly>` in the installed manifest points to the full **DLL filename**, not just the 2024 folder. The packaged template uses the path above and the supplied Windows username `md.ashraf`. Adjust it if your path differs.
6. Keep only one manifest for this AddInId. Back up/move older RevitSem/AequAI/duplicate krypRevit manifests outside the Addins folders. If a duplicate is company-managed, ask IT to handle it.
7. Restart Revit. You should see AI Chat, Review actions and MCP Status with different symbols.

All icons are embedded in the DLL at small/large ribbon sizes; no image installation is needed. Revit supplies the .NET Framework 4.8, Windows Forms and WPF runtime dependencies. This package needs no MSI, Visual Studio, PowerShell installer or HTTP.sys URL reservation. Company restrictions on custom unsigned add-ins, provider access or local connections still require IT approval; the package does not override them.

## Build and tests

Windows source rebuild: Visual Studio 2022 .NET desktop build tools, the .NET Framework 4.8 targeting pack and Revit 2024 API DLLs. From a Developer PowerShell:

```powershell
msbuild krypRevit.Addin\krypRevit.Addin.csproj /t:Rebuild /p:Configuration=Release
# Or rebuild and copy for the current Windows user:
.\install.ps1 -BuildFromSource
```

A nonstandard Revit directory can be supplied using `/p:RevitInstallDir=...` or the install script parameter. No Autodesk API DLLs are bundled in the release.

Chat protocol checks (requires .NET 8 SDK; no real API calls):

```sh
dotnet run --project tests/ChatHarness/ChatHarness.csproj -c Release
```

This links the production conversation/provider code to mocked HTTP/model-tool dispatch, checking all three providers, native tool exchanges, schema normalization, cancellation, error recovery, no AI apply tool, context changes and bounded tool loops. The test-only JSON adapter is .NET 8; the production DLL uses the .NET Framework serializer.

Optional MCP/transport tests:

```sh
python -m venv .venv
.venv/bin/python -m pip install -r mcp_server/requirements.txt
KRYPREVIT_DOTNET=/path/to/dotnet .venv/bin/python -m unittest discover -s tests -v
```

On Windows use `.venv\Scripts\python` and set the SDK variable in your shell. Twelve C# transport tests are skipped unless KRYPREVIT_DOTNET is set; the other eleven exercise MCP/client behavior. The optional external MCP server/configuration remains in `mcp_server` and `codex-config.toml`, using 127.0.0.1:5060. It is independent of the built-in chat.

## Validation limits

The .NET Framework 4.8 Release build succeeded with zero warnings/errors. Four embedded icon hashes and the chat entry point/types were checked; a repeat build produced the same DLL hash. Fourteen chat checks and twenty-three MCP/transport tests passed. These are compilation/protocol tests: **Windows Forms layout, DPAPI persistence, actual provider accounts, Revit startup/rendering and model edits have not been executed on Windows/Revit here.** Complete `REVIT-VALIDATION.md` on a disposable model before using working documents. Build provenance, package-signature verification limits and checksums are in BUILD-INFO.json.
