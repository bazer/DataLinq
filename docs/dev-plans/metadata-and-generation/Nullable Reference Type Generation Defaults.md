> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Nullable Reference Type Generation Defaults

**Status:** Draft implementation plan.
**Goal:** Make nullable reference type generation the default for DataLinq-generated C# model and database files, make generated files self-contained with nullable directives, allow an explicit opt-out, and make the source generator follow the nullable context declared in model files.

## Executive Position

This is the right default to change. DataLinq already knows database nullability, already emits `?` for nullable value types, and already has a `UseNullableReferenceTypes` setting. Defaulting reference-type nullability to off is the wrong tradeoff in modern C#. It makes generated models less truthful, especially for nullable `string` and `byte[]` columns, and it forces users to remember an extra DataLinq-specific switch for behavior that should be normal in 2026.

The sharp edge is compatibility. Changing the default means projects that omitted `UseNullableReferenceTypes` will start seeing nullable reference annotations in regenerated model files. That is a source diff and can expose real nullability warnings in user partials or consumers. Still, it is the better default. The compatibility escape hatch should be explicit:

```json
"UseNullableReferenceTypes": false
```

The more important technical point: do not try to infer project nullability from the CLI. The CLI does not reliably know which `.csproj` will compile the generated files. The robust answer is to make generated files declare their own nullable context. If DataLinq emits nullable reference annotations, the files should include `#nullable enable`. That works whether the consuming project has nullable enabled or disabled.

## Current Code Audit

### Configuration Default

`src/DataLinq/Config/ConfigFile.cs` exposes:

```csharp
public bool? UseNullableReferenceTypes { get; set; }
```

`src/DataLinq/Config/DataLinqConfig.cs` lowers that nullable config value into a non-null runtime setting:

```csharp
UseNullableReferenceTypes = database.UseNullableReferenceTypes ?? false;
```

User config merge preserves explicit overrides:

```csharp
if (database.UseNullableReferenceTypes != null)
    UseNullableReferenceTypes = database.UseNullableReferenceTypes.Value;
```

So the current behavior is:

- omitted setting -> `false`
- `true` -> emit nullable reference annotations in CLI-generated model files
- `false` -> do not emit nullable reference annotations from the CLI model file factory

`docs/Configuration files.md` documents the current default as `false`.

### CLI Model File Generation

`src/DataLinq.Tools/ModelGenerator.cs` passes the database config setting into `ModelFileFactoryOptions`:

```csharp
UseNullableReferenceTypes = db.UseNullableReferenceTypes
```

`src/DataLinq.SharedCore/Factories/Models/ModelFileFactory.cs` uses the option in two main places:

- nullable reference column properties
- nullable foreign-key relation properties

For scalar columns, `GetPropertyNullable(ColumnDefinition column)` currently adds `?` for nullable or auto-incrementing value types always, but it only adds `?` for known reference types when `UseNullableReferenceTypes` is true:

```csharp
bool isReferenceType = (csTypeName == "string" || csTypeName == "byte[]");

if (isReferenceType)
    return options.UseNullableReferenceTypes ? "?" : "";

return "?";
```

This means a nullable database `varchar` column becomes:

- `string?` when `UseNullableReferenceTypes` is true
- `string` plus `[Nullable]` when `UseNullableReferenceTypes` is false

The factory currently does not emit any `#nullable` directive. In a project with nullable disabled, generated `string?` can trigger C# nullable-context warnings. In a warnings-as-errors build, that is a build break caused by a generated file that failed to declare its own context.

### Source Generator Nullability

`src/DataLinq.Generators/ModelGeneratorInput.cs` currently determines source-generator nullability from project-level compilation options only:

```csharp
internal static bool IsNullableEnabled(Compilation compilation)
{
    return compilation.Options.NullableContextOptions switch
    {
        NullableContextOptions.Enable => true,
        NullableContextOptions.Warnings => true,
        NullableContextOptions.Annotations => true,
        _ => false,
    };
}
```

