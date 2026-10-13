using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ocularr.Update.API.Model
{
    public interface IUpdateFileService
    {
        Task<UpdateFileEntity> Insert(UpdateFileEntity model);
        Task<UpdateFileEntity> Find(string version, string branch, OperatingSystem os, Architecture arch, bool installer);
        Task<List<UpdateFileEntity>> Find(string branch, OperatingSystem os, Architecture arch, bool installer, int count);
        Task<List<string>> FileNames(int updateId);
    }

    public class UpdateFileService : IUpdateFileService
    {
        private readonly ILogger _logger;
        private readonly IUpdateFileRepository _repo;
        private readonly Config _config;

        public UpdateFileService(IUpdateFileRepository repo,
                                 ILogger<IUpdateFileService> logger,
                                 IOptions<Config> optionsConfig)
        {
            _repo = repo;
            _logger = logger;
            _config = optionsConfig.Value;
        }

        public Task<UpdateFileEntity> Insert(UpdateFileEntity model)
        {
            return _repo.Insert(model);
        }

        public Task<List<string>> FileNames(int updateId)
        {
            return _repo.FileNames(updateId);
        }

        public Task<UpdateFileEntity> Find(string version, string branch, OperatingSystem os, Architecture arch, bool installer)
        {
            return _repo.Find(version, _config.MapBranch(branch), os, arch, installer);
        }

        public Task<List<UpdateFileEntity>> Find(string branch, OperatingSystem os, Architecture arch, bool installer, int count)
        {
            return _repo.Find(_config.MapBranch(branch), os, arch, installer, count);
        }
    }
}
