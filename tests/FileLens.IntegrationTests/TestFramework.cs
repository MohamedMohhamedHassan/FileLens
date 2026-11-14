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
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute { }

