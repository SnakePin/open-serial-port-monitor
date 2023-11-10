using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Whitestone.OpenSerialPortMonitor.Main.Framework;
using Whitestone.OpenSerialPortMonitor.Main.Messages;

namespace Whitestone.OpenSerialPortMonitor.Main.ViewModels
{
    public class SerialDataSendViewModel : PropertyChangedBase, IHandle<SerialPortConnect>, IHandle<SerialPortDisconnect>, IHandle<ConnectionError>
    {
        private readonly IEventAggregator _eventAggregator;

        private string _dataToSend = string.Empty;
        private ICommand _EnterKeyCommand { get; set; }

        public string DataToSend
        {
            get { return _dataToSend; }
            set
            {
                _dataToSend = value;
                NotifyOfPropertyChange(() => DataToSend);
                NotifyOfPropertyChange(() => IsValidData);
            }
        }

        private bool _isText = true;
        public bool IsText
        {
            get { return _isText; }
            set
            {
                _isText = value;
                NotifyOfPropertyChange(() => IsText);
                NotifyOfPropertyChange(() => IsValidData);
            }
        }

        private bool _isHex = false;
        public bool IsHex
        {
            get { return _isHex; }
            set
            {
                _isHex = value;
                NotifyOfPropertyChange(() => IsHex);
                NotifyOfPropertyChange(() => IsValidData);
            }
        }

        public bool IsValidData
        {
            get
            {
                if (!IsConnected)
                {
                    return false;
                }

                if (DataToSend.Length <= 0)
                {
                    return false;
                }

                if (IsText) // Left this here for readability even though it is not needed. Otherwise someone might think it was missing or forgotten about.
                {
                }

                if (IsHex)
                {
                    try
                    {
                        string[] characters = DataToSend.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string hex in characters)
                        {
                            byte value = Convert.ToByte(hex, 16);
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private bool _isConnected = false;
        public bool IsConnected
        {
            get { return _isConnected; }
            set
            {
                _isConnected = value;
                NotifyOfPropertyChange(() => IsConnected);
                NotifyOfPropertyChange(() => IsValidData);
            }
        }
        public ICommand EnterKeyCommand
        {
            get { return _EnterKeyCommand; }
            set
            {
                _EnterKeyCommand = value;
                NotifyOfPropertyChange(() => EnterKeyCommand);
            }
        }

        public SerialDataSendViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.SubscribeOnPublishedThread(this);
            EnterKeyCommand = new KeyCommand(DoSend);
        }

        public void DoSend()
        {
            List<byte> data = new List<byte>();

            if (IsHex)
            {
                string[] characters = DataToSend.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string hex in characters)
                {
                    byte value = Convert.ToByte(hex, 16);
                    data.Add(value);
                }
            }

            if (IsText)
            {
                string parsed = DataToSend.Replace("\\\\r", "\r").Replace("\\\\n", "\n");
                data.AddRange(System.Text.Encoding.ASCII.GetBytes(parsed));
            }

            _eventAggregator.PublishOnUIThreadAsync(new Messages.SerialPortSend() { Data = data.ToArray() });
        }

        public async Task HandleAsync(SerialPortConnect message, CancellationToken cancellationToken)
        {
            IsConnected = true;
        }

        public async Task HandleAsync(SerialPortDisconnect message, CancellationToken cancellationToken)
        {
            IsConnected = false;
        }

        public async Task HandleAsync(ConnectionError message, CancellationToken cancellationToken)
        {
            IsConnected = false;
        }
    }
}
