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
            // From here the head block of the Box 3D cards can be delivered: only now may the item answers hand out a Box 3D card address.
            Helpers.Box3dDrawer.HeadDelivered = true;
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

    /// <summary>
    /// The two tags the head of index.html carries (once the transformation is registered, always, whether somebody draws Box 3D or not): the rules and the boot script of the Box 3D cards are
    /// served live by the plugin (no-cache), because the browser keeps index.html itself for good (its validators come from the file on disk, not from this transformation).
    /// </summary>
    public const string HeadTags = "<link rel=\"stylesheet\" href=\"../PlayniteBridge/box.css\"><script src=\"../PlayniteBridge/boot.js\"></script>";

    /// <summary>
    /// The boot script (no data, runs in &lt;head&gt; before the web client's own scripts): (1) answers window.PlayniteBridge.isGame / box3d at once from what the script remembered in localStorage
    /// (true = a game, false = certainly none once the whole list is remembered, null = not known yet); (2) marks every card the web client inserts on a game's detail page whose picture address carries
    /// "tag=pbbox-" with the class pbBoxCard (its container pbBoxHost) in the microtask of the insertion, before the first paint. The observer works only on detail pages that may be games: a page whose
    /// id is certainly no game (movie, series, person) is left alone.
    /// </summary>
    public const string BootScript =
        // (0) The web client script itself, as a deferred script written right here: deferred scripts run in document order, so it runs before every bundle of
        // Jellyfin (all deferred, after this head) and its listeners and wrappers stand at Jellyfin's first render - also on a cold load of a game page (F5, link).
        // The deferred tag at the end of the body loads it a second time; that copy does nothing (window.PlayniteBridgeLoaded).
        "(function(){try{if(document.readyState==='loading'){document.write('<script defer src=\"../PlayniteBridge/script.js\"></script>');}}catch(e){}})();"
        + "(function(){try{var ls=window.localStorage,P=window.PlayniteBridge=window.PlayniteBridge||{},known={},complete=false,hold=null;"
        + "var norm=function(i){return String(i||'').replace(/-/g,'').toLowerCase();};"
        + "try{(JSON.parse(ls.getItem('pbKnownGames'))||[]).forEach(function(k){known[k]=1;});complete=ls.getItem('pbGamesComplete')==='1'&&Object.keys(known).length>0;}catch(e){}"
        + "try{hold=JSON.parse(ls.getItem('pbBox3dHold'));}catch(e){}"
        + "P.isGame=function(i){var k=norm(i);return known[k]?true:(complete?false:null);};"
        + "P.box3d=function(i){if(!hold){return null;}var k=norm(i);return !!(hold.ours&&hold.ids&&hold.ids.indexOf(k)>=0);};"
        + "P.box3dTag=function(i){return hold&&hold.tags&&hold.tags[norm(i)]||null;};"
        + "var S='.cardImageContainer[data-src*=\"tag=pbbox-\"],.cardImageContainer[style*=\"tag=pbbox-\"]';"
        + "function relevant(){var h=location.hash||'';if(h.indexOf('#/details')!==0){return false;}var m=/[?&]id=([0-9a-fA-F-]{32,36})/.exec(h);return !(m&&complete&&!known[norm(m[1])]);}"
        + "function mark(n){if(n.nodeType!==1){return;}var l=n.matches&&n.matches(S)?[n]:(n.querySelectorAll?n.querySelectorAll(S):[]);"
        + "for(var i=0;i<l.length;i++){var c=l[i].closest('.card');if(c){c.classList.add('pbBoxCard');var h=c.closest('.detailImageContainer');if(h){h.classList.add('pbBoxHost');}}}}"
        + "new MutationObserver(function(ms){if(!relevant()){return;}for(var i=0;i<ms.length;i++){var a=ms[i].addedNodes;for(var j=0;j<a.length;j++){mark(a[j]);}}}).observe(document,{childList:true,subtree:true});"
        // TV layout: Jellyfin's action sheet is a history entry the router pushes with ITS location and pops with history.go(-1); after an in-place version pick the entry the pop returns to holds the version SHOWN,
        // so the router sees another location and rebuilds the page. This listener runs before the router's: the popped entry's URL is set to the router's own location for the pop and the shown
        // version's URL is written back when no pick followed.
        + "var rurl=null;var pu=History.prototype.pushState;History.prototype.pushState=function(s,t,u){if(document.documentElement.classList.contains('layout-tv')&&typeof u==='string'&&u.indexOf('#/details?id=')>=0){rurl=u;}return pu.apply(this,arguments);};"
        + "window.addEventListener('popstate',function(){var c=document.querySelector('.itemDetailPage.pbGame:not(.hide)');var m=/[?&]id=([0-9a-fA-F-]+)/.exec(location.hash||'');"
        + "if(!rurl||!c||!c.pbVersion||!m||norm(m[1])!==c.pbVersion){return;}var r=/[?&]id=([0-9a-fA-F-]+)/.exec(rurl);if(!r||norm(r[1])===c.pbVersion){return;}"
        + "var shown=location.href,before=c.pbVersion;history.replaceState(history.state,'',rurl);"
        + "setTimeout(function(){var c2=document.querySelector('.itemDetailPage.pbGame:not(.hide)');if(c2&&c2.pbVersion===before&&!c2.pbSwitching){history.replaceState(history.state,'',shown);}},700);},true);}catch(e){}})();";

    /// <summary>Static rules of the marked Box 3D card: no card ground, no placeholder icon or blurhash, no marker, the picture contained at the bottom; plus its size and anchor.</summary>
    public static string BoxRules(Helpers.Box3dDraw draw)
    {
        var css = new System.Text.StringBuilder();
        css.Append(".card.pbBoxCard{background:transparent !important;box-shadow:none !important}")
            .Append(".pbBoxCard .cardPadder{background-color:transparent !important;box-shadow:none !important}")
            .Append(".pbBoxCard .cardPadder *{visibility:hidden !important}")
            .Append(".pbBoxCard .blurhash-canvas{display:none !important}")
            .Append(".pbBoxCard .cardImageContainer{background-color:transparent !important;background-size:contain !important;background-position:center bottom !important;background-repeat:no-repeat !important;animation:none !important}")
            // Jellyfin's lazy loader hides a picture (opacity 0) until it has loaded again, also when the page comes back from its view cache with the picture still set: no hiding for the Box 3D card.
            .Append(".pbBoxCard .cardImageContainer.lazy-hidden{opacity:1 !important}")
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

    /// <summary>
    /// The style sheet the head links (served live): the rules of <see cref="BoxRules"/> for the current drawer (the static rules alone while nobody draws, they match no card then), and a
    /// custom property with the key of the rules so the web script can tell that a setting changed and load the sheet again.
    /// </summary>
    public static string BoxSheet() => ":root{--pb-box-key:\"" + BoxStyleKey() + "\"}" + BoxRules(Helpers.Box3dDrawer.Current() ?? new Helpers.Box3dDraw(false, 100, "Default"));

    /// <summary>Short key of the current rules (size, anchor, drawer): changes when a setting that moves the box changes.</summary>
    public static string BoxStyleKey()
    {
        var draw = Helpers.Box3dDrawer.Current();
        return draw is null ? "none" : (draw.ByArtworkPlus ? "ap" : "bridge") + "-" + draw.SizePercent + "-" + draw.Anchor;
    }

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

            // The two Box 3D tags go right after <head> (before any stylesheet or script), once, as soon as the transformation is registered.
            var head = contents.IndexOf("<head>", StringComparison.Ordinal);
            var result = contents.Insert(body, ScriptTag);
            return Helpers.Box3dDrawer.HeadDelivered && head >= 0 && head < body && !contents.Contains("PlayniteBridge/boot.js", StringComparison.Ordinal)
                ? result.Insert(head + "<head>".Length, HeadTags) : result;
        }
        catch (Exception ex)
        {
            Log?.Invoke(ex, "PlayniteBridge: transforming index.html failed, returned unchanged");
            return contents;
        }
    }
}
