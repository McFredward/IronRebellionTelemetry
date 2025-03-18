using System.Net;
using System.Net.Sockets;
using System.Threading;
using System;
using BepInEx.Logging;
using System.Runtime.InteropServices;

namespace IronRebellionTelemetry
{
    public class TelemetrySender //: IDisposable
    {
        private static Thread senderThread;
        private static bool isRunning = false;
        private static UdpClient udpClient;
        private static int port = 6969; 
        private static string targetIP = "127.0.0.1";
        private static int interval = 20;

        private static int stompedIterations = 6; //How many iterations the the stomped signal should be set to true
        private static int stompedCounter = 0;
        private static int landedIterations = 6;
        private static int landedCounter = 0;
        private static int jumpedIterations = 6;
        private static int jumpedCounter = 0;
        private static int weaponFiredIterations = 6;
        private static int weaponFiredCounter = 0;


        
        public static void Start()
        {
            if (isRunning) return;

            isRunning = true;
            udpClient = new UdpClient();
            senderThread = new Thread(SendTelemetry);
            senderThread.IsBackground = true;
            senderThread.Start();

            BepInExPlugin.Log.LogInfo("TelemetrySender started");
        }

        public static void Stop()
        {
            isRunning = false;
            udpClient?.Close();
            BepInExPlugin.Log.LogInfo("TelemetrySender stopped");
        }

        public static void SendTelemetry()
        {
            while (isRunning)
            {
                try
                {
                    TelemetryData telemetry = BepInExPlugin.telemetry;
                    var data = ToBytes(telemetry);

                    udpClient.Send(data, data.Length, targetIP, port);

                    if (telemetry.stomped)
                    {
                        stompedCounter++;                        
                    }
                    
                    if (telemetry.landed) landedCounter++;
                    
                    if (telemetry.jumped) jumpedCounter++;
                    
                    if (telemetry.weaponFired) weaponFiredCounter++;
                    

                    if (telemetry.stomped && stompedCounter > stompedIterations)
                    {
                        BepInExPlugin.stompedSend = true;
                        stompedCounter = 0;
                    }

                    if (telemetry.landed && landedCounter > landedIterations)
                    {
                        BepInExPlugin.landedSend = true;
                        landedCounter = 0;
                    }

                    if (telemetry.jumped && jumpedCounter > jumpedIterations)
                    {
                        BepInExPlugin.jumpedSend = true;
                        jumpedCounter = 0;
                    }

                    if (telemetry.weaponFired && weaponFiredCounter > weaponFiredIterations)
                    {
                        BepInExPlugin.weaponFiredSend = true;
                        weaponFiredCounter = 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInExPlugin.Log.LogError($"TelemetrySender Error: {ex.Message}");
                }

                Thread.Sleep(interval);
            }
        }

        static byte[] ToBytes<T>(T data) where T : struct
        {
            int size = Marshal.SizeOf(data);
            byte[] arr = new byte[size];
            using (SafeBuffer buffer = new SafeBuffer(size))
            {
                Marshal.StructureToPtr(data, buffer.DangerousGetHandle(), true);
                Marshal.Copy(buffer.DangerousGetHandle(), arr, 0, size);
            }
            return arr;
        }

        public void Dispose()
        {
            Stop();
        }

        internal class SafeBuffer : SafeHandle
        {
            public SafeBuffer(int size) : base(IntPtr.Zero, true)
            {
                SetHandle(Marshal.AllocHGlobal(size));
            }

            public override bool IsInvalid => handle == IntPtr.Zero;

            protected override bool ReleaseHandle()
            {
                Marshal.FreeHGlobal(handle);
                return true;
            }
        }
    }
}
