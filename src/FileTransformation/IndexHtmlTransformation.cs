using System;
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Common.Plugins;
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
        Helpers.Box3dDrawer.Plugins = () => _services.GetRequiredService<IPluginManager>().Plugins.Select(p => (p.Id, p.Manifest.Status, (object?)p.Instance));
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

    /// <summary>Marker of the head block of the Box 3D cards (rules + marking script); written only while somebody draws Box 3D, once.</summary>
    public const string BoxHeadId = "pbBoxStatic";

    // The card of a Box 3D game carries "tag=pbbox-" in the address of its picture (the item answer says so, VirtualItemsFilter.BoxTagPrefix). This script runs in <head>, before the web
    // client's own scripts, with no data: every card the web client inserts on a detail page whose picture carries the prefix gets the class pbBoxCard (its container pbBoxHost) in the
    // microtask of the insertion, before the first paint. Works in every browser that has MutationObserver (no :has()).
    private const string BoxMarkScript =
        "(function(){try{var S='.cardImageContainer[data-src*=\"tag=pbbox-\"],.cardImageContainer[style*=\"tag=pbbox-\"]';"
        + "function mark(n){if(n.nodeType!==1){return;}var l=n.matches&&n.matches(S)?[n]:(n.querySelectorAll?n.querySelectorAll(S):[]);"
        + "for(var i=0;i<l.length;i++){var c=l[i].closest('.card');if(c){c.classList.add('pbBoxCard');var h=c.closest('.detailImageContainer');if(h){h.classList.add('pbBoxHost');}}}}"
        + "new MutationObserver(function(ms){if((location.hash||'').indexOf('#/details')!==0){return;}"
        + "for(var i=0;i<ms.length;i++){var a=ms[i].addedNodes;for(var j=0;j<a.length;j++){mark(a[j]);}}}).observe(document,{childList:true,subtree:true});}catch(e){}})();";

    /// <summary>Static rules of the marked Box 3D card: no card ground, no placeholder icon or blurhash, no marker, the picture contained at the bottom; plus its size and anchor.</summary>
    public static string BoxRules(Helpers.Box3dDraw draw)
    {
        var css = new System.Text.StringBuilder();
        css.Append(".card.pbBoxCard{background:transparent !important;box-shadow:none !important}")
            .Append(".pbBoxCard .cardPadder{background-color:transparent !important;box-shadow:none !important}")
            .Append(".pbBoxCard .cardPadder *{visibility:hidden !important}")
            .Append(".pbBoxCard .blurhash-canvas{display:none !important}")
            .Append(".pbBoxCard .cardImageContainer{background-color:transparent !important;background-size:contain !important;background-position:center bottom !important;background-repeat:no-repeat !important;animation:none !important}")
            .Append(".pbBoxCard .cardImageContainer .pbVersionMarker{display:none !important}");
        if (draw.SizePercent != 100 && draw.SizePercent > 0)
        {
            // The same numbers as the web script's poster rules (posterCss): the card's width and margins in vw per layout, the container lifted into the card's place.
            var f = draw.SizePercent / 100.0;
            var (ax, ay) = Anchor(draw.Anchor);
            string N(double v) => (Math.Round(v, 4) + 0.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            var host = ".detailImageContainer.hide-mobile.pbBoxHost";
            css.Append(".layout-desktop " + host + ",.layout-tv " + host + "{position:relative;z-index:3}")
                .Append(".layout-desktop " + host + "{top:-12.96em}")
                .Append(".layout-desktop " + host + ">.card{top:0}")
                .Append(".layout-tv " + host + "{top:18.75vw}")
                .Append(".layout-tv " + host + ">.card{top:0;transform:translateY(-50%)}")
                .Append("@media all and (max-width:62.5em){.layout-desktop " + host + "{top:0}}");
            foreach (var (layout, width, mobile) in new[] { (".layout-desktop", 25.0, false), (".layout-tv", 25.0, false), (".layout-mobile", 30.0, true) })
            {
                var root = layout + " .detailImageContainer.pbBoxHost ";
                var shift = width * 1.5 * (1 - f) * (mobile ? 1 - ay : ay);
                css.Append(root + ".card.pbBoxCard{width:" + N(width * f) + "vw;max-width:" + N(width * f) + "vw;margin-left:" + N(width * (1 - f) * ax) + "vw;margin-" + (mobile ? "bottom" : "top") + ":" + N(shift) + "vw}");
            }
        }

        return css.ToString();
    }

    private static (double X, double Y) Anchor(string anchor) => anchor switch
    {
        "TopCenter" => (0.5, 0),
        "TopRight" => (1, 0),
        "CenterLeft" => (0, 0.5),
        "Center" => (0.5, 0.5),
        "CenterRight" => (1, 0.5),
        "BottomLeft" => (0, 1),
        "BottomCenter" => (0.5, 1),
        "BottomRight" => (1, 1),
        _ => (0, 0)
    };

    /// <summary>The head block: the rules of <see cref="BoxRules"/> and the marking script; empty while nobody draws Box 3D (the page is then exactly Jellyfin's own).</summary>
    public static string BoxHead() =>
        Helpers.Box3dDrawer.Current() is { } draw ? "<style id=\"" + BoxHeadId + "\">" + BoxRules(draw) + "</style><script>" + BoxMarkScript + "</script>" : string.Empty;

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

            // The Box 3D head block goes right after <head> (before any stylesheet or script), once, only while somebody draws Box 3D.
            var head = contents.IndexOf("<head>", StringComparison.Ordinal);
            var result = contents.Insert(body, ScriptTag);
            return head >= 0 && head < body && !contents.Contains(BoxHeadId, StringComparison.Ordinal) && BoxHead() is { Length: > 0 } block ? result.Insert(head + "<head>".Length, block) : result;
        }
        catch (Exception ex)
        {
            Log?.Invoke(ex, "PlayniteBridge: transforming index.html failed, returned unchanged");
            return contents;
        }
    }
}
