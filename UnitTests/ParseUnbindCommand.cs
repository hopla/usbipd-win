﻿using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using UsbIpServer;

namespace UnitTests
{
    using ExitCode = Program.ExitCode;

    [TestClass]
    public class ParseUnbindCommand
        : ParseTest
    {
        static readonly BusId TestBusId = BusId.Parse("3-42");
        static readonly Guid TestGuid = Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");

        [TestMethod]
        public void AllSuccess()
        {
            var mock = CreateMock();
            mock.Setup(m => m.UnbindAll(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));

            Test(ExitCode.Success, mock, "unbind", "--all");
        }

        [TestMethod]
        public void AllFailure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.UnbindAll(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(false));

            Test(ExitCode.Failure, mock, "unbind", "--all");
        }

        [TestMethod]
        public void BusIdSuccess()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Unbind(It.Is<BusId>(busId => busId == TestBusId),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));

            Test(ExitCode.Success, mock, "unbind", "--bus-id", TestBusId.ToString());
        }

        [TestMethod]
        public void BusIdFailure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Unbind(It.Is<BusId>(busId => busId == TestBusId),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(false));

            Test(ExitCode.Failure, mock, "unbind", "--bus-id", TestBusId.ToString());
        }

        [TestMethod]
        public void GuidSuccess()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Unbind(It.Is<Guid>(guid => guid == Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}")),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));

            Test(ExitCode.Success, mock, "unbind", "--guid", "{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");
        }

        [TestMethod]
        public void GuidFailure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Unbind(It.Is<Guid>(guid => guid == Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}")),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(false));

            Test(ExitCode.Failure, mock, "unbind", "--guid", "{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "unbind", "--help");
        }

        [TestMethod]
        public void OptionMissing()
        {
            Test(ExitCode.ParseError, "unbind");
        }

        [TestMethod]
        public void AllAndBusId()
        {
            Test(ExitCode.ParseError, "unbind", "--all", "--bus-id", TestBusId.ToString());
        }

        [TestMethod]
        public void AllAndGuid()
        {
            Test(ExitCode.ParseError, "unbind", "--all", "--guid", TestGuid.ToString());
        }

        [TestMethod]
        public void BusIdAndGuid()
        {
            Test(ExitCode.ParseError, "unbind", "--bus-id", TestBusId.ToString(), "--guid", TestGuid.ToString());
        }

        [TestMethod]
        public void AllAndBusIdAndGuid()
        {
            Test(ExitCode.ParseError, "unbind", "--all", "--bus-id", TestBusId.ToString(), "--guid", TestGuid.ToString());
        }

        [TestMethod]
        public void AllWithArgument()
        {
            Test(ExitCode.ParseError, "unbind", "--all=argument");
        }

        [TestMethod]
        public void BusIdArgumentMissing()
        {
            Test(ExitCode.ParseError, "unbind", "--bus-id");
        }

        [TestMethod]
        public void GuidArgumentMissing()
        {
            Test(ExitCode.ParseError, "unbind", "--guid");
        }

        [TestMethod]
        public void BusIdArgumentInvalid()
        {
            Test(ExitCode.ParseError, "unbind", "--bus-id", "not-a-bus-id");
        }

        [TestMethod]
        public void GuidArgumentInvalid()
        {
            Test(ExitCode.ParseError, "unbind", "--guid", "not-a-guid");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "unbind", "--bus-id", TestBusId.ToString(), "stray-argument");
        }
    }
}
