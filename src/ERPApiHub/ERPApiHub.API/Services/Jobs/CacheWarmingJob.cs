using ERPApiHub.Application.Query;
using Microsoft.Extensions.Options;

namespace ERPApiHub.API.Services.Jobs;

public sealed class CacheWarmingJob(
    QueryService queryService,
    IOptions<JobOptions> options,
    ILogger<CacheWarmingJob> logger)
{
    public Task WarmAsync() => WarmAsync(CancellationToken.None);

    private async Task WarmAsync(CancellationToken cancellationToken)
    {
        var jobOptions = options.Value;
        if (!jobOptions.Enabled)
        {
            return;
        }

        if (jobOptions.TenantIds.Length == 0 || jobOptions.FrequentlyAccessedDoctypes.Length == 0)
        {
            logger.LogDebug("Skipping cache warming because Jobs:TenantIds or Jobs:FrequentlyAccessedDoctypes is empty.");
            return;
        }

        var pageSize = Math.Clamp(jobOptions.WarmPageSize, 1, 500);
        var pageCount = Math.Clamp(jobOptions.WarmPageCount, 1, 20);

        try
        {
            foreach (var tenantId in jobOptions.TenantIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                foreach (var doctype in jobOptions.FrequentlyAccessedDoctypes.Where(type => !string.IsNullOrWhiteSpace(type)))
                {
                    for (var page = 1; page <= pageCount; page++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var request = new QueryRequest
                        {
                            Doctype = doctype,
                            Page = page,
                            PageSize = pageSize
                        };

                        await queryService.ListAsync(request, tenantId, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache warming job failed.");
            throw;
        }
    }
}
