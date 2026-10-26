using DemoRestaurant.Logic.Online;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class OnlineStartupTests
    {
        [Test]
        public void Unconfigured_OrOfflineChoice_StaysOffline()
        {
            Assert.AreEqual(StartupStep.None, StartupPlanner.Next(new StartupFacts()));
            Assert.AreEqual(StartupStep.None, StartupPlanner.Next(new StartupFacts { Configured = true, PlayOffline = true }));
            Assert.AreEqual(OnlineState.Offline, StartupPlanner.StateFor(StartupStep.None));
        }

        [Test]
        public void Steps_FollowTheContractOrder()
        {
            var f = new StartupFacts { Configured = true };
            Assert.AreEqual(StartupStep.ShowSignIn, StartupPlanner.Next(f));
            f.SignedIn = true;
            Assert.AreEqual(StartupStep.ShowVerifyEmail, StartupPlanner.Next(f));
            f.EmailVerified = true;
            Assert.AreEqual(StartupStep.Bootstrap, StartupPlanner.Next(f));
            f.Bootstrapped = true;
            Assert.AreEqual(StartupStep.LoadConfig, StartupPlanner.Next(f));
            f.ConfigLoaded = true;
            Assert.AreEqual(StartupStep.Sync, StartupPlanner.Next(f));
            Assert.AreEqual(OnlineState.Connecting, StartupPlanner.StateFor(StartupStep.Sync));
            f.Synced = true;
            Assert.AreEqual(StartupStep.Done, StartupPlanner.Next(f));
            Assert.AreEqual(OnlineState.Online, StartupPlanner.StateFor(StartupStep.Done));
        }

        [TestCase("a@b.co", true)]
        [TestCase(" player@example.com ", true)]
        [TestCase("no-at.com", false)]
        [TestCase("a@b", false)]
        [TestCase("a@@b.com", false)]
        [TestCase("a b@c.com", false)]
        [TestCase("", false)]
        public void Email_BasicShape(string email, bool ok)
        {
            Assert.AreEqual(ok, CredentialRules.LooksLikeEmail(email));
        }

        [Test]
        public void Password_LengthOnlyForNewAccounts()
        {
            Assert.AreEqual(CredentialCheck.PasswordTooShort, CredentialRules.Check("a@b.co", "short", newAccount: true));
            Assert.AreEqual(CredentialCheck.Ok, CredentialRules.Check("a@b.co", "short", newAccount: false));
            Assert.AreEqual(CredentialCheck.PasswordTooShort, CredentialRules.Check("a@b.co", "", newAccount: false));
            Assert.AreEqual(CredentialCheck.EmailInvalid, CredentialRules.Check("nope", "longenough", newAccount: true));
        }
    }
}
