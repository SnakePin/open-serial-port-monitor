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

namespace Whitestone.OpenSerialPortMonitor.Main.ViewModels
{
    public class SerialConnectorViewModel : PropertyChangedBase, IHandle<ConnectionError>
    {
        private readonly IEventAggregator _eventAggregator;

        public BindableCollection<string> ComPorts { get; set; }
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

        private SerialReader.SerialPortDefinition[] serialPortDefinitions;

        public SerialConnectorViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            BindPortNameList();
        }

        private void BindPortNameList()
        {
            serialPortDefinitions = SerialReader.GetAvailablePorts().ToArray();
            ComPorts = new BindableCollection<string>(serialPortDefinitions.Select(x => x.PortName));
            SelectedComPort = serialPortDefinitions.FirstOrDefault()?.PortName;
        }

        private void BindParameterValuesForPort(string portName)
        {
            var port = serialPortDefinitions.First(x => x.PortName == portName);
            BaudRates = new BindableCollection<int>(port.SupportedBaudRates);
            SelectedBaudRate = BaudRates.Contains(9600) ? 9600 : BaudRates.First();

            DataBits = new BindableCollection<int>(port.SupportedDataBits);
            SelectedDataBits = DataBits.Contains(8) ? 8 : DataBits.First();

            Parities = new BindableCollection<Parity>(port.SupportedParityTypes);
            SelectedParity = Parities.Contains(Parity.None) ? Parity.None : Parities.First();

            StopBits = new BindableCollection<StopBits>(port.SupportedStopBits);
            SelectedStopBits = StopBits.Contains(System.IO.Ports.StopBits.One) ? System.IO.Ports.StopBits.One : StopBits.First();
        }

        public void Connect()
        {
            IsConnected = true;

            _eventAggregator.PublishOnUIThread(new SerialPortConnect
            {
                PortName = SelectedComPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                Parity = SelectedParity,
                StopBits = SelectedStopBits
            });
        }

        public void Disconnect()
        {
            IsConnected = false;

            _eventAggregator.PublishOnUIThread(new SerialPortDisconnect());
        }

        public void Handle(ConnectionError message)
        {
            IsConnected = false;

            string errorMessage = message.Exception.Message;
            if (message.Exception.InnerException != null)
            {
                errorMessage = message.Exception.InnerException.Message;
            }
            MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