`src/DataLinq.Generators/ModelGenerator.cs` passes that single boolean into `GeneratorFileFactoryOptions.UseNullableReferenceTypes`.

This is not enough once generated model files can carry `#nullable enable`. A C# file can locally enable nullable annotations even when the project default is disabled. Roslyn exposes that source-level state through the semantic model, but DataLinq currently ignores it and only looks at `Compilation.Options.NullableContextOptions`.

### Syntax Parsing

`src/DataLinq.SharedCore/Factories/SyntaxParser.cs` records whether a property type is syntactically nullable:

```csharp
property.SetCsNullableCore(propSyntax.Type is NullableTypeSyntax);
```

and the typed draft path does the same. That correctly preserves explicit `string?`, `int?`, and nullable relation declarations. It does not record whether the source file has nullable annotations enabled through `#nullable enable`.

That distinction matters:

- `string?` syntax tells DataLinq the property declaration is nullable.
- `#nullable enable` tells the compiler and generator that nullable reference annotations are meaningful in that file.

Both pieces are needed.

### Tests

`src/DataLinq.Generators.Tests/GeneratorTestBase.cs` creates generator test compilations with:

```csharp
.WithNullableContextOptions(NullableContextOptions.Enable)
```

Several nullability tests in `ModelGenerationLogicTests` inspect generated `NullableTypeSyntax`, but the helper currently forces project-level nullable enabled. There is no focused test proving that the source generator follows a local `#nullable enable` directive when project-level nullable is disabled.

## Desired Behavior

### Default CLI Behavior

When `UseNullableReferenceTypes` is omitted from `datalinq.json`, DataLinq should behave as if it were true:

```json
"UseNullableReferenceTypes": true
```

Generated C# model and database files should include:

```csharp
#nullable enable
```

near the top of the file.

This should apply to:

- CLI-generated database model files
- CLI-generated table model files
- CLI-generated view model files
- source-generator output files when their input model/database context is nullable-enabled

The directive should be emitted whether or not the project has nullable enabled. That is intentional. Generated files should be self-contained and should not require the CLI to guess the consuming project settings.

### Explicit Opt-Out

Users must be able to opt out with existing config syntax:

```json
"UseNullableReferenceTypes": false
```

When explicitly false:

- CLI-generated nullable reference columns should not use `?`
- CLI-generated nullable reference relations should not use `?`
- generated files should not include `#nullable enable`
- source-generator output for those generated model files should not turn reference nullability back on just because the project has nullable enabled

The last point is easy to miss. If opt-out is meant to be real, generated model files need to communicate it to the source generator. The cleanest way is to emit an explicit directive:

```csharp
#nullable disable
```

when `UseNullableReferenceTypes` is explicitly false. Without that directive, a project with `<Nullable>enable</Nullable>` will make the source generator see nullable enabled anyway.

### Directive Placement

This plan should compose with the generated-header plan. The top of a generated file should be:

```csharp
// <auto-generated />
// This file was generated by DataLinq. Do not edit this file directly.
#nullable enable

using System;
```

or, for explicit opt-out:

```csharp
// <auto-generated />
// This file was generated by DataLinq. Do not edit this file directly.
#nullable disable

using System;
```

The generated-file marker stays first. The nullable directive follows the comment banner and precedes `using` statements.

## Proposed Setting Semantics

Keep `UseNullableReferenceTypes` for compatibility, but change the default:

| Raw config value | Effective behavior |
| --- | --- |
| omitted | enabled |
| `true` | enabled |
| `false` | disabled |

This is a breaking default change for regeneration, but it is simple, visible, and reversible.

A more explicit future name such as `GeneratedNullableReferenceTypes` would be nicer, but renaming the setting now would create avoidable config churn. Keep the existing property and document the new default.

## Implementation Plan

### 1. Change the Runtime Config Default

Change `DataLinqDatabaseConfig` construction from:

```csharp
UseNullableReferenceTypes = database.UseNullableReferenceTypes ?? false;
```

to:

```csharp
UseNullableReferenceTypes = database.UseNullableReferenceTypes ?? true;
```

Keep merge behavior unchanged so `datalinq.user.json` can still override the value.

