using Microsoft.AspNetCore.Mvc;

namespace ServarrAPI.Controllers
{
    [ApiController]
    [Route("v1")]
    public class VersionController : ControllerBase
    {
        [HttpGet]
        public object Version()
        {
            return new
            {
                Version = ApiInfo.Version,
                Branch = ApiInfo.Branch,
                Commit = ApiInfo.GitCommit,
                Tag = ApiInfo.GitTag,
                BuildDate = ApiInfo.BuildDate,
                Repository = ApiInfo.RepoUrl,
                Author = ApiInfo.Author,
                CI = ApiInfo.CI
            };
        }
    }
}
