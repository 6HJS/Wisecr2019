using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace SecuCodeApp
{
    public enum TagState : ushort
    {
        None = 0x00,

        SetOffset = 0x01,
        Done = 0x02,

        Authenticate = 0x03,

        PerformHash = 0x04,
        GetSessionKey = 0x05,
        StartInUserMode = 0x06,

        StartAttestation = 0x07,
        GetAttestationResponse = 0x08,

        EnableObserverMode = 0x0B,
        DisableObserverMode = 0x0C,

        RestartInBootMode = 0x7F,
        BootLoader = 0x8F,

        Unknown,
    }

    /// <summary>
    /// Implementation of the SecuCode protocol.
    /// </summary>
    public static class SecuCode
    {
        public enum StatusCode
        {
            Success,
            TimeOut,
            KeyDecodingFailure,
            HashFailure,
            PowerFailure,
            NotInBootLoader,
            FailureCountExceeded,
            UnknownError,
            ReaderError,
        }

        public enum PilotTagSelection
        {
            First = 0,
            Random = 1,
            FixedTag = 2,
            LowestVoltage = 3,
            HighestVoltage = 4,
            LowestReadRate = 5,
            HighestReadRate = 6,
            LowestRssi = 7,
            HighestRssi = 8,
        }

        public static PilotTagSelection PilotTagSelectionFromIndex(int index)
        {
            return index switch
            {
                0 => PilotTagSelection.First,
                1 => PilotTagSelection.Random,
                2 => PilotTagSelection.FixedTag,
                3 => PilotTagSelection.LowestVoltage,
                4 => PilotTagSelection.HighestVoltage,
                5 => PilotTagSelection.LowestReadRate,
                6 => PilotTagSelection.HighestReadRate,
                7 => PilotTagSelection.LowestRssi,
                8 => PilotTagSelection.HighestRssi,
                _ => throw new Exception($"Bad pilot tag selection: {index}"),
            };
        }

        /// Finds the position of the pilot tag
        public static int FindPilotTag(this PilotTagSelection selection, List<ushort> tags, Dictionary<ushort, TagMetadata> metadata)
        {
            return selection switch
            {
                PilotTagSelection.First => 0,
                PilotTagSelection.Random => new Random().Next(0, tags.Count),
                PilotTagSelection.FixedTag => tags.FindIndex(t => t == 0xAA00),
                PilotTagSelection.LowestVoltage => tags.MinPosition(tag => metadata[tag].Voltage),
                PilotTagSelection.HighestVoltage => tags.MinPosition(tag => -metadata[tag].Voltage),
                PilotTagSelection.LowestReadRate => tags.MinPosition(tag => metadata[tag].ReadCount),
                PilotTagSelection.HighestReadRate => tags.MinPosition(tag => -metadata[tag].ReadCount),
                PilotTagSelection.LowestRssi => tags.MinPosition(tag => metadata[tag].Rssi),
                PilotTagSelection.HighestRssi => tags.MinPosition(tag => -metadata[tag].Rssi),
                _ => 0,
            };
        }

        /// <summary>
        /// The security mode flags (CSI field in Authenticate)
        /// </summary>
        [Flags]
        public enum Mode : ushort
        {
            Default = 0,
            IpProtect = 1 << 0,
            FixedKey = 1 << 1,
            BlakeHash = 1 << 2,
            HashLength256 = 1 << 3,
            DecryptAfterEachPacket = 1 << 4,
        }

        public static async Task<OperationResponse> SendAuthenticate(
            ReaderManager reader, ushort tagId, byte[] sessionData, bool isObserverTag = false, IProgressListener progress = null)
        {
            // TODO: we shouldn't need this any more -- the values here are wrong.
            var mode = Mode.Default | Mode.IpProtect | Mode.FixedKey | Mode.BlakeHash | Mode.HashLength256;

            Profiler.StartScope("SendControl(Authenticate)");
            var response = await reader.SendControlWithPowerCheck(tagId, TagState.Authenticate, (ushort)mode, TimeSpan.FromSeconds(20.0), false);
            Profiler.EndScope();

            if (response.status != StatusCode.Success)
            {
                return response;
            }

            Profiler.StartScope("SendData");
            response = await reader.SendData(tagId, sessionData, sessionData.Length, 0x00, TimeSpan.FromSeconds(20.0), progress);
            Profiler.EndScope();

            if (!response.IsSuccess())
            {
                return response;
            }

            if (isObserverTag)
            {
                Console.WriteLine($"SendAuthenticate to observer tag: {tagId:X4}");

                Profiler.StartScope("SendControl(GetSessionKeyObserver)");
                response = await reader.SendControlWithPowerCheck(tagId, TagState.GetSessionKey, 0, TimeSpan.FromSeconds(0.5));
                Profiler.EndScope();

                // We expect this command to timeout, since after successfully sending this message the tag will no longer send responses
                if (response.status == StatusCode.TimeOut)
                {
                    return OperationResponse.Success;
                }
                return response;
            }
            else
            {
                // Regular case
                Console.WriteLine($"SendAuthenticate to pilot tag: {tagId:X4}");

                Profiler.StartScope("SendControl(GetSessionKey)");
                response = await reader.SendControlWithPowerCheck(tagId, TagState.GetSessionKey, 0, TimeSpan.FromSeconds(10.0));
                Profiler.EndScope();

                return response;
            }
        }

        /// <summary>
        /// Sends an attestation challenge to a target tag, and returns the tag's response
        /// </summary>
        public static async Task<(OperationResponse, byte[])> RunAttestation(
            ReaderManager reader, ushort tagId, byte[] challenge, ushort mode, IProgressListener progress = null
        )
        {
            var response = await reader.SendControlWithPowerCheck(tagId, TagState.StartAttestation, mode, TimeSpan.FromSeconds(20.0), false);
            if (!response.IsSuccess())
            {
                return (response, null);
            }

            progress?.Update(tagId, Stage.SendingChallenge, 0.0);
            response = await reader.SendData(tagId, challenge, challenge.Length, 0x00, TimeSpan.FromSeconds(20.0), progress);
            if (!response.IsSuccess())
            {
                return (response, null);
            }

            progress?.Update(tagId, Stage.ReadingResponse, 0.0);
            response = await reader.SendControlWithPowerCheck(tagId, TagState.GetAttestationResponse, 0x00, TimeSpan.FromSeconds(20.0), false);
            if (!response.IsSuccess())
            {
                return (response, null);
            }

            return await ReadTagBytes(reader, tagId, 0, 8);
        }


        public static async Task<OperationResponse> SendData(
            ReaderManager reader, ushort tagId, byte[] bytesToSend, byte[] sessionKey, byte blockSize = 32, IProgressListener progress = null
        )
        {
            if (sessionKey == null)
            {
                throw new Exception("Session key is null.");
            }

            Profiler.StartScope("EncryptPacket");
            var data = EncryptPacket(bytesToSend, sessionKey);
            Profiler.EndScope();

            // BlockWrite messages contain 16-bit words
            var bytesPerChunk = blockSize * 2;

            var response = await reader.SendData(tagId, data, bytesPerChunk, 0x00, TimeSpan.FromSeconds(20.0), progress);
            if (!response.IsSuccess())
            {
                return response;
            }

            Profiler.StartScope("SendDone");
            response = await reader.SendControlCheckPrev(tagId, TagState.Done, 0, 0x00, TimeSpan.FromSeconds(10.0));
            Profiler.EndScope();

            return response;
        }

        public static byte[] GenerateSignature(byte[] sessionKey, byte[] firmware, byte tagVersion, byte newVersion)
        {
            var payloadBytes = PaddedDataWithLength(firmware);
            var cmacBytes = payloadBytes
                .Concat(new byte[] { tagVersion })
                .Concat(new byte[] { newVersion })
                .Concat(new byte[Utils.CalculatePadding(payloadBytes.Length + 2, 16)])
                .ToArray();
            return Native.TinyCrypt.ComputeCmac(sessionKey, cmacBytes);
        }

        public static byte[] EncryptPacket(byte[] data, byte[] key)
        {
            return Native.TinyCrypt.AesEncrypt_DefaultIV(key, PaddedDataWithLength(data));
        }

        private static byte[] PaddedDataWithLength(byte[] data)
        {
            var length = new byte[] { (byte)(data.Length & 0xFF), (byte)(data.Length >> 8) };
            return length.Concat(data).Concat(new byte[Utils.CalculatePadding(data.Length + 2, 16)]).ToArray();
        }


        public static async Task<(OperationResponse, byte[])> ReadTagBytes(ReaderManager reader, ushort tagId, ushort offset, ushort numWords = 1)
        {
            return await reader.Read(tagId, 3, offset, numWords, TimeSpan.FromSeconds(5.0));
        }
    }

}
