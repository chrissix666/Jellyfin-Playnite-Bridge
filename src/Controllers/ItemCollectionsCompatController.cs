#if !NET10_0_OR_GREATER
using System;
using Jellyfin.Plugin.PlayniteBridge.Filters;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PlayniteBridge.Controllers;

/// <summary>
/// Jellyfin 12.x has GET Items/{itemId}/Collections (the page of an item's collections, asked by third-party scripts such as Jellyfin-DetailsGroup); Jellyfin 10.10.7 has no such route, so every page
/// of a game logged a 404 in the browser console there. The same route answers the empty page for a Playnite game or collection folder (what 12.x answers for an item without collections). Every other
/// id keeps the 404 of 10.10.7: a script that asks first and falls back to its own index on a 404 (Jellyfin-DetailsGroup) must keep doing so for films.
/// </summary>
[ApiController]
[Authorize]
public class ItemCollectionsCompatController : ControllerBase
{
    private readonly VirtualItemsFilter _filter;

    public ItemCollectionsCompatController(VirtualItemsFilter filter)
    {
        _filter = filter;
    }

    [HttpGet("Items/{itemId}/Collections")]
    public ActionResult<QueryResult<BaseItemDto>> GetItemCollections([FromRoute] Guid itemId, [FromQuery] int? startIndex)
    {
        return _filter.Find(itemId, out _) is not null
            ? Ok(VirtualItemsFilter.EmptyItemCollections(startIndex))
            : NotFound();
    }
}
#endif
