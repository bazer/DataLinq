namespace DataLinq.Linq;

internal readonly record struct ProjectionEvaluationOptions(
    bool AllowCompatibilityObjectConstruction,
    bool AllowCompatibilityMemberReflection)
{
    public static ProjectionEvaluationOptions Default { get; } = new(
        AllowCompatibilityObjectConstruction: true,
        AllowCompatibilityMemberReflection: true);

    public static ProjectionEvaluationOptions AotStrict { get; } = new(
        AllowCompatibilityObjectConstruction: false,
        AllowCompatibilityMemberReflection: false);
}
