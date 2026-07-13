using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanDebugWriter
{
    public static string WriteTemplate(QueryPlanTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var builder = new StringBuilder();
        builder.AppendLine("query-template v0");
        WriteSources(builder, template);
        WriteOperations(builder, template);
        WriteProjection(builder, template.Projection);
        WriteResult(builder, template.Result);
        WriteBindingDeclarations(builder, template.BindingDeclarations);
        WriteSpecialization(builder, template.Specialization);
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    }

    public static string WriteInvocation(QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var builder = new StringBuilder();
        builder.AppendLine("query-invocation v0");
        builder.AppendLine("binding-values:");
        if (invocation.Values.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            foreach (var value in invocation.Values.Items)
            {
                builder
                    .Append("  ")
                    .Append(value.Id)
                    .Append(' ')
                    .Append(ToToken(value.Kind));

                if (value is QueryPlanInvocationValue.LocalSequence sequence)
                {
                    builder
                        .Append(" count=")
                        .Append(sequence.Values.Count.ToString(CultureInfo.InvariantCulture))
                        .Append(" values=<redacted>");
                }
                else
                {
                    builder.Append(" value=<redacted>");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    }

    private static void WriteSources(StringBuilder builder, QueryPlanTemplate template)
    {
        builder.AppendLine("sources:");
        foreach (var source in template.Sources)
        {
            builder
                .Append("  ")
                .Append(source.Id)
                .Append(' ')
                .Append(ToToken(source.Kind))
                .Append(" alias=")
                .Append(source.Alias)
                .Append(" table=")
                .Append(source.Table.DbName)
                .Append(" element=")
                .Append(TypeName(source.ElementType))
                .Append(" cardinality=")
                .Append(ToToken(source.Cardinality))
                .Append(" nullable=")
                .Append(source.IsNullable.ToString(CultureInfo.InvariantCulture).ToLowerInvariant())
                .AppendLine();
        }
    }

    private static void WriteOperations(StringBuilder builder, QueryPlanTemplate template)
    {
        builder.AppendLine("operations:");
        if (template.Operations.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        foreach (var operation in template.Operations)
        {
            builder.Append("  ");
            switch (operation)
            {
                case QueryPlanOperation.Where where:
                    builder.Append("where ").AppendLine(FormatPredicate(where.Predicate));
                    break;

                case QueryPlanOperation.Having having:
                    builder.Append("having ").AppendLine(FormatPredicate(having.Predicate));
                    break;

                case QueryPlanOperation.OrderBy orderBy:
                    builder
                        .Append("order-by ")
                        .AppendLine(string.Join(", ", orderBy.Orderings.Select(FormatOrdering)));
                    break;

                case QueryPlanOperation.Skip skip:
                    builder.Append("skip ").AppendLine(FormatValue(skip.Count));
                    break;

                case QueryPlanOperation.Take take:
                    builder.Append("take ").AppendLine(FormatValue(take.Count));
                    break;

                case QueryPlanOperation.Join join:
                    builder.Append("join ").AppendLine(FormatJoin(join.JoinShape));
                    break;

                case QueryPlanOperation.GroupBy groupBy:
                    builder
                        .Append("group-by ")
                        .AppendLine(string.Join(", ", groupBy.Keys.Select(FormatValue)));
                    break;

                case QueryPlanOperation.Pushdown pushdown:
                    builder.AppendLine("pushdown");
                    foreach (var innerOperation in pushdown.Operations)
                    {
                        builder
                            .Append("    ")
                            .AppendLine(FormatOperation(innerOperation));
                    }

                    if (pushdown.PreservedOrderings.Count != 0)
                    {
                        builder
                            .Append("    preserves-order ")
                            .AppendLine(string.Join(", ", pushdown.PreservedOrderings.Select(FormatOrdering)));
                    }

                    break;

                default:
                    throw new InvalidOperationException($"Unknown query plan operation '{operation.GetType().Name}'.");
            }
        }
    }

    private static string FormatOperation(QueryPlanOperation operation)
    {
        return operation switch
        {
            QueryPlanOperation.Where where => $"where {FormatPredicate(where.Predicate)}",
            QueryPlanOperation.Having having => $"having {FormatPredicate(having.Predicate)}",
            QueryPlanOperation.OrderBy orderBy => $"order-by {string.Join(", ", orderBy.Orderings.Select(FormatOrdering))}",
            QueryPlanOperation.Skip skip => $"skip {FormatValue(skip.Count)}",
            QueryPlanOperation.Take take => $"take {FormatValue(take.Count)}",
            QueryPlanOperation.Join join => $"join {FormatJoin(join.JoinShape)}",
            QueryPlanOperation.GroupBy groupBy => $"group-by {string.Join(", ", groupBy.Keys.Select(FormatValue))}",
            QueryPlanOperation.Pushdown => "pushdown",
            _ => throw new InvalidOperationException($"Unknown query plan operation '{operation.GetType().Name}'.")
        };
    }

    private static void WriteProjection(StringBuilder builder, QueryPlanProjection projection)
    {
        builder.AppendLine("projection:");
        builder.Append("  ");
        switch (projection)
        {
            case QueryPlanProjection.Entity entity:
                builder
                    .Append("entity source=")
                    .Append(entity.Source.Id)
                    .Append(" type=")
                    .Append(TypeName(entity.ResultType))
                    .Append(" disposition=")
                    .AppendLine(ToToken(entity.Disposition));
                break;

            case QueryPlanProjection.ScalarMember scalar:
                builder
                    .Append("scalar-member ")
                    .Append(FormatColumn(scalar.Source, scalar.Column))
                    .Append(" type=")
                    .Append(TypeName(scalar.ResultType))
                    .Append(" disposition=")
                    .AppendLine(ToToken(scalar.Disposition));
                break;

            case QueryPlanProjection.Anonymous anonymous:
                builder
                    .Append("anonymous type=")
                    .Append(TypeName(anonymous.ResultType))
                    .Append(" sources=")
                    .Append(string.Join(",", anonymous.Sources.Select(static source => source.Id)))
                    .Append(" members=")
                    .Append(FormatMembers(anonymous.Members))
                    .Append(" disposition=")
                    .Append(ToToken(anonymous.Disposition))
                    .Append(" recipe=")
                    .AppendLine(FormatRecipe(anonymous.Recipe));
                break;

            case QueryPlanProjection.ComputedRowLocal computed:
                builder
                    .Append("computed-row-local type=")
                    .Append(TypeName(computed.ResultType))
                    .Append(" sources=")
                    .Append(string.Join(",", computed.Sources.Select(static source => source.Id)))
                    .Append(" disposition=")
                    .Append(ToToken(computed.Disposition))
                    .Append(" recipe=")
                    .AppendLine(FormatRecipe(computed.Recipe));
                break;

            case QueryPlanProjection.JoinedRowLocal joined:
                builder
                    .Append("joined-row-local type=")
                    .Append(TypeName(joined.ResultType))
                    .Append(" sources=")
                    .Append(string.Join(",", joined.Sources.Select(static source => source.Id)))
                    .Append(" members=")
                    .Append(FormatMembers(joined.Members))
                    .Append(" disposition=")
                    .Append(ToToken(joined.Disposition))
                    .Append(" recipe=")
                    .AppendLine(FormatRecipe(joined.Recipe));
                break;

            case QueryPlanProjection.SqlRow sqlRow:
                builder
                    .Append("sql-row type=")
                    .Append(TypeName(sqlRow.ResultType))
                    .Append(" members=")
                    .Append(FormatMembers(sqlRow.Members))
                    .Append(" disposition=")
                    .AppendLine(ToToken(sqlRow.Disposition));
                break;

            case QueryPlanProjection.TransparentIdentifier transparent:
                builder
                    .Append("transparent-identifier type=")
                    .Append(TypeName(transparent.ResultType))
                    .Append(" sources=")
                    .Append(string.Join(", ", transparent.SourcesByMember.Select(source => $"{source.Key}={source.Value.Id}")))
                    .Append(" disposition=")
                    .AppendLine(ToToken(transparent.Disposition));
                break;

            case QueryPlanProjection.GroupedAggregate grouped:
                builder
                    .Append("grouped-aggregate type=")
                    .Append(TypeName(grouped.ResultType))
                    .Append(" source=")
                    .Append(grouped.Source.Id)
                    .Append(" members=")
                    .Append(FormatMembers(grouped.Members))
                    .Append(" disposition=")
                    .AppendLine(ToToken(grouped.Disposition));
                break;

            default:
                throw new InvalidOperationException($"Unknown query plan projection '{projection.GetType().Name}'.");
        }
    }

    private static void WriteResult(StringBuilder builder, QueryPlanResult result)
    {
        builder.AppendLine("result:");
        builder
            .Append("  ")
            .Append(ToToken(result.Kind))
            .Append(" type=")
            .Append(TypeName(result.ResultType));

        if (result.AggregateSelector is not null)
        {
            builder
                .Append(" selector=")
                .Append(FormatValue(result.AggregateSelector));
        }

        builder.AppendLine();
    }

    private static void WriteBindingDeclarations(
        StringBuilder builder,
        QueryPlanBindingDeclarations declarations)
    {
        builder.AppendLine("binding-declarations:");
        if (declarations.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            builder
                .Append("  ")
                .Append(declaration.Id)
                .Append(' ')
                .Append(ToToken(declaration.Kind))
                .Append(" model=")
                .Append(TypeName(declaration.ModelType))
                .Append(" provider=")
                .Append(TypeName(declaration.ProviderType))
                .Append(" allows-null=")
                .AppendLine(declaration.AllowsNull.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
        }
    }

    private static void WriteSpecialization(
        StringBuilder builder,
        QueryPlanSpecialization specialization)
    {
        builder.AppendLine("specialization:");
        if (specialization.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        foreach (var constraint in specialization.Items)
        {
            builder
                .Append("  ")
                .Append(constraint.BindingId)
                .Append(' ');

            switch (constraint)
            {
                case QueryPlanBindingSpecialization.ScalarNullness scalar:
                    builder
                        .Append("scalar nullness=")
                        .Append(ToToken(scalar.Nullness));
                    break;
                case QueryPlanBindingSpecialization.LocalSequenceShape sequence:
                    builder
                        .Append("local-sequence count=")
                        .Append(sequence.Count.ToString(CultureInfo.InvariantCulture))
                        .Append(" null-count=")
                        .Append(sequence.NullCount.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown query plan binding specialization '{constraint.GetType().Name}'.");
            }

            builder.AppendLine();
        }
    }

    private static string FormatOrdering(QueryPlanOrdering ordering)
        => $"{FormatValue(ordering.Value)} {ToToken(ordering.Direction)}";

    private static string FormatJoin(QueryPlanJoin join)
        => $"{ToToken(join.Kind)} {FormatColumn(join.LeftSource, join.LeftColumn)} = {FormatColumn(join.RightSource, join.RightColumn)}";

    private static string FormatPredicate(QueryPlanPredicate predicate)
    {
        return predicate switch
        {
            QueryPlanPredicate.Fixed fixedPredicate => fixedPredicate.Value ? "fixed(true)" : "fixed(false)",
            QueryPlanPredicate.And and => $"and({string.Join(", ", and.Terms.Select(FormatPredicate))})",
            QueryPlanPredicate.Or or => $"or({string.Join(", ", or.Terms.Select(FormatPredicate))})",
            QueryPlanPredicate.Not not => $"not({FormatPredicate(not.Predicate)})",
            QueryPlanPredicate.Compare compare => FormatCompare(compare),
            QueryPlanPredicate.In inPredicate => $"{(inPredicate.IsNegated ? "not-in" : "in")}({FormatValue(inPredicate.Item)}, {FormatValue(inPredicate.Sequence)})",
            QueryPlanPredicate.Exists exists => FormatExists(exists),
            _ => throw new InvalidOperationException($"Unknown query plan predicate '{predicate.GetType().Name}'.")
        };
    }

    private static string FormatCompare(QueryPlanPredicate.Compare compare)
    {
        var nullSemantics = compare.NullSemantics == QueryPlanNullSemantics.Default
            ? string.Empty
            : $" nulls={ToToken(compare.NullSemantics)}";

        return $"compare({FormatValue(compare.Left)} {OperatorToken(compare.Operator)} {FormatValue(compare.Right)}{nullSemantics})";
    }

    private static string FormatExists(QueryPlanPredicate.Exists exists)
    {
        var predicate = exists.Predicate is null
            ? string.Empty
            : $" predicate={FormatPredicate(exists.Predicate)}";

        return $"{(exists.IsNegated ? "not-exists" : "exists")}(relation={exists.Relation.PropertyName} parent={exists.ParentSource.Id} child={exists.ChildSource.Id}{predicate})";
    }

    private static string FormatMembers(System.Collections.Generic.IReadOnlyList<QueryPlanProjectionMember> members)
        => $"[{string.Join(", ", members.Select(static member => $"{member.Name}={FormatValue(member.Value)}"))}]";

    private static string FormatRecipe(QueryPlanProjectionRecipe recipe)
    {
        return recipe switch
        {
            QueryPlanProjectionRecipe.Source source =>
                $"source({source.SourceSlot.Id}:{TypeName(source.ResultType)})",
            QueryPlanProjectionRecipe.SourceColumn column =>
                $"source-column({FormatColumn(column.SourceSlot, column.Column)}:{TypeName(column.ResultType)})",
            QueryPlanProjectionRecipe.ScalarBinding scalar =>
                $"scalar-binding({scalar.BindingId}:{TypeName(scalar.ResultType)})",
            QueryPlanProjectionRecipe.Intrinsic intrinsic =>
                $"intrinsic({ToToken(intrinsic.IntrinsicKind)}:{TypeName(intrinsic.ResultType)})",
            QueryPlanProjectionRecipe.Convert convert =>
                $"convert({FormatRecipe(convert.Operand)} -> {TypeName(convert.ResultType)})",
            QueryPlanProjectionRecipe.Not not =>
                $"not({FormatRecipe(not.Operand)}:{TypeName(not.ResultType)})",
            QueryPlanProjectionRecipe.Binary binary =>
                $"binary({ToToken(binary.Operator)} {FormatRecipe(binary.Left)}, {FormatRecipe(binary.Right)}:{TypeName(binary.ResultType)})",
            QueryPlanProjectionRecipe.SupportedMember member =>
                $"member({ToToken(member.Member)} {FormatRecipe(member.Instance)}:{TypeName(member.ResultType)})",
            QueryPlanProjectionRecipe.Function function =>
                $"function({ToToken(function.FunctionKind)}:{TypeName(function.ResultType)} {string.Join(", ", function.Arguments.Select(FormatRecipe))})",
            QueryPlanProjectionRecipe.Conditional conditional =>
                $"conditional(test={FormatRecipe(conditional.Test)}, true={FormatRecipe(conditional.IfTrue)}, false={FormatRecipe(conditional.IfFalse)}:{TypeName(conditional.ResultType)})",
            QueryPlanProjectionRecipe.NewArray newArray =>
                $"new-array({TypeName(newArray.ElementType)} [{string.Join(", ", newArray.Elements.Select(FormatRecipe))}])",
            QueryPlanProjectionRecipe.CompatibilityConstructor constructor =>
                $"compat-constructor({FormatConstructor(constructor.Constructor)} [{string.Join(", ", constructor.Arguments.Select(FormatRecipe))}])",
            QueryPlanProjectionRecipe.CompatibilityMember member =>
                $"compat-member({FormatMember(member.Member)} instance={(member.Instance is null ? "static" : FormatRecipe(member.Instance))}:{TypeName(member.ResultType)})",
            _ => throw new InvalidOperationException($"Unknown projection recipe '{recipe.GetType().Name}'.")
        };
    }

    private static string FormatConstructor(System.Reflection.ConstructorInfo constructor)
        => $"{TypeName(constructor.DeclaringType ?? typeof(object))}({string.Join(",", constructor.GetParameters().Select(parameter => TypeName(parameter.ParameterType)))})";

    private static string FormatMember(System.Reflection.MemberInfo member)
        => $"{TypeName(member.DeclaringType ?? typeof(object))}.{member.Name}";

    private static string FormatValue(QueryPlanValue value)
    {
        return value switch
        {
            QueryPlanColumnValue column => FormatColumn(column.Source, column.Column),
            QueryPlanIntrinsicValue intrinsic => $"intrinsic({IntrinsicToken(intrinsic)}:{TypeName(intrinsic.ClrType)})",
            QueryPlanScalarBindingReference scalar => $"scalar-binding({scalar.BindingId}:{TypeName(scalar.ClrType)})",
            QueryPlanLocalSequenceBindingReference sequence => $"local-sequence-binding({sequence.BindingId}:{TypeName(sequence.ElementType)})",
            QueryPlanFunctionValue function => $"function({ToToken(function.Function)}:{TypeName(function.ClrType)} {string.Join(", ", function.Arguments.Select(FormatValue))})",
            QueryPlanConvertedValue converted => $"convert({FormatValue(converted.Value)} -> {TypeName(converted.TargetType)})",
            QueryPlanGroupKeyValue groupKey => $"group-key({FormatValue(groupKey.Key)}:{TypeName(groupKey.ClrType)})",
            QueryPlanGroupedAggregateValue groupedAggregate => groupedAggregate.Selector is null
                ? $"grouped-aggregate({ToToken(groupedAggregate.Aggregate)}:{TypeName(groupedAggregate.ClrType)})"
                : $"grouped-aggregate({ToToken(groupedAggregate.Aggregate)}:{TypeName(groupedAggregate.ClrType)} selector={FormatValue(groupedAggregate.Selector)})",
            _ => throw new InvalidOperationException($"Unknown query plan value '{value.GetType().Name}'.")
        };
    }

    private static string IntrinsicToken(QueryPlanIntrinsicValue intrinsic) => intrinsic.Intrinsic switch
    {
        QueryPlanIntrinsicKind.Null => "null",
        QueryPlanIntrinsicKind.BooleanTrue => "true",
        QueryPlanIntrinsicKind.BooleanFalse => "false",
        _ => throw new InvalidOperationException($"Unknown query plan intrinsic '{intrinsic.Intrinsic}'.")
    };

    private static string FormatColumn(QueryPlanSourceSlot source, DataLinq.Metadata.ColumnDefinition column)
        => $"column({source.Id}.{column.DbName}:{TypeName(column.ValueProperty?.CsType.Type ?? typeof(object))})";

    private static string OperatorToken(QueryPlanComparisonOperator comparisonOperator) => comparisonOperator switch
    {
        QueryPlanComparisonOperator.Equal => "==",
        QueryPlanComparisonOperator.NotEqual => "!=",
        QueryPlanComparisonOperator.GreaterThan => ">",
        QueryPlanComparisonOperator.GreaterThanOrEqual => ">=",
        QueryPlanComparisonOperator.LessThan => "<",
        QueryPlanComparisonOperator.LessThanOrEqual => "<=",
        _ => comparisonOperator.ToString()
    };

    private static string ToToken<T>(T value)
        where T : struct, Enum
        => ToKebabCase(value.ToString());

    private static string ToKebabCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && (char.IsLower(value[index - 1]) || index + 1 < value.Length && char.IsLower(value[index + 1])))
                builder.Append('-');

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } nullableType)
            return $"{TypeName(nullableType)}?";

        if (type.IsArray)
            return $"{TypeName(type.GetElementType()!)}[]";

        if (IsAnonymous(type))
            return "anonymous";

        if (!type.IsGenericType)
            return type.Name;

        var genericName = type.Name;
        var tick = genericName.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
            genericName = genericName[..tick];

        return $"{genericName}<{string.Join(",", type.GetGenericArguments().Select(TypeName))}>";
    }

    private static bool IsAnonymous(Type type)
        => Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), inherit: false) &&
           type.IsGenericType &&
           type.Name.Contains("AnonymousType", StringComparison.Ordinal) &&
           (type.Name.StartsWith("<>", StringComparison.Ordinal) || type.Name.StartsWith("VB$", StringComparison.Ordinal)) &&
           type.Attributes.HasFlag(System.Reflection.TypeAttributes.NotPublic);
}
