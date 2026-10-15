using Microsoft.AspNetCore.Mvc;
using RfidReaderService.Models;
using RfidReaderService.Services;

namespace RfidReaderService.Controllers;

[ApiController]
[Route("api/reader")]
public class ReaderController(ImpinjReaderManager manager) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<ReaderStatusDto> GetStatus() => Ok(manager.GetStatus());

    [HttpPost("connect")]
    public IActionResult Connect([FromBody] ConnectRequest? request)
    {
        manager.Connect(request?.Hostname);
        return Ok(manager.GetStatus());
    }

    [HttpPost("disconnect")]
    public IActionResult Disconnect()
    {
        manager.Disconnect();
        return Ok(manager.GetStatus());
    }

    [HttpPost("start")]
    public IActionResult Start()
    {
        manager.StartReading();
        return Ok(manager.GetStatus());
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        manager.StopReading();
        return Ok(manager.GetStatus());
    }
}
