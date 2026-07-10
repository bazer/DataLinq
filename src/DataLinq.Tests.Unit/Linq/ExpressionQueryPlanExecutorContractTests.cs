using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;

namespace DataLinq.Tests.Unit.Linq;

public class ExpressionQueryPlanExecutorContractTests
{
    [Test]
    public async Task PostParseExecutorMethods_DoNotAcceptExpressionTreeTypes()
    {
        var violations = typeof(ExpressionQueryPlanExecutor)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters().Select(parameter => new
            {
                Method = method.Name,
                Parameter = parameter.Name,
                parameter.ParameterType
            }))
            .Where(item => ContainsExpressionTreeType(item.ParameterType))
            .Select(item => $"{item.Method}({item.Parameter}: {item.ParameterType})")
            .OrderBy(static violation => violation, StringComparer.Ordinal)
            .ToArray();

        if (violations.Length != 0)
        {
            throw new InvalidOperationException(
                "Post-parse query execution must not receive or recover an expression tree. Violations: " +
                string.Join(", ", violations));
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task TemplateProjectionAndRecipeContracts_DoNotStoreExpressionTreeTypes()
    {
        var contractTypes = new[]
        {
            typeof(QueryPlanTemplate),
            typeof(QueryPlanProjection),
            typeof(QueryPlanProjectionRecipe)
        }
        .SelectMany(type => new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
        .Distinct()
        .ToArray();

        var fieldViolations = contractTypes
            .SelectMany(type => type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(field => ContainsExpressionTreeType(field.FieldType))
                .Select(field => $"{type.FullName}.{field.Name}: {field.FieldType}"));
        var propertyViolations = contractTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => ContainsExpressionTreeType(property.PropertyType))
                .Select(property => $"{type.FullName}.{property.Name}: {property.PropertyType}"));
        var violations = fieldViolations
            .Concat(propertyViolations)
            .OrderBy(static violation => violation, StringComparer.Ordinal)
            .ToArray();

        if (violations.Length != 0)
        {
            throw new InvalidOperationException(
                "Post-parse query contracts must not retain expression-tree state. Violations: " +
                string.Join(", ", violations));
        }

        await Assert.That(violations).IsEmpty();
    }

    private static bool ContainsExpressionTreeType(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
            return ContainsExpressionTreeType(type.GetElementType()!);

        if (typeof(Expression).IsAssignableFrom(type))
            return true;

        return type.IsGenericType && type.GetGenericArguments().Any(ContainsExpressionTreeType);
    }
}
