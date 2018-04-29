﻿using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Gigya.Common.Contracts.Exceptions;
using Gigya.Microdot.Interfaces.Configuration;
using Gigya.Microdot.Interfaces.Logging;
using Gigya.Microdot.Interfaces.SystemWrappers;
using Gigya.Microdot.ServiceDiscovery.Config;
using Gigya.Microdot.SharedLogic.Monitor;
using Gigya.Microdot.SharedLogic.Rewrite;
using Metrics;

namespace Gigya.Microdot.ServiceDiscovery.Rewrite
{
    /// <summery>
    /// Monitors Consul using Health API and KeyValue API to find the current active version of a service,
    /// and provides a list of healthy nodes.
    /// </summery>
    public sealed class ConsulNodeMonitor : INodeMonitor
    {
        private int _disposed;
        private string _activeVersion;
        private Node[] _nodesOfAllVersions;
        private INode[] _nodes = new INode[0];
        private Task<ulong> _nodesInitTask;
        private Task<ulong> _versionInitTask;
        private ConsulResult<ServiceEntry[]> _lastNodesResult;
        private ConsulResult<KeyValueResponse[]> _lastVersionResult;

        private ILog Log { get; }
        private IServiceListMonitor ServiceListMonitor { get; }
        private ConsulClient ConsulClient { get; }
        private IDateTime DateTime { get; }
        private Func<ConsulConfig> GetConfig { get; }
        private AggregatingHealthStatus AggregatingHealthStatus { get; }
        private string DataCenter { get; }
        private Task NodesLoopTask { get; }
        private Task VersionLoopTask { get; }
        private CancellationTokenSource ShutdownToken { get; }
        private EnvironmentException LastError { get; set; }
        private DateTime LastErrorTime { get; set; }
        private string DeploymentIdentifier { get; }

        public ConsulNodeMonitor(
            string deploymentIdentifier, 
            ILog log, 
            IServiceListMonitor serviceListMonitor, 
            ConsulClient consulClient, 
            IEnvironmentVariableProvider environmentVariableProvider, 
            IDateTime dateTime,
            Func<ConsulConfig> getConfig, 
            Func<string, AggregatingHealthStatus> getAggregatingHealthStatus)
        {
            DeploymentIdentifier = deploymentIdentifier;
            Log = log;
            ServiceListMonitor = serviceListMonitor;
            ConsulClient = consulClient;
            DateTime = dateTime;
            GetConfig = getConfig;
            DataCenter = environmentVariableProvider.DataCenter;
            ShutdownToken = new CancellationTokenSource();
            AggregatingHealthStatus = getAggregatingHealthStatus("ConsulClient");
            AggregatingHealthStatus.RegisterCheck(DeploymentIdentifier, CheckHealth);

            VersionLoopTask = LoadVersionLoop();
            NodesLoopTask = LoadNodesLoop();
        }


        private string ActiveVersion
        {
            get => _activeVersion;
            set
            {
                _activeVersion = value;
                SetNodesByActiveVersion();
            }
        }

        private Node[] NodesOfAllVersions
        {
            get => _nodesOfAllVersions;
            set
            {
                _nodesOfAllVersions = value;
                SetNodesByActiveVersion();
            }
        }

        /// <inheritdoc />
        public INode[] Nodes
        {
            get
            {
                if (_disposed > 0)
                    throw new ObjectDisposedException(nameof(ConsulNodeMonitor));

                if (!IsDeployed)
                    throw Ex.ServiceNotDeployed(DataCenter, DeploymentIdentifier);

                if (_nodes.Length == 0 && LastError != null)
                {
                    if (LastError.StackTrace == null)
                        throw LastError;

                    ExceptionDispatchInfo.Capture(LastError).Throw();
                }

                return _nodes;
            }
        }

        /// <inheritdoc />
        public bool IsDeployed => ServiceListMonitor.Services.Contains(DeploymentIdentifier);

        /// <inheritdoc />
        public Task Init() => Task.WhenAll(_nodesInitTask, _versionInitTask);

