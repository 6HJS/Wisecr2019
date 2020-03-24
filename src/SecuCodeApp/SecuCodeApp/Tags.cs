using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecuCodeApp
{
    public class Tag
    {
        public UInt16 TagId { get; set; }
        public byte[] DeviceKey { get; set; }
        public byte[] LatestEpc { get; set; }
        public bool Valid { get; set; } = true;
        public bool Done { get; set; } = true;

        public byte Version { get; set; } = 0;

        public int ReadCount { get; set; } = 0;
        public double ReadRate { get; set; } = 0.0;

        public int Voltage { get; set; } = 0;
        public sbyte Rssi { get; internal set; } = 0;

        public double TempOffset { get; set; } = 0.0;
        public double TempDivisor { get; set; } = 1000.0;
    }
}
