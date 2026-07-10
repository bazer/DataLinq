using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal abstract record QueryPlanProjectionRecipe
{
    protected QueryPlanProjectionRecipe(
        QueryPlanProjectionRecipeKind kind,
        Type resultType,
        QueryPlanProjectionDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        Kind = kind;
        ResultType = resultType;
        Disposition = disposition;
    }

    public QueryPlanProjectionRecipeKind Kind { get; }

    public Type ResultType { get; }

    public QueryPlanProjectionDisposition Disposition { get; }

    public sealed record Source : QueryPlanProjectionRecipe
    {
        public Source(QueryPlanSourceSlot sourceSlot)
            : base(QueryPlanProjectionRecipeKind.Source, SourceType(sourceSlot), QueryPlanProjectionDisposition.AotSafe)
        {
            SourceSlot = sourceSlot;
        }

        public QueryPlanSourceSlot SourceSlot { get; }

        private static Type SourceType(QueryPlanSourceSlot sourceSlot)
        {
            ArgumentNullException.ThrowIfNull(sourceSlot);
            return sourceSlot.ElementType;
        }
    }

    public sealed record SourceColumn : QueryPlanProjectionRecipe
    {
        public SourceColumn(QueryPlanSourceSlot source, ColumnDefinition column, Type resultType)
            : base(QueryPlanProjectionRecipeKind.SourceColumn, resultType, QueryPlanProjectionDisposition.AotSafe)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(column);
            SourceSlot = source;
            Column = column;
        }

        public QueryPlanSourceSlot SourceSlot { get; }

        public ColumnDefinition Column { get; }
    }

    public sealed record ScalarBinding : QueryPlanProjectionRecipe
    {
        public ScalarBinding(string bindingId, Type resultType)
            : base(QueryPlanProjectionRecipeKind.ScalarBinding, resultType, QueryPlanProjectionDisposition.AotSafe)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
            BindingId = bindingId;
        }

        public string BindingId { get; }
    }

    public sealed record Intrinsic : QueryPlanProjectionRecipe
    {
        public Intrinsic(QueryPlanProjectionIntrinsicKind intrinsic, Type resultType)
            : base(QueryPlanProjectionRecipeKind.Intrinsic, resultType, QueryPlanProjectionDisposition.AotSafe)
        {
            if (!Enum.IsDefined(intrinsic))
                throw new ArgumentOutOfRangeException(nameof(intrinsic), intrinsic, "Unknown projection intrinsic kind.");

            IntrinsicKind = intrinsic;
        }

        public QueryPlanProjectionIntrinsicKind IntrinsicKind { get; }
    }

    public sealed record Convert : QueryPlanProjectionRecipe
    {
        public Convert(QueryPlanProjectionRecipe operand, Type resultType)
            : base(QueryPlanProjectionRecipeKind.Convert, resultType, RecipeDisposition(operand))
        {
            Operand = operand;
        }

        public QueryPlanProjectionRecipe Operand { get; }
    }

    public sealed record Not : QueryPlanProjectionRecipe
    {
        public Not(QueryPlanProjectionRecipe operand, Type resultType)
            : base(QueryPlanProjectionRecipeKind.Not, resultType, RecipeDisposition(operand))
        {
            Operand = operand;
        }

        public QueryPlanProjectionRecipe Operand { get; }
    }

    public sealed record Binary : QueryPlanProjectionRecipe
    {
        public Binary(
            QueryPlanProjectionBinaryOperator @operator,
            QueryPlanProjectionRecipe left,
            QueryPlanProjectionRecipe right,
            Type resultType)
            : base(
                QueryPlanProjectionRecipeKind.Binary,
                resultType,
                CompositeDisposition(left, right))
        {
            if (!Enum.IsDefined(@operator))
                throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "Unknown projection binary operator.");

            Operator = @operator;
            Left = left;
            Right = right;
        }

        public QueryPlanProjectionBinaryOperator Operator { get; }

        public QueryPlanProjectionRecipe Left { get; }

        public QueryPlanProjectionRecipe Right { get; }
    }

    public sealed record SupportedMember : QueryPlanProjectionRecipe
    {
        public SupportedMember(
            QueryPlanProjectionSupportedMemberKind member,
            QueryPlanProjectionRecipe instance,
            Type resultType)
            : base(QueryPlanProjectionRecipeKind.SupportedMember, resultType, RecipeDisposition(instance))
        {
            if (!Enum.IsDefined(member))
                throw new ArgumentOutOfRangeException(nameof(member), member, "Unknown supported projection member.");

            Member = member;
            Instance = instance;
        }

        public QueryPlanProjectionSupportedMemberKind Member { get; }

        public QueryPlanProjectionRecipe Instance { get; }
    }

    public sealed record Function : QueryPlanProjectionRecipe
    {
        public Function(
            QueryPlanProjectionFunctionKind function,
            IEnumerable<QueryPlanProjectionRecipe> arguments,
            Type resultType)
            : this(function, Freeze(arguments, nameof(arguments)), resultType)
        {
        }

        private Function(
            QueryPlanProjectionFunctionKind function,
            IReadOnlyList<QueryPlanProjectionRecipe> arguments,
            Type resultType)
            : base(QueryPlanProjectionRecipeKind.Function, resultType, CompositeDisposition(arguments))
        {
            if (!Enum.IsDefined(function))
                throw new ArgumentOutOfRangeException(nameof(function), function, "Unknown projection function.");
            if (arguments.Count == 0)
                throw new ArgumentException("Projection functions must contain at least one argument.", nameof(arguments));

            FunctionKind = function;
            Arguments = arguments;
        }

        public QueryPlanProjectionFunctionKind FunctionKind { get; }

        public IReadOnlyList<QueryPlanProjectionRecipe> Arguments { get; }
    }

    public sealed record Conditional : QueryPlanProjectionRecipe
    {
        public Conditional(
            QueryPlanProjectionRecipe test,
            QueryPlanProjectionRecipe ifTrue,
            QueryPlanProjectionRecipe ifFalse,
            Type resultType)
            : base(
                QueryPlanProjectionRecipeKind.Conditional,
                resultType,
                CompositeDisposition(test, ifTrue, ifFalse))
        {
            Test = test;
            IfTrue = ifTrue;
            IfFalse = ifFalse;
        }

        public QueryPlanProjectionRecipe Test { get; }

        public QueryPlanProjectionRecipe IfTrue { get; }

        public QueryPlanProjectionRecipe IfFalse { get; }
    }

    public sealed record NewArray : QueryPlanProjectionRecipe
    {
        public NewArray(
            Type elementType,
            IEnumerable<QueryPlanProjectionRecipe> elements,
            Type resultType)
            : this(elementType, Freeze(elements, nameof(elements)), resultType)
        {
        }

        private NewArray(
            Type elementType,
            IReadOnlyList<QueryPlanProjectionRecipe> elements,
            Type resultType)
            : base(QueryPlanProjectionRecipeKind.NewArray, resultType, CompositeDisposition(elements))
        {
            ArgumentNullException.ThrowIfNull(elementType);
            if (!IsSupportedArrayElementType(elementType))
            {
                throw new ArgumentException(
                    $"Projection array element type '{elementType}' is not supported by the normalized recipe evaluator.",
                    nameof(elementType));
            }

            ElementType = elementType;
            Elements = elements;
        }

        public Type ElementType { get; }

        public IReadOnlyList<QueryPlanProjectionRecipe> Elements { get; }
    }

    public sealed record CompatibilityConstructor : QueryPlanProjectionRecipe
    {
        public CompatibilityConstructor(
            ConstructorInfo constructor,
            IEnumerable<QueryPlanProjectionRecipe> arguments,
            Type resultType)
            : base(
                QueryPlanProjectionRecipeKind.CompatibilityConstructor,
                resultType,
                QueryPlanProjectionDisposition.SqlOnlyCompatibility)
        {
            ArgumentNullException.ThrowIfNull(constructor);
            Constructor = constructor;
            Arguments = Freeze(arguments, nameof(arguments));
        }

        public ConstructorInfo Constructor { get; }

        public IReadOnlyList<QueryPlanProjectionRecipe> Arguments { get; }
    }

    public sealed record CompatibilityMember : QueryPlanProjectionRecipe
    {
        public CompatibilityMember(
            MemberInfo member,
            QueryPlanProjectionRecipe? instance,
            Type resultType)
            : base(
                QueryPlanProjectionRecipeKind.CompatibilityMember,
                resultType,
                QueryPlanProjectionDisposition.SqlOnlyCompatibility)
        {
            ArgumentNullException.ThrowIfNull(member);
            if (member is not FieldInfo and not PropertyInfo)
                throw new ArgumentException("Compatibility projection members must be fields or properties.", nameof(member));

            Member = member;
            Instance = instance;
        }

        public MemberInfo Member { get; }

        public QueryPlanProjectionRecipe? Instance { get; }
    }

    internal static bool IsSupportedArrayElementType(Type elementType)
        => elementType == typeof(string) ||
           elementType == typeof(int) ||
           elementType == typeof(long) ||
           elementType == typeof(short) ||
           elementType == typeof(byte) ||
           elementType == typeof(bool) ||
           elementType == typeof(decimal) ||
           elementType == typeof(double) ||
           elementType == typeof(float) ||
           elementType == typeof(Guid) ||
           elementType == typeof(DateTime) ||
           elementType == typeof(DateOnly) ||
           elementType == typeof(TimeOnly) ||
           elementType == typeof(object);

    internal static bool IsSupportedConversion(Type sourceType, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);

        if (!IsValidRecipeType(sourceType) || !IsValidRecipeType(targetType))
            return false;

        if (sourceType == targetType || targetType.IsAssignableFrom(sourceType))
            return true;

        var sourceUnderlyingType = Nullable.GetUnderlyingType(sourceType);
        var targetUnderlyingType = Nullable.GetUnderlyingType(targetType);

        // Boxing a nullable value boxes its underlying value when one is present.
        if (sourceUnderlyingType is not null &&
            !targetType.IsValueType &&
            targetType.IsAssignableFrom(sourceUnderlyingType))
        {
            return true;
        }

        // Unwrapping a nullable value is not an implicit conversion and would
        // require a distinct recipe node with explicit null behavior.
        if (sourceUnderlyingType is not null && targetUnderlyingType is null)
            return false;

        var nonNullableSourceType = sourceUnderlyingType ?? sourceType;
        var nonNullableTargetType = targetUnderlyingType ?? targetType;
        return nonNullableSourceType == nonNullableTargetType ||
            IsImplicitNumericConversion(nonNullableSourceType, nonNullableTargetType);
    }

    private static bool IsImplicitNumericConversion(Type sourceType, Type targetType)
    {
        if (sourceType.IsEnum || targetType.IsEnum)
            return false;

        return Type.GetTypeCode(sourceType) switch
        {
            TypeCode.SByte => targetType == typeof(short) ||
                targetType == typeof(int) ||
                targetType == typeof(long) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.Byte => targetType == typeof(short) ||
                targetType == typeof(ushort) ||
                targetType == typeof(int) ||
                targetType == typeof(uint) ||
                targetType == typeof(long) ||
                targetType == typeof(ulong) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.Int16 => targetType == typeof(int) ||
                targetType == typeof(long) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.UInt16 => targetType == typeof(int) ||
                targetType == typeof(uint) ||
                targetType == typeof(long) ||
                targetType == typeof(ulong) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.Char => targetType == typeof(ushort) ||
                targetType == typeof(int) ||
                targetType == typeof(uint) ||
                targetType == typeof(long) ||
                targetType == typeof(ulong) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.Int32 => targetType == typeof(long) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.UInt32 => targetType == typeof(long) ||
                targetType == typeof(ulong) ||
                targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.Int64 or TypeCode.UInt64 => targetType == typeof(float) ||
                targetType == typeof(double) ||
                targetType == typeof(decimal),
            TypeCode.Single => targetType == typeof(double),
            _ => false
        };
    }

    private static bool IsValidRecipeType(Type type)
        => type != typeof(void) &&
           !type.IsByRef &&
           !type.IsPointer &&
           !type.ContainsGenericParameters;

    private static QueryPlanProjectionDisposition RecipeDisposition(QueryPlanProjectionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.Disposition;
    }

    private static QueryPlanProjectionDisposition CompositeDisposition(
        params QueryPlanProjectionRecipe[] recipes)
        => CompositeDisposition((IReadOnlyList<QueryPlanProjectionRecipe>)recipes);

    private static QueryPlanProjectionDisposition CompositeDisposition(
        IReadOnlyList<QueryPlanProjectionRecipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        return recipes.Any(static recipe => recipe.Disposition == QueryPlanProjectionDisposition.SqlOnlyCompatibility)
            ? QueryPlanProjectionDisposition.SqlOnlyCompatibility
            : QueryPlanProjectionDisposition.AotSafe;
    }

    private static ReadOnlyCollection<QueryPlanProjectionRecipe> Freeze(
        IEnumerable<QueryPlanProjectionRecipe> recipes,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        var array = recipes.ToArray();
        if (array.Any(static recipe => recipe is null))
            throw new ArgumentException("Projection recipe collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
    }
}

internal enum QueryPlanProjectionRecipeKind
{
    Source,
    SourceColumn,
    ScalarBinding,
    Intrinsic,
    Convert,
    Not,
    Binary,
    SupportedMember,
    Function,
    Conditional,
    NewArray,
    CompatibilityConstructor,
    CompatibilityMember
}

internal enum QueryPlanProjectionIntrinsicKind
{
    Null,
    BooleanTrue,
    BooleanFalse
}

internal enum QueryPlanProjectionBinaryOperator
{
    Add,
    Equal,
    NotEqual,
    AndAlso,
    OrElse,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

internal enum QueryPlanProjectionSupportedMemberKind
{
    NullableHasValue,
    NullableValue,
    StringLength
}

internal enum QueryPlanProjectionFunctionKind
{
    StringTrim,
    StringToUpper,
    StringToLower,
    StringSubstring,
    DatePartYear,
    DatePartMonth,
    DatePartDay,
    DatePartDayOfYear,
    DatePartDayOfWeek,
    TimePartHour,
    TimePartMinute,
    TimePartSecond,
    TimePartMillisecond
}
