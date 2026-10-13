using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ocularr.Update.API.Model;
using Architecture = System.Runtime.InteropServices.Architecture;
using OperatingSystem = Ocularr.Update.API.Model.OperatingSystem;

namespace Ocularr.Update.API.Test
{
    public sealed class EmptyUpdateFiles : IUpdateFileService
    {
        public Task<UpdateFileEntity> Insert(UpdateFileEntity model) => throw new NotSupportedException();

        public Task<List<string>> FileNames(int updateId) => throw new NotSupportedException();

        public Task<UpdateFileEntity> Find(string version, string branch, OperatingSystem os, Architecture arch, bool installer) => Task.FromResult<UpdateFileEntity>(null);

        public Task<List<UpdateFileEntity>> Find(string branch, OperatingSystem os, Architecture arch, bool installer, int count) => Task.FromResult(new List<UpdateFileEntity>());
    }
}
