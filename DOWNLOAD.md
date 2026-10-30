# Download krypRevit 0.3.0 — built-in AI Chat restored

- [Compiled copy-only ZIP](releases/krypRevit-NoAdmin-v0.3.0.zip) — built-in chat, distinct command icons, no Python needed for chat.
- [Complete source ZIP](releases/krypRevit-source-v0.3.0.zip) — current C# project and test harnesses.

Open a ZIP on GitHub and choose **Download raw file**. Do not use Save page as. Adjacent SHA-256 files are provided for integrity checks.

Close Revit and replace the DLL/PDB in your existing per-user installation:

```text
C:\Users\md.ashraf\AppData\Roaming\Autodesk\Revit\Addins\2024\krypRevit
```

Keep your existing manifest if its Assembly path is correct; do not add a duplicate. Restart Revit and open **krypRevit > AI Assistant > AI Chat**. Select OpenAI, Claude or Gemini, enter a provider API key and an enabled model. Saving the key is optional and uses Windows-user encryption.

AI Chat has a speech-bubble icon, Review actions a checklist/checkmark, and MCP Status a plug/connection icon. MCP is optional; the built-in chat does not need a separate MCP client or Python. Model edits still require your review and native Revit confirmation.

The Release build passed with zero warnings/errors. Fourteen chat protocol checks plus twenty-three MCP/transport tests passed. The actual Windows GUI, provider accounts and Revit model behavior remain untested here; validate on a disposable model. Company restrictions on custom add-ins and provider access still apply.
