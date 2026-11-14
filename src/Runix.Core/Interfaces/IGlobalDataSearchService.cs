using System;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface IGlobalDataSearchService
{
    Task<GlobalSearchReport> SearchAcrossDatabaseAsync(
        GlobalSearchOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
