﻿using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using UsbIpServer;

namespace UnitTests
{
    using ExitCode = Program.ExitCode;

    [TestClass]
    public sealed class ParseBindCommand
        : ParseTest
    {
        static readonly BusId TestBusId = BusId.Parse("3-42");

        [TestMethod]
        public void Success()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));

            Test(ExitCode.Success, mock, "bind", "--bus-id", TestBusId.ToString());
        }

        [TestMethod]
        public void Failure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(false));

            Test(ExitCode.Failure, mock, "bind", "--bus-id", TestBusId.ToString());
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "bind", "--help");
        }

        [TestMethod]
        public void BusIdOptionMissing()
        {
            Test(ExitCode.ParseError, "bind");
        }

        [TestMethod]
        public void BusIdArgumentMissing()
        {
            Test(ExitCode.ParseError, "bind", "--bus-id");
        }

        [TestMethod]
        public void BusIdArgumentInvalid()
        {
            Test(ExitCode.ParseError, "bind", "--bus-id", "not-a-bus-id");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "bind", "--bus-id", TestBusId.ToString(), "stray-argument");
        }
    }
}