### 2. Preserve Whether the Value Was Explicit

The source generator needs a real opt-out signal. Add runtime metadata or factory options that can distinguish:

- default enabled
- explicit enabled
- explicit disabled

The simplest CLI-side shape is:

```csharp
public enum NullableReferenceTypeGeneration
{
    Enable,
    Disable
}
```

For config, the nullable bool already distinguishes omitted from explicit values before lowering. If the implementation wants source files to emit `#nullable disable` only for explicit false, `DataLinqDatabaseConfig` should preserve a flag such as:

```csharp
public bool? UseNullableReferenceTypesConfigValue { get; }
```

If we are comfortable making disabled output explicit whenever effective false, then a separate flag is not required. That is my preference: explicit `false` should produce `#nullable disable`, because otherwise source-generator behavior can be inconsistent in nullable-enabled consuming projects.

### 3. Emit Nullable Directives from CLI File Factory

Extend `ModelFileFactoryOptions` with a nullable directive policy, for example:

```csharp
public NullableDirectiveMode NullableDirective { get; set; } = NullableDirectiveMode.Enable;
```

where:

```csharp
public enum NullableDirectiveMode
{
    None,
    Enable,
    Disable
}
```

Then `ModelGenerator.CreateModels(...)` should set:

- `Enable` when `db.UseNullableReferenceTypes` is true
- `Disable` when `db.UseNullableReferenceTypes` is false

`ModelFileFactory.FileHeader(...)` should emit the directive before `using` statements.

Do not infer this from `UseNullableReferenceTypes` inside the formatter without an explicit option if the generated-header implementation introduces a shared header renderer. Header rendering should own "things before `using`".

### 4. Emit Nullable Directives from Source Generator Output

Extend `GeneratorFileFactoryOptions` with the same nullable directive policy.

When `UseNullableReferenceTypes` is true, source-generator output should include:

```csharp
#nullable enable
```

When false, include:

```csharp
#nullable disable
```

This makes generated source deterministic and self-contained. It also prevents nullable warnings from depending on the target project default.

### 5. Infer Source Nullable Context from Model Files

Replace the source generator's single project-level nullable decision with a source-aware decision.

Current behavior:

```text
effective nullable = compilation.Options.NullableContextOptions
```

Desired behavior:

```text
effective nullable for generated database =
    nullable context at the DataLinq database/model declarations,
    falling back to compilation.Options.NullableContextOptions
```

Use Roslyn semantic nullable context rather than hand-parsing directives. The important API shape is:

```csharp
var semanticModel = compilation.GetSemanticModel(syntaxTree);
var nullableContext = semanticModel.GetNullableContext(position);
var annotationsEnabled = nullableContext.AnnotationsEnabled;
```

Implementation options:

1. Add nullable-context data to `ModelDeclarationInput`.
   - This requires the syntax-provider transform to receive enough semantic context.
   - Include the nullable flag in `ModelDeclarationSnapshot`; today it uses `syntax.WithoutTrivia()`, which intentionally drops directives and comments.
   - This is best for incremental correctness.
2. Compute nullable context in `ExecuteForDatabase(...)` from metadata source locations.
   - Use `DatabaseDefinition`, `ModelDefinition`, and source locations to find the relevant syntax tree and declaration span.
   - This is less invasive but easier to get subtly wrong because metadata has lost direct declaration references.

The better implementation is option 1. The current incremental comparer ignores trivia, so a file changing from no directive to `#nullable enable` may not invalidate the model declaration input unless nullable context becomes part of the captured input. That would be a nasty IDE bug: the source changed, but generated nullability did not update.

### 6. Decide Mixed-Context Behavior

Generated CLI model files will be consistent, because all files for a database come from the same DataLinq config. Handwritten model files can be mixed:

- database class in nullable disabled context
- table model in nullable enabled context
- another table model in nullable disabled context

The current `GeneratorFileFactoryOptions.UseNullableReferenceTypes` is database-wide, so the first implementation should define a database-wide rule:

