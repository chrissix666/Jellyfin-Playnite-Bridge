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

    /// <summary>The rules of the Box 3D cards for the current settings, no-cache with an ETag of the content: always current, a cheap 304 while nothing changed.</summary>
    [HttpGet("box.css")]
    public ActionResult GetBoxStyle() => Text(FileTransformation.IndexHtmlCallback.BoxSheet(), "text/css");

    /// <summary>The boot script the head of index.html loads (no data).</summary>
    [HttpGet("boot.js")]
    public ActionResult GetBoot() => Text(FileTransformation.IndexHtmlCallback.BootScript, "application/javascript");

    [HttpGet("strings.json")]
    public ActionResult GetStrings() => PluginFile("playnite-bridge-strings.json", "application/json");

    private ActionResult Text(string content, string contentType)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var tag = "\"" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes))[..16].ToLowerInvariant() + "\"";
        Response.Headers.CacheControl = "no-cache";
        if (Request.Headers.IfNoneMatch.ToString() == tag)
        {
            Response.Headers.ETag = tag;
            return StatusCode(304);
        }

        Response.Headers.ETag = tag;
        return File(bytes, contentType + "; charset=utf-8");
    }

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
