using System;

using System.Runtime.InteropServices;


namespace SecuCodeApp.Native
{
    public static class PAM
    {
        [DllImport("Utils.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "compute_pam")]
        private static extern int compute_pam(UInt16 VTm, IntPtr sleepT, IntPtr activeT);

        public static (byte sleepT, byte activeT) ComputePAM(UInt16 VTm)
        {
            var sleepT = new byte[1];
            var activeT = new byte[1];

            var sleepTPtr = Marshal.AllocHGlobal(Marshal.SizeOf<byte>());
            var activeTPtr = Marshal.AllocHGlobal(Marshal.SizeOf<byte>());

            try
            {
                var result = compute_pam(VTm, sleepTPtr, activeTPtr);
                if (result != 0)
                {
                    Console.WriteLine($"`compute_pam` return an error {result}");
                }

                Marshal.Copy(sleepTPtr, sleepT, 0, 1);
                Marshal.Copy(activeTPtr, activeT, 0, 1);
            }
            finally
            {
                Marshal.FreeHGlobal(sleepTPtr);
                Marshal.FreeHGlobal(activeTPtr);
            }

            return (sleepT[0], activeT[0]);
        }
    }
}
