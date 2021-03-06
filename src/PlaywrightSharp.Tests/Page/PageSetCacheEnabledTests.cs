using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using PlaywrightSharp.Tests.BaseTests;
using Xunit;
using Xunit.Abstractions;

namespace PlaywrightSharp.Tests.Page
{
    ///<playwright-file>page.spec.js</playwright-file>
    ///<playwright-describe>Page.setCacheEnabled</playwright-describe>
    public class PageSetCacheEnabledTests : PlaywrightSharpPageBaseTest
    {
        internal PageSetCacheEnabledTests(ITestOutputHelper output) : base(output)
        {
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.setCacheEnabled</playwright-describe>
        ///<playwright-it>should enable or disable the cache based on the state passed</playwright-it>
        [Fact]
        public async Task ShouldEnableOrDisableTheCacheBasedOnTheStatePassed()
        {
            await Page.GoToAsync(TestConstants.ServerUrl + "/cached/one-style.html");
            // WebKit does r.setCachePolicy(ResourceRequestCachePolicy::ReloadIgnoringCacheData);
            // when navigating to the same url, load empty.html to avoid that.
            await Page.GoToAsync(TestConstants.EmptyPage);
            var (cachedRequestIfModifiedSinceHeader, _) = await TaskUtils.WhenAll(
                Server.WaitForRequest("/cached/one-style.html", request => request.Headers["if-modified-since"]),
                Page.GoToAsync(TestConstants.ServerUrl + "/cached/one-style.html")
            );
            // Rely on "if-modified-since" caching in our test server.
            Assert.NotEqual(StringValues.Empty, cachedRequestIfModifiedSinceHeader);

            await Page.SetCacheEnabledAsync(false);
            await Page.GoToAsync(TestConstants.EmptyPage);
            var (nonCachedRequestIfModifiedSinceHeader, _) = await TaskUtils.WhenAll(
                Server.WaitForRequest("/cached/one-style.html", request => request.Headers["if-modified-since"]),
                Page.GoToAsync(TestConstants.ServerUrl + "/cached/one-style.html")
            );
            Assert.Equal(StringValues.Empty, nonCachedRequestIfModifiedSinceHeader);
        }
    }
}
