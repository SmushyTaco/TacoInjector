namespace TacoInjector.Models;

public readonly record struct InjectionResult(bool Succeeded, string Message)
{
    public static InjectionResult Success(string message) =>
        new(true, message);

    public static InjectionResult Failure(string message) =>
        new(false, message);
}
