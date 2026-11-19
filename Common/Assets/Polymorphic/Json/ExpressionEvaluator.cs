using Vintagestory.API.Common.Entities;

namespace Vintagestory.API.Common;

public interface IExpressionEvaluator
{
    /// <summary>
    /// Parses an expression.
    /// </summary>
    /// <param name="entity">The source entity to use for variable lookups.</param>
    /// <param name="expression">The expression to parse.</param>
    /// <typeparam name="T">The expected type of the attribute, if found.</typeparam>
    /// <returns>Returns the resolved attribute value of type T, or null.</returns>
    T? ParseExpression<T>(Entity entity, string expression) where T : unmanaged;
}

public static class ExpressionEvaluator
{
    public static IExpressionEvaluator? Instance;
}
