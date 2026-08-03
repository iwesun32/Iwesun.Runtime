---
name: build-test
description: "Build and test the Iwesun.Runtime.Web solution. Use when asked to build, test, or verify the HTML/SVG element model library."
---

# Build and Test Iwesun.Runtime.Web

## Build Commands

Always run from the repository root (`d:\Git Space\Runtime\modules\Web`):

```powershell
# Debug build
dotnet build Iwesun.Runtime.Web.slnx -c Debug

# Release build
dotnet build Iwesun.Runtime.Web.slnx -c Release
```

## Test Commands

```powershell
# Run all tests (150+ test cases)
dotnet test Iwesun.Runtime.Web.slnx -c Debug

# Run all tests in Release
dotnet test Iwesun.Runtime.Web.slnx -c Release

# Run specific test class
dotnet test Iwesun.Runtime.Web.slnx --filter "FullyQualifiedName~ClassName"

# Run with detailed output
dotnet test Iwesun.Runtime.Web.slnx -v n
```

## Important Rules

1. **Zero warnings policy**: `TreatWarningsAsErrors=true` means any warning fails the build.
2. **No external dependencies**: This library only depends on .NET base libraries and WebView2 core DLL (type reference only).
3. **No WinUI or DoubaoUIClone references**: This is a pure base library - no site-specific dependencies allowed.
4. **Deterministic builds**: Same inputs produce same outputs.

## Test Coverage Areas

Tests must cover:
- Element catalog and type resolution
- Property reflection and slot filling
- Event registration and bubbling
- Layout constraint validation
- Audit statistics and reporting
- Tree building and traversal

## Validation Checklist After Build

- [ ] 0 warnings, 0 errors in both Debug and Release
- [ ] All 150+ tests pass
- [ ] No WinUI, DoubaoUIClone, or site-specific namespace references
- [ ] No file I/O or network access in library code
