---
name: internals-visible-to-tests
description: Add InternalsVisibleTo entries for matching test assemblies and change hard-to-reach private members to internal only when direct testing is justified.
user-invocable: true
disable-model-invocation: false
---

# InternalsVisibleTo for Tests

## Goal

Expose the smallest possible internal surface to matching test projects when a behavior is too hard to verify indirectly through the public API alone.

## When to use

- A private helper contains important logic that is expensive, flaky, or awkward to test only through higher-level behavior
- A matching test project already exists
- The matching test project name ends with one of:
  - `.Test`
  - `.Tests`
  - `.IntegrationTest`
  - `.IntegrationTests`
- Indirect testing would be much less clear than testing the logic directly

Prefer indirect testing first. Use this skill only when relaxing visibility materially improves test quality.

## Discovery

1. Find the source library `.csproj`
2. Find matching test projects with the same base name and one of the supported suffixes
3. Inspect evaluated project properties before composing `InternalsVisibleTo`

Use:

```powershell
dotnet msbuild ".\src\MyLibrary\MyLibrary.csproj" -nologo -getProperty:ProjectName -getProperty:MSBuildProjectName -getProperty:AssemblyName
```

Important:

- `$(ProjectName)` may be empty
- Prefer `$(MSBuildProjectName)` for pattern-based entries
- If the test project sets a custom `AssemblyName`, use that exact assembly name instead of composing it from the source project name

## Supported suffix rules

Check for matching test projects in this order:

1. `.Test`
2. `.Tests`
3. `.IntegrationTest`
4. `.IntegrationTests`

If multiple matching test projects exist, add one `InternalsVisibleTo` entry per friend assembly.

## Preferred csproj edits

If the matching test project follows the default `<library>.Test` pattern:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="$(MSBuildProjectName).Test" />
</ItemGroup>
```

If the actual test project uses another supported suffix, use that exact suffix:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="$(MSBuildProjectName).Tests" />
  <InternalsVisibleTo Include="$(MSBuildProjectName).IntegrationTests" />
</ItemGroup>
```

If the friend assembly name is custom, use the explicit assembly name:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Company.Product.Execution.IntegrationTests" />
</ItemGroup>
```

## Code visibility rule

Change only the minimum member visibility needed for testing:

```csharp
private void CalculateState()
```

to:

```csharp
internal void CalculateState()
```

Do not widen members to `public` for testing.

## Workflow

1. Search for the source library and matching test projects
2. Confirm the correct friend assembly names
3. Edit the source `.csproj` and add `InternalsVisibleTo`
4. Change only the required `private` members to `internal`
5. If the accessibility change violates member-ordering or code-style rules, move the member to the appropriate location in the type
6. Add or update tests in the matching test project
7. Run build and tests
8. If coverage is the reason for the change, rerun coverage and verify the targeted logic is now covered

## Guardrails

- Do not use `$(ProjectName)` if it evaluates to empty
- Do not add `InternalsVisibleTo` to unrelated projects
- Do not expose more members than necessary
- Prefer changing a method before widening an entire type
- After changing visibility, reorder or move the member if required by analyzer or style rules
- Keep the public API unchanged
- If the assembly is strong-named, include the required friend assembly public key when needed

## Response format

Return:

- source project updated
- matching test project(s) found
- `InternalsVisibleTo` entry or entries added
- members changed from `private` to `internal`
- tests added or updated
- validation commands and results
