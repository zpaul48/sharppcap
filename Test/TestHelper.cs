using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System.Diagnostics;

namespace Test
{
    internal static class TestHelper
    {
        public static string GetFile(string name)
        {
            var assembly = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(assembly, "capture_files", name);
        }


        /// <summary>
        /// Find the first Ethernet adapter that is actually connected to something
        /// </summary>
        /// <returns></returns>
        internal static PcapDevice GetPcapDevice()
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var device in LibPcapLiveDeviceList.Instance)
            {
                var friendlyName = device.Interface.FriendlyName ?? string.Empty;
                if (friendlyName.ToLower().Contains("loopback") || friendlyName == "any")
                {
                    continue;
                }
                var nic = nics.FirstOrDefault(ni => ni.Name == friendlyName);
                if (nic?.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                LinkLayers link;
                try
                {
                    device.Open();
                    link = device.LinkType;
                }
                catch (PcapException ex)
                {
                    Console.WriteLine(ex);
                    continue;
                }
                finally
                {
                    if (device.Opened) device.Close();
                }

                if (link == LinkLayers.Ethernet)
                {
                    return device;
                }
            }
            throw new InvalidOperationException("No ethernet pcap supported devices found, are you running" +
                                           " as a user with access to adapters (root on Linux)?");
        }

        /// <summary>
        /// Run a test routine, and report what packets were captured during that routine
        /// </summary>
        /// <param name="filter">to avoid noise from OS affecting test result, a filter is needed</param>
        /// <param name="routine">the routine to run</param>
        /// <returns></returns>
        internal static List<RawCapture> RunCapture(string filter, Action<PcapDevice> routine)
        {
            var device = GetPcapDevice();
            Console.WriteLine($"Using device {device}");
            var received = new List<RawCapture>();
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                device.Open(DeviceMode.Promiscuous, 1);
            }
            else
            {
                device.Open(DeviceMode.Normal, 1);
            }
            device.Filter = filter;
            // We can't use the same device for capturing and sending in Linux
            // var sender = new LibPcapLiveDevice(device.Interface);
            // sender.Open(DeviceMode.Promiscuous, 1000);
            try
            {
                routine(device);
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    var raw = device.GetNextPacket();
                    if (raw != null)
                    {
                        var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
                        Console.WriteLine($"Received: {packet} after {sw.Elapsed} (at {raw.Timeval})");
                        received.Add(raw);
                    }
                    else
                    {
                        Console.WriteLine($"Received: null packet after {sw.Elapsed})");
                        if (sw.ElapsedMilliseconds > 20000)
                        {
                            // No more packets in queue, and 2 seconds has passed
                            break;
                        }
                    }

                }
                /*
                while (true)
                {
                    var raw = sender.GetNextPacket();
                    if (raw != null)
                    {
                        var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
                        Console.WriteLine($"Sent: {packet} after {sw.Elapsed} (at {raw.Timeval})");
                    }
                    else
                    {
                        Console.WriteLine($"Sent: null packet after {sw.Elapsed})");
                        if (sw.ElapsedMilliseconds > 20000)
                        {
                            // No more packets in queue, and 2 seconds has passed
                            break;
                        }
                    }

                }
                */
                Console.WriteLine(device.Statistics);
                // Console.WriteLine(sender.Statistics);
            }
            finally
            {
                // sender.Close();
                device.Close();
            }
            return received;
        }

        internal static void ConfirmIdleState()
        {
            var devices = DeviceFixture.GetDevices().OfType<PcapDevice>();
            foreach (var d in devices)
            {
                var isOpened = d.Opened;
                var isStarted = d.Started;
                if (isStarted) d.StopCapture();
                if (isOpened) d.Close();
                var status = TestContext.CurrentContext.Result.Outcome.Status;
                // If test already failed, no point asserting here
                if (status != TestStatus.Failed)
                {
                    Assert.IsFalse(isOpened, "Expected device to not to be Opened");
                    Assert.IsFalse(isStarted, "Expected device to not be Started");
                }
            }
        }
    }
}