- If every DataLinq declaration for a database has nullable annotations disabled, generate disabled output.
- If every DataLinq declaration has nullable annotations enabled, generate enabled output.
- If declarations are mixed, generate enabled output and report a warning diagnostic explaining that mixed nullable contexts were found for one DataLinq database.

Generating enabled output for mixed contexts is safer than dropping annotations. The warning is important because mixed nullability in one generated database can be surprising.

A later refinement can make nullable policy per generated file, but that is not necessary for the CLI-generated model-file case.

### 7. Keep Database Nullability Separate

Do not confuse these:

- `[Nullable]` / `ColumnDefinition.Nullable`: database column accepts null
- `ValueProperty.CsNullable`: C# property type was explicitly nullable
- `UseNullableReferenceTypes`: generated reference-type annotations should be emitted and interpreted
- `#nullable enable`: compiler context for reference-type annotations

This change should not alter database schema nullability. It only changes the generated C# surface and the compiler context in generated files.

## Test Plan

Add focused tests instead of broad golden-file churn.

### Config Tests

- omitted `UseNullableReferenceTypes` defaults to true
- explicit `UseNullableReferenceTypes: true` stays true
- explicit `UseNullableReferenceTypes: false` stays false
- user config can override base config in both directions

### CLI Model Factory Tests

- default/effective enabled output includes `#nullable enable`
- enabled output renders nullable reference columns as `string?`
- explicit disabled output includes `#nullable disable` or omits nullable enable according to the final directive policy
- explicit disabled output renders nullable reference columns as `string`
- value-type nullable behavior stays unchanged
- generated files still parse with Roslyn

### Source Generator Tests

Use generator test compilations with project-level nullable disabled:

- model file contains `#nullable enable`; generated DataLinq source uses nullable reference annotations and includes `#nullable enable`
- model file contains `#nullable disable`; generated DataLinq source does not enable nullable reference annotations and includes `#nullable disable`
- project-level nullable enabled but model file contains `#nullable disable`; generated output follows the file-level opt-out
- project-level nullable disabled and model file has no directive; generated output stays disabled
- changing only `#nullable enable` to `#nullable disable` invalidates incremental generator input and changes generated output

The current `GeneratorTestBase` always enables nullable context. It needs an overload that accepts `NullableContextOptions` and actually passes the `parseOptions` parameter through to `CSharpGeneratorDriver.Create(...)` if parse options matter for a test.

### Regression Tests

- nullable `string` database column generated by the CLI compiles in a project with nullable disabled because the file contains `#nullable enable`
- source-generator output compiles without nullable-context warnings when generated from CLI-produced model files

## Documentation Updates After Implementation

Update `docs/Configuration files.md`:

- `UseNullableReferenceTypes` default changes from `false` to `true`
- explain that `false` is the opt-out
- explain that generated files declare their nullable context with `#nullable enable` or `#nullable disable`

Update any generated example snippets that show nullable reference columns without `?`.

Do not update user-facing docs before implementation lands.

## Compatibility Notes

This change affects regenerated model files. Users who commit generated models should expect one source diff where nullable reference columns and relations gain `?` and generated files gain nullable directives.

The source-generator side is a correctness fix, not just polish. Once model files carry `#nullable enable`, ignoring that directive in the generator would produce inconsistent generated code in projects with nullable disabled.

For users who cannot accept the new annotations yet, the migration path is explicit:

```json
"UseNullableReferenceTypes": false
```

That should remain supported indefinitely.

## Acceptance Criteria

- Omitted `UseNullableReferenceTypes` now enables nullable reference type generation.
- Explicit `UseNullableReferenceTypes: false` opts out.
- CLI-generated C# model and database files include a nullable directive before `using` statements.
- Nullable reference columns and relations render with `?` by default.
- Source-generator output follows `#nullable enable` / `#nullable disable` in model files, not only project-level nullable settings.
- Source-generator incremental inputs include nullable context so directive-only changes refresh generated output.
- Tests cover default, explicit opt-out, project-level nullable disabled, source-level nullable enabled, and source-level nullable disabled.
