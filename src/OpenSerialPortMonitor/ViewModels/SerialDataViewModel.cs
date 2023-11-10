using Caliburn.Micro;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Whitestone.OpenSerialPortMonitor.Main.Framework;
using Whitestone.OpenSerialPortMonitor.Main.Messages;
using Whitestone.OpenSerialPortMonitor.SerialCommunication;

namespace Whitestone.OpenSerialPortMonitor.Main.ViewModels
{
    public class SerialDataViewModel : PropertyChangedBase, IHandle<SerialPortConnect>, IHandle<SerialPortDisconnect>, IHandle<Autoscroll>, IHandle<SerialPortSend>, IHandle<ClearScreen>
    {
        private readonly IEventAggregator _eventAggregator;
        private SerialReader _serialReader;
        private System.Timers.Timer _cacheTimer;
        private int _rawDataCounter = 0;

        public SerialDataViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.SubscribeOnPublishedThread(this);

            _serialReader = new SerialReader();
        }

        private bool _isAutoscroll = true;
        public bool IsAutoscroll
        {
            get { return _isAutoscroll; }
            set
            {
                _isAutoscroll = value;
                NotifyOfPropertyChange(() => IsAutoscroll);
            }
        }

        private StringBuilder _dataViewParsedBuilder = new StringBuilder();
        private string _dataViewParsed = string.Empty;
        public string DataViewParsed
        {
            get { return _dataViewParsed; }
            set
            {
                _dataViewParsed = value;
                NotifyOfPropertyChange(() => DataViewParsed); //TODO: This can throw if the task is cancelled, which closing the window results in.
            }
        }

        private StringBuilder _dataViewRawBuilder = new StringBuilder();
        private string _dataViewRaw = string.Empty;
        public string DataViewRaw
        {
            get { return _dataViewRaw; }
            set
            {
                _dataViewRaw = value;
                NotifyOfPropertyChange(() => DataViewRaw);
            }
        }

        private StringBuilder _dataViewHexBuilder = new StringBuilder();
        private string _dataViewHex = string.Empty;
        public string DataViewHex
        {
            get { return _dataViewHex; }
            set
            {
                _dataViewHex = value;
                NotifyOfPropertyChange(() => DataViewHex);
            }
        }

        public async Task HandleAsync(SerialPortConnect message, CancellationToken cancellationToken)
        {
            try
            {
                _cacheTimer = new System.Timers.Timer();
                _cacheTimer.Interval = 200;
                _cacheTimer.Elapsed += _cacheTimer_Elapsed;
                _cacheTimer.Start();

                _serialReader = new SerialReader();
                _serialReader.Start(message.PortName, message.BaudRate, message.Parity, message.DataBits, message.StopBits);
                _serialReader.SerialDataReceived += SerialDataReceived;
            }
            catch (Exception ex)
            {
                await _eventAggregator.PublishOnUIThreadAsync(new ConnectionError() { Exception = ex });
            }
        }

        public async Task HandleAsync(SerialPortDisconnect message, CancellationToken cancellationToken)
        {
            _cacheTimer.Stop();
            _serialReader.Stop();
            queuedLock.Enter();
            queuedLock.Exit();
        }

        void _cacheTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                queuedLock.Enter();
                string dataParsed = _dataViewParsedBuilder.ToString();
                _dataViewParsedBuilder = new StringBuilder();

                string dataHex = _dataViewHexBuilder.ToString();
                _dataViewHexBuilder = new StringBuilder();

                string dataRaw = _dataViewRawBuilder.ToString();
                _dataViewRawBuilder = new StringBuilder();

                string TrimFromStart(string str, int size = 0x8000) { return (str.Length > size) ? str.Remove(0, str.Length - size) : str; }
                DataViewParsed = TrimFromStart(DataViewParsed);
                DataViewHex = TrimFromStart(DataViewHex);
                DataViewRaw = TrimFromStart(DataViewRaw);

                DataViewParsed += dataParsed;
                DataViewHex += dataHex;
                DataViewRaw += dataRaw;
            }
            finally
            {
                queuedLock.Exit();
            }
        }

        public async Task HandleAsync(Autoscroll message, CancellationToken cancellationToken)
        {
            IsAutoscroll = message.IsTurnedOn;
        }

        public async Task HandleAsync(ClearScreen message, CancellationToken cancellationToken)
        {
            DataViewParsed = string.Empty;
            DataViewHex = string.Empty;
            DataViewRaw = string.Empty;
        }

        QueuedLock queuedLock = new QueuedLock();
        void SerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                queuedLock.Enter();
                _dataViewParsedBuilder.Append(Encoding.ASCII.GetString(e.Data));

                foreach (byte data in e.Data)
                {
                    char character = (char)data;
                    if (char.IsControl(character))
                    {
                        character = '.';
                    }

                    _dataViewHexBuilder.Append(string.Format("{0:x2} ", data));
                    _dataViewRawBuilder.Append(character);

                    if (++_rawDataCounter == 16)
                    {
                        _dataViewHexBuilder.Append("\r\n");
                        _dataViewRawBuilder.Append("\r\n");
                        _rawDataCounter = 0;
                    }
                }
            }
            finally
            {
                queuedLock.Exit();
            }
        }

        public async Task HandleAsync(SerialPortSend message, CancellationToken cancellationToken)
        {
            await Task.Run(() => _serialReader.Send(message.Data));
        }
    }
}
