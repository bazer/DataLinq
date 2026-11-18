# W7 Source Alias Correctness

Implementation plan, 2026-11-16. Audited against `origin/v0.10` at `8f7515e9`, which includes W6. W0 supplies the generator baseline. The functional acceptance criteria are in [issue #93](https://github.com/bazer/DataLinq/issues/93) and the [G10 contract](README.md#g10-source-type-alias-correctness).

## What a model author can expect

In a model file with `using Text = System.String;` and `#nullable disable`, a declaration such as `[Nullable, Column("name")] public abstract Text Name { get; }` should behave exactly like the same declaration written with `string`. The compiled property type is `System.String`, the generated value-property metadata has `CsNullable = true`, and reading SQL `NULL` yields C# `null`.

Two scope decisions were agreed on 2026-11-16.

- **Property coverage.** Support mapped column types, including primary and foreign keys, and both reference and collection navigation properties on models the generator already discovers. This includes an alias for the related model and an alias for a supported closed collection type. Model discovery through an aliased base contract and new relation or storage shapes are outside this wave.
- **Model regeneration.** When the file-based reader encounters an identifiable property alias it cannot bind, it reports the property and stops before replacing files. The diagnostic should give the directly spelled target type as a workaround when known. Loading the consumer's project or adding an MSBuild dependency is outside the agreed CLI scope.

## Implementation order

### 1. Reproduce the contract through generated code

Add a focused TUnit generator case for the `Customer.Name` declaration above, paired with its directly spelled equivalent. Compile the full output, including the table implementation and database metadata, and inspect the bound property types. Put the model in its own source file so ordinary file-local aliases really go out of scope. Add a SQLite test in the unit suite using the actual generated model. Insert SQL `NULL` and a non-null control through SQL, then read both through DataLinq and inspect the provider's generated metadata.

There are two close starting points. The generator already checks the metadata assignment for directly spelled `string`. The parser/materializer test substitutes CLR types into a draft by hand. The W7 runtime test must use the metadata emitted by the generator as consumed by the provider. [Generator control](../../../../src/DataLinq.Generators.Tests/ModelGenerationLogicTests.cs) · [Parser and materializer controls](../../../../src/DataLinq.Tests.Unit/Core/SyntaxParserTests.cs)

### 2. Bind the property before building metadata

At `ModelDeclarationInput.Create`, resolve mapped property symbols in the current semantic model. Pass comparable facts into `ReadMetadataSafely` and the shared property parser alongside the existing resolved type-attribute input. Both the draft and mutable parser paths need the same classification. Preserve original syntax and locations for diagnostics.

Keep the type used in a C# declaration, the underlying type used for runtime metadata, and the property's top-level nullability distinct. For example, an alias targeting `System.Nullable<int>` is nullable even when the property spelling contains no `?`. A plain `int` stays strict under `[Nullable]`. For reference types, preserve the existing directly spelled rules for explicit annotations, disabled annotations at the property, and database column nullability. Keep generic arguments, containing types, array shape and nested nullable annotations where they affect the declaration.

Apply the facts before enum lookup, scalar mapping and relation binding consume the draft. Reuse the existing converter resolver for registration, model/provider matching and validation. It already rejects generic scalar converter identities. Cover closed generics with supported forms such as `IImmutableRelation<Child>`, and use a non-generic typed ID for the converter case. Preserve existing handling of directly spelled types, including any types completed by generated source. [Generator inputs](../../../../src/DataLinq.Generators/ModelGeneratorInput.cs) · [Metadata construction](../../../../src/DataLinq.Generators/ModelGenerator.cs) · [Property parser](../../../../src/DataLinq.SharedCore/Factories/SyntaxParser.cs) · [Converter resolution](../../../../src/DataLinq.Generators/Roslyn/ScalarConverterMetadataResolver.cs)

### 3. Emit types that resolve in every generated file

Use C# keywords for built-ins or an unambiguous qualified name for the resolved target. The runtime type-name helper already serves some metadata expressions. Other emitters still interpolate `CsType.Name`. Audit generated interfaces, immutable and mutable properties, constructor and lookup signatures, key readers, navigation types and metadata bootstrap. Verify a supported aliased enum default and converter-backed key compile wherever the generator repeats their types or expressions.

Fix import collection at the same time. Today `ParseUsings` reads only `UsingDirectiveSyntax.Name`, so `using Text = System.String;` can turn into the invalid ordinary import `using System.String;`. Keep ordinary namespace imports and prevent alias targets from becoming generated imports. Compile two model files that reuse the same alias for different targets to catch accidental sharing. [Source type resolution](../../../../src/DataLinq.Generators/Roslyn/SourceModelSyntaxResolver.cs) · [Implementation and metadata emitters](../../../../src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs)

### 4. Make file-based reads fail at the property

Use the existing source-located invalid-model failure. Identify the alias in scope at the property, including a namespace alias in a qualified name. The message should name the model, property and alias, explain that the file reader lacks semantic resolution, and identify the property type span. Test a sibling namespace with the same identifier, an unused alias and an ordinary custom type as controls.

The file reader has all scanned syntax trees before it selects model and enum declarations. Retain visible global alias declarations from those inputs, including files containing only a `global using`. An alias declared outside the supplied files is indistinguishable from an ordinary unknown type to this reader. Document that limit. Exercise normal `generate models` preservation with an existing directory and verify every destination is byte-for-byte intact after the failure. [File reader](../../../../src/DataLinq.Tools/Factories/MetadataFromFileFactory.cs) · [Regeneration entry point](../../../../src/DataLinq.Tools/ModelGenerator.cs) · [Model directory tests](../../../../src/DataLinq.Tests.Unit/Core/ModelGeneratorModelDirectoryTests.cs)

### 5. Prove incremental correctness and close W7

Include resolved type identity, type kind and relevant nullability in declaration equality and in the per-database emission signature wherever they affect output. Today the declaration snapshot includes enclosing import **text**, and the formatter tracks some runtime type names. Neither fact proves that changing a global alias in another file updates cached metadata.

With one tracked generator driver, change only an alias target from `System.String` to `System.Int32` and back. Repeat with a global alias in a separate tree while the model tree stays identical. Compile every result, check the affected property's type and `CsNullable`, and compare generated sources and diagnostics with a fresh driver. Keep an independent database as a control for cached emission. Extend the existing import-edit test, which already checks a default-value diagnostic across a target change. [Incremental tests](../../../../src/DataLinq.Generators.Tests/IncrementalGeneratorScaleTests.cs) · [Emission comparer](../../../../src/DataLinq.Generators/DatabaseEmissionInput.cs)

## Acceptance evidence

| Case | Observable result |
| --- | --- |
| Reference and value nullability | SQLite reads `NULL` and text through `Customer.Name`. Under enabled annotations, `Text` and `Text?` match `string` and `string?`. Under disabled annotations, `[Nullable]` preserves strict `int` and permits a nullable value-type target. |
| Namespace, enum and array aliases | Compile a namespace-qualified property such as `Bcl.String` with `using Bcl = System;`, an alias for `byte[]`, and an aliased enum with a default. Check their metadata against directly spelled controls. |
| Scalar converter and keys | An aliased typed ID retains its converter and provider type. Write a value and look it up through the generated key API. |
| Navigations | An aliased parent reference and an alias for the closed `IImmutableRelation<Child>` compile. Inspect relation metadata and generated async accessor signatures. Follow the relation in each direction on seeded rows. An optional missing parent yields the same result as the directly spelled navigation. |
| File-based tooling | Assert failure text and source span for a used alias, successful reads for controls, and preservation of existing files. |
| Incremental execution | Local and cross-file target changes update the affected output in a reused driver. Reverting restores the original contract, and unrelated database emission stays cached in the control case. |

Start with focused `generators` and `unit` runs. Review representative generated source against the directly spelled controls and existing approvals. If a passing case would require a broad generator or metadata redesign, bring that case back for a scope decision. Use the [local feedback loop](../../../Contributing.md#local-feedback-loop) for the completed change.

Once the behavior passes, document build-time support and the regeneration limitation in the [source generator guide](../../../internals/Source%20Generator.md) and [model generation guide](../../../model-generation.md). Record the candidate revision, test selections, results and representative generated output on [release evidence issue #106](https://github.com/bazer/DataLinq/issues/106) before closing #93. W8 owns the broad integration and performance runs. [Issue sequencing](https://github.com/bazer/DataLinq/issues/93#issuecomment-5417698260) · [Release evidence plan](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md#source-type-aliases)
