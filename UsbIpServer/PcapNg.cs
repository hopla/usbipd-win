﻿// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static UsbIpServer.Interop.UsbIp;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class PcapNg
        : IDisposable
    {
        public PcapNg(IConfiguration config, ILogger<PcapNg> logger)
        {
            Logger = logger;

            var path = config["usbipd:PcapNg:Path"];
            Logger.Debug($"usbipd:PcapNg:Path = '{path}'");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                Stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                Enabled = true;
                PacketWriterTask = Task.Run(() => PacketWriterAsync(Cancellation.Token));
                TimestampBase = (ulong)(DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks);
                Stopwatch.Start();
            }
            catch (IOException ex)
            {
                logger.InternalError("Unable to start capture.", ex);
            }
        }

        public void DumpPacket(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, ReadOnlySpan<byte> data)
        {
            if (!Enabled)
            {
                return;
            }

            var timestamp = GetTimestamp() / 10;  // in micro seconds

            using var usbmon = new BinaryWriter(new MemoryStream());
            usbmon.Write((ulong)basic.seqnum);
            usbmon.Write((byte)'S');
            usbmon.Write((byte)(cmdSubmit.number_of_packets != 0 ? 0 : basic.ep == 0 ? 2 : 3));
            usbmon.Write((byte)(basic.ep | (basic.direction == UsbIpDir.USBIP_DIR_IN ? 0x80u : 0x00u)));
            usbmon.Write((byte)basic.devid);
            usbmon.Write((ushort)(basic.devid >> 16));
            usbmon.Write((byte)(basic.ep == 0 ? '\0' : '-'));
            usbmon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
            usbmon.Write(timestamp / 1000000); // seconds
            usbmon.Write((uint)(timestamp % 1000000)); // micro seconds
            usbmon.Write(-115); // -EINPROGRESS
            usbmon.Write(cmdSubmit.transfer_buffer_length); // length
            usbmon.Write(0); // actual
            if (basic.ep == 0)
            {
                usbmon.Write(cmdSubmit.setup.bmRequestType.B);
                usbmon.Write(cmdSubmit.setup.bRequest);
                usbmon.Write(cmdSubmit.setup.wValue.W);
                usbmon.Write(cmdSubmit.setup.wIndex.W);
                usbmon.Write(cmdSubmit.setup.wLength);
            }
            else
            {
                usbmon.Write(0ul);
            }
            if (cmdSubmit.number_of_packets != 0)
            {
                usbmon.Write(cmdSubmit.interval);
                usbmon.Write(cmdSubmit.start_frame);
            }
            else
            {
                usbmon.Write(0ul);
            }
            usbmon.Write(cmdSubmit.transfer_flags);
            usbmon.Write(cmdSubmit.number_of_packets);
            usbmon.Write(data);

            PacketBlocks.Enqueue(CreateEnhancedPacketBlock(usbmon));
            try
            {
                Semaphore.Release(1);
            }
            catch (SemaphoreFullException) { }
        }

        public void DumpPacket(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, UsbIpHeaderRetSubmit retSubmit, ReadOnlySpan<byte> data)
        {
            if (!Enabled)
            {
                return;
            }

            var timestamp = GetTimestamp() / 10;  // in micro seconds

            using var usbmon = new BinaryWriter(new MemoryStream());
            usbmon.Write((ulong)basic.seqnum);
            usbmon.Write((byte)'C');
            usbmon.Write((byte)(retSubmit.number_of_packets != 0 ? 0 : basic.ep == 0 ? 2 : 3));
            usbmon.Write((byte)(basic.ep | (basic.direction == UsbIpDir.USBIP_DIR_IN ? 0x80u : 0x00u)));
            usbmon.Write((byte)basic.devid);
            usbmon.Write((ushort)(basic.devid >> 16));
            usbmon.Write((byte)'-');
            usbmon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
            usbmon.Write(timestamp / 1000000); // seconds
            usbmon.Write((uint)(timestamp % 1000000)); // micro seconds
            usbmon.Write(retSubmit.status);
            usbmon.Write(cmdSubmit.transfer_buffer_length); // length
            usbmon.Write(retSubmit.actual_length); // actual
            usbmon.Write(0ul); // setup not relevant
            if (cmdSubmit.number_of_packets != 0)
            {
                usbmon.Write(cmdSubmit.interval);
                usbmon.Write(retSubmit.start_frame);
            }
            else
            {
                usbmon.Write(0ul);
            }
            usbmon.Write(cmdSubmit.transfer_flags);
            usbmon.Write(retSubmit.number_of_packets);
            usbmon.Write(data);

            PacketBlocks.Enqueue(CreateEnhancedPacketBlock(usbmon));
            try
            {
                Semaphore.Release(1);
            }
            catch (SemaphoreFullException) { }
        }

        async Task PacketWriterAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Stream.WriteAsync(CreateSectionHeaderBlock(), CancellationToken.None);
                await Stream.WriteAsync(CreateInterfaceDescriptionBlock(), CancellationToken.None);
                while (true)
                {
                    await Semaphore.WaitAsync(cancellationToken);
                    while (PacketBlocks.TryDequeue(out var block))
                    {
                        await Stream.WriteAsync(block, CancellationToken.None);
                        ++TotalPacketsWritten;
                    }
                }
            }
            catch (IOException ex)
            {
                Logger.InternalError("Write failure during capture.", ex);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await Stream.WriteAsync(CreateInterfaceStatisticsBlock(), CancellationToken.None);
                    await Stream.FlushAsync(CancellationToken.None);
                }
                catch (IOException ex)
                {
                    Logger.InternalError("Failure flushing capture.", ex);
                }
            }
            finally
            {
                Enabled = false;
                PacketBlocks.Clear();
                await Stream.DisposeAsync();
            }
        }

        ulong GetTimestamp()
        {
            // Units are 100 ns.
            return TimestampBase + (ulong)Stopwatch.ElapsedTicks * 10000000 / (ulong)Stopwatch.Frequency;
        }

        static void AddOption(BinaryWriter block, ushort code, byte[] data)
        {
            Pad(block);
            block.Write(code);
            block.Write((ushort)data.Length);
            block.Write(data);
        }

        static void AddOption(BinaryWriter block, ushort code, byte value)
        {
            AddOption(block, code, new byte[] { value });
        }

        static void AddOption(BinaryWriter block, ushort code, ulong value)
        {
            AddOption(block, code, BitConverter.GetBytes(value));
        }

        static void AddOption(BinaryWriter block, ushort code, string value)
        {
            AddOption(block, code, Encoding.UTF8.GetBytes(value));
        }

        static byte[] CreateSectionHeaderBlock()
        {
            using var block = CreateBlock(0x0a0d0d0a);
            block.Write(0x1a2b3c4d); // endianness magig
            block.Write((ushort)1); // major pcapng version
            block.Write((ushort)0); // minor pcapng version
            block.Write(0xffffffffffffffff); // unspecified section size
            AddOption(block, 3, $"{Environment.OSVersion.VersionString}"); // shb_os
            AddOption(block, 4, $"{Program.Product} {GitVersionInformation.InformationalVersion}"); // shb_userappl
            return FinishBlock(block);
        }

        static byte[] CreateInterfaceDescriptionBlock()
        {
            using var block = CreateBlock(1);
            block.Write((ushort)220); // LINKTYPE_USB_LINUX_MMAPPED
            block.Write((ushort)0); // reserved
            block.Write(0); // snaplen (unlimited)
            AddOption(block, 2, "USBIP"); // if_name
            AddOption(block, 9, 7); // if_tsresol, 10^-7 s == 100 ns
            return FinishBlock(block);
        }

        byte[] CreateEnhancedPacketBlock(BinaryWriter usbmon)
        {
            var timestamp = GetTimestamp();

            usbmon.Flush();
            var data = ((MemoryStream)usbmon.BaseStream).ToArray();

            using var block = CreateBlock(6);
            block.Write(0); // interface ID
            block.Write((uint)(timestamp >> 32));
            block.Write((uint)timestamp);
            block.Write(data.Length); // captured packet length
            block.Write(data.Length); // original packet length
            block.Write(data);
            return FinishBlock(block);
        }

        byte[] CreateInterfaceStatisticsBlock()
        {
            var timestamp = GetTimestamp();

            using var block = CreateBlock(5);
            block.Write(0); // interface ID
            block.Write((uint)(timestamp >> 32));
            block.Write((uint)timestamp);
            AddOption(block, 2, BitConverter.GetBytes((uint)(TimestampBase >> 32)).Concat(BitConverter.GetBytes((uint)TimestampBase)).ToArray()); // isb_starttime
            AddOption(block, 3, BitConverter.GetBytes((uint)(timestamp >> 32)).Concat(BitConverter.GetBytes((uint)timestamp)).ToArray()); // isb_endtime
            AddOption(block, 4, TotalPacketsWritten); // isb_ifrecv
            AddOption(block, 5, 0ul); // isb_ifdrop
            AddOption(block, 6, TotalPacketsWritten); // isb_filteraccept
            AddOption(block, 7, 0ul); // isb_osdrop
            AddOption(block, 8, TotalPacketsWritten); // isb_usrdeliv
            return FinishBlock(block);
        }

        static void Pad(BinaryWriter block)
        {
            var padding = (4 - block.Seek(0, SeekOrigin.Current)) & 3;
            if (padding != 0)
            {
                block.Write(new byte[padding]);
            }
        }

        static BinaryWriter CreateBlock(uint blockType)
        {
            var block = new BinaryWriter(new MemoryStream());
            block.Write(blockType);
            block.Write(0); // length to be replaced later
            return block;
        }

        static byte[] FinishBlock(BinaryWriter block)
        {
            Pad(block);
            block.Write(0); // opt_endofopt
            var length = (uint)block.Seek(0, SeekOrigin.Current) + 4;
            block.Write(length); // block total length
            block.Seek(4, SeekOrigin.Begin);
            block.Write(length); // block total length
            block.Flush();
            var memoryStream = (MemoryStream)block.BaseStream;
            var result = memoryStream.ToArray();
            block.Close();
            return result;
        }

        bool Enabled;
        ulong TotalPacketsWritten;
        readonly ulong TimestampBase;
        readonly Stopwatch Stopwatch = new();
        readonly ILogger Logger;
        readonly Stream Stream = Stream.Null;
        readonly SemaphoreSlim Semaphore = new(0, 1);
        readonly ConcurrentQueue<byte[]> PacketBlocks = new();
        readonly CancellationTokenSource Cancellation = new();
        readonly Task PacketWriterTask = Task.CompletedTask;

        #region IDisposable

        bool IsDisposed;
        public void Dispose()
        {
            if (!IsDisposed)
            {
                Cancellation.Cancel();
                PacketWriterTask.Wait();
                Cancellation.Dispose();
                Semaphore.Dispose();
                Stream.Dispose();
                IsDisposed = true;
            }
        }

        #endregion
    }
}
