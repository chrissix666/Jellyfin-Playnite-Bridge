using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.PlayniteBridge.Controllers;

[ApiController]
[Route("PlayniteBridge")]
[AllowAnonymous]
public class ScriptController : ControllerBase
{
    [HttpGet("script.js")]
    public ActionResult GetScript() => PluginFile("playnite-bridge.js", "application/javascript");

    [HttpGet("strings.json")]
    public ActionResult GetStrings() => PluginFile("playnite-bridge-strings.json", "application/json");

    // A file next to the plugin's DLL with no-cache plus an ETag from its last write time and length; ASP.NET answers a matching If-None-Match with 304.
    private ActionResult PluginFile(string name, string contentType)
    {
        var info = new System.IO.FileInfo(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(ScriptController).Assembly.Location)!, name));
        if (!info.Exists)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        var tag = new EntityTagHeaderValue("\"" + info.LastWriteTimeUtc.Ticks.ToString("x") + "-" + info.Length.ToString("x") + "\"");
        return PhysicalFile(info.FullName, contentType, info.LastWriteTimeUtc, tag);
    }
}
