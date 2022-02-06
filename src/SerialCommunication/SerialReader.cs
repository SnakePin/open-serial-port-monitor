using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Timers;

namespace Whitestone.OpenSerialPortMonitor.SerialCommunication
{
    public class SerialReader : IDisposable
    {
        // Constants
        private readonly int MAX_RECEIVE_BUFFER = 128;
        private readonly int BUFFER_TIMER_INTERVAL = 100;

        // Win32 Constants
        private static readonly ReadOnlyCollection<(int BaudRate, UInt32 FlagValue)> BaudRateWin32Flags = new ReadOnlyCollection<(int, UInt32)>(
        new[] {
            (75, 0x00000001u), (110, 0x00000002u),
            /* .NET doesn't support this one */ /*(134.5f, 0x00000004u),*/
            (150, 0x00000008u), (300, 0x00000010u),
            (600, 0x00000020u), (1200, 0x00000040u),
            (1800, 0x00000080u), (2400, 0x00000100u),
            (4800, 0x00000200u), (7200, 0x00000400u),
            (9600, 0x00000800u), (14400, 0x00001000u),
            (19200, 0x00002000u), (38400, 0x00004000u),
            (56000, 0x00008000u), (128000, 0x00010000u),
            (115200, 0x00020000u), (57600, 0x00040000u),
        });

        private static readonly ReadOnlyCollection<(int DataBits, UInt32 FlagValue)> DataBitsWin32Flags = new ReadOnlyCollection<(int, UInt32)>(
        new[] {
            (5, 0x00000001u), (6, 0x00000002u),
            (7, 0x00000004u), (8, 0x00000008u),
            (16, 0x00000010u)
        });

        private static readonly ReadOnlyCollection<(StopBits StopBits, UInt32 FlagValue)> StopBitsWin32Flags = new ReadOnlyCollection<(StopBits, UInt32)>(
        new[] {
            (StopBits.One, 0x00000001u),
            (StopBits.OnePointFive, 0x00000002u),
            (StopBits.Two, 0x00000004u),
        });

        private static readonly ReadOnlyCollection<(Parity Parity, UInt32 FlagValue)> ParityWin32Flags = new ReadOnlyCollection<(Parity, UInt32)>(
        new[] {
            (Parity.None, 0x00000100u),
            (Parity.Odd, 0x00000200u),
            (Parity.Even, 0x00000400u),
            (Parity.Mark, 0x00000800u),
            (Parity.Space, 0x00001000u),
       });

        // Event handlers
        public event EventHandler<SerialDataReceivedEventArgs> SerialDataReceived;

        // Private variables
        private SerialPort _serialPort = null;
        private List<byte> _receiveBuffer = null;
        private Timer _bufferTimer = null;
        private DateTime _lastReceivedData = DateTime.Now;
        private bool _disposed = false;
        private System.Threading.Thread _readThread = null;
        private bool _readThreadRunning = false;

        public class SerialPortDefinition
        {
            public SerialPortDefinition(string PortName, int[] SupportedBaudRates, int[] SupportedDataBits, Parity[] SupportedParityTypes, StopBits[] SupportedStopBits)
            {
                this.PortName = PortName;
                this.SupportedBaudRates = Array.AsReadOnly(SupportedBaudRates);
                this.SupportedDataBits = Array.AsReadOnly(SupportedDataBits);
                this.SupportedParityTypes = Array.AsReadOnly(SupportedParityTypes);
                this.SupportedStopBits = Array.AsReadOnly(SupportedStopBits);
            }

            public override string ToString()
            {
                return this.PortName;
            }

            public readonly string PortName;
            public readonly ReadOnlyCollection<int> SupportedBaudRates;
            public readonly ReadOnlyCollection<int> SupportedDataBits;
            public readonly ReadOnlyCollection<Parity> SupportedParityTypes;
            public readonly ReadOnlyCollection<StopBits> SupportedStopBits;
        }

        public SerialReader()
        {
            _receiveBuffer = new List<byte>(MAX_RECEIVE_BUFFER * 3);
        }

