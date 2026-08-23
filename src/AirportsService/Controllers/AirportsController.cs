using AirportsService.Models;
using AirportsService.Services;
using Microsoft.AspNetCore.Mvc;

namespace AirportsService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AirportsController : ControllerBase
{
    private readonly IAirportsRepository _repository;

    public AirportsController(IAirportsRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult> GetAirports([FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        offset = Math.Max(offset, 0);
        limit = limit <= 0 ? 50 : limit;
        limit = Math.Min(limit, 100);

        var items = await _repository.GetPagedAsync(offset, limit);

        var result = new
        {
            items,
            offset,
            limit,
            totalCount = items.TotalCount
        };

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Airport>> GetAirportById(string id)
    {
        var airport = await _repository.GetAsync(id);

        if (airport is null)
            return NotFound();

        return Ok(airport);
    }
}