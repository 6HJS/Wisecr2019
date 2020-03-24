using LiveCharts;
using LiveCharts.Wpf;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SecuCodeApp
{
    /// <summary>
    /// Interaction logic for GraphWindow.xaml
    /// </summary>
    public partial class GraphWindow : Window
    {
        public SeriesCollection SeriesCollection { get; set; } = new SeriesCollection();
        public bool Stopped { get; set; } = false;

        public GraphManager graphManager;

        public GraphWindow(string title, Tag tag)
        {
            this.DataContext = this;
            this.InitializeComponent();

            this.Title = title;
            this.graphManager = new GraphManager(this.valueAxis, this.SeriesCollection, tag);

            Task.Run(() => this.graphManager.UpdateGraphsTask());
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!this.Stopped)
            {
                e.Cancel = true;
                this.Hide();
            }
        }
        
        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            this.graphManager.isVisible = this.IsVisible;
        }
    }

    public class GraphManager
    {
        public bool isVisible = false;

        public SeriesCollection SeriesCollection { get; set; }

        private LineSeries temperature = new LineSeries { Title = "Temperature", Values = new ChartValues<double>(), PointGeometry = null, Fill = Brushes.Transparent };
        private LineSeries accelX = new LineSeries { Title = "Accel_x", Values = new ChartValues<double>(), PointGeometry = null, Fill = Brushes.Transparent };
        private LineSeries accelY = new LineSeries { Title = "Accel_y", Values = new ChartValues<double>(), PointGeometry = null, Fill = Brushes.Transparent };
        private LineSeries accelZ = new LineSeries { Title = "Accel_z", Values = new ChartValues<double>(), PointGeometry = null, Fill = Brushes.Transparent };

        private MeanTracker currentAccelX = new MeanTracker();
        private MeanTracker currentAccelY = new MeanTracker();
        private MeanTracker currentAccelZ = new MeanTracker();
        private MeanTracker currentTemp = new MeanTracker();
        private Axis valueAxis;

        enum ScaleType { Accel, Temp };
        private ScaleType scaleType = ScaleType.Accel;

        private readonly Tag tag;

        public GraphManager(Axis valueAxis, SeriesCollection series, Tag tag)
        {
            this.tag = tag;
            this.SeriesCollection = series;

            this.valueAxis = valueAxis;
            this.valueAxis.MinValue = -2.0;
            this.valueAxis.MaxValue = 2.0;

            this.SeriesCollection.Add(this.temperature);
            this.SeriesCollection.Add(this.accelX);
            this.SeriesCollection.Add(this.accelY);
            this.SeriesCollection.Add(this.accelZ);
        }

        public async void UpdateGraphsTask()
        {
            var i = 0;
            try
            {
                while (true)
                {
                    await Task.Delay(50);
                    if (!this.isVisible)
                    {
                        continue;
                    }

                    this.accelX.Values.Add(this.currentAccelX.Reset());
                    this.accelY.Values.Add(this.currentAccelY.Reset());
                    this.accelZ.Values.Add(this.currentAccelZ.Reset());
                    this.temperature.Values.Add(this.currentTemp.Reset());

                    if (i > 40)
                    {
                        this.accelX.Values.RemoveAt(0);
                        this.accelY.Values.RemoveAt(0);
                        this.accelZ.Values.RemoveAt(0);
                        this.temperature.Values.RemoveAt(0);
                    }
                    i += 1;
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
        }

        public void SetAccelScale()
        {
            if (this.scaleType != ScaleType.Accel)
            {
                this.valueAxis.Dispatcher.Invoke(() =>
                {
                    this.valueAxis.MinValue = -2;
                    this.valueAxis.MaxValue = 2;
                    this.scaleType = ScaleType.Accel;
                });
            }
        }

        public void SetTempScale()
        {
            if (this.scaleType != ScaleType.Temp)
            {
                this.valueAxis.Dispatcher.Invoke(() =>
                {
                    this.valueAxis.MinValue = 0;
                    this.valueAxis.MaxValue = 40;
                    this.scaleType = ScaleType.Temp;
                });
            }
        }

        public void SetAccel(double x, double y, double z)
        {
            this.SetAccelScale();
            this.currentAccelX.Next(x);
            this.currentAccelY.Next(y);
            this.currentAccelZ.Next(z);
        }

        public void UpdateSensorValues(byte[] epc)
        {
            if (epc[1] == 0xA1)
            {
                this.SetAccelScale();
                this.SetAccel(
                    ConvertAccelValue(epc[6]),
                    ConvertAccelValue(epc[4]),
                    ConvertAccelValue(epc[8])
                );
            }
            else
            {
                this.currentAccelX.Next(0.0);
                this.currentAccelY.Next(0.0);
                this.currentAccelZ.Next(0.0);
            }

            if (epc[1] == 0xA0)
            {
                this.SetTempScale();
                this.currentTemp.Next(this.ConvertTempValue(epc[7], epc[8]));
            }
            else
            {
                this.currentTemp.Next(0.0);
            }
        }

        private static double ConvertAccelValue(byte lsb)
        {
            return 4.0 * (lsb - 128) / 250.0;
        }

        private double ConvertTempValue(byte msb, byte lsb)
        {
            return ((msb << 8) | lsb) / this.tag.TempDivisor + this.tag.TempOffset;
        }
    }
}
