using System;
using Microsoft.AspNetCore.Mvc;

namespace Ocularr.Update.API.Controllers
{
    [Route("v1/[controller]")]
    public class TimeController
    {
        [HttpGet]
        public object Time()
        {
            var time = DateTime.UtcNow.ToString("o");

            return new
            {
                DateTimeUtc = time
            };
        }
    }
}
