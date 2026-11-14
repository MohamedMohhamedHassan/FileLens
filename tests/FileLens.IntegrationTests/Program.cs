using System.Reflection;

namespace FileLens.IntegrationTests;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine(" FileLens.IntegrationTests Runner");
        Console.WriteLine("========================================");

        var assembly = typeof(Program).Assembly;
        var testClasses = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetMethods().Any(m => m.GetCustomAttribute<FactAttribute>() != null))
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
                if (factAttr == null)
                    continue;

                object? instance = null;
                try
                {
                    instance = Activator.CreateInstance(testClass);
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
                finally
                {
                    if (instance is IDisposable disposable)
                    {
                        disposable.Dispose();
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

