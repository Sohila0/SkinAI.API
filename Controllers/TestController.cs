using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/test")]
    public class TestController : ControllerBase
    {
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                ok = true,
                sub = User.FindFirst("sub")?.Value,
                email = User.FindFirst("email")?.Value,
                role = User.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
            });
        }
    }
}
