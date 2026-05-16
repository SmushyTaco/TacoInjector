using TacoInjector.Models;

namespace TacoInjector.Services;

public interface IInjectionBackend
{
    ValueTask<InjectionResult> InjectAsync(
        int processId,
        string dllPath,
        CancellationToken cancellationToken);
}