# krypRevit for Revit 2024

The compiled copy-only package is [releases/krypRevit-NoAdmin-v0.2.2.zip](releases/krypRevit-NoAdmin-v0.2.2.zip). Open the file on GitHub and choose **Download raw file** to download the actual ZIP. Do not use Save page as. The adjacent SHA-256 file can be used to check integrity.

Close Revit, extract the ZIP, and follow START-HERE.txt. Copy the contents of Revit-Addins-2024 into:

```text
C:\Users\md.ashraf\AppData\Roaming\Autodesk\Revit\Addins\2024
```

The DLL already includes green crystal icons for MCP Status and Review actions. No compiler, PowerShell installer or administrator-created HTTP URL reservation is needed. Company permission to load custom unsigned add-ins is still required.

This is an external MCP bridge, not a built-in chat window. Optional AI-client setup is described in AI-client/CONNECT.txt inside the package.

Build: Release, .NET Framework 4.8, Revit API 24.0, zero compiler warnings/errors. Repeated DLL build hashes matched and the embedded icon hash was checked. The 23 MCP/transport tests passed for unchanged functional components; icon rendering, Revit model actions and Windows deployment remain untested. Test on a disposable model first.
