using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ocularr.Update.API.Datastore;
using Ocularr.Update.API.Extensions;

namespace Ocularr.Update.API.Model
{
    public interface IUpdateFileRepository : IBasicRepository<UpdateFileEntity>
    {
        Task<UpdateFileEntity> Find(string version, string branch, OperatingSystem os, Architecture arch, bool installer);
        Task<List<UpdateFileEntity>> Find(string branch, OperatingSystem os, Architecture arch, bool installer, int count);
        Task<List<string>> FileNames(int updateId);
    }

    public class UpdateFileRepository : BasicRepository<UpdateFileEntity>, IUpdateFileRepository
    {
        public UpdateFileRepository(IDatabase database)
        : base(database)
        {
        }

        public async Task<UpdateFileEntity> Find(string version, string branch, OperatingSystem os, Architecture arch, bool installer)
        {
            var result = await Query(Builder()
                                     .Where<UpdateFileEntity>(f => f.OperatingSystem == os &&
                                                              f.Architecture == arch &&
                                                              f.Installer == installer)
                                     .Where<UpdateEntity>(u => u.Branch == branch &&
                                                               u.Version == version));
            return result.FirstOrDefault();
        }

        public async Task<List<UpdateFileEntity>> Find(string branch, OperatingSystem os, Architecture arch, bool installer, int count)
        {
            var builder = Builder().Where<UpdateFileEntity>(f => f.OperatingSystem == os && f.Architecture == arch && f.Installer == installer);

            var result = await Query(builder.Where<UpdateEntity>(u => u.Branch == branch).OrderBy($"update.intversion DESC LIMIT {count}"))
                .ConfigureAwait(false);

            return result;
        }

        public async Task<List<string>> FileNames(int updateId)
        {
            var result = await Query(Builder().Where<UpdateFileEntity>(f => f.UpdateId == updateId)).ConfigureAwait(false);

            return result.Select(f => f.Filename).ToList();
        }

        protected override SqlBuilder Builder()
        {
            return new SqlBuilder()
                .Join<UpdateFileEntity, UpdateEntity>((f, u) => f.UpdateId == u.Id);
        }

        protected override async Task<List<UpdateFileEntity>> Query(SqlBuilder builder)
        {
            var result = await _database.QueryJoined<UpdateFileEntity, UpdateEntity>(builder,
                                                                                     (file, update) =>
                                                                                     {
                                                                                         file.Update = update;
                                                                                         return file;
                                                                                     });
            return result.ToList();
        }
    }
}
