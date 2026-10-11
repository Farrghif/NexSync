using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class AppStateTests
{
    [TestMethod]
    public void StartsOnLoginScreen()
    {
        var state = new AppState();
        Assert.AreEqual(AppScreen.Login, state.CurrentScreen);
        Assert.IsNull(state.LocalRoot);
        Assert.IsFalse(state.SyncRequested);
    }

    [TestMethod]
    public void NavigatesLogin_Setup_Status()
    {
        var state = new AppState();
        var changes = 0;
        state.Changed += () => changes++;

        state.GoTo(AppScreen.Setup);
        Assert.AreEqual(AppScreen.Setup, state.CurrentScreen);

        state.SetLocalRoot(@"C:\NexSync");
        Assert.AreEqual(@"C:\NexSync", state.LocalRoot);

        state.GoTo(AppScreen.Status);
        Assert.AreEqual(AppScreen.Status, state.CurrentScreen);
        Assert.IsTrue(changes >= 3);
    }

    [TestMethod]
    public void SyncRequest_CanBeSignalledAndCleared()
    {
        var state = new AppState();
        state.RequestSync();
        Assert.IsTrue(state.SyncRequested);
        state.ClearSyncRequest();
        Assert.IsFalse(state.SyncRequested);
    }
}

[TestClass]
public sealed class SingleInstanceTests
{
    [TestMethod]
    public void SecondAcquisitionFailsWhileFirstHeld()
    {
        using var first = new SingleInstance(@"Global\NexSync-Test-Hold");
        Assert.IsTrue(first.TryAcquire());

        using var second = new SingleInstance(@"Global\NexSync-Test-Hold");
        Assert.IsFalse(second.TryAcquire());
    }

    [TestMethod]
    public void AcquisitionSucceedsAfterRelease()
    {
        var first = new SingleInstance(@"Global\NexSync-Test-Release");
        Assert.IsTrue(first.TryAcquire());
        first.Dispose();

        using var second = new SingleInstance(@"Global\NexSync-Test-Release");
        Assert.IsTrue(second.TryAcquire());
    }
}
