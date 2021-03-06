using System.IO;
using System.Threading.Tasks;
using PlaywrightSharp.Tests.Attributes;
using PlaywrightSharp.Tests.BaseTests;
using Xunit;
using Xunit.Abstractions;

namespace PlaywrightSharp.Tests.Page
{
    ///<playwright-file>page.spec.js</playwright-file>
    ///<playwright-describe>Page.addScriptTag</playwright-describe>
    public class PageAddScriptTagTests : PlaywrightSharpPageBaseTest
    {
        internal PageAddScriptTagTests(ITestOutputHelper output) : base(output)
        {
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should throw an error if no options are provided</playwright-it>
        [Fact(Skip = "Not relevant for C#, js specific")]
        public void ShouldThrowAnErrorIfNoOptionsAreProvided()
        {
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should work with a url</playwright-it>
        [Fact]
        public async Task ShouldWorkWithAUrl()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var scriptHandle = await Page.AddScriptTagAsync(new AddTagOptions { Url = "/injectedfile.js" });
            Assert.NotNull(scriptHandle);
            Assert.Equal(42, await Page.EvaluateAsync<int>("() => __injected"));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should work with a url and type=module</playwright-it>
        [Fact]
        public async Task ShouldWorkWithAUrlAndTypeModule()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            await Page.AddScriptTagAsync(new AddTagOptions { Url = "/es6/es6import.js", Type = "module" });
            Assert.Equal(42, await Page.EvaluateAsync<int>("() => __es6injected"));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should work with a path and type=module</playwright-it>
        [Fact]
        public async Task ShouldWorkWithAPathAndTypeModule()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            await Page.AddScriptTagAsync(new AddTagOptions { Path = Path.Combine(Directory.GetCurrentDirectory(), "assets/es6/es6pathimport.js"), Type = "module" });
            await Page.WaitForFunctionAsync("window.__es6injected");
            Assert.Equal(42, await Page.EvaluateAsync<int>("() => __es6injected"));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should work with a content and type=module</playwright-it>
        [Fact]
        public async Task ShouldWorkWithAContentAndTypeModule()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            await Page.AddScriptTagAsync(new AddTagOptions { Content = "import num from '/es6/es6module.js'; window.__es6injected = num;", Type = "module" });
            await Page.WaitForFunctionAsync("window.__es6injected");
            Assert.Equal(42, await Page.EvaluateAsync<int>("() => __es6injected"));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should throw an error if loading from url fail</playwright-it>
        [Fact]
        public async Task ShouldThrowAnErrorIfLoadingFromUrlFail()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            await Assert.ThrowsAsync<PlaywrightSharpException>(() => Page.AddScriptTagAsync(new AddTagOptions { Url = "/nonexistfile.js" }));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should work with a path</playwright-it>
        [Fact]
        public async Task ShouldWorkWithAPath()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var scriptHandle = await Page.AddScriptTagAsync(new AddTagOptions { Path = Path.Combine(Directory.GetCurrentDirectory(), "assets/injectedfile.js") });
            Assert.NotNull(scriptHandle);
            Assert.Equal(42, await Page.EvaluateAsync<int>("() => __injected"));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should include sourceURL when path is provided</playwright-it>
        [Fact]
        public async Task ShouldIncludeSourceURLWhenPathIsProvided()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            await Page.AddScriptTagAsync(new AddTagOptions { Path = Path.Combine(Directory.GetCurrentDirectory(), "assets/injectedfile.js") });
            string result = await Page.EvaluateAsync<string>("() => __injectedError.stack");
            Assert.Contains(Path.Combine("assets", "injectedfile.js"), result);
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should work with content</playwright-it>
        [Fact]
        public async Task ShouldWorkWithContent()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var scriptHandle = await Page.AddScriptTagAsync(new AddTagOptions { Content = "window.__injected = 35;" });
            Assert.NotNull(scriptHandle);
            Assert.Equal(35, await Page.EvaluateAsync<int>("() => __injected"));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should throw when added with content to the CSP page</playwright-it>
        [SkipBrowserAndPlatformFact(skipFirefox: true)]
        public async Task ShouldThrowWhenAddedWithContentToTheCSPPage()
        {
            await Page.GoToAsync(TestConstants.ServerUrl + "/csp.html");
            await Assert.ThrowsAsync<PlaywrightSharpException>(() =>
                Page.AddScriptTagAsync(new AddTagOptions { Content = "window.__injected = 35;" }));
        }

        ///<playwright-file>page.spec.js</playwright-file>
        ///<playwright-describe>Page.addScriptTag</playwright-describe>
        ///<playwright-it>should throw when added with URL to the CSP page</playwright-it>
        [Fact]
        public async Task ShouldThrowWhenAddedWithURLToTheCSPPage()
        {
            await Page.GoToAsync(TestConstants.ServerUrl + "/csp.html");
            await Assert.ThrowsAsync<PlaywrightSharpException>(() =>
                Page.AddScriptTagAsync(new AddTagOptions { Url = TestConstants.CrossProcessUrl + "/injectedfile.js" }));
        }
    }
}
