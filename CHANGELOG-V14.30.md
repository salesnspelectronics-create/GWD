# V14.30 — Offline Repository Compile Fix

- Fixed CS1009 `Unrecognized escape sequence` in `WizardForm.cs`.
- Root cause: the FolderBrowserDialog description contained `D:\Developer-Packages` in a regular C# string literal, so `\D` was parsed as an invalid escape sequence.
- Changed the description to a verbatim C# string literal (`@"..."`).
- Preserves all V14.29 Offline / Local Dependency Repository functionality.