        private async Task LoadVersionLoop()
        {
            try
            {
                _versionInitTask = LoadVersion(0);
                var modifyIndex = await _versionInitTask.ConfigureAwait(false);
                while (!ShutdownToken.IsCancellationRequested)
                {
                    modifyIndex = await LoadVersion(modifyIndex).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) when (ShutdownToken.IsCancellationRequested)
            {
                // Ignore exception during shutdown.
            }
        }

        private async Task LoadNodesLoop()
        {
            try
            {
                _nodesInitTask = LoadNodes(0);
                var modifyIndex = await _nodesInitTask.ConfigureAwait(false);
                while (!ShutdownToken.IsCancellationRequested)
                {
                    modifyIndex = await LoadNodes(modifyIndex).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) when (ShutdownToken.IsCancellationRequested)
            {
                // Ignore exception during shutdown.
            }
        }

        private async Task<ulong> LoadVersion(ulong modifyIndex)
        {
            ConsulConfig config = GetConfig();
           
            await DateTime.DelayUntil(LastErrorTime + config.ErrorRetryInterval, ShutdownToken.Token).ConfigureAwait(false);

            double maxSecondsToWaitForResponse = Math.Max(0, config.HttpTimeout.TotalSeconds - 2);
            string urlCommand = $"v1/kv/service/{DeploymentIdentifier}?dc={DataCenter}&index={modifyIndex}&wait={maxSecondsToWaitForResponse}s";
            _lastVersionResult = await ConsulClient.Call<KeyValueResponse[]>(urlCommand, ShutdownToken.Token).ConfigureAwait(false);

            if (_lastVersionResult.Error != null)
            {
                ErrorResult(_lastVersionResult);
            }
            else if (_lastVersionResult.IsDeployed == false || _lastVersionResult.Response == null)
            {
                LastErrorTime = DateTime.UtcNow;
                // This situation is ignored because other processes are responsible for indicating when a service is undeployed.
            }
            else
            {
                string version = _lastVersionResult.Response?.SingleOrDefault()?.TryDecodeValue()?.Version;

                if (version != null)
                {
                    ActiveVersion = version;
                    return _lastVersionResult.ModifyIndex ?? 0;
                }
            }
            
            return 0;
        }

        private async Task<ulong> LoadNodes(ulong modifyIndex)
        {
            ConsulConfig config = GetConfig();
          
            await DateTime.DelayUntil(LastErrorTime + config.ErrorRetryInterval, ShutdownToken.Token).ConfigureAwait(false);

            double maxSecondsToWaitForResponse = Math.Max(0, config.HttpTimeout.TotalSeconds - 2);
            string urlCommand = $"v1/health/service/{DeploymentIdentifier}?dc={DataCenter}&passing&index={modifyIndex}&wait={maxSecondsToWaitForResponse}s";
            _lastNodesResult = await ConsulClient.Call<ServiceEntry[]>(urlCommand, ShutdownToken.Token).ConfigureAwait(false);

            if (_lastNodesResult.Error != null)
            {
                ErrorResult(_lastNodesResult);
            }
            else if (_lastNodesResult.IsDeployed == false || _lastNodesResult.Response == null)
            {
                NodesOfAllVersions = new Node[0];
                LastErrorTime = DateTime.UtcNow;
            }
            else
            {
                NodesOfAllVersions = _lastNodesResult.Response.Select(n => n.ToNode()).ToArray();
                return _lastNodesResult.ModifyIndex ?? 0;
            }
            return 0;
        }

        private void SetNodesByActiveVersion()
        {
            if (ActiveVersion != null && NodesOfAllVersions != null)
            {
                _nodes = NodesOfAllVersions.Where(n => n.Version == ActiveVersion).ToArray();
                if (_nodes.Length == 0)
                    ErrorResult(_lastNodesResult, "No endpoints were specified in Consul for the requested service and service's active version.");
                else
                    LastError = null;
            }
        }

        private void ErrorResult<T>(ConsulResult<T> result, string message=null)
        {
            EnvironmentException error = result.Error ?? new EnvironmentException(message);

            if (error.InnerException is TaskCanceledException == false)
            {
                Log.Error("Error calling Consul", exception: result?.Error, unencryptedTags: new
                {
                    serviceName = DeploymentIdentifier,
                    consulAddress = result?.ConsulAddress,
                    commandPath = result?.CommandPath,
                    responseCode = result?.StatusCode,
                    content = result?.ResponseContent,
                    activeVersion = ActiveVersion,
                    lastVersionCommand = _lastVersionResult?.CommandPath,
                    lastVersionResponse = _lastVersionResult?.ResponseContent,
                    lastNodesCommand = _lastNodesResult?.CommandPath,
                    lastNodesResponse = _lastNodesResult?.ResponseContent,
                });
            }

            LastError = error;
            LastErrorTime = DateTime.UtcNow;
        }


        private HealthCheckResult CheckHealth()
        {
            if (!IsDeployed)
                HealthCheckResult.Healthy($"Not exists on Consul (key value store)");

            var error = _lastNodesResult?.Error ?? _lastVersionResult?.Error ?? LastError;
            if (error != null)
            {
                return HealthCheckResult.Unhealthy($"Consul error: " + error.Message);
            }

            string healthMessage;
            
            if (Nodes.Length < NodesOfAllVersions?.Length)
                healthMessage = $"{Nodes.Length} nodes matching to version {ActiveVersion} from total of {NodesOfAllVersions.Length} nodes";
            else
                healthMessage = $"{Nodes.Length} nodes";

            return HealthCheckResult.Healthy(healthMessage);
        }


        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposed) != 1)
                return;

            AggregatingHealthStatus.RemoveCheck(DeploymentIdentifier);
            ShutdownToken?.Cancel();
            NodesLoopTask.Wait(TimeSpan.FromSeconds(3));
            VersionLoopTask.Wait(TimeSpan.FromSeconds(3));
            ShutdownToken?.Dispose();
        }
    }
}