        public static IEnumerable<SerialPortDefinition> GetAvailablePorts()
        {
            var comPorts = new List<SerialPortDefinition>();

            foreach (string portName in SerialPort.GetPortNames())
            {
                SerialPort _port = null;
                try
                {
                    _port = new SerialPort(portName);
                    _port.Open();

                    T GetFieldValueReflection<T>(object obj, string fieldName, BindingFlags bFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    {
                        return (T)obj.GetType().GetField(fieldName, bFlags).GetValue(obj);
                    }

                    var commProp = GetFieldValueReflection<object>(_port.BaseStream, "commProp");
                    var dwSettableBaud = (UInt32)GetFieldValueReflection<Int32>(commProp, "dwSettableBaud");
                    var wSettableData = GetFieldValueReflection<UInt16>(commProp, "wSettableData");
                    var wSettableStopParity = GetFieldValueReflection<UInt16>(commProp, "wSettableStopParity");

                    var supportedBaudRates = BaudRateWin32Flags.Where(x => (x.FlagValue & dwSettableBaud) != 0).Select(x => x.BaudRate).OrderBy(x => x).ToArray();
                    var supportedDataBits = DataBitsWin32Flags.Where(x => (x.FlagValue & wSettableData) != 0).Select(x => x.DataBits).OrderBy(x => x).ToArray();
                    var supportedParity = ParityWin32Flags.Where(x => (x.FlagValue & wSettableStopParity) != 0).Select(x => x.Parity).ToArray();
                    var supportedStopBits = StopBitsWin32Flags.Where(x => (x.FlagValue & wSettableStopParity) != 0).Select(x => x.StopBits).ToArray();

                    comPorts.Add(new SerialPortDefinition(portName, supportedBaudRates, supportedDataBits, supportedParity, supportedStopBits));
                }
                finally
                {
                    _port?.Close();
                }
            }

            return comPorts.OrderBy(port => port);
        }

        public void Start(string portName, int baudRate, Parity parity, int dataBits, StopBits stopBits)
        {
            // Start the timer to empty the receive buffer (in case data smaller than MAX_RECEIVE_BUFFER is received)
            _bufferTimer = new Timer();
            _bufferTimer.Interval = BUFFER_TIMER_INTERVAL;
            _bufferTimer.Elapsed += _bufferTimer_Elapsed;
            _bufferTimer.Start();

            // Instantiate new serial port communication
            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);

            // Open serial port communication
            _serialPort.Open();

            // Check that it is actually open
            if (!_serialPort.IsOpen)
            {
                throw new Exception(string.Format("Could not open serial port: {0}", portName));
            }

            _serialPort.ReadTimeout = 100; // Milliseconds

            _readThread = new System.Threading.Thread(ReadThread);
            _readThreadRunning = true;
            _readThread.Start();
        }

        private async void ReadThread()
        {
            while (_readThreadRunning)
            {
                try
                {
                    // This is the proper way to read data, according to http://www.sparxeng.com/blog/software/must-use-net-system-io-ports-serialport
                    // Though he uses BeginRead/EndRead, ReadAsync is the preferred way in .NET 4.5
                    byte[] buffer = new byte[MAX_RECEIVE_BUFFER * 3];
                    int bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length);

                    byte[] received = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, received, 0, bytesRead);
                    lock (_receiveBuffer)
                    {
                        _receiveBuffer.AddRange(received);
                    }
                }
                catch
                {

                }
            }
        }

        public void Send(byte[] data)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Write(data, 0, data.Length);
            }
        }

        public void Stop()
        {
            _readThreadRunning = false;

            // Disconnect from the serial port
            if (_serialPort != null)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }

            // Stop the timer used for the buffer
            if (_bufferTimer != null)
            {
                _bufferTimer.Stop();
                _bufferTimer = null;
            }

            // Send remaining data in the receive buffer
            lock (_receiveBuffer)
            {
                SendBuffer(ref _receiveBuffer);
            }
        }

        private void _bufferTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (_receiveBuffer)
            {
                // Only send data if the last data received was more than BUFFER_TIMER_INTERVAL milliseconds ago
                // This is to ensure it only empties the buffer when not a lot of data has been received lately
                if ((DateTime.Now - _lastReceivedData).TotalMilliseconds > BUFFER_TIMER_INTERVAL && _receiveBuffer.Count > 0)
                {
                    SendBuffer(ref _receiveBuffer);
                }
            }
        }

        private void SendBuffer(ref List<byte> buffer)
        {
            byte[] byteBuffer = buffer.ToArray();
            buffer.Clear();
            SerialDataReceived?.Invoke(this, new SerialDataReceivedEventArgs() { Data = byteBuffer });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                Stop();
            }

            _disposed = true;
        }
    }
}
