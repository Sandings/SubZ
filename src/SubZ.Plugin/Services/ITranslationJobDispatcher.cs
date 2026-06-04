using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SubZ.Plugin.Services;

public interface ITranslationJobDispatcher
{
    Task EnqueueAsync(IEnumerable<string> targets, CancellationToken cancellationToken);
}
