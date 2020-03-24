#define NO_OFFSET_MESSAGE

using Org.LLRP.LTK.LLRPV1;
using Org.LLRP.LTK.LLRPV1.DataType;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SecuCodeApp
{

    public class OperationResponse
    {
        public SecuCode.StatusCode status;
        public string context;

        public static OperationResponse Success = new OperationResponse { status = SecuCode.StatusCode.Success };
        public static OperationResponse UnknownError = new OperationResponse { status = SecuCode.StatusCode.UnknownError };

        public bool IsSuccess()
        {
            return this.status == SecuCode.StatusCode.Success;
        }
    }

    /// This class implements helper functions for communicating with an Impinj RFID reader.
    ///
    /// Note: This implementation isn't robust (or performant), particularly in the way it checks
    /// for state changes in the tag. This is partially due to the underlying library (LTK) and
    /// partially due to how the implementation evolved over time.
    public class ReaderManager
    {
        public LLRPClient reader;

        private int nextAccessCmdId = 1;

        private CancellationTokenSource connectionCancellationSource;

        /// <summary>
        /// Attempts to connect to an RFID reader using LLRP.
        /// </summary>
        /// <param name="address">The name / address of the reader to connect to</param>
        /// <returns>Success, or why the connection attempt failed</returns>
        public ENUM_ConnectionAttemptStatusType Connect(string address)
        {
            if (this.connectionCancellationSource != null)
            {
                this.connectionCancellationSource.Cancel();
            }

            this.reader = new LLRPClient();
            this.reader.Open(address, 2000, out var status);
            //this.reader.Enable_ImpinjExtensions();
            this.reader.Enable_EventsAndReports();

            this.nextAccessCmdId = 1;

            this.connectionCancellationSource = new CancellationTokenSource();

            return status;
        }

        /// <summary>
        /// Returns whether the ReaderManager is currently connected to a reader.
        /// Note: even if this call returns true, sending a command may fail.
        /// </summary>
        public bool IsConnected()
        {
            return this.reader != null && this.reader.IsConnected;
        }

        /// <summary>
        /// Disconnects from the reader cancelling any pending tasks
        /// </summary>
        public void Disconnect()
        {
            if (this.IsConnected())
            {
                this.ResetState();
            }

            if (this.reader != null)
            {
                this.reader.Dispose();
                this.reader = null;
            }
        }

        /// <summary>
        /// Attempts to reset the reader's state by clearing any active AccessSpecs and RoSpecs.
        /// </summary>
        public void ResetState()
        {
            this.PrepareReader();
            this.reader.Delete_AccessSpec();
            this.reader.Delete_RoSpec(0);
        }

        private void PrepareReader()
        {
            Profiler.StartScope("PrepareReader");

            if (!this.IsConnected())
            {
                Profiler.EndScope();
                throw new Exception("Not connected");
            }

            if (this.connectionCancellationSource != null)
            {
                Console.WriteLine("Cancelled pending tasks");
                Profiler.StartScope("CancelPending");
                this.connectionCancellationSource.Cancel();
                Profiler.EndScope();
            }

            this.connectionCancellationSource = new CancellationTokenSource();
            Profiler.EndScope();
        }

        /// <summary>
        /// Sends a `Read` command to a tag (with EPC starting with `tagId`) and waits for the result
        /// </summary>
        public Task<(OperationResponse response, byte[] payload)>
            Read(ushort tagId, int memoryBank, int address, int length, TimeSpan? timeout = null)
        {
            this.PrepareReader();
            this.AddReadAccessSpec(tagId, (ushort)memoryBank, (ushort)address, (ushort)length);
            return this.StartAndWaitForBytes(tagId, length, timeout);
        }

        /// <summary>
        /// Runs a simple inventory command for a specified `duration`
        /// </summary>
        public async Task Inventory(TimeSpan duration)
        {
            this.PrepareReader();

            this.reader.Delete_AccessSpec();
            this.reader.Delete_RoSpec(1);
            this.reader.Add_RoSpec(duration);
            this.reader.Enable_RoSpec(1);

            try
            {
                await Task.Delay(duration, this.connectionCancellationSource.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            this.reader.Delete_RoSpec(1);
        }

        /// <summary>
        /// Sends a control message to a tag (with EPC starting with `tagId`). The message is encoded a special
        /// 1 word BlockWrite command.
        /// 
        /// Note: this will always return a power failure error if it is the first control message that is sent.
        /// </summary>
        public Task<OperationResponse> SendControlWithPowerCheck(ushort tagId, TagState state, ushort value, TimeSpan? timeout = null, bool checkPowerFailure = true)
        {
            this.PrepareReader();

            Console.WriteLine($"Sending control message: state: {state}, value: {value}");
            this.AddControlAccessSpec(tagId, (ushort)state, value);
            ushort? prevState = null;
            return this.StartAndWaitForEpc(tagId, epc =>
            {
                var canCheckPowerFailure = checkPowerFailure && prevState != null && prevState != epc[1];

                if (epc[1] == (ushort)state) { return SecuCode.StatusCode.Success; }
                else if (canCheckPowerFailure && epc[1] == (byte)TagState.BootLoader) { return SecuCode.StatusCode.PowerFailure; }

                prevState = epc[1];
                return null;
            }, timeout);
        }

        public Task<OperationResponse> SendRestartInBootloader(ushort tagId, bool sendToAll = false, TimeSpan? timeout = null)
        {
            this.PrepareReader();

            this.AddControlAccessSpec(sendToAll ? null : (ushort?)tagId, (ushort)TagState.RestartInBootMode, 0x00);
            return this.StartAndWaitForEpc(tagId, epc =>
            {
                if (epc[1] == (ushort)TagState.BootLoader) { return SecuCode.StatusCode.Success; }
                return null;
            }, timeout);
        }

        public Task<OperationResponse> SendRestartInUserMode(ushort tagId, TimeSpan? timeout = null)
        {
            this.PrepareReader();

            this.AddControlAccessSpec(tagId, (ushort)TagState.StartInUserMode, 0x00);
            byte? prevState = null;
            return this.StartAndWaitForEpc(tagId, epc =>
            {
                if (epc[1] == prevState) { return SecuCode.StatusCode.Success; }
                prevState = epc[1];
                return null;
            }, timeout);
        }

        /// <summary>
        /// Sends a control message to a tag (with EPC starting with `tagId`). The message is encoded a special
        /// 1 word BlockWrite command.
        /// 
        /// The function returns when the tag state transitions away from its previous state
        /// </summary>
        public Task<OperationResponse> SendControlCheckPrev(ushort tagId, TagState state, ushort value, ushort prevEpc, TimeSpan? timeout = null)
        {
            this.PrepareReader();

            Console.WriteLine($"Sending control message: state: {state}, value: {value}");
            this.AddControlAccessSpec(tagId, (ushort)state, value);
            return this.StartAndWaitForEpc(tagId, epc =>
            {
                if (epc[1] != prevEpc) { return SecuCode.StatusCode.Success; }
                return null;
            }, timeout);
        }

        public Task<OperationResponse> SendControl(ushort tagId, TagState state, ushort value, TimeSpan? timeout = null)
        {
            this.PrepareReader();

            Console.WriteLine($"Sending control message: state: {state}, value: {value}");
            this.AddControlAccessSpec(tagId, (ushort)state, value);
            return this.StartAndWaitForEpc(tagId, epc =>
            {
                if (epc[1] == (ushort)state) { return SecuCode.StatusCode.Success; }
                return null;
            }, timeout);
        }

        /// <summary>
        /// Sends a control message to a tag (with EPC starting with `tagId`). The message is encoded a special
        /// 1 word BlockWrite command.
        /// 
        /// Note: waiting for an ACK is less reliable than the EPC based method above
        /// </summary>
        public Task<OperationResponse> SendControlWaitForAck(ushort tagId, TagState state, ushort value, TimeSpan? timeout = null)
        {
            this.PrepareReader();

            Console.WriteLine($"Sending control message: state: {state}, value: {value}");
            var command = this.AddControlAccessSpec(tagId, (ushort)state, value);
            return this.StartAndWaitBlockWriteAcks(new List<PARAM_C1G2BlockWrite> { command }, tagId);
        }

        public Task<OperationResponse> SendControlWaitForNoResponse(ushort tagId, TagState state, ushort value, TimeSpan? timeout = null)
        {
            this.PrepareReader();

            Console.WriteLine($"Sending control message: state: {state}, value: {value}");
            var command = this.AddControlAccessSpec(tagId, (ushort)state, value);
            return this.StartAndWaitBlockWriteNotFound(new List<PARAM_C1G2BlockWrite> { command }, tagId, timeout);
        }

        /// <summary>
        /// Sends an array of `data` to a tag (with EPC starting with `tagId`) to a specific `address`.
        /// 
        /// The transfer is done by grouping multiple BlockWrite commands into a single AccessSpec
        /// 
        /// Note: with the current configuration `ReaderManager.SendData` tends to peform better
        /// </summary>
        public async Task<OperationResponse> SendDataBatch(ushort tagId, byte[] data, int bytesPerChunk, ushort baseAddress, TimeSpan? timeout = null)
        {
            this.PrepareReader();

            var targetTag = LLRPHelpers.WispTag(tagId);

            var controlResponse = await this.SendControlWithPowerCheck(tagId, TagState.SetOffset, 0x0000);
            if (controlResponse.status != SecuCode.StatusCode.Success)
            {
                return controlResponse;
            }

            var commands = new List<PARAM_C1G2BlockWrite>();
            var offset = baseAddress;

            foreach (var chunk in data.Split(bytesPerChunk))
            {
                var bytes = chunk.ToArray();
                commands.Add(this.BlockWriteCmd(offset, bytes));
                offset += (ushort)(bytes.Length / 2);
            }

            this.reader.Add_ContinuousAccessSpec(targetTag, commands.Select(a => a as IParameter).ToList());

            return await this.StartAndWaitBlockWriteAcks(commands, tagId, timeout);
        }

        /// <summary>
        /// Sends an array of `data` to a tag (with EPC starting with `tagId`) to a specific `address`.
        /// 
        /// This transfer is done by sending BlockWrite commands individually, configuring the reader
        /// to resend on failure before sending the next BlockWrite command.
        /// </summary>
        public async Task<OperationResponse> SendData(
            ushort tagId, byte[] data, int bytesPerChunk, ushort address, TimeSpan? timeout = null, IProgressListener progress = null
        )
        {
            if (data.Length % 2 != 0)
            {
                throw new Exception($"Data length must be a multiple of word size (16-bits) was: {data.Length}");
            }

            this.PrepareReader();

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var offset = address;
            foreach (var chunk in data.Split(bytesPerChunk))
            {
                var message = $"Sending chunk ({chunk.Count()} bytes) to offset: {offset}";
                Console.WriteLine(message);
                progress?.Update(tagId, 2.0 * (offset - address) / (double)data.Length, message);

                Profiler.StartScope($"SendDataBlock");
                var bytes = chunk.ToArray();
                var sendResponse = await this.SendDataBlock(tagId, offset, bytes, timeout);
                Profiler.EndScope();

                if (sendResponse.status != SecuCode.StatusCode.Success)
                {
                    return sendResponse;
                }

                offset += (ushort)(bytes.Length / 2);
            }
            progress?.Update(tagId, 1.0, $"Wrote: {data.Length / 2} words");

            stopWatch.Stop();

            Console.WriteLine($"Wrote: {data.Length / 2} words in {stopWatch.Elapsed.TotalSeconds} seconds: {data.ToByteString()}");
            progress?.Info($"Wrote: {data.Length / 2} words in {stopWatch.Elapsed.TotalSeconds} seconds");

            return new OperationResponse { status = SecuCode.StatusCode.Success };
        }

        private Task<OperationResponse> SendDataBlock(ushort tagId, ushort offset, byte[] dataBlock, TimeSpan? timeout = null)
        {
            var targetTag = LLRPHelpers.WispTag(tagId);
            var accessSpec = this.BlockWriteCmd(offset, dataBlock);
            this.reader.Add_ContinuousAccessSpec(targetTag, new List<IParameter> { accessSpec });
            return this.StartAndWaitBlockWriteAcks(new List<PARAM_C1G2BlockWrite> { accessSpec }, tagId, timeout);
        }

        /// <summary>
        /// Generates a "true" BlockWrite command from an offset and an array of data, taking care to
        /// avoid overlapping with the control messages we generate
        /// </summary>
        private PARAM_C1G2BlockWrite BlockWriteCmd(ushort offset, byte[] dataBlock)
        {
            var words = new UInt16Array();
            for (var i = 0; i < dataBlock.Length; i += 2)
            {
                words.Add((ushort)((dataBlock[i + 1] << 8) | dataBlock[i]));
            }

            var accessSpec = LLRPHelpers.DataMessage(words, offset, (ushort)this.nextAccessCmdId);
            this.nextAccessCmdId += 1;

            return accessSpec;
        }

        /// <summary>
        /// Adds a new access spec for reading a number of bytes from the tag
        /// </summary>
        private void AddReadAccessSpec(ushort tagId, ushort memoryBank, ushort address, ushort length)
        {
            var accessSpecOp = LLRPHelpers.ReadMessage(memoryBank, address, length);
            var targetTag = LLRPHelpers.WispTag(tagId);

            this.reader.Add_ContinuousAccessSpec(targetTag, new List<IParameter> { accessSpecOp });
        }

        /// <summary>
        /// Adds a new access spec for sending a control message
        /// </summary>
        private PARAM_C1G2BlockWrite AddControlAccessSpec(ushort? tagId, ushort state, ushort value)
        {
            var accessSpecOp = LLRPHelpers.ControlMessage(state, value);
            var targetTag = LLRPHelpers.WispTag(tagId);
            this.reader.Add_ContinuousAccessSpec(targetTag, new List<IParameter> { accessSpecOp });
            return accessSpecOp;
        }

        /// <summary>
        /// Starts the currently configured access spec, and waits for the task to end, or a 
        /// certain EPC value to be seen
        /// </summary>
        private async Task<OperationResponse> StartAndWaitForEpc(ushort tagId, Func<byte[], SecuCode.StatusCode?> check, TimeSpan? timeout = null)
        {
            this.reader.Enable_AccessSpec();

            this.reader.Add_RoSpec(timeout);
            this.reader.Enable_RoSpec(1);

            var response = await new TagReportTask(this.reader, tagId, OnEpc(tagId, check), timeout, this.connectionCancellationSource.Token).Task;

            this.reader.Delete_AccessSpec();
            this.reader.Delete_RoSpec(1);

            return response;
        }

        /// <summary>
        /// Start a new task with a list of C1G2BlockWrite commands, waiting for each command to be
        /// acked successfully before returning
        /// </summary>
        private async Task<OperationResponse> StartAndWaitBlockWriteAcks(List<PARAM_C1G2BlockWrite> commands, ushort tagId, TimeSpan? timeout = null)
        {
            this.reader.Enable_AccessSpec();

            this.reader.Add_RoSpec(timeout);
            this.reader.Enable_RoSpec(1);

            var response = await new TagReportTask(this.reader, tagId, OnBlockWriteAck(commands, tagId), timeout, this.connectionCancellationSource.Token).Task;

            this.reader.Delete_AccessSpec();
            this.reader.Delete_RoSpec(1);

            return response;
        }

        private async Task<OperationResponse> StartAndWaitBlockWriteNotFound(List<PARAM_C1G2BlockWrite> commands, ushort tagId, TimeSpan? timeout = null)
        {
            this.reader.Enable_AccessSpec();

            this.reader.Add_RoSpec(timeout);
            this.reader.Enable_RoSpec(1);

            var task = new TagReportTask(this.reader, tagId, (msg) => null, timeout, this.connectionCancellationSource.Token, true);
            var notFoundTask = Task.Run(async () =>
            {
                var totalTime = 0;
                while (!this.connectionCancellationSource.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    totalTime += 1;
                    if (!task.HasSeenTargetTag())
                    {
                        Console.WriteLine($"Target tag not found within 1 second, total delay: {totalTime} seconds");
                        break;
                    }
                }
                return OperationResponse.Success;
            });
            var response = await Task.WhenAny(task.Task, notFoundTask);

            this.PrepareReader();
            this.reader.Delete_AccessSpec();
            this.reader.Delete_RoSpec(1);

            return await response;
        }

        /// <summary>
        /// Start a new task with a C1G2Read command, waiting for a certain number of bytes to be 
        /// read before returning
        /// </summary>
        private async Task<(OperationResponse, byte[])> StartAndWaitForBytes(ushort tagId, int numBytes, TimeSpan? timeout = null)
        {
            this.reader.Enable_AccessSpec();

            this.reader.Add_RoSpec();
            this.reader.Enable_RoSpec(1);

            byte[] bytes = null;

            var task = new TagReportTask(this.reader, tagId, (message) =>
            {
                if (message.TagReportData == null)
                {
                    return null;
                }

                foreach (var data in message.TagReportData)
                {
                    for (var i = 0; i < data.AccessCommandOpSpecResult.Count; ++i)
                    {
                        if (data.AccessCommandOpSpecResult[i] is PARAM_C1G2ReadOpSpecResult result && result.ReadData.Count >= numBytes)
                        {
                            bytes = DataHelpers.Msp430WordsToBytes(result.ReadData);
                            return SecuCode.StatusCode.Success;
                        }
                    }

                    // Check if the EPC has changed to a restart value (indicates power failure)
                    if (data.EPCParameter.Count > 0)
                    {
                        if (DataHelpers.GetEpc(data.EPCParameter[0])[1] == (byte)TagState.RestartInBootMode)
                        {
                            return SecuCode.StatusCode.PowerFailure;
                        }
                    }
                }

                return null;
            }, timeout, this.connectionCancellationSource.Token);

            var response = await task.Task;

            this.reader.Delete_AccessSpec();
            this.reader.Delete_RoSpec(1);
            return (response, bytes);
        }

        /// <summary>
        /// Helper method for generating a callback that processes BlockWrite commands and determining whether they succeed or not.
        /// </summary>
        private static Func<MSG_RO_ACCESS_REPORT, SecuCode.StatusCode?> OnBlockWriteAck(List<PARAM_C1G2BlockWrite> commands, ushort tagId, int maxFailures = -1)
        {
            var failures = 0;
            var lockObject = new object();
            byte? prevState = null;

            return (MSG_RO_ACCESS_REPORT message) =>
            {
                if (message.TagReportData == null)
                {
                    return null;
                }

                foreach (var epc in message.TagReportData
                    .Where(data => data.EPCParameter.Count > 0)
                    .Select(data => DataHelpers.GetEpc(data.EPCParameter[0]))
                    .Where(data => data.Length > 10))
                {
                    if (epc[10] != (tagId >> 8) || epc[11] != (tagId & 0xFF))
                    {
                        // Not the target tag so skip check
                        continue;
                    }

                    var checkForPowerFailure = prevState != null && prevState != epc[1];
                    prevState = epc[1];
                    if (checkForPowerFailure && epc[1] == (byte)TagState.BootLoader)
                    {
                        Console.WriteLine("------------- Tag state changed ------------");
                        Console.WriteLine(epc.ToByteString());
                        Console.WriteLine(SecuCode.StatusCode.PowerFailure);
                        return SecuCode.StatusCode.PowerFailure;
                    }
                }

                var results = message.TagReportData
                    .Where(data => data.EPCParameter.Count > 0)
                    .Where(data =>
                    {
                        var epc = DataHelpers.GetEpc(data.EPCParameter[0]);
                        return epc[10] == (tagId >> 8) && epc[11] == (tagId & 0xFF);
                    })
                    .SelectMany(data => data.AccessCommandOpSpecResult.ToList())
                    .Select(data => data as PARAM_C1G2BlockWriteOpSpecResult)
                    .Where(result => result != null);

                // We need to lock here since we are manipulating the commands object and this 
                // callback could be called multiple times while it is still processing the data.
                //
                // Even with the lock, I'm still unsure whether this is fully safe since the
                // LTK library may also use the command buffer at the same time.
                lock (lockObject)
                {
                    foreach (var result in results)
                    {
                        var command = commands.Find(cmd => cmd.OpSpecID == result.OpSpecID);
                        if (command == null)
                        {
                            continue;
                        }

                        if (result.Result == ENUM_C1G2BlockWriteResultType.Success && result.NumWordsWritten == command.WriteData.Count)
                        {
                            Console.WriteLine($"Successfully sent: {command.OpSpecID}");
                            commands.Remove(command);
                        }

                        failures += 1;
                        if (maxFailures > 0 && failures > maxFailures)
                        {
                            return SecuCode.StatusCode.FailureCountExceeded;
                        }
                    }

                    if (commands.Count == 0)
                    {
                        return SecuCode.StatusCode.Success;
                    }
                }

                return null;
            };
        }

        static Func<MSG_RO_ACCESS_REPORT, SecuCode.StatusCode?> OnEpc(ushort tagId, Func<byte[], SecuCode.StatusCode?> epcCheck)
        {
            return (MSG_RO_ACCESS_REPORT message) =>
            {
                if (message.TagReportData == null)
                {
                    return null;
                }

                var tags = message.TagReportData
                    .Where(data => data.EPCParameter.Count > 0)
                    .Select(data => DataHelpers.GetEpc(data.EPCParameter[0]))
                    .Where(data => data.Length > 10);

                foreach (var tag in tags)
                {
                    if (tag[10] != (tagId >> 8) || tag[11] != (tagId & 0xFF))
                    {
                        continue;
                    }

                    var check = epcCheck(tag);
                    if (check != null)
                    {
                        Console.WriteLine("------------- Tag state changed ------------");
                        Console.WriteLine(tag.ToByteString());
                        Console.WriteLine(check);
                        return check;
                    }
                }
                return null;
            };
        }

    }

    /// <summary>
    /// Wrapper class for making it easier to write callbacks that process access reports.
    /// </summary>
    class TagReportTask
    {
        private readonly Func<MSG_RO_ACCESS_REPORT, SecuCode.StatusCode?> onAccessReport;
        private readonly TaskCompletionSource<OperationResponse> taskCompletionSource = new TaskCompletionSource<OperationResponse>();
        private readonly LLRPClient client;
        private readonly ushort tagId;
        private readonly bool checkPowerFailure;

        public Task<OperationResponse> Task { get { return this.taskCompletionSource.Task; } }

        private CancellationTokenSource timerCancellationSource;
        private CancellationTokenSource linkedCancellationTokenSource;
        private CancellationToken cancellationToken;

        private readonly object stateLock = new object();
        private bool done = false;
        private bool tagSeen = true;
        private bool shouldClearTagSeen = true;
        private byte? prevState = null;

        public TagReportTask(
            LLRPClient client,
            ushort tagId,
            Func<MSG_RO_ACCESS_REPORT, SecuCode.StatusCode?> onAccessReport,
            TimeSpan? timeout,
            CancellationToken cancellationToken,
            bool checkPowerFailure = false
        )
        {
            this.onAccessReport = onAccessReport;
            this.client = client;
            this.cancellationToken = cancellationToken;
            this.tagId = tagId;
            this.checkPowerFailure = checkPowerFailure;

            this.client.OnRoAccessReportReceived += this.OnRoAccessReportReceived;
            this.client.OnReaderEventNotification += this.OnReaderEventNotification;

            if (timeout != null)
            {
                this.timerCancellationSource = new CancellationTokenSource();
                System.Threading.Tasks.Task.Run(async () => await this.TimeoutTask(timeout.Value), this.timerCancellationSource.Token);
                this.linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(this.timerCancellationSource.Token, this.cancellationToken);
                this.cancellationToken = this.linkedCancellationTokenSource.Token;
            }
        }

        private async Task TimeoutTask(TimeSpan timeout)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(timeout, this.cancellationToken);
                lock (this.stateLock)
                {
                    this.timerCancellationSource = null;
                    if (!this.done)
                    {
                        Console.Write("Timeout");
                        this.taskCompletionSource.SetResult(new OperationResponse { status = SecuCode.StatusCode.TimeOut });
                        this.Done();
                    }
                }
            }
            catch (Exception)
            {
                lock (this.stateLock)
                {
                    this.timerCancellationSource = null;
                    if (!this.done)
                    {
                        this.Done();
                    }
                }
            }
        }

        private void Done()
        {
            this.client.OnRoAccessReportReceived -= this.OnRoAccessReportReceived;
            this.client.OnReaderEventNotification -= this.OnReaderEventNotification;

            this.done = true;
            if (this.timerCancellationSource != null)
            {
                this.timerCancellationSource.Cancel();
            }
        }

        private void OnReaderEventNotification(MSG_READER_EVENT_NOTIFICATION msg)
        {
            lock (this.stateLock)
            {
                if (this.done)
                {
                    return;
                }

                if (msg.ReaderEventNotificationData.ROSpecEvent.EventType == ENUM_ROSpecEventType.End_Of_ROSpec)
                {
                    this.taskCompletionSource.SetResult(new OperationResponse { status = SecuCode.StatusCode.TimeOut });
                    this.Done();
                }
                else if (msg.ReaderEventNotificationData.ConnectionCloseEvent != null)
                {
                    this.taskCompletionSource.SetResult(new OperationResponse { status = SecuCode.StatusCode.ReaderError });
                    this.Done();
                }
            }
        }

        private void OnRoAccessReportReceived(MSG_RO_ACCESS_REPORT message)
        {
            lock (this.stateLock)
            {
                if (this.done)
                {
                    return;
                }

                if (message.TagReportData == null)
                {
                    return;
                }

                if (this.shouldClearTagSeen)
                {
                    this.tagSeen = false;
                    this.shouldClearTagSeen = false;
                }

                var tags = message.TagReportData
                    .Where(data => data.EPCParameter.Count > 0)
                    .Select(data => DataHelpers.GetEpc(data.EPCParameter[0]))
                    .Where(data => data.Length > 10);

                foreach (var tag in tags)
                {
                    if (tag[10] == (this.tagId >> 8) && tag[11] == (this.tagId & 0xFF))
                    {
                        this.tagSeen = true;
                        break;
                    }

                    var checkForPowerFailure = this.checkPowerFailure && this.prevState != null && this.prevState != tag[1];
                    this.prevState = tag[1];
                    if (checkForPowerFailure && tag[1] == (byte)TagState.BootLoader)
                    {
                        Console.WriteLine("------------- Tag state changed ------------");
                        Console.WriteLine(tag.ToByteString());
                        Console.WriteLine(SecuCode.StatusCode.PowerFailure);
                        this.taskCompletionSource.SetResult(new OperationResponse { status = SecuCode.StatusCode.PowerFailure });
                        this.Done();
                        return;
                    }
                }

                var reportStatus = this.onAccessReport(message);
                if (reportStatus != null)
                {
                    this.Done();
                    this.taskCompletionSource.SetResult(new OperationResponse { status = reportStatus.Value });
                }
            }
        }

        // Returns true if we have seen the taget tag since the last access report
        public bool HasSeenTargetTag()
        {
            lock (this.stateLock)
            {
                var wasSeen = this.tagSeen;
                this.shouldClearTagSeen = true;
                return wasSeen;
            }
        }
    }
}
