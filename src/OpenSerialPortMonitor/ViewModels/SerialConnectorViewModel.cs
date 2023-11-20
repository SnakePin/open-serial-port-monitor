using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caliburn.Micro;
using Whitestone.OpenSerialPortMonitor.SerialCommunication;
using Whitestone.OpenSerialPortMonitor.Main.Messages;
using System.IO.Ports;
using System.Windows;
using System.Threading;

namespace Whitestone.OpenSerialPortMonitor.Main.ViewModels
{
    public class SerialConnectorViewModel : PropertyChangedBase, IHandle<ConnectionError>
    {
        private readonly IEventAggregator _eventAggregator;

        public BindableCollection<string> ComPortNames { get; set; }
        public BindableCollection<int> BaudRates { get; set; }
        public BindableCollection<Parity> Parities { get; set; }
        public BindableCollection<int> DataBits { get; set; }
        public BindableCollection<StopBits> StopBits { get; set; }

        private string _selectedComPort = null;
        public string SelectedComPort
        {
            get
            {
                return _selectedComPort;
            }
            set
            {
                _selectedComPort = value;
                if (value != null)
                {
                    BindParameterValuesForPort(value);
                }
            }
        }
        public int SelectedBaudRate { get; set; }
        public int SelectedDataBits { get; set; }
        public Parity SelectedParity { get; set; }
        public StopBits SelectedStopBits { get; set; }

        private bool _isConnected = false;
        public bool IsConnected
        {
            get { return _isConnected; }
            set
            {
                _isConnected = value;
                NotifyOfPropertyChange(() => IsConnected);
                NotifyOfPropertyChange(() => IsDisconnected);
            }
        }
        public bool IsDisconnected
        {
            get { return !_isConnected; }
            set
            {
                _isConnected = !value;
                NotifyOfPropertyChange(() => IsConnected);
                NotifyOfPropertyChange(() => IsDisconnected);
            }
        }

        private Dictionary<string, SerialReader.SerialPortDefinition> serialPortMap;

        public SerialConnectorViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.SubscribeOnPublishedThread(this);

            serialPortMap = SerialReader.GetAvailablePorts().ToDictionary(x => x.PortName);
            ComPortNames = new BindableCollection<string>(serialPortMap.Keys);
            SelectedComPort = serialPortMap.First().Key;
        }

        private void BindParameterValuesForPort(string portName)
        {
            var port = serialPortMap[portName];

            BaudRates = new BindableCollection<int>(port.SupportedBaudRates);
            SelectedBaudRate = BaudRates.Contains(9600) ? 9600 : BaudRates.First();

            DataBits = new BindableCollection<int>(port.SupportedDataBits);
            SelectedDataBits = DataBits.Contains(8) ? 8 : DataBits.First();

            Parities = new BindableCollection<Parity>(port.SupportedParityTypes);
            SelectedParity = Parities.Contains(Parity.None) ? Parity.None : Parities.First();

            StopBits = new BindableCollection<StopBits>(port.SupportedStopBits);
            SelectedStopBits = StopBits.Contains(System.IO.Ports.StopBits.One) ? System.IO.Ports.StopBits.One : StopBits.First();
        }

        public async Task ConnectAsync()
        {
            IsConnected = true;

            await _eventAggregator.PublishOnUIThreadAsync(new SerialPortConnect
            {
                PortName = SelectedComPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                Parity = SelectedParity,
                StopBits = SelectedStopBits
            });
        }

        public async Task DisconnectAsync()
        {
            IsConnected = false;

            await _eventAggregator.PublishOnUIThreadAsync(new SerialPortDisconnect());
        }

        public async Task HandleAsync(ConnectionError message, CancellationToken cancellationToken)
        {
            IsConnected = false;

            string errorMessage = message.Exception.Message;
            if (message.Exception.InnerException != null)
            {
                errorMessage = message.Exception.InnerException.Message;
            }
            await Task.Run(() => MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }
}
