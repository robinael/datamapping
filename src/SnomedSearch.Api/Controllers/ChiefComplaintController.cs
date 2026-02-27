using Microsoft.AspNetCore.Mvc;
using SnomedSearch.Core.Common;
using SnomedSearch.Core.Entities;
using SnomedSearch.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace SnomedSearch.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChiefComplaintController : ControllerBase
    {
        private readonly ISnomedRepository _repository;

        public ChiefComplaintController(ISnomedRepository repository)
        {
            _repository = repository;
        }

        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<ChiefComplaint>>> SearchChiefComplaintsAsync(
            [FromQuery] string? searchTerm, 
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 10, 
            CancellationToken cancellationToken = default)
        {
            var results = await _repository.SearchChiefComplaintsPagedAsync(searchTerm ?? "", page, pageSize, cancellationToken);
            return Ok(results);
        }
    }
}
