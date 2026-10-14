using System;
using System.Collections.Generic;

namespace Ocularr.Update.API
{
    public class Config
    {
        public const string Master = "master";
        public const string Develop = "develop";

        public string DataDirectory { get; set; }
        public string ApiKey { get; set; }
        public string GithubOrg { get; set; }
        public string GithubToken { get; set; }
        public string Project { get; set; }
        public bool LogSql { get; set; }
        public string GithubUrl { get; set; }
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(3);
        public Dictionary<string, string> BranchRedirects { get; set; }

        public string MapBranch(string branch)
        {
            if (branch == null)
            {
                return null;
            }

            var mapped = BranchRedirects != null && BranchRedirects.TryGetValue(branch.ToLowerInvariant(), out var redirect) ? redirect : branch.ToLowerInvariant();

            return mapped == Develop ? Develop : Master;
        }
    }
}
