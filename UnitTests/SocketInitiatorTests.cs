using NUnit.Framework;
using QuickFix;
using QuickFix.Logger;
using QuickFix.Store;
using QuickFix.Transport;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{

    [TestFixture]
    public class SocketInitiatorTests
    {
        protected SessionTestSupport.MockResponder _responder = new();

        private const string DefaultConfig = $@"
[DEFAULT]
StartTime = 00:00:00
EndTime = 23:59:59
ConnectionType = initiator
SocketConnectHost = 127.0.0.1
SocketConnectPort = 50000
UseDataDictionary = N
ReconnectInterval = 30
HeartBtInt = 30

[SESSION]
SenderCompID = sender
TargetCompID = target
BeginString = FIX.4.4
";
        private static SettingsDictionary GenerateRandomSettings(SessionID sessionID)
        {
            var dictionary = new SettingsDictionary();
            dictionary.SetString(SessionSettings.BEGINSTRING, sessionID.BeginString);
            dictionary.SetString(SessionSettings.SENDERCOMPID, sessionID.SenderCompID);
            dictionary.SetString(SessionSettings.TARGETCOMPID, sessionID.TargetCompID);
            return dictionary;
        }

        private static SessionSettings CreateSettings()
        {
            return new SessionSettings(new StringReader(DefaultConfig));
        }

        private static SocketInitiator CreateInitiator()
        {
            var settings = CreateSettings();
            var lf = new NullLogFactory();

            return new SocketInitiator(
                new NullApplication(),
                new MemoryStoreFactory(),
                settings,
                lf);

        }

        [Test]
        public void TestInitiatorRecreation()
        {
            for (int i = 0; i < 3; i++)
            {
                var initiator = CreateInitiator();
                initiator.Start();
                initiator.Dispose();
            }
        }

        [Test]
        public void TestAddSession()
        {
            using var initiator = CreateInitiator();
            initiator.Start();
            for (var i = 0; i < 10; i++)
            {
                var sessionID = new SessionID("FIX.4.4", $"INITSENDER{i}", $"INITTARGET{i}");
                Assert.That(initiator.AddSession(sessionID, GenerateRandomSettings(sessionID)), Is.True);
            }

            Assert.That(initiator.GetSessionIDs(), Has.Exactly(11).Items);

            initiator.Stop();
        }

        [Test]
        public void TestRemoveSession()
        {
            using var initiator = CreateInitiator();
            initiator.Start();
            for (var i = 0; i < 10; i++)
            {
                var sessionID = new SessionID("FIX.4.4", $"INITSENDER{i}", $"INITTARGET{i}");
                Assert.That(initiator.AddSession(sessionID, GenerateRandomSettings(sessionID)), Is.True);
            }

            Assert.That(initiator.GetSessionIDs(), Has.Exactly(11).Items);

            for (var i = 0; i < 10; i++)
            {
                var sessionID = new SessionID("FIX.4.4", $"INITSENDER{i}", $"INITTARGET{i}");
                initiator.RemoveSession(sessionID, true);
            }

            Assert.That(initiator.GetSessionIDs(), Has.Exactly(1).Items);

            initiator.Stop();
        }

        [Test]
        public async Task TestSessionReset()
        {
            using var initiator = CreateInitiator();
            initiator.Start();
            for (var i = 0; i < 10; i++)
            {
                var sessionID = new SessionID("FIX.4.4", $"INITSENDER{i}", $"INITTARGET{i}");
                Assert.That( initiator.AddSession(sessionID, GenerateRandomSettings(sessionID)), Is.True);
            }

            foreach (var sessionID in initiator.GetSessionIDs())
            {
                var session = Session.LookupSession(sessionID);
                Assert.That(session, Is.Not.Null);
                session!.SetResponder(_responder);
                
                Assert.That(session!.HasResponder, Is.True);

                session!.Reset("Test");
                Assert.That(session!.HasResponder, Is.False);
            }

            initiator.Stop();
        }
    }
}
