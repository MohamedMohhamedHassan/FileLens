namespace FileLens.UnitTests;

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

    public static void Contains(string expectedSubstring, string? actualString)
    {
        if (actualString is null || !actualString.Contains(expectedSubstring, StringComparison.Ordinal))
            throw new Exception($"Expected string to contain '{expectedSubstring}', but was '{actualString}'.");
    }

    public static void InRange<T>(T actual, T low, T high) where T : IComparable<T>
    {
        if (actual.CompareTo(low) < 0 || actual.CompareTo(high) > 0)
            throw new Exception($"Expected value in range [{low}, {high}], but was {actual}.");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineDataAttribute : Attribute
{
    public object?[] Data { get; }
    public InlineDataAttribute(params object?[] data) => Data = data;
}

