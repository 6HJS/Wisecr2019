using System;

using System.Runtime.InteropServices;

namespace SecuCodeApp.Native
{
    public static class TinyCrypt
    {
        [DllImport("TINYCRYPT.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "compute_cmac")]
        private static extern int compute_cmac(IntPtr key, IntPtr data, UInt32 dlen, IntPtr tag);

        public static byte[] ComputeCmac(byte[] key, byte[] data)
        {
            if (key == null || key.Length != 16)
            {
                throw new Exception("Invalid key");
            }

            if (data == null || data.Length % 16 != 0)
            {
                throw new Exception("Data was null or not a multiple of 16 bytes");
            }

            var cmacOut = new byte[16];

            var keyPtr = Marshal.AllocHGlobal(Marshal.SizeOf(key[0]) * key.Length);
            var dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data[0]) * data.Length);
            var cmacOutPtr = Marshal.AllocHGlobal(Marshal.SizeOf(cmacOut[0]) * cmacOut.Length);

            try
            {
                Marshal.Copy(key, 0, keyPtr, key.Length);
                Marshal.Copy(data, 0, dataPtr, data.Length);
                Marshal.Copy(cmacOut, 0, cmacOutPtr, cmacOut.Length);

                var result = compute_cmac(keyPtr, dataPtr, (uint)data.Length, cmacOutPtr);
                if (result != 1)
                {
                    throw new Exception($"`compute_cmac` return an error {result}");
                }

                Marshal.Copy(cmacOutPtr, cmacOut, 0, cmacOut.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(keyPtr);
                Marshal.FreeHGlobal(dataPtr);
                Marshal.FreeHGlobal(cmacOutPtr);
            }

            return cmacOut;
        }

        [DllImport("TINYCRYPT.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "aes_encrypt")]
        private static extern int aes_encrypt(IntPtr iv, IntPtr key, IntPtr data, UInt32 dlen, IntPtr outPtr);

        public static readonly byte[] AES_IV = new byte[16] {
            0xAA, 0x00, 0xAA, 0x00,
            0xAA, 0x00, 0xAA, 0x00,
            0xAA, 0x00, 0xAA, 0x00,
            0xAA, 0x00, 0xAA, 0x00
        };

        public static byte[] AesEncrypt_DefaultIV(byte[] key, byte[] data)
        {
            return AesEncrypt(AES_IV, key, data);
        }

        public static byte[] AesEncrypt(byte[] iv, byte[] key, byte[] data)
        {
            if (iv == null || iv.Length != 16)
            {
                throw new Exception("Invalid IV");
            }

            if (key == null || key.Length != 16)
            {
                throw new Exception("Invalid key");
            }

            if (data == null)
            {
                throw new Exception("Data was null");
            }

            var dataOut = new byte[data.Length];

            var keyPtr = Marshal.AllocHGlobal(Marshal.SizeOf(key[0]) * key.Length);
            var ivPtr = Marshal.AllocHGlobal(Marshal.SizeOf(iv[0]) * iv.Length);
            var dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data[0]) * data.Length);
            var outPtr = Marshal.AllocHGlobal(Marshal.SizeOf(dataOut[0]) * dataOut.Length);

            try
            {
                Marshal.Copy(key, 0, keyPtr, key.Length);
                Marshal.Copy(iv, 0, ivPtr, iv.Length);
                Marshal.Copy(data, 0, dataPtr, data.Length);

                var result = aes_encrypt(ivPtr, keyPtr, dataPtr, (uint)data.Length, outPtr);
                if (result != 1)
                {
                    throw new Exception($"`aes_encrypt` return an error {result}");
                }

                Marshal.Copy(outPtr, dataOut, 0, dataOut.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(ivPtr);
                Marshal.FreeHGlobal(keyPtr);
                Marshal.FreeHGlobal(dataPtr);
                Marshal.FreeHGlobal(outPtr);
            }

            return dataOut;
        }

        [DllImport("TINYCRYPT.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "attestation_response_fast")]
        private static extern int attestation_response_fast(IntPtr key, IntPtr challenge, IntPtr code, UInt16 code_len, UInt16 tag_id, UInt16 version, IntPtr outPtr);


        [DllImport("TINYCRYPT.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "attestation_response_elaborate")]
        private static extern int attestation_response_elaborate(IntPtr key, IntPtr challenge, IntPtr code, UInt16 code_len, UInt16 tag_id, UInt16 version, IntPtr outPtr);


        public static byte[] ComputeAttestationResponse(byte[] key, byte[] challenge, byte[] code, ushort tagId, byte version, bool elaborate)
        {
            if (key == null || key.Length != 16)
            {
                throw new Exception("Invalid key");
            }

            if (challenge == null || challenge.Length != 16)
            {
                throw new Exception("Invalid challenge");
            }

            if (code == null)
            {
                throw new Exception("Code was null");
            }

            var outBytes = new byte[16];

            var keyPtr = Marshal.AllocHGlobal(Marshal.SizeOf(key[0]) * key.Length);
            var challengePtr = Marshal.AllocHGlobal(Marshal.SizeOf(challenge[0]) * challenge.Length);
            var codePtr = Marshal.AllocHGlobal(Marshal.SizeOf(code[0]) * code.Length);
            var outPtr = Marshal.AllocHGlobal(Marshal.SizeOf(outBytes[0]) * outBytes.Length);

            try
            {
                Marshal.Copy(key, 0, keyPtr, key.Length);
                Marshal.Copy(challenge, 0, challengePtr, challenge.Length);
                Marshal.Copy(code, 0, codePtr, code.Length);

                if (elaborate)
                {
                    attestation_response_elaborate(keyPtr, challengePtr, codePtr, (ushort)code.Length, tagId, version, outPtr);
                }
                else
                {
                    attestation_response_fast(keyPtr, challengePtr, codePtr, (ushort)code.Length, tagId, version, outPtr);
                }


                Marshal.Copy(outPtr, outBytes, 0, outBytes.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(keyPtr);
                Marshal.FreeHGlobal(challengePtr);
                Marshal.FreeHGlobal(codePtr);
                Marshal.FreeHGlobal(outPtr);
            }

            return outBytes;
        }
    }
}
