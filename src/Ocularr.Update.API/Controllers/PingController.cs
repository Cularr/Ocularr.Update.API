using Microsoft.AspNetCore.Mvc;

namespace Ocularr.Update.API.Controllers
{
    [Route("v1/[controller]")]
    public class PingController
    {
        [HttpGet]
        public string Ping()
        {
            return "Pong";
        }
    }
}
