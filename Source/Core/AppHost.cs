using AgentCommandEnvironment.Core.Controllers;
using AgentCommandEnvironment.Core.Interfaces;
using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Services;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core
{
    public static class AppHost
    {
        private static readonly Object SyncRoot = new();
        private static Boolean _isStarted;
        private static ILocalizationControllerService _localization = null!;
        private static GlobalContext _globalContext = null!;
        private static WorkspaceStateTrackerService _workspaceStateTracker = null!;
        private static SmartTaskSchedulerService _smartTaskScheduler = null!;
        private static HttpClient _httpClient = null!;
        private static JsonSerializerOptions _jsonSerializerOptions = null!;
        private static IUiDispatcherService? _dispatcherService;
        private static AssignmentController? _assignmentController;

        public static void Start()
        {
            if (_isStarted)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_isStarted)
                {
                    return;
                }

                _localization = new GetTextLocalizationController();
                _globalContext = new GlobalContext();
                _workspaceStateTracker = new WorkspaceStateTrackerService();
                _smartTaskScheduler = new SmartTaskSchedulerService();
                _httpClient = CreateHttpClient();
                _jsonSerializerOptions = CreateJsonSerializerOptions();
                EnsureAssignmentController();
                _isStarted = true;
            }
        }

        public static void Stop()
        {
            lock (SyncRoot)
            {
                if (!_isStarted)
                {
                    return;
                }

                if (_assignmentController != null)
                {
                    _assignmentController.Dispose();
                    _assignmentController = null;
                }

                _httpClient?.Dispose();
                _httpClient = null!;
                _isStarted = false;
            }
        }

        public static void ConfigureDispatcher(IUiDispatcherService dispatcherService)
        {
            if (dispatcherService == null)
            {
                throw new ArgumentNullException(nameof(dispatcherService));
            }

            lock (SyncRoot)
            {
                _dispatcherService = dispatcherService;
                if (!_isStarted)
                {
                    Start();
                }

                EnsureAssignmentController();
            }
        }

        public static ILocalizationControllerService Localization => _localization;
        public static GlobalContext GlobalContext => _globalContext;
        public static WorkspaceStateTrackerService WorkspaceStateTracker => _workspaceStateTracker;
        public static SmartTaskSchedulerService SmartTaskScheduler => _smartTaskScheduler;
        public static HttpClient HttpClient => _httpClient;
        public static JsonSerializerOptions JsonSerializerOptions => _jsonSerializerOptions;
        public static AssignmentController AssignmentController
        {
            get
            {
                lock (SyncRoot)
                {
                    EnsureAssignmentController();
                    if (_assignmentController == null)
                    {
                        throw new InvalidOperationException("AssignmentController is not initialized. Call ConfigureDispatcher first.");
                    }

                    return _assignmentController;
                }
            }
        }

        public static AssignmentController? TryGetAssignmentController()
        {
            lock (SyncRoot)
            {
                return _assignmentController;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler httpClientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            HttpClient createdHttpClient = new HttpClient(httpClientHandler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            return createdHttpClient;
        }

        private static JsonSerializerOptions CreateJsonSerializerOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return options;
        }

        private static void EnsureAssignmentController()
        {
            if (_assignmentController != null)
            {
                return;
            }

            if (_dispatcherService == null || _httpClient == null || _jsonSerializerOptions == null || _workspaceStateTracker == null || _globalContext == null || _smartTaskScheduler == null)
            {
                return;
            }

            _assignmentController = new AssignmentController(_dispatcherService, _httpClient, _jsonSerializerOptions, _workspaceStateTracker, _globalContext, _smartTaskScheduler);
        }
    }
}

