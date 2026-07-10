// <copyright file="LinuxSecurityParserTests.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Services.Sources.Linux.Security;
using Logship.Agent.FileBasedTests.Utility;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logship.Agent.FileBasedTests.Security
{
    [TestClass]
    public sealed class LinuxSecurityParserTests
    {
        [TestMethod]
        public void AuthParserParsesSuccessfulSshLogin()
        {
            var parsed = LinuxAuthEventParser.TryParse(
                "Jan 12 10:11:12 host sshd[123]: Accepted publickey for alice from 203.0.113.10 port 52022 ssh2",
                "/var/log/auth.log");

            Assert.IsNotNull(parsed);
            Assert.AreEqual("ssh_login", parsed["EventType"]);
            Assert.AreEqual("success", parsed["Result"]);
            Assert.AreEqual("publickey", parsed["AuthMethod"]);
            Assert.AreEqual("alice", parsed["User"]);
            Assert.AreEqual("203.0.113.10", parsed["SourceAddress"]);
            Assert.AreEqual("52022", parsed["SourcePort"]);
        }

        [TestMethod]
        public void AuthParserParsesFailedInvalidSshLogin()
        {
            var parsed = LinuxAuthEventParser.TryParse(
                "Jan 12 10:11:12 host sshd[123]: Failed password for invalid user deploy from 203.0.113.11 port 52023 ssh2",
                "/var/log/auth.log");

            Assert.IsNotNull(parsed);
            Assert.AreEqual("ssh_login", parsed["EventType"]);
            Assert.AreEqual("failure", parsed["Result"]);
            Assert.AreEqual("deploy", parsed["User"]);
            Assert.AreEqual(true, parsed["InvalidUser"]);
        }

        [TestMethod]
        public void AuthParserParsesSudoCommand()
        {
            var parsed = LinuxAuthEventParser.TryParse(
                "Jan 12 10:11:12 host sudo: alice : TTY=pts/0 ; PWD=/home/alice ; USER=root ; COMMAND=/usr/bin/id",
                "/var/log/auth.log");

            Assert.IsNotNull(parsed);
            Assert.AreEqual("sudo_command", parsed["EventType"]);
            Assert.AreEqual("alice", parsed["User"]);
            Assert.AreEqual("root", parsed["TargetUser"]);
            Assert.AreEqual("/usr/bin/id", parsed["Command"]);
        }

        [TestMethod]
        public void AuthParserKeepsGenericPamFailureAsPamEvent()
        {
            var parsed = LinuxAuthEventParser.TryParse(
                "Jan 12 10:11:12 host sshd[123]: pam_unix(sshd:auth): authentication failure; logname= uid=0 euid=0 tty=ssh ruser= rhost=203.0.113.12 user=root",
                "/var/log/auth.log");

            Assert.IsNotNull(parsed);
            Assert.AreEqual("pam_auth", parsed["EventType"]);
            Assert.AreEqual("sshd", parsed["Service"]);
            Assert.AreEqual("root", parsed["User"]);
            Assert.AreEqual("203.0.113.12", parsed["SourceAddress"]);
        }

        [TestMethod]
        public void SshConfigParserReadsIncludesAndUsesLaterOverride()
        {
            using var temp = new TempDirectory();
            var sshDir = Path.Combine(temp.Path, "ssh");
            var includeDir = Path.Combine(sshDir, "sshd_config.d");
            Directory.CreateDirectory(includeDir);

            var configPath = Path.Combine(sshDir, "sshd_config");
            File.WriteAllText(configPath, """
                PasswordAuthentication yes
                Include sshd_config.d/*.conf
                PermitRootLogin yes
                """);

            File.WriteAllText(Path.Combine(includeDir, "hardening.conf"), """
                PasswordAuthentication no
                PermitEmptyPasswords no
                """);

            var parsed = LinuxSshConfigParser.Parse(configPath);

            Assert.AreEqual("no", parsed.Settings["PasswordAuthentication"]);
            Assert.AreEqual("yes", parsed.Settings["PermitRootLogin"]);
            Assert.AreEqual("no", parsed.Settings["PermitEmptyPasswords"]);
            Assert.AreEqual(2, parsed.FilesRead.Count);
        }
    }
}
