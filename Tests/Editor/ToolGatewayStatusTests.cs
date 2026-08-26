using System;
using System.Collections.Generic;
using System.Linq;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.ToolGateway;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class ToolGatewayStatusTests
    {
        private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp() => DotCraftAgentPresence.ResetForTests();

        [TearDown]
        public void TearDown() => DotCraftAgentPresence.ResetForTests();

        [Test]
        public void ClientsAreOrderedByActivityAndOrderIsStable()
        {
            var sessions = new List<McpClientSession>
            {
                Session("session-00000003", Now.AddSeconds(-30)),
                Session("session-00000001", Now.AddSeconds(-1)),
                Session("session-00000002", Now.AddSeconds(-1))
            };

            var first = Summary(sessions).Clients.Select(client => client.SessionId).ToArray();
            var second = Summary(Enumerable.Reverse(sessions).ToList())
                .Clients.Select(client => client.SessionId).ToArray();

            Assert.That(first[0], Is.EqualTo("session-00000001"));
            Assert.That(first[2], Is.EqualTo("session-00000003"));
            Assert.That(second, Is.EqualTo(first), "Ordering must not depend on input order.");
        }

        [Test]
        public void IndicatorReflectsPresenceAndErrorTakesPrecedence()
        {
            Assert.That(
                Summary(Array.Empty<McpClientSession>(), isRunning: false).Indicator,
                Is.EqualTo(ToolGatewayIndicatorState.Hidden));
            Assert.That(
                Summary(Array.Empty<McpClientSession>()).Indicator,
                Is.EqualTo(ToolGatewayIndicatorState.Idle),
                "Gateway running with no clients is the ordinary steady state, not an error.");
            Assert.That(
                Summary(new[] { Session("session-00000001", Now) }).Indicator,
                Is.EqualTo(ToolGatewayIndicatorState.Active));
            Assert.That(
                Summary(new[] { Session("session-00000001", Now) }, lastError: "boom").Indicator,
                Is.EqualTo(ToolGatewayIndicatorState.Warning));
        }

        [Test]
        public void IndicatorIsVisibleWhenOnlyTheAssistantIsConnected()
        {
            var summary = ToolGatewayStatusSummary.FromState(
                isRunning: false,
                lastError: null,
                agent: ConnectedAgent(),
                sessions: Array.Empty<McpClientSession>(),
                nowUtc: Now);

            Assert.That(summary.IsVisible, Is.True);
            Assert.That(summary.Indicator, Is.EqualTo(ToolGatewayIndicatorState.Active));
        }

        [Test]
        public void HeaderSubtitleSummarisesWhatIsAttached()
        {
            Assert.That(
                Summary(Array.Empty<McpClientSession>(), isRunning: false).HeaderSubtitle,
                Is.EqualTo("Gateway is stopped."));
            Assert.That(
                Summary(Array.Empty<McpClientSession>()).HeaderSubtitle,
                Is.EqualTo("Gateway running · waiting for clients"));

            var withAgent = ToolGatewayStatusSummary.FromState(
                true,
                null,
                ConnectedAgent(),
                new[] { Session("session-00000001", Now), Session("session-00000002", Now) },
                Now);
            Assert.That(withAgent.HeaderSubtitle, Is.EqualTo("Gateway running · 1 agent, 2 clients"));
        }

        [Test]
        public void ClientRowShowsNameVersionAndActivity()
        {
            var row = Summary(new[] { Session("session-00000001", Now.AddSeconds(-30)) }).Clients.Single();

            Assert.That(row.Name, Is.EqualTo("Claude Code 2.0.31"));
            Assert.That(row.ActivityText, Is.EqualTo("30s"));
        }

        [Test]
        public void StructureKeyTracksRowsButNotTimestamps()
        {
            var initial = Summary(new[] { Session("session-00000001", Now) });

            Assert.That(
                Summary(new[] { Session("session-00000001", Now.AddSeconds(-5)) }).StructureKey,
                Is.EqualTo(initial.StructureKey),
                "Activity changes must not rebuild the dropdown tree.");
            Assert.That(
                Summary(new[] { Session("session-00000001", Now), Session("session-00000002", Now) }).StructureKey,
                Is.Not.EqualTo(initial.StructureKey));
            Assert.That(
                Summary(Array.Empty<McpClientSession>()).StructureKey,
                Is.Not.EqualTo(initial.StructureKey));
        }

        [Test]
        public void EstimateHeightIsClampedAndGrowsWithClientCount()
        {
            var empty = ToolGatewayStatusDropdown.EstimateHeight(Summary(Array.Empty<McpClientSession>()));
            var few = ToolGatewayStatusDropdown.EstimateHeight(Summary(Sessions(3)));
            var many = ToolGatewayStatusDropdown.EstimateHeight(Summary(Sessions(40)));

            Assert.That(few, Is.GreaterThan(empty));
            Assert.That(many, Is.LessThanOrEqualTo(460f), "Overflow scrolls rather than running off-screen.");
        }

        [Test]
        public void RelativeTimeCoversItsBoundariesAndClampsClockSkew()
        {
            Assert.That(ToolGatewayRelativeTime.Since(Now, Now), Is.EqualTo("now"));
            Assert.That(ToolGatewayRelativeTime.Since(Now.AddSeconds(-5), Now), Is.EqualTo("5s"));
            Assert.That(ToolGatewayRelativeTime.Since(Now.AddSeconds(-60), Now), Is.EqualTo("1m"));
            Assert.That(ToolGatewayRelativeTime.Since(Now.AddHours(-1), Now), Is.EqualTo("1h"));

            // A gateway can report a timestamp slightly in the future.
            Assert.That(ToolGatewayRelativeTime.Since(Now.AddSeconds(30), Now), Is.EqualTo("now"));
            Assert.That(ToolGatewayRelativeTime.DurationSince(Now.AddSeconds(30), Now), Is.EqualTo("0s"));
        }

        [Test]
        public void PresenceRaisesChangedOncePerDistinctSnapshot()
        {
            var owner = new object();
            var raised = 0;
            void Handler() => raised++;

            DotCraftAgentPresence.Changed += Handler;
            try
            {
                DotCraftAgentPresence.Publish(owner, AgentPresenceSnapshot.WindowOpen());
                DotCraftAgentPresence.Publish(owner, AgentPresenceSnapshot.WindowOpen());

                Assert.That(raised, Is.EqualTo(1), "An identical snapshot must not raise a change.");
                Assert.That(DotCraftAgentPresence.Current.IsWindowOpen, Is.True);
            }
            finally
            {
                DotCraftAgentPresence.Changed -= Handler;
            }
        }

        [Test]
        public void AConnectedOwnerIsNotClobberedByAnotherWindow()
        {
            var connected = new object();
            var other = new object();

            DotCraftAgentPresence.Publish(connected, ConnectedAgent());
            DotCraftAgentPresence.Publish(other, AgentPresenceSnapshot.WindowOpen());
            Assert.That(DotCraftAgentPresence.Current.IsConnected, Is.True);

            DotCraftAgentPresence.Clear(other);
            Assert.That(DotCraftAgentPresence.Current.IsConnected, Is.True);

            DotCraftAgentPresence.Clear(connected);
            Assert.That(DotCraftAgentPresence.Current, Is.EqualTo(AgentPresenceSnapshot.Absent));
        }

        [Test]
        public void AgentNameFallsBackFromTitleToNameToDefault()
        {
            Assert.That(
                AgentPresenceSnapshot.ResolveName(new AgentInfo { Title = "Claude Code", Name = "claude" }),
                Is.EqualTo("Claude Code"));
            Assert.That(AgentPresenceSnapshot.ResolveName(new AgentInfo { Name = "claude" }), Is.EqualTo("claude"));
            Assert.That(AgentPresenceSnapshot.ResolveName(null), Is.EqualTo(AgentPresenceSnapshot.DefaultAgentName));
        }

        private static ToolGatewayStatusSummary Summary(
            IReadOnlyList<McpClientSession> sessions,
            bool isRunning = true,
            string lastError = null)
        {
            return ToolGatewayStatusSummary.FromState(
                isRunning,
                lastError,
                AgentPresenceSnapshot.Absent,
                sessions,
                Now);
        }

        private static McpClientSession[] Sessions(int count) =>
            Enumerable.Range(0, count)
                .Select(index => Session($"session-{index:D8}", Now.AddSeconds(-index)))
                .ToArray();

        private static McpClientSession Session(string sessionId, DateTime lastSeen) => new()
        {
            SessionId = sessionId,
            ClientTitle = "Claude Code",
            ClientVersion = "2.0.31",
            ConnectedAtUtc = Now.AddMinutes(-5),
            LastSeenUtc = lastSeen
        };

        private static AgentPresenceSnapshot ConnectedAgent() =>
            AgentPresenceSnapshot.Connected("Claude Code", "1.0.0", Now);
    }
}
