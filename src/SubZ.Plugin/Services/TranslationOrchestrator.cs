using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SubZ.Plugin.Api;
using SubZ.Plugin.Configuration;

namespace SubZ.Plugin.Services;

public sealed class TranslationOrchestrator
{
    private readonly PluginOptions _options;
    private readonly TranslationExecutionPolicy _policy;
    private readonly ITranslationJobDispatcher _dispatcher;
    private readonly TranslationProfileResolver _profileResolver;

    public TranslationOrchestrator(
        PluginOptions options,
        TranslationExecutionPolicy policy,
        ITranslationJobDispatcher dispatcher,
        TranslationProfileResolver profileResolver)
    {
        _options = options;
        _policy = policy;
        _dispatcher = dispatcher;
        _profileResolver = profileResolver;
    }

    // Called by library ingest hook / scheduled ingest processing.
    public async Task<bool> TryRunForLibraryIngestAsync(IEnumerable<string> discoveredMediaPaths, CancellationToken cancellationToken)
    {
        if (!_policy.ShouldRunOnLibraryIngest())
        {
            return false;
        }

        await _dispatcher.EnqueueAsync(discoveredMediaPaths, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Called by manual API or plugin page action.
    public async Task<ManualExecutionResponse> RunManualAsync(ManualExecutionRequest request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new ManualExecutionResponse
            {
                Accepted = false,
                Message = UiText.PluginDisabled(),
                Warnings = Array.Empty<string>()
            };
        }

        IReadOnlyList<string> targets;
        try
        {
            targets = _policy.BuildManualTargets(request);
        }
        catch (Exception ex)
        {
            return new ManualExecutionResponse
            {
                Accepted = false,
                Message = ex.Message,
                Warnings = Array.Empty<string>()
            };
        }

        await _dispatcher.EnqueueAsync(targets, cancellationToken).ConfigureAwait(false);
        var profile = _profileResolver.ResolveActiveProfile(_options);

        return new ManualExecutionResponse
        {
            Accepted = true,
            Message = UiText.ManualAccepted(),
            PlannedTargets = targets,
            ActiveProfileName = profile.Name,
            Warnings = Array.Empty<string>()
        };
    }
}
