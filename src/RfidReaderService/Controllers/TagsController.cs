using Microsoft.AspNetCore.Mvc;
using RfidReaderService.Models;
using RfidReaderService.Services;

namespace RfidReaderService.Controllers;

[ApiController]
[Route("api/tags")]
public class TagsController(ImpinjReaderManager manager) : ControllerBase
{
    /// <summary>Tags currently considered "in view" (seen within the configured freshness window).</summary>
    [HttpGet]
    public ActionResult<IReadOnlyCollection<TagReadDto>> GetSnapshot() => Ok(manager.GetTagSnapshot());

    /// <summary>Raw feed of the most recent tag reads, oldest first, newest last.</summary>
    [HttpGet("recent")]
    public ActionResult<IReadOnlyCollection<TagReadDto>> GetRecent([FromQuery] int count = 100) =>
        Ok(manager.GetRecentTags(count));

    [HttpDelete]
    public IActionResult Clear()
    {
        manager.ClearTagBuffers();
        return NoContent();
    }
}
