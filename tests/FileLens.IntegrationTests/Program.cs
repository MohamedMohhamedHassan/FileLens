using System.Reflection;

namespace FileLens.IntegrationTests;

public static class Program
{
    // [STAThread] cannot be honoured on an async Main — we manage STA ourselves per test.
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

                // Tests marked [StaFact] run on a dedicated STA thread (required for WPF).
                // Tests marked [Fact] run normally on the async context.
                var staAttr = method.GetCustomAttribute<StaFactAttribute>();
                bool needsSta = staAttr != null;

                Exception? testException = null;

                if (needsSta)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    var staThread = new Thread(() =>
                    {
                        object? instance = null;
                        try
                        {
                            instance = Activator.CreateInstance(testClass);
                            var result = method.Invoke(instance, null);
                            if (result is Task task)
                            {
                                // Pump the STA message loop while awaiting the task
                                task.GetAwaiter().GetResult();
                            }
                            tcs.SetResult(true);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex.InnerException ?? ex);
                        }
                        finally
                        {
                            if (instance is IDisposable d) d.Dispose();
                        }
                    });
                    staThread.SetApartmentState(ApartmentState.STA);
                    staThread.Start();

                    try
                    {
                        await tcs.Task;
                    }
                    catch (Exception ex)
                    {
                        testException = ex;
                    }
                }
                else
                {
                    object? instance = null;
                    try
                    {
                        instance = Activator.CreateInstance(testClass);
                        var result = method.Invoke(instance, null);
                        if (result is Task task)
                        {
                            await task;
                        }
                    }
                    catch (Exception ex)
                    {
                        testException = ex.InnerException ?? ex;
                    }
                    finally
                    {
                        if (instance is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }

                if (testException == null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("  [PASS] ");
                    Console.ResetColor();
                    Console.WriteLine(method.Name);
                    passed++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("  [FAIL] ");
                    Console.ResetColor();
                    Console.WriteLine($"{method.Name}: {testException.Message}");
                    failed++;
                }
            }
        }

        Console.WriteLine("\n----------------------------------------");
        Console.WriteLine($"Results: Total: {passed + failed}, Passed: {passed}, Failed: {failed}");
        Console.WriteLine("----------------------------------------");

        return failed > 0 ? 1 : 0;
    }
}
