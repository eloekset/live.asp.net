// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using live.asp.net.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace live.asp.net.Services
{
    public class BlogShowDetailsService : IShowDetailsService
    {
        private static readonly string CacheKey = nameof(BlogShowDetailsService);

        private readonly IMemoryCache _cache;
        private readonly HttpClient _htmlHttpClient;

        public BlogShowDetailsService(IHostingEnvironment hostingEnv, IMemoryCache cache)
        {
            _cache = cache;
            _htmlHttpClient = new HttpClient();
            _htmlHttpClient.DefaultRequestHeaders.Add("accept", "text/html");
            _htmlHttpClient.DefaultRequestHeaders.Add("User-Agent", "System.Net.Http.HttpClient like Mozilla/5.0 Edge");
        }

        public async Task<ShowDetails> LoadAsync(string showId, DateTimeOffset showDate)
        {
            var result = _cache.Get<ShowDetails>(GetCacheKey(showId));

            if (result == null)
            {
                result = await LoadFromBlog(showId, showDate);

                if (result == null)
                {
                    // Cache an empty show detail object for one hour to avoid querying the blog too much
                    _cache.Set(GetCacheKey(showId), new ShowDetails { ShowId = showId }, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                    });
                }
                else
                {
                    _cache.Set(GetCacheKey(showId), result, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                    });
                }
            }

            return result;
        }

        private object GetCacheKey(string showId)
        {
            return $"{CacheKey}_ShowDetails_{showId}";
        }

        public Task SaveAsync(ShowDetails showDetails)
        {
            throw new NotImplementedException();
        }

        public Task DeleteAsync(string showId)
        {
            throw new NotImplementedException();
        }

        private async Task<ShowDetails> LoadFromBlog(string showId, DateTimeOffset showDate)
        {
            var blogsByTag = await _htmlHttpClient.GetStringAsync(@"https://blogs.msdn.microsoft.com/webdev/tag/communitystandup/");
            string blogPostLink = await FindBlogPostLinkForShow(showDate, blogsByTag, 1);

            if (!string.IsNullOrWhiteSpace(blogPostLink))
            {
                string showDescription = await GetShowDescriptionFromBlogPost(blogPostLink);
                return new ShowDetails
                {
                    ShowId = showId,
                    Description = showDescription
                };
            }

            return null;
        }

        private async Task<string> GetShowDescriptionFromBlogPost(string blogPostLink)
        {
            string blogPostContent = await _htmlHttpClient.GetStringAsync(blogPostLink);

            // TODO: Extract only the content we're interested in

            return blogPostContent;
        }

        private async Task<string> FindBlogPostLinkForShow(DateTimeOffset showDate, string blogListHtml, int currentPage)
        {
            var linkRegex = new Regex(@"<h2 class=""entry-title""><a href=""(https://blogs\.msdn\.microsoft\.com/webdev/\d{4}/\d{2}/\d{2}/notes-from-the-asp-net-community-standup-(\w+)-(\w+)-(\w+).+)"" rel=""bookmark"">(.+)</a></h2>");
            var linksWithDates = linkRegex.Matches(blogListHtml);

            foreach (Match linkWithDate in linksWithDates)
            {
                int blogYear, blogMonth, blogDay;
                if (int.TryParse(linkWithDate.Groups[4].Value, out blogYear) &&
                    TryParseMonth(linkWithDate.Groups[2].Value, out blogMonth) &&
                    TryParseDay(linkWithDate.Groups[3].Value, out blogDay))
                {
                    DateTime blogDate = new DateTime(blogYear, blogMonth, blogDay);
                    TimeSpan blogAfterShowTimeSpan = blogDate - showDate.Date;
                    if (blogAfterShowTimeSpan.TotalDays >= 0 && blogAfterShowTimeSpan.TotalDays < 2)
                    {
                        // Assume this blog post is for the show we're looking for
                        return linkWithDate.Groups[1].Value;
                    }
                }
            }

            string nextPageLink = GetNextPageLink(blogListHtml, currentPage);

            if (!string.IsNullOrWhiteSpace(nextPageLink))
            {
                blogListHtml = await _htmlHttpClient.GetStringAsync(nextPageLink);
                return await FindBlogPostLinkForShow(showDate, blogListHtml, (currentPage + 1));
            }

            return null;
        }

        private string GetNextPageLink(string blogListHtml, int currentPage)
        {
            var nextPageLinks = Regex.Matches(blogListHtml, @"<a class='page-numbers' href='(https://blogs.msdn.microsoft.com/webdev/tag/communitystandup/page/(\d+)/)'>\d+</a>");

            foreach (Match nextPageLink in nextPageLinks)
            {
                int pageNumberInLink;

                if (int.TryParse(nextPageLink.Groups[2].Value, out pageNumberInLink))
                {
                    if (pageNumberInLink == (currentPage + 1))
                        return nextPageLink.Groups[1].Value;
                }
            }

            return null;
        }

        private bool TryParseMonth(string monthString, out int month)
        {
            month = 0;

            if (string.IsNullOrWhiteSpace(monthString))
            {
                return false;
            }

            if (int.TryParse(monthString, out month))
            {
                return true;
            }
            else if (monthString.Trim().Length < 3)
            {
                return false;
            }

            switch(monthString.ToLower().Trim().Substring(0, 3))
            {
                case "jan":
                    month = 1;
                    return true;
                case "feb":
                    month = 2;
                    return true;
                case "mar":
                    month = 3;
                    return true;
                case "apr":
                    month = 4;
                    return true;
                case "may":
                    month = 5;
                    return true;
                case "jun":
                    month = 6;
                    return true;
                case "jul":
                    month = 7;
                    return true;
                case "aug":
                    month = 8;
                    return true;
                case "sep":
                    month = 9;
                    return true;
                case "oct":
                    month = 10;
                    return true;
                case "nov":
                    month = 11;
                    return true;
                case "dec":
                    month = 12;
                    return true;
                default:
                    return false;
            }
        }

        private bool TryParseDay(string dayString, out int day)
        {
            day = 0;

            if (string.IsNullOrWhiteSpace(dayString))
            {
                return false;
            }

            dayString = Regex.Match(dayString, @"\d+").Value;

            return int.TryParse(dayString, out day);
        }
    }
}
