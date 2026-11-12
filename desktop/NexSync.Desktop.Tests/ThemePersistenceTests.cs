using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class ThemePersistenceTests
{
    private string _dir = null!;
    private string _dataDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-theme-test", Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_dir, "data");
        Directory.CreateDirectory(_dataDir);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // Answers every request with {} so the sync loop swallows parse errors and never hits the network.
    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
    }

    [TestMethod]
    public void FreshInstall_DefaultsToLightMode()
    {
        using var services = new DesktopServices("http://localhost:5189", _dataDir, new OkHandler());
        Assert.IsFalse(services.State.IsDarkMode, "No theme file means light mode.");
    }

    [TestMethod]
    public void DarkMode_ToggledOnce_PersistsAcrossRestart()
    {
        using (var first = new DesktopServices("http://localhost:5189", _dataDir, new OkHandler()))
        {
            first.State.SetDarkMode(true);
            Assert.IsTrue(first.State.IsDarkMode);
        }

        Assert.AreEqual("dark", File.ReadAllText(Path.Combine(_dataDir, "theme.txt")).Trim(),
            "Toggling must write the theme file.");

        using var second = new DesktopServices("http://localhost:5189", _dataDir, new OkHandler());
        Assert.IsTrue(second.State.IsDarkMode, "A new instance must load the saved theme.");
    }
}
