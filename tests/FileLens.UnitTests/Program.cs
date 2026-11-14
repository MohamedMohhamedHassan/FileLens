using System.Reflection;

namespace FileLens.UnitTests;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine(" FileLens.UnitTests Runner");
        Console.WriteLine("========================================");

        var assembly = typeof(Program).Assembly;
        var testClasses = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && (t.Name.EndsWith("Tests") || t.GetMethods().Any(m => m.GetCustomAttribute<FactAttribute>() != null || m.GetCustomAttribute<TheoryAttribute>() != null)))
            .ToList();

        int passed = 0;
        int failed = 0;

        foreach (var testClass in testClasses)
        {
            Console.WriteLine($"\nRunning {testClass.Name}...");
            var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods)
            {
                var factAttr = method.GetCustomAttribute<FactAttribute>();
                var theoryAttr = method.GetCustomAttribute<TheoryAttribute>();

                if (factAttr == null && theoryAttr == null)
                    continue;

                if (factAttr != null)
                {
                    var instance = Activator.CreateInstance(testClass);
                    try
                    {
                        var result = method.Invoke(instance, null);
                        if (result is Task task)
                        {
                            await task;
                        }
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("  [PASS] ");
                        Console.ResetColor();
                        Console.WriteLine(method.Name);
                        passed++;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write("  [FAIL] ");
                        Console.ResetColor();
                        Console.WriteLine($"{method.Name}: {ex.InnerException?.Message ?? ex.Message}");
                        failed++;
                    }
                }

                if (theoryAttr != null)
                {
                    var inlineDataAttrs = method.GetCustomAttributes<InlineDataAttribute>().ToList();
                    foreach (var inlineData in inlineDataAttrs)
                    {
                        var instance = Activator.CreateInstance(testClass);
                        var dataDesc = string.Join(", ", inlineData.Data.Select(d => d?.ToString() ?? "null"));
                        try
                        {
                            var result = method.Invoke(instance, inlineData.Data);
                            if (result is Task task)
                            {
                                await task;
                            }
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("  [PASS] ");
                            Console.ResetColor();
                            Console.WriteLine($"{method.Name}({dataDesc})");
                            passed++;
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Write("  [FAIL] ");
                            Console.ResetColor();
                            Console.WriteLine($"{method.Name}({dataDesc}): {ex.InnerException?.Message ?? ex.Message}");
                            failed++;
                        }
                    }
                }
            }
        }

        Console.WriteLine("\n----------------------------------------");
        Console.WriteLine($"Results: Total: {passed + failed}, Passed: {passed}, Failed: {failed}");
        Console.WriteLine("----------------------------------------");

        return failed > 0 ? 1 : 0;
    }
}

