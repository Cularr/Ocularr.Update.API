using NUnit.Framework;
using Ocularr.Update.API.Release.Github;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class ReleaseNotesFixture
    {
        private const string GeneratedNotes =
            "## What's Changed\r\n" +
            "* New: MangaBaka import list by @someone in https://github.com/Cularr/Ocularr/pull/12\r\n" +
            "* Fixed: Volume sorting by @other in https://github.com/Cularr/Ocularr/pull/13\r\n" +
            "* Translations update from Weblate by @weblate in https://github.com/Cularr/Ocularr/pull/14\r\n" +
            "* feat(lists): Add search by @someone in https://github.com/Cularr/Ocularr/pull/15\r\n" +
            "* fix!: Drop old token by @other in https://github.com/Cularr/Ocularr/pull/16\r\n" +
            "\r\n" +
            "**Full Changelog**: https://github.com/Cularr/Ocularr/compare/v5.0.0.1...v5.0.0.2";

        private const string ServarrNotes =
            "* 0123456789abcdef0123456789abcdef01234567 New: Calendar view\n" +
            "* 0123456789abcdef0123456789abcdef01234567 Fix: Crash on start\n" +
            "* 0123456789abcdef0123456789abcdef01234567 (feat): Series search\n";

        [Test]
        public void generated_notes_drop_author_and_link() =>
            Assert.Multiple(() =>
            {
                Assert.That(GithubReleaseSource.Features(GeneratedNotes), Is.EqualTo(new[] { "MangaBaka import list", "Add search" }));
                Assert.That(GithubReleaseSource.Fixes(GeneratedNotes), Is.EqualTo(new[] { "Volume sorting", "Drop old token" }));
            });

        [Test]
        public void commit_notes_are_still_read() =>
            Assert.Multiple(() =>
            {
                Assert.That(GithubReleaseSource.Features(ServarrNotes), Is.EqualTo(new[] { "Calendar view", "Series search" }));
                Assert.That(GithubReleaseSource.Fixes(ServarrNotes), Is.EqualTo(new[] { "Crash on start" }));
            });

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Some text New: not a bullet")]
        [TestCase("See * New: inline * Fixed: star")]
        public void notes_without_bullets_are_empty(string body) =>
            Assert.Multiple(() =>
            {
                Assert.That(GithubReleaseSource.Features(body), Is.Empty);
                Assert.That(GithubReleaseSource.Fixes(body), Is.Empty);
            });
    }
}
