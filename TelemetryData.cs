using System.Runtime.InteropServices;

namespace IronRebellionTelemetry
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TelemetryData
    {
        public float velocityX;
        public float velocityY;
        public float velocityZ;

        public float angularX;
        public float angularY;
        public float angularZ;

        public float rotationX;
        public float rotationY;
        public float rotationZ;

        public float adjustedTilt;
        public float currentLean;

        public bool isFlying;
        public bool isRunning;
        public bool isHit;
        public bool weaponFired;
        public bool stomped;
        public bool landed;
        public bool jumped;

        // 0 = none, -1 = left, 1 = right
        public float stompedFoot;

        public float speed;
        public TelemetryData()
        {
        }
    }
}
