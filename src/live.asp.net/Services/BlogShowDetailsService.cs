// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using live.asp.net.Models;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace live.asp.net.Services
{
    public class BlogShowDetailsService : IShowDetailsService
    {
        private static readonly string CacheKey = nameof(BlogShowDetailsService);

        private readonly IMemoryCache _cache;
        private readonly TelemetryClient _telemetry;
        private readonly HttpClient _htmlHttpClient;

        public BlogShowDetailsService(IHostingEnvironment hostingEnv, IMemoryCache cache, TelemetryClient telemetry)
        {
            _cache = cache;
            _telemetry = telemetry;
            _htmlHttpClient = new HttpClient();
            _htmlHttpClient.DefaultRequestHeaders.Add("accept", "text/html");
            _htmlHttpClient.DefaultRequestHeaders.Add("User-Agent", "System.Net.Http.HttpClient like Mozilla/5.0 Edge");
        }

        public async Task<ShowDetails> LoadAsync(string showId, DateTimeOffset showDate)
        {
            var result = _cache.Get<ShowDetails>(GetCacheKey(showId));

            if (result == null)
            {
                try
                {
                    result = await LoadFromBlog(showId, showDate);
                }
                catch(Exception ex)
                {
                    _telemetry.TrackException(ex);
                }

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
            var downloadStarted = DateTimeOffset.UtcNow;
            var blogsByTag = await _htmlHttpClient.GetStringAsync(@"https://blogs.msdn.microsoft.com/webdev/tag/communitystandup/");
            string blogPostLink = await FindBlogPostLinkForShow(showDate, blogsByTag, 1);
            _telemetry.TrackDependency("BlogContent.FindBlogPostLinkForShow", showId, downloadStarted, DateTimeOffset.UtcNow - downloadStarted, !string.IsNullOrWhiteSpace(blogPostLink));

            if (!string.IsNullOrWhiteSpace(blogPostLink))
            {
                downloadStarted = DateTimeOffset.UtcNow;
                string showDescription = await GetShowDescriptionFromBlogPost(blogPostLink);
                _telemetry.TrackDependency("BlogContent.GetShowDescriptionFromBlogPost", showId, downloadStarted, DateTimeOffset.UtcNow - downloadStarted, !string.IsNullOrWhiteSpace(showDescription));
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

            blogPostContent = RemoveScriptElements(blogPostContent);
            blogPostContent = AdjustDownHeaderElements(blogPostContent);
            blogPostContent = ExtractArticleContent(blogPostContent);
            blogPostContent = ExtractEntryContent(blogPostContent);
            blogPostContent = ExtractContentBelowVideo(blogPostContent);
            blogPostContent = RemoveBeginningClosingTags(blogPostContent);
            blogPostContent = RemoveBackToTopLink(blogPostContent);
            blogPostContent = PrependWithOriginalContentLocation(blogPostContent, blogPostLink);

            return blogPostContent;
        }

        private string RemoveScriptElements(string html)
        {
            return Regex.Replace(html, "<script.+?</script>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        private string AdjustDownHeaderElements(string html)
        {
            html = AdjustDownHeaderElements(html, 5, 6);
            html = AdjustDownHeaderElements(html, 4, 5);
            html = AdjustDownHeaderElements(html, 3, 5);
            html = AdjustDownHeaderElements(html, 2, 5);
            html = AdjustDownHeaderElements(html, 1, 5);

            return html;
        }

        private string AdjustDownHeaderElements(string html, int fromLevel, int toLevel)
        {
            html = Regex.Replace(html, "<h" + fromLevel + ">", "<h" + toLevel + ">", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "</h" + fromLevel + ">", "</h" + toLevel + ">", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return html;
        }

        private string ExtractArticleContent(string html)
        {
            string articleContent = Regex.Match(html, @"<article.+?>(.+?)</article>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value;

            if (!string.IsNullOrWhiteSpace(articleContent))
            {
                return articleContent;
            }

            return html;
        }

        private string ExtractEntryContent(string html)
        {
            string entryContent = Regex.Match(html, "<div class=\"entry-content\">(.+?)</div><!-- .entry-content", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;

            if (!string.IsNullOrWhiteSpace(entryContent))
            {
                return entryContent;
            }

            return html;
        }

        private string ExtractContentBelowVideo(string html)
        {
            string videoIframe = Regex.Match(html, "<iframe.+?</iframe>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Value;

            if (!string.IsNullOrWhiteSpace(videoIframe))
            {
                string contentBelowVideo = html.Substring(html.IndexOf(videoIframe) + videoIframe.Length);

                if (!string.IsNullOrWhiteSpace(contentBelowVideo))
                {
                    return contentBelowVideo;
                }
            }

            return html;
        }

        private string RemoveBeginningClosingTags(string html)
        {
            if (!string.IsNullOrWhiteSpace(html))
            {
                bool startsWithClosingTag = html.StartsWith("</");
                while (startsWithClosingTag)
                {
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        html = html.Substring(html.IndexOf(">") + 1);
                        startsWithClosingTag = html.StartsWith("</");
                    }
                    else
                    {
                        return string.Empty;
                    }
                }
            }

            return html;
        }

        private string RemoveBackToTopLink(string html)
        {
            if (!string.IsNullOrWhiteSpace(html))
            {
                int backToTopStart = html.IndexOf("<div class=\"back-to-top-wrap\"");

                if (backToTopStart > -1)
                {
                    int divClosingTagStart = html.IndexOf("</div>", backToTopStart);

                    if (divClosingTagStart > backToTopStart)
                    {
                        int length = divClosingTagStart + "</div>".Length - backToTopStart;
                        string backToTopHtml = html.Substring(backToTopStart, length);
                        html = html.Replace(backToTopHtml, string.Empty);
                    }
                }
            }

            return html;
        }

        private string PrependWithOriginalContentLocation(string html, string blogPostLink)
        {
            if (!string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(blogPostLink))
            {
                string originalContentLink = $"<p><i><a href=\"" + blogPostLink + "\">Content grabbed from " + blogPostLink + "</a></i></p>";
                html = originalContentLink + "\r\n" + html;
            }

            return html;
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
