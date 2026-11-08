using System;
using System.Collections.Generic;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PlayniteBridge;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Helpers.PlayniteSnapshot.Root = System.IO.Path.Combine(applicationPaths.DataPath, "playnite-bridge");
        Helpers.ImageCache.CopiesFile = System.IO.Path.Combine(applicationPaths.DataPath, "playnite-bridge", "artwork.json");
        Helpers.BlurHashStore.StoreFile = System.IO.Path.Combine(applicationPaths.DataPath, "playnite-bridge", "blurhashes.json");
        Helpers.MediaInfoStore.StoreFile = System.IO.Path.Combine(applicationPaths.DataPath, "playnite-bridge", "mediainfo.json");
        Helpers.Documents.CoverDirectory = System.IO.Path.Combine(applicationPaths.CachePath, "playnite-bridge", "document-covers");
        Helpers.DocumentThumbs.PendingFile = System.IO.Path.Combine(applicationPaths.DataPath, "playnite-bridge", "document-thumbs-pending.json");
        Helpers.Curator.ManifestFile = System.IO.Path.Combine(applicationPaths.DataPath, "playnite-bridge", "curator.json");
        Helpers.SteamVideos.UseFolder(System.IO.Path.Combine(applicationPaths.DataPath, "playnite-bridge"));
    }

    public static Plugin? Instance { get; private set; }

    private static int _configurationSaves;

    /// <summary>How many times the settings were saved since the server started (GlobeLink.DataStamp).</summary>
    internal static int ConfigurationSaves => System.Threading.Volatile.Read(ref _configurationSaves);

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        try
        {
            base.UpdateConfiguration(configuration);
        }
        finally
        {
            System.Threading.Interlocked.Increment(ref _configurationSaves);
        }
    }

    public override string Name => "Playnite Bridge";

    public override string Description => "Shows the games from Playnite, from every store and added by hand, in a Jellyfin library, without adding them to Jellyfin's database.";

    public override Guid Id => Guid.Parse("59d303d7-b936-4c4b-81a7-e69b2b27d6b6");

    public override string ConfigurationFileName => "Jellyfin.Plugin.PlayniteBridge.xml";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };
    }
}
