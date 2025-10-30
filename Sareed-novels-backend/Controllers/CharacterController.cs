using Application.Characters.Commands.CreateCharacter;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/novel/{novelId}/character")]
    public class CharacterController(IMediator mediator) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> CreateCharacter([FromRoute] Guid novelId, [FromForm] CreateCharacterRequest request)
        {
            var command = new CreateCharacterCommand(novelId, request.CharacterName, request.CharacterDescription, request.CharacterAge, request.CharacterImageFile);
            var result = await mediator.Send(command);
            if (result.Success)
            {
                return Ok(result);
            }
            return BadRequest(result);
        }
    }
}
