using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SecuCodeApp
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow : Window, IProgressListener
    {
        public ObservableCollection<ProgressEntry> TagList { get; set; } = new ObservableCollection<ProgressEntry>();

        public ProgressWindow(Tag[] tags)
        {
            this.SetTargetTags(tags);
            this.DataContext = this;
            this.InitializeComponent();
        }

        public void SetTargetTags(Tag[] tags, bool firmwareBarEnabled = true, bool attestBarEnabled = true)
        {
            this.TagList.Clear();
            foreach (var tag in tags)
            {
                this.TagList.Add(new ProgressEntry(tag, firmwareBarEnabled, attestBarEnabled));
            }
        }

        public void SetPilotTag(ushort tagId)
        {
            foreach (var entry in this.TagList)
            {
                entry.IsPilot = entry.tag.TagId == tagId;
            }
        }

        private void UpdateTag(ushort tagId, double stageProgress, string status = null, Stage? stage = null)
        {
            var entry = this.TagList.First(entry => entry.tag.TagId == tagId);
            if (entry == null)
            {
                return;
            }

            // TODO: clean this up -- we want to be able to have sub-stages within a stage, 
            // without resetting the progress bar, but this currently isn't implemented, so we 
            // special case the attestation stage
            if (stage == Stage.Attesting && entry.CurrentStage != Stage.Attesting)
            {
                entry.isAttesting = true;
                this.SetPilotTag(tagId);
            }

            // Clear any errors for the current tag
            this.SetError(tagId, null);

            entry.CurrentStage = stage != null ? stage.Value : entry.CurrentStage;
            var totalProgress = 100.0 * TotalProgress(entry.CurrentStage, stageProgress);

            entry.Status = status;

            if (!entry.isAttesting)
            {
                entry.updateStage = entry.CurrentStage;
                entry.Progress = totalProgress;
                entry.OnPropertyChanged(nameof(entry.UpdateStatus));
            }
            else
            {
                entry.attestStage = entry.CurrentStage;
                entry.AttestProgress = totalProgress;
                entry.OnPropertyChanged(nameof(entry.AttestStatus));
            }
        }

        public void SetError(ushort tagId, string error)
        {
            var entry = this.TagList.First(entry => entry.tag.TagId == tagId);
            if (entry == null)
            {
                return;
            }

            if (!entry.isAttesting)
            {
                entry.error = error != null;
                entry.OnPropertyChanged(nameof(entry.ProgressColor));
            }
            else
            {
                entry.attestError = error != null;
                entry.OnPropertyChanged(nameof(entry.AttestProgressColor));
            }

            if (error != null)
            {
                entry.Status = error;
            }
        }

        public void LogMessage(string message)
        {
            _ = this.textBlock.Dispatcher.InvokeAsync(() =>
            {
                this.textBlock.AppendText(message + "\n");
                this.scrollViewer.ScrollToEnd();
            });
        }

        public void Clear()
        {
            this.textBlock.Dispatcher.Invoke(() => this.textBlock.Document.Blocks.Clear());
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            this.Clear();
        }

        public void Error(string error, ushort? tag)
        {
            this.SetError(tag.Value, error);
        }

        public void Update(ushort tag, Stage stage, double stageProgress, string description = null)
        {
            this.UpdateTag(tag, stageProgress, description, stage);
        }

        public void Update(ushort tag, double stageProgress, string description = null)
        {
            this.UpdateTag(tag, stageProgress, description);
        }

        public void Info(string message)
        {
            this.LogMessage(message);
        }

        private static double TotalProgress(Stage stage, double stageProgress)
        {
            var (current, next) = stage switch
            {
                Stage.Waiting => (0, 0.1),
                Stage.GoToBootloader => (0.1, 0.2),

                Stage.SendSessionKey => (0.2, 0.4),
                Stage.WaitingForDataObserver => (0.4, 0.8),
                Stage.SendingData => (0.4, 0.8),
                Stage.Updated => (0.8, 1.0),

                Stage.Attesting => (0.0, 0.1),
                Stage.SendingChallenge => (0.2, 0.4),
                Stage.ReadingResponse => (0.8, 1.0),
                Stage.Done => (1.0, 1.0),
                Stage.Skipped => (1.0, 1.0),

                _ => (0.0, 0.0),
            };

            return current + (next - current) * stageProgress;
        }
    }

    public class ProgressEntry : INotifyPropertyChanged
    {
        public Tag tag;

        public ProgressEntry(Tag tag, bool firmwareBarEnabled, bool attestBarEnabled)
        {
            this.tag = tag;
            this.voltage = tag.Voltage.ToString();
            this.error = false;

            this.firmwareBarEnabled = firmwareBarEnabled;
            this.attestBarEnabled = attestBarEnabled;
        }

        private readonly bool firmwareBarEnabled;
        public GridLength FirmwareColumnWidth => firmwareBarEnabled ?
            new GridLength(2, GridUnitType.Star) :
            new GridLength(0, GridUnitType.Pixel);

        private readonly bool attestBarEnabled = true;
        public GridLength AttestColumnWidth => attestBarEnabled ?
            new GridLength(1, GridUnitType.Star) :
            new GridLength(0, GridUnitType.Pixel);

        public string TagId => $"Tag: {this.tag.TagId:X04}";

        private string voltage;
        public string Voltage
        {
            get { return this.voltage; }
            set { this.voltage = value; this.OnPropertyChanged(nameof(this.Voltage)); }
        }

        private double progress = 0.0;
        public double Progress
        {
            get { return this.progress; }
            set { this.progress = value; this.OnPropertyChanged(nameof(this.Progress)); }
        }

        private double attestProgress = 0.0;
        public double AttestProgress
        {
            get { return this.attestProgress; }
            set { this.attestProgress = value; this.OnPropertyChanged(nameof(this.AttestProgress)); }
        }

        public Stage CurrentStage = Stage.Waiting;
        public bool isAttesting = false;

        private string status = null;
        public string Status
        {
            get { return this.status ?? ""; }
            set { this.status = value; this.OnPropertyChanged(nameof(this.Status)); }
        }


        public Stage? updateStage = null;
        public string UpdateStatus => this.updateStage != null ? $"Firmware update: {this.updateStage.ToString()}" : "Firmware update";

        public Stage? attestStage = null;
        public string AttestStatus => this.attestStage != null ? $"Remote attestation: {this.attestStage.ToString()}" : "Remote attestation";


        private bool isPilot = false;
        public bool IsPilot
        {
            get { return this.isPilot; }
            set
            {
                this.isPilot = value;
                this.OnPropertyChanged(nameof(this.IsPilot));
                this.OnPropertyChanged(nameof(this.FontWeight));
            }
        }

        public FontWeight FontWeight => this.isPilot ? FontWeights.Bold : FontWeights.Normal;


        public bool error;

        public Brush ProgressColor => this.error ?
            new SolidColorBrush(Color.FromRgb(0xB0, 0x06, 0x25)) :
            new SolidColorBrush(Color.FromRgb(0x06, 0xB0, 0x25));


        public bool attestError;
        public Brush AttestProgressColor => this.attestError ?
            new SolidColorBrush(Color.FromRgb(0xB0, 0x06, 0x25)) :
            new SolidColorBrush(Color.FromRgb(0x06, 0x25, 0xB0));


        public Brush BackgroundColor => new SolidColorBrush(Colors.Transparent);


        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
