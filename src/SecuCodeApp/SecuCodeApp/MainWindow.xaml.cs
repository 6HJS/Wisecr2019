using Microsoft.Win32;
using Nett;
using Org.LLRP.LTK.LLRPV1;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SecuCodeApp
{
    public partial class MainWindow : Window
    {
        private readonly ReaderManager reader = new ReaderManager();
        private bool isConnected = false;
        private MspFirmware firmware;
        private byte[] bytesToSend = null;

        public Dictionary<string, BasicMessageBox> sharedMessageBoxes = new Dictionary<string, BasicMessageBox>();

        private readonly Dictionary<string, int> epcLookupTable = new Dictionary<string, int>();
        private readonly Dictionary<ushort, int> tagEntryLookupTable = new Dictionary<ushort, int>();
        private readonly Dictionary<string, GraphWindow> graphs = new Dictionary<string, GraphWindow>();
        private readonly Dictionary<string, Tag> activeTags = new Dictionary<string, Tag>();

        private ProgressWindow progressWindow;

        public ObservableCollection<EpcEntry> MacAddressCounts { get; set; } = new ObservableCollection<EpcEntry>();
        public ObservableCollection<TagEntry> TagList { get; set; } = new ObservableCollection<TagEntry>();

        private Dictionary<string, Tag> tagConfig = new Dictionary<string, Tag>();
        private Tag selectedTag;

        // Set an initial session key value to make things easier to test with
        private byte[] sessionKey = new byte[16] {
            0x83,0xEE,0x36,0x63,
            0xC1,0xD5,0x1F,0xE2,
            0x33,0x57,0xAA,0x50,
            0x83,0x2C,0x09,0x00
        };

        public string ReaderIpText { get; set; } = Properties.Settings.Default.ConnectionString;

        private int maxAttempts = Properties.Settings.Default.MaxAttempts;

        public int MaxAttempts
        {
            get { return this.maxAttempts; }
            set
            {
                this.maxAttempts = value;
                Properties.Settings.Default.MaxAttempts = value;
                Properties.Settings.Default.Save();
            }
        }

        public MainWindow()
        {
            this.DataContext = this;
            this.InitializeComponent();

            this.LoadTagList();
            var i = 0;
            foreach (var entry in this.tagConfig)
            {
                this.TagList.Add(new TagEntry(entry.Value));
                this.tagEntryLookupTable.Add(entry.Value.TagId, i);
                i += 1;

                this.activeTags.Add($"{entry.Value.TagId:X4}", entry.Value);
            }

            this.UpdateKeyTextBox(this.sessionKey.ToByteString());
            this.maxAttemptsTextBox.Text = this.MaxAttempts.ToString();
        }

        private void LoadTagList()
        {
            try
            {
                var config = File.ReadAllText(Properties.Settings.Default.TagList);
                this.tagConfig = Toml.ReadString<Dictionary<string, Tag>>(config);
            }
            catch (Exception ex)
            {
                BasicMessageBox.Show($"Failed to load tag list {ex}", "Failed to load tag list");
            }

        }

        private async Task RunBenchmark(int repeatCount, string path, Func<Task<RunResults>> func, string benchmarkName)
        {
            var validTags = this.activeTags.Values.Where(t => t.Valid).ToArray();

            Console.WriteLine(Path.GetFullPath(path));
            if (!File.Exists(path))
            {
                using var f = File.CreateText(path);
                f.Write("firmware_size,done,not_done,missing,attempts,duration_sec,");
                f.Write(string.Join(",", validTags.Select(t => $"{t.TagId:X4}.vcc")));
                f.Write(",");
                f.Write(string.Join(",", validTags.Select(t => $"{t.TagId:X4}.ReadRate")));
                f.Write(",");
                f.Write(string.Join(",", validTags.Select(t => $"{t.TagId:X4}.peak_rssi")));
                f.Write("\n");

            }
            var firmwareSize = this.bytesToSend.Length;

            var version = this.version.Dispatcher.Invoke(() => byte.Parse(this.version.Text));

            using var logger = new StreamWriter(path, true, System.Text.Encoding.UTF8);

            Profiler.ClearProfiler();
            Profiler.StartScope("RunBenchmark");
            for (var i = 0; i < repeatCount; ++i)
            {
                this.reader.ResetState();
                await Task.Delay(1000);

                Console.WriteLine("Start of benchmark");
                this.version.Dispatcher.Invoke(() =>
                {
                    this.version.Text = $"{version + 1}";
                });
                Profiler.StartScope($"{benchmarkName}[{version}]");
                var results = await func();
                Profiler.EndScope();
                version += 1;

                Func<Tag, double> GetValueOrZero(Func<TagMetadata, double> getter)
                {
                    return (Tag tag) =>
                    {
                        if (results.tagMetadata.TryGetValue(tag.TagId, out var value))
                        {
                            return getter(value);
                        }
                        return 0;
                    };
                }

                logger.Write($"{firmwareSize},{results.done},{results.notDone},{results.missing},{results.attempts},{results.time},");
                logger.Write(string.Join(",", validTags.Select(GetValueOrZero(t => t.Voltage))));
                logger.Write(",");
                logger.Write(string.Join(",", validTags.Select(GetValueOrZero(t => t.ReadRate))));
                logger.Write(",");
                logger.Write(string.Join(",", validTags.Select(GetValueOrZero(t => t.Rssi))));
                logger.Write("\n");

            }
            Profiler.EndScope();
            Profiler.SaveMeasurements();
        }

        private SendMode GetSendMode()
        {
            if (this.broadcastMode.IsChecked == true)
            {
                return SendMode.Broadcast;
            }
            else if (this.sequentialMode.IsChecked == true)
            {
                return SendMode.Sequential;
            }
            else if (this.noneMode.IsChecked == true)
            {
                return SendMode.None;
            }

            throw new Exception("No send mode selected");
        }

        private AttestMode GetAttestMode()
        {
            if (this.attestNone.IsChecked == true)
            {
                return AttestMode.None;
            }
            else if (this.attestSimple.IsChecked == true)
            {
                return AttestMode.Simple;
            }
            else if (this.attestFull.IsChecked == true)
            {
                return AttestMode.Full;
            }

            throw new Exception("No attestation mode selected");
        }


        private byte[] GetEncryptedSessionKey()
        {
            if (this.sessionKey == null || this.selectedTag == null)
            {
                return null;
            }
            return Native.TinyCrypt.AesEncrypt_DefaultIV(this.selectedTag.DeviceKey, this.sessionKey);
        }

        private byte[] GetSessionData(Tag tag, bool isObserver = false)
        {
            if (this.sessionKey == null || tag == null || this.bytesToSend == null)
            {
                throw new Exception("Tag, firmware, and/or session key was null");
            }

            var newVersion = this.version.Dispatcher.Invoke(() => byte.Parse(this.version.Text));
            var signature = SecuCode.GenerateSignature(this.sessionKey, this.bytesToSend, tag.Version, newVersion);

            byte sleepTime = 0, activeTime = 0;
            this.Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(this.sleepTime.Text) | string.IsNullOrWhiteSpace(this.activeTime.Text))
                {
                    (sleepTime, activeTime) = Native.PAM.ComputePAM((ushort)tag.Voltage);
                }
                else
                {
                    sleepTime = byte.Parse(this.sleepTime.Text);
                    activeTime = byte.Parse(this.activeTime.Text);
                }
            });
            var observerFlag = isObserver ? (byte)0x01 : (byte)0x00;

            return Native.TinyCrypt.AesEncrypt_DefaultIV(tag.DeviceKey, this.sessionKey)
                .Concat(signature)
                .Concat(new byte[] { newVersion, sleepTime, activeTime, observerFlag })
                .ToArray();
        }



        public void Connect()
        {
            var status = this.reader.Connect(this.ReaderIpText.Trim());
            if (status != ENUM_ConnectionAttemptStatusType.Success)
            {
                if ((int)status == -1)
                {
                    BasicMessageBox.Show(
                        $"An unknown error occured.\n\nThis is probably a TCP error, check that the reader IP address is correct: '{this.ReaderIpText.Trim()}'",
                        "Error connecting to reader"
                    );
                }
                else
                {
                    BasicMessageBox.Show(status.ToString(), "Error connecting to reader");
                }
                this.UpdateConnectionStatus(false);
                return;
            }

            Properties.Settings.Default.ConnectionString = this.ReaderIpText.Trim();
            Properties.Settings.Default.Save();

            this.reader.reader.OnRoAccessReportReceived += this.Reader_OnRoAccessReportReceived;
            this.reader.ResetState();
        }

        public void Disconnect()
        {
            this.reader.Disconnect();
        }

        #region Debug

        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            var window = this.GetProgressWindow();
            window.SetTargetTags(this.activeTags.Values.ToArray());
        }

        #endregion

        #region ReaderCallbacks
        private void Reader_OnRoAccessReportReceived(MSG_RO_ACCESS_REPORT message)
        {
            if (message.TagReportData == null)
            {
                return;
            }

            foreach (var tagReportData in message.TagReportData)
            {
                if (tagReportData.EPCParameter.Count > 0)
                {
                    this.AddTagData(tagReportData);
                }
            }
        }

        private void AddTagData(PARAM_TagReportData tagReportData)
        {
            var epc = Utils.GetEpcString(tagReportData.EPCParameter[0]);
            try
            {
                this.listBox.Dispatcher.Invoke(() =>
                {
                    var maskedEpc = Utils.MaskEpc(epc);

                    var tagId = epc.Substring(2 * 10);
                    var epcBytes = Utils.GetEpcBytes(tagReportData.EPCParameter[0]);

                    if (this.activeTags.ContainsKey(tagId))
                    {
                        var voltage = (epcBytes[6] << 8) | epcBytes[7];

                        this.activeTags[tagId].LatestEpc = epcBytes;
                        this.activeTags[tagId].ReadCount += 1;
                        this.activeTags[tagId].Voltage = voltage;
                        this.activeTags[tagId].Version = epcBytes[9];
                        this.activeTags[tagId].Rssi = tagReportData.PeakRSSI.PeakRSSI;

                        var tagIdInt = this.activeTags[tagId].TagId;
                    }

                    if (this.graphs.ContainsKey(tagId))
                    {
                        this.graphs[tagId].graphManager.UpdateSensorValues(epcBytes);
                    }

                    var tagIdShort = Convert.ToUInt16(tagId, 16);
                    if (this.tagEntryLookupTable.ContainsKey(tagIdShort))
                    {
                        var voltage = (epcBytes[6] << 8) | epcBytes[7];
                        this.TagList[this.tagEntryLookupTable[tagIdShort]].Voltage = $"{voltage}";
                    }
                    else
                    {
                        Console.WriteLine($"{tagIdShort:X} not found");
                    }

                    if (!this.epcLookupTable.ContainsKey(maskedEpc))
                    {
                        this.epcLookupTable.Add(maskedEpc, this.MacAddressCounts.Count);
                        this.MacAddressCounts.Add(new EpcEntry { Epc = epc, Count = 0 });
                    }

                    this.MacAddressCounts[this.epcLookupTable[maskedEpc]].Count += 1;
                    this.MacAddressCounts[this.epcLookupTable[maskedEpc]].Epc = epc;
                });

                this.mostRecentEpcTextBox.Dispatcher.Invoke(() =>
                {
                    this.mostRecentEpcTextBox.Text = epc;
                });
            }
            catch { }
        }

        #endregion

        #region UI_Update

        private ProgressWindow GetProgressWindow()
        {
            if (this.progressWindow == null || !this.progressWindow.IsLoaded)
            {
                this.progressWindow = new ProgressWindow(Array.Empty<Tag>());
            }
            this.progressWindow.Show();
            return this.progressWindow;
        }

        private void UpdateKeyTextBox(string value)
        {
            this.sessionKeyTextBox.Dispatcher.Invoke(() =>
            {
                this.sessionKeyTextBox.Text = value;
            });
        }

        private void UpdateEnryptedKey()
        {
            this.encryptedKeyTextBox.Dispatcher.Invoke(() =>
            {
                this.encryptedKeyTextBox.Document.Blocks.Clear();

                var paragraph = new Paragraph
                {
                    Padding = new Thickness(0.0),
                    Margin = new Thickness(0.0)
                };

                if (this.selectedTag == null || this.bytesToSend == null)
                {
                    paragraph.Inlines.Add(new Span(new Run(this.GetEncryptedSessionKey()?.ToByteString() ?? "")));
                    this.encryptedKeyTextBox.Document.Blocks.Add(paragraph);
                    return;
                }

                var sessionData = this.GetSessionData(this.selectedTag).ToByteString();

                // Session key
                paragraph.Inlines.Add(new Span(new Run(sessionData.Substring(0, 32)))
                {
                    Background = new SolidColorBrush(Colors.LightPink)
                });

                // Signature
                paragraph.Inlines.Add(new Span(new Run(sessionData.Substring(32, 32)))
                {
                    Background = new SolidColorBrush(Colors.LightGreen)
                });

                // New version
                paragraph.Inlines.Add(new Span(new Run(sessionData.Substring(64, 2)))
                {
                    Background = new SolidColorBrush(Colors.LightSalmon)
                });

                this.encryptedKeyTextBox.Document.Blocks.Add(paragraph);
            });
        }

        private void UpdateEncryptedDataTextBox()
        {
            this.payloadBytes.Dispatcher.Invoke(() =>
            {
                if (this.bytesToSend != null && this.sessionKey != null)
                {
                    var encryptedPayload = SecuCode.EncryptPacket(this.bytesToSend, this.sessionKey);
                    var decryptedPayload = Native.Aes.DecryptBytes(encryptedPayload, this.sessionKey, CipherMode.CBC);

                    Console.WriteLine($"Encrypted: {encryptedPayload.ToByteString("0x", ",")} Length = {encryptedPayload.Length}");
                    Console.WriteLine($"Decrypted: {decryptedPayload.ToByteString("0x", ",")} Length = {decryptedPayload.Length}");

                    this.payloadBytes.Text = decryptedPayload.ToByteString();
                    this.encryptedPayloadBytes.Text = encryptedPayload.ToByteString();
                }
            });
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            this.isConnected = isConnected;
            this.toggleConnectionButton.Content = isConnected ? "Disconnect" : "Connect";
            this.inventoryButton.IsEnabled = isConnected;
            this.updateVaildList.IsEnabled = isConnected;
        }
        #endregion


        #region EventHandlers

        private void ToggleConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.isConnected)
            {
                this.UpdateConnectionStatus(false);
                this.Disconnect();
                this.sendDataAllButton.IsEnabled = false;
                this.sendDataAllButton100.IsEnabled = false;
                this.goToUserMode.IsEnabled = false;
            }
            else
            {
                this.UpdateConnectionStatus(true);
                this.Connect();

                this.sendDataAllButton.IsEnabled = (this.bytesToSend != null && this.isConnected);
                this.sendDataAllButton100.IsEnabled = (this.bytesToSend != null && this.isConnected);
                this.goToUserMode.IsEnabled = true;
            }
        }

        private void LoadProgramButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    this.firmware = new MspFirmware(dialog.FileName);
                    this.UpdateFirmwareSource(this.firmware);

                    this.bytesToSend = MspBoot.EncodeProgram(this.firmware);
                    this.loadedFileName.Text = $"{dialog.FileName} ({this.bytesToSend.Length} bytes)";

                    Console.WriteLine($"Sending: {this.bytesToSend.ToByteString("0x", ",")} Length = {this.bytesToSend.Length}");
                    this.sendDataAllButton.IsEnabled = (this.bytesToSend != null && this.isConnected);
                    this.sendDataAllButton100.IsEnabled = (this.bytesToSend != null && this.isConnected);

                    this.UpdateEncryptedDataTextBox();
                    this.UpdateEnryptedKey();
                }
                catch (Exception ex)
                {
                    BasicMessageBox.Show($"{ex.Message}\n{ex.StackTrace}", "Failed to load program");
                }
            }
        }

        private void UpdateFirmwareSource(MspFirmware firmware)
        {
            this.loadedBytes.Document.Blocks.Clear();

            var paragraph = new Paragraph
            {
                Padding = new Thickness(0.0),
                Margin = new Thickness(0.0)
            };

            foreach (var (address, data) in firmware.Segments)
            {
                paragraph.Inlines.Add(new Span(new Run($"0x{address:x}..0x{address + data.Length:x} ({data.Length} bytes)\n")));
                paragraph.Inlines.Add(new Span(new Run(data.ToByteString() + "\n\n")));
            }

            this.loadedBytes.Document.Blocks.Add(paragraph);
        }

        private async void InventoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await this.reader.Inventory(TimeSpan.FromSeconds(1000));
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                BasicMessageBox.Show($"{ex.Message}\n{ex.StackTrace}", "Failed to run inventory command");
            }
        }

        private async void UpdateVaildList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var tag in this.activeTags)
                {
                    tag.Value.LatestEpc = null;
                    tag.Value.ReadCount = 0;
                    tag.Value.ReadRate = 0.0;
                    tag.Value.Valid = false;
                }

                await this.reader.Inventory(TimeSpan.FromSeconds(1));

                foreach (var tag in this.activeTags)
                {
                    tag.Value.Valid = tag.Value.ReadCount != 0;
                }

                foreach (var tag in this.TagList)
                {
                    tag.OnPropertyChanged(nameof(tag.BackgroundColor));
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                BasicMessageBox.Show($"{ex.Message}\n{ex.StackTrace}", "Failed to run inventory command");
            }
        }

        private void RegenerateSessionKeyButton_Click(object sender, RoutedEventArgs e)
        {
            this.RegenerateSessionKey();
        }

        private void RegenerateSessionKey()
        {
            this.sessionKey = new byte[16];
            new Random().NextBytes(this.sessionKey);

            this.UpdateKeyTextBox(this.sessionKey.ToByteString());
            this.UpdateEnryptedKey();
            this.UpdateEncryptedDataTextBox();
        }

        private void TagList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is ListBox source)
            {
                if (source.SelectedItem is TagEntry item)
                {
                    this.selectedTag = item?.tag;
                    this.UpdateEnryptedKey();
                }
            }
        }

        private void TagList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.Source is ListBox source)
            {
                if (source.SelectedItem is TagEntry item)
                {
                    var key = $"{item.TagId:X4}";
                    if (!this.graphs.ContainsKey(key))
                    {
                        var window = new GraphWindow(key, item.tag);
                        this.graphs.Add(key, window);
                    }

                    var graphWindow = this.graphs[key];
                    graphWindow.Show();
                    graphWindow.Activate();
                }
            }
        }
        private void TagList_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.Source is ListBox source)
            {
                if (source.SelectedItem is TagEntry item)
                {
                    if (e.ChangedButton == System.Windows.Input.MouseButton.Right)
                    {
                        item.tag.Valid = !item.tag.Valid;
                        item.OnPropertyChanged(nameof(item.BackgroundColor));
                    }
                }
            }
        }

        private UpdateConfig GetConfig()
        {
            this.MaxAttempts = int.Parse(this.maxAttemptsTextBox.Text);
            var pilotStrategy = SecuCode.PilotTagSelectionFromIndex(this.pilotSelection.SelectedIndex);

            var validTags = this.activeTags.Values.Where(t => t.Valid).ToArray();
            if (validTags.Length == 0)
            {
                throw new Exception("No vaild tags, (tip: you may want to try the 'Update Valid Tags' button first).");
            }

            var sendMode = this.GetSendMode();
            var attestMode = this.GetAttestMode();

            if (sendMode == SendMode.None && attestMode == AttestMode.None)
            {
                throw new Exception("Both 'SendMode' and 'AttestMode' cannot be None");
            }

            return new UpdateConfig
            {
                TargetTags = validTags.ToDictionary(t => t.TagId),
                SessionKey = this.sessionKey,
                Firmware = this.firmware,
                TargetVersion = byte.Parse(this.version.Text),
                MaxAttempts = this.MaxAttempts,
                ReaderSleepTimeMs = (int)(double.Parse(this.readerOffTime.Text) * 1000.0),
                BlockSize = byte.Parse(this.maxBlockSize.Text),
                PilotSelectionStrategy = pilotStrategy,
                SendMode = sendMode,
                AttestMode = attestMode,
            };
        }

        private async Task<RunResults> RunUpdate()
        {
            var (config, progress) = this.Dispatcher.Invoke(() =>
            {
                var config = this.GetConfig();

                var progress = this.GetProgressWindow();
                progress.SetTargetTags(config.TargetTags.Values.ToArray(), config.SendMode != SendMode.None, config.AttestMode != AttestMode.None);

                return (config, progress);
            });

            using var updateRunner = new UpdateRunner(this.reader, config, progress);
            return await updateRunner.Run().ConfigureAwait(false);
        }

        private async void GoToUserMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var tag in this.activeTags.Where(t => t.Value.Valid))
                {
                    _ = await this.reader.SendRestartInUserMode(tag.Value.TagId, TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                BasicMessageBox.Show($"Failed to set tags into user mode {ex}", "Go To User Mode failed");
            }
        }

        private async void SendToAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await this.RunUpdate();
            }
            catch (Exception ex)
            {
                BasicMessageBox.Show($"Failed to broadcast to all tags {ex}", "Send to all failed");
            }
        }

        private async void SendDataAllButton100_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var repeatTimes = int.Parse(this.repeatTimes.Text.Trim());
                var logName = $"SecureBroadcast_log_{ this.GetSendMode()}.csv";
                await Task.Run(async () => await this.RunBenchmark(repeatTimes, logName, this.RunUpdate, "SendToAll"));
            }
            catch (Exception ex)
            {
                BasicMessageBox.Show($"Failed to run benchmark {ex}", "Benchmark failed");
            }
        }

        private void ShowGraphs_Click(object sender, RoutedEventArgs e)
        {
            foreach (var tag in this.activeTags.Values.Where(t => t.Valid))
            {
                var key = $"{tag.TagId:X4}";
                if (!this.graphs.ContainsKey(key))
                {
                    var window = new GraphWindow(key, tag);
                    this.graphs.Add(key, window);
                }

                var graphWindow = this.graphs[key];
                graphWindow.Show();
                graphWindow.Activate();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            this.reader.Disconnect();
            foreach (var graph in this.graphs)
            {
                graph.Value.Stopped = true;
                graph.Value.Close();
            }

            if (this.progressWindow != null)
            {
                this.progressWindow.Close();
            }
        }

        #endregion
    }

    public class EpcEntry : INotifyPropertyChanged
    {
        public string epc;
        public string Epc
        {
            get { return this.epc; }
            set { this.epc = value; this.OnPropertyChanged(nameof(this.Epc)); }
        }


        public int count;
        public int Count
        {
            get { return this.count; }
            set { this.count = value; this.OnPropertyChanged(nameof(this.Count)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class TagEntry : INotifyPropertyChanged
    {
        public Tag tag;

        public TagEntry(Tag tag)
        {
            this.tag = tag;
            this.TagId = string.Format("{0:X4}", tag.TagId);
            this.DevKey = tag.DeviceKey.ToByteString();
            this.Voltage = "???";
        }

        public string TagId { get; set; }
        public string DevKey { get; set; }

        private string voltage;
        public string Voltage
        {
            get { return this.voltage; }
            set { this.voltage = value; this.OnPropertyChanged(nameof(this.Voltage)); }
        }

        public Brush BackgroundColor
        {
            get { if (this.tag.Valid) { return new SolidColorBrush(Colors.LightGreen); } else { return new SolidColorBrush(Colors.Transparent); } }
        }


        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
