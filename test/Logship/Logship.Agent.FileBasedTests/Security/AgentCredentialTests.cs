// Copyright © Logship LLC. All rights reserved.
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Internals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logship.Agent.FileBasedTests.Security;

[TestClass]
public sealed class AgentCredentialTests
{
    [TestMethod]
    public async Task ConcurrentEnrollmentAndRestartPreservePrivateCredentials()
    {
        var directory = Directory.CreateTempSubdirectory();
        var account = Guid.NewGuid();
        LocalStorage Open() => new(NullLogger<LocalStorage>.Instance, Options.Create(new OutputConfiguration { DataPath = directory.FullName }));
        try
        {
            var proofs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Open().GetEnrollmentProofAsync(account, CancellationToken.None)));
            Assert.AreEqual(1, proofs.Distinct().Count());
            Assert.AreEqual(64, proofs[0].Length);
            Assert.AreNotEqual(proofs[0], await Open().GetEnrollmentProofAsync(Guid.NewGuid(), CancellationToken.None));
            await Open().StoreTokenAsync("first", CancellationToken.None);
            await Open().StoreTokenAsync("renewed", CancellationToken.None);
            Assert.AreEqual("renewed", await Open().RetrieveTokenAsync(CancellationToken.None));
        }
        finally { directory.Delete(recursive: true); }
    }
}
