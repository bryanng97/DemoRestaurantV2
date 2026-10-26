using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class ApiErrorPolicyTests
    {
        [TestCase(0, null, ApiAction.Backoff)]
        [TestCase(500, "INTERNAL", ApiAction.Backoff)]
        [TestCase(503, null, ApiAction.Backoff)]
        [TestCase(429, "RATE_LIMITED", ApiAction.RetryAfter)]
        [TestCase(401, "UNAUTHENTICATED", ApiAction.RefreshSession)]
        [TestCase(403, "EMAIL_NOT_VERIFIED", ApiAction.VerifyEmail)]
        [TestCase(403, "OTHER", ApiAction.Fail)]
        [TestCase(404, "NOT_FOUND", ApiAction.NotFound)]
        [TestCase(409, "PLAYER_NOT_INITIALIZED", ApiAction.Bootstrap)]
        [TestCase(409, "REVISION_CONFLICT", ApiAction.Reload)]
        [TestCase(409, "INSUFFICIENT_COINS", ApiAction.Reload)]
        [TestCase(409, "OWNERSHIP_MISMATCH", ApiAction.Reload)]
        [TestCase(409, "RULESET_OUTDATED", ApiAction.RefreshConfig)]
        [TestCase(409, "LEVEL_TOO_LOW", ApiAction.Fail)]
        [TestCase(429, "SALES_RATE_LIMITED", ApiAction.RetryAfter)]
        [TestCase(409, "REWARD_NOT_READY", ApiAction.Reload)]
        [TestCase(409, "IDEMPOTENCY_KEY_REUSED", ApiAction.Fail)]
        [TestCase(409, "SELF_VISIT_NOT_ALLOWED", ApiAction.Fail)]
        [TestCase(400, "VALIDATION_ERROR", ApiAction.Fail)]
        [TestCase(413, null, ApiAction.Fail)]
        public void Classify_UsesStatusAndCodeOnly(int status, string code, ApiAction expected)
        {
            Assert.AreEqual(expected, ApiErrorPolicy.Classify(status, code));
        }

        [Test]
        public void Backoff_GrowsAndIsCapped()
        {
            Assert.AreEqual(1, RetryBackoff.DelaySeconds(1, 0.999), 0.01);
            Assert.AreEqual(4, RetryBackoff.DelaySeconds(3, 0.999), 0.01);
            Assert.AreEqual(RetryBackoff.CapSeconds, RetryBackoff.DelaySeconds(30, 0.9999), 0.01);
            Assert.AreEqual(0, RetryBackoff.DelaySeconds(2, 0));
            Assert.IsFalse(RetryBackoff.ShouldRetry(RetryBackoff.MaxAttempts));
        }

        [Test]
        public void RetryAfter_ParsesSecondsOrFallsBack()
        {
            Assert.AreEqual(7, RetryBackoff.ParseRetryAfter("7"));
            Assert.AreEqual(5, RetryBackoff.ParseRetryAfter(null));
            Assert.AreEqual(5, RetryBackoff.ParseRetryAfter("soon"));
        }
    }

    public class PendingOperationTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        [Test]
        public void SameRequest_ReusesKeyUntilCompleted()
        {
            int n = 0;
            var store = new PendingOperationStore(() => "key" + ++n);

            var a = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            var b = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            Assert.AreEqual(a.key, b.key);

            store.Complete(a.key);
            var c = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            Assert.AreNotEqual(a.key, c.key);
        }

        [Test]
        public void DifferentBody_GetsOwnKey()
        {
            var store = new PendingOperationStore();
            var a = store.Begin("POST", "/purchases", "{\"itemId\":\"x\"}", Now);
            var b = store.Begin("POST", "/purchases", "{\"itemId\":\"y\"}", Now);
            Assert.AreNotEqual(a.key, b.key);
            Assert.AreEqual(2, store.Items.Count);
        }

        [Test]
        public void SurvivesRestart()
        {
            var store = new PendingOperationStore();
            var a = store.Begin("POST", "/rewards/daily/claims", "{}", Now);

            var restored = new PendingOperationStore();
            restored.Restore(store.ToList());

            Assert.AreEqual(a.key, restored.Begin("POST", "/rewards/daily/claims", "{}", Now).key);
        }
    }
}
