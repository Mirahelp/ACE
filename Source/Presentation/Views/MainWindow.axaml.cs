using AgentCommandEnvironment.Core;
using AgentCommandEnvironment.Core.Constants;
using AgentCommandEnvironment.Core.Controllers;
using AgentCommandEnvironment.Core.Events;
using AgentCommandEnvironment.Core.Interfaces;
using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;
using AgentCommandEnvironment.Core.Services;
using AgentCommandEnvironment.Presentation.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System;
using System.Linq;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Presentation.Views
{
    public sealed partial class MainWindow : Window
    {

        private readonly AssignmentController assignmentController;
        private readonly AssignmentLogService assignmentLogService;
        private readonly CommandExecutionService commandExecutionService;
        private readonly HttpClient httpClient;
        private readonly JsonSerializerOptions jsonSerializerOptions;
        private readonly ChatCompletionService chatCompletionService;
        private readonly ObservableCollection<SmartTaskExecutionContext> assignmentTaskItems;
        private readonly ObservableCollection<WorkspaceFileItem> workspaceFileItems;
        private readonly ObservableCollection<SmartTask> aceRootTasks;
        private readonly ObservableCollection<SemanticFactRecord> aceFacts;
        private readonly ObservableCollection<PolicyDecisionItem> policyDecisionItems;
        private readonly ObservableCollection<SuccessHeuristicItem> assignmentSuccessHeuristics;
        private readonly Dictionary<SmartTaskExecutionContext, TaskMonitorWindow> taskMonitorWindows;
        private readonly Dictionary<String, String?> completedTaskContextById;
        private readonly HashSet<String> missingDependencyWarnings;
        private readonly WorkspaceStateTrackerService workspaceStateTracker;
        private readonly AssignmentRuntimeService assignmentRuntimeService;

        private SmartTaskSchedulerService? activeSmartTaskScheduler;
        private SmartTask? assignmentRootSmartTask;
        private GlobalContext globalContext = null!;
        private String? selectedWorkspacePath;
        private Boolean automationHandsFreeMode;
        private Int32 maxCommandRetryAttempts;
        private Int32 maxRepairAttemptsPerTask;
        private Boolean hasShownAssignmentFailureDialog;
        private Boolean allowNewSoftwareInstallation;
        private Boolean allowNetworkOperations;
        private Boolean allowSystemConfigurationChanges;
        private Boolean showQuickStartOverlay;
        private Boolean hasQuickStartChecklistBeenDismissed;
        private Boolean isAssignmentLaunchPending;
        private Boolean hasShownUsageLiabilityWarning;
        private PolicyRiskToleranceOptions PolicyRiskToleranceOptions;
        private Boolean supervisorTabEnabled;
        private Boolean resultTabEnabled;
        private SuccessHeuristicEvaluationStatusOptions assignmentResultStatus = SuccessHeuristicEvaluationStatusOptions.Pending;
        private String assignmentResultHeadline = String.Empty;
        private String assignmentResultDetail = String.Empty;
        private const String DarkThemeLogoUri = "avares://Ace/Assets/Images/Mirahelp_light_logo.png";
        private const String LightThemeLogoUri = "avares://Ace/Assets/Images/Mirahelp_dark_logo.png";


        public ICommand ViewSmartTaskDetailsCommand { get; } = null!;
        public ICommand MonitorSmartTaskExecutionCommand { get; } = null!;

        private String? CurrentOpenAiApiKey => assignmentController.OpenAiApiKey;
        private String? CurrentOpenAiModelId => assignmentController.SelectedOpenAiModelId;
        private Boolean IsOpenAiConfigured => assignmentController.IsOpenAiConfigured;
        private Boolean IsLoadingOpenAiModels => assignmentController.IsLoadingModels;
        private Boolean IsAssignmentRunning => assignmentController.IsAssignmentRunning;
        private Boolean IsAssignmentPaused => assignmentController.IsAssignmentPaused;
        private String AssignmentStatusText => assignmentController.AssignmentStatusText;
        private String AssignmentAnswerOutputText => assignmentController.AssignmentAnswerOutputText;
        private String AssignmentRawOutputText => assignmentController.AssignmentRawOutputText;
        private String CommandsSummaryOutputText => assignmentController.CommandsSummaryOutputText;
        private String? AssignmentFailureReason => assignmentController.AssignmentFailureReason;

        private const Int32 RecursivePlannerMaxDepth = 4;
        private const Int32 RecursivePlannerMaxRequests = 20;
        private const Int32 AttemptTranscriptSectionLimit = 6000;
        private const Int32 DefaultRepairAgentRetries = 5;

        private Thumb? rightPaneSplitterThumb;

        public MainWindow()
        {
            InitializeComponent();

            ConfigureRightPaneSplitter();
            

            ViewSmartTaskDetailsCommand = new RelayCommand<SmartTask?>(task =>
            {
                if (task != null)
                {
                    ShowSmartTaskWindow(task);
                }
            });

            MonitorSmartTaskExecutionCommand = new RelayCommand<SmartTask?>(task =>
            {
                SmartTaskExecutionContext? boundTask = task?.BoundAssignmentTask;
                if (boundTask != null)
                {
                    ShowTaskMonitorWindow(boundTask);
                }
            });

            Log("MainWindow runtime type: " + GetType().AssemblyQualifiedName + ", base: " + GetType().BaseType?.FullName);

            assignmentController = AppHost.AssignmentController;
            assignmentController.StateChanged += OnAssignmentControllerStateChanged;
            assignmentController.SystemLogEntryAdded += OnAssignmentControllerSystemLog;
            assignmentController.UsageChanged += OnAssignmentControllerUsageChanged;
            globalContext = assignmentController.GlobalContext;
            IUiDispatcherService dispatcherService = new AvaloniaDispatcherService();
            assignmentLogService = new AssignmentLogService(assignmentController, dispatcherService);
            commandExecutionService = new CommandExecutionService(assignmentLogService);

            httpClient = AppHost.HttpClient;
            jsonSerializerOptions = AppHost.JsonSerializerOptions;
            chatCompletionService = new ChatCompletionService(assignmentController, httpClient, jsonSerializerOptions, assignmentLogService);
            assignmentTaskItems = assignmentController.AssignmentTasks;
            ApplyRepairRetrySettingsToAllTaskContexts();
            workspaceFileItems = assignmentController.WorkspaceFiles;
            aceRootTasks = assignmentController.RootTasks;
            aceFacts = assignmentController.Facts;
            policyDecisionItems = assignmentController.PolicyDecisions;
            taskMonitorWindows = new Dictionary<SmartTaskExecutionContext, TaskMonitorWindow>();
            completedTaskContextById = new Dictionary<String, String?>(StringComparer.OrdinalIgnoreCase);
            missingDependencyWarnings = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            assignmentSuccessHeuristics = assignmentController.SuccessHeuristics;
            workspaceStateTracker = AppHost.WorkspaceStateTracker;
            ICommandApprovalService commandApprovalService = new AvaloniaCommandApprovalService(this);
            assignmentRuntimeService = new AssignmentRuntimeService(
                assignmentController,
                httpClient,
                jsonSerializerOptions,
                workspaceStateTracker,
                globalContext,
                AppHost.SmartTaskScheduler,
                dispatcherService,
                commandApprovalService,
                assignmentLogService,
                chatCompletionService);
            automationHandsFreeMode = true;
            maxCommandRetryAttempts = 5;
            maxRepairAttemptsPerTask = DefaultRepairAgentRetries;
            hasShownAssignmentFailureDialog = false;
            hasShownUsageLiabilityWarning = false;
            assignmentController.ResetSystemLog();
            allowNewSoftwareInstallation = true;
            allowNetworkOperations = true;
            allowSystemConfigurationChanges = true;
            showQuickStartOverlay = true;
            hasQuickStartChecklistBeenDismissed = false;
            PolicyRiskToleranceOptions = PolicyRiskToleranceOptions.UpToMedium;

            globalContext.SecurityProfile = new SecurityProfile
            {
                AllowInstall = allowNewSoftwareInstallation,
                AllowNetwork = allowNetworkOperations,
                AllowSystemConfiguration = allowSystemConfigurationChanges,
                PolicyRiskToleranceOptions = PolicyRiskToleranceOptions
            };

            activeSmartTaskScheduler = AppHost.SmartTaskScheduler;

            assignmentController.ClearAssignmentFailureReason();
            assignmentController.ResetUsageMetrics();
            assignmentController.ResetBudgetTracking();

            if (WorkspaceFilesListView != null)
            {
                WorkspaceFilesListView.ItemsSource = workspaceFileItems;
            }
            if (SupervisorTableViewControl != null)
            {
                SupervisorTableViewControl.AttachTasks(aceRootTasks);
                SupervisorTableViewControl.SmartTaskDetailsRequested += OnSupervisorTaskDetailsRequested;
            }
            if (AceFactsListView != null)
            {
                AceFactsListView.ItemsSource = aceFacts;
            }
            if (PolicyInterceptorListView != null)
            {
                PolicyInterceptorListView.ItemsSource = policyDecisionItems;
            }
            if (SuccessHeuristicsListView != null)
            {
                SuccessHeuristicsListView.ItemsSource = assignmentSuccessHeuristics;
            }
            if (ResultHeuristicsListView != null)
            {
                ResultHeuristicsListView.ItemsSource = assignmentSuccessHeuristics;
            }

            Opened += OnMainWindowOpened;

            if (IncludeWorkspaceContextCheckBox != null)
            {
                IncludeWorkspaceContextCheckBox.IsCheckedChanged += OnIncludeWorkspaceContextChanged;
            }
            if (AllowNewSoftwareInstallationsCheckBox != null)
            {
                AllowNewSoftwareInstallationsCheckBox.IsCheckedChanged += OnConstraintCheckboxChanged;
            }
            if (AllowNetworkAccessCheckBox != null)
            {
                AllowNetworkAccessCheckBox.IsCheckedChanged += OnConstraintCheckboxChanged;
            }
            if (AllowSystemConfigurationChangesCheckBox != null)
            {
                AllowSystemConfigurationChangesCheckBox.IsCheckedChanged += OnConstraintCheckboxChanged;
            }
            if (AssignmentPromptTextBox != null)
            {
                AssignmentPromptTextBox.TextChanged += OnAssignmentPromptTextChanged;
            }
            if (AssignmentTitleTextBox != null)
            {
                AssignmentTitleTextBox.TextChanged += OnAssignmentPromptTextChanged;
            }

            if (RecursiveExitBiasSlider != null)
            {
                RecursiveExitBiasSlider.Value = assignmentController.RecursionExitBiasBase * 100.0;
                RecursiveExitBiasSlider.ValueChanged += OnRecursiveExitBiasSliderValueChanged;
            }
            if (RecursiveExitBiasIncrementSlider != null)
            {
                RecursiveExitBiasIncrementSlider.Value = assignmentController.RecursionExitBiasIncrement * 100.0;
                RecursiveExitBiasIncrementSlider.ValueChanged += OnRecursiveExitBiasIncrementSliderValueChanged;
            }
            UpdateRecursiveExitBiasText();

            if (PolicyRiskToleranceSlider != null)
            {
                PolicyRiskToleranceSlider.Value = MapToleranceToSlider(PolicyRiskToleranceOptions);
                PolicyRiskToleranceSlider.ValueChanged += OnPolicyRiskToleranceSliderValueChanged;
            }
            UpdatePolicyRiskToleranceOptionsLabel(PolicyRiskToleranceOptions);

            if (CommandRetryAttemptsSlider != null)
            {
                CommandRetryAttemptsSlider.Value = maxCommandRetryAttempts;
                CommandRetryAttemptsSlider.ValueChanged += OnCommandRetryAttemptsSliderValueChanged;
            }
            UpdateCommandRetryAttemptsLabel();

            UpdateUiState();
            supervisorTabEnabled = false;
            UpdateSupervisorTabState();
            

            InitializeThemeHeader();

        }


        private static void Log(String message)
        {
            String logPath = Path.Combine(AppContext.BaseDirectory, "ui.log");
            File.AppendAllText(logPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            assignmentController.StateChanged -= OnAssignmentControllerStateChanged;
            assignmentController.SystemLogEntryAdded -= OnAssignmentControllerSystemLog;
            assignmentController.UsageChanged -= OnAssignmentControllerUsageChanged;

            assignmentController.CancelAssignmentRun();
            assignmentController.ClearAssignmentCancellationToken();

            foreach (KeyValuePair<SmartTaskExecutionContext, TaskMonitorWindow> pair in taskMonitorWindows)
            {
                TaskMonitorWindow monitorWindow = pair.Value;
                if (monitorWindow.IsVisible)
                {
                    monitorWindow.Close();
                }
            }

            if (ThemeToggleButton != null)
            {
                ThemeToggleButton.Click -= OnThemeToggleButtonClick;
            }

            if (Application.Current != null)
            {
                Application.Current.ActualThemeVariantChanged -= OnApplicationThemeChanged;
            }

            if (SupervisorTableViewControl != null)
            {
                SupervisorTableViewControl.SmartTaskDetailsRequested -= OnSupervisorTaskDetailsRequested;
                SupervisorTableViewControl.AttachTasks(null);
            }

            taskMonitorWindows.Clear();
            commandExecutionService.StopAllBackgroundCommands("Application closing");
        }

        private void InitializeThemeHeader()
        {
            if (ThemeToggleButton != null)
            {
                ThemeToggleButton.Click += OnThemeToggleButtonClick;
            }

            Application? application = Application.Current;
            if (application != null)
            {
                application.ActualThemeVariantChanged += OnApplicationThemeChanged;
            }

            UpdateThemeToggleButton();
        }

        private void InvokeOnUiThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(_ => action(), DispatcherPriority.Background);
            }
        }

        private static Boolean ContainsAny(String source, String[] patterns)
        {
            if (String.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            String normalized = source.ToLowerInvariant();
            for (Int32 index = 0; index < patterns.Length; index++)
            {
                if (normalized.Contains(patterns[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnThemeToggleButtonClick(Object? sender, RoutedEventArgs e)
        {
            ThemeVariant current = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
            ThemeVariant next = current == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = next;
            }
            UpdateThemeToggleButton();
        }

        private void OnApplicationThemeChanged(Object? sender, EventArgs e)
        {
            UpdateThemeToggleButton();
        }

        private void UpdateThemeToggleButton()
        {
            if (ThemeToggleButton != null)
            {
                ThemeVariant current = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
                ThemeToggleButton.Content = current == ThemeVariant.Dark
                    ? UiText(UiCatalogKeys.ButtonThemeDark)
                    : UiText(UiCatalogKeys.ButtonThemeLight);
            }

            ApplyLogoForTheme();
        }

        private void ApplyLogoForTheme()
        {
            if (AppLogoImage == null)
            {
                return;
            }

            ThemeVariant current = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
            String uri = current == ThemeVariant.Dark ? DarkThemeLogoUri : LightThemeLogoUri;

            try
            {
                using Stream assetStream = AssetLoader.Open(new Uri(uri));
                AppLogoImage.Source = new Bitmap(assetStream);
            }
            catch (Exception ex)
            {
                Log("Failed to load themed logo: " + ex.Message);
            }
        }

        private static String UiText(String key, params Object[] args)
        {
            ILocalizationControllerService? localization = AppHost.Localization;
            String text = localization != null ? localization.GetText(key) : key;
            if (!String.IsNullOrWhiteSpace(text) && args != null && args.Length > 0)
            {
                try
                {
                    text = String.Format(CultureInfo.CurrentCulture, text, args);
                }
                catch (FormatException)
                {
                    
                }
            }
            return text ?? String.Empty;
        }

        private void UpdateUiState()
        {
            InvokeOnUiThread(() =>
            {
                Boolean isWorkspaceSelected = !String.IsNullOrWhiteSpace(selectedWorkspacePath);
                Boolean isOpenAiConfigured = IsOpenAiConfigured;
                Boolean isLoadingModels = IsLoadingOpenAiModels;
                Boolean isAssignmentRunning = IsAssignmentRunning;
                String? selectedModelId = CurrentOpenAiModelId;
                String? configuredApiKey = CurrentOpenAiApiKey;

                TextBlock? workspaceStatusTextBlock = WorkspaceStatusTextBox;
                if (workspaceStatusTextBlock != null)
                {
                    workspaceStatusTextBlock.Text = !isWorkspaceSelected
                        ? UiText(UiCatalogKeys.PromptWorkspaceSelection)
                        : UiText(UiCatalogKeys.StatusWorkspaceReady, selectedWorkspacePath ?? String.Empty);
                }

                TextBlock? openAiStatusTextBlock = OpenAiStatusTextBlock;
                ComboBox? modelComboBox = ModelComboBox;
                if (openAiStatusTextBlock != null)
                {
                    if (!isOpenAiConfigured)
                    {
                        openAiStatusTextBlock.Text = UiText(UiCatalogKeys.StatusOpenAiKeyMissing);
                    }
                    else if (isLoadingModels)
                    {
                        openAiStatusTextBlock.Text = UiText(UiCatalogKeys.StatusOpenAiLoading);
                    }
                    else if (!String.IsNullOrWhiteSpace(selectedModelId))
                    {
                        openAiStatusTextBlock.Text = UiText(UiCatalogKeys.StatusOpenAiUsingModel, selectedModelId);
                    }
                    else if (modelComboBox != null && modelComboBox.Items.Count > 0)
                    {
                        openAiStatusTextBlock.Text = UiText(UiCatalogKeys.PromptSelectModel);
                    }
                    else
                    {
                        openAiStatusTextBlock.Text = UiText(UiCatalogKeys.StatusNoModelsLoaded);
                    }
                }

                Boolean isAssignmentActiveOrLaunching = isAssignmentRunning || isAssignmentLaunchPending;
                Boolean canStartAssignment =
                    isWorkspaceSelected &&
                    isOpenAiConfigured &&
                    !isLoadingModels &&
                    !isAssignmentActiveOrLaunching &&
                    !String.IsNullOrWhiteSpace(selectedModelId);

                Button? startAssignmentButton = StartAssignmentButton;
                if (startAssignmentButton != null)
                {
                    startAssignmentButton.IsEnabled = canStartAssignment;
                    startAssignmentButton.Opacity = canStartAssignment ? 1.0 : 0.6;
                }

                if (CancelAssignmentButton != null)
                {
                    CancelAssignmentButton.IsEnabled = isAssignmentActiveOrLaunching;
                }
                if (PauseAssignmentButton != null)
                {
                    PauseAssignmentButton.IsEnabled = isAssignmentRunning;
                }

                if (modelComboBox != null)
                {
                    modelComboBox.IsEnabled = isOpenAiConfigured && !isLoadingModels;
                }
                if (OpenAiApiKeyPasswordBox != null)
                {
                    OpenAiApiKeyPasswordBox.IsEnabled = !isAssignmentRunning;
                    String currentPassword = OpenAiApiKeyPasswordBox.Text ?? String.Empty;
                    String desiredPassword = configuredApiKey ?? String.Empty;
                    if (!String.Equals(currentPassword, desiredPassword, StringComparison.Ordinal))
                    {
                        OpenAiApiKeyPasswordBox.Text = desiredPassword;
                    }
                }
                if (LoadApiKeyButton != null)
                {
                    LoadApiKeyButton.IsEnabled = !isAssignmentRunning;
                }

                if (AssignmentTitleTextBox != null)
                {
                    AssignmentTitleTextBox.IsEnabled = !isAssignmentRunning;
                }
                if (AssignmentPromptTextBox != null)
                {
                    AssignmentPromptTextBox.IsEnabled = !isAssignmentRunning;
                }

                if (SelectWorkspaceButton != null)
                {
                    SelectWorkspaceButton.IsEnabled = !isAssignmentRunning;
                }

                if (AssignmentStatusTextBlock != null)
                {
                    AssignmentStatusTextBlock.Text = AssignmentStatusText;
                }
            });

            UpdateQuickStartOverlayStatus();
        }

        public void RefreshAceBlackboardViews()
        {
            InvokeOnUiThread(() =>
            {
                aceFacts.Clear();

                IReadOnlyList<SemanticFactRecord> facts = globalContext.Facts;
                for (Int32 index = facts.Count - 1; index >= 0; index--)
                {
                    aceFacts.Add(facts[index]);
                }
            });
        }
        private void ResetAssignmentStateForNewRun()
        {
            supervisorTabEnabled = false;
            UpdateSupervisorTabState();
            ResetResultEvaluationState();
            assignmentController.ResetBudgetTracking();
            assignmentController.ClearAssignmentFailureReason();
            assignmentController.ResetUsageMetrics();
            assignmentController.ResetTaskIdentityTracking();
            hasShownAssignmentFailureDialog = false;
            assignmentController.ResetAssignmentOutputs();
            assignmentRootSmartTask = null;

            assignmentTaskItems.Clear();
            completedTaskContextById.Clear();
            missingDependencyWarnings.Clear();
            policyDecisionItems.Clear();
            aceRootTasks.Clear();
            assignmentSuccessHeuristics.Clear();
            workspaceStateTracker.Reset(selectedWorkspacePath);

            if (taskMonitorWindows.Count > 0)
            {
                List<TaskMonitorWindow> openWindows = new List<TaskMonitorWindow>(taskMonitorWindows.Values);
                foreach (TaskMonitorWindow window in openWindows)
                {
                    window.Close();
                }
                taskMonitorWindows.Clear();
            }

            globalContext.ResetState();
            RefreshAceBlackboardViews();

            assignmentController.ResetSystemLog();
            UpdateUsageUi();
        }

        private void UpdateSupervisorTabState()
        {
            InvokeOnUiThread(() =>
            {
                if (SupervisorTabItem == null)
                {
                    return;
                }

                SupervisorTabItem.IsEnabled = supervisorTabEnabled;
                SupervisorTabItem.Opacity = supervisorTabEnabled ? 1.0 : 0.6;
            });
        }

        private void OnSupervisorTaskDetailsRequested(SmartTask task)
        {
            if (task == null)
            {
                return;
            }

            ShowSmartTaskWindow(task);
        }

        private void OnAssignmentControllerStateChanged(Object? sender, EventArgs e)
        {
            InvokeOnUiThread(UpdateUiState);
        }

        private void OnAssignmentControllerSystemLog(Object? sender, SystemLogEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(e.Message))
            {
                return;
            }

            Debug.WriteLine("[SystemLog] " + e.Message);
        }

        private void UpdateResultTabState()
        {
            InvokeOnUiThread(() =>
            {
                TabItem? resultTabItem = ResultTabItem;
                if (resultTabItem == null)
                {
                    return;
                }

                resultTabItem.IsEnabled = resultTabEnabled;
                resultTabItem.Opacity = resultTabEnabled ? 1.0 : 0.6;
            });
        }

        private void UpdateResultSummaryUi()
        {
            InvokeOnUiThread(() =>
            {
                TextBlock? glyphBlock = ResultStatusGlyphTextBlock;
                TextBlock? headlineBlock = ResultStatusHeadlineTextBlock;
                TextBlock? detailBlock = ResultStatusDetailTextBlock;

                if (glyphBlock != null)
                {
                    glyphBlock.Text = GetEvaluationGlyph(assignmentResultStatus);
                    glyphBlock.Foreground = GetEvaluationBrush(assignmentResultStatus);
                }

                if (headlineBlock != null)
                {
                    headlineBlock.Text = assignmentResultHeadline;
                }

                if (detailBlock != null)
                {
                    detailBlock.Text = assignmentResultDetail;
                }
            });
        }

        private static String GetEvaluationGlyph(SuccessHeuristicEvaluationStatusOptions status)
        {
            return status switch
            {
                SuccessHeuristicEvaluationStatusOptions.Passed => "\u2714",
                SuccessHeuristicEvaluationStatusOptions.Failed => "\u2716",
                _ => "\u25CB"
            };
        }

        private static Brush GetEvaluationBrush(SuccessHeuristicEvaluationStatusOptions status)
        {
            Color color = status switch
            {
                SuccessHeuristicEvaluationStatusOptions.Passed => Color.FromRgb(0x00, 0xB2, 0x94),
                SuccessHeuristicEvaluationStatusOptions.Failed => Color.FromRgb(0xD8, 0x3C, 0x3C),
                _ => Color.FromRgb(0x66, 0x66, 0x66)
            };
            return new SolidColorBrush(color);
        }

        private void SwitchToResultTab()
        {
            InvokeOnUiThread(() =>
            {
                TabItem? resultTabItem = ResultTabItem;
                if (!resultTabEnabled || AssignmentTabControl == null || resultTabItem == null)
                {
                    return;
                }

                AssignmentTabControl.SelectedItem = resultTabItem;
            });
        }

        private void ResetResultEvaluationState()
        {
            resultTabEnabled = false;
            assignmentResultStatus = SuccessHeuristicEvaluationStatusOptions.Pending;
            assignmentResultHeadline = "Awaiting evaluation";
            assignmentResultDetail = "Success heuristics will be evaluated when the assignment finishes.";
            UpdateResultSummaryUi();
            UpdateResultTabState();
        }

        private void SwitchToSupervisorTab()
        {
            InvokeOnUiThread(() =>
            {
                if (!supervisorTabEnabled || AssignmentTabControl == null || SupervisorTabItem == null)
                {
                    return;
                }

                AssignmentTabControl.SelectedItem = SupervisorTabItem;
            });
        }

        private void AppendLog(String message)
        {
            assignmentLogService.AppendSystemLog(message);
        }

        private void AppendTaskLog(SmartTaskExecutionContext taskItem, String message)
        {
            assignmentLogService.AppendTaskLog(taskItem, message);
        }

        private static String KeepTail(String value, Int32 maxLength)
        {
            if (String.IsNullOrEmpty(value) || maxLength <= 0)
            {
                return String.Empty;
            }

            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(value.Length - maxLength, maxLength);
        }

        private void AppendAttemptTranscript(
            SmartTaskExecutionContext? taskContext,
            Int32 attemptNumber,
            String phase,
            String? inputText,
            String? outputText,
            String resultText,
            Boolean appendToContext)
        {
            if (taskContext == null)
            {
                return;
            }

            String inputBlock = String.IsNullOrWhiteSpace(inputText) ? "(none)" : inputText.TrimEnd();
            String outputBlock = String.IsNullOrWhiteSpace(outputText) ? "(none)" : outputText.TrimEnd();
            inputBlock = KeepTail(inputBlock, AttemptTranscriptSectionLimit);
            outputBlock = KeepTail(outputBlock, AttemptTranscriptSectionLimit);
            String label = "Attempt " + attemptNumber + " - " + phase;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(label);
            builder.AppendLine();
            builder.AppendLine("Input:");
            builder.AppendLine(inputBlock);
            builder.AppendLine();
            builder.AppendLine("Output:");
            builder.AppendLine(outputBlock);
            builder.AppendLine();
            builder.AppendLine("Result: " + resultText);

            AppendTaskLog(taskContext, builder.ToString().TrimEnd());

            if (appendToContext)
            {
                String summary = label + " => " + resultText;
                if (String.IsNullOrWhiteSpace(taskContext.TaskContext))
                {
                    taskContext.TaskContext = summary;
                }
                else
                {
                    taskContext.TaskContext = taskContext.TaskContext.TrimEnd() + Environment.NewLine + summary;
                }
                taskContext.AppendContextEntry(summary);
            }
        }

        private static String BuildCommandInputDescription(IList<AgentCommandDescription>? commands)
        {
            if (commands == null || commands.Count == 0)
            {
                return "(no commands)";
            }

            StringBuilder builder = new StringBuilder();
            for (Int32 index = 0; index < commands.Count; index++)
            {
                AgentCommandDescription? command = commands[index];
                if (command == null)
                {
                    continue;
                }

                builder.Append("- Attempt command ");
                builder.Append(index + 1);
                builder.Append(": ");
                builder.AppendLine(BuildCommandDisplayText(command));
            }

            return builder.ToString().TrimEnd();
        }

        private static String BuildPersonaInputDescription(String personaName, String systemInstruction, String userPrompt)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(personaName + " system instruction:");
            builder.AppendLine(systemInstruction.Trim());
            builder.AppendLine();
            builder.AppendLine("User prompt:");
            builder.AppendLine(userPrompt.Trim());
            return builder.ToString().TrimEnd();
        }

        private static String BuildCommandAttemptOutputText(SmartTaskExecutionContext taskContext, CommandRunResult? commandOutcome, String fallbackText)
        {
            if (commandOutcome != null && commandOutcome.Succeeded && !String.IsNullOrWhiteSpace(taskContext.LastResultText))
            {
                return taskContext.LastResultText!;
            }

            if (commandOutcome != null && !String.IsNullOrWhiteSpace(commandOutcome.FailureReason))
            {
                return commandOutcome.FailureReason!;
            }

            if (!String.IsNullOrWhiteSpace(taskContext.LastResultText))
            {
                return taskContext.LastResultText!;
            }

            return fallbackText;
        }

        private static String BuildCommandAttemptResultText(CommandRunResult? commandOutcome, Boolean commandsMissing, Boolean informationalTask)
        {
            if (informationalTask)
            {
                return "No command execution required";
            }

            if (commandsMissing)
            {
                return "Blocked: persona returned no commands";
            }

            if (commandOutcome == null)
            {
                return "No command execution performed";
            }

            if (commandOutcome.Succeeded)
            {
                return "Commands completed successfully";
            }

            if (!commandOutcome.CommandsAttempted)
            {
                return "Blocked before execution: " + (commandOutcome.FailureReason ?? "Unknown reason");
            }

            return "Failed: " + (commandOutcome.FailureReason ?? "Unknown reason");
        }

        private void AppendCommandOutput(SmartTaskExecutionContext taskContext, AgentCommandDescription commandDescription, CommandExecutionResult commandResult)
        {
            assignmentLogService.AppendCommandOutput(taskContext, commandDescription, commandResult);
        }

        private void CaptureFailureResult(SmartTaskExecutionContext taskContext, String failureSummary)
        {
            if (taskContext == null || String.IsNullOrWhiteSpace(failureSummary))
            {
                return;
            }

            taskContext.LastResultText = TextUtilityService.BuildCompactSnippet(failureSummary, 2000);
        }

        private void CaptureCommandFailureResult(
            SmartTaskExecutionContext taskContext,
            String commandDisplay,
            String failureDetail,
            CommandExecutionResult? executionResult)
        {
            if (taskContext == null)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            if (!String.IsNullOrWhiteSpace(commandDisplay))
            {
                builder.AppendLine("Command: " + commandDisplay);
            }

            if (!String.IsNullOrWhiteSpace(failureDetail))
            {
                builder.AppendLine("Failure: " + failureDetail);
            }

            if (executionResult != null)
            {
                builder.AppendLine("Exit code: " + executionResult.ExitCode);

                if (!String.IsNullOrWhiteSpace(executionResult.StandardErrorText))
                {
                    builder.AppendLine("stderr:");
                    builder.AppendLine(executionResult.StandardErrorText.Trim());
                }

                if (!String.IsNullOrWhiteSpace(executionResult.StandardOutputText))
                {
                    builder.AppendLine("stdout:");
                    builder.AppendLine(executionResult.StandardOutputText.Trim());
                }
            }

            CaptureFailureResult(taskContext, builder.ToString());
        }

        private void UpdateUsageUi()
        {
            UsageSnapshot snapshot = assignmentController.GetUsageSnapshot();
            InvokeOnUiThread(() =>
            {
                if (UsageTotalRequestsValueTextBlock != null)
                {
                    UsageTotalRequestsValueTextBlock.Text = snapshot.TotalRequests.ToString("N0", CultureInfo.InvariantCulture);
                }

                Int64 totalTokens = snapshot.TotalTokens;
                if (UsageTokensValueTextBlock != null)
                {
                    UsageTokensValueTextBlock.Text = totalTokens == 0
                        ? "0"
                        : UsageFormattingResult.FormatCompactNumber(totalTokens);
                }

                if (UsageTokensDetailTextBlock != null)
                {
                    String promptTokensText = UsageFormattingResult.FormatCompactNumber(snapshot.PromptTokens);
                    String completionTokensText = UsageFormattingResult.FormatCompactNumber(snapshot.CompletionTokens);
                    UsageTokensDetailTextBlock.Text = UiText(UiCatalogKeys.TextTokensDetailPlaceholder, promptTokensText, completionTokensText);
                }

                if (UsageLastUpdateTextBlock != null)
                {
                    DateTime stamp = snapshot.LastUpdatedUtc == default ? DateTime.UtcNow : snapshot.LastUpdatedUtc;
                    String localizedTime = stamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                    UsageLastUpdateTextBlock.Text = UiText(UiCatalogKeys.TextUsageUpdatedPlaceholder, localizedTime);
                }

            });
        }

        private void OnAssignmentControllerUsageChanged(Object? sender, EventArgs e)
        {
            UpdateUsageUi();
        }

        private void CaptureAssignmentFailureReason(String reason)
        {
            assignmentController.CaptureAssignmentFailureReason(reason);
        }

        private String BuildDanglingTaskReason(Boolean assignmentCancelled)
        {
            return assignmentController.BuildDanglingTaskReason(assignmentCancelled);
        }

        private void UpdateQuickStartOverlayStatus()
        {
            InvokeOnUiThread(() =>
            {
                if (QuickStartOverlayGrid == null)
                {
                    return;
                }

                Boolean hasApiKey = IsOpenAiConfigured && !String.IsNullOrWhiteSpace(CurrentOpenAiApiKey);
                Boolean hasModel = !String.IsNullOrWhiteSpace(CurrentOpenAiModelId);
                Boolean hasWorkspace = !String.IsNullOrWhiteSpace(selectedWorkspacePath);
                String promptText = AssignmentPromptTextBox?.Text ?? String.Empty;
                Boolean hasPrompt = !String.IsNullOrWhiteSpace(promptText);

                Boolean prerequisitesMet = hasApiKey && hasModel && hasWorkspace && hasPrompt;
                Boolean shouldShowOverlay = !prerequisitesMet || !hasQuickStartChecklistBeenDismissed;
                showQuickStartOverlay = shouldShowOverlay;
                QuickStartOverlayGrid.IsVisible = shouldShowOverlay;

                String apiStatusText = UiText(hasApiKey ? UiCatalogKeys.QuickStartApiConfigured : UiCatalogKeys.QuickStartApiMissing);
                SetQuickStartStatus(QuickStartApiStatusGlyph, QuickStartApiStatusTextBlock, hasApiKey, apiStatusText);

                String modelStatusText = hasModel
                    ? UiText(UiCatalogKeys.QuickStartModelSelected, CurrentOpenAiModelId ?? String.Empty)
                    : UiText(UiCatalogKeys.QuickStartModelMissing);
                SetQuickStartStatus(QuickStartModelStatusGlyph, QuickStartModelStatusTextBlock, hasModel, modelStatusText);

                String workspaceStatusText = hasWorkspace
                    ? UiText(UiCatalogKeys.QuickStartWorkspaceReady, selectedWorkspacePath ?? String.Empty)
                    : UiText(UiCatalogKeys.QuickStartWorkspaceMissing);
                SetQuickStartStatus(QuickStartWorkspaceStatusGlyph, QuickStartWorkspaceStatusTextBlock, hasWorkspace, workspaceStatusText);

                String promptLengthText = promptText.Length.ToString(CultureInfo.InvariantCulture);
                String promptStatusText = hasPrompt
                    ? UiText(UiCatalogKeys.QuickStartPromptReady, promptLengthText)
                    : UiText(UiCatalogKeys.QuickStartPromptMissing);
                SetQuickStartStatus(QuickStartAssignmentStatusGlyph, QuickStartAssignmentStatusTextBlock, hasPrompt, promptStatusText);
            });
        }

        private Boolean TryReserveOpenAiRequest(String scopeDescription)
        {
            return assignmentController.TryReserveOpenAiRequest(scopeDescription);
        }

        internal Boolean TryReserveSmartTaskSlot(SmartTask task)
        {
            if (assignmentController.TryReserveSmartTaskSlot())
            {
                return true;
            }

            assignmentController.UpdateSmartTaskState(task, SmartTaskStateOptions.Skipped, "Task budget exceeded");
            AppendLog("Skipping task '" + (task.Intent ?? task.Id ?? "unknown") + "' because the smart task budget was exceeded.");
            return false;
        }

        private async Task<Boolean> RunRecursiveSupervisorAsync(String? assignmentTitle, String assignmentPrompt, String? workspaceContext, CancellationToken cancellationToken)
        {
            SmartTask? rootTask = assignmentRootSmartTask;
            if (rootTask == null)
            {
                AppendLog("Supervisor error: root task was not initialized.");
                return false;
            }

            SmartTaskExecutionContext rootContext = assignmentController.EnsureExecutionContextForSmartTask(rootTask, maxRepairAttemptsPerTask);
            rootContext.TaskContext = assignmentPrompt;

            SmartTaskSchedulerService pendingTasks = new SmartTaskSchedulerService();
            SmartTaskSchedulerService? previousScheduler = activeSmartTaskScheduler;
            activeSmartTaskScheduler = pendingTasks;
            pendingTasks.Push(rootTask);

            Boolean overallSuccess = true;

            try
            {
                while (pendingTasks.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WaitIfPausedAsync(cancellationToken);

                    SmartTask currentTask = pendingTasks.Pop();
                    Boolean continueAssignment = await ProcessSmartTaskAsync(currentTask, pendingTasks, cancellationToken);
                    if (!continueAssignment)
                    {
                        overallSuccess = false;
                        break;
                    }
                }

                return overallSuccess;
            }
            finally
            {
                activeSmartTaskScheduler = previousScheduler;
            }
        }

        private async Task<Boolean> ProcessSmartTaskAsync(SmartTask smartTask, SmartTaskSchedulerService pendingTasks, CancellationToken cancellationToken)
        {
            SmartTaskExecutionContext taskContext = assignmentController.EnsureExecutionContextForSmartTask(smartTask, maxRepairAttemptsPerTask);
            taskContext.StartedAt = DateTime.Now;
            taskContext.SetStatus(AssignmentTaskStatusOptions.InProgress);
            assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Planning, "Delegator evaluating task");

            if (!TryReserveSmartTaskSlot(smartTask))
            {
                AppendTaskLog(taskContext, "Skipped because task budget was exceeded.");
                return false;
            }

            String observationContext = BuildSmartTaskObservationContext(smartTask);
            if (TrySkipTaskViaDeduplication(smartTask, taskContext))
            {
                return true;
            }

            Double executionBias = assignmentController.GetExecutionBiasForDepth(smartTask.Depth);
            Boolean mustDecompose = SmartTaskHelperService.ShouldForceDecomposition(smartTask);
            Boolean allowDecomposition = mustDecompose || executionBias < 1.0;

            DelegatorDecisionResult delegatorDecision = await RequestDelegatorDecisionAsync(smartTask, observationContext, allowDecomposition, cancellationToken)
                ?? new DelegatorDecisionResult { Strategy = "Execute", Reason = "Delegator returned null" };
            SmartTaskStrategyOptions strategy = SmartTaskHelperService.NormalizeDelegatorStrategy(delegatorDecision.Strategy, allowDecomposition);

            if (mustDecompose && strategy != SmartTaskStrategyOptions.Decompose)
            {
                AppendTaskLog(taskContext, "Supervisor forcing decomposition to delegate work from manager-level task.");
                strategy = SmartTaskStrategyOptions.Decompose;
            }

            assignmentController.UpdateSmartTaskStrategy(smartTask, strategy);
            AppendTaskLog(taskContext, "Delegator chose strategy: " + strategy + (delegatorDecision.Reason != null ? " (" + delegatorDecision.Reason + ")" : String.Empty));

            taskContext.RequiresCommandExecution = strategy == SmartTaskStrategyOptions.Execute || strategy == SmartTaskStrategyOptions.Research;

            if (strategy == SmartTaskStrategyOptions.Skip)
            {
                MarkSmartTaskCompleted(smartTask, taskContext, "Delegator determined the goal is already satisfied.");
                return true;
            }

            if (strategy == SmartTaskStrategyOptions.Decompose && !allowDecomposition)
            {
                strategy = SmartTaskStrategyOptions.Execute;
            }

            if (strategy == SmartTaskStrategyOptions.Decompose)
            {
                List<ArchitectPlannedSubtaskResult> subtasks = await RequestArchitectSubtasksAsync(smartTask, observationContext, cancellationToken);
                subtasks = await VerifyPlannedSubtasksAsync(smartTask, subtasks, cancellationToken);
                if (subtasks.Count == 0)
                {
                    AppendTaskLog(taskContext, "Architect returned zero subtasks; falling back to Execute strategy.");
                    strategy = SmartTaskStrategyOptions.Execute;
                }
                else
                {
                    assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Executing, "Managing subtasks");
                    EnqueueChildSmartTasks(smartTask, subtasks, pendingTasks);
                    AppendTaskLog(taskContext, "Queued " + subtasks.Count + " subtask(s) for execution.");
                    return true;
                }
            }

            Int32 plannedAttemptNumber = Math.Max(1, taskContext.AttemptCount + 1);
            List<AgentCommandDescription> commands;
            if (strategy == SmartTaskStrategyOptions.Research)
            {
                commands = await RequestResearcherCommandsAsync(smartTask, taskContext, observationContext, cancellationToken, plannedAttemptNumber: plannedAttemptNumber);
            }
            else
            {
                commands = await RequestEngineerCommandsAsync(smartTask, taskContext, observationContext, cancellationToken, plannedAttemptNumber: plannedAttemptNumber);

                if (taskContext.RequiresCommandExecution && commands.Count == 0)
                {
                    AppendTaskLog(taskContext, "Engineer provided no commands; requesting a retry with additional guidance.");
                    commands = await RequestEngineerCommandsAsync(smartTask, taskContext, observationContext, cancellationToken, startWithReminder: true, plannedAttemptNumber: plannedAttemptNumber);
                }
            }

            Boolean commandSuccess = await ExecuteStrategyCommandsAsync(smartTask, taskContext, commands, strategy, cancellationToken);
            if (!commandSuccess)
            {
                Boolean scheduledRepair = assignmentController.TryScheduleRepairSmartTask(
                    smartTask,
                    taskContext,
                    pendingTasks,
                    maxRepairAttemptsPerTask,
                    CreateRepairCallbacks());
                if (scheduledRepair)
                {
                    AppendTaskLog(taskContext, "Primary execution failed; queued a repair smart task to continue immediately.");
                    return true;
                }
            }

            return commandSuccess;
        }

        private Boolean TrySkipTaskViaDeduplication(SmartTask smartTask, SmartTaskExecutionContext taskContext)
        {
            if (!assignmentController.HasIntentBeenCompleted(smartTask.Intent))
            {
                return false;
            }

            AppendTaskLog(taskContext, "Deduplication cache indicates this intent is already completed. Skipping task.");
            taskContext.CompletedAt = DateTime.Now;
            taskContext.SetStatus(AssignmentTaskStatusOptions.Skipped);
            taskContext.AllowsDependentsToProceed = true;
            assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Skipped, "Deduplicated");
            return true;
        }



        private void MarkSmartTaskCompleted(SmartTask smartTask, SmartTaskExecutionContext taskContext, String stage)
        {
            taskContext.CompletedAt = DateTime.Now;
            taskContext.SetStatus(AssignmentTaskStatusOptions.Succeeded);
            assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Succeeded, stage);

            assignmentController.RegisterCompletedIntent(smartTask.Intent);
        }

        private void EnqueueChildSmartTasks(SmartTask parent, List<ArchitectPlannedSubtaskResult> plannedSubtasks, SmartTaskSchedulerService pendingTasks)
        {
            List<SmartTask> childTasks = new List<SmartTask>();

            for (Int32 index = 0; index < plannedSubtasks.Count; index++)
            {
                ArchitectPlannedSubtaskResult planned = plannedSubtasks[index];
                SmartTask childTask = new SmartTask();
                childTask.Id = "task-" + Guid.NewGuid().ToString("N");
                childTask.Intent = String.IsNullOrWhiteSpace(planned.Intent) ? (parent.Intent + " / subtask " + (index + 1).ToString(CultureInfo.InvariantCulture)) : planned.Intent!.Trim();
                childTask.Type = ParseSmartTaskType(planned.Type);
                childTask.ParentId = parent.Id;
                childTask.Phase = planned.Phase;
                childTask.Depth = parent.Depth + 1;
                assignmentController.ApplyWorkBudgetToSmartTask(childTask, childTask.Depth);
                childTasks.Add(childTask);

                InvokeOnUiThread(() =>
                {
                    parent.Subtasks.Add(childTask);
                });

                SmartTaskExecutionContext childContext = assignmentController.EnsureExecutionContextForSmartTask(childTask, maxRepairAttemptsPerTask);
                childContext.ParentTaskId = parent.Id;
                childContext.TaskContext = BuildSubtaskTaskContext(parent, plannedSubtasks, index);
                AppendTaskLog(childContext, "Subtask created from architect plan.");
            }

            ScheduleSmartTasksForImmediateExecution(childTasks, pendingTasks);
        }

        private static String BuildSubtaskTaskContext(SmartTask parent, IList<ArchitectPlannedSubtaskResult> plannedSubtasks, Int32 currentIndex)
        {
            String parentIntent = !String.IsNullOrWhiteSpace(parent.Intent) ? parent.Intent!.Trim() : "(unspecified intent)";
            SmartTaskExecutionContext? parentContext = parent.BoundAssignmentTask;
            String? parentContextText = parentContext?.TaskContext;
            String? parentResultText = parentContext?.LastResultText;
            Int32 sanitizedIndex = currentIndex;

            if (plannedSubtasks == null || plannedSubtasks.Count == 0)
            {
                return parentIntent;
            }

            if (sanitizedIndex < 0 || sanitizedIndex >= plannedSubtasks.Count)
            {
                sanitizedIndex = 0;
            }

            ArchitectPlannedSubtaskResult currentPlan = plannedSubtasks[sanitizedIndex];

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Parent task intent: " + parentIntent);
            if (!String.IsNullOrWhiteSpace(parent.Stage))
            {
                builder.AppendLine("Parent stage: " + parent.Stage!.Trim());
            }

            if (!String.IsNullOrWhiteSpace(parentContextText))
            {
                builder.AppendLine();
                builder.AppendLine("Parent context summary:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(parentContextText.Trim(), 1200));
            }
            else if (!String.IsNullOrWhiteSpace(parentResultText))
            {
                builder.AppendLine();
                builder.AppendLine("Parent recent result:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(parentResultText.Trim(), 800));
            }

            builder.AppendLine();
            builder.AppendLine("Sibling subtask plan:");
            for (Int32 index = 0; index < plannedSubtasks.Count; index++)
            {
                ArchitectPlannedSubtaskResult siblingPlan = plannedSubtasks[index];
                String siblingIntent = !String.IsNullOrWhiteSpace(siblingPlan.Intent) ? siblingPlan.Intent!.Trim() : "(unspecified intent)";
                builder.Append("  ");
                builder.Append(index + 1);
                builder.Append('.');
                builder.Append(' ');
                builder.Append(siblingIntent);

                if (!String.IsNullOrWhiteSpace(siblingPlan.Notes))
                {
                    builder.Append(" — ");
                    builder.Append(TextUtilityService.BuildCompactSnippet(siblingPlan.Notes!.Trim(), 600));
                }

                if (index == sanitizedIndex)
                {
                    builder.Append("  <-- current subtask");
                }

                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("Focus for this subtask:");
            if (!String.IsNullOrWhiteSpace(currentPlan.Notes))
            {
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(currentPlan.Notes!.Trim(), 1000));
            }
            else if (!String.IsNullOrWhiteSpace(currentPlan.Intent))
            {
                builder.AppendLine(currentPlan.Intent!.Trim());
            }
            else
            {
                builder.AppendLine(parentIntent);
            }

            return builder.ToString().Trim();
        }

        private void ScheduleSmartTasksForImmediateExecution(IEnumerable<SmartTask> tasks, SmartTaskSchedulerService? schedulerOverride = null)
        {
            SmartTaskSchedulerService? schedulerCandidate = schedulerOverride ?? activeSmartTaskScheduler;
            if (schedulerCandidate == null)
            {
                return;
            }

            SmartTaskSchedulerService scheduler = schedulerCandidate;

            List<SmartTask> orderedTasks = new List<SmartTask>();
            foreach (SmartTask task in tasks)
            {
                if (task != null)
                {
                    orderedTasks.Add(task);
                }
            }

            if (orderedTasks.Count == 0)
            {
                return;
            }

            for (Int32 index = orderedTasks.Count - 1; index >= 0; index--)
            {
                scheduler.Push(orderedTasks[index]);
            }
        }

        private RepairOrchestrationResult CreateRepairCallbacks()
        {
            return new RepairOrchestrationResult
            {
                AppendTaskLog = AppendTaskLog
            };
        }

        private FailureResolutionResult CreateFailureResolutionCallbacks()
        {
            return new FailureResolutionResult
            {
                AppendTaskLog = AppendTaskLog,
                InsertTasksAfter = InsertPlannedTasksAfter
            };
        }


        private SmartTaskTypeOptions ParseSmartTaskType(String? typeText)
        {
            if (String.IsNullOrWhiteSpace(typeText))
            {
                return SmartTaskTypeOptions.Worker;
            }

            String normalized = typeText.Trim().ToLowerInvariant();
            if (normalized.Contains("research"))
            {
                return SmartTaskTypeOptions.Research;
            }
            if (normalized.Contains("phase"))
            {
                return SmartTaskTypeOptions.Phase;
            }
            return SmartTaskTypeOptions.Worker;
        }

        private String BuildSmartTaskObservationContext(SmartTask smartTask)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Intent: " + (smartTask.Intent ?? "(unspecified)"));
            builder.AppendLine("Depth: " + smartTask.Depth.ToString(CultureInfo.InvariantCulture));
            Double bias = assignmentController.GetExecutionBiasForDepth(smartTask.Depth);
            builder.AppendLine("Execution bias: " + (bias * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%");
            builder.AppendLine("Workspace: " + (selectedWorkspacePath ?? "(not selected)"));

            SmartTaskExecutionContext? context = smartTask.BoundAssignmentTask;
            if (context != null && !String.IsNullOrWhiteSpace(context.TaskContext))
            {
                builder.AppendLine();
                builder.AppendLine("Previous context:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(context.TaskContext, 1200));
            }

            if (context != null && !String.IsNullOrWhiteSpace(context.AggregatedContextSnapshot))
            {
                builder.AppendLine();
                builder.AppendLine("Aggregated notes:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(context.AggregatedContextSnapshot, 1200));
            }

            if (context != null && !String.IsNullOrWhiteSpace(context.LastResultText))
            {
                builder.AppendLine();
                builder.AppendLine("Last command output snippet:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(context.LastResultText, 800));
            }
            if (context != null && !String.IsNullOrWhiteSpace(context.TaskLogText))
            {
                builder.AppendLine();
                builder.AppendLine("Execution log:");
                builder.AppendLine(context.TaskLogText.Trim());
            }

            assignmentController.AppendSemanticFactsSection(builder, "Semantic blackboard facts shared by other tasks:", includeReminder: true);

            SmartTask? parentTask = assignmentController.FindSmartTaskNode(smartTask.ParentId);
            if (parentTask != null)
            {
                builder.AppendLine();
                builder.AppendLine("Parent intent: " + (parentTask.Intent ?? "(unspecified)"));
                builder.AppendLine("Parent stage: " + (parentTask.Stage ?? "(none)"));
            }

            SmartTaskExecutionContext? rootContext = assignmentRootSmartTask?.BoundAssignmentTask;
            if (rootContext != null && !String.IsNullOrWhiteSpace(rootContext.TaskContext))
            {
                builder.AppendLine();
                builder.AppendLine("Assignment brief:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(rootContext.TaskContext, 2000));
            }

            if (assignmentSuccessHeuristics.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Success heuristics:");
                builder.AppendLine(assignmentController.BuildSuccessHeuristicSummary());
            }

            return builder.ToString();
        }

        private async Task<DelegatorDecisionResult?> RequestDelegatorDecisionAsync(SmartTask smartTask, String observationContext, Boolean allowDecomposition, CancellationToken cancellationToken)
        {
            String systemInstruction =
                "You are the Delegator persona inside the ACE recursive supervisor. " +
                "Your responsibility is to analyze the current task intent and decide whether to Skip (goal already satisfied), perform Research (read-only commands), Execute (perform work commands), or Decompose (create manager subtasks). " +
                "When key facts are missing (files not yet read, requirements unclear), prefer choosing Research first so the Engineer can act with confidence in a later pass. " +
                "Return a strict JSON object with: {\"strategy\": \"Skip|Research|Execute|Decompose\", \"reason\": string, \"notes\": string}. " +
                "Never choose Decompose when the allow_decomposition flag is false.";

            StringBuilder userBuilder = new StringBuilder();
            userBuilder.AppendLine("Task intent: " + (smartTask.Intent ?? "(unspecified)"));
            userBuilder.AppendLine("Depth: " + smartTask.Depth);
            userBuilder.AppendLine("Allow decomposition: " + (allowDecomposition ? "true" : "false"));
            userBuilder.AppendLine();
            userBuilder.AppendLine("Observation context:");
            userBuilder.AppendLine(observationContext);
            userBuilder.AppendLine();
            userBuilder.AppendLine("Respond ONLY with the JSON object.");

            ChatCompletionMessage[] messages = chatCompletionService.BuildPersonaMessages(systemInstruction, userBuilder.ToString());
            ChatStreamingResult? result = await chatCompletionService.SendChatCompletionRequestAsync(messages, UsageChannelOptions.General, "Delegator", cancellationToken);
            if (result == null || String.IsNullOrWhiteSpace(result.RawContent))
            {
                AppendLog("Delegator returned no content; defaulting to Execute strategy.");
                return new DelegatorDecisionResult { Strategy = "Execute", Reason = "No response" };
            }

            DelegatorDecisionResult? parsed = TryDeserializeJson<DelegatorDecisionResult>(result.RawContent);
            if (parsed == null)
            {
                String? extracted = ExtractJsonObject(result.RawContent);
                if (!String.IsNullOrWhiteSpace(extracted))
                {
                    parsed = TryDeserializeJson<DelegatorDecisionResult>(extracted);
                }
            }

            if (parsed == null)
            {
                AppendLog("Delegator response parsing failed; defaulting to Execute.");
                parsed = new DelegatorDecisionResult { Strategy = "Execute", Reason = "Invalid JSON" };
            }

            return parsed;
        }

        private async Task<List<ArchitectPlannedSubtaskResult>> RequestArchitectSubtasksAsync(SmartTask smartTask, String observationContext, CancellationToken cancellationToken)
        {
            String systemInstruction =
                "You are the Architect persona. Decompose the provided intent into a small list of child tasks that can be executed sequentially. " +
                "Return JSON: {\"subtasks\":[{\"intent\":string, \"type\":string, \"notes\":string, \"phase\":string}]}";

            StringBuilder userBuilder = new StringBuilder();
            userBuilder.AppendLine("Parent intent: " + (smartTask.Intent ?? "(unspecified)"));
            userBuilder.AppendLine("Depth: " + smartTask.Depth);
            userBuilder.AppendLine("Context:");
            userBuilder.AppendLine(observationContext);
            userBuilder.AppendLine();
            userBuilder.AppendLine("Respond ONLY with the JSON object.");

            ChatCompletionMessage[] messages = chatCompletionService.BuildPersonaMessages(systemInstruction, userBuilder.ToString());
            ChatStreamingResult? result = await chatCompletionService.SendChatCompletionRequestAsync(messages, UsageChannelOptions.General, "Architect", cancellationToken);
            if (result == null || String.IsNullOrWhiteSpace(result.RawContent))
            {
                AppendLog("Architect returned no content; skipping decomposition.");
                return new List<ArchitectPlannedSubtaskResult>();
            }

            ArchitectPlanResult? parsed = TryDeserializeJson<ArchitectPlanResult>(result.RawContent);
            if (parsed == null)
            {
                String? extracted = ExtractJsonObject(result.RawContent);
                if (!String.IsNullOrWhiteSpace(extracted))
                {
                    parsed = TryDeserializeJson<ArchitectPlanResult>(extracted);
                }
            }

            if (parsed == null || parsed.Subtasks == null)
            {
                AppendLog("Architect response parsing failed.");
                return new List<ArchitectPlannedSubtaskResult>();
            }

            return parsed.Subtasks;
        }

        private async Task GenerateAssignmentSuccessHeuristicsAsync(String? assignmentTitle, String assignmentPrompt, String? workspaceContext, CancellationToken cancellationToken)
        {
            assignmentSuccessHeuristics.Clear();

            if (!TryReserveOpenAiRequest("Quality assurance heuristics"))
            {
                assignmentSuccessHeuristics.Add(new SuccessHeuristicItem
                {
                    Description = "Deliver outputs that satisfy the assignment prompt.",
                    Mandatory = true,
                    Evidence = "Manual inspection"
                });
                assignmentController.ResetHeuristicEvaluationIndicators();
                return;
            }

            String systemInstruction =
                "You are the Quality Assurance persona. Analyze the assignment brief and propose a short list of success heuristics that" +
                " indicate the work is complete. Only require tests or tooling when they materially increase confidence. Return JSON" +
                " in the shape {\"heuristics\":[{\"description\":string, \"mandatory\":bool, \"evidence\":string}]} and keep the list small.";

            StringBuilder userBuilder = new StringBuilder();
            userBuilder.AppendLine("Assignment title: " + (String.IsNullOrWhiteSpace(assignmentTitle) ? "(not provided)" : assignmentTitle));
            userBuilder.AppendLine("Prompt:");
            userBuilder.AppendLine(assignmentPrompt);
            if (!String.IsNullOrWhiteSpace(workspaceContext))
            {
                userBuilder.AppendLine();
                userBuilder.AppendLine("Workspace context summary:");
                userBuilder.AppendLine(TextUtilityService.BuildCompactSnippet(workspaceContext!, 1600));
            }
            userBuilder.AppendLine();
            userBuilder.AppendLine("Provide at most 5 heuristics.");

            ChatCompletionMessage[] messages = chatCompletionService.BuildPersonaMessages(systemInstruction, userBuilder.ToString());
            ChatStreamingResult? result = await chatCompletionService.SendChatCompletionRequestAsync(messages, UsageChannelOptions.General, "QualityAssurance", cancellationToken);
            SuccessHeuristicPlanResult? plan = null;
            if (result != null && !String.IsNullOrWhiteSpace(result.RawContent))
            {
                plan = TryDeserializeJson<SuccessHeuristicPlanResult>(result.RawContent);
                if (plan == null)
                {
                    String? extracted = ExtractJsonObject(result.RawContent);
                    if (!String.IsNullOrWhiteSpace(extracted))
                    {
                        plan = TryDeserializeJson<SuccessHeuristicPlanResult>(extracted);
                    }
                }
            }

            if (plan?.Heuristics != null)
            {
                foreach (SuccessHeuristicItem item in plan.Heuristics)
                {
                    if (!String.IsNullOrWhiteSpace(item.Description))
                    {
                        assignmentSuccessHeuristics.Add(new SuccessHeuristicItem
                        {
                            Description = item.Description!,
                            Mandatory = item.Mandatory,
                            Evidence = item.Evidence
                        });
                    }
                }
            }

            if (assignmentSuccessHeuristics.Count == 0)
            {
                assignmentSuccessHeuristics.Add(new SuccessHeuristicItem
                {
                    Description = "Deliver outputs that satisfy the assignment prompt.",
                    Mandatory = true,
                    Evidence = "Manual inspection"
                });
            }

            AppendLog("Quality assurance heuristics generated: " + assignmentSuccessHeuristics.Count + ".");
            assignmentController.ResetHeuristicEvaluationIndicators();
        }

        private String BuildAssignmentOutcomeEvidenceSnapshot()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Assignment status: " + AssignmentStatusText);

            if (!String.IsNullOrWhiteSpace(AssignmentAnswerOutputText))
            {
                builder.AppendLine();
                builder.AppendLine("Final answer excerpt:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(AssignmentAnswerOutputText, 1500));
            }

            if (!String.IsNullOrWhiteSpace(CommandsSummaryOutputText))
            {
                builder.AppendLine();
                builder.AppendLine("Command summary excerpt:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(CommandsSummaryOutputText, 1000));
            }

            builder.AppendLine();
            builder.AppendLine("Success heuristics to evaluate:");
            builder.AppendLine(assignmentController.BuildSuccessHeuristicSummary());

            assignmentController.AppendSemanticFactsSection(builder, "Observed semantic facts:", maxFacts: 8);

            builder.AppendLine();
            builder.AppendLine("Task outcomes:");
            Int32 maxTasks = Math.Min(assignmentTaskItems.Count, 15);
            for (Int32 index = 0; index < maxTasks; index++)
            {
                SmartTaskExecutionContext task = assignmentTaskItems[index];
                builder.AppendLine("- " + task.Label + " => " + task.Status);
                if (!String.IsNullOrWhiteSpace(task.LastResultText))
                {
                    builder.AppendLine("  Evidence: " + TextUtilityService.BuildCompactSnippet(task.LastResultText, 300));
                }
            }

            if (!String.IsNullOrWhiteSpace(AssignmentRawOutputText))
            {
                builder.AppendLine();
                builder.AppendLine("Raw agent output excerpt:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(AssignmentRawOutputText, 800));
            }

            return builder.ToString();
        }

        private async Task EvaluateAssignmentResultAsync(CancellationToken cancellationToken)
        {
            if (String.Equals(AssignmentStatusText, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                FinalizeResultEvaluation(null, "Assignment cancelled", "The assignment was cancelled before the success heuristics could be evaluated.");
                return;
            }

            if (assignmentSuccessHeuristics.Count == 0)
            {
                FinalizeResultEvaluation(true, "No heuristics captured", "The run completed without explicit success heuristics, so it is treated as successful.");
                return;
            }

            String? evaluationApiKey = CurrentOpenAiApiKey;
            String? evaluationModelId = CurrentOpenAiModelId;
            if (String.IsNullOrWhiteSpace(evaluationApiKey) || String.IsNullOrWhiteSpace(evaluationModelId))
            {
                FinalizeResultEvaluation(null, "Evaluation unavailable", "Connect to OpenAI to evaluate success heuristics for this assignment.");
                return;
            }

            try
            {
                String evidenceSnapshot = BuildAssignmentOutcomeEvidenceSnapshot();
                HeuristicEvaluationListResult? evaluation = await RequestHeuristicEvaluationResponseAsync(evidenceSnapshot, cancellationToken);

                if (evaluation == null || evaluation.Heuristics == null || evaluation.Heuristics.Count == 0)
                {
                    FinalizeResultEvaluation(null, "Evaluation incomplete", "The result evaluator did not return any structured heuristic decisions.");
                    return;
                }

                assignmentController.ApplyHeuristicEvaluationResponse(evaluation);

                Boolean heuristicsPassed = assignmentController.DetermineHeuristicVerdict();
                String summaryText = !String.IsNullOrWhiteSpace(evaluation.Summary)
                    ? evaluation.Summary!.Trim()
                    : (heuristicsPassed ? "All mandatory heuristics passed." : assignmentController.BuildFailedHeuristicSummary(true));

                String headline = heuristicsPassed ? "Success heuristics satisfied" : "Success heuristics failed";
                FinalizeResultEvaluation(heuristicsPassed, headline, summaryText);
            }
            catch (OperationCanceledException)
            {
                FinalizeResultEvaluation(null, "Evaluation cancelled", "Result evaluation was cancelled.");
            }
            catch (Exception exception)
            {
                AppendLog("Result evaluation failed: " + exception.Message);
                FinalizeResultEvaluation(null, "Evaluation failed", "Result evaluation failed: " + exception.Message);
            }
        }

        private async Task<HeuristicEvaluationListResult?> RequestHeuristicEvaluationResponseAsync(String evidenceSnapshot, CancellationToken cancellationToken)
        {
            String systemInstruction =
                "You are the Result Auditor persona. Using the provided heuristics and evidence, mark each heuristic as passed or failed." +
                " Use 0-based indexes matching the order the heuristics were provided. Respond ONLY with JSON: {\"summary\":string,\"heuristics\":[{\"index\":number,\"passed\":bool,\"notes\":string}]}";

            StringBuilder userBuilder = new StringBuilder();
            userBuilder.AppendLine("Heuristics (in order):");
            userBuilder.AppendLine(assignmentController.BuildSuccessHeuristicSummary());
            userBuilder.AppendLine();
            userBuilder.AppendLine("Evidence to evaluate:");
            userBuilder.AppendLine(evidenceSnapshot);
            userBuilder.AppendLine();
            userBuilder.AppendLine("Return only the JSON structure.");

            ChatCompletionMessage[] messages = chatCompletionService.BuildPersonaMessages(systemInstruction, userBuilder.ToString());
            ChatStreamingResult? result = await chatCompletionService.SendChatCompletionRequestAsync(messages, UsageChannelOptions.General, "ResultEvaluator", cancellationToken);
            if (result == null || String.IsNullOrWhiteSpace(result.RawContent))
            {
                return null;
            }

            HeuristicEvaluationListResult? response = TryDeserializeJson<HeuristicEvaluationListResult>(result.RawContent);
            if (response == null)
            {
                String? extracted = ExtractJsonObject(result.RawContent);
                if (!String.IsNullOrWhiteSpace(extracted))
                {
                    response = TryDeserializeJson<HeuristicEvaluationListResult>(extracted);
                }
            }

            return response;
        }

        private void FinalizeResultEvaluation(Boolean? heuristicsPassed, String headline, String detail)
        {
            assignmentResultHeadline = headline;
            assignmentResultDetail = detail;
            assignmentResultStatus = heuristicsPassed switch
            {
                true => SuccessHeuristicEvaluationStatusOptions.Passed,
                false => SuccessHeuristicEvaluationStatusOptions.Failed,
                _ => SuccessHeuristicEvaluationStatusOptions.Pending
            };

            resultTabEnabled = true;
            UpdateResultSummaryUi();
            UpdateResultTabState();

            if (heuristicsPassed.HasValue)
            {
                UpdateAssignmentStatusFromHeuristics(heuristicsPassed.Value);
                ApplyHeuristicVerdictToRootTask(heuristicsPassed.Value, detail);
                if (heuristicsPassed.Value == false)
                {
                    String failureDetail = !String.IsNullOrWhiteSpace(detail)
                        ? detail
                        : "the success heuristics were not satisfied";
                    CaptureAssignmentFailureReason(failureDetail);
                }
            }

            SwitchToResultTab();
        }

        private void UpdateAssignmentStatusFromHeuristics(Boolean heuristicsPassed)
        {
            if (String.Equals(AssignmentStatusText, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (heuristicsPassed)
            {
                if (!String.Equals(AssignmentStatusText, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    assignmentController.UpdateAssignmentStatus("Completed");
                }
            }
            else
            {
                assignmentController.UpdateAssignmentStatus("Failed");
            }

            UpdateUiState();
        }

        private void ApplyHeuristicVerdictToRootTask(Boolean heuristicsPassed, String? detail)
        {
            SmartTask? rootTask = assignmentRootSmartTask;
            if (rootTask == null)
            {
                return;
            }

            SmartTaskStateOptions verdictState = heuristicsPassed ? SmartTaskStateOptions.Succeeded : SmartTaskStateOptions.Failed;
            String stage = heuristicsPassed ? "Result: heuristics passed" : "Result: heuristics failed";
            if (!String.IsNullOrWhiteSpace(detail))
            {
                stage = stage + " - " + TextUtilityService.BuildCompactSnippet(detail, 160);
            }

            assignmentController.UpdateSmartTaskState(rootTask, verdictState, stage);
        }
        private async Task<List<ArchitectPlannedSubtaskResult>> VerifyPlannedSubtasksAsync(SmartTask parentTask, List<ArchitectPlannedSubtaskResult> proposedSubtasks, CancellationToken cancellationToken)
        {
            if (proposedSubtasks == null || proposedSubtasks.Count == 0)
            {
                return new List<ArchitectPlannedSubtaskResult>();
            }

            if (proposedSubtasks.Count == 1 && assignmentSuccessHeuristics.Count == 0)
            {
                return proposedSubtasks;
            }

            if (!TryReserveOpenAiRequest("Task verifier"))
            {
                return proposedSubtasks;
            }

            String systemInstruction =
                "You are the Task Verification persona. Remove duplicates, collapse redundant repairs, and ensure subtasks clearly advance the" +
                " parent intent. If a task is unnecessary, mark it rejected with a reason. Respond with JSON: {\"accepted\":[],\"rejected\":[],\"notes\":string}.";

            StringBuilder userBuilder = new StringBuilder();
            userBuilder.AppendLine("Parent intent: " + (parentTask.Intent ?? "(unspecified)"));
            userBuilder.AppendLine();
            userBuilder.AppendLine("Success heuristics:");
            userBuilder.AppendLine(assignmentController.BuildSuccessHeuristicSummary());
            userBuilder.AppendLine();
            userBuilder.AppendLine("Proposed subtasks:");
            for (Int32 index = 0; index < proposedSubtasks.Count; index++)
            {
                ArchitectPlannedSubtaskResult subtask = proposedSubtasks[index];
                userBuilder.AppendLine("{" +
                    "\"intent\": \"" + (subtask.Intent ?? String.Empty).Replace("\"", "'") + "\"," +
                    " \"type\": \"" + (subtask.Type ?? String.Empty).Replace("\"", "'") + "\"," +
                    " \"notes\": \"" + (subtask.Notes ?? String.Empty).Replace("\"", "'") + "\" }");
            }
            userBuilder.AppendLine();
            userBuilder.AppendLine("Return only the JSON structure.");

            ChatCompletionMessage[] messages = chatCompletionService.BuildPersonaMessages(systemInstruction, userBuilder.ToString());
            ChatStreamingResult? result = await chatCompletionService.SendChatCompletionRequestAsync(messages, UsageChannelOptions.General, "TaskVerifier", cancellationToken);
            if (result == null || String.IsNullOrWhiteSpace(result.RawContent))
            {
                return proposedSubtasks;
            }

            TaskVerificationResult? response = TryDeserializeJson<TaskVerificationResult>(result.RawContent);
            if (response == null)
            {
                String? extracted = ExtractJsonObject(result.RawContent);
                if (!String.IsNullOrWhiteSpace(extracted))
                {
                    response = TryDeserializeJson<TaskVerificationResult>(extracted);
                }
            }

            if (response?.Rejected != null)
            {
                foreach (TaskVerificationRejectionResult rejection in response.Rejected)
                {
                    if (!String.IsNullOrWhiteSpace(rejection.Intent))
                    {
                        AppendLog("Task verifier rejected '" + rejection.Intent + "': " + (rejection.Reason ?? "No reason provided."));
                    }
                }
            }

            if (response?.Accepted != null && response.Accepted.Count > 0)
            {
                return response.Accepted;
            }

            return proposedSubtasks;
        }

        private async Task<List<AgentCommandDescription>> RequestResearcherCommandsAsync(
            SmartTask smartTask,
            SmartTaskExecutionContext taskContext,
            String observationContext,
            CancellationToken cancellationToken,
            Boolean startWithReminder = false,
            Int32 plannedAttemptNumber = 1)
        {
            String systemInstruction = BuildResearcherSystemInstruction();
            return await RequestPersonaCommandsWithRetriesAsync(
                "Researcher",
                systemInstruction,
                includeReminder => BuildResearcherUserPrompt(smartTask, observationContext, includeReminder),
                taskContext,
                cancellationToken,
                startWithReminder,
                plannedAttemptNumber);
        }

        private async Task<List<AgentCommandDescription>> RequestEngineerCommandsAsync(
            SmartTask smartTask,
            SmartTaskExecutionContext taskContext,
            String observationContext,
            CancellationToken cancellationToken,
            Boolean startWithReminder = false,
            Int32 plannedAttemptNumber = 1)
        {
            String systemInstruction = BuildEngineerSystemInstruction();
            return await RequestPersonaCommandsWithRetriesAsync(
                "Engineer",
                systemInstruction,
                includeReminder => BuildEngineerUserPrompt(smartTask, observationContext, includeReminder),
                taskContext,
                cancellationToken,
                startWithReminder,
                plannedAttemptNumber);
        }

        private async Task<List<AgentCommandDescription>> RequestPersonaCommandsWithRetriesAsync(
            String personaName,
            String systemInstruction,
            Func<Boolean, String> buildUserPrompt,
            SmartTaskExecutionContext taskContext,
            CancellationToken cancellationToken,
            Boolean startWithReminder,
            Int32 plannedAttemptNumber)
        {
            const Int32 maxAttempts = 3;
            Boolean includeReminder = startWithReminder;

            for (Int32 attempt = 1; attempt <= maxAttempts; attempt++)
            {
                String userPrompt = buildUserPrompt(includeReminder);
                String personaInput = BuildPersonaInputDescription(personaName, systemInstruction, userPrompt);
                ChatCompletionMessage[] messages = chatCompletionService.BuildPersonaMessages(systemInstruction, userPrompt);
                ChatStreamingResult? result = await chatCompletionService.SendChatCompletionRequestAsync(messages, UsageChannelOptions.General, personaName, cancellationToken);
                List<AgentCommandDescription> commands = TryExtractCommandsFromPersonaResponse(result);
                String responseText = result?.RawContent ?? "(no response)";
                String phaseLabel = personaName + " planning" + (attempt > 1 ? " retry " + attempt : String.Empty);
                String resultText = commands.Count > 0
                    ? "Returned " + commands.Count + " command(s)"
                    : "Response missing commands";
                AppendAttemptTranscript(taskContext, plannedAttemptNumber, phaseLabel, personaInput, responseText, resultText, appendToContext: true);
                if (commands.Count > 0)
                {
                    return commands;
                }

                if (result != null && !String.IsNullOrWhiteSpace(result.RawContent))
                {
                    String snippet = TextUtilityService.BuildCompactSnippet(result.RawContent, 600);
                    AppendLog(personaName + " invalid payload (attempt " + attempt + "): " + snippet);
                }

                AppendLog(personaName + " response on attempt " + attempt + " did not contain executable commands. Retrying with a stricter reminder.");
                includeReminder = true;
            }

            return new List<AgentCommandDescription>();
        }

        private List<AgentCommandDescription> TryExtractCommandsFromPersonaResponse(ChatStreamingResult? result)
        {
            if (result == null || String.IsNullOrWhiteSpace(result.RawContent))
            {
                return new List<AgentCommandDescription>();
            }

            PersonaCommandResult? parsed = TryParsePersonaCommandResponse(result.RawContent);
            if (parsed?.Commands == null || parsed.Commands.Count == 0)
            {
                return new List<AgentCommandDescription>();
            }

            NormalizeCommandList(parsed.Commands);
            return parsed.Commands;
        }

        private PersonaCommandResult? TryParsePersonaCommandResponse(String? rawContent)
        {
            if (String.IsNullOrWhiteSpace(rawContent))
            {
                return null;
            }

            PersonaCommandResult? parsed = TryDeserializeJson<PersonaCommandResult>(rawContent);
            if (parsed != null)
            {
                return parsed;
            }

            PersonaCommandResult? lenient = TryParsePersonaCommandResponseLenient(rawContent);
            if (lenient != null)
            {
                return lenient;
            }

            String? extracted = ExtractJsonObject(rawContent);
            if (!String.IsNullOrWhiteSpace(extracted))
            {
                return TryParsePersonaCommandResponseLenient(extracted);
            }

            return null;
        }

        private PersonaCommandResult? TryParsePersonaCommandResponseLenient(String jsonText)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(jsonText);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                PersonaCommandResult response = new PersonaCommandResult
                {
                    Notes = ReadFlexibleString(root, "notes")
                };

                List<AgentCommandDescription> commands = new List<AgentCommandDescription>();
                if (root.TryGetProperty("commands", out JsonElement commandsElement) && commandsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement commandElement in commandsElement.EnumerateArray())
                    {
                        AgentCommandDescription? command = ConvertCommandElement(commandElement);
                        if (command != null)
                        {
                            commands.Add(command);
                        }
                    }
                }

                if (commands.Count == 0)
                {
                    return null;
                }

                response.Commands = commands;
                return response;
            }
            catch
            {
                return null;
            }
        }

        private static AgentCommandDescription? ConvertCommandElement(JsonElement commandElement)
        {
            if (commandElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            AgentCommandDescription command = new AgentCommandDescription
            {
                Id = ReadFlexibleString(commandElement, "id"),
                Description = ReadFlexibleString(commandElement, "description"),
                Executable = ReadFlexibleString(commandElement, "executable"),
                Arguments = ReadFlexibleArguments(commandElement),
                WorkingDirectory = ReadFlexibleString(commandElement, "workingDirectory"),
                DangerLevel = ReadFlexibleString(commandElement, "dangerLevel"),
                RunInBackground = ReadFlexibleBool(commandElement, "runInBackground"),
                MaxRunSeconds = ReadFlexibleInt(commandElement, "maxRunSeconds"),
                ExpectedExitCode = ReadFlexibleInt(commandElement, "expectedExitCode")
            };

            if (String.IsNullOrWhiteSpace(command.DangerLevel))
            {
                command.DangerLevel = "safe";
            }

            return command;
        }

        private static String? ReadFlexibleArguments(JsonElement container)
        {
            if (!container.TryGetProperty("arguments", out JsonElement argumentsElement))
            {
                return null;
            }

            return ConvertJsonValueToArgumentString(argumentsElement);
        }

        private static String? ReadFlexibleString(JsonElement container, String propertyName)
        {
            if (!container.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.GetRawText();
                case JsonValueKind.Array:
                    return ConvertJsonArrayToInlineString(value);
                case JsonValueKind.Object:
                    return value.GetRawText();
                default:
                    return null;
            }
        }

        private static String? ConvertJsonArrayToInlineString(JsonElement value)
        {
            StringBuilder builder = new StringBuilder();
            foreach (JsonElement element in value.EnumerateArray())
            {
                String? segment = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
                if (String.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(segment);
            }

            return builder.Length > 0 ? builder.ToString() : null;
        }

        private static String? ConvertJsonValueToArgumentString(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Array:
                    StringBuilder builder = new StringBuilder();
                    foreach (JsonElement element in value.EnumerateArray())
                    {
                        String? segment = ConvertJsonValueToArgumentString(element);
                        if (String.IsNullOrWhiteSpace(segment))
                        {
                            continue;
                        }

                        if (builder.Length > 0)
                        {
                            builder.Append(' ');
                        }

                        builder.Append(segment);
                    }
                    return builder.Length > 0 ? builder.ToString() : null;
                case JsonValueKind.Object:
                    return value.GetRawText();
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.GetRawText();
                default:
                    return null;
            }
        }

        private static Boolean? ReadFlexibleBool(JsonElement container, String propertyName)
        {
            if (!container.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.String:
                    if (Boolean.TryParse(value.GetString(), out Boolean parsedBool))
                    {
                        return parsedBool;
                    }
                    break;
                case JsonValueKind.Number:
                    if (value.TryGetInt32(out Int32 numericValue))
                    {
                        return numericValue != 0;
                    }
                    break;
            }

            return null;
        }

        private static Int32? ReadFlexibleInt(JsonElement container, String propertyName)
        {
            if (!container.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out Int32 numericValue))
                {
                    return numericValue;
                }
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                if (Int32.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out Int32 parsedValue))
                {
                    return parsedValue;
                }
            }

            return null;
        }

        private static void NormalizeCommandList(IList<AgentCommandDescription>? commands)
        {
            if (commands == null)
            {
                return;
            }

            for (Int32 index = 0; index < commands.Count; index++)
            {
                AgentCommandDescription? command = commands[index];
                if (command == null)
                {
                    continue;
                }

                command.Executable = TrimToNull(command.Executable)?.Trim();
                command.Arguments = TrimToNull(command.Arguments);
                command.WorkingDirectory = TrimToNull(command.WorkingDirectory);
                command.DangerLevel = TrimToNull(command.DangerLevel) ?? "safe";

                NormalizeExecutableAndArguments(command);
            }
        }

        private static void NormalizeExecutableAndArguments(AgentCommandDescription command)
        {
            if (String.IsNullOrWhiteSpace(command.Executable))
            {
                return;
            }

            String trimmedExecutable = command.Executable.Trim();

            if (!String.IsNullOrWhiteSpace(command.Arguments))
            {
                command.Executable = TrimOuterQuotes(trimmedExecutable);
                return;
            }

            String? normalizedExecutable = null;
            String remaining = String.Empty;

            if (trimmedExecutable.StartsWith("\"", StringComparison.Ordinal))
            {
                Int32 closingQuoteIndex = trimmedExecutable.IndexOf('"', 1);
                if (closingQuoteIndex > 1)
                {
                    normalizedExecutable = trimmedExecutable.Substring(1, closingQuoteIndex - 1);
                    remaining = trimmedExecutable.Substring(closingQuoteIndex + 1).Trim();
                }
            }

            if (normalizedExecutable == null)
            {
                Int32 firstSpaceIndex = trimmedExecutable.IndexOf(' ');
                if (firstSpaceIndex > 0)
                {
                    normalizedExecutable = trimmedExecutable.Substring(0, firstSpaceIndex);
                    remaining = trimmedExecutable.Substring(firstSpaceIndex + 1).Trim();
                }
                else
                {
                    normalizedExecutable = trimmedExecutable;
                }
            }

            command.Executable = TrimOuterQuotes(normalizedExecutable ?? trimmedExecutable);
            if (!String.IsNullOrWhiteSpace(remaining))
            {
                command.Arguments = remaining;
            }
        }

        private static String TrimOuterQuotes(String? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return String.Empty;
            }

            String trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed;
        }

        private static String? TrimToNull(String? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            String trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static String BuildResearcherSystemInstruction()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("You are the Researcher persona for a Windows-based autonomous coding agent. ");
            builder.Append("Identify any knowledge gaps and emit as many read-only commands (inspection, listing, searching) as needed to close them before engineering begins. ");
            builder.Append("Chain commands when required (for example, list files, then read the important ones) so downstream tasks receive complete context. ");
            builder.Append("Respond strictly with JSON matching {\"commands\":[Command],\"notes\":string}. ");
            builder.Append("Each Command object must include: id, description, executable, arguments, workingDirectory, dangerLevel, expectedExitCode. ");
            builder.Append("Prefer powershell.exe -NoProfile -Command scripts (use here-strings @\"...\"@) and built-in Windows tools. ");
            builder.Append("Never emit markdown fences or prose.");
            return builder.ToString();
        }

        private static String BuildEngineerSystemInstruction()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("You are the Engineer persona. Create idempotent Windows command sequences that may begin with inspections (reading files, listing directories) and then apply the required modifications to satisfy the task intent. ");
            builder.Append("Return JSON matching {\"commands\":[Command],\"notes\":string}. ");
            builder.Append("Command schema fields: id, description, executable, arguments, workingDirectory (relative to workspace), dangerLevel, expectedExitCode. ");
            builder.Append("Use powershell.exe -NoProfile -Command for file edits (Set-Content, Out-File, here-strings), dotnet, npm, etc., and feel free to chain multiple commands when the work requires several steps. ");
            builder.Append("The runtime treats exit code 0 as success (or the provided expectedExitCode). Include accurate exit codes so retries are meaningful. ");
            builder.Append("Provide every command necessary for a deterministic result—do not omit essential investigative steps—and never include markdown fences or commentary.");
            return builder.ToString();
        }

        private String BuildResearcherUserPrompt(SmartTask smartTask, String observationContext, Boolean includeReminder)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Research intent: " + (smartTask.Intent ?? "(unspecified)"));
            builder.AppendLine("Workspace root: " + (selectedWorkspacePath ?? "(not selected)"));
            builder.AppendLine();
            builder.AppendLine("Observation context:");
            builder.AppendLine(observationContext);
            builder.AppendLine();
            builder.AppendLine("Emit as many read-only commands as necessary to inspect files, list directories, and capture diagnostics so execution tasks have every fact they need.");
            builder.AppendLine();
            builder.AppendLine("Return only the JSON response defined in the system instruction.");
            if (includeReminder)
            {
                builder.AppendLine();
                builder.AppendLine("Reminder: Your previous output was invalid. Provide valid JSON with at least one read-only command.");
            }

            return builder.ToString();
        }

        private String BuildEngineerUserPrompt(SmartTask smartTask, String observationContext, Boolean includeReminder)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Execution intent: " + (smartTask.Intent ?? "(unspecified)"));
            builder.AppendLine("Workspace root: " + (selectedWorkspacePath ?? "(not selected)"));
            builder.AppendLine();
            builder.AppendLine("Observation context:");
            builder.AppendLine(observationContext);
            builder.AppendLine();
            builder.AppendLine("Commands may start by gathering the needed context (reading files, listing directories) before applying changes, and they must rely on accurate exit codes to signal success. Return only the JSON payload.");
            if (includeReminder)
            {
                builder.AppendLine();
                builder.AppendLine("Reminder: Provide valid JSON according to the schema with concrete executable commands.");
            }

            return builder.ToString();
        }

        private async Task<Boolean> ExecuteStrategyCommandsAsync(SmartTask smartTask, SmartTaskExecutionContext taskContext, IList<AgentCommandDescription>? commands, SmartTaskStrategyOptions strategy, CancellationToken cancellationToken)
        {
            String personaName = strategy == SmartTaskStrategyOptions.Research ? "Researcher" : "Engineer";

            if (commands != null)
            {
                NormalizeCommandList(commands);
                taskContext.AssociatedCommands = new List<AgentCommandDescription>(commands);
            }
            else
            {
                taskContext.AssociatedCommands = new List<AgentCommandDescription>();
            }

            if ((taskContext.AssociatedCommands == null || taskContext.AssociatedCommands.Count == 0) && !taskContext.RequiresCommandExecution)
            {
                AppendTaskLog(taskContext, personaName + " returned no commands; marking task as informational.");
                MarkSmartTaskCompleted(smartTask, taskContext, "No commands required");
                return true;
            }

            Int32 maxAttempts = taskContext.MaxRepairAttempts > 0 ? taskContext.MaxRepairAttempts : maxRepairAttemptsPerTask;
            if (maxAttempts <= 0)
            {
                maxAttempts = 1;
            }

            Boolean encounteredCommandlessFailure = false;
            Boolean encounteredExecutionBlock = false;

            for (Int32 attempt = 1; attempt <= maxAttempts; attempt++)
            {
                taskContext.AttemptCount = attempt;

                CommandRunResult? commandOutcome = null;
                IList<AgentCommandDescription>? commandsToRun = taskContext.AssociatedCommands;
                Boolean hasCommands = commandsToRun != null && commandsToRun.Count > 0;
                String commandInputDescription = BuildCommandInputDescription(commandsToRun);

                if (hasCommands)
                {
                    commandOutcome = await RunCommandsWithPolicyAndAnalystAsync(smartTask, taskContext, commandsToRun!, cancellationToken);
                    String outputText = BuildCommandAttemptOutputText(taskContext, commandOutcome, "(no command output captured)");
                    String resultText = BuildCommandAttemptResultText(commandOutcome, commandsMissing: false, informationalTask: false);
                    AppendAttemptTranscript(taskContext, attempt, personaName + " commands", commandInputDescription, outputText, resultText, appendToContext: true);
                    if (commandOutcome != null && commandOutcome.BlockReason == CommandBlockReason.OperatorDeclined)
                    {
                        AppendTaskLog(taskContext, "Command execution cancelled by operator; aborting task without retries.");
                        taskContext.CompletedAt = DateTime.Now;
                        taskContext.SetStatus(AssignmentTaskStatusOptions.Failed);
                        String failureDetail = "Operator cancelled command approval";
                        assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Failed, failureDetail);
                        CaptureAssignmentFailureReason(failureDetail);
                        return false;
                    }
                    if (commandOutcome != null && commandOutcome.Succeeded)
                    {
                        MarkSmartTaskCompleted(smartTask, taskContext, strategy == SmartTaskStrategyOptions.Research ? "Research completed" : "Execution completed");
                        return true;
                    }
                }
                else if (taskContext.RequiresCommandExecution)
                {
                    encounteredCommandlessFailure = true;
                    AppendTaskLog(taskContext, personaName + " did not provide executable commands for attempt " + attempt + ".");
                    String missingResult = BuildCommandAttemptResultText(null, commandsMissing: true, informationalTask: false);
                    AppendAttemptTranscript(taskContext, attempt, personaName + " commands", commandInputDescription, "(no output captured)", missingResult, appendToContext: true);
                    break;
                }
                else
                {
                    AppendTaskLog(taskContext, "No commands required for this informational task.");
                    String infoResult = BuildCommandAttemptResultText(null, commandsMissing: false, informationalTask: true);
                    AppendAttemptTranscript(taskContext, attempt, personaName + " commands", commandInputDescription, "(none)", infoResult, appendToContext: true);
                    MarkSmartTaskCompleted(smartTask, taskContext, "No commands required");
                    return true;
                }

                AppendTaskLog(taskContext, "Attempt " + attempt + " failed.");
                CaptureAttemptContextForRetry(taskContext, commandsToRun, commandOutcome);

                Boolean commandsAttemptedThisCycle = commandOutcome != null && commandOutcome.CommandsAttempted;
                if (!commandsAttemptedThisCycle)
                {
                    encounteredExecutionBlock = true;
                    String reason = commandOutcome?.FailureReason ?? "Command execution was blocked by prerequisites.";
                    AppendTaskLog(taskContext, "Execution halted before any command ran: " + reason);
                    if (attempt < maxAttempts)
                    {
                        AppendTaskLog(taskContext, "Retrying attempt " + (attempt + 1) + " of " + maxAttempts + " before invoking repair agent.");
                        continue;
                    }
                    
                }
                else if (attempt < maxAttempts)
                {
                    List<AgentCommandDescription> refreshedCommands = await RequestCommandsForRetryAsync(smartTask, taskContext, strategy, cancellationToken);
                    if (refreshedCommands.Count > 0)
                    {
                        taskContext.AssociatedCommands = new List<AgentCommandDescription>(refreshedCommands);
                        AppendTaskLog(taskContext, "Generated updated commands using previous attempt context; retrying attempt " + (attempt + 1) + ".");
                        continue;
                    }

                    AppendTaskLog(taskContext, "Retrying attempt " + (attempt + 1) + " with previous commands (persona returned no new plan).");
                    continue;
                }

                if (!hasCommands)
                {
                    break;
                }

                Boolean shouldRetry = false;

                StructuredRepairResult? repairResponse = await assignmentRuntimeService.RequestRepairResponseAsync(
                    taskContext,
                    selectedWorkspacePath,
                    trackSmartTask: true,
                    cancellationToken);
                if (repairResponse != null)
                {
                    Boolean repairApplied = await ApplyRepairAsync(taskContext, repairResponse, cancellationToken);
                    if (repairApplied)
                    {
                        String repairDecision = (repairResponse.RepairDecision ?? String.Empty).Trim().ToLowerInvariant();
                        if (repairDecision == "retry_with_new_commands")
                        {
                            if (taskContext.AssociatedCommands == null || taskContext.AssociatedCommands.Count == 0)
                            {
                                AppendTaskLog(taskContext, "Repair agent requested a retry but did not supply replacement commands.");
                            }
                            else
                            {
                                AppendTaskLog(taskContext, "Repair agent supplied replacement commands; retrying attempt " + (attempt + 1) + ".");
                                shouldRetry = true;
                            }
                        }
                        else if (repairDecision == "add_new_tasks")
                        {
                            taskContext.CompletedAt = DateTime.Now;
                            taskContext.SetStatus(AssignmentTaskStatusOptions.Succeeded);
                            assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Succeeded, "Repair delegated work to new tasks");
                            AppendTaskLog(taskContext, "Repair agent added new tasks; treating this task as satisfied.");
                            return true;
                        }
                        else if (repairDecision == "give_up")
                        {
                            AppendTaskLog(taskContext, "Repair agent opted to give up on this task.");
                        }
                        else if (taskContext.AssociatedCommands != null && taskContext.AssociatedCommands.Count > 0)
                        {
                            AppendTaskLog(taskContext, "Repair agent updated commands; retrying attempt " + (attempt + 1) + ".");
                            shouldRetry = true;
                        }
                    }
                }

                if (shouldRetry)
                {
                    maxAttempts = maxAttempts + 1;
                    continue;
                }

                break;
            }

            taskContext.CompletedAt = DateTime.Now;
            Boolean resolvedByFailureAgent = await assignmentRuntimeService.TryHandleTerminalTaskFailureAsync(
                taskContext,
                selectedWorkspacePath,
                CreateFailureResolutionCallbacks(),
                cancellationToken);
            if (resolvedByFailureAgent)
            {
                taskContext.SetStatus(AssignmentTaskStatusOptions.Succeeded);
                assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Succeeded, "Failure-resolution override");
                AppendTaskLog(taskContext, "Failure-resolution agent allowed continuation after command failures.");
                return true;
            }

            taskContext.SetStatus(AssignmentTaskStatusOptions.Failed);
            String failureStage;
            if (encounteredCommandlessFailure)
            {
                failureStage = personaName + " never produced executable commands";
            }
            else if (encounteredExecutionBlock)
            {
                failureStage = personaName + " commands were blocked before execution";
            }
            else
            {
                failureStage = personaName + " commands failed after repair attempts";
            }
            Boolean skippedRepair = assignmentController.TrySkipFailedRepairTask(
                smartTask,
                taskContext,
                failureStage,
                CreateRepairCallbacks());
            if (skippedRepair)
            {
                return true;
            }

            assignmentController.UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Failed, failureStage);
            AppendTaskLog(taskContext, failureStage + ".");
            CaptureAssignmentFailureReason(failureStage);
            return false;
        }

        private void CaptureAttemptContextForRetry(SmartTaskExecutionContext taskContext, IList<AgentCommandDescription>? commands, CommandRunResult? outcome)
        {
            if (taskContext == null)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Attempt " + taskContext.AttemptCount + " recap:");

            if (commands != null && commands.Count > 0)
            {
                builder.AppendLine("Commands executed:");
                Int32 limit = Math.Min(commands.Count, 6);
                for (Int32 index = 0; index < limit; index++)
                {
                    AgentCommandDescription? command = commands[index];
                    if (command == null)
                    {
                        continue;
                    }
                    builder.AppendLine("- " + BuildCommandDisplayText(command));
                }
                if (commands.Count > limit)
                {
                    builder.AppendLine("- ..." + (commands.Count - limit) + " additional command(s)");
                }
            }

            if (outcome != null && !String.IsNullOrWhiteSpace(outcome.FailureReason))
            {
                builder.AppendLine("Outcome: " + outcome.FailureReason);
            }

            if (!String.IsNullOrWhiteSpace(taskContext.LastResultText))
            {
                builder.AppendLine("Result snippet:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(taskContext.LastResultText, 800));
            }

            String summary = builder.ToString().Trim();
            if (summary.Length == 0)
            {
                return;
            }

            if (String.IsNullOrWhiteSpace(taskContext.TaskContext))
            {
                taskContext.TaskContext = summary;
            }
            else
            {
                taskContext.TaskContext = taskContext.TaskContext.TrimEnd() + Environment.NewLine + summary;
            }

            taskContext.AppendContextEntry(summary);
        }

        private async Task<List<AgentCommandDescription>> RequestCommandsForRetryAsync(
            SmartTask smartTask,
            SmartTaskExecutionContext taskContext,
            SmartTaskStrategyOptions strategy,
            CancellationToken cancellationToken)
        {
            if (smartTask == null)
            {
                return new List<AgentCommandDescription>();
            }

            String observationContext = BuildSmartTaskObservationContext(smartTask);
            Int32 plannedAttemptNumber = Math.Max(1, taskContext.AttemptCount + 1);

            if (strategy == SmartTaskStrategyOptions.Research)
            {
                return await RequestResearcherCommandsAsync(smartTask, taskContext, observationContext, cancellationToken, startWithReminder: true, plannedAttemptNumber: plannedAttemptNumber);
            }

            if (strategy == SmartTaskStrategyOptions.Execute)
            {
                return await RequestEngineerCommandsAsync(smartTask, taskContext, observationContext, cancellationToken, startWithReminder: true, plannedAttemptNumber: plannedAttemptNumber);
            }

            return new List<AgentCommandDescription>();
        }

        private async Task<CommandRunResult> RunCommandsWithPolicyAndAnalystAsync(
            SmartTask smartTask,
            SmartTaskExecutionContext taskContext,
            IList<AgentCommandDescription> commands,
            CancellationToken cancellationToken)
        {
            if (smartTask == null)
            {
                AppendTaskLog(taskContext, "Command execution aborted because the smart task context was missing.");
                return CommandRunResult.Blocked("Smart task context unavailable.", CommandBlockReason.MissingContext);
            }

            if (commands == null || commands.Count == 0)
            {
                return CommandRunResult.Success();
            }

            if (String.IsNullOrWhiteSpace(selectedWorkspacePath))
            {
                AppendTaskLog(taskContext, "Cannot run commands because no workspace was selected.");
                return CommandRunResult.Blocked("Workspace not selected.", CommandBlockReason.MissingWorkspace);
            }

            String workspaceRoot = selectedWorkspacePath!;
            StringBuilder combinedOutputBuilder = new StringBuilder();
            Boolean sawBackgroundCommand = false;

            for (Int32 index = 0; index < commands.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AgentCommandDescription? command = commands[index];
                if (command == null || String.IsNullOrWhiteSpace(command.Executable))
                {
                    AppendTaskLog(taskContext, "Command #" + (index + 1).ToString(CultureInfo.InvariantCulture) + " is missing an executable.");
                    return CommandRunResult.Blocked("Invalid command definition.", CommandBlockReason.InvalidCommandDefinition);
                }

                String commandDisplay = BuildCommandDisplayText(command);
                PolicyRiskLevelOptions riskLevel = MapDangerLevelToRisk(command.DangerLevel);
                Boolean allowedByPolicy = IsRiskAllowed(riskLevel, PolicyRiskToleranceOptions);
                Boolean isPotentiallyDangerous = riskLevel >= PolicyRiskLevelOptions.Medium;
                Boolean isCriticallyDangerous = riskLevel == PolicyRiskLevelOptions.High;

                Boolean policyOverrideGranted = false;
                if (!allowedByPolicy)
                {
                    policyOverrideGranted = await ConfirmPolicyOverrideAsync(commandDisplay, riskLevel);
                    if (!policyOverrideGranted)
                    {
                        AppendTaskLog(taskContext, "Command skipped because it exceeded the configured risk tolerance: " + commandDisplay);
                        RecordPolicyDecision(commandDisplay, false, "Operator declined override", riskLevel);
                        return CommandRunResult.Blocked("Command cancelled by operator.", CommandBlockReason.OperatorDeclined);
                    }

                    AppendTaskLog(taskContext, "Operator approved a policy override for command: " + commandDisplay);
                    RecordPolicyDecision(commandDisplay, true, "Operator override", riskLevel);
                }
                else
                {
                    RecordPolicyDecision(commandDisplay, true, "Allowed", riskLevel);
                }

                if (!policyOverrideGranted && !await ConfirmCommandIfRequired(commandDisplay, isPotentiallyDangerous, isCriticallyDangerous))
                {
                    AppendTaskLog(taskContext, "Command cancelled by operator: " + commandDisplay);
                    return CommandRunResult.Blocked("Command cancelled by operator.", CommandBlockReason.OperatorDeclined);
                }

                String workingDirectory = ResolveWorkingDirectory(workspaceRoot, command.WorkingDirectory);
                if (!Directory.Exists(workingDirectory))
                {
                    AppendTaskLog(taskContext, "Working directory does not exist: " + workingDirectory);
                    CaptureCommandFailureResult(taskContext, commandDisplay, "Working directory does not exist: " + workingDirectory, null);
                    return CommandRunResult.Failure("Working directory missing: " + workingDirectory);
                }

                AppendTaskLog(taskContext, "Executing command: " + commandDisplay);
                CommandExecutionResult executionResult = await commandExecutionService.RunCommandAsync(
                    taskContext,
                    command,
                    command.Executable!,
                    command.Arguments ?? String.Empty,
                    workingDirectory,
                    cancellationToken);

                AppendCommandOutput(taskContext, command, executionResult);

                if (executionResult.TimedOut)
                {
                    AppendTaskLog(taskContext, "Command timed out before completing.");
                    CaptureCommandFailureResult(taskContext, commandDisplay, "Command timed out before completing.", executionResult);
                    return CommandRunResult.Failure("Command timed out before completing.");
                }

                if (!executionResult.RanInBackground)
                {
                    Int32 expectedExitCode = command.ExpectedExitCode ?? 0;
                    if (executionResult.ExitCode != expectedExitCode)
                    {
                        AppendTaskLog(taskContext, "Command failed with exit code " + executionResult.ExitCode + " (expected " + expectedExitCode + ").");
                        String exitDetail = "Unexpected exit code " + executionResult.ExitCode + " (expected " + expectedExitCode + ").";
                        CaptureCommandFailureResult(taskContext, commandDisplay, exitDetail, executionResult);
                        return CommandRunResult.Failure(exitDetail);
                    }

                    if (!String.IsNullOrWhiteSpace(executionResult.StandardOutputText))
                    {
                        combinedOutputBuilder.AppendLine("=== " + commandDisplay + " stdout ===");
                        combinedOutputBuilder.AppendLine(executionResult.StandardOutputText);
                    }

                    if (!String.IsNullOrWhiteSpace(executionResult.StandardErrorText))
                    {
                        combinedOutputBuilder.AppendLine("=== " + commandDisplay + " stderr ===");
                        combinedOutputBuilder.AppendLine(executionResult.StandardErrorText);
                    }

                    Boolean recordedWorkspaceChange = assignmentRuntimeService.CaptureWorkspaceChangesAsSemanticFacts(smartTask, workspaceRoot, commandDisplay);
                    if (recordedWorkspaceChange)
                    {
                        RefreshAceBlackboardViews();
                    }
                }
                else
                {
                    sawBackgroundCommand = true;
                }
            }

            if (combinedOutputBuilder.Length > 0)
            {
                String combinedOutput = combinedOutputBuilder.ToString();
                taskContext.LastResultText = TextUtilityService.BuildCompactSnippet(combinedOutput, 6000);
                AnalystExtractionResult? extraction = await RequestAnalystExtractionAsync(smartTask, combinedOutput, cancellationToken);
                if (extraction != null)
                {
                    ApplyAnalystExtraction(smartTask, extraction);
                }
            }
            else if (sawBackgroundCommand)
            {
                taskContext.LastResultText = UiText(UiCatalogKeys.TextBackgroundCommandRunning);
            }

            return CommandRunResult.Success();
        }

        private static String BuildCommandDisplayText(AgentCommandDescription command)
        {
            String executable = command.Executable ?? String.Empty;
            String arguments = command.Arguments ?? String.Empty;
            return String.IsNullOrWhiteSpace(arguments) ? executable : executable + " " + arguments;
        }

        private static PolicyRiskLevelOptions MapDangerLevelToRisk(String? dangerLevel)
        {
            if (String.IsNullOrWhiteSpace(dangerLevel))
            {
                return PolicyRiskLevelOptions.Low;
            }

            String normalized = dangerLevel.Trim().ToLowerInvariant();
            return normalized switch
            {
                "safe" => PolicyRiskLevelOptions.Low,
                "dangerous" => PolicyRiskLevelOptions.Medium,
                "critical" => PolicyRiskLevelOptions.High,
                _ => PolicyRiskLevelOptions.Medium
            };
        }

        private void RecordPolicyDecision(String commandText, Boolean allowed, String reason, PolicyRiskLevelOptions riskLevel)
        {
            PolicyDecisionItem decision = new PolicyDecisionItem
            {
                TimestampUtc = DateTime.UtcNow,
                Command = commandText,
                Allowed = allowed,
                Reason = reason,
                Risk = FormatRiskLabelForDisplay(riskLevel)
            };

            InvokeOnUiThread(() => policyDecisionItems.Add(decision));
        }

        private static String ResolveWorkingDirectory(String workspaceRoot, String? workingDirectory)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory))
            {
                return workspaceRoot;
            }

            if (Path.IsPathRooted(workingDirectory))
            {
                return workingDirectory;
            }

            return BuildWorkspacePath(workspaceRoot, workingDirectory);
        }

        private static String BuildWorkspacePath(String workspaceRoot, String relativePath)
        {
            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            String combined = Path.Combine(workspaceRoot, relativePath);
            return Path.GetFullPath(combined);
        }

        private static Boolean IsRiskAllowed(PolicyRiskLevelOptions riskLevel, PolicyRiskToleranceOptions tolerance)
        {
            Int32 riskRank = riskLevel switch
            {
                PolicyRiskLevelOptions.None => 1,
                PolicyRiskLevelOptions.Low => 2,
                PolicyRiskLevelOptions.Medium => 3,
                PolicyRiskLevelOptions.High => 4,
                _ => 0
            };

            Int32 toleranceRank = tolerance switch
            {
                PolicyRiskToleranceOptions.None => 1,
                PolicyRiskToleranceOptions.LowOnly => 2,
                PolicyRiskToleranceOptions.UpToMedium => 3,
                _ => 4
            };

            return riskRank <= toleranceRank;
        }

        private static String DescribePolicyRiskLevelOptions(PolicyRiskLevelOptions riskLevel)
        {
            return riskLevel switch
            {
                PolicyRiskLevelOptions.None => "no risk",
                PolicyRiskLevelOptions.Low => "low risk",
                PolicyRiskLevelOptions.Medium => "medium risk",
                PolicyRiskLevelOptions.High => "high risk",
                _ => "unknown risk"
            };
        }

        private static String DescribePolicyRiskToleranceOptions(PolicyRiskToleranceOptions tolerance)
        {
            return tolerance switch
            {
                PolicyRiskToleranceOptions.None => "no risk",
                PolicyRiskToleranceOptions.LowOnly => "low risk",
                PolicyRiskToleranceOptions.UpToMedium => "medium risk",
                _ => "high risk"
            };
        }

        private static String FormatRiskLabelForDisplay(PolicyRiskLevelOptions riskLevel)
        {
            String label = DescribePolicyRiskLevelOptions(riskLevel);
            return CapitalizeLabel(label);
        }

        private static String FormatRiskToleranceForDisplay(PolicyRiskToleranceOptions tolerance)
        {
            String label = DescribePolicyRiskToleranceOptions(tolerance);
            return CapitalizeLabel(label);
        }

        private static String CapitalizeLabel(String label)
        {
            if (String.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            if (label.Length == 1)
            {
                return label.ToUpper(CultureInfo.CurrentCulture);
            }

            Char leading = Char.ToUpper(label[0], CultureInfo.CurrentCulture);
            return leading + label.Substring(1);
        }

        private async Task<AnalystExtractionResult?> RequestAnalystExtractionAsync(SmartTask smartTask, String combinedOutput, CancellationToken cancellationToken)
        {
            String systemInstruction =
                "You are the Analyst persona. Extract semantic blackboard facts from the command output so downstream tasks inherit essential context." +
                "Respond with JSON: {\"facts\":[{\"summary\":string,\"detail\":string,\"file\":string|null}], \"summary\": string}." +
                "Each fact should describe a concrete file, implementation detail, or constraint future tasks need.";

            StringBuilder userBuilder = new StringBuilder();
            userBuilder.AppendLine("Task intent: " + (smartTask.Intent ?? "(unspecified)"));
            userBuilder.AppendLine("Combined output:");
            userBuilder.AppendLine(TextUtilityService.BuildCompactSnippet(combinedOutput, 4000));
            userBuilder.AppendLine();
            userBuilder.AppendLine("Respond ONLY with the JSON object.");

            ChatCompletionMessage[] messages = chatCompletionService.BuildPersonaMessages(systemInstruction, userBuilder.ToString());
            ChatStreamingResult? result = await chatCompletionService.SendChatCompletionRequestAsync(messages, UsageChannelOptions.General, "Analyst", cancellationToken);
            if (result == null || String.IsNullOrWhiteSpace(result.RawContent))
            {
                return null;
            }

            AnalystExtractionResult? parsed = TryDeserializeJson<AnalystExtractionResult>(result.RawContent);
            if (parsed == null)
            {
                String? extracted = ExtractJsonObject(result.RawContent);
                if (!String.IsNullOrWhiteSpace(extracted))
                {
                    parsed = TryDeserializeJson<AnalystExtractionResult>(extracted);
                }
            }

            return parsed;
        }

        private void ApplyAnalystExtraction(SmartTask smartTask, AnalystExtractionResult extraction)
        {
            Boolean recordedFact = false;

            if (extraction.Facts != null)
            {
                foreach (AnalystFactResult fact in extraction.Facts)
                {
                    String? summary = fact.GetSummary();
                    if (String.IsNullOrWhiteSpace(summary))
                    {
                        continue;
                    }

                    String detail = fact.GetDetail() ?? "(detail omitted)";
                    assignmentController.RecordSemanticFact(smartTask, summary!, detail, fact.File);
                    recordedFact = true;
                }
            }

            if (!String.IsNullOrWhiteSpace(extraction.Summary))
            {
                String sourceLabel = smartTask.Intent ?? smartTask.Id ?? "Task";
                String summaryTitle = recordedFact ? "Summary: " + sourceLabel : sourceLabel + " summary";
                assignmentController.RecordSemanticFact(smartTask, summaryTitle, extraction.Summary!);
                recordedFact = true;
            }

            if (!recordedFact)
            {
                SmartTaskExecutionContext? context = smartTask.BoundAssignmentTask;
                if (context != null && !String.IsNullOrWhiteSpace(context.LastResultText))
                {
                    String title = (smartTask.Intent ?? smartTask.Id ?? "Task") + " output";
                    assignmentController.RecordSemanticFact(smartTask, title, TextUtilityService.BuildCompactSnippet(context.LastResultText, 400));
                }
            }

            RefreshAceBlackboardViews();
        }


        private void OnAssignmentPromptTextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateQuickStartOverlayStatus();
        }

        private void SetQuickStartStatus(TextBlock? glyphBlock, TextBlock? statusTextBlock, Boolean isComplete, String detailText)
        {
            if (glyphBlock != null)
            {
                glyphBlock.Text = BuildStatusGlyph(isComplete);
                glyphBlock.Foreground = isComplete ? new SolidColorBrush(Color.FromRgb(0x00, 0xB2, 0x94)) : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            }

            if (statusTextBlock != null)
            {
                statusTextBlock.Text = detailText;
            }
        }

        private static String BuildStatusGlyph(Boolean isComplete)
        {
            return isComplete ? "\u2714" : "\u25CB";
        }

        private async void OnSelectWorkspaceClick(object? sender, RoutedEventArgs e)
        {
            if (StorageProvider == null)
            {
                return;
            }

            FolderPickerOpenOptions pickerOptions = new FolderPickerOpenOptions
            {
                AllowMultiple = false
            };

            if (!String.IsNullOrWhiteSpace(selectedWorkspacePath) && Directory.Exists(selectedWorkspacePath))
            {
                IStorageFolder? startFolder = await StorageProvider.TryGetFolderFromPathAsync(selectedWorkspacePath);
                if (startFolder != null)
                {
                    pickerOptions.SuggestedStartLocation = startFolder;
                }
            }

            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(pickerOptions);
            String? folderName = folders.FirstOrDefault()?.TryGetLocalPath();

            if (!String.IsNullOrWhiteSpace(folderName))
            {
                selectedWorkspacePath = folderName;
                if (WorkspacePathTextBox != null)
                {
                    WorkspacePathTextBox.Text = selectedWorkspacePath;
                }
                workspaceStateTracker.Reset(selectedWorkspacePath);
                workspaceFileItems.Clear();
                SynchronizeWorkspaceAutoFilesWithCheckbox();
            }
            else if (String.IsNullOrWhiteSpace(selectedWorkspacePath))
            {
                selectedWorkspacePath = null;
                if (WorkspacePathTextBox != null)
                {
                    WorkspacePathTextBox.Text = String.Empty;
                }
                workspaceStateTracker.Reset(null);
                workspaceFileItems.Clear();
            }

            UpdateUiState();
        }

        private async void OnLoadApiKeyClick(object? sender, RoutedEventArgs e)
        {
            if (assignmentController.IsAssignmentRunning)
            {
                AppendLog("Cannot change API key while an assignment is running.");
                return;
            }

            String apiKeyCandidate = OpenAiApiKeyPasswordBox?.Text ?? String.Empty;
            apiKeyCandidate = apiKeyCandidate.Trim();

            if (String.IsNullOrWhiteSpace(apiKeyCandidate))
            {
                assignmentController.ClearOpenAiConfiguration();
                ResetModelComboBox();
                AppendLog("OpenAI API key cleared.");
                UpdateUiState();
                return;
            }

            AppendLog("OpenAI API key loaded from inline form.");

            IReadOnlyList<String> modelIds = await assignmentController.LoadOpenAiModelsAsync(apiKeyCandidate, CancellationToken.None);
            PopulateModelComboBox(modelIds, assignmentController.SelectedOpenAiModelId);
            UpdateUiState();
        }

        private void ResetModelComboBox()
        {
            if (ModelComboBox == null)
            {
                return;
            }

            ModelComboBox.ItemsSource = null;
            ModelComboBox.Items.Clear();
            ModelComboBox.SelectedItem = null;
        }

        private void PopulateModelComboBox(IReadOnlyList<String> modelIds, String? selectedModelId)
        {
            if (ModelComboBox == null)
            {
                return;
            }

            ModelComboBox.ItemsSource = null;
            ModelComboBox.Items.Clear();

            if (modelIds != null)
            {
                for (Int32 index = 0; index < modelIds.Count; index++)
                {
                    ModelComboBox.Items.Add(modelIds[index]);
                }
            }

            if (!String.IsNullOrWhiteSpace(selectedModelId))
            {
                ModelComboBox.SelectedItem = selectedModelId;
            }
            else if (ModelComboBox.Items.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
            }
        }

        private void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ModelComboBox == null)
            {
                return;
            }

            String? selectedModelId = ModelComboBox.SelectedItem as String;
            Boolean selectionApplied = assignmentController.TrySelectOpenAiModel(selectedModelId);
            if (selectionApplied && !String.IsNullOrWhiteSpace(selectedModelId))
            {
                AppendLog("Selected OpenAI model: " + selectedModelId);
            }
            else if (!selectionApplied && !String.IsNullOrWhiteSpace(selectedModelId))
            {
                AppendLog("Requested OpenAI model '" + selectedModelId + "' is not currently available.");
            }

            UpdateUiState();
        }

        private void OnRecursiveExitBiasSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            Double normalized = e.NewValue / 100.0;
            if (Double.IsNaN(normalized))
            {
                normalized = assignmentController.RecursionExitBiasBase;
            }

            if (normalized < 0.0)
            {
                normalized = 0.0;
            }
            else if (normalized > 1.0)
            {
                normalized = 1.0;
            }

            assignmentController.UpdateRecursionExitBiasBase(normalized);
            UpdateRecursiveExitBiasText();
        }

        private void OnRecursiveExitBiasIncrementSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            Double normalized = e.NewValue / 100.0;
            if (Double.IsNaN(normalized))
            {
                normalized = assignmentController.RecursionExitBiasIncrement;
            }

            if (normalized < 0.0)
            {
                normalized = 0.0;
            }
            else if (normalized > 1.0)
            {
                normalized = 1.0;
            }

            assignmentController.UpdateRecursionExitBiasIncrement(normalized);
            UpdateRecursiveExitBiasText();
        }

        private void UpdateRecursiveExitBiasText()
        {
            if (RecursiveExitBiasValueTextBlock != null)
            {
                Double percent = assignmentController.RecursionExitBiasBase * 100.0;
                RecursiveExitBiasValueTextBlock.Text = percent.ToString("0") + "%";
            }

            if (RecursiveExitBiasIncrementValueTextBlock != null)
            {
                Double percent = assignmentController.RecursionExitBiasIncrement * 100.0;
                RecursiveExitBiasIncrementValueTextBlock.Text = percent.ToString("0") + "%";
            }
        }

        private async void OnStartAssignmentClick(object? sender, RoutedEventArgs e)
        {
            Boolean launchStateLocked = false;
            try
            {
                if (assignmentController.IsAssignmentRunning)
                {
                    AppendLog("Assignment already running; ignoring Start request.");
                    return;
                }

                if (String.IsNullOrWhiteSpace(selectedWorkspacePath))
                {
                    AppendLog("Cannot start assignment: workspace folder is not selected.");
                    await DialogService.ShowInfoAsync(this, "Workspace Required", "Select a workspace folder before starting an assignment.");
                    return;
                }

                String? runApiKey = CurrentOpenAiApiKey;
                if (String.IsNullOrWhiteSpace(runApiKey))
                {
                    AppendLog("Cannot start assignment: OpenAI API key is not configured.");
                    await DialogService.ShowInfoAsync(this, "OpenAI API Key Required", "Load an OpenAI API key before starting an assignment.");
                    return;
                }

                String? selectedModelId = CurrentOpenAiModelId;
                if (String.IsNullOrWhiteSpace(selectedModelId))
                {
                    AppendLog("Cannot start assignment: no OpenAI model selected.");
                    await DialogService.ShowInfoAsync(this, "Model Required", "Select an OpenAI model before starting an assignment.");
                    return;
                }

                String? assignmentTitle = AssignmentTitleTextBox?.Text;
                String? assignmentPrompt = AssignmentPromptTextBox?.Text;

                if (String.IsNullOrWhiteSpace(assignmentPrompt))
                {
                    AppendLog("Cannot start assignment: prompt is empty.");
                    await DialogService.ShowInfoAsync(this, "Prompt Required", "Enter an assignment prompt before starting.");
                    return;
                }

                if (!Directory.Exists(selectedWorkspacePath))
                {
                    AppendLog("Cannot start assignment: workspace directory does not exist.");
                    await DialogService.ShowInfoAsync(this, "Workspace Error", "The selected workspace folder does not exist.");
                    return;
                }

                if (!hasQuickStartChecklistBeenDismissed)
                {
                    hasQuickStartChecklistBeenDismissed = true;
                    UpdateQuickStartOverlayStatus();
                }

                isAssignmentLaunchPending = true;
                launchStateLocked = true;
                UpdateUiState();

                commandExecutionService.StopAllBackgroundCommands("Starting new assignment");

                ResetAssignmentStateForNewRun();

                assignmentController.UpdateAssignmentPausedState(false);
                if (PauseAssignmentButton != null)
                {
                    PauseAssignmentButton.Content = "Pause";
                }

                SynchronizeWorkspaceAutoFilesWithCheckbox();

                String? workspaceContext = null;
                if (workspaceFileItems.Count > 0)
                {
                    IReadOnlyList<WorkspaceFileItem> contextFiles = CreateWorkspaceFileSnapshot();
                    RegisterWorkspaceFilesAsSemanticFacts(contextFiles);
                }

                if (!isAssignmentLaunchPending)
                {
                    AppendLog("Assignment launch cancelled before workspace context completed.");
                    return;
                }

                assignmentController.CancelAssignmentRun();
                assignmentController.ClearAssignmentCancellationToken();

                String agentDirectoryPath = Path.Combine(selectedWorkspacePath, ".agent");
                try
                {
                    Directory.CreateDirectory(agentDirectoryPath);
                }
                catch (Exception exception)
                {
                    AppendLog("Failed to create .agent directory: " + exception.Message);
                    return;
                }

                String runIdentifier = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                String assignmentRunDirectoryPath = Path.Combine(agentDirectoryPath, runIdentifier);
                try
                {
                    Directory.CreateDirectory(assignmentRunDirectoryPath);
                }
                catch (Exception exception)
                {
                    AppendLog("Failed to create assignment run directory: " + exception.Message);
                    return;
                }

                CancellationToken cancellationToken = assignmentController.PrepareAssignmentCancellationToken();

                await GenerateAssignmentSuccessHeuristicsAsync(assignmentTitle, assignmentPrompt!, workspaceContext, cancellationToken);

                if (!isAssignmentLaunchPending)
                {
                    AppendLog("Assignment launch cancelled before execution start.");
                    return;
                }

                assignmentController.UpdateAssignmentRunningState(true, "Running");
                isAssignmentLaunchPending = false;
                launchStateLocked = false;
                UpdateUiState();

                assignmentRootSmartTask = assignmentController.EnsureAssignmentRootSmartTask(assignmentTitle, assignmentPrompt!);
                supervisorTabEnabled = true;
                UpdateSupervisorTabState();
                SwitchToSupervisorTab();

                UpdateUsageUi();
                AppendLog("Starting assignment using model '" + selectedModelId + "'.");
                UpdateUiState();

                Boolean runResult = false;

                try
                {
                    AppendLog("Supervisor: starting recursive cognitive game loop...");

                    runResult = await RunRecursiveSupervisorAsync(assignmentTitle, assignmentPrompt!, workspaceContext, cancellationToken);

                    if (AssignmentStatusText == "Running")
                    {
                        assignmentController.UpdateAssignmentStatus(runResult ? "Completed" : "Failed");
                        if (!runResult && String.IsNullOrWhiteSpace(AssignmentFailureReason))
                        {
                            CaptureAssignmentFailureReason("one or more tasks failed to complete successfully");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    assignmentController.UpdateAssignmentStatus("Cancelled");
                    if (String.IsNullOrWhiteSpace(AssignmentFailureReason))
                    {
                        CaptureAssignmentFailureReason("the run was cancelled");
                    }
                    AppendLog("Assignment run cancelled.");
                }
                catch (Exception exception)
                {
                    assignmentController.UpdateAssignmentStatus("Failed");
                    CaptureAssignmentFailureReason("an unexpected error occurred: " + exception.Message);
                    AppendLog("Assignment run failed: " + exception.Message);
                    ShowAssignmentFailedDialog(
                        "Assignment failed",
                        "The assignment failed due to an unexpected error. Please review the System Log for more details.",
                        exception.Message);
                }
                finally
                {
                    assignmentController.UpdateAssignmentRunningState(false, AssignmentStatusText);
                    assignmentController.UpdateAssignmentPausedState(false);
                    if (PauseAssignmentButton != null)
                    {
                        PauseAssignmentButton.Content = "Pause";
                    }
                    UpdateUiState();

                    String capturedRunDirectoryPath = assignmentRunDirectoryPath;
                    String? capturedAssignmentTitle = assignmentTitle;
                    String? capturedAssignmentPrompt = assignmentPrompt;
                    String? capturedWorkspaceContext = workspaceContext;
                    _ = Task.Run(() => SaveAssignmentArtifactsSafely(capturedRunDirectoryPath, capturedAssignmentTitle, capturedAssignmentPrompt, capturedWorkspaceContext, null));

                    assignmentController.ClearAssignmentCancellationToken();

                    await EvaluateAssignmentResultAsync(CancellationToken.None);

                    assignmentController.FinalizeAssignmentRootSmartTask();

                    Boolean assignmentCancelled = String.Equals(AssignmentStatusText, "Cancelled", StringComparison.OrdinalIgnoreCase);
                    Boolean assignmentFailed = String.Equals(AssignmentStatusText, "Failed", StringComparison.OrdinalIgnoreCase);
                    if (assignmentCancelled || assignmentFailed)
                    {
                        String finalizeReason = BuildDanglingTaskReason(assignmentCancelled);
                        FinalizeDanglingTasks(finalizeReason, assignmentCancelled);
                    }

                    hasQuickStartChecklistBeenDismissed = false;
                    UpdateQuickStartOverlayStatus();
                }
            }
            catch (OperationCanceledException oce)
            {
                AppendLog("Assignment start cancelled before launch: " + oce.Message);
                assignmentController.UpdateAssignmentStatus("Cancelled");
                if (String.IsNullOrWhiteSpace(AssignmentFailureReason))
                {
                    CaptureAssignmentFailureReason("the run was cancelled before it could start");
                }
                assignmentController.ClearAssignmentCancellationToken();
                assignmentController.UpdateAssignmentRunningState(false, AssignmentStatusText);
                UpdateUiState();
            }
            catch (Exception ex)
            {
                AppendLog("Critical error starting assignment: " + ex);
                CaptureAssignmentFailureReason("a startup error occurred: " + ex.Message);
                await DialogService.ShowInfoAsync(this, "Error", "Critical error starting assignment: " + ex.Message);
                assignmentController.UpdateAssignmentRunningState(false, AssignmentStatusText);
                UpdateUiState();
            }
            finally
            {
                if (launchStateLocked)
                {
                    isAssignmentLaunchPending = false;
                    UpdateUiState();
                }
            }
        }

        private async Task<StructuredAgentResult?> BuildRecursivePlannerPlanAsync(String? assignmentTitle, String assignmentPrompt, String? workspaceContext, CancellationToken cancellationToken)
        {
            (Double WorkRetentionFraction, Double DelegationFraction) rootBudget = assignmentController.CalculateWorkBudgetForDepth(0);
            PlannerRequestContext rootContext = PlannerRequestContext.CreateRoot(assignmentTitle, assignmentPrompt, workspaceContext, rootBudget.WorkRetentionFraction, rootBudget.DelegationFraction);
            Double rootBias = assignmentController.GetExecutionBiasForDepth(0);
            Boolean rootHasDelegationBudget = WorkBudgetSettings.HasMeaningfulDelegation(rootBudget.DelegationFraction);
            rootContext.AllowDecomposition = rootBias < 1.0 && rootHasDelegationBudget;
            Stack<PlannerRequestContext> pendingContexts = new Stack<PlannerRequestContext>();
            pendingContexts.Push(rootContext);

            List<AgentPlannedTask> rootTasks = new List<AgentPlannedTask>();
            StructuredAgentResult? rootResponse = null;
            Int32 plannerCallCount = 0;
            List<(String Scope, String RawContent)> plannerCallLog = new List<(String Scope, String RawContent)>();

            while (pendingContexts.Count > 0)
            {
                PlannerRequestContext context = pendingContexts.Pop();
                plannerCallCount = plannerCallCount + 1;

                if (plannerCallCount > RecursivePlannerMaxRequests)
                {
                    AppendLog("Planner aborted: exceeded recursive planning request limit of " + RecursivePlannerMaxRequests + ".");
                    return null;
                }

                String scopeDescription = BuildPlannerScopeDescription(context, plannerCallCount);
                String overlayTitle = context.InvocationKind == PlannerInvocationOptions.AssignmentRoot
                    ? "Planning assignment"
                    : "Expanding " + (context.ParentTask?.Label ?? context.ParentTask?.Id ?? "task");
                AppendLog("Planner: " + overlayTitle + " (call " + plannerCallCount + ", depth " + context.Depth + ")");

                StructuredAgentResult? response = await assignmentRuntimeService.RequestPlannerResponseAsync(context, cancellationToken);
                if (response == null)
                {
                    return null;
                }

                String rawContent = response.RawContent ?? String.Empty;
                plannerCallLog.Add((scopeDescription, rawContent));

                if (rootResponse == null && context.InvocationKind == PlannerInvocationOptions.AssignmentRoot)
                {
                    rootResponse = response;
                }

                if (response.Tasks == null || response.Tasks.Count == 0)
                {
                    continue;
                }

                EnforceManualPlanningWhenDelegationDisabled(context, response);

                for (Int32 index = 0; index < response.Tasks.Count; index++)
                {
                    AgentPlannedTask plannedTask = response.Tasks[index];
                    EnsurePlannerTaskHasId(plannedTask);

                    if (context.ParentTask == null)
                    {
                        rootTasks.Add(plannedTask);
                    }
                    else
                    {
                        AddChildPlannerTask(context.ParentTask, plannedTask);
                    }
                }

                for (Int32 index = response.Tasks.Count - 1; index >= 0; index--)
                {
                    AgentPlannedTask plannedTask = response.Tasks[index];
                    EvaluateTaskForRecursivePlanning(plannedTask, context, pendingContexts);
                }
            }

            StructuredAgentResult aggregatedResponse = new StructuredAgentResult();
            aggregatedResponse.Answer = rootResponse?.Answer;
            aggregatedResponse.Explanation = rootResponse?.Explanation;
            EnforceSequentialExecutionRules(rootTasks);
            aggregatedResponse.Tasks = rootTasks;
            aggregatedResponse.IsStructured = true;
            aggregatedResponse.RawContent = BuildPlannerRawTranscript(plannerCallLog);
            return aggregatedResponse;
        }

        private static String BuildPlannerScopeDescription(PlannerRequestContext context, Int32 plannerCallIndex)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Call ");
            builder.Append(plannerCallIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(" (Depth ");
            builder.Append(context.Depth.ToString(CultureInfo.InvariantCulture));
            builder.Append(") Scope: ");

            if (context.InvocationKind == PlannerInvocationOptions.AssignmentRoot)
            {
                builder.Append("Assignment root");
            }
            else
            {
                String label = context.ParentTask?.Label ?? context.ParentTask?.Id ?? "(unnamed task)";
                builder.Append("Expand task '");
                builder.Append(label);
                builder.Append("'");
            }

            builder.Append(" | Retain ");
            builder.Append(FormatPercent(context.WorkRetentionFraction));
            builder.Append(", delegate up to ");
            builder.Append(FormatPercent(context.DelegationFraction));

            return builder.ToString();
        }

        private static String BuildPlannerRawTranscript(List<(String Scope, String RawContent)> plannerCallLog)
        {
            if (plannerCallLog.Count == 0)
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (Int32 index = 0; index < plannerCallLog.Count; index++)
            {
                (String Scope, String RawContent) entry = plannerCallLog[index];
                builder.AppendLine("=== Planner Call " + (index + 1).ToString(CultureInfo.InvariantCulture) + " ===");
                builder.AppendLine(entry.Scope);
                builder.AppendLine(entry.RawContent);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static void EnsurePlannerTaskHasId(AgentPlannedTask task)
        {
            if (!String.IsNullOrWhiteSpace(task.Id))
            {
                return;
            }

            task.Id = "task-" + Guid.NewGuid().ToString("N");
        }

        private static void AddChildPlannerTask(AgentPlannedTask parentTask, AgentPlannedTask childTask)
        {
            if (parentTask.Subtasks == null)
            {
                parentTask.Subtasks = new List<AgentPlannedTask>();
            }

            parentTask.Subtasks.Add(childTask);
            EnsureParentDependency(childTask, parentTask);
        }

        private static void EnsureParentDependency(AgentPlannedTask childTask, AgentPlannedTask parentTask)
        {
            if (String.IsNullOrWhiteSpace(parentTask.Id))
            {
                return;
            }

            if (childTask.Dependencies == null)
            {
                childTask.Dependencies = new List<String>();
            }

            Boolean alreadyIncluded = false;
            for (Int32 index = 0; index < childTask.Dependencies.Count; index++)
            {
                String dependency = childTask.Dependencies[index];
                if (String.Equals(dependency, parentTask.Id, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyIncluded = true;
                    break;
                }
            }

            if (!alreadyIncluded)
            {
                childTask.Dependencies.Add(parentTask.Id);
            }
        }

        private void EnforceSequentialExecutionRules(List<AgentPlannedTask> tasks)
        {
            if (tasks == null || tasks.Count == 0)
            {
                return;
            }

            AgentPlannedTask? previousSibling = null;
            for (Int32 index = 0; index < tasks.Count; index++)
            {
                AgentPlannedTask current = tasks[index];
                EnsurePlannerTaskHasId(current);

                if (previousSibling != null && !String.IsNullOrWhiteSpace(previousSibling.Id))
                {
                    EnsureDependencyReference(current, previousSibling.Id!);
                }

                if (current.Subtasks != null && current.Subtasks.Count > 0)
                {
                    for (Int32 childIndex = 0; childIndex < current.Subtasks.Count; childIndex++)
                    {
                        AgentPlannedTask childTask = current.Subtasks[childIndex];
                        EnsurePlannerTaskHasId(childTask);
                        EnsureParentDependency(childTask, current);
                    }

                    EnforceSequentialExecutionRules(current.Subtasks);
                }

                previousSibling = current;
            }
        }

        private static void EnforceManualPlanningWhenDelegationDisabled(PlannerRequestContext context, StructuredAgentResult response)
        {
            if (context.AllowDecomposition || response.Tasks == null || response.Tasks.Count == 0)
            {
                return;
            }

            List<AgentPlannedTask> flattenedTasks = new List<AgentPlannedTask>();
            for (Int32 index = 0; index < response.Tasks.Count; index++)
            {
                AgentPlannedTask task = response.Tasks[index];
                FlattenPlannerTask(task, flattenedTasks);
            }

            response.Tasks.Clear();
            for (Int32 index = 0; index < flattenedTasks.Count; index++)
            {
                response.Tasks.Add(flattenedTasks[index]);
            }
        }

        private static void FlattenPlannerTask(AgentPlannedTask task, List<AgentPlannedTask> destination)
        {
            destination.Add(task);

            if (task.Subtasks == null || task.Subtasks.Count == 0)
            {
                return;
            }

            List<AgentPlannedTask> childTasks = task.Subtasks;
            task.Subtasks = null;
            for (Int32 index = 0; index < childTasks.Count; index++)
            {
                AgentPlannedTask child = childTasks[index];
                EnsureParentDependency(child, task);
                FlattenPlannerTask(child, destination);
            }
        }

        private static void EnsureDependencyReference(AgentPlannedTask task, String dependencyId)
        {
            if (String.IsNullOrWhiteSpace(dependencyId))
            {
                return;
            }

            if (task.Dependencies == null)
            {
                task.Dependencies = new List<String>();
            }

            for (Int32 index = 0; index < task.Dependencies.Count; index++)
            {
                String existing = task.Dependencies[index];
                if (String.Equals(existing, dependencyId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            task.Dependencies.Add(dependencyId);
        }

        private void EvaluateTaskForRecursivePlanning(AgentPlannedTask task, PlannerRequestContext parentContext, Stack<PlannerRequestContext> pendingContexts)
        {
            Int32 childDepth = parentContext.Depth + 1;
            (Double WorkRetentionFraction, Double DelegationFraction) childBudget = assignmentController.CalculateWorkBudgetForDepth(childDepth);
            PlannerRequestContext childContext = parentContext.CreateChild(task, childBudget.WorkRetentionFraction, childBudget.DelegationFraction);

            Double executionBias = assignmentController.GetExecutionBiasForDepth(childContext.Depth);
            Boolean hasDelegationBudget = WorkBudgetSettings.HasMeaningfulDelegation(childBudget.DelegationFraction);
            childContext.AllowDecomposition = executionBias < 1.0 && hasDelegationBudget;

            if (ShouldRequestRecursivePlanning(task, childContext.Depth, childBudget.DelegationFraction) && childContext.AllowDecomposition)
            {
                pendingContexts.Push(childContext);
            }

            if (task.Subtasks == null || task.Subtasks.Count == 0)
            {
                return;
            }

            for (Int32 index = task.Subtasks.Count - 1; index >= 0; index--)
            {
                AgentPlannedTask childTask = task.Subtasks[index];
                EnsurePlannerTaskHasId(childTask);
                EnsureParentDependency(childTask, task);
                EvaluateTaskForRecursivePlanning(childTask, childContext, pendingContexts);
            }
        }

        private static String FormatPercent(Double fraction)
        {
            Double percent = fraction * 100.0;
            return percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        private Boolean ShouldRequestRecursivePlanning(AgentPlannedTask task, Int32 depth, Double availableDelegationFraction)
        {
            if (depth >= RecursivePlannerMaxDepth)
            {
                return false;
            }

            if (!WorkBudgetSettings.HasMeaningfulDelegation(availableDelegationFraction))
            {
                return false;
            }

            Boolean hasCommands = task.Commands != null && task.Commands.Count > 0;
            Boolean hasSubtasks = task.Subtasks != null && task.Subtasks.Count > 0;

            if (hasCommands)
            {
                return false;
            }

            return !hasSubtasks;
        }

        private void ResetTasksFromPlannerResponse(StructuredAgentResult plannerResponse)
        {
            assignmentTaskItems.Clear();
            completedTaskContextById.Clear();
            missingDependencyWarnings.Clear();

            SmartTask? rootTask = assignmentRootSmartTask;
            if (rootTask != null)
            {
                assignmentController.RegisterSmartTaskNode(rootTask);
            }

            DateTime now = DateTime.Now;

            Int32 nextTaskNumber = 1;

            if (plannerResponse.Tasks != null && plannerResponse.Tasks.Count > 0)
            {
                for (Int32 index = 0; index < plannerResponse.Tasks.Count; index++)
                {
                    AgentPlannedTask plannedTask = plannerResponse.Tasks[index];
                    nextTaskNumber = AddPlannerTaskRecursive(plannedTask, null, now, nextTaskNumber, false);
                }
            }
            else
            {
                SmartTaskExecutionContext singleTask = new SmartTaskExecutionContext();
                singleTask.TaskNumber = 1;
                singleTask.Label = UiText(UiCatalogKeys.TaskFallbackAnswerLabel);
                singleTask.Type = "QuestionAnswer";
                singleTask.AgentTaskId = assignmentController.EnsureUniqueAgentTaskId("single-step");
                singleTask.AssociatedCommands = new List<AgentCommandDescription>();
                singleTask.CreatedAt = now;
                singleTask.AttemptCount = 0;
                singleTask.MaxRepairAttempts = maxRepairAttemptsPerTask;
                singleTask.SetStatus(AssignmentTaskStatusOptions.Planned);
                singleTask.TaskLogText = String.Empty;
                singleTask.TaskContext = UiText(UiCatalogKeys.TaskFallbackAnswerContext);
                assignmentController.AssignCreationOrder(singleTask);
                assignmentTaskItems.Add(singleTask);
            }

            RenumberTasks();
        }

        private Int32 AddPlannerTaskRecursive(AgentPlannedTask plannedTask, String? parentTaskId, DateTime now, Int32 sequenceNumber, Boolean parentRequiresSequentialDependency)
        {
            SmartTaskExecutionContext viewItem = new SmartTaskExecutionContext();
            viewItem.TaskNumber = sequenceNumber;
            viewItem.Label = String.IsNullOrWhiteSpace(plannedTask.Label)
                ? UiText(UiCatalogKeys.TaskDefaultLabel, sequenceNumber)
                : plannedTask.Label!;
            viewItem.Type = plannedTask.Type ?? String.Empty;
            viewItem.AgentTaskId = assignmentController.EnsureUniqueAgentTaskId(plannedTask.Id ?? ("task-" + sequenceNumber.ToString(CultureInfo.InvariantCulture)));
            viewItem.AssociatedCommands = plannedTask.Commands ?? new List<AgentCommandDescription>();
            Boolean hasExecutableCommands = viewItem.AssociatedCommands.Count > 0;
            viewItem.CreatedAt = now;
            viewItem.AttemptCount = 0;
            viewItem.MaxRepairAttempts = maxRepairAttemptsPerTask;
            viewItem.SetStatus(AssignmentTaskStatusOptions.Planned);
            viewItem.TaskLogText = String.Empty;
            viewItem.ParentTaskId = parentTaskId;
            viewItem.Dependencies = plannedTask.Dependencies != null ? new List<String>(plannedTask.Dependencies) : new List<String>();
            viewItem.RequiresCommandExecution = hasExecutableCommands;

            if (!String.IsNullOrWhiteSpace(parentTaskId) && parentRequiresSequentialDependency)
            {
                Boolean alreadyIncluded = viewItem.Dependencies.Exists(dependency => String.Equals(dependency, parentTaskId, StringComparison.OrdinalIgnoreCase));
                if (!alreadyIncluded)
                {
                    viewItem.Dependencies.Add(parentTaskId);
                }
            }

            if (!String.IsNullOrWhiteSpace(plannedTask.Context))
            {
                viewItem.TaskContext = plannedTask.Context;
            }
            else if (!String.IsNullOrWhiteSpace(plannedTask.Description))
            {
                viewItem.TaskContext = plannedTask.Description;
            }

            viewItem.Priority = plannedTask.Priority ?? viewItem.Priority;
            viewItem.Phase = plannedTask.Phase;
            viewItem.SetContextTags(plannedTask.ContextTags);
            viewItem.AllowsDependentsToProceed = !hasExecutableCommands;

            assignmentController.AssignCreationOrder(viewItem);
            assignmentTaskItems.Add(viewItem);

            
            SmartTask smartTaskNode = assignmentController.GetOrCreateSmartTaskNode(viewItem.AgentTaskId, viewItem, parentTaskId, viewItem.Label);
            assignmentController.ApplyWorkBudgetToSmartTask(smartTaskNode, assignmentController.CalculateSmartTaskDepth(smartTaskNode));

            Int32 nextSequence = sequenceNumber + 1;

            if (plannedTask.Subtasks != null)
            {
                for (Int32 index = 0; index < plannedTask.Subtasks.Count; index++)
                {
                    AgentPlannedTask childTask = plannedTask.Subtasks[index];
                    nextSequence = AddPlannerTaskRecursive(childTask, viewItem.AgentTaskId, now, nextSequence, hasExecutableCommands);
                }
            }

            return nextSequence;
        }

        private void UpdateBoundSmartTaskState(SmartTaskExecutionContext taskItem, SmartTaskStateOptions newState, String? stage = null)
        {
            SmartTask? smartTask = assignmentController.FindSmartTaskForAssignmentTask(taskItem);
            if (smartTask == null)
            {
                return;
            }

            assignmentController.UpdateSmartTaskState(smartTask, newState, stage);
        }

        private void RenumberTasks()
        {
            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                assignmentTaskItems[index].TaskNumber = index + 1;
            }
        }

        private async Task<Boolean> RunPlannerTaskGraphAsync(CancellationToken cancellationToken)
        {
            if (assignmentTaskItems.Count == 0)
            {
                AppendLog("Planner did not produce executable tasks; skipping task runner.");
                return true;
            }

            AppendLog("Executing planner task graph (" + assignmentTaskItems.Count + " task(s)).");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);

                SmartTaskExecutionContext? nextTask = FindNextReadyTask();
                if (nextTask == null)
                {
                    if (HasInProgressTasks())
                    {
                        await Task.Delay(200, cancellationToken);
                        continue;
                    }

                    if (HasPendingTasks())
                    {
                        AppendLog("No runnable planner tasks remain because dependencies were never satisfied. Skipping blocked tasks.");
                        MarkBlockedTasksAsSkipped("Skipped automatically because dependencies were not satisfied.");
                    }

                    break;
                }

                Boolean continueAssignment = await ExecutePlannerTaskAsync(nextTask, cancellationToken);
                if (!continueAssignment)
                {
                    return false;
                }
            }

            AppendLog("Planner task graph execution finished.");
            return true;
        }

        private SmartTaskExecutionContext? FindNextReadyTask()
        {
            SmartTaskExecutionContext? nextTask = null;

            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                SmartTaskExecutionContext taskItem = assignmentTaskItems[index];
                if (taskItem.StatusKind != AssignmentTaskStatusOptions.Planned &&
                    taskItem.StatusKind != AssignmentTaskStatusOptions.PendingApproval)
                {
                    continue;
                }

                if (!AreDependenciesSatisfied(taskItem))
                {
                    continue;
                }

                if (nextTask == null ||
                    taskItem.CreationOrder < nextTask.CreationOrder ||
                    (taskItem.CreationOrder == nextTask.CreationOrder && taskItem.TaskNumber < nextTask.TaskNumber))
                {
                    nextTask = taskItem;
                }
            }

            return nextTask;
        }

        private Boolean HasInProgressTasks()
        {
            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                if (assignmentTaskItems[index].StatusKind == AssignmentTaskStatusOptions.InProgress)
                {
                    return true;
                }
            }
            return false;
        }

        private Boolean AreDependenciesSatisfied(SmartTaskExecutionContext taskItem)
        {
            if (taskItem.Dependencies == null || taskItem.Dependencies.Count == 0)
            {
                return true;
            }

            for (Int32 index = 0; index < taskItem.Dependencies.Count; index++)
            {
                String dependencyId = taskItem.Dependencies[index];
                if (String.IsNullOrWhiteSpace(dependencyId))
                {
                    continue;
                }

                SmartTaskExecutionContext? dependencyTask = FindTaskByAgentTaskId(dependencyId);
                if (dependencyTask == null)
                {
                    WarnAboutMissingDependency(taskItem, dependencyId);
                    return false;
                }

                if (dependencyTask.StatusKind == AssignmentTaskStatusOptions.Succeeded ||
                    dependencyTask.StatusKind == AssignmentTaskStatusOptions.Skipped ||
                    dependencyTask.AllowsDependentsToProceed)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private async Task<Boolean> ExecutePlannerTaskAsync(SmartTaskExecutionContext taskItem, CancellationToken cancellationToken)
        {
            InvokeOnUiThread(() =>
            {
                taskItem.StartedAt = DateTime.Now;
                taskItem.SetStatus(AssignmentTaskStatusOptions.InProgress);
            });

            SmartTask? smartTask = assignmentController.FindSmartTaskForAssignmentTask(taskItem);

            if (smartTask != null && assignmentController.HasIntentBeenCompleted(smartTask.Intent))
            {
                AppendTaskLog(taskItem, "Deduplicated: goal already completed previously. Skipping task.");
                InvokeOnUiThread(() =>
                {
                    taskItem.CompletedAt = DateTime.Now;
                    taskItem.SetStatus(AssignmentTaskStatusOptions.Skipped);
                });
                UpdateBoundSmartTaskState(taskItem, SmartTaskStateOptions.Skipped, "Deduplicated");
                taskItem.AllowsDependentsToProceed = true;
                return true;
            }

            Boolean isContainerTask = smartTask != null && smartTask.Subtasks != null && smartTask.Subtasks.Count > 0;
            String executingStage = isContainerTask ? "Coordinating subtasks" : "Executing commands";
            UpdateBoundSmartTaskState(taskItem, SmartTaskStateOptions.Executing, executingStage);

            if (smartTask != null && AssignmentStatusTextBlock != null)
            {
                Int32 depth = smartTask.Depth;
                Double bias = assignmentController.GetExecutionBiasForDepth(depth);
                Int32 biasPercent = (Int32)Math.Round(bias * 100.0, MidpointRounding.AwayFromZero);
                AssignmentStatusTextBlock.Text = $"Executing task #{taskItem.TaskNumber} at depth {depth} (Execution bias {biasPercent}%)";
            }

            if (isContainerTask)
            {
                AppendTaskLog(taskItem, "Task started (waiting for subtasks).");
                return true;
            }

            AppendTaskLog(taskItem, "Task execution started.");

            Boolean succeeded = await ExecuteTaskWithRepairsAsync(taskItem, cancellationToken);

            InvokeOnUiThread(() =>
            {
                taskItem.CompletedAt = DateTime.Now;
            });

            if (succeeded)
            {
                InvokeOnUiThread(() => taskItem.SetStatus(AssignmentTaskStatusOptions.Succeeded));
                UpdateBoundSmartTaskState(taskItem, SmartTaskStateOptions.Succeeded, "Completed");
                CaptureTaskCompletionContext(taskItem);
                if (smartTask != null)
                {
                    assignmentController.RegisterCompletedIntent(smartTask.Intent);
                }
                AppendTaskLog(taskItem, "Task completed successfully.");
                return true;
            }

            InvokeOnUiThread(() => taskItem.SetStatus(AssignmentTaskStatusOptions.Failed));
            UpdateBoundSmartTaskState(taskItem, SmartTaskStateOptions.Failed, "Failed");
            CaptureTaskFailureContext(taskItem);
            AppendTaskLog(taskItem, "Task failed after exhausting repair attempts.");

            Boolean continueAssignment = await assignmentRuntimeService.TryHandleTerminalTaskFailureAsync(
                taskItem,
                selectedWorkspacePath,
                CreateFailureResolutionCallbacks(),
                cancellationToken);
            if (!continueAssignment)
            {
                AppendLog("Task '" + taskItem.Label + "' failed and halted the assignment.");
                ShowAssignmentFailedDialogForTask(taskItem);
                return false;
            }

            AppendLog("Continuing after failure of task '" + taskItem.Label + "'.");
            return true;
        }

        private async Task<Boolean> ExecuteTaskWithRepairsAsync(SmartTaskExecutionContext taskContext, CancellationToken cancellationToken)
        {
            SmartTask? smartTask = assignmentController.FindSmartTaskForAssignmentTask(taskContext);
            if (smartTask == null)
            {
                AppendTaskLog(taskContext, "Task is not linked to a smart task; cannot execute commands.");
                return false;
            }

            if (taskContext.AssociatedCommands == null)
            {
                taskContext.AssociatedCommands = new List<AgentCommandDescription>();
            }

            List<AgentCommandDescription> commands = taskContext.AssociatedCommands;
            if (commands.Count == 0)
            {
                if (taskContext.RequiresCommandExecution)
                {
                    AppendTaskLog(taskContext, "Planner marked this task as executable but did not provide commands.");
                    return false;
                }

                AppendTaskLog(taskContext, "No commands are required for this task.");
                return true;
            }

            NormalizeCommandList(commands);

            Int32 maxAttempts = taskContext.MaxRepairAttempts > 0 ? taskContext.MaxRepairAttempts : maxRepairAttemptsPerTask;
            if (maxAttempts <= 0)
            {
                maxAttempts = 1;
            }

            for (Int32 attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                taskContext.AttemptCount = attempt;

                CommandRunResult commandOutcome = await RunCommandsWithPolicyAndAnalystAsync(smartTask, taskContext, commands, cancellationToken);
                if (commandOutcome.Succeeded)
                {
                    return true;
                }

                if (!commandOutcome.CommandsAttempted)
                {
                    AppendTaskLog(taskContext, "Command execution was blocked: " + (commandOutcome.FailureReason ?? "unknown reason"));
                    return false;
                }

                if (attempt >= maxAttempts)
                {
                    break;
                }

                Boolean trackRepairSmartTask = attempt + 1 >= maxAttempts;
                StructuredRepairResult? repairResponse = await assignmentRuntimeService.RequestRepairResponseAsync(
                    taskContext,
                    selectedWorkspacePath,
                    trackRepairSmartTask,
                    cancellationToken);
                if (repairResponse == null)
                {
                    break;
                }

                Boolean repairApplied = await ApplyRepairAsync(taskContext, repairResponse, cancellationToken);
                if (!repairApplied)
                {
                    break;
                }

                String decision = (repairResponse.RepairDecision ?? String.Empty).Trim().ToLowerInvariant();
                if (decision == "add_new_tasks")
                {
                    AppendTaskLog(taskContext, "Repair agent added new tasks and marked this task as satisfied.");
                    taskContext.AllowsDependentsToProceed = true;
                    return true;
                }

                if (decision == "give_up")
                {
                    AppendTaskLog(taskContext, "Repair agent opted to give up on this task.");
                    break;
                }

                if (taskContext.AssociatedCommands == null)
                {
                    taskContext.AssociatedCommands = new List<AgentCommandDescription>();
                }

                commands = taskContext.AssociatedCommands;
                NormalizeCommandList(commands);
                if (commands.Count == 0)
                {
                    AppendTaskLog(taskContext, "Repair agent did not supply replacement commands.");
                    break;
                }
            }

            return false;
        }

        private void OnPauseAssignmentClick(object? sender, RoutedEventArgs e)
        {
            if (!assignmentController.IsAssignmentRunning)
            {
                return;
            }

            Boolean newPausedState = !assignmentController.IsAssignmentPaused;
            assignmentController.UpdateAssignmentPausedState(newPausedState);
            if (newPausedState)
            {
                if (PauseAssignmentButton != null)
                {
                    PauseAssignmentButton.Content = "Resume";
                }
                AppendLog("Assignment paused.");
            }
            else
            {
                if (PauseAssignmentButton != null)
                {
                    PauseAssignmentButton.Content = "Pause";
                }
                AppendLog("Assignment resumed.");
            }
        }

        private void OnCancelAssignmentClick(object? sender, RoutedEventArgs e)
        {
            if (assignmentController.IsAssignmentRunning)
            {
                CaptureAssignmentFailureReason("the run was cancelled by the user");
                assignmentController.CancelAssignmentRun();
                assignmentController.UpdateAssignmentPausedState(false);
                if (PauseAssignmentButton != null)
                {
                    PauseAssignmentButton.Content = "Pause";
                }
                commandExecutionService.StopAllBackgroundCommands("Assignment cancelled");
                UpdateUiState();
                return;
            }

            if (!isAssignmentLaunchPending)
            {
                return;
            }

            AppendLog("Assignment launch cancelled before execution start.");
            isAssignmentLaunchPending = false;
            assignmentController.CancelAssignmentRun();
            assignmentController.ClearAssignmentCancellationToken();
            commandExecutionService.StopAllBackgroundCommands("Assignment start cancelled");
            UpdateUiState();
        }


        private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
        {
            while (assignmentController.IsAssignmentPaused && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(200, cancellationToken);
            }
        }

        private void SynchronizeWorkspaceAutoFilesWithCheckbox()
        {
            if (String.IsNullOrWhiteSpace(selectedWorkspacePath))
            {
                return;
            }

            Boolean isIncludeChecked = IncludeWorkspaceContextCheckBox != null && IncludeWorkspaceContextCheckBox.IsChecked == true;

            if (isIncludeChecked)
            {
                AddWorkspaceAutoFilesToList();
            }
            else
            {
                RemoveWorkspaceFilesFromWorkspaceDirectory();
            }
        }

        private IReadOnlyList<WorkspaceFileItem> CreateWorkspaceFileSnapshot()
        {
            List<WorkspaceFileItem> snapshot = new List<WorkspaceFileItem>(workspaceFileItems.Count);
            for (Int32 index = 0; index < workspaceFileItems.Count; index++)
            {
                WorkspaceFileItem source = workspaceFileItems[index];
                WorkspaceFileItem clone = new WorkspaceFileItem
                {
                    Path = source.Path,
                    FullPath = source.FullPath,
                    IsFromWorkspaceAuto = source.IsFromWorkspaceAuto
                };

                snapshot.Add(clone);
            }

            return snapshot;
        }

        private void RegisterWorkspaceFilesAsSemanticFacts(IReadOnlyList<WorkspaceFileItem> files)
        {
            if (files == null || files.Count == 0)
            {
                return;
            }

            if (globalContext == null)
            {
                return;
            }

            const String source = "WorkspaceFiles";

            for (Int32 index = 0; index < files.Count; index++)
            {
                WorkspaceFileItem? file = files[index];
                if (file == null)
                {
                    continue;
                }

                String? fullPath = file.FullPath;
                if (String.IsNullOrWhiteSpace(fullPath))
                {
                    continue;
                }

                globalContext.SetFact(fullPath, String.Empty, source, fullPath, SemanticFactOptions.General);
            }
        }

        private void AddWorkspaceAutoFilesToList()
        {
            if (String.IsNullOrWhiteSpace(selectedWorkspacePath))
            {
                return;
            }

            if (!Directory.Exists(selectedWorkspacePath))
            {
                return;
            }

            String workspaceRootFullPath;
            try
            {
                workspaceRootFullPath = Path.GetFullPath(selectedWorkspacePath);
            }
            catch
            {
                return;
            }

            Task.Run(() =>
            {
                String[] allFiles;
                try
                {
                    allFiles = Directory.GetFiles(workspaceRootFullPath, "*.*", SearchOption.AllDirectories);
                }
                catch
                {
                    return;
                }

                List<String> filePaths = new List<String>();
                for (Int32 index = 0; index < allFiles.Length; index++)
                {
                    filePaths.Add(allFiles[index]);
                }

                InvokeOnUiThread(() =>
                {
                    for (Int32 index = 0; index < filePaths.Count; index++)
                    {
                        String fullPath = filePaths[index];

                        Boolean alreadyExists = false;
                        for (Int32 existingIndex = 0; existingIndex < workspaceFileItems.Count; existingIndex++)
                        {
                            WorkspaceFileItem existingItem = workspaceFileItems[existingIndex];
                            if (String.Equals(existingItem.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                            {
                                alreadyExists = true;
                                break;
                            }
                        }

                        if (alreadyExists)
                        {
                            continue;
                        }

                        String relativePath;
                        try
                        {
                            relativePath = Path.GetRelativePath(workspaceRootFullPath, fullPath);
                        }
                        catch
                        {
                            relativePath = fullPath;
                        }

                        WorkspaceFileItem item = new WorkspaceFileItem();
                        item.FullPath = fullPath;
                        item.Path = relativePath;
                        item.IsFromWorkspaceAuto = true;

                        workspaceFileItems.Add(item);
                    }
                });
            });
        }

        private void RemoveWorkspaceFilesFromWorkspaceDirectory()
        {
            if (workspaceFileItems.Count == 0)
            {
                return;
            }

            String? workspaceRootFullPath = null;
            if (!String.IsNullOrWhiteSpace(selectedWorkspacePath))
            {
                try
                {
                    workspaceRootFullPath = Path.GetFullPath(selectedWorkspacePath);
                }
                catch
                {
                    workspaceRootFullPath = null;
                }
            }

            for (Int32 index = workspaceFileItems.Count - 1; index >= 0; index--)
            {
                WorkspaceFileItem item = workspaceFileItems[index];
                if (!item.IsFromWorkspaceAuto)
                {
                    continue;
                }

                if (workspaceRootFullPath == null)
                {
                    workspaceFileItems.RemoveAt(index);
                    continue;
                }

                if (String.IsNullOrWhiteSpace(item.FullPath))
                {
                    workspaceFileItems.RemoveAt(index);
                    continue;
                }

                if (item.FullPath.StartsWith(workspaceRootFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    workspaceFileItems.RemoveAt(index);
                }
            }
        }

        private void OnMainWindowOpened(object? sender, EventArgs e)
        {
            if (hasShownUsageLiabilityWarning)
            {
                return;
            }

            hasShownUsageLiabilityWarning = true;

            String title = UiText(UiCatalogKeys.TitleApplication);
            String message = UiText(UiCatalogKeys.DialogUsageWarningMessage);
            String buttonText = UiText(UiCatalogKeys.ButtonAgree);

            _ = DialogService.ShowInfoAsync(this, title, message, buttonText);
        }

        private void OnIncludeWorkspaceContextChanged(object? sender, RoutedEventArgs e)
        {
            SynchronizeWorkspaceAutoFilesWithCheckbox();
        }

        private void OnConstraintCheckboxChanged(object? sender, RoutedEventArgs e)
        {
            UpdateConstraintSettingsFromUi();
        }

        private void OnPolicyRiskToleranceSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateConstraintSettingsFromUi();
        }

        private void UpdateConstraintSettingsFromUi()
        {
            allowNewSoftwareInstallation = AllowNewSoftwareInstallationsCheckBox?.IsChecked != false;
            allowNetworkOperations = AllowNetworkAccessCheckBox?.IsChecked != false;
            allowSystemConfigurationChanges = AllowSystemConfigurationChangesCheckBox?.IsChecked != false;
            PolicyRiskToleranceOptions = GetSelectedPolicyRiskToleranceOptionsFromUi();
            UpdatePolicyRiskToleranceOptionsLabel(PolicyRiskToleranceOptions);

            if (globalContext != null)
            {
                globalContext.SecurityProfile = new SecurityProfile
                {
                    AllowInstall = allowNewSoftwareInstallation,
                    AllowNetwork = allowNetworkOperations,
                    AllowSystemConfiguration = allowSystemConfigurationChanges,
                    PolicyRiskToleranceOptions = PolicyRiskToleranceOptions
                };
            }
        }

        private PolicyRiskToleranceOptions GetSelectedPolicyRiskToleranceOptionsFromUi()
        {
            if (PolicyRiskToleranceSlider == null)
            {
                return PolicyRiskToleranceOptions;
            }

            return MapSliderValueToTolerance(PolicyRiskToleranceSlider.Value);
        }

        private static Double MapToleranceToSlider(PolicyRiskToleranceOptions tolerance)
        {
            return tolerance switch
            {
                PolicyRiskToleranceOptions.None => 0.0,
                PolicyRiskToleranceOptions.LowOnly => 1.0,
                PolicyRiskToleranceOptions.UpToMedium => 2.0,
                _ => 3.0
            };
        }

        private static PolicyRiskToleranceOptions MapSliderValueToTolerance(Double sliderValue)
        {
            Int32 roundedValue = (Int32)Math.Round(sliderValue, MidpointRounding.AwayFromZero);
            return roundedValue switch
            {
                0 => PolicyRiskToleranceOptions.None,
                1 => PolicyRiskToleranceOptions.LowOnly,
                2 => PolicyRiskToleranceOptions.UpToMedium,
                _ => PolicyRiskToleranceOptions.AllowAll
            };
        }

        private void UpdatePolicyRiskToleranceOptionsLabel(PolicyRiskToleranceOptions tolerance)
        {
            if (PolicyRiskToleranceValueTextBlock == null)
            {
                return;
            }

            String scope = FormatRiskToleranceForDisplay(tolerance);
            PolicyRiskToleranceValueTextBlock.Text = UiText(UiCatalogKeys.TextPolicyAutoApproveValue, scope);
        }

        private void OnCommandRetryAttemptsSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            maxCommandRetryAttempts = (Int32)Math.Round(e.NewValue, MidpointRounding.AwayFromZero);
            UpdateCommandRetryAttemptsLabel();
        }

        private void UpdateCommandRetryAttemptsLabel()
        {
            if (CommandRetryAttemptsValueTextBlock == null)
            {
                return;
            }

            String label = maxCommandRetryAttempts == 0
                ? UiText(UiCatalogKeys.TextRetriesZero)
                : UiText(UiCatalogKeys.TextRetriesValue, maxCommandRetryAttempts.ToString(CultureInfo.InvariantCulture));

            CommandRetryAttemptsValueTextBlock.Text = label;
        }

        private void ApplyRepairRetrySettingsToAllTaskContexts()
        {
            if (assignmentTaskItems == null)
            {
                return;
            }

            foreach (SmartTaskExecutionContext context in assignmentTaskItems)
            {
                context.MaxRepairAttempts = maxRepairAttemptsPerTask;
            }
        }

        private async void OnAddWorkspaceFileClick(object? sender, RoutedEventArgs e)
        {
            if (StorageProvider == null)
            {
                return;
            }

            FilePickerOpenOptions pickerOptions = new FilePickerOpenOptions
            {
                AllowMultiple = false
            };

            if (!String.IsNullOrWhiteSpace(selectedWorkspacePath))
            {
                IStorageFolder? startFolder = await StorageProvider.TryGetFolderFromPathAsync(selectedWorkspacePath);
                if (startFolder != null)
                {
                    pickerOptions.SuggestedStartLocation = startFolder;
                }
            }

            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(pickerOptions);
            if (files == null || files.Count == 0)
            {
                return;
            }

            String? fullPath = files[0].TryGetLocalPath();
            if (String.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            String normalizedFullPath;
            try
            {
                normalizedFullPath = Path.GetFullPath(fullPath);
            }
            catch
            {
                normalizedFullPath = fullPath;
            }

            for (Int32 index = 0; index < workspaceFileItems.Count; index++)
            {
                WorkspaceFileItem existingItem = workspaceFileItems[index];
                if (String.Equals(existingItem.FullPath, normalizedFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            String displayPath = normalizedFullPath;
            if (!String.IsNullOrWhiteSpace(selectedWorkspacePath))
            {
                try
                {
                    String workspaceRootFullPath = Path.GetFullPath(selectedWorkspacePath);
                    if (normalizedFullPath.StartsWith(workspaceRootFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        displayPath = Path.GetRelativePath(workspaceRootFullPath, normalizedFullPath);
                    }
                }
                catch
                {
                    displayPath = normalizedFullPath;
                }
            }

            WorkspaceFileItem item = new WorkspaceFileItem();
            item.FullPath = normalizedFullPath;
            item.Path = displayPath;
            item.IsFromWorkspaceAuto = false;

            workspaceFileItems.Add(item);
        }

        private void OnRemoveWorkspaceFileClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            if (button.DataContext is WorkspaceFileItem item)
            {
                workspaceFileItems.Remove(item);
            }
        }

        private void SaveAssignmentArtifactsSafely(String runDirectoryPath, String? assignmentTitle, String? assignmentPrompt, String? workspaceContext, StructuredAgentResult? plannerResponse)
        {
            try
            {
                SaveAssignmentArtifacts(runDirectoryPath, assignmentTitle, assignmentPrompt, workspaceContext, plannerResponse);
            }
            catch (Exception exception)
            {
                AppendLog("Failed to persist assignment artifacts: " + exception.Message);
            }
        }

        private void ConfigureRightPaneSplitter()
        {
            if (RightPaneSplitter == null)
            {
                return;
            }

            RightPaneSplitter.ShowsPreview = false;
            RightPaneSplitter.TemplateApplied += OnRightPaneSplitterTemplateApplied;
        }

        private void OnRightPaneSplitterTemplateApplied(object? sender, TemplateAppliedEventArgs e)
        {
            if (RightPaneSplitter == null)
            {
                return;
            }

            rightPaneSplitterThumb = e.NameScope.Find<Thumb>("PART_Thumb");
            if (rightPaneSplitterThumb != null)
            {
                rightPaneSplitterThumb.Background = Brushes.Transparent;
                rightPaneSplitterThumb.BorderBrush = Brushes.Transparent;
            }
        }

        private void SaveAssignmentArtifacts(String runDirectoryPath, String? assignmentTitle, String? assignmentPrompt, String? workspaceContext, StructuredAgentResult? plannerResponse)
        {
            if (String.IsNullOrWhiteSpace(runDirectoryPath))
            {
                return;
            }

            if (!Directory.Exists(runDirectoryPath))
            {
                try
                {
                    Directory.CreateDirectory(runDirectoryPath);
                }
                catch
                {
                    return;
                }
            }

            String titleText = assignmentTitle ?? String.Empty;
            String promptText = assignmentPrompt ?? String.Empty;
            String workspaceContextText = workspaceContext ?? String.Empty;

            StringBuilder assignmentBuilder = new StringBuilder();
            assignmentBuilder.AppendLine("Title:");
            assignmentBuilder.AppendLine(titleText);
            assignmentBuilder.AppendLine();
            assignmentBuilder.AppendLine("Prompt:");
            assignmentBuilder.AppendLine(promptText);
            assignmentBuilder.AppendLine();
            assignmentBuilder.AppendLine("Workspace context:");
            assignmentBuilder.AppendLine(workspaceContextText);

            String assignmentPath = Path.Combine(runDirectoryPath, "assignment.txt");
            File.WriteAllText(assignmentPath, assignmentBuilder.ToString(), Encoding.UTF8);

            if (plannerResponse != null)
            {
                String plannerRaw = plannerResponse.RawContent ?? String.Empty;
                String plannerJsonPath = Path.Combine(runDirectoryPath, "planner-raw.json");
                File.WriteAllText(plannerJsonPath, plannerRaw, Encoding.UTF8);
            }

            String agentOutputText = AssignmentAnswerOutputText;
            String agentOutputPath = Path.Combine(runDirectoryPath, "agent-output.txt");
            File.WriteAllText(agentOutputPath, agentOutputText, Encoding.UTF8);

            String agentRawOutputText = AssignmentRawOutputText;
            String agentRawOutputPath = Path.Combine(runDirectoryPath, "agent-raw-output.txt");
            File.WriteAllText(agentRawOutputPath, agentRawOutputText, Encoding.UTF8);

            String commandsOutputText = CommandsSummaryOutputText;
            String commandsOutputPath = Path.Combine(runDirectoryPath, "commands-output.txt");
            File.WriteAllText(commandsOutputPath, commandsOutputText, Encoding.UTF8);

            String systemLogText = assignmentController.GetSystemLogSnapshot();
            String systemLogPath = Path.Combine(runDirectoryPath, "system-log.txt");
            File.WriteAllText(systemLogPath, systemLogText, Encoding.UTF8);

            StringBuilder tasksBuilder = new StringBuilder();
            InvokeOnUiThread(() =>
            {
                for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
                {
                    SmartTaskExecutionContext taskItem = assignmentTaskItems[index];
                    tasksBuilder.AppendLine("Task " + taskItem.TaskNumber + " - " + taskItem.Label);
                    tasksBuilder.AppendLine("Type: " + taskItem.Type);
                    tasksBuilder.AppendLine("Status: " + taskItem.Status);
                    tasksBuilder.AppendLine("Attempts: " + taskItem.AttemptCount + " / " + taskItem.MaxRepairAttempts);
                    if (!String.IsNullOrWhiteSpace(taskItem.LastResultText))
                    {
                        tasksBuilder.AppendLine("LastResult:");
                        tasksBuilder.AppendLine(taskItem.LastResultText);
                    }
                    tasksBuilder.AppendLine();
                }
            });

            String tasksPath = Path.Combine(runDirectoryPath, "tasks.txt");
            File.WriteAllText(tasksPath, tasksBuilder.ToString(), Encoding.UTF8);
        }



        private Boolean HasPendingTasks()
        {
            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                SmartTaskExecutionContext taskItem = assignmentTaskItems[index];
                if (taskItem.StatusKind == AssignmentTaskStatusOptions.Planned ||
                    taskItem.StatusKind == AssignmentTaskStatusOptions.PendingApproval)
                {
                    return true;
                }
            }

            return false;
        }

        private void MarkBlockedTasksAsSkipped(String reason)
        {
            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                SmartTaskExecutionContext taskItem = assignmentTaskItems[index];
                if (taskItem.StatusKind == AssignmentTaskStatusOptions.Planned ||
                    taskItem.StatusKind == AssignmentTaskStatusOptions.PendingApproval)
                {
                    taskItem.SetStatus(AssignmentTaskStatusOptions.Skipped);
                    AppendTaskLog(taskItem, reason);
                    UpdateBoundSmartTaskState(taskItem, SmartTaskStateOptions.Skipped, reason);
                }
            }
        }

        private void FinalizeDanglingTasks(String reason, Boolean treatAsSkipped)
        {
            AssignmentTaskStatusOptions targetStatus = treatAsSkipped ? AssignmentTaskStatusOptions.Skipped : AssignmentTaskStatusOptions.Failed;
            SmartTaskStateOptions targetSmartState = treatAsSkipped ? SmartTaskStateOptions.Skipped : SmartTaskStateOptions.Failed;
            List<SmartTaskExecutionContext> updatedTasks = new List<SmartTaskExecutionContext>();

            InvokeOnUiThread(() =>
            {
                for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
                {
                    SmartTaskExecutionContext taskItem = assignmentTaskItems[index];
                    if (taskItem.StatusKind == AssignmentTaskStatusOptions.Succeeded ||
                        taskItem.StatusKind == AssignmentTaskStatusOptions.Failed ||
                        taskItem.StatusKind == AssignmentTaskStatusOptions.Skipped)
                    {
                        continue;
                    }

                    taskItem.SetStatus(targetStatus);
                    if (!taskItem.CompletedAt.HasValue)
                    {
                        taskItem.CompletedAt = DateTime.Now;
                    }
                    updatedTasks.Add(taskItem);
                }
            });

            for (Int32 index = 0; index < updatedTasks.Count; index++)
            {
                SmartTaskExecutionContext task = updatedTasks[index];
                AppendTaskLog(task, reason);
                UpdateBoundSmartTaskState(task, targetSmartState, reason);
            }
        }

        private void WarnAboutMissingDependency(SmartTaskExecutionContext taskItem, String dependencyId)
        {
            String taskKey = String.IsNullOrWhiteSpace(taskItem.AgentTaskId) ? taskItem.TaskNumber.ToString(CultureInfo.InvariantCulture) : taskItem.AgentTaskId!;
            String cacheKey = taskKey + "->" + dependencyId;
            if (missingDependencyWarnings.Contains(cacheKey))
            {
                return;
            }

            missingDependencyWarnings.Add(cacheKey);
            AppendLog("Task '" + taskItem.Label + "' is waiting on missing dependency '" + dependencyId + "'.");
            AppendTaskLog(taskItem, "Task waiting on missing dependency '" + dependencyId + "'.");
        }

        private String BuildAggregatedContextForTask(SmartTaskExecutionContext taskItem)
        {
            StringBuilder builder = new StringBuilder();

            AppendTaskMetadataSummary(taskItem, builder);

            if (!String.IsNullOrWhiteSpace(taskItem.TaskContext))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("Planner-provided context:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(taskItem.TaskContext));
            }

            HashSet<String> appended = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            AppendDependencyContexts(taskItem.Dependencies, builder, appended);
            AppendParentChainContexts(taskItem.ParentTaskId, builder, appended);

            return builder.ToString().Trim();
        }

        private void AppendTaskMetadataSummary(SmartTaskExecutionContext taskItem, StringBuilder builder)
        {
            List<String> metadataLines = new List<String>();

            if (!String.IsNullOrWhiteSpace(taskItem.Priority))
            {
                metadataLines.Add("Priority: " + taskItem.Priority);
            }

            if (taskItem.ContextTags != null && taskItem.ContextTags.Count > 0)
            {
                metadataLines.Add("Context tags: " + String.Join(", ", taskItem.ContextTags));
            }

            if (taskItem.Dependencies != null && taskItem.Dependencies.Count > 0)
            {
                List<String> dependencyDescriptions = new List<String>();
                for (Int32 index = 0; index < taskItem.Dependencies.Count; index++)
                {
                    String dependencyId = taskItem.Dependencies[index];
                    if (String.IsNullOrWhiteSpace(dependencyId))
                    {
                        continue;
                    }

                    String dependencyStatus = DescribeDependencyStatus(dependencyId);
                    dependencyDescriptions.Add(dependencyId + " (" + dependencyStatus + ")");
                }

                if (dependencyDescriptions.Count > 0)
                {
                    metadataLines.Add("Dependencies: " + String.Join(", ", dependencyDescriptions));
                }
            }

            if (metadataLines.Count == 0)
            {
                return;
            }

            builder.AppendLine("Task metadata:");
            for (Int32 index = 0; index < metadataLines.Count; index++)
            {
                builder.AppendLine("- " + metadataLines[index]);
            }
        }

        private String DescribeDependencyStatus(String dependencyId)
        {
            SmartTaskExecutionContext? dependencyTask = FindTaskByAgentTaskId(dependencyId);
            if (dependencyTask == null)
            {
                return "unknown";
            }

            if (!String.IsNullOrWhiteSpace(dependencyTask.Status))
            {
                return dependencyTask.Status;
            }

            return dependencyTask.StatusKind.ToString();
        }

        private void AppendDependencyContexts(List<String>? dependencyIds, StringBuilder builder, HashSet<String> appended)
        {
            if (dependencyIds == null || dependencyIds.Count == 0)
            {
                return;
            }

            for (Int32 index = 0; index < dependencyIds.Count; index++)
            {
                String dependencyId = dependencyIds[index];
                AppendContextFromCompletedTask(dependencyId, "Context from dependency '" + dependencyId + "':", builder, appended);
            }
        }

        private void AppendParentChainContexts(String? parentTaskId, StringBuilder builder, HashSet<String> appended)
        {
            if (String.IsNullOrWhiteSpace(parentTaskId))
            {
                return;
            }

            HashSet<String> visited = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            String? currentId = parentTaskId;

            while (!String.IsNullOrWhiteSpace(currentId))
            {
                if (visited.Contains(currentId))
                {
                    break;
                }

                visited.Add(currentId);
                AppendContextFromCompletedTask(currentId, "Context from parent '" + currentId + "':", builder, appended);

                SmartTaskExecutionContext? parentTask = FindTaskByAgentTaskId(currentId);
                if (parentTask == null)
                {
                    break;
                }

                currentId = parentTask.ParentTaskId;
            }
        }

        private void AppendContextFromCompletedTask(String? taskId, String header, StringBuilder builder, HashSet<String> appended)
        {
            if (String.IsNullOrWhiteSpace(taskId))
            {
                return;
            }

            if (appended.Contains(taskId))
            {
                return;
            }

            if (!completedTaskContextById.TryGetValue(taskId, out String? dependencyContext))
            {
                return;
            }

            if (String.IsNullOrWhiteSpace(dependencyContext))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(header);
            builder.AppendLine(TextUtilityService.BuildCompactSnippet(dependencyContext));
            appended.Add(taskId);
        }

        private SmartTaskExecutionContext? FindTaskByAgentTaskId(String? agentTaskId)
        {
            if (String.IsNullOrWhiteSpace(agentTaskId))
            {
                return null;
            }

            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                SmartTaskExecutionContext candidate = assignmentTaskItems[index];
                if (String.Equals(candidate.AgentTaskId, agentTaskId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void CaptureTaskCompletionContext(SmartTaskExecutionContext taskItem)
        {
            if (String.IsNullOrWhiteSpace(taskItem.AgentTaskId))
            {
                return;
            }

            String snapshot = BuildTaskCompletionContextSnapshot(taskItem);
            if (snapshot.Length == 0)
            {
                return;
            }

            completedTaskContextById[taskItem.AgentTaskId] = snapshot;
        }

        private void CaptureTaskFailureContext(SmartTaskExecutionContext taskItem)
        {
            if (String.IsNullOrWhiteSpace(taskItem.AgentTaskId))
            {
                return;
            }

            String snapshot = BuildTaskFailureContextSnapshot(taskItem);
            if (snapshot.Length == 0)
            {
                return;
            }

            completedTaskContextById[taskItem.AgentTaskId] = snapshot;
        }

        private String BuildTaskCompletionContextSnapshot(SmartTaskExecutionContext taskItem)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Task: " + taskItem.Label);
            builder.AppendLine("Type: " + taskItem.Type);

            if (!String.IsNullOrWhiteSpace(taskItem.TaskContext))
            {
                builder.AppendLine("Original context:");
                builder.AppendLine(taskItem.TaskContext.Trim());
            }

            if (taskItem.ProducedContextEntries.Count > 0)
            {
                builder.AppendLine("Generated outputs:");
                for (Int32 index = 0; index < taskItem.ProducedContextEntries.Count; index++)
                {
                    builder.AppendLine(taskItem.ProducedContextEntries[index]);
                }
            }
            else if (!String.IsNullOrWhiteSpace(taskItem.LastResultText))
            {
                builder.AppendLine("Last result snapshot:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(taskItem.LastResultText));
            }

            return builder.ToString().Trim();
        }

        private String BuildTaskFailureContextSnapshot(SmartTaskExecutionContext taskItem)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Task failed: " + taskItem.Label);
            builder.AppendLine("Type: " + taskItem.Type);
            builder.AppendLine("Status: " + taskItem.Status);

            if (!String.IsNullOrWhiteSpace(taskItem.TaskContext))
            {
                builder.AppendLine("Original context:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(taskItem.TaskContext));
            }

            if (!String.IsNullOrWhiteSpace(taskItem.AggregatedContextSnapshot))
            {
                builder.AppendLine("Aggregated context snapshot:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(taskItem.AggregatedContextSnapshot));
            }

            if (!String.IsNullOrWhiteSpace(taskItem.LastResultText))
            {
                builder.AppendLine("Failure output:");
                builder.AppendLine(TextUtilityService.BuildCompactSnippet(taskItem.LastResultText));
            }
            else if (taskItem.RepairAttemptHistory.Count > 0)
            {
                builder.AppendLine("Repair attempts:");
                Int32 limit = Math.Min(3, taskItem.RepairAttemptHistory.Count);
                for (Int32 index = 0; index < limit; index++)
                {
                    builder.AppendLine("- " + taskItem.RepairAttemptHistory[index]);
                }
            }

            return builder.ToString().Trim();
        }

        private void AddAutoRecoveryTask(SmartTaskExecutionContext failedTask)
        {
            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                SmartTaskExecutionContext candidate = assignmentTaskItems[index];
                if (candidate == failedTask)
                {
                    continue;
                }

                if (!String.Equals(candidate.ParentTaskId, failedTask.AgentTaskId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (String.Equals(candidate.Type, "Recovery", StringComparison.OrdinalIgnoreCase))
                {
                    AppendTaskLog(failedTask, "Recovery task already queued; skipping duplicate.");
                    return;
                }
            }

            SmartTaskExecutionContext recoveryTask = new SmartTaskExecutionContext();
            recoveryTask.TaskNumber = assignmentTaskItems.Count + 1;
            recoveryTask.Label = "Investigate and repair failure of task: " + failedTask.Label;
            recoveryTask.Type = "Recovery";
            recoveryTask.AgentTaskId = assignmentController.EnsureUniqueAgentTaskId(failedTask.AgentTaskId + "-recovery");
            recoveryTask.AssociatedCommands = new List<AgentCommandDescription>();
            recoveryTask.CreatedAt = DateTime.Now;
            recoveryTask.AttemptCount = 0;
            recoveryTask.MaxRepairAttempts = maxRepairAttemptsPerTask;
            recoveryTask.SetStatus(AssignmentTaskStatusOptions.Planned);
            recoveryTask.TaskLogText = String.Empty;
            recoveryTask.ParentTaskId = failedTask.AgentTaskId;
            recoveryTask.Dependencies = new List<String>();
            recoveryTask.TaskContext = "Automatic recovery workflow for '" + failedTask.Label + "'.";
            recoveryTask.Priority = "Critical";
            recoveryTask.SetContextTags(new List<String> { "recovery", "self-heal" });
            assignmentController.AssignCreationOrder(recoveryTask);
            assignmentTaskItems.Add(recoveryTask);
            RenumberTasks();
            AppendTaskLog(recoveryTask, "Automatically added recovery task for failed task '" + failedTask.Label + "'.");
        }


        private async Task<Boolean> ConfirmCommandIfRequired(String commandDescription, Boolean isPotentiallyDangerous, Boolean isCriticallyDangerous)
        {
            if (automationHandsFreeMode)
            {
                AppendLog("Hands-free mode auto-confirmed command: " + commandDescription);
                return true;
            }

            if (!isPotentiallyDangerous)
            {
                return true;
            }

            String title = "Confirmation";
            String messagePrefix = isCriticallyDangerous ? "a critically dangerous" : "a potentially dangerous";
            String message = "The system is about to run " + messagePrefix + " command.";

            return await DialogService.ShowConfirmationAsync(this, title, message, "Execute", "Cancel", commandDescription, true);
        }

        private async Task<Boolean> ConfirmPolicyOverrideAsync(String commandDescription, PolicyRiskLevelOptions riskLevel)
        {
            if (automationHandsFreeMode)
            {
                String riskLabelForLog = FormatRiskLabelForDisplay(riskLevel);
                AppendLog("Hands-free mode paused for approval because the command exceeds the configured tolerance (" + riskLabelForLog + "): " + commandDescription);
            }

            String message = "This command exceeds your current tolerance.";
            return await DialogService.ShowConfirmationAsync(this, "Policy", message, "Yes", "Cancel", commandDescription, true);
        }




        private String BuildAnswerText(StructuredAgentResult plannerResponse)
        {
            String answer = plannerResponse.Answer ?? String.Empty;
            String explanation = plannerResponse.Explanation ?? String.Empty;

            String trimmedAnswer = answer.Trim();
            String trimmedExplanation = explanation.Trim();

            if (trimmedAnswer.Length == 0 && trimmedExplanation.Length == 0)
            {
                return String.Empty;
            }

            if (trimmedExplanation.Length == 0)
            {
                return trimmedAnswer;
            }

            if (trimmedAnswer.Length == 0)
            {
                return trimmedExplanation;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(trimmedAnswer);
            builder.AppendLine();
            builder.AppendLine(trimmedExplanation);
            return builder.ToString();
        }

        private String BuildCommandsSummaryText(StructuredAgentResult plannerResponse)
        {
            if (plannerResponse.Tasks == null || plannerResponse.Tasks.Count == 0)
            {
                if (plannerResponse.IsStructured)
                {
                    return "No tasks or commands provided by agent for this assignment.";
                }

                return "Agent response was not in the expected structured format; no tasks or commands parsed.";
            }

            Boolean hasAnyCommand = false;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Tasks and commands provided by agent:");

            for (Int32 index = 0; index < plannerResponse.Tasks.Count; index++)
            {
                AgentPlannedTask plannedTask = plannerResponse.Tasks[index];
                String taskIdText = String.IsNullOrWhiteSpace(plannedTask.Id) ? (index + 1).ToString(CultureInfo.InvariantCulture) : plannedTask.Id!;
                String taskLabelText = plannedTask.Label ?? String.Empty;
                String taskTypeText = FormatTaskTypeText(plannedTask.Type);

                builder.AppendLine();
                builder.AppendLine("Task " + taskIdText + " (" + taskTypeText + "): " + taskLabelText);

                List<String> metadataParts = new List<String>();
                if (!String.IsNullOrWhiteSpace(plannedTask.Priority))
                {
                    metadataParts.Add("Priority=" + plannedTask.Priority);
                }
                if (!String.IsNullOrWhiteSpace(plannedTask.Phase))
                {
                    metadataParts.Add("Phase=" + plannedTask.Phase);
                }
                if (plannedTask.ContextTags != null && plannedTask.ContextTags.Count > 0)
                {
                    metadataParts.Add("Tags=" + String.Join(",", plannedTask.ContextTags));
                }
                if (plannedTask.Dependencies != null && plannedTask.Dependencies.Count > 0)
                {
                    metadataParts.Add("Dependencies=" + String.Join(",", plannedTask.Dependencies));
                }
                if (metadataParts.Count > 0)
                {
                    builder.AppendLine("  Metadata: " + String.Join(" | ", metadataParts));
                }

                if (plannedTask.Commands == null || plannedTask.Commands.Count == 0)
                {
                    builder.AppendLine("  No commands for this task.");
                    continue;
                }

                hasAnyCommand = true;

                for (Int32 commandIndex = 0; commandIndex < plannedTask.Commands.Count; commandIndex++)
                {
                    AgentCommandDescription command = plannedTask.Commands[commandIndex];
                    String commandIdText = String.IsNullOrWhiteSpace(command.Id) ? (commandIndex + 1).ToString(CultureInfo.InvariantCulture) : command.Id!;
                    String dangerText = String.IsNullOrWhiteSpace(command.DangerLevel) ? "safe" : command.DangerLevel!;
                    String executableText = command.Executable ?? String.Empty;
                    String argumentsText = command.Arguments ?? String.Empty;
                    String workingDirectoryText = command.WorkingDirectory ?? "(workspace)";

                    builder.AppendLine("  Command " + commandIdText + " (" + dangerText + "):");
                    builder.AppendLine("    Working directory: " + workingDirectoryText);
                    builder.AppendLine("    Executable: " + executableText);
                    builder.AppendLine("    Arguments: " + argumentsText);
                    builder.AppendLine("    Description: " + (command.Description ?? String.Empty));
                }
            }

            if (!hasAnyCommand)
            {
                builder.AppendLine();
                builder.AppendLine("No commands were included in any task.");
            }

            return builder.ToString();
        }

        private static String FormatTaskTypeText(String? rawType)
        {
            if (String.IsNullOrWhiteSpace(rawType))
            {
                return String.Empty;
            }

            String trimmed = rawType.Trim();
            if (trimmed.Length == 1)
            {
                return trimmed.ToUpperInvariant();
            }

            Char firstChar = Char.ToUpperInvariant(trimmed[0]);
            String remaining = trimmed.Substring(1);
            return firstChar + remaining;
        }

        private void UpdateOutputsForPlannerResponse(StructuredAgentResult plannerResponse)
        {
            String answerText = BuildAnswerText(plannerResponse);
            String rawText = plannerResponse.RawContent ?? plannerResponse.Answer ?? String.Empty;
            String commandsText = BuildCommandsSummaryText(plannerResponse);
            assignmentController.UpdateAssignmentOutputs(answerText, rawText, commandsText);
        }

        private T? TryDeserializeJson<T>(String jsonText) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(jsonText, jsonSerializerOptions);
            }
            catch
            {
                return null;
            }
        }

        private String? ExtractJsonObject(String text)
        {
            Int32 firstIndex = text.IndexOf('{');
            Int32 lastIndex = text.LastIndexOf('}');
            if (firstIndex >= 0 && lastIndex > firstIndex)
            {
                Int32 length = lastIndex - firstIndex + 1;
                return text.Substring(firstIndex, length);
            }

            return null;
        }


        private async Task<Boolean> ApplyRepairAsync(SmartTaskExecutionContext failedTask, StructuredRepairResult repairResponse, CancellationToken cancellationToken)
        {
            String decision = repairResponse.RepairDecision ?? String.Empty;
            String decisionLower = decision.ToLowerInvariant();

            if (decisionLower == "give_up" || decisionLower.Length == 0)
            {
                AppendLog("Repair agent decided to give up for task '" + failedTask.Label + "'. Reason: " + (repairResponse.Reason ?? String.Empty));
                AppendTaskLog(failedTask, "Repair agent decided to give up. Reason: " + (repairResponse.Reason ?? String.Empty));
                return false;
            }

            if (decisionLower == "retry_with_new_commands")
            {
                if (repairResponse.ReplacementCommands == null || repairResponse.ReplacementCommands.Count == 0)
                {
                    AppendLog("Repair decision was retry_with_new_commands but no replacement commands were provided.");
                    AppendTaskLog(failedTask, "Repair decision was retry_with_new_commands but no replacement commands were provided.");
                    return false;
                }

                NormalizeCommandList(repairResponse.ReplacementCommands);
                failedTask.AssociatedCommands = repairResponse.ReplacementCommands;
                failedTask.RequiresCommandExecution = true;
                AppendLog("Repair agent provided replacement commands for task '" + failedTask.Label + "'. Reason: " + (repairResponse.Reason ?? String.Empty));
                AppendTaskLog(failedTask, "Repair agent provided replacement commands. Reason: " + (repairResponse.Reason ?? String.Empty));
                failedTask.RecordRepairHistoryEntry("Replacement commands queued. Reason: " + (repairResponse.Reason ?? String.Empty));
                return true;
            }

            if (decisionLower == "add_new_tasks")
            {
                if (repairResponse.NewTasks == null || repairResponse.NewTasks.Count == 0)
                {
                    AppendLog("Repair decision was add_new_tasks but no new tasks were provided.");
                    AppendTaskLog(failedTask, "Repair decision was add_new_tasks but no new tasks were provided.");
                    return false;
                }
                InsertPlannedTasksAfter(failedTask, repairResponse.NewTasks);
                assignmentController.AllowDependentsToProceed(
                    failedTask,
                    "Dependents unlocked for repair task orchestration.",
                    AppendTaskLog);

                AppendLog("Repair agent added new tasks after failed task '" + failedTask.Label + "'. Reason: " + (repairResponse.Reason ?? String.Empty));
                AppendTaskLog(failedTask, "Repair agent added new tasks. Reason: " + (repairResponse.Reason ?? String.Empty));
                failedTask.RecordRepairHistoryEntry("Inserted new tasks before retry. Reason: " + (repairResponse.Reason ?? String.Empty));
                return true;
            }

            AppendLog("Repair decision '" + decision + "' is not recognized. Giving up on repair for task '" + failedTask.Label + "'.");
            AppendTaskLog(failedTask, "Repair decision '" + decision + "' is not recognized. Giving up on repair.");
            return false;
        }

        private void InsertPlannedTasksAfter(SmartTaskExecutionContext referenceTask, List<AgentPlannedTask> plannedTasks)
        {
            if (plannedTasks == null || plannedTasks.Count == 0)
            {
                return;
            }

            Int32 insertionIndex = assignmentTaskItems.IndexOf(referenceTask);
            if (insertionIndex < 0)
            {
                insertionIndex = assignmentTaskItems.Count - 1;
            }

            insertionIndex = insertionIndex + 1;
            DateTime now = DateTime.Now;
            List<SmartTask> newSmartTasks = new List<SmartTask>();

            for (Int32 index = 0; index < plannedTasks.Count; index++)
            {
                AgentPlannedTask planned = plannedTasks[index];
                SmartTaskExecutionContext newTask = CreateAssignmentTaskFromPlan(planned, referenceTask.AgentTaskId, now);
                SmartTask? smartTaskNode = assignmentController.FindSmartTaskForAssignmentTask(newTask);
                if (smartTaskNode != null)
                {
                    newSmartTasks.Add(smartTaskNode);
                }

                if (insertionIndex >= 0 && insertionIndex <= assignmentTaskItems.Count)
                {
                    assignmentTaskItems.Insert(insertionIndex, newTask);
                    insertionIndex = insertionIndex + 1;
                }
                else
                {
                    assignmentTaskItems.Add(newTask);
                }
            }

            RenumberTasks();
            ScheduleSmartTasksForImmediateExecution(newSmartTasks);
        }

        private SmartTaskExecutionContext CreateAssignmentTaskFromPlan(AgentPlannedTask plannedTask, String? parentTaskId, DateTime createdAt)
        {
            SmartTaskExecutionContext newTaskItem = new SmartTaskExecutionContext();
            newTaskItem.TaskNumber = assignmentTaskItems.Count + 1;
            newTaskItem.Label = String.IsNullOrWhiteSpace(plannedTask.Label) ? "Task " + newTaskItem.TaskNumber : plannedTask.Label!;
            newTaskItem.Type = plannedTask.Type ?? "Task";
            String preferredId = plannedTask.Id ?? Guid.NewGuid().ToString();
            String uniqueId = assignmentController.EnsureUniqueAgentTaskId(preferredId);
            newTaskItem.AgentTaskId = uniqueId;
            plannedTask.Id = uniqueId;
            newTaskItem.AssociatedCommands = plannedTask.Commands ?? new List<AgentCommandDescription>();
            Boolean hasExecutableCommands = newTaskItem.AssociatedCommands.Count > 0;
            newTaskItem.CreatedAt = createdAt;
            newTaskItem.AttemptCount = 0;
            newTaskItem.MaxRepairAttempts = maxRepairAttemptsPerTask;
            newTaskItem.SetStatus(AssignmentTaskStatusOptions.Planned);
            newTaskItem.TaskLogText = String.Empty;
            newTaskItem.ParentTaskId = parentTaskId;
            newTaskItem.Dependencies = plannedTask.Dependencies != null ? new List<String>(plannedTask.Dependencies) : new List<String>();
            newTaskItem.RequiresCommandExecution = hasExecutableCommands;

            if (!String.IsNullOrWhiteSpace(parentTaskId))
            {
                Boolean alreadyDepends = false;
                for (Int32 index = 0; index < newTaskItem.Dependencies.Count; index++)
                {
                    if (String.Equals(newTaskItem.Dependencies[index], parentTaskId, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyDepends = true;
                        break;
                    }
                }

                if (!alreadyDepends)
                {
                    newTaskItem.Dependencies.Add(parentTaskId);
                }
            }

            if (!String.IsNullOrWhiteSpace(plannedTask.Context))
            {
                newTaskItem.TaskContext = plannedTask.Context;
            }
            else if (!String.IsNullOrWhiteSpace(plannedTask.Description))
            {
                newTaskItem.TaskContext = plannedTask.Description;
            }

            newTaskItem.Priority = plannedTask.Priority ?? newTaskItem.Priority;
            newTaskItem.Phase = plannedTask.Phase;
            newTaskItem.SetContextTags(plannedTask.ContextTags);
            newTaskItem.AllowsDependentsToProceed = !hasExecutableCommands;
            assignmentController.AssignCreationOrder(newTaskItem);

            SmartTask smartTaskNode = assignmentController.GetOrCreateSmartTaskNode(newTaskItem.AgentTaskId, newTaskItem, parentTaskId, newTaskItem.Label);
            assignmentController.ApplyWorkBudgetToSmartTask(smartTaskNode, assignmentController.CalculateSmartTaskDepth(smartTaskNode));

            return newTaskItem;
        }


        public void OnSmartTaskDetailsMenuItemClick(object? sender, RoutedEventArgs e)
        {
            MenuItem? menuItem = sender as MenuItem;
            if (menuItem == null)
            {
                return;
            }

            SmartTask? smartTask = menuItem.CommandParameter as SmartTask;
            if (smartTask == null)
            {
                smartTask = menuItem.DataContext as SmartTask;
            }

            if (smartTask == null && menuItem.DataContext is SmartTaskExecutionContext context)
            {
                smartTask = assignmentController.FindSmartTaskForAssignmentTask(context);
            }

            if (smartTask == null)
            {
                return;
            }

            ShowSmartTaskWindow(smartTask);
            e.Handled = true;
        }

        public void OnSmartTaskMonitorMenuItemClick(object? sender, RoutedEventArgs e)
        {
            MenuItem? menuItem = sender as MenuItem;
            if (menuItem == null)
            {
                return;
            }

            SmartTaskExecutionContext? taskItem = menuItem.CommandParameter as SmartTaskExecutionContext;
            if (taskItem == null)
            {
                taskItem = menuItem.DataContext as SmartTaskExecutionContext;
            }

            if (taskItem == null && menuItem.DataContext is SmartTask smartTask)
            {
                taskItem = smartTask.BoundAssignmentTask;
            }

            if (taskItem == null)
            {
                return;
            }

            ShowTaskMonitorWindow(taskItem);
            e.Handled = true;
        }

        public void OnMonitorMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            Object? tag = menuItem.Tag;
            SmartTaskExecutionContext? taskItem = tag as SmartTaskExecutionContext;
            if (taskItem == null)
            {
                if (menuItem.DataContext is SmartTaskExecutionContext contextItem)
                {
                    taskItem = contextItem;
                }
                else
                {
                    return;
                }
            }

            ShowTaskMonitorWindow(taskItem);
        }

        private void ShowTaskMonitorWindow(SmartTaskExecutionContext taskItem)
        {
            InvokeOnUiThread(() =>
            {
                if (taskMonitorWindows.TryGetValue(taskItem, out TaskMonitorWindow? existingWindow) && existingWindow != null)
                {
                    if (existingWindow.WindowState == WindowState.Minimized)
                    {
                        existingWindow.WindowState = WindowState.Normal;
                    }

                    existingWindow.Activate();
                    return;
                }

                TaskMonitorWindow monitorWindow = new TaskMonitorWindow(taskItem);
                monitorWindow.Closed += OnTaskMonitorWindowClosed;
                taskMonitorWindows[taskItem] = monitorWindow;
                monitorWindow.Show(this);
            });
        }

        private void OnTaskMonitorWindowClosed(Object? sender, EventArgs e)
        {
            TaskMonitorWindow? monitorWindow = sender as TaskMonitorWindow;
            if (monitorWindow == null)
            {
                return;
            }

            SmartTaskExecutionContext monitoredTaskItem = monitorWindow.MonitoredTaskItem;

            if (taskMonitorWindows.ContainsKey(monitoredTaskItem))
            {
                taskMonitorWindows.Remove(monitoredTaskItem);
            }
        }

        private void ShowAssignmentFailedDialogForTask(SmartTaskExecutionContext failedTask)
        {
            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.Append("The assignment task '");
            messageBuilder.Append(failedTask.Label);
            messageBuilder.Append("' failed and could not be completed automatically. Please review the task log and the System Log for details.");

            String detailsText = failedTask.TaskLogText;
            if (String.IsNullOrWhiteSpace(detailsText))
            {
                detailsText = failedTask.LastResultText ?? String.Empty;
            }

            ShowAssignmentFailedDialog(
                "Assignment task failed",
                messageBuilder.ToString(),
                detailsText);
        }

        private void ShowAssignmentFailedDialog(String titleText, String messageText, String? detailsText)
        {
            if (hasShownAssignmentFailureDialog)
            {
                return;
            }

            hasShownAssignmentFailureDialog = true;
            InvokeOnUiThread(() =>
            {
                AssignmentFailedDialog dialog = new AssignmentFailedDialog(titleText, messageText, detailsText ?? String.Empty);
                _ = dialog.ShowDialog(this);
            });
        }

        private void ShowSmartTaskWindow(SmartTask task)
        {
            if (task == null)
            {
                return;
            }

            
            assignmentController.EnsureExecutionContextForSmartTask(task, maxRepairAttemptsPerTask);

            InvokeOnUiThread(() =>
            {
                SmartTaskWindow window = new SmartTaskWindow(task);
                window.Show(this);
            });
        }
    }
}



