using System;
using System.Collections.Generic;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.Settings;
using UnityEngine;

namespace DotCraft.Editor.ToolGateway
{
    internal sealed class UnityToolGatewayRuntime
    {
        private static readonly Lazy<UnityToolGatewayRuntime> LazyInstance =
            new(() => new UnityToolGatewayRuntime());
        private readonly object _gate = new();
        private readonly UnityToolGatewayState _state;
        private UnityToolGatewayServer _server;
        private string _token;
        private string _lastError;

        private UnityToolGatewayRuntime()
            : this(new UnityToolGatewayState())
        {
        }

        internal UnityToolGatewayRuntime(UnityToolGatewayState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            DotCraftSettings.Saved += OnSettingsSaved;
        }

        public static UnityToolGatewayRuntime Instance => LazyInstance.Value;

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                    return _server?.IsRunning == true;
            }
        }

        public string LastError
        {
            get
            {
                lock (_gate)
                    return _lastError ?? _server?.LastError;
            }
        }

        public string ManifestRevision => _state.CurrentManifest?.Revision ?? string.Empty;

        public int ToolCount => _state.CurrentManifest?.Tools?.Count ?? 0;

        /// <summary>MCP clients currently attached, most recent activity first.</summary>
        public IReadOnlyList<McpClientSession> ClientSessions =>
            McpClientSessionRegistry.Instance.Snapshot(DateTime.UtcNow);

        internal event Action StatusChanged;

        internal void NotifySessionsChanged() => NotifyStatusChanged();

        public void ApplySettings()
        {
            if (DotCraftSettings.Instance.EnableToolGateway)
                Start();
            else
                Shutdown(notify: true, clearSessions: true);
        }

        public void Restart()
        {
            Shutdown(notify: false, clearSessions: false);
            Start();
        }

        private void RefreshManifest()
        {
            try
            {
                _state.RefreshManifest();
                NotifyStatusChanged();
            }
            catch (Exception ex)
            {
                RecordError(ex.Message);
            }
        }

        /// <summary>Domain reload and quit. Sessions are kept: gateways stay connected across a reload.</summary>
        public void Shutdown() => Shutdown(notify: true, clearSessions: false);

        private void Start()
        {
            lock (_gate)
            {
                if (_server?.IsRunning == true)
                {
                    _state.RefreshManifest();
                    NotifyStatusChanged();
                    return;
                }

                try
                {
                    _state.RefreshManifest();
                    _token = UnityToolGatewayState.CreateToken();
                    var handler = new UnityToolGatewayHandler(_token);
                    _server = new UnityToolGatewayServer(handler);
                    _server.Start();
                    _state.PublishDiscovery(_server.Endpoint, _token);
                    _lastError = null;
                    if (DotCraftSettings.Instance.VerboseLogging)
                        Debug.Log($"[DotCraft] Unity Tool Gateway started on loopback port {_server.Port}.");
                }
                catch (Exception ex)
                {
                    _server?.Dispose();
                    _server = null;
                    _state.RemoveDiscovery(_token);
                    _lastError = ex.Message;
                    Debug.LogError($"[DotCraft] Unity Tool Gateway failed to start: {ex.Message}");
                }
            }

            NotifyStatusChanged();
        }

        private void Shutdown(bool notify, bool clearSessions)
        {
            lock (_gate)
            {
                _server?.Dispose();
                _server = null;
                _state.RemoveDiscovery(_token);
                _token = null;
            }

            if (clearSessions)
                McpClientSessionRegistry.Instance.Clear();

            if (notify)
                NotifyStatusChanged();
        }

        private void OnSettingsSaved()
        {
            if (IsRunning)
                RefreshManifest();
        }

        private void RecordError(string message)
        {
            lock (_gate)
                _lastError = message;
            NotifyStatusChanged();
        }

        private void NotifyStatusChanged()
        {
            MainThreadDispatcher.RunOrEnqueue(() => StatusChanged?.Invoke());
        }
    }
}
