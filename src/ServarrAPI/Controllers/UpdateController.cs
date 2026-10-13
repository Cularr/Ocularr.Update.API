using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using ServarrAPI.Datastore;
using ServarrAPI.Model;
using ServarrAPI.Release;
using ServarrAPI.Stats;
using ServarrAPI.TaskQueue;
using ServarrAPI.Util;
using Architecture = System.Runtime.InteropServices.Architecture;
using OperatingSystem = ServarrAPI.Model.OperatingSystem;

namespace ServarrAPI.Controllers.Update
{
    [ApiController]
    [Route("v1/[controller]")]
    public class UpdateController : Controller
    {
        private readonly IUpdateFileService _updateFileService;
        private readonly UpdateChecks _updateChecks;
        private readonly IBackgroundTaskQueue _queue;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public UpdateController(IUpdateFileService updateFileService, UpdateChecks updateChecks, IBackgroundTaskQueue queue, IServiceScopeFactory serviceScopeFactory)
        {
            _updateFileService = updateFileService;
            _updateChecks = updateChecks;
            _queue = queue;
            _serviceScopeFactory = serviceScopeFactory;
        }

        [HttpPost]
        [ApiKey]
        public IActionResult Publish()
        {
            ReleaseRefreshService.QueueRefresh(_queue, _serviceScopeFactory);

            return Accepted();
        }

        [Route("{branch}/changes")]
        [HttpGet]
        public async Task<object> GetChanges([FromRoute(Name = "branch")] string updateBranch,
                                             [FromQuery(Name = "version")] string urlVersion,
                                             [FromQuery(Name = "os")] OperatingSystem operatingSystem,
                                             [FromQuery(Name = "arch")] Architecture arch = Architecture.X64)
        {
            Response.Headers[HeaderNames.CacheControl] = GetCacheControlHeader(DateTime.UtcNow.AddMinutes(5));

            var updateFiles = await _updateFileService.Find(updateBranch, operatingSystem, arch, false, 5);

            var response = new List<UpdatePackage>();

            foreach (var updateFile in updateFiles)
            {
                await updateFile.Update.LoadAsync();
                var update = updateFile.Update.Value;
                UpdateChanges updateChanges = null;

                if (update.New.Count != 0 || update.Fixed.Count != 0)
                {
                    updateChanges = new UpdateChanges
                    {
                        New = update.New,
                        Fixed = update.Fixed
                    };
                }

                response.Add(new UpdatePackage
                {
                    Version = update.Version,
                    ReleaseDate = update.ReleaseDate,
                    Filename = updateFile.Filename,
                    Url = updateFile.Url,
                    Changes = updateChanges,
                    Hash = updateFile.Hash,
                    Branch = update.Branch.ToString().ToLower()
                });
            }

            return response;
        }

        [Route("{branch}")]
        [HttpGet]
        public async Task<object> GetUpdates([FromRoute(Name = "branch")] string updateBranch,
                                             [FromQuery(Name = "version")] string urlVersion,
                                             [FromQuery(Name = "os")] OperatingSystem operatingSystem,
                                             [FromQuery(Name = "arch")] Architecture arch,
                                             [FromQuery(Name = "dbType")] string dbType,
                                             [FromQuery(Name = "active")] bool? activeInstall = null)
        {
            Response.Headers[HeaderNames.CacheControl] = GetCacheControlHeader(DateTime.UtcNow);

            // Check given version
            if (!Version.TryParse(urlVersion, out var version))
            {
                ModelState.AddModelError("version", "Invalid version number specified.");
                return ValidationProblem(ModelState);
            }

            // Dont' send metrics for dev/debug instances
            if (version.Major < 10 && activeInstall.HasValue)
            {
                _updateChecks.Record(version, updateBranch, operatingSystem, arch, dbType, activeInstall.Value);
            }

            var files = await _updateFileService.Find(updateBranch, operatingSystem, arch, false, 1);

            var updateFile = files.FirstOrDefault();

            if (updateFile == null)
            {
                return new UpdatePackageContainer
                {
                    Available = false
                };
            }

            await updateFile.Update.LoadAsync();
            var update = updateFile.Update.Value;

            // Compare given version and update version
            var updateVersion = new Version(update.Version);
            if (updateVersion.CompareTo(version) <= 0)
            {
                return new UpdatePackageContainer
                {
                    Available = false
                };
            }

            // Get the update changes
            UpdateChanges updateChanges = null;

            if (update.New.Count != 0 || update.Fixed.Count != 0)
            {
                updateChanges = new UpdateChanges
                {
                    New = update.New,
                    Fixed = update.Fixed
                };
            }

            return new UpdatePackageContainer
            {
                Available = true,
                UpdatePackage = new UpdatePackage
                {
                    Version = update.Version,
                    ReleaseDate = update.ReleaseDate,
                    Filename = updateFile.Filename,
                    Url = updateFile.Url,
                    Changes = updateChanges,
                    Hash = updateFile.Hash,
                    Branch = update.Branch.ToString().ToLower()
                }
            };
        }

        [Route("{branch}/updatefile")]
        [HttpGet]
        public async Task<object> GetUpdateFile([FromRoute(Name = "branch")] string updateBranch,
                                                [FromQuery(Name = "version")] string urlVersion,
                                                [FromQuery(Name = "os")] OperatingSystem operatingSystem,
                                                [FromQuery(Name = "arch")] Architecture arch,
                                                [FromQuery(Name = "installer")] bool installer = false)
        {
            Response.Headers[HeaderNames.CacheControl] = GetCacheControlHeader(DateTime.UtcNow);

            UpdateFileEntity updateFile;

            if (urlVersion != null)
            {
                if (!Version.TryParse(urlVersion, out var _))
                {
                    return new
                    {
                        ErrorMessage = "Invalid version number specified."
                    };
                }

                updateFile = await _updateFileService.Find(urlVersion, updateBranch, operatingSystem, arch, installer);
            }
            else
            {
                var updateFiles = await _updateFileService.Find(updateBranch, operatingSystem, arch, installer, 1);
                updateFile = updateFiles.FirstOrDefault();
            }

            if (updateFile == null)
            {
                return new
                {
                    ErrorMessage = $"Update file for {updateBranch}-{urlVersion} not found."
                };
            }

            return RedirectPermanent(updateFile.Url);
        }

        private string GetCacheControlHeader(DateTime expiry)
        {
            var now = DateTime.UtcNow;
            var maxage = (int)(expiry - now).TotalSeconds;

            return $"public,s-maxage={maxage},max-age=0";
        }
    }
}
