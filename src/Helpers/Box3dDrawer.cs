using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using Jellyfin.Plugin.PlayniteBridge.Controllers;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>Who draws the Box 3D of the game detail pages and with which size and anchor: the one place the server decides it (item answer, image route, index.html).</summary>
/// <param name="ByArtworkPlus">True when ArtworkPlus draws (its settings), false when the Bridge does.</param>
/// <param name="SizePercent">Size of the Box 3D card in percent (100 = the card's own size).</param>
/// <param name="Anchor">Where a smaller card sits (TopLeft ... BottomRight); anything else counts as TopLeft, as in the web script.</param>
public sealed record Box3dDraw(bool ByArtworkPlus, int SizePercent, string Anchor);

public static class Box3dDrawer
{
    /// <summary>The installed plugins with their status and instance; set once by the registration at the server start (null in the offline checks: nobody draws then).</summary>
    public static Func<IEnumerable<(Guid Id, PluginStatus Status, object? Instance)>>? Plugins { get; set; }

    /// <summary>The drawer now, or null when nobody draws Box 3D on the game pages.</summary>
    public static Box3dDraw? Current() => Plugin.Instance is { } plugin && Plugins is { } plugins ? Decide(plugin.Configuration, plugins()) : null;

    /// <summary>
    /// ArtworkPlus draws while it is active and Settings From is ArtworkPlus and its own "Box 3D" is on (CaseModBox3dEnabled, size CaseModPosterSizeBox3d, anchor
    /// CaseModPosterSizeAnchorBox3d, read by reflection); else the Bridge draws when its Box 3D is on and Settings From is not ArtworkPlus; else nobody.
    /// </summary>
    public static Box3dDraw? Decide(PluginConfiguration config, IEnumerable<(Guid Id, PluginStatus Status, object? Instance)> plugins)
    {
        var active = plugins.Where(p => p.Id == GamesController.ArtworkPlusId && p.Status == PluginStatus.Active).Select(p => ((Guid, PluginStatus, object?)?)p).FirstOrDefault();
        if (GamesController.ArtworkPlusDraws(config, active is not null))
        {
            var settings = Settings(active!.Value.Item3);
            return settings is null ? null : new Box3dDraw(true, settings.Value.Size, settings.Value.Anchor);
        }

        return config.ArtPosterBox3dEnabled ? new Box3dDraw(false, config.ArtPosterBox3dSizePercent, config.ArtPosterBox3dAnchor.ToString()) : null;
    }

    /// <summary>ArtworkPlus's own rule (CaseModController): size and anchor follow the Poster size block: off = 100 / Default; mode General = the common size and anchor; else the Box 3D ones; 10 to 200.</summary>
    private static (int Size, string Anchor)? Settings(object? instance)
    {
        try
        {
            var configuration = instance?.GetType().GetProperty("Configuration")?.GetValue(instance);
            if (configuration is null)
            {
                return null;
            }

            object? Get(string name) => configuration.GetType().GetProperty(name)?.GetValue(configuration);
            if (Get("CaseModBox3dEnabled") is not true)
            {
                return null;
            }

            if (Get("CaseModPosterSizeEnabled") is not true)
            {
                return (100, "Default");
            }

            var general = Get("CaseModPosterSizeMode")?.ToString() == "General";
            var size = Math.Clamp(Get(general ? "CaseModPosterSizeAll" : "CaseModPosterSizeBox3d") is int s ? s : 100, 10, 200);
            var anchor = Get(general ? "CaseModPosterSizeAnchorAll" : "CaseModPosterSizeAnchorBox3d")?.ToString() ?? "Default";
            return (size, ValidAnchors.Contains(anchor) ? anchor : "Default");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly HashSet<string> ValidAnchors = new(StringComparer.Ordinal)
    {
        "Default", "TopLeft", "TopCenter", "TopRight", "CenterLeft", "Center", "CenterRight", "BottomLeft", "BottomCenter", "BottomRight"
    };
}
