using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal sealed class QueryPlanTemplate
{
    public QueryPlanTemplate(
        IEnumerable<QueryPlanSourceSlot> sources,
        IEnumerable<QueryPlanOperation> operations,
        QueryPlanProjection projection,
        QueryPlanResult result,
        QueryPlanBindingDeclarations bindingDeclarations,
        QueryPlanSpecialization specialization)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bindingDeclarations);
        ArgumentNullException.ThrowIfNull(specialization);

        Sources = Freeze(sources, nameof(sources));
        Operations = Freeze(operations, nameof(operations));
        Projection = projection;
        Result = result;
        BindingDeclarations = bindingDeclarations;
        Specialization = specialization;

        ValidateSourceIds(Sources);
        QueryPlanTemplateValidator.Validate(this);
    }

    public IReadOnlyList<QueryPlanSourceSlot> Sources { get; }

    public IReadOnlyList<QueryPlanOperation> Operations { get; }

    public QueryPlanProjection Projection { get; }

    public QueryPlanResult Result { get; }

    public QueryPlanBindingDeclarations BindingDeclarations { get; }

    public QueryPlanSpecialization Specialization { get; }

    public QueryPlanSourceSlot GetSource(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Sources.FirstOrDefault(source => source.Id == id)
            ?? throw new InvalidOperationException($"Query plan source slot '{id}' does not exist.");
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
    {
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Query plan collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
    }

    private static void ValidateSourceIds(IReadOnlyList<QueryPlanSourceSlot> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("A query plan template must contain at least one source slot.", nameof(sources));

        var duplicate = sources
            .GroupBy(static source => source.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Query plan source slot id '{duplicate.Key}' is duplicated.", nameof(sources));
    }
}

internal static class QueryPlanTemplateValidator
{
    public static void Validate(QueryPlanTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        ValidateSpecialization(template.BindingDeclarations, template.Specialization);
        ValidateSourceReferences(template);

        foreach (var operation in template.Operations)
            ValidateOperation(operation, template.BindingDeclarations);

        ValidateProjection(template.Projection, template.BindingDeclarations);
        if (template.Result.AggregateSelector is not null)
            ValidateValue(template.Result.AggregateSelector, template.BindingDeclarations);
    }

    private static void ValidateSpecialization(
        QueryPlanBindingDeclarations declarations,
        QueryPlanSpecialization specialization)
    {
        foreach (var constraint in specialization.Items)
        {
            if (!declarations.TryGet(constraint.BindingId, out var declaration))
            {
                throw new ArgumentException(
                    $"Query plan specialization references undeclared binding '{constraint.BindingId}'.",
                    nameof(specialization));
            }

            if (declaration.Kind != constraint.Kind)
            {
                throw new ArgumentException(
                    $"Query plan specialization for binding '{constraint.BindingId}' has kind '{constraint.Kind}', " +
                    $"but the declaration has kind '{declaration.Kind}'.",
                    nameof(specialization));
            }

            if (constraint is QueryPlanBindingSpecialization.ScalarNullness
                {
                    Nullness: QueryPlanBindingNullness.Null
                } && !declaration.AllowsNull)
            {
                throw new ArgumentException(
                    $"Query plan specialization for binding '{constraint.BindingId}' requires null, " +
                    "but the declaration does not allow null invocation values.",
                    nameof(specialization));
            }

            if (constraint is QueryPlanBindingSpecialization.LocalSequenceShape
                {
                    NullCount: > 0
                } && !declaration.AllowsNull)
            {
                throw new ArgumentException(
                    $"Query plan specialization for binding '{constraint.BindingId}' requires null sequence elements, " +
                    "but the declaration does not allow null invocation values.",
                    nameof(specialization));
            }
        }

        foreach (var declaration in declarations.Items)
        {
            if (!specialization.TryGet(declaration.Id, out _))
            {
                throw new ArgumentException(
                    $"Query plan binding declaration '{declaration.Id}' has no explicit specialization.",
                    nameof(specialization));
            }
        }
    }

    private static void ValidateOperation(
        QueryPlanOperation operation,
        QueryPlanBindingDeclarations declarations)
    {
        switch (operation)
        {
            case QueryPlanOperation.Where where:
                ValidatePredicate(where.Predicate, declarations);
                break;
            case QueryPlanOperation.Having having:
                ValidatePredicate(having.Predicate, declarations);
                break;
            case QueryPlanOperation.OrderBy orderBy:
                foreach (var ordering in orderBy.Orderings)
                    ValidateValue(ordering.Value, declarations);
                break;
            case QueryPlanOperation.Skip skip:
                ValidateValue(skip.Count, declarations);
                break;
            case QueryPlanOperation.Take take:
                ValidateValue(take.Count, declarations);
                break;
            case QueryPlanOperation.Pushdown pushdown:
                foreach (var innerOperation in pushdown.Operations)
                    ValidateOperation(innerOperation, declarations);
                foreach (var ordering in pushdown.PreservedOrderings)
                    ValidateValue(ordering.Value, declarations);
                break;
            case QueryPlanOperation.GroupBy groupBy:
                foreach (var key in groupBy.Keys)
                    ValidateValue(key, declarations);
                break;
            case QueryPlanOperation.Join:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan operation '{operation.GetType().Name}'.",
                    nameof(operation));
        }
    }

    private static void ValidatePredicate(
        QueryPlanPredicate predicate,
        QueryPlanBindingDeclarations declarations)
    {
        switch (predicate)
        {
            case QueryPlanPredicate.Fixed:
                break;
            case QueryPlanPredicate.And and:
                foreach (var term in and.Terms)
                    ValidatePredicate(term, declarations);
                break;
            case QueryPlanPredicate.Or or:
                foreach (var term in or.Terms)
                    ValidatePredicate(term, declarations);
                break;
            case QueryPlanPredicate.Not not:
                ValidatePredicate(not.Predicate, declarations);
                break;
            case QueryPlanPredicate.Compare compare:
                ValidateValue(compare.Left, declarations);
                ValidateValue(compare.Right, declarations);
                break;
            case QueryPlanPredicate.In inPredicate:
                ValidateValue(inPredicate.Item, declarations);
                ValidateValue(inPredicate.Sequence, declarations);
                break;
            case QueryPlanPredicate.Exists exists:
                if (exists.Predicate is not null)
                    ValidatePredicate(exists.Predicate, declarations);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan predicate '{predicate.GetType().Name}'.",
                    nameof(predicate));
        }
    }

    private static void ValidateProjection(
        QueryPlanProjection projection,
        QueryPlanBindingDeclarations declarations)
    {
        if (!Enum.IsDefined(projection.Disposition))
        {
            throw new ArgumentException(
                $"Query plan projection '{projection.Kind}' has undefined disposition '{projection.Disposition}'.",
                nameof(projection));
        }

        if (projection.Disposition == QueryPlanProjectionDisposition.Unsupported)
        {
            throw new ArgumentException(
                $"Query plan projection '{projection.Kind}' is unsupported in an executable query template.",
                nameof(projection));
        }

        switch (projection)
        {
            case QueryPlanProjection.Anonymous anonymous:
                ValidateMembers(anonymous.Members, declarations);
                ValidateProjectionRecipeRoot(anonymous.ResultType, anonymous.Disposition, anonymous.Recipe);
                ValidateProjectionRecipe(anonymous.Recipe, declarations);
                break;
            case QueryPlanProjection.JoinedRowLocal joined:
                ValidateMembers(joined.Members, declarations);
                ValidateProjectionRecipeRoot(joined.ResultType, joined.Disposition, joined.Recipe);
                ValidateProjectionRecipe(joined.Recipe, declarations);
                break;
            case QueryPlanProjection.SqlRow sqlRow:
                ValidateMembers(sqlRow.Members, declarations);
                ValidateProjectionConstructor(
                    sqlRow.Constructor,
                    sqlRow.ResultType,
                    sqlRow.Members.Select(static member => member.Value.ClrType).ToArray(),
                    "SQL-row projection");
                break;
            case QueryPlanProjection.GroupedAggregate grouped:
                ValidateMembers(grouped.Members, declarations);
                ValidateProjectionConstructor(
                    grouped.Constructor,
                    grouped.ResultType,
                    grouped.Members.Select(static member => member.Value.ClrType).ToArray(),
                    "grouped-aggregate projection");
                break;
            case QueryPlanProjection.Entity:
            case QueryPlanProjection.ScalarMember:
                break;
            case QueryPlanProjection.ComputedRowLocal computed:
                ValidateProjectionRecipeRoot(computed.ResultType, computed.Disposition, computed.Recipe);
                ValidateProjectionRecipe(computed.Recipe, declarations);
                break;
            case QueryPlanProjection.TransparentIdentifier:
                throw new ArgumentException(
                    "Transparent-identifier projections are parser-internal and cannot be retained in an executable query template.",
                    nameof(projection));
            default:
                throw new ArgumentException(
                    $"Unknown query plan projection '{projection.GetType().Name}'.",
                    nameof(projection));
        }
    }

    private static void ValidateProjectionRecipeRoot(
        Type projectionResultType,
        QueryPlanProjectionDisposition projectionDisposition,
        QueryPlanProjectionRecipe recipe)
    {
        if (recipe.ResultType != projectionResultType)
        {
            throw new ArgumentException(
                $"Projection recipe result type '{recipe.ResultType}' does not match projection result type '{projectionResultType}'.",
                nameof(recipe));
        }

        if (recipe.Disposition is not (
            QueryPlanProjectionDisposition.AotSafe or
            QueryPlanProjectionDisposition.SqlOnlyCompatibility))
        {
            throw new ArgumentException(
                $"Projection recipe has invalid executable disposition '{recipe.Disposition}'.",
                nameof(recipe));
        }

        // A projection kind may impose a stricter backend fence than its
        // normalized recipe. Anonymous and joined-row-local projections are
        // SQL-only in 0.9 even when their inner scalar recipe is AOT-safe.
        if (projectionDisposition == QueryPlanProjectionDisposition.AotSafe &&
            recipe.Disposition != QueryPlanProjectionDisposition.AotSafe)
        {
            throw new ArgumentException(
                $"Projection disposition '{projectionDisposition}' does not match recipe disposition '{recipe.Disposition}'.",
                nameof(recipe));
        }
    }

    private static void ValidateProjectionRecipe(
        QueryPlanProjectionRecipe recipe,
        QueryPlanBindingDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        switch (recipe)
        {
            case QueryPlanProjectionRecipe.Source:
                break;
            case QueryPlanProjectionRecipe.SourceColumn column:
                var columnType = column.Column.ValueProperty?.CsType.Type;
                if (columnType is not null &&
                    GetNonNullableType(columnType) != GetNonNullableType(column.ResultType))
                {
                    throw new ArgumentException(
                        $"Projection source-column recipe type '{column.ResultType}' does not match column model type '{columnType}'.",
                        nameof(recipe));
                }
                break;
            case QueryPlanProjectionRecipe.ScalarBinding scalar:
                ValidateReference(
                    scalar.BindingId,
                    QueryPlanBindingKind.Scalar,
                    scalar.ResultType,
                    declarations);
                break;
            case QueryPlanProjectionRecipe.Intrinsic intrinsic:
                ValidateProjectionIntrinsic(intrinsic);
                break;
            case QueryPlanProjectionRecipe.Convert convert:
                if (!QueryPlanProjectionRecipe.IsSupportedConversion(
                        convert.Operand.ResultType,
                        convert.ResultType))
                {
                    throw new ArgumentException(
                        $"Projection conversion from '{convert.Operand.ResultType}' to '{convert.ResultType}' is not supported.",
                        nameof(recipe));
                }
                ValidateProjectionRecipe(convert.Operand, declarations);
                break;
            case QueryPlanProjectionRecipe.Not not:
                if (!IsValidProjectionNot(not.Operand.ResultType, not.ResultType))
                {
                    throw new ArgumentException(
                        "Projection Not recipes require matching Boolean or nullable-Boolean operand and result types.",
                        nameof(recipe));
                }
                ValidateProjectionRecipe(not.Operand, declarations);
                break;
            case QueryPlanProjectionRecipe.Binary binary:
                ValidateProjectionBinary(binary);
                ValidateProjectionRecipe(binary.Left, declarations);
                ValidateProjectionRecipe(binary.Right, declarations);
                break;
            case QueryPlanProjectionRecipe.SupportedMember member:
                ValidateProjectionSupportedMember(member);
                ValidateProjectionRecipe(member.Instance, declarations);
                break;
            case QueryPlanProjectionRecipe.Function function:
                ValidateProjectionFunction(function);
                foreach (var argument in function.Arguments)
                    ValidateProjectionRecipe(argument, declarations);
                break;
            case QueryPlanProjectionRecipe.Conditional conditional:
                ValidateProjectionConditional(conditional);
                ValidateProjectionRecipe(conditional.Test, declarations);
                ValidateProjectionRecipe(conditional.IfTrue, declarations);
                ValidateProjectionRecipe(conditional.IfFalse, declarations);
                break;
            case QueryPlanProjectionRecipe.NewArray newArray:
                ValidateProjectionArray(newArray);
                foreach (var element in newArray.Elements)
                    ValidateProjectionRecipe(element, declarations);
                break;
            case QueryPlanProjectionRecipe.CompatibilityConstructor constructor:
                ValidateCompatibilityConstructor(constructor);
                foreach (var argument in constructor.Arguments)
                    ValidateProjectionRecipe(argument, declarations);
                break;
            case QueryPlanProjectionRecipe.CompatibilityMember member:
                ValidateCompatibilityMember(member);
                if (member.Instance is not null)
                    ValidateProjectionRecipe(member.Instance, declarations);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown projection recipe '{recipe.GetType().Name}'.",
                    nameof(recipe));
        }
    }

    private static void ValidateProjectionBinary(QueryPlanProjectionRecipe.Binary binary)
    {
        switch (binary.Operator)
        {
            case QueryPlanProjectionBinaryOperator.AndAlso:
            case QueryPlanProjectionBinaryOperator.OrElse:
                if (binary.Left.ResultType != typeof(bool) ||
                    binary.Right.ResultType != typeof(bool) ||
                    binary.ResultType != typeof(bool))
                {
                    throw new ArgumentException(
                        $"Projection binary operator '{binary.Operator}' requires Boolean operands and result.",
                        nameof(binary));
                }
                break;
            case QueryPlanProjectionBinaryOperator.Equal:
            case QueryPlanProjectionBinaryOperator.NotEqual:
                if (binary.ResultType != typeof(bool) ||
                    !AreProjectionOperandTypesCompatible(binary.Left.ResultType, binary.Right.ResultType))
                {
                    throw new ArgumentException(
                        $"Projection equality operator '{binary.Operator}' requires compatible operands and a Boolean result.",
                        nameof(binary));
                }
                break;
            case QueryPlanProjectionBinaryOperator.GreaterThan:
            case QueryPlanProjectionBinaryOperator.GreaterThanOrEqual:
            case QueryPlanProjectionBinaryOperator.LessThan:
            case QueryPlanProjectionBinaryOperator.LessThanOrEqual:
                if (GetNonNullableType(binary.ResultType) != typeof(bool) ||
                    !AreProjectionOperandTypesCompatible(binary.Left.ResultType, binary.Right.ResultType) ||
                    (Nullable.GetUnderlyingType(binary.ResultType) is not null &&
                     Nullable.GetUnderlyingType(binary.Left.ResultType) is null &&
                     Nullable.GetUnderlyingType(binary.Right.ResultType) is null))
                {
                    throw new ArgumentException(
                        $"Projection comparison operator '{binary.Operator}' requires compatible operands and a Boolean or nullable-Boolean result.",
                        nameof(binary));
                }
                break;
            case QueryPlanProjectionBinaryOperator.Add:
                if (!IsSupportedProjectionAdd(
                        binary.Left.ResultType,
                        binary.Right.ResultType,
                        binary.ResultType))
                {
                    throw new ArgumentException(
                        $"Projection Add does not support operand types '{binary.Left.ResultType}' and '{binary.Right.ResultType}'.",
                        nameof(binary));
                }
                break;
            default:
                throw new ArgumentException(
                    $"Unknown projection binary operator '{binary.Operator}'.",
                    nameof(binary));
        }
    }

    private static bool IsSupportedProjectionAdd(
        Type leftType,
        Type rightType,
        Type resultType)
    {
        var leftUnderlying = Nullable.GetUnderlyingType(leftType);
        var rightUnderlying = Nullable.GetUnderlyingType(rightType);
        var resultUnderlying = Nullable.GetUnderlyingType(resultType);
        var nonNullableLeft = leftUnderlying ?? leftType;
        var nonNullableRight = rightUnderlying ?? rightType;
        var nonNullableResult = resultUnderlying ?? resultType;

        if (nonNullableLeft == typeof(string) || nonNullableRight == typeof(string))
            return resultType == typeof(string);

        var isLifted = leftUnderlying is not null || rightUnderlying is not null;
        return IsProjectionNumericType(nonNullableLeft) &&
            nonNullableLeft == nonNullableRight &&
            nonNullableLeft == nonNullableResult &&
            (isLifted == (resultUnderlying is not null));
    }

    private static bool IsValidProjectionNot(Type operandType, Type resultType)
        => operandType == resultType &&
           (resultType == typeof(bool) || resultType == typeof(bool?));

    private static bool AreProjectionOperandTypesCompatible(Type leftType, Type rightType)
        => GetNonNullableType(leftType) == GetNonNullableType(rightType);

    private static bool IsProjectionNumericType(Type type)
    {
        if (type.IsEnum)
            return false;

        return Type.GetTypeCode(type) is
            TypeCode.Byte or
            TypeCode.SByte or
            TypeCode.Int16 or
            TypeCode.UInt16 or
            TypeCode.Int32 or
            TypeCode.UInt32 or
            TypeCode.Int64 or
            TypeCode.UInt64 or
            TypeCode.Single or
            TypeCode.Double or
            TypeCode.Decimal;
    }

    private static void ValidateProjectionSupportedMember(
        QueryPlanProjectionRecipe.SupportedMember member)
    {
        switch (member.Member)
        {
            case QueryPlanProjectionSupportedMemberKind.NullableHasValue:
                if (Nullable.GetUnderlyingType(member.Instance.ResultType) is null ||
                    member.ResultType != typeof(bool))
                {
                    throw new ArgumentException(
                        "NullableHasValue projection members require a nullable instance and Boolean result.",
                        nameof(member));
                }
                break;
            case QueryPlanProjectionSupportedMemberKind.NullableValue:
                var underlyingType = Nullable.GetUnderlyingType(member.Instance.ResultType);
                if (underlyingType is null || member.ResultType != underlyingType)
                {
                    throw new ArgumentException(
                        "NullableValue projection members require a nullable instance and its underlying result type.",
                        nameof(member));
                }
                break;
            case QueryPlanProjectionSupportedMemberKind.StringLength:
                if (GetNonNullableType(member.Instance.ResultType) != typeof(string) ||
                    member.ResultType != typeof(int))
                {
                    throw new ArgumentException(
                        "StringLength projection members require a String instance and Int32 result.",
                        nameof(member));
                }
                break;
            default:
                throw new ArgumentException(
                    $"Unknown supported projection member '{member.Member}'.",
                    nameof(member));
        }
    }

    private static void ValidateProjectionConditional(
        QueryPlanProjectionRecipe.Conditional conditional)
    {
        if (conditional.Test.ResultType != typeof(bool))
            throw new ArgumentException("Projection conditional tests must be Boolean.", nameof(conditional));

        if (!IsRecipeTypeCompatible(conditional.ResultType, conditional.IfTrue.ResultType) ||
            !IsRecipeTypeCompatible(conditional.ResultType, conditional.IfFalse.ResultType))
        {
            throw new ArgumentException(
                "Projection conditional branch types must be compatible with the conditional result type.",
                nameof(conditional));
        }
    }

    private static void ValidateProjectionArray(QueryPlanProjectionRecipe.NewArray newArray)
    {
        if (!newArray.ResultType.IsArray ||
            newArray.ResultType.GetElementType() != newArray.ElementType)
        {
            throw new ArgumentException(
                "Projection array recipe result type does not match its element type.",
                nameof(newArray));
        }

        foreach (var element in newArray.Elements)
        {
            if (!IsRecipeTypeCompatible(newArray.ElementType, element.ResultType))
            {
                throw new ArgumentException(
                    $"Projection array element type '{element.ResultType}' is incompatible with '{newArray.ElementType}'.",
                    nameof(newArray));
            }
        }
    }

    private static void ValidateCompatibilityConstructor(
        QueryPlanProjectionRecipe.CompatibilityConstructor constructor)
    {
        ValidateProjectionConstructor(
            constructor.Constructor,
            constructor.ResultType,
            constructor.Arguments.Select(static argument => argument.ResultType).ToArray(),
            "Compatibility constructor");
    }

    private static void ValidateProjectionConstructor(
        System.Reflection.ConstructorInfo constructor,
        Type resultType,
        IReadOnlyList<Type> argumentTypes,
        string context)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(argumentTypes);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        var declaringType = constructor.DeclaringType
            ?? throw new ArgumentException($"{context} has no declaring type.", nameof(constructor));
        if (declaringType != resultType)
        {
            throw new ArgumentException(
                $"{context} type '{declaringType}' does not match result type '{resultType}'.",
                nameof(constructor));
        }

        var parameters = constructor.GetParameters();
        if (parameters.Length != argumentTypes.Count)
        {
            throw new ArgumentException(
                $"{context} expects {parameters.Length} arguments but the projection contains {argumentTypes.Count}.",
                nameof(constructor));
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            if (!IsRecipeTypeCompatible(parameters[index].ParameterType, argumentTypes[index]))
            {
                throw new ArgumentException(
                    $"{context} argument {index} has projection type '{argumentTypes[index]}', " +
                    $"expected '{parameters[index].ParameterType}'.",
                    nameof(constructor));
            }
        }
    }

    private static void ValidateCompatibilityMember(
        QueryPlanProjectionRecipe.CompatibilityMember member)
    {
        var (isStatic, memberType) = member.Member switch
        {
            System.Reflection.FieldInfo field => (field.IsStatic, field.FieldType),
            System.Reflection.PropertyInfo property when property.GetMethod is not null =>
                (property.GetMethod.IsStatic, property.PropertyType),
            System.Reflection.PropertyInfo => throw new ArgumentException(
                $"Compatibility property '{member.Member.Name}' has no getter.",
                nameof(member)),
            _ => throw new ArgumentException(
                $"Compatibility member '{member.Member.Name}' is not a field or property.",
                nameof(member))
        };

        if (isStatic != (member.Instance is null))
        {
            throw new ArgumentException(
                $"Compatibility member '{member.Member.Name}' has an invalid static/instance recipe shape.",
                nameof(member));
        }

        if (!isStatic &&
            member.Member.DeclaringType is { } declaringType &&
            member.Instance is { } instance &&
            !declaringType.IsAssignableFrom(GetNonNullableType(instance.ResultType)))
        {
            throw new ArgumentException(
                $"Compatibility member instance type '{instance.ResultType}' is not assignable to '{declaringType}'.",
                nameof(member));
        }

        if (memberType != member.ResultType)
        {
            throw new ArgumentException(
                $"Compatibility member type '{memberType}' does not match recipe result type '{member.ResultType}'.",
                nameof(member));
        }
    }

    private static bool IsRecipeTypeCompatible(Type expectedType, Type actualType)
        => expectedType.IsAssignableFrom(actualType) ||
           Nullable.GetUnderlyingType(expectedType) == actualType;

    private static Type GetNonNullableType(Type type)
        => Nullable.GetUnderlyingType(type) ?? type;

    private static void ValidateProjectionIntrinsic(QueryPlanProjectionRecipe.Intrinsic intrinsic)
    {
        if (intrinsic.IntrinsicKind == QueryPlanProjectionIntrinsicKind.Null &&
            intrinsic.ResultType.IsValueType &&
            Nullable.GetUnderlyingType(intrinsic.ResultType) is null)
        {
            throw new ArgumentException(
                $"Null projection intrinsics require a reference or nullable result type, not '{intrinsic.ResultType}'.",
                nameof(intrinsic));
        }

        if ((intrinsic.IntrinsicKind is QueryPlanProjectionIntrinsicKind.BooleanTrue or
            QueryPlanProjectionIntrinsicKind.BooleanFalse) &&
            intrinsic.ResultType != typeof(bool))
        {
            throw new ArgumentException(
                $"Boolean projection intrinsics require result type '{typeof(bool)}', not '{intrinsic.ResultType}'.",
                nameof(intrinsic));
        }
    }

    private static void ValidateProjectionFunction(QueryPlanProjectionRecipe.Function function)
    {
        var expectedCount = function.FunctionKind switch
        {
            QueryPlanProjectionFunctionKind.StringSubstring => function.Arguments.Count is 2 or 3
                ? function.Arguments.Count
                : -1,
            QueryPlanProjectionFunctionKind.StringTrim or
            QueryPlanProjectionFunctionKind.StringToUpper or
            QueryPlanProjectionFunctionKind.StringToLower or
            QueryPlanProjectionFunctionKind.DatePartYear or
            QueryPlanProjectionFunctionKind.DatePartMonth or
            QueryPlanProjectionFunctionKind.DatePartDay or
            QueryPlanProjectionFunctionKind.DatePartDayOfYear or
            QueryPlanProjectionFunctionKind.DatePartDayOfWeek or
            QueryPlanProjectionFunctionKind.TimePartHour or
            QueryPlanProjectionFunctionKind.TimePartMinute or
            QueryPlanProjectionFunctionKind.TimePartSecond or
            QueryPlanProjectionFunctionKind.TimePartMillisecond => 1,
            _ => -1
        };

        if (expectedCount < 0 || function.Arguments.Count != expectedCount)
        {
            throw new ArgumentException(
                $"Projection function '{function.FunctionKind}' has invalid argument count {function.Arguments.Count}.",
                nameof(function));
        }


        var sourceType = GetNonNullableType(function.Arguments[0].ResultType);
        switch (function.FunctionKind)
        {
            case QueryPlanProjectionFunctionKind.StringTrim:
            case QueryPlanProjectionFunctionKind.StringToUpper:
            case QueryPlanProjectionFunctionKind.StringToLower:
                if (sourceType != typeof(string) || function.ResultType != typeof(string))
                    throw InvalidProjectionFunctionTypes(function);
                break;
            case QueryPlanProjectionFunctionKind.StringSubstring:
                if (sourceType != typeof(string) ||
                    function.ResultType != typeof(string) ||
                    function.Arguments.Skip(1).Any(argument => GetNonNullableType(argument.ResultType) != typeof(int)))
                {
                    throw InvalidProjectionFunctionTypes(function);
                }
                break;
            case QueryPlanProjectionFunctionKind.DatePartYear:
            case QueryPlanProjectionFunctionKind.DatePartMonth:
            case QueryPlanProjectionFunctionKind.DatePartDay:
            case QueryPlanProjectionFunctionKind.DatePartDayOfYear:
                if ((sourceType != typeof(DateTime) && sourceType != typeof(DateOnly)) ||
                    function.ResultType != typeof(int))
                {
                    throw InvalidProjectionFunctionTypes(function);
                }
                break;
            case QueryPlanProjectionFunctionKind.DatePartDayOfWeek:
                if ((sourceType != typeof(DateTime) && sourceType != typeof(DateOnly)) ||
                    function.ResultType != typeof(DayOfWeek))
                {
                    throw InvalidProjectionFunctionTypes(function);
                }
                break;
            case QueryPlanProjectionFunctionKind.TimePartHour:
            case QueryPlanProjectionFunctionKind.TimePartMinute:
            case QueryPlanProjectionFunctionKind.TimePartSecond:
            case QueryPlanProjectionFunctionKind.TimePartMillisecond:
                if ((sourceType != typeof(DateTime) && sourceType != typeof(TimeOnly)) ||
                    function.ResultType != typeof(int))
                {
                    throw InvalidProjectionFunctionTypes(function);
                }
                break;
            default:
                throw new ArgumentException(
                    $"Unknown projection function '{function.FunctionKind}'.",
                    nameof(function));
        }
    }

    private static ArgumentException InvalidProjectionFunctionTypes(
        QueryPlanProjectionRecipe.Function function)
        => new(
            $"Projection function '{function.FunctionKind}' has incompatible argument or result types.",
            nameof(function));

    private static void ValidateMembers(
        IReadOnlyList<QueryPlanProjectionMember> members,
        QueryPlanBindingDeclarations declarations)
    {
        foreach (var member in members)
            ValidateValue(member.Value, declarations);
    }

    private static void ValidateValue(
        QueryPlanValue value,
        QueryPlanBindingDeclarations declarations)
    {
        switch (value)
        {
            case QueryPlanColumnValue:
                break;
            case QueryPlanIntrinsicValue intrinsic:
                ValidateIntrinsic(intrinsic);
                break;
            case QueryPlanScalarBindingReference scalar:
                ValidateReference(
                    scalar.BindingId,
                    QueryPlanBindingKind.Scalar,
                    scalar.ClrType,
                    declarations);
                break;
            case QueryPlanLocalSequenceBindingReference sequence:
                ValidateReference(
                    sequence.BindingId,
                    QueryPlanBindingKind.LocalSequence,
                    sequence.ElementType,
                    declarations);
                break;
            case QueryPlanFunctionValue function:
                ValidateFunction(function);
                foreach (var argument in function.Arguments)
                    ValidateValue(argument, declarations);
                break;
            case QueryPlanConvertedValue converted:
                ValidateValue(converted.Value, declarations);
                break;
            case QueryPlanGroupKeyValue groupKey:
                ValidateValue(groupKey.Key, declarations);
                break;
            case QueryPlanGroupedAggregateValue groupedAggregate when groupedAggregate.Selector is not null:
                ValidateValue(groupedAggregate.Selector, declarations);
                break;
            case QueryPlanGroupedAggregateValue:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan value '{value.GetType().Name}'.",
                    nameof(value));
        }
    }

    private static void ValidateFunction(QueryPlanFunctionValue function)
    {
        var hasValidArgumentCount = function.Function switch
        {
            QueryPlanFunctionKind.StringStartsWith or
            QueryPlanFunctionKind.StringEndsWith or
            QueryPlanFunctionKind.StringContains => function.Arguments.Count == 2,
            QueryPlanFunctionKind.StringSubstring => function.Arguments.Count is 2 or 3,
            QueryPlanFunctionKind.StringIsNullOrEmpty or
            QueryPlanFunctionKind.StringIsNullOrWhiteSpace or
            QueryPlanFunctionKind.StringLength or
            QueryPlanFunctionKind.StringTrim or
            QueryPlanFunctionKind.StringToUpper or
            QueryPlanFunctionKind.StringToLower or
            QueryPlanFunctionKind.DatePartYear or
            QueryPlanFunctionKind.DatePartMonth or
            QueryPlanFunctionKind.DatePartDay or
            QueryPlanFunctionKind.DatePartDayOfYear or
            QueryPlanFunctionKind.DatePartDayOfWeek or
            QueryPlanFunctionKind.TimePartHour or
            QueryPlanFunctionKind.TimePartMinute or
            QueryPlanFunctionKind.TimePartSecond or
            QueryPlanFunctionKind.TimePartMillisecond => function.Arguments.Count == 1,
            _ => false
        };

        if (!hasValidArgumentCount)
        {
            throw new ArgumentException(
                $"Query plan function '{function.Function}' has invalid argument count {function.Arguments.Count}.",
                nameof(function));
        }
    }

    private static void ValidateIntrinsic(QueryPlanIntrinsicValue intrinsic)
    {
        if (!Enum.IsDefined(intrinsic.Intrinsic))
        {
            throw new ArgumentException(
                $"Query plan intrinsic kind '{intrinsic.Intrinsic}' is not defined.",
                nameof(intrinsic));
        }

        if (intrinsic.ClrType == typeof(void) ||
            intrinsic.ClrType.IsByRef ||
            intrinsic.ClrType.IsPointer ||
            intrinsic.ClrType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"Query plan intrinsic '{intrinsic.Intrinsic}' has invalid CLR type '{intrinsic.ClrType}'.",
                nameof(intrinsic));
        }

        if (intrinsic.Intrinsic == QueryPlanIntrinsicKind.Null &&
            intrinsic.ClrType.IsValueType &&
            Nullable.GetUnderlyingType(intrinsic.ClrType) is null)
        {
            throw new ArgumentException(
                $"Null query plan intrinsics require a reference or nullable CLR type, not '{intrinsic.ClrType}'.",
                nameof(intrinsic));
        }

        if (intrinsic.Intrinsic is QueryPlanIntrinsicKind.BooleanTrue or QueryPlanIntrinsicKind.BooleanFalse &&
            intrinsic.ClrType != typeof(bool))
        {
            throw new ArgumentException(
                $"Boolean query plan intrinsic '{intrinsic.Intrinsic}' requires CLR type '{typeof(bool)}', not '{intrinsic.ClrType}'.",
                nameof(intrinsic));
        }
    }

    private static void ValidateSourceReferences(QueryPlanTemplate template)
    {
        var sourcesById = template.Sources.ToDictionary(static source => source.Id, StringComparer.Ordinal);

        foreach (var operation in template.Operations)
            ValidateOperationSources(operation, sourcesById);

        ValidateProjectionSources(template.Projection, sourcesById);
        if (template.Result.AggregateSelector is not null)
            ValidateValueSources(template.Result.AggregateSelector, sourcesById);
    }

    private static void ValidateOperationSources(
        QueryPlanOperation operation,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (operation)
        {
            case QueryPlanOperation.Where where:
                ValidatePredicateSources(where.Predicate, sourcesById);
                break;
            case QueryPlanOperation.Having having:
                ValidatePredicateSources(having.Predicate, sourcesById);
                break;
            case QueryPlanOperation.OrderBy orderBy:
                foreach (var ordering in orderBy.Orderings)
                    ValidateValueSources(ordering.Value, sourcesById);
                break;
            case QueryPlanOperation.Skip skip:
                ValidateValueSources(skip.Count, sourcesById);
                break;
            case QueryPlanOperation.Take take:
                ValidateValueSources(take.Count, sourcesById);
                break;
            case QueryPlanOperation.Join join:
                ValidateSource(join.JoinShape.LeftSource, sourcesById);
                ValidateColumn(join.JoinShape.LeftSource, join.JoinShape.LeftColumn);
                ValidateSource(join.JoinShape.RightSource, sourcesById);
                ValidateColumn(join.JoinShape.RightSource, join.JoinShape.RightColumn);
                break;
            case QueryPlanOperation.Pushdown pushdown:
                foreach (var innerOperation in pushdown.Operations)
                    ValidateOperationSources(innerOperation, sourcesById);
                foreach (var ordering in pushdown.PreservedOrderings)
                    ValidateValueSources(ordering.Value, sourcesById);
                break;
            case QueryPlanOperation.GroupBy groupBy:
                foreach (var key in groupBy.Keys)
                    ValidateValueSources(key, sourcesById);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan operation '{operation.GetType().Name}'.",
                    nameof(operation));
        }
    }

    private static void ValidatePredicateSources(
        QueryPlanPredicate predicate,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (predicate)
        {
            case QueryPlanPredicate.Fixed:
                break;
            case QueryPlanPredicate.And and:
                foreach (var term in and.Terms)
                    ValidatePredicateSources(term, sourcesById);
                break;
            case QueryPlanPredicate.Or or:
                foreach (var term in or.Terms)
                    ValidatePredicateSources(term, sourcesById);
                break;
            case QueryPlanPredicate.Not not:
                ValidatePredicateSources(not.Predicate, sourcesById);
                break;
            case QueryPlanPredicate.Compare compare:
                ValidateValueSources(compare.Left, sourcesById);
                ValidateValueSources(compare.Right, sourcesById);
                break;
            case QueryPlanPredicate.In inPredicate:
                ValidateValueSources(inPredicate.Item, sourcesById);
                ValidateValueSources(inPredicate.Sequence, sourcesById);
                break;
            case QueryPlanPredicate.Exists exists:
                ValidateSource(exists.ParentSource, sourcesById);
                ValidateSource(exists.ChildSource, sourcesById);
                if (exists.Predicate is not null)
                    ValidatePredicateSources(exists.Predicate, sourcesById);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan predicate '{predicate.GetType().Name}'.",
                    nameof(predicate));
        }
    }

    private static void ValidateProjectionSources(
        QueryPlanProjection projection,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (projection)
        {
            case QueryPlanProjection.Entity entity:
                ValidateSource(entity.Source, sourcesById);
                break;
            case QueryPlanProjection.ScalarMember scalar:
                ValidateSource(scalar.Source, sourcesById);
                ValidateColumn(scalar.Source, scalar.Column);
                break;
            case QueryPlanProjection.Anonymous anonymous:
                ValidateProjectionMemberSources(anonymous.Members, sourcesById);
                ValidateSources(anonymous.Sources, sourcesById);
                ValidateProjectionRecipeSources(anonymous.Recipe, sourcesById);
                break;
            case QueryPlanProjection.ComputedRowLocal computed:
                ValidateSources(computed.Sources, sourcesById);
                ValidateProjectionRecipeSources(computed.Recipe, sourcesById);
                break;
            case QueryPlanProjection.JoinedRowLocal joined:
                ValidateProjectionMemberSources(joined.Members, sourcesById);
                ValidateSources(joined.Sources, sourcesById);
                ValidateProjectionRecipeSources(joined.Recipe, sourcesById);
                break;
            case QueryPlanProjection.SqlRow sqlRow:
                ValidateProjectionMemberSources(sqlRow.Members, sourcesById);
                break;
            case QueryPlanProjection.TransparentIdentifier transparent:
                ValidateSources(transparent.SourcesByMember.Values, sourcesById);
                break;
            case QueryPlanProjection.GroupedAggregate grouped:
                ValidateSource(grouped.Source, sourcesById);
                ValidateProjectionMemberSources(grouped.Members, sourcesById);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan projection '{projection.GetType().Name}'.",
                    nameof(projection));
        }
    }

    private static void ValidateProjectionRecipeSources(
        QueryPlanProjectionRecipe recipe,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (recipe)
        {
            case QueryPlanProjectionRecipe.Source source:
                ValidateSource(source.SourceSlot, sourcesById);
                break;
            case QueryPlanProjectionRecipe.SourceColumn column:
                ValidateSource(column.SourceSlot, sourcesById);
                ValidateColumn(column.SourceSlot, column.Column);
                break;
            case QueryPlanProjectionRecipe.Convert convert:
                ValidateProjectionRecipeSources(convert.Operand, sourcesById);
                break;
            case QueryPlanProjectionRecipe.Not not:
                ValidateProjectionRecipeSources(not.Operand, sourcesById);
                break;
            case QueryPlanProjectionRecipe.Binary binary:
                ValidateProjectionRecipeSources(binary.Left, sourcesById);
                ValidateProjectionRecipeSources(binary.Right, sourcesById);
                break;
            case QueryPlanProjectionRecipe.SupportedMember member:
                ValidateProjectionRecipeSources(member.Instance, sourcesById);
                break;
            case QueryPlanProjectionRecipe.Function function:
                foreach (var argument in function.Arguments)
                    ValidateProjectionRecipeSources(argument, sourcesById);
                break;
            case QueryPlanProjectionRecipe.Conditional conditional:
                ValidateProjectionRecipeSources(conditional.Test, sourcesById);
                ValidateProjectionRecipeSources(conditional.IfTrue, sourcesById);
                ValidateProjectionRecipeSources(conditional.IfFalse, sourcesById);
                break;
            case QueryPlanProjectionRecipe.NewArray newArray:
                foreach (var element in newArray.Elements)
                    ValidateProjectionRecipeSources(element, sourcesById);
                break;
            case QueryPlanProjectionRecipe.CompatibilityConstructor constructor:
                foreach (var argument in constructor.Arguments)
                    ValidateProjectionRecipeSources(argument, sourcesById);
                break;
            case QueryPlanProjectionRecipe.CompatibilityMember member when member.Instance is not null:
                ValidateProjectionRecipeSources(member.Instance, sourcesById);
                break;
            case QueryPlanProjectionRecipe.ScalarBinding:
            case QueryPlanProjectionRecipe.Intrinsic:
            case QueryPlanProjectionRecipe.CompatibilityMember:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown projection recipe '{recipe.GetType().Name}'.",
                    nameof(recipe));
        }
    }

    private static void ValidateProjectionMemberSources(
        IReadOnlyList<QueryPlanProjectionMember> members,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        foreach (var member in members)
            ValidateValueSources(member.Value, sourcesById);
    }

    private static void ValidateValueSources(
        QueryPlanValue value,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        switch (value)
        {
            case QueryPlanColumnValue column:
                ValidateSource(column.Source, sourcesById);
                ValidateColumn(column.Source, column.Column);
                break;
            case QueryPlanFunctionValue function:
                foreach (var argument in function.Arguments)
                    ValidateValueSources(argument, sourcesById);
                break;
            case QueryPlanConvertedValue converted:
                ValidateValueSources(converted.Value, sourcesById);
                break;
            case QueryPlanGroupKeyValue groupKey:
                ValidateValueSources(groupKey.Key, sourcesById);
                break;
            case QueryPlanGroupedAggregateValue groupedAggregate when groupedAggregate.Selector is not null:
                ValidateValueSources(groupedAggregate.Selector, sourcesById);
                break;
            case QueryPlanIntrinsicValue:
            case QueryPlanScalarBindingReference:
            case QueryPlanLocalSequenceBindingReference:
            case QueryPlanGroupedAggregateValue:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown query plan value '{value.GetType().Name}'.",
                    nameof(value));
        }
    }

    private static void ValidateSources(
        IEnumerable<QueryPlanSourceSlot> sources,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        foreach (var source in sources)
            ValidateSource(source, sourcesById);
    }

    private static void ValidateSource(
        QueryPlanSourceSlot source,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> sourcesById)
    {
        if (!sourcesById.TryGetValue(source.Id, out var declared))
        {
            throw new ArgumentException(
                $"Query plan references source slot '{source.Id}', which is not declared by the template.",
                nameof(source));
        }

        if (!ReferenceEquals(declared, source))
        {
            throw new ArgumentException(
                $"Query plan source reference '{source.Id}' does not match the source slot declared by the template.",
                nameof(source));
        }
    }

    private static void ValidateColumn(
        QueryPlanSourceSlot source,
        DataLinq.Metadata.ColumnDefinition column)
    {
        if (!ReferenceEquals(source.Table, column.Table))
        {
            throw new ArgumentException(
                $"Query plan column '{column.DbName}' does not belong to source slot '{source.Id}' table '{source.Table.DbName}'.",
                nameof(column));
        }
    }

    private static void ValidateReference(
        string bindingId,
        QueryPlanBindingKind expectedKind,
        Type expectedType,
        QueryPlanBindingDeclarations declarations)
    {
        if (!declarations.TryGet(bindingId, out var declaration))
        {
            throw new ArgumentException(
                $"Query plan value references undeclared binding '{bindingId}'.",
                nameof(declarations));
        }

        if (declaration.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Query plan binding reference '{bindingId}' has kind '{expectedKind}', " +
                $"but its declaration has kind '{declaration.Kind}'.",
                nameof(declarations));
        }

        if (declaration.ModelType != expectedType)
        {
            throw new ArgumentException(
                $"Query plan binding reference '{bindingId}' expects model type '{expectedType}', " +
                $"but its declaration uses '{declaration.ModelType}'.",
                nameof(declarations));
        }
    }
}
