using System.Web;
using GlobalJobHunter.Service.Models;
using GlobalJobHunter.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GlobalJobHunter.Service.Providers;

public sealed class IndeedProvider : PlaywrightJobProvider
{
    private const string SearchUrl =
        "https://www.indeed.com/jobs?q=.net+developer&sc=0kf:attr(DSQF7);&fromage=2";

    public override string SourcePlatform => "Indeed";

    public IndeedProvider(
        IPlaywrightBrowserService browserService,
        ILogger<IndeedProvider> logger)
        : base(browserService, logger) { }

    protected override async Task<IEnumerable<JobPosting>> FetchWithBrowserAsync(
        IBrowserContext context, CancellationToken ct)
    {
        IPage? page = null;
        var results = new List<JobPosting>();

        try
        {
            page = await context.NewPageAsync();

            Logger.LogInformation("[Indeed] Navigating to search URL...");
            await page.GotoAsync(SearchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000
            });

            // ── CAPTCHA / bot-wall detection ──
            var title = await page.TitleAsync();
            if (ContainsCaptchaKeyword(title))
            {
                Logger.LogWarning("[Indeed] CAPTCHA/bot-wall detected (title: '{Title}'). Skipping.", title);
                return results;
            }

            // ── Wait for job cards container ──
            try
            {
                await page.WaitForSelectorAsync(
                    "[data-testid='mosaic-provider-jobcards'], #mosaic-provider-jobcards",
                    new PageWaitForSelectorOptions { Timeout = 15_000 });
            }
            catch (TimeoutException)
            {
                Logger.LogWarning("[Indeed] Job cards container not found within timeout. Possibly blocked or layout changed.");
                return results;
            }

            // ── Wait for network idle (swallow — ads keep network busy) ──
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
                // Expected — ads/trackers keep requests alive. Proceed with whatever is loaded.
            }

            await HumanDelayAsync(ct);

            // ── Extract job cards ──
            var cards = await page.QuerySelectorAllAsync("li.css-1ac2h1w, div[data-jk]");
            Logger.LogInformation("[Indeed] Found {Count} job card elements.", cards.Count);

            foreach (var card in cards)
            {
                try
                {
                    var titleText = await ExtractTextAsync(card,
                        "[data-testid='jobTitle'] span, h2.jobTitle a span, h2.jobTitle span");
                    var company   = await ExtractTextAsync(card,
                        "[data-testid='company-name'], span.css-1h7lukg");
                    var location  = await ExtractTextAsync(card,
                        "[data-testid='text-location'], div.css-1restlb");
                    var dateText  = await ExtractTextAsync(card,
                        "[data-testid='myJobsStateDate'], span.date");
                    var hrefRaw   = await ExtractAttributeAsync(card,
                        "a.jcs-JobTitle[href], h2.jobTitle a[href]", "href");

                    if (string.IsNullOrWhiteSpace(titleText) || string.IsNullOrWhiteSpace(hrefRaw))
                        continue;

                    var url        = BuildJobUrl(hrefRaw);
                    var postedDate = ParseRelativeDate(dateText);
                    var workModel  = DetermineWorkModel(location);

                    results.Add(new JobPosting
                    {
                        Title          = titleText.Trim(),
                        Company        = company?.Trim() ?? "Unknown",
                        Location       = location?.Trim() ?? "N/A",
                        WorkModel      = workModel,
                        SourcePlatform = SourcePlatform,
                        Url            = url,
                        PostedDate     = postedDate,
                        Description    = $"{titleText} at {company} ({location})"
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "[Indeed] Failed to parse a job card. Skipping.");
                }
            }

            Logger.LogInformation("[Indeed] Fetched {Count} jobs.", results.Count);
        }
        finally
        {
            if (page != null)
            {
                try { await page.CloseAsync(); }
                catch { /* best effort */ }
            }
        }

        return results;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static bool ContainsCaptchaKeyword(string title)
    {
        var lower = title.ToLowerInvariant();
        return lower.Contains("captcha")
            || lower.Contains("verify")
            || lower.Contains("robot")
            || lower.Contains("unusual traffic")
            || lower.Contains("blocked");
    }

    /// <summary>
    /// Builds the canonical Indeed job URL.
    /// Prefers the <c>jk=</c> query parameter → viewjob URL.
    /// Falls back to a full URL if <c>jk</c> is missing.
    /// </summary>
    private static string BuildJobUrl(string href)
    {
        try
        {
            var fullUri = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(href)
                : new Uri("https://www.indeed.com" + href);

            var jk = HttpUtility.ParseQueryString(fullUri.Query)["jk"];
            return !string.IsNullOrWhiteSpace(jk)
                ? $"https://www.indeed.com/viewjob?jk={jk}"
                : fullUri.ToString();
        }
        catch
        {
            return "https://www.indeed.com" + href;
        }
    }

    private static async Task<string?> ExtractTextAsync(IElementHandle card, string selector)
    {
        try
        {
            var el = await card.QuerySelectorAsync(selector);
            if (el is null) return null;
            return (await el.InnerTextAsync())?.Trim();
        }
        catch { return null; }
    }

    private static async Task<string?> ExtractAttributeAsync(
        IElementHandle card, string selector, string attribute)
    {
        try
        {
            var el = await card.QuerySelectorAsync(selector);
            if (el is null) return null;
            return await el.GetAttributeAsync(attribute);
        }
        catch { return null; }
    }
}
