using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class OAuthIconFixture
    {
        private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";
        private const string Secret = "outside-the-icons-directory";

        private FakeTokenEndpoint _tokens;

        [SetUp]
        public void SetUp() => _tokens = new FakeTokenEndpoint();

        [Test]
        public async Task a_local_icon_is_shown_on_the_page_without_opening_another_host()
        {
            using var factory = Create("baka.svg");
            var client = factory.Client();
            await File.WriteAllTextAsync(Path.Combine(Icons(factory), "baka.svg"), Svg);
            var authorize = await client.GetAsync($"/v1/oauth/mangabaka/authorize?state={Uri.EscapeDataString(OAuthService.Callback)}");
            var state = OAuthService.Query(authorize.Headers.Location.ToString())["state"];

            var response = await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}");
            var page = await response.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That(page, Does.Contain("<img src=\"/v1/oauth/icons/baka.svg\""));
                Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("img-src 'self';"));
            });
        }

        [TestCase(null, "round")]
        [TestCase("Round", "round")]
        [TestCase("Fill", "fill")]
        [TestCase("Square", "square")]
        [TestCase("wide", "wide")]
        public async Task the_icon_box_takes_the_configured_shape(string shape, string expected)
        {
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:IconUrl"] = "baka.svg", ["OAuth:Providers:mangabaka:IconShape"] = shape });

            var response = await factory.Client().GetAsync("/v1/oauth/mangabaka/auth?state=x");
            var page = await response.Content.ReadAsStringAsync();

            Assert.That(page, Does.Contain($"<div class=\"mark {expected}\">"));
        }

        [TestCase("Oval")]
        [TestCase("7")]
        [TestCase("+1")]
        [TestCase(" Wide")]
        [TestCase("Wide ")]
        [TestCase("Round,Wide")]
        public void an_unknown_icon_shape_stops_the_service_from_starting(string shape)
        {
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:IconShape"] = shape });

            Assert.That(() => factory.CreateClient(), Throws.Exception.With.Message.Contains("IconShape"));
        }

        [Test]
        public async Task an_icon_is_served_as_an_image_that_cannot_run_scripts()
        {
            using var factory = Create("baka.svg");
            var client = factory.Client();
            await File.WriteAllTextAsync(Path.Combine(Icons(factory), "baka.svg"), Svg);

            var response = await client.GetAsync("/v1/oauth/icons/baka.svg");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo("image/svg+xml"));
                Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
                Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Is.EqualTo("default-src 'none'; style-src 'unsafe-inline'; sandbox"));
            });
        }

        [TestCase("notes.txt")]
        [TestCase("page.html")]
        [TestCase("script.js")]
        [TestCase("baka.svg.bak")]
        [TestCase("noextension")]
        public async Task only_image_types_are_served(string name)
        {
            using var factory = Create("baka.svg");
            var client = factory.Client();
            await File.WriteAllTextAsync(Path.Combine(Icons(factory), name), Secret);

            var response = await client.GetAsync($"/v1/oauth/icons/{name}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [TestCase("../outside.svg")]
        [TestCase("..%2Foutside.svg")]
        [TestCase("%2e%2e/outside.svg")]
        [TestCase("%2e%2e%2foutside.svg")]
        [TestCase("..%5Coutside.svg")]
        [TestCase("..\\outside.svg")]
        [TestCase("%252e%252e%252foutside.svg")]
        [TestCase("....//outside.svg")]
        [TestCase("sub/../../outside.svg")]
        [TestCase("baka.svg%00.png")]
        [TestCase("/outside.svg")]
        public async Task a_path_cannot_leave_the_icons_directory(string path)
        {
            using var factory = Create("baka.svg");
            var client = factory.Client();
            await File.WriteAllTextAsync(Path.Combine(factory.DataDirectory, "outside.svg"), Secret);
            await File.WriteAllTextAsync(Path.Combine(Icons(factory), "baka.svg"), Svg);

            var context = await factory.Server.SendAsync(x =>
            {
                x.Request.Method = HttpMethods.Get;
                x.Request.Path = new PathString($"/v1/oauth/icons/{Uri.UnescapeDataString(path)}");
            });
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
                Assert.That(body, Does.Not.Contain(Secret));
            });
        }

        [TestCase("../baka.svg")]
        [TestCase("..\\baka.svg")]
        [TestCase("sub/baka.svg")]
        [TestCase("/baka.svg")]
        [TestCase(".baka.svg")]
        [TestCase("baka.html")]
        [TestCase("baka")]
        [TestCase("baka.svg ")]
        [TestCase("http://icons.test/baka.svg")]
        [TestCase("//icons.test/baka.svg")]
        [TestCase("javascript:alert(1).svg")]
        public void an_icon_that_is_not_a_plain_image_name_or_https_url_stops_the_service_from_starting(string icon)
        {
            using var factory = Create(icon);

            var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
            Assert.That(exception.Message, Does.Contain("OAuth:Providers:mangabaka"));
        }

        [TestCase("Baka-1_x.PNG", "image/png")]
        [TestCase("baka.SVG", "image/svg+xml")]
        [TestCase("baka.webp", "image/webp")]
        [TestCase("baka.jpg", "image/jpeg")]
        [TestCase("baka.ico", "image/x-icon")]
        public async Task every_accepted_icon_name_is_also_served(string name, string type)
        {
            using var factory = Create(name);
            var client = factory.Client();
            await File.WriteAllTextAsync(Path.Combine(Icons(factory), name), Svg);

            var response = await client.GetAsync($"/v1/oauth/icons/{name}");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(type));
            });
        }

        private static string Icons(ServiceFactory factory) => Path.Combine(factory.DataDirectory, "icons");

        private ServiceFactory Create(string icon)
        {
            var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:IconUrl"] = icon });
            Directory.CreateDirectory(Icons(factory));
            return factory;
        }
    }
}
