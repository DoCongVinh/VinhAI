using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;

namespace AIOrchestrator.Services
{
    public interface IAiProviderService
    {
        Task<string> GenerateResponseAsync(AiAgent agent, string prompt, AppSettings settings, CancellationToken ct = default);
    }
}
