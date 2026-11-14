namespace FileLens.IntegrationTests;

public static class Assert
{
    public static void True(bool condition, string? userMessage = null)
    {
        if (!condition)
            throw new Exception(userMessage ?? "Expected true but was false.");
    }

    public static void False(bool condition, string? userMessage = null)
    {
        if (condition)
            throw new Exception(userMessage ?? "Expected false but was true.");
    }

    public static void Equal<T>(T? expected, T? actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected: {expected}, Actual: {actual}");
    }

    public static void NotNull(object? value)
    {
        if (value is null)
            throw new Exception("Expected not null but was null.");
    }

    public static void Null(object? value)
    {
        if (value is not null)
            throw new Exception($"Expected null but was {value}.");
    }

    public static void Contains(string substring, string fullString)
    {
        if (!fullString.Contains(substring, StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Expected string to contain '{substring}', but was: '{fullString}'");
    }

    public static void Contains<T>(IEnumerable<T> collection, Func<T, bool> predicate)
    {
        if (!collection.Any(predicate))
            throw new Exception("Expected collection to contain matching item, but none matched.");
    }

    public static void DoesNotContain<T>(IEnumerable<T> collection, Func<T, bool> predicate)
    {
        if (collection.Any(predicate))
            throw new Exception("Expected collection NOT to contain matching item, but found match.");
    }

    public static void Empty<T>(IEnumerable<T> collection)
    {
        if (collection.Any())
            throw new Exception("Expected empty collection, but had elements.");
    }

    public static void NotEmpty<T>(IEnumerable<T> collection)
    {
        if (!collection.Any())
            throw new Exception("Expected non-empty collection, but was empty.");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class FactAttribute : Attribute { }

/// <summary>
/// Marks a test that must run on a dedicated STA thread (required for WPF UI components).
/// Implies [Fact] — the runner treats it as a Fact but dispatches to STA.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StaFactAttribute : FactAttribute { }
