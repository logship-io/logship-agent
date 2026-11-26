using System.Runtime.InteropServices;
using Logship.Agent.Core.Services.Sources.Linux.Proc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logship.Agent.FileBasedTests.SystemEvents;

[TestClass]
public sealed class ProcClockTickTests
{
    [TestMethod]
    public void ReadsClockTicksFromNativeAuxiliaryVector()
    {
        nuint[] entries = [6, 4096, 17, 250, 0, 0];
        using var vector = new MemoryStream(MemoryMarshal.AsBytes(entries.AsSpan()).ToArray());

        Assert.AreEqual(250, ProcHelpers.ReadClockTicks(vector));
    }

    [TestMethod]
    public void MissingOrTruncatedClockTickEntryDoesNotReturnAFrequency()
    {
        nuint[] entries = [6, 4096, 17];
        using var vector = new MemoryStream(MemoryMarshal.AsBytes(entries.AsSpan()).ToArray());

        Assert.IsNull(ProcHelpers.ReadClockTicks(vector));
    }

    [TestMethod]
    public void LinuxExposesClockTicksInProc()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Requires Linux procfs.");
        }

        using var vector = File.OpenRead("/proc/self/auxv");
        Assert.IsTrue(ProcHelpers.ReadClockTicks(vector) > 0);
    }
}
