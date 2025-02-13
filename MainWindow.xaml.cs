using System.Net;
using System.Net.Sockets;
using MSMQ.Messaging;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ServiceProcess;
using System.Printing;

namespace MSMQTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MessageQueue? m_localQueue;
        private MessageQueue? m_remoteQueue;
        private bool m_isConnected = false;
        private IMessageFormatter m_formatter = new XmlMessageFormatter();

        public MainWindow()
        {
            InitializeComponent();
        }

        #region EVENT_HANDLERS

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtLocalIPAddress.Text = ".";
            txtLocalQueue.Text = "msmq_test_local_queue";

            txtRemoteIPAddress.Text = ".";
            txtRemoteQueue.Text = "msmq_test_local_queue";

            LogLocalIPAddress();

            // verify that the MSMQ feature is installed and the service is running
            if (!IsMsmqServiceRunning())
            {
                MessageBox.Show(this, "The MSMQ service is not running. Please start the service and try again.", "MSMQ Service Not Running", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CloseQueues("Application closing ...");
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs? e)
        {
            btnConnect.IsEnabled =
                !string.IsNullOrEmpty(txtLocalIPAddress.Text)
                && !string.IsNullOrEmpty(txtLocalQueue.Text)
                && !string.IsNullOrEmpty(txtRemoteIPAddress.Text)
                && !string.IsNullOrEmpty(txtRemoteQueue.Text);

            txtMessage.IsEnabled = m_isConnected;

            btnSend.IsEnabled = m_isConnected && !string.IsNullOrEmpty(txtMessage.Text);
        }

        private void OnConnectBtnClick(object sender, RoutedEventArgs e)
        {
            if (!m_isConnected)
            {
                Connect();

                if (!m_isConnected)
                {
                    CloseQueues("At least one queue failed to open. Disconnecting...");
                    Log("");
                }
            }
            else
            {
                CloseQueues("Disconnect requested ...");
                m_isConnected = false;
            }

            btnConnect.Content = m_isConnected ? "Disconnect" : "Connect";
            
            OnTextChanged(null, null);
        }

        private void OnSendBtnClick(object sender, RoutedEventArgs e)
        {
            SendMessage(txtMessage.Text);
            txtMessage.Text = string.Empty;
        }

        #endregion

        #region UTILITIES

        private void Connect()
        {
            Log("--- Connecting -------------------------------------------");
            m_localQueue = OpenQueue("local-queue", txtLocalIPAddress.Text, txtLocalQueue.Text);
            Log("");
            m_remoteQueue = OpenQueue("remote-queue", txtRemoteIPAddress.Text, txtRemoteQueue.Text);

            if (m_localQueue != null && m_remoteQueue != null)
            {
                m_isConnected = true;

                if (m_remoteQueue != null)
                {
                    m_remoteQueue.ReceiveCompleted += OnReceiveMessage;
                    m_remoteQueue.Purge();
                    m_remoteQueue.BeginReceive();
                }
            }
            else
            {
                m_isConnected = false;
            }
            Log("----------------------------------------------------------");
        }

        private void CloseQueues(string reason)
        {
            Log("");
            Log("--- Closing queues ---------------------------------------");
            Log("   " + reason);
            try
            {
                if (m_localQueue != null)
                {
                    m_localQueue.Close();
                    m_localQueue = null;
                    Log("   Closed local queue.");
                }
                if (m_remoteQueue != null)
                {
                    m_remoteQueue.ReceiveCompleted -= OnReceiveMessage;
                    m_remoteQueue.Close();
                    m_remoteQueue = null;
                    Log("   Closed remote queue.");
                }
            }
            catch (Exception ex)
            {
                Log("   Error closing queues: {0}", ex.Message);
            }
            Log("----------------------------------------------------------");
        }

        private MessageQueue? OpenQueue(string queueDescription, string address, string queueName)
        {
            MessageQueue? queue = null;
            try
            {
                string formatName = MakeFormatName(address, queueName);

                Log("Opening {0} ...", queueDescription);
                Log("     Formatter: {0}", rbBinary.IsChecked == true ? "Binary" : "XML");
                Log("       Address: {0}", address);
                Log("         Queue: {0}", queueName);
                Log("    FormatName: {0}", formatName);

                queue = OpenLocalQueue(formatName);
                queue.Formatter = rbBinary.IsChecked == true ? new BinaryMessageFormatter() : new XmlMessageFormatter();

                Log("        Status: Queue opened successfully.");
            }
            catch (Exception exception)
            {
                Log("        Status: Open failed - {0}", exception.Message);
            }
            return queue;
        }

        private void SendMessage(string message)
        {
            Log("> " + message);

            if (m_localQueue == null)
            {
                Log("Attempt to send message to a null queue.");
                return;
            }

            try
            {
                m_localQueue.Formatter = m_formatter;
                m_localQueue.Send(message);
            }
            catch (Exception ex)
            {
                Log("Error sending message: {0}", ex.Message);
            }
        }

        private void OnReceiveMessage(object sender, ReceiveCompletedEventArgs args)
        {
            if (m_remoteQueue == null)
            {
                Log("Attempt to receive message from a null queue.");
                return;
            }

            try
            {
                Message message = m_remoteQueue.EndReceive(args.AsyncResult);

                if (message != null)
                {
                    message.Formatter = m_formatter;

                    Log("< " + message.Body);
                }
                else
                {
                    Log("< NULL message received.");
                }

                m_remoteQueue.BeginReceive();
            }
            catch (Exception ex)
            {
                Log("< Error on receiving message: {0}", ex.Message);
            }
        }

        private void LogLocalIPAddress()
        {
            Log("--- Local IP Address(s) ----------------------------------");
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in  host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    Log("  " + ip.ToString());
                }
            }
            Log("----------------------------------------------------------");
            Log("");
        }

        public static bool IsMsmqServiceRunning()
        {
            List<ServiceController> services = ServiceController.GetServices().ToList();
            ServiceController? msQue = services.Find(o => o.ServiceName == "MSMQ");

            return ((msQue != null) && (msQue.Status == ServiceControllerStatus.Running));
        }

        public static string MakeFormatName(string address, string queueName)
        {
            string formatName;

            if (string.IsNullOrEmpty(address) || (address == "."))
            {
                formatName = string.Format(@".\Private$\{0}", queueName);
            }
            else
            {
                // FormatName: Direct=TCP:10.125.80.247\Private$\hmi-rm-out
                formatName = string.Format(@"Direct=TCP:{0}\Private$\{1}", address, queueName);
            }

            return formatName;
        }

        public MessageQueue OpenLocalQueue(string queueName)
        {
            MessageQueue queue;
            if (MessageQueue.Exists(queueName))
            {
                // it exists - open it
                Log("                Opening existing queue.");
                queue = new MessageQueue(queueName);
            }
            else
            {
                // create if it doesn't exist
                Log("                Creating new queue.");
                queue = MessageQueue.Create(queueName);
                queue.SetPermissions("ANONYMOUS LOGON",
                                        MessageQueueAccessRights.WriteMessage | MessageQueueAccessRights.ReceiveMessage | MessageQueueAccessRights.PeekMessage,
                                        AccessControlEntryType.Allow);
            }
            return queue;
        }

        #endregion

        #region LOGGER

        private void Log(string msg)
        {
            txtLog.Dispatcher.Invoke(() =>
            {
                txtLog.Text += msg + "\n";
                txtLog.ScrollToEnd();
            });
        }

        private void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }

        #endregion
    }
}