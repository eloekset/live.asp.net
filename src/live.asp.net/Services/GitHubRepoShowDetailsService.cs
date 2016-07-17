// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using live.asp.net.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace live.asp.net.Services
{
    public class GitHubRepoShowDetailsService : IShowDetailsService
    {
        private static readonly string CacheKey = nameof(GitHubRepoShowDetailsService);

        private readonly AppSettings _appSettings;
        private readonly IMemoryCache _cache;
        private readonly TelemetryClient _telemtry;

        public GitHubRepoShowDetailsService(
            IOptions<AppSettings> appSettings,
            IMemoryCache cache,
            TelemetryClient telemetry)
        {
            _appSettings = appSettings.Value;
            _cache = cache;
            _telemtry = telemetry;
        }

        public async Task<ShowDetails> LoadAsync(string showId)
        {
            var showDetails = _cache.Get<ShowDetails>(GetCacheKey(showId));

            if (showDetails == null)
            {
                showDetails = await LoadFromGitRepo(showId);

                if (showDetails == null)
                {
                    // GitHub API has a rate limit of 60/h without API key and 5000/h with API key.
                    // Store empty ShowDetails in mem cache to avoid hitting API for shows without 
                    // details content.
                    showDetails = new ShowDetails()
                    {
                        ShowId = showId
                    };
                }

                _cache.Set(GetCacheKey(showId), showDetails, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                });
            }

            return showDetails;
        }

        public Task SaveAsync(ShowDetails showDetails)
        {
            // Can be supported but requires API key
            throw new NotImplementedException();
        }
        public Task DeleteAsync(string showId)
        {
            // Can be supported but requires API key
            throw new NotImplementedException();
        }

        private string GetCacheKey(string showId)
        {
            return $"{CacheKey}_{showId}";
        }

        private string GetGitRepoUriForFile(string showId)
        {
            string owner = _appSettings.GitHubContentOwner;
            string repo = _appSettings.GitHubContentRepository;
            string branch = _appSettings.GitHubContentBranch;
            string showDetailsFolder = _appSettings.GitHubContentShowDetailsFolder;

            if (string.IsNullOrWhiteSpace(owner))
                owner = "aspnet";
            if (string.IsNullOrWhiteSpace(repo))
                repo = "live.asp.net.contents";
            if (string.IsNullOrWhiteSpace(branch))
                branch = "master";
            if (string.IsNullOrWhiteSpace(showDetailsFolder))
                showDetailsFolder = "ShowDetails";

            return $"https://api.github.com/repos/{owner}/{repo}/contents/{showDetailsFolder}/ShowDetails_{showId}.json?ref={branch}";
        }

        private async Task<ShowDetails> LoadFromGitRepo(string showId)
        {
            var downloadStarted = DateTimeOffset.UtcNow;
            _telemtry.TrackDependency("GitHub.Api", "repos.contents", downloadStarted, DateTimeOffset.UtcNow - downloadStarted, true);
            string fileContents;

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "live.asp.net");
                    var response = await httpClient.GetStringAsync(GetGitRepoUriForFile(showId));
                    dynamic responseObject = JsonConvert.DeserializeObject(response);
                    string base64Content = responseObject.content;
                    byte[] decoded = Convert.FromBase64String(base64Content);
                    fileContents = Encoding.UTF8.GetString(decoded);
                }

                return JsonConvert.DeserializeObject<ShowDetails>(fileContents);
            }
            catch
            {
                return null;
            }
        }
    }
}