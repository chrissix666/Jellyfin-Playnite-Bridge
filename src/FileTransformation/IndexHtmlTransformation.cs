using System;
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.FileTransformation;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<Registration>();
        serviceCollection.AddSingleton<Filters.VirtualItemsFilter>();
        serviceCollection.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.AddService<Filters.VirtualItemsFilter>());
    }
}

/// <summary>
/// Jellyfin 10.10.7 has no hook for index.html (the web client only loads the plugins listed in its config.json),
/// so the script tag is added through the File Transformation plugin, found at runtime by reflection.
/// </summary>
public class Registration : IHostedService
{
    private readonly ILogger<Registration> _logger;
    private readonly IServiceProvider _services;

    public Registration(ILogger<Registration> logger, IServiceProvider services)
    {
        _logger = logger;
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The filter is a singleton that Jellyfin would make at the first web request; ArtworkPlusLink and GlobeLink answer through it, so it is made at the start,
        // in the background and again a few times if the server is not ready yet.
        _ = Task.Run(() => CreateFilterAsync(cancellationToken), CancellationToken.None);
        try
        {
            var assemblies = AssemblyLoadContext.All.SelectMany(c => c.Assemblies).ToList();
            var register = assemblies
                .FirstOrDefault(a => a.GetName().Name == "Jellyfin.Plugin.FileTransformation")
                ?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface")
                ?.GetMethod("RegisterTransformation");
            var parse = assemblies
                .FirstOrDefault(a => a.GetName().Name == "Newtonsoft.Json")
                ?.GetType("Newtonsoft.Json.Linq.JObject")
                ?.GetMethod("Parse", new[] { typeof(string) });
            if (register is null || parse is null)
            {
                _logger.LogWarning("PlayniteBridge: File Transformation not found, the web client script is not injected");
                return Task.CompletedTask;
            }

            var payload = JsonSerializer.Serialize(new
            {
                id = Plugin.Instance!.Id,
                // A regex, unanchored as in JavaScript Injector and ArtworkPlus: it also matches chunks such as
                // "session-login-index-html.*.chunk.js", which the callback returns unchanged (no "</body>").
                fileNamePattern = "index.html",
                callbackAssembly = typeof(IndexHtmlCallback).Assembly.FullName,
                callbackClass = typeof(IndexHtmlCallback).FullName,
                callbackMethod = nameof(IndexHtmlCallback.Transform)
            });
            IndexHtmlCallback.Log = (ex, message) => _logger.LogError(ex, "{Message}", message);
            register.Invoke(null, new[] { parse.Invoke(null, new object?[] { payload }) });
            _logger.LogInformation("PlayniteBridge: index.html transformation registered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: registering the index.html transformation failed");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateFilterAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 8 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                _services.GetRequiredService<Filters.VirtualItemsFilter>();
                _logger.LogInformation("PlayniteBridge: the interface for other plugins is ready (attempt {Attempt})", attempt);
                return;
            }
            catch (Exception ex)
            {
                if (attempt == 8)
                {
                    _logger.LogWarning(ex, "PlayniteBridge: the filter could not be made at the start; it is made at the first web request");
                    return;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

public static class IndexHtmlCallback
{
    public const string ScriptTag = "<script defer src=\"../PlayniteBridge/script.js\"></script>";

    /// <summary>Error log, set by <see cref="Registration"/>; a plain delegate so the callback needs no Jellyfin types.</summary>
    public static Action<Exception, string>? Log { get; set; }

    /// <summary>
    /// Called by File Transformation with a JObject payload; returns the file with the script tag before the one "&lt;/body&gt;",
    /// else unchanged, so the result is never shorter than the input (File Transformation writes it into the same stream). Null only for an
    /// empty file (File Transformation then keeps the original); when "contents" cannot be read it throws, which fails only that request.
    /// </summary>
    public static string? Transform(object payload)
    {
        // JObject has no "contents" property, only the indexer this[string].
        var contents = payload.GetType().GetProperty("Item", new[] { typeof(string) })
            ?.GetValue(payload, new object[] { "contents" })?.ToString()
            ?? throw new InvalidOperationException("PlayniteBridge: File Transformation handed no readable contents");

        if (contents.Length == 0)
        {
            // an empty file stays as it is (null keeps the original, as ArtworkPlus does); "" would be written back
            return null;
        }

        try
        {
            var body = contents.IndexOf("</body>", StringComparison.Ordinal);
            if (body < 0 || contents.IndexOf("</body>", body + 1, StringComparison.Ordinal) >= 0 || contents.Contains(ScriptTag, StringComparison.Ordinal))
            {
                return contents;
            }

            return contents.Insert(body, ScriptTag);
        }
        catch (Exception ex)
        {
            Log?.Invoke(ex, "PlayniteBridge: transforming index.html failed, returned unchanged");
            return contents;
        }
    }
}
