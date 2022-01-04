﻿using System.CommandLine.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using UsbIpServer;

namespace UnitTests
{
    public abstract class ParseTest
    {
        internal static Mock<ICommandHandlers> CreateMock() => new(MockBehavior.Strict);

        internal static void Test(Program.ExitCode expect, params string[] args)
        {
            Test(expect, CreateMock(), args);
        }

        internal static void Test(Program.ExitCode expect, Mock<ICommandHandlers> mock, params string[] args)
        {
            Assert.AreEqual(MockBehavior.Strict, mock.Behavior);
            var exitCode = Program.Run(new TestConsole(), mock.Object, args);
            Assert.AreEqual(expect, exitCode);
            mock.VerifyAll();
        }
    }
}
