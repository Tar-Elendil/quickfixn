using NUnit.Framework;
using QuickFix;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    public class SocketInitiator_ConnectionTests
    {
        private const int ConnectPort = 55501;
        private const string TargetCompID = "target";
        private const string SenderCompID = "sender";
        private const string FIXMessageEnd = @"\x0110=\d{3}\x01";
        private const string FIXMessageDelimit = @"(8=FIX|\A).*?(" + FIXMessageEnd + @"|\z)";
        //private Dictionary<string, SocketState> _sessions = new Dictionary<string, SocketState>();
        //private HashSet<string> _loggedOnCompIDs = new HashSet<string>();
        //private SeqNumType _senderSequenceNumber = 1;
        private readonly SessionID _sessionID = new (Values.BeginString_FIX42, SenderCompID, TargetCompID);
        private const int HeartBeatInt = 30;

        private const string DefaultConfig = $@"
[DEFAULT]
StartTime = 12:00:00
EndTime = 12:00:00
ConnectionType = initiator
SocketConnectHost = 127.0.0.1
SocketConnectPort = 55501
UseDataDictionary = N
ReconnectInterval = 30
HeartBtInt = 30

[SESSION]
SenderCompID = sender
TargetCompID = target
BeginString = FIX.4.4
";

        private static SessionSettings CreateSettings()
        {
            return new SessionSettings(new StringReader(DefaultConfig));
        }

        public SocketInitiator CreateSocketInitiator()
        {
            var settings = CreateSettings();
            
            var lf = new NullLogFactory();
            var storeFactory = new MemoryStoreFactory();

            return new SocketInitiator(
                new NullApplication(),
                storeFactory,
                settings,
                lf);
/*
            var session = new Session(true, new NullApplication(), 
                storeFactory, settings.GetSessions().First(), new DataDictionaryProvider(), 
                new SessionSchedule(settings.Get()), HeartBeatInt, NullQuickFixLoggerFactory.Instance, new DefaultMessageFactory(), string.Empty);

            var ipEndpoint = new IPEndPoint(IPAddress.Loopback, ConnectPort);
            var socketSettings = new SocketSettings();
            socketSettings.Configure(settings.Get());

            return new SocketInitiatorThread(dummyInitiator, session, ipEndpoint, socketSettings, NullQuickFixLoggerFactory.Instance);*/
        }


        private class SocketState
        {
            public SocketState(Socket s)
            {
                Socket = s;
            }
            public readonly Socket Socket;
            public readonly byte[] RxBuffer = new byte[1024];
            public string MessageFragment = string.Empty;
        }

        private Socket ListenForConnection(bool expectFailure = false)
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, ConnectPort);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.Bind(endpoint);
                socket.Listen();
                return socket;
            }
            catch (Exception ex)
            {
                if (expectFailure) throw;
                Assert.Fail($"Failed to accept: {ex.Message}");
            }
            return socket;
        }

        private static void CloseAndNotify(Socket s)
        {
            lock (s)
            {
                if (s.Connected)
                {
                    try { s.Shutdown(SocketShutdown.Both); } catch { }
                }
                s.Close();
                SafePulse(s);
            }
        }

        private static void SafePulse(object socket)
        {
            try
            {
                lock (socket)
                {
                    Monitor.Pulse(socket);
                }
            }
            catch (ObjectDisposedException)
            {
                // Ignore, socket already disposed
            }
            catch (SynchronizationLockException)
            {
                // Ignore, socket already finalized and GC'ed
            }
        }


        private static bool WaitForDisconnect(Socket s)
        {
            lock (s)
            {
                const int timeoutMilliseconds = 10000; // total 10 seconds
                Stopwatch stopwatch = Stopwatch.StartNew();

                while (IsSocketConnected(s))
                {
                    int remainingTimeout = timeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds;
                    if (remainingTimeout <= 0)
                        break;

                    Monitor.Wait(s, Math.Min(remainingTimeout, 500)); // Wait at most 500ms or remaining
                }

                return !IsSocketConnected(s);
            }
        }

        private static bool IsSocketConnected(Socket socket)
        {
            try
            {
                if (!socket.Connected)
                    return false;

                bool connectionClosed = socket.Poll(0, SelectMode.SelectRead);
                bool dataAvailable = (socket.Available == 0);
                return !(connectionClosed && dataAvailable);
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public static async Task<bool> ReceivedLogon(Socket socket)
        {
            var handler = await socket.AcceptAsync();

            // Receive message.
            var buffer = new byte[1024];
            var received = await handler.ReceiveAsync(buffer, SocketFlags.None);
            var response = Encoding.UTF8.GetString(buffer, 0, received);

            return response?.Contains("35=A") ?? false;
        }

        [Test]
        public async Task ManualResetWhilstConnected()
        {
            using var initiator = CreateSocketInitiator();
            initiator.Start();
            var sessionId = initiator.GetSessionIDs().First();
            var session = Session.LookupSession(sessionId);
            Assert.That(session, Is.Not.Null);

            using var listener = ListenForConnection();
            
            Assert.That(await ReceivedLogon(listener));

            Assert.That(session!.IsEnabled);
            Assert.That(session!.SentLogon);
            Assert.DoesNotThrow(delegate { session!.Reset("Test"); });
        }

        [Test]
        public async Task ScheduledResetWhilstConnected()
        {
            using var initiator = CreateSocketInitiator();
            initiator.Start();
            var sessionId = initiator.GetSessionIDs().First();
            var session = Session.LookupSession(sessionId);
            Assert.That(session, Is.Not.Null);

            using var listener = ListenForConnection();
            var handler = await listener.AcceptAsync();
            while (true)
            {
                var buffer = new byte[1024];
                var received = await handler.ReceiveAsync(buffer, SocketFlags.None);
                var response = Encoding.UTF8.GetString(buffer, 0, received);

                break;
            }
            Assert.That(session!.IsEnabled);
            //Assert.That(session!.IsLoggedOn);
        }

        [SetUp]
        public void Setup()
        {
        }

        [TearDown]
        public void TearDown()
        {
        }
    }
}
