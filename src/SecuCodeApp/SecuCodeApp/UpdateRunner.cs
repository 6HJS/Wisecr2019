using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static SecuCodeApp.SecuCode;

namespace SecuCodeApp
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1063",
        Justification = "`tagStates` is the only owned `IDisposable` resource, and can be disposed multiple times safely")]
    public class UpdateRunner : IDisposable
    {
        // The configuration for this update runner
        private readonly UpdateConfig config;

        // The LLRP reader that will be used for sending update commands to tags
        private readonly ReaderManager reader;

        // An interface provided that allows the caller to listen for update events
        private readonly IProgressListener progress;

        // Maintains the current state for each tag, updated in a background thread/task.
        private readonly TagStateUpdater tagStates;

        public UpdateRunner(ReaderManager reader, UpdateConfig config, IProgressListener progressListener)
        {
            // Check that pilot tag selection is valid
            if (config.PilotSelectionStrategy == PilotTagSelection.FixedTag)
            {
                if (config.TargetTags.Values.First(a => a.TagId == 0xAA00) == null)
                {
                    throw new Exception("`Fixed tag` used for Pilot tag selection, but no fixed tag was found");
                }
            }

            this.reader = reader;
            this.config = config;
            this.progress = progressListener;
            this.tagStates = new TagStateUpdater(reader);
        }

        /// Perform the configured update
        public async Task<RunResults> Run()
        {
            var results = this.config.SendMode switch
            {
                SendMode.Broadcast => await this.SendToAllBroadcastWithRetry(),
                SendMode.Sequential => await this.SendToAllSequentialWithRetry(),
                SendMode.None => await this.SendToNone(),
                _ => throw new Exception("Unimplemented"),
            };

            _ = await this.AttestAll();


            this.progress.Info("Finished processing run command");

            return results;
        }

        /// Skips sending firmware to tags, but still updates their status
        async Task<RunResults> SendToNone()
        {
            var metadata = await this.ScanTags(this.config.TargetTags.Keys.ToArray(), TimeSpan.FromSeconds(1));
            foreach (var tag in metadata)
            {
                this.progress.Update(tag.Key, Stage.Skipped, 1.0);
            }
            return new RunResults
            {
                tagMetadata = metadata
            };
        }

        /// Attempts to perform a firmware update on all target tags, one-by-one, retrying each 
        /// update up to the number of configured maximum attempts.
        private async Task<RunResults> SendToAllSequentialWithRetry()
        {
            var timer = new Stopwatch();
            timer.Start();

            var totalRetries = 0;
            var pendingTags = this.config.TargetTags.Keys.ToArray();

            var tagMetadata = await this.ScanTags(pendingTags, TimeSpan.FromSeconds(1));

            var notDone = 0;
            foreach (var tag in pendingTags)
            {
                if (tagMetadata.TryGetValue(tag, out var tagData) && tagData.Version == this.config.TargetVersion)
                {
                    // Tag is already up to date 
                    this.progress.Update(tag, Stage.Updated, 1.0);
                    continue;
                }

                var attempts = 0;
                while (await this.SendSequential(tag) != true)
                {
                    totalRetries += 1;
                    attempts += 1;
                    if (attempts >= this.config.MaxAttempts)
                    {
                        notDone += 1;
                        break;
                    }
                }
            }

            var elapsed = timer.Elapsed.TotalSeconds;
            this.progress.Info($"Successfully updated all tags. (Duration = {elapsed}s, Failures = {totalRetries})\n\n");

            return new RunResults
            {
                done = pendingTags.Count() - notDone,
                missing = 0, // Missing tags will be counted as `notDone` for send sequential
                notDone = notDone,
                attempts = totalRetries + 1,
                time = elapsed,
                tagMetadata = tagMetadata,
            };
        }

        /// Attempts to perform a firmware update on all target tags, by performing one or more
        /// broadcast rounds.
        private async Task<RunResults> SendToAllBroadcastWithRetry()
        {
            var timer = new Stopwatch();
            timer.Start();

            Dictionary<ushort, TagMetadata> tagMetadata = null;

            var doneTags = new List<ushort>();
            var pendingTags = this.config.TargetTags.Keys.ToList();
            var missingTags = new List<ushort>();

            // Checks the state of all pending tags to determine which tags still need to be updated
            async Task<Dictionary<ushort, TagMetadata>> UpdatePendingTagStates()
            {
                var targetTags = pendingTags.ToArray();
                var scanResults = await this.ScanTags(targetTags, TimeSpan.FromSeconds(1));

                pendingTags = new List<ushort>();
                foreach (var tag in targetTags)
                {
                    if (!scanResults.TryGetValue(tag, out var value))
                    {
                        // This tag was not found during our scan, so it is now marked as missing
                        missingTags.Add(tag);
                        this.progress.Error("Missing", tag);
                        continue;
                    }

                    if (value.Version == this.config.TargetVersion)
                    {
                        // This tag is now done
                        doneTags.Add(tag);
                        this.progress.Update(tag, Stage.Updated, 1.0);
                    }
                    else
                    {
                        // This tag is still not done
                        pendingTags.Add(tag);
                        this.progress.Update(tag, Stage.Waiting, 0.0);
                    }
                }

                return scanResults;
            }

            var attempts = 0;
            while (attempts < this.config.MaxAttempts)
            {
                var scanResults = await UpdatePendingTagStates();
                if (tagMetadata == null)
                {
                    tagMetadata = scanResults;
                }

                // If there were no pending tags but there are still missing tags, skip the 
                // current attempt and scan again.
                while (pendingTags.Count == 0 && missingTags.Count != 0 && attempts + 1 < this.config.MaxAttempts)
                {
                    this.progress.Info($"Sleeping for {this.config.ReaderSleepTimeMs} milliseconds, to find missing tags: {TagString(missingTags)}\n");
                    if (this.config.ReaderSleepTimeMs != 0)
                    {
                        this.reader.ResetState();
                        await Task.Delay(TimeSpan.FromMilliseconds(this.config.ReaderSleepTimeMs));
                    }
                    
                    attempts += 1;

                    pendingTags = missingTags;
                    missingTags = new List<ushort>();
                    scanResults = await UpdatePendingTagStates();
                }

                // If there are still no pending tags, we are done
                if (pendingTags.Count == 0)
                {
                    break;
                }

                if (pendingTags.Count > 1 && this.config.PilotSelectionStrategy == PilotTagSelection.FixedTag)
                {
                    // If there is more than one remaining tag remaining, remove the tag from the done list, 
                    // and add it to the not done list
                    if (pendingTags.IndexOf(0xAA00) == -1)
                    {
                        if (doneTags.IndexOf(0xAA00) == -1)
                        {
                            throw new Exception("Pilot tag not found");
                        }
                        doneTags.Remove(0xAA00);
                        pendingTags.Add(0xAA00);
                    }
                }

                this.UpdatePilotTag(pendingTags, scanResults);

                var roundTimer = new Stopwatch();
                roundTimer.Start();

                await this.SendToAllRound(pendingTags);

                attempts += 1;
                this.progress.Info($"Attempt {attempts}, duration: {roundTimer.Elapsed.TotalSeconds}\n");
            }

            return new RunResults
            {
                done = doneTags.Count,
                notDone = pendingTags.Count,
                missing = missingTags.Count,
                attempts = attempts,
                time = timer.Elapsed.TotalSeconds,
                tagMetadata = tagMetadata,
            };
        }

        /// Attemps to attest the target tag using the currently configured attestation mode. 
        /// Returns a dictionary mapping tag ids to attestation results
        private async Task<Dictionary<ushort, AttestResult>> AttestAll()
        {
            var results = new Dictionary<ushort, AttestResult>(
                this.config.TargetTags.Keys.ToDictionary(tag => tag, _ => AttestResult.Incomplete)
            );

            var attempts = 0;
            while (attempts < this.config.MaxAttempts && results.ContainsValue(AttestResult.Incomplete))
            {
                var tagsToCheck = results.Where(entry => entry.Value == AttestResult.Incomplete).ToArray();
                foreach (var entry in tagsToCheck)
                {
                    this.progress.Update(entry.Key, Stage.Attesting, 0.0, "Waiting...");
                    results[entry.Key] = await this.AttestTag(entry.Key);
                }

                attempts += 1;
            }

            return results;
        }

        /// Attempts to perform a firmware update for a single tag
        private async Task<bool> SendSequential(ushort tag)
        {
            if (!await this.SetTagToBootloader(tag))
            {
                this.progress.Info("Failed to set tag into bootloader");
                return false;
            }

            if (!await this.SendSessionKey(tag, false))
            {
                this.progress.Info("Failed to send session key to tag");
                return false;
            }

            if (!await this.SendEncryptedFirmware(tag))
            {
                this.progress.Info("Failed to send payload to tag");
                return false;
            }

            this.progress.Update(tag, Stage.Updated, 1.0);
            return true;
        }

        /// Attempts to perform attestation on a single tag using the currently configured 
        /// attestation mode
        private async Task<AttestResult> AttestTag(ushort tag)
        {
            if (this.config.AttestMode == AttestMode.None)
            {
                this.progress.Update(tag, Stage.Skipped, 1.0);
                return AttestResult.Skipped;
            }

            // The tag must be in the bootloader state in order to run attestation
            if (!await this.SetTagToBootloader(tag))
            {
                this.progress.Info("Failed to set tag into bootloader");
                return AttestResult.Incomplete;
            }

            // Check that this tag is running the target firmware version
            var version = this.tagStates.GetTag(tag)?.Version;
            if (version != this.config.TargetVersion)
            {
                this.progress.Error($"Incorrect version: {version?.ToString() ?? "not found"}", tag);
                return AttestResult.WrongVersion;
            }

            var (challenge, expectedResponse) = this.GetChallengeResponse(tag);

            var (response, payload) = await RunAttestation(this.reader, tag, challenge, (ushort)this.config.AttestMode, this.progress);
            if (!response.IsSuccess())
            {
                this.progress.Error($"Error running attestation: {response.status}.", tag);
                return AttestResult.Incomplete;
            }

            this.progress.Info($"Challange response: {payload.ToByteString()}");

#if DEBUG
            var testExpectedResponse = new byte[] { 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F, 0x7F };
            if (!payload.SequenceEqual(testExpectedResponse))
#else
            if (!payload.SequenceEqual(expectedResponse))
#endif
            {
                this.progress.Error($"Bad response (got: {payload.ToByteString()}, expected: {expectedResponse.ToByteString()})", tag);
                return AttestResult.Failed;
            }

            // Attempt to return tag to user code
            _ = await this.reader.SendRestartInUserMode(tag, TimeSpan.FromSeconds(1.0));

            this.progress.Update(tag, Stage.Done, 1.0);
            return AttestResult.Passed;
        }

        /// Performs a single broadcast iteration targeting the tags specified `tagList`.
        /// 
        /// This involves the following steps
        ///   1. Restart to bootloader
        ///   2. Session key transfer
        ///   3. Data sending to pilot tag
        private async Task SendToAllRound(List<ushort> tagList)
        {
            // Keep track of which tags have failed so far
            var failedTags = new HashSet<ushort>();
            var pilotTag = tagList[0];

            // Restart each tag into into bootloader mode
            this.progress.Info($"Restarting: {TagString(tagList)}, into Bootloader\n");
            foreach (var tag in tagList)
            {
                if (!await this.SetTagToBootloader(tag))
                {
                    if (tag == pilotTag)
                    {
                        this.progress.Info("Pilot tag failure");
                        return;
                    }
                    failedTags.Add(tag);
                }
            }
            tagList.RemoveAll(t => failedTags.Contains(t));
            this.progress.Info($"Restarted tags in bootloader mode ({failedTags.Count} failed)");

            // Send session key to each tag in the bootloader
            this.progress.Info($"Sending session key to: {TagString(tagList)}\n");
            foreach (var tag in tagList)
            {
                if (!await this.SendSessionKey(tag, tag != pilotTag))
                {
                    if (tag == pilotTag)
                    {
                        this.progress.Info("Pilot tag failure");
                        return;
                    }
                    failedTags.Add(tag);
                }
            }
            tagList.RemoveAll(t => failedTags.Contains(t));
            this.progress.Info($"Sent session key to tags ({failedTags.Count} failed)");

            // Sends payload to pilot tag (observer tags should sniff results)
            await this.SendEncryptedFirmware(pilotTag);

            this.progress.Update(pilotTag, Stage.Updated, 1.0);
        }

        /// Forces a tag to enter bootloader mode, returning 'false' on failure
        private async Task<bool> SetTagToBootloader(ushort tag)
        {
            this.progress.Update(tag, Stage.GoToBootloader, 0.0);

            // Check whether this tag is already in the bootloader mode
            if (this.tagStates.IsInBootloader(tag))
            {
                this.progress.Update(tag, Stage.GoToBootloader, 1.0);
                return true;
            }

            var response = await this.reader.SendRestartInBootloader(tag, false, TimeSpan.FromSeconds(5.0));
            if (!response.IsSuccess())
            {
                this.progress.Error($"Unable to start tag in bootloader mode: {response.status}, {this.EpcString(tag)}", tag);
                return false;
            }

            this.progress.Update(tag, Stage.GoToBootloader, 1.0);
            return true;
        }

        /// Attempts to send the session key to a target tag, returning 'false' on failure
        private async Task<bool> SendSessionKey(ushort tag, bool isObserver)
        {
            this.progress.Update(tag, Stage.SendSessionKey, 0.0);
            var sessionData = this.GetSessionData(this.config.TargetTags[tag], isObserver);

            var response = await SendAuthenticate(this.reader, tag, sessionData, isObserver, this.progress);
            if (!response.IsSuccess())
            {
                this.progress.Error($"Failed to send session key: {response.status}, {this.EpcString(tag)}", tag);
                return false;
            }

            this.progress.Update(tag, isObserver ? Stage.WaitingForDataObserver : Stage.SendingData, 0.0);
            return true;
        }

        /// Attempts to send the encrypted firmware encoded as MSPBoot commands to the target tag
        private async Task<bool> SendEncryptedFirmware(ushort tag)
        {
            var bytes = MspBoot.EncodeProgram(this.config.Firmware);

            this.progress.Info($"Sending payload to {tag:X04}");
            var response = await SendData(this.reader, tag, bytes, this.config.SessionKey, (byte)this.config.BlockSize, this.progress);
            if (!response.IsSuccess())
            {
                this.progress.Error($"Failed to send data: {response.status}.", tag);
                return false;
            }
            return true;
        }

        /// Scan for tags using an inventory command, returning a dictionary mapping tagIds 
        /// to scan results.
        /// 
        /// Returns results after all target tags have been read, or the timeout has elapsed
        private async Task<Dictionary<ushort, TagMetadata>> ScanTags(ushort[] tags, TimeSpan timeout)
        {
            this.progress.Info($"Scanning for: {TagString(tags)}\n");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var targetTags = new HashSet<ushort>(tags);
            var scanResults = new Dictionary<ushort, (TagEvent latest, int count)>();

            // Start an inventory command and read tag events from the event stream
            _ = this.reader.Inventory(TimeSpan.FromSeconds(1));
            await foreach (var tagEvent in TagDataListener.EventStream(this.reader, timeout))
            {
                if (targetTags.Contains(tagEvent.TagId))
                {
                    var count = 0;
                    if (scanResults.ContainsKey(tagEvent.TagId))
                    {
                        count = scanResults[tagEvent.TagId].count;
                    }
                    scanResults[tagEvent.TagId] = (tagEvent, count + 1);
                }

                if (scanResults.Count == tags.Length)
                {
                    // Received data from every target tag at least once
                    break;
                }
            }

            var duration = stopwatch.Elapsed.TotalSeconds;
            this.progress.Info($"Found {TagString(scanResults.Keys)} in {duration} seconds\n");

            return TagMetadata.FromScanResults(scanResults, duration);
        }

        /// Moves the pilot tag to the first position in the `tags` list
        private void UpdatePilotTag(List<ushort> tags, Dictionary<ushort, TagMetadata> metadata)
        {
            tags.Swap(0, this.config.PilotSelectionStrategy.FindPilotTag(tags, metadata));
            this.progress.SetPilotTag(tags[0]);
            this.progress.Info($"{tags[0]:X04} Selected as pilot tag ({this.config.PilotSelectionStrategy})");
        }

        // Helper function for printing a colections of tags as a string
        private static string TagString(IEnumerable<ushort> tags)
        {
            return string.Join(",", tags.Select(t => $"{t:X04}"));
        }

        /// Helper function for getting the target tag's EPC value as a string
        private string EpcString(ushort tag)
        {
            return this.tagStates.GetTag(tag)?.Epc.ToByteString() ?? "No EPC";
        }

        /// Helper function for generating session data for a specific tag
        private byte[] GetSessionData(Tag tag, bool isObserver)
        {
            var payload = MspBoot.EncodeProgram(this.config.Firmware);
            var signature = GenerateSignature(this.config.SessionKey, payload, tag.Version, this.config.TargetVersion);
            var observerFlag = isObserver ? (byte)0x01 : (byte)0x00;

            return Native.TinyCrypt.AesEncrypt_DefaultIV(tag.DeviceKey, this.config.SessionKey)
                .Concat(signature)
                .Concat(new byte[] { this.config.TargetVersion, 0, 0, observerFlag })
                .ToArray();
        }

        /// Helper function for getting an attestation (challange, response) pair for a target tag
        private (byte[] challenge, byte[] response) GetChallengeResponse(ushort tag)
        {
            var (offset, data) = this.config.Firmware.Segments[0];

            var rand16 = new byte[16];
            new Random().NextBytes(rand16);

            var challenge = (new byte[] { (byte)(offset >> 8), (byte)offset, (byte)(data.Length >> 8), (byte)data.Length })
                .Concat(rand16)
                .ToArray();

            this.progress.Info($"Attestation challenge: {challenge.ToByteString()}");
            this.progress.Update(tag, 0.1, $"Challenge = {challenge.ToByteString()}");

            var response = Native.TinyCrypt.ComputeAttestationResponse(
                this.config.TargetTags[tag].DeviceKey,
                rand16,
                data,
                tag,
                this.config.TargetVersion,
                this.config.AttestMode == AttestMode.Full
            );

            return (challenge, response);
        }

        public void Dispose()
        {
            this.tagStates.Dispose();
        }
    }

    public enum SendMode
    {
        Broadcast,
        Sequential,
        None,
    }

    public enum AttestMode : ushort
    {
        None = 0xFF,
        Simple = 0x00,
        Full = 0x01,
    }

    public class UpdateConfig
    {
        /// A mapping between tag ids and tag config (e.g device key) of the tags to update
        public Dictionary<ushort, Tag> TargetTags;

        /// The shared session key for the update round
        public byte[] SessionKey = new byte[16];

        /// The firmware to send and use for attestation
        public MspFirmware Firmware;

        /// The firmware version 
        public byte TargetVersion = 1;

        /// The number of rounds (for broadcast mode) or the total number of retries (for sequential mode)
        public int MaxAttempts = 1;

        /// The amount of time to sleep in an attempt to find missing tags (broadcast mode only)
        public int ReaderSleepTimeMs = 0;

        /// The number of words to send in a single BlockWrite LLRP message
        public int BlockSize = 32;

        /// The sending strategy used to transmit the firmware to the target tags
        public SendMode SendMode = SendMode.Broadcast;

        /// The attestation mode to use during the attestation step
        public AttestMode AttestMode = AttestMode.None;

        /// Controls the strategy use for selecting the tag we broadcast to 
        public PilotTagSelection PilotSelectionStrategy = PilotTagSelection.First;
    }

    public struct RunResults
    {
        /// The number of successfully updated tags
        public int done;

        /// The number of tags that "went missing" (i.e. failed to respond after an inventory)
        /// during any attempt.
        public int missing;

        /// The number of tags that we attempted to update, but still were on the wrong version 
        /// after all update attempts have been made
        public int notDone;

        /// The total number attempts that were executed for this run
        public int attempts;

        /// The total time (in seconds) that the run took to complete (including retries)
        public double time;

        /// Keeps track of useful metadata for tags during the 
        public Dictionary<ushort, TagMetadata> tagMetadata;
    }

    public enum AttestResult
    {
        Passed,
        Skipped,
        WrongVersion,
        Incomplete,
        Failed,
    }

    public struct TagMetadata
    {
        /// The tag's current version
        public int Version;

        /// The voltage of the tag when it was last read
        public int Voltage;

        // The total number of reads during the scan period
        public int ReadCount;

        /// The number of reads / secs during the scan period
        public double ReadRate;

        /// The RSSI of the tag when it was last read
        public sbyte Rssi;

        /// Extracts tag metadata from scan results
        internal static Dictionary<ushort, TagMetadata> FromScanResults(Dictionary<ushort, (TagEvent latest, int count)> scanResults, double duration)
        {
            var output = new Dictionary<ushort, TagMetadata>();

            foreach (var entry in scanResults)
            {
                var (latest, count) = entry.Value;
                output.Add(entry.Key, new TagMetadata
                {
                    Version = latest.Version,
                    Voltage = latest.Voltage,
                    ReadCount = count,
                    ReadRate = count / duration,
                    Rssi = latest.PeakRSSI
                });
            }

            return output;
        }
    }

    class TagStateUpdater : IDisposable
    {
        /// Uesd to cancel the background task when this object is disposed
        private readonly CancellationTokenSource cancellationSource;

        /// A dictionary containing the latest tag event for each tag.
        /// Note: since this is modified in a background task we need to use a concurrent container
        public readonly ConcurrentDictionary<ushort, TagEvent> tags;

        public TagStateUpdater(ReaderManager reader)
        {
            this.tags = new ConcurrentDictionary<ushort, TagEvent>();

            // Spawn a background task that listens for TagEvents and updates 
            this.cancellationSource = new CancellationTokenSource();
            Task.Run(async () =>
            {
                await foreach (var data in TagDataListener.EventStream(reader, null, this.cancellationSource.Token))
                {
                    this.tags[data.TagId] = data;
                }
                Console.WriteLine("TagStateListener background task exited");
            }).ConfigureAwait(false);
        }

        /// Retrives the latest tag event for `tagId` (or null if there is no tag event)
        public TagEvent? GetTag(ushort tagId)
        {
            return this.tags.TryGetValue(tagId, out var tagEvent) ? (TagEvent?)tagEvent : null;
        }

        /// Checks whether the target tag is currently in the bootloader state
        public bool IsInBootloader(ushort tagId)
        {
            return this.tags.TryGetValue(tagId, out var tagEvent) && tagEvent.State == TagState.BootLoader;
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (!this.cancellationSource.IsCancellationRequested)
                {
                    this.cancellationSource.Cancel();
                }
                this.cancellationSource.Dispose();
            }
            this.disposed = true;
        }
    }
}
