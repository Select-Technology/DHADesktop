using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.ProjectProperties;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using Microsoft.Web.WebView2.Core;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using Microsoft.Xrm.Sdk;
using WinForms = System.Windows.Forms; 

namespace DHA.DSTC.WPF
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Private Fields
        private readonly DataverseConnector _dataverseConnector;
        private readonly TimeEntryService _timeEntryService;
        private readonly ProjectService _projectService;
        private readonly TeamMemberService _teamMemberService;
        private readonly CalendarService _calendarService;
        private readonly DisbursementService _disbursementService;
        private bool _suppressDisbursementReload = false;
        private System.Threading.Timer _disbursementSearchTimer;

        private bool _isEditingTimeEntry = false;
        private bool _isEditingDisbursement = false;
        private readonly object _editLock = new object();
        private DateTime _lastEditAttempt = DateTime.MinValue;
        private const int EDIT_COOLDOWN_MS = 500; // Prevent rapid successive edits

        // Collections for data binding
        private ObservableCollection<TimeEntry> _timeEntries;
        private ObservableCollection<Project> _projects;
        private ObservableCollection<TeamMember> _teamMembers;
        private ObservableCollection<Disbursement> _disbursements;
        private ObservableCollection<DisbursementType> _disbursementTypes;

        private readonly ObservableCollection<Quote> _quotes = new ObservableCollection<Quote>();
        private Quote _selectedQuote;
        private bool _isAddingTimeEntry = false;
        private System.Threading.Timer _disbursementProjectSearchTimer;
        private System.Threading.Timer _disbursementQuoteSearchTimer;
        private bool _isClearingDisbursementForm = false;

        // System tray
        private WinForms.NotifyIcon _notifyIcon;
        private bool _isExitingFromTray = false;

        // Calendar data
        private DateTime _currentCalendarMonth;
        private Dictionary<DateTime, decimal> _dailyHours;
        private readonly object _calendarLock = new object();
        private CancellationTokenSource _calendarCancellationToken;

        // Calendar UI elements (created once, reused for performance)
        private readonly Border[] _calendarCells = new Border[42]; // 6 weeks * 7 days
        private readonly TextBlock[] _dayLabels = new TextBlock[42];
        private readonly TextBlock[] _hoursLabels = new TextBlock[42];
        private readonly DateTime[] _calendarCellDates = new DateTime[42]; // Store dates for click navigation

        // Performance optimisation - cached brushes (removed old hardcoded ones)
        private static readonly SolidColorBrush _transparentBrush = Brushes.Transparent;
        private static readonly SolidColorBrush _todayBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly SolidColorBrush _otherMonthBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));

        // Current user tracking
        private TeamMember _currentUser;
        private ColleagueConfiguration _currentUserConfig;

        // Project search timer
        private System.Threading.Timer _searchTimer;
        private const int SearchDelayMs = 300; // 300ms delay after typing stops

        // Pinned jobs and recent search history
        private SearchStateManager _searchStateManager;

        // Sticky time entry date
        private DateTime _lastSelectedTimeEntryDate = DateTime.Today;

        // Carry-over: last project selected in the time entry panel
        private Project _lastTimeEntryProject;

        // Suppresses the DateRange_Changed reload while ExpandDateRangeToInclude is adjusting pickers
        private bool _suppressDateRangeChange = false;

        // Daily progress tracking
        private decimal _todayExpectedHours = 8.0m;
        private decimal _todayActualHours = 0.0m;
        private bool _isActivatingFromSecondaryInstance = false;
        private bool _isAddingDisbursement = false;

        // Progress bar colors (for time entry bar)
        private static readonly SolidColorBrush _progressGreenBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        private static readonly SolidColorBrush _progressBlueBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
        private static readonly SolidColorBrush _progressOrangeBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        private static readonly SolidColorBrush _progressRedBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));

        private static readonly SolidColorBrush _calendarGreenBrush = new SolidColorBrush(Color.FromRgb(220, 252, 231)); // Light green (100%+)
        private static readonly SolidColorBrush _calendarLightOrangeBrush = new SolidColorBrush(Color.FromRgb(255, 237, 213)); // Very light orange (75-99%)
        private static readonly SolidColorBrush _calendarOrangeBrush = new SolidColorBrush(Color.FromRgb(254, 215, 170)); // Light orange (50-74%)
        private static readonly SolidColorBrush _calendarRedBrush = new SolidColorBrush(Color.FromRgb(254, 226, 226)); // Light
                                                                                                                       //
                                                                                                                       // Muted versions for past/future dates (progress bar)
        private static readonly SolidColorBrush _progressMutedGreenBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        private static readonly SolidColorBrush _progressMutedBlueBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241));
        private static readonly SolidColorBrush _progressMutedOrangeBrush = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        private static readonly SolidColorBrush _progressMutedRedBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));

        // Text and UI colors
        private static readonly SolidColorBrush _calendarTextBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        private static readonly SolidColorBrush _calendarHoursBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        private static readonly SolidColorBrush _calendarHoursContrastBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99)); // Darker text for colored backgrounds
        private static readonly SolidColorBrush _calendarOtherMonthBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
        private static readonly SolidColorBrush _calendarLightGrayBrush = new SolidColorBrush(Color.FromRgb(249, 250, 251));
        private static readonly SolidColorBrush _calendarTodayBorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly SolidColorBrush _calendarNormalBorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
        // red (<50%)
        private Ellipse[] _calendarDots; // Store references to dot indicators
        private bool _isIntelGPU = false; // Track if using Intel GPU

        private bool _useProgressBarFallback = false;
        private bool _hasTestedProgressBarRendering = false;
        private int _renderingTestAttempts = 0;
        private const int MAX_RENDERING_TEST_ATTEMPTS = 3;
        private System.Threading.Timer _dateCheckTimer;

        private readonly object _calendarDataLock = new object();
        private readonly SemaphoreSlim _calendarUpdateSemaphore = new SemaphoreSlim(1, 1);
        private volatile bool _isCalendarUpdateInProgress = false;
        #endregion

        #region Properties for Data Binding
        public ObservableCollection<TimeEntry> TimeEntries
        {
            get => _timeEntries;
            set
            {
                _timeEntries = value;
                OnPropertyChanged(nameof(TimeEntries));
            }
        }

        public ObservableCollection<Project> Projects
        {
            get => _projects;
            set
            {
                _projects = value;
                OnPropertyChanged(nameof(Projects));
            }
        }

   

        public ObservableCollection<TeamMember> TeamMembers
        {
            get => _teamMembers;
            set
            {
                _teamMembers = value;
                OnPropertyChanged(nameof(TeamMembers));
            }
        }

        public ObservableCollection<Disbursement> Disbursements
        {
            get => _disbursements;
            set
            {
                _disbursements = value;
                OnPropertyChanged(nameof(Disbursements));
            }
        }

        public ObservableCollection<DisbursementType> DisbursementTypes
        {
            get => _disbursementTypes;
            set
            {
                _disbursementTypes = value;
                OnPropertyChanged(nameof(DisbursementTypes));
            }
        }
        #endregion

        #region Constructor and Initialisation
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            _searchStateManager = new SearchStateManager();
            InitializeWebBrowserComponents();
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
            // Initialise services using ServiceLocator
            if (!ServiceLocator.Initialize())
            {
                MessageBox.Show($"Failed to initialise services: {ServiceLocator.LastError}",
                    "Initialisation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            _dataverseConnector = ServiceLocator.DataverseConnector;
            _timeEntryService = ServiceLocator.TimeEntryService;
            _projectService = ServiceLocator.ProjectService;
            _teamMemberService = ServiceLocator.TeamMemberService;
            _calendarService = ServiceLocator.CalendarService;
            _disbursementService = ServiceLocator.DisbursementService;

            // Initialise collections
            TimeEntries = new ObservableCollection<TimeEntry>();
            Projects = new ObservableCollection<Project>();
            TeamMembers = new ObservableCollection<TeamMember>();
            Disbursements = new ObservableCollection<Disbursement>();
            DisbursementTypes = new ObservableCollection<DisbursementType>();
            _dailyHours = new Dictionary<DateTime, decimal>();

            // Set up calendar
            _currentCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            // FIXED: Set all date pickers to TODAY for proper initialization
            TimeEntryDatePicker.SelectedDate = DateTime.Today;
            DisbursementDatePicker.SelectedDate = DateTime.Today;

            // FIXED: Set date range to TODAY so we see today's entries by default
            FromDatePicker.SelectedDate = DateTime.Today;  // Changed from AddDays(-1)
            ToDatePicker.SelectedDate = DateTime.Today;

            // FIXED: Initialize sticky date to today
            _lastSelectedTimeEntryDate = DateTime.Today;

            ProjectToggle.IsChecked = true;
            QuoteToggle.IsChecked = false;

            InitializeSystemTray();
            InitializeEventHandlers();
            InitializeCalendarStructure();

            // Populate pinned items and recent searches from saved state
            RefreshPinnedProjectsUI();
            RefreshPinnedQuotesUI();
            RefreshRecentProjectSearchesUI();
            RefreshRecentQuoteSearchesUI();

            LoadInitialData();
           
        }

        // Replace the previous tab selection handler with this simpler version
        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(e.Source is TabControl tabControl)) return;

            // Calendar tab (index 1) update — only when WebBrowser is ready
            if (tabControl.SelectedIndex == 1 && e.RemovedItems.Count > 0 && _webBrowserReady)
            {
                Task.Delay(100).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() => UpdateCalendarWebBrowser());
                });
            }

            // Disbursements tab (index 2) carry-over — always runs, no browser dependency
            if (tabControl.SelectedIndex == 2 && e.RemovedItems.Count > 0 && _lastTimeEntryProject != null)
            {
                Task.Delay(150).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() => PreSelectCarryOverProject(_lastTimeEntryProject));
                });
            }
        }

        private void InitializeEventHandlers()
        {
            // Time Entry events
            //AddTimeEntryButton.Click += AddTimeEntryButton_Click;
            ProjectSearchBox.TextChanged += ProjectSearchBox_TextChanged;
            ProjectSearchBox.GotFocus += ProjectSearchBox_GotFocus;
            ProjectsList.SelectionChanged += ProjectsList_SelectionChanged; 
            RefreshEntriesButton.Click += RefreshEntriesButton_Click;
            TimeEntriesDataGrid.MouseDoubleClick += TimeEntriesDataGrid_MouseDoubleClick;
            TimeEntriesDataGrid.KeyDown += TimeEntriesDataGrid_KeyDown;
            TimeEntryDatePicker.SelectedDateChanged += TimeEntryDatePicker_SelectedDateChanged;


            // Quote events - MAKE SURE THESE LINES EXIST:
            QuoteSearchBox.TextChanged += QuoteSearchBox_TextChanged;
            QuoteSearchBox.GotFocus += QuoteSearchBox_GotFocus;
            QuotesList.SelectionChanged += QuotesList_SelectionChanged;

            // Disbursement events
            AddDisbursementButton.Click += AddDisbursementButton_Click;
            DisbursementProjectSearchBox.TextChanged += DisbursementProjectSearchBox_TextChanged;
            DisbursementProjectSearchBox.GotFocus += DisbursementProjectSearchBox_GotFocus;
            DisbursementProjectsList.SelectionChanged += DisbursementProjectsList_SelectionChanged;
            ClearProjectSelectionButton.Click += ClearProjectSelectionButton_Click;
            RefreshDisbursementsButton.Click += RefreshDisbursementsButton_Click;
            DisbursementTypeComboBox.SelectionChanged += DisbursementTypeComboBox_SelectionChanged;
            DisbursementUnitsTextBox.TextChanged += DisbursementUnitsTextBox_TextChanged;
            DisbursementsDataGrid.MouseDoubleClick += DisbursementsDataGrid_MouseDoubleClick;
            DisbursementsDataGrid.KeyDown += DisbursementsDataGrid_KeyDown;

              // ADD THIS LINE  
            QuotesList.SelectionChanged += QuotesList_SelectionChanged;      // ADD T

            // Date picker events
            FromDatePicker.SelectedDateChanged += DateRange_Changed;
            ToDatePicker.SelectedDateChanged += DateRange_Changed;

            // Calendar events
            //PreviousMonthButton.Click += PreviousMonthButton_Click;
           // NextMonthButton.Click += NextMonthButton_Click;

            //ProgressCanvas.SizeChanged += (s, e) => UpdateProgressBar();

            // Team member selection
            TeamMemberComboBox.SelectionChanged += TeamMemberComboBox_SelectionChanged;

            // Window events
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;

            // Set up context menu for time entries
            SetupTimeEntriesContextMenu();

            MainTabControl.SelectionChanged += (s, e) =>
            {
                if (e.Source == MainTabControl) OnImportantStateChanged();
            };

            // Update existing handlers to trigger session saves
            TeamMemberComboBox.SelectionChanged += (s, e) => OnImportantStateChanged();
            FromDatePicker.SelectedDateChanged += (s, e) => OnImportantStateChanged();
            ToDatePicker.SelectedDateChanged += (s, e) => OnImportantStateChanged();
            TimeEntryDatePicker.SelectedDateChanged += (s, e) => OnImportantStateChanged();
            DisbursementProjectsList.SelectionChanged += (s, e) => OnImportantStateChanged();
            DisbursementTypeComboBox.SelectionChanged += (s, e) => OnImportantStateChanged();
        }

        private void InitializeSystemTray()
        {
            _notifyIcon = new WinForms.NotifyIcon();

            // Try to load app.ico from resources
            try
            {
                // First try to get the icon from the application resources
                var iconUri = new Uri("pack://application:,,,/Resources/app.ico");
                var iconStream = Application.GetResourceStream(iconUri);

                if (iconStream != null)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream.Stream);
                }
                else
                {
                    // Try alternative method - get from assembly
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("DHA.DSTC.WPF.Resources.app.ico"))
                    {
                        if (stream != null)
                        {
                            _notifyIcon.Icon = new System.Drawing.Icon(stream);
                        }
                        else
                        {
                            // Final fallback - use application icon
                            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not load app.ico: {ex.Message}");
                // Fallback - this should always work
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon.Text = "DHA Time Management";
            _notifyIcon.Visible = true;

            // Create context menu
            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("Show Application", null, (s, e) => ShowApplication());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double-click to show
            _notifyIcon.DoubleClick += (s, e) => ShowApplication();
        }

        private async Task LoadInitialData()
        {
            try
            {
                FileLogger.Info("=== LoadInitialData START ===");
                FileLogger.LogAppState();

                // Only proceed with data loading if successfully connected
                UpdateStatus("Loading data...");

                // CRITICAL FIX: Retry user identification if it failed initially
                // Skip this when impersonating — the CallerId is already set and we don't want
                // ServiceLocator.Connect() to call GetUserInfo() which would reset it.
                if (!ServiceLocator.IsImpersonating)
                {
                    int userRetryAttempts = 0;
                    while ((ServiceLocator.CurrentUserId == Guid.Empty || string.IsNullOrEmpty(ServiceLocator.CurrentUserName) || ServiceLocator.CurrentUserName.StartsWith("Error")) && userRetryAttempts < 3)
                    {
                        userRetryAttempts++;
                        System.Diagnostics.Debug.WriteLine($"LoadInitialData: User identification attempt {userRetryAttempts}");

                        UpdateStatus("Connecting to Dataverse...");

                        // Run connection on a background thread so the UI stays responsive
                        // while CrmServiceClient establishes the OAuth session.
                        await Task.Run(() => ServiceLocator.Connect(forceReconnect: false, showMessages: false));

                        System.Diagnostics.Debug.WriteLine($"LoadInitialData: After retry {userRetryAttempts}: UserId={ServiceLocator.CurrentUserId}, UserName='{ServiceLocator.CurrentUserName}'");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LoadInitialData: Impersonating {ServiceLocator.ImpersonatedUserName} — skipping user identification retry to preserve CallerId");
                }

                // Load team members first (with filtering)
                await LoadTeamMembersAsync();
                // Load colleague configuration for current user
                await LoadColleagueConfigurationAsync();
                // Load projects
                await LoadProjectsAsync();
                // Load quotes for both time entries AND disbursements
                FileLogger.Info("LoadInitialData: About to call LoadQuotesAsync");
                await LoadQuotesAsync();
                FileLogger.Info("LoadInitialData: LoadQuotesAsync completed");
                await LoadDisbursementQuotesAsync(); // Load quotes for disbursement panel
                FileLogger.Info("LoadInitialData: LoadDisbursementQuotesAsync completed");
                
                // 🔍 DIAGNOSTIC: Investigate why Q27557 is not showing
                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("🔍 Running diagnostic for quote Q27557...");
                ServiceLocator.QuoteService.DiagnoseQuote("Q27557");
                System.Diagnostics.Debug.WriteLine("");
                
                                                     // Load disbursement types
                await LoadDisbursementTypesAsync();
                // Load recent time entries for current user
                await LoadTimeEntriesAsync();
                // Load recent disbursements for current user
                await LoadDisbursementsAsync();
                // Load calendar data
                await LoadCalendarDataAsync();

                // Initialize disbursement toggles AFTER quotes are loaded
                InitializeDisbursementToggles();

                // 🔥 FIX: Update progress bar after all data is loaded
                Dispatcher.Invoke(() =>
                {
                    UpdateDailyProgress();
                    System.Diagnostics.Debug.WriteLine("LoadInitialData: Progress bar updated after initial data load");
                });

                UpdateStatus("Ready");
                UpdateConnectionStatus(true);
                FileLogger.Info("=== LoadInitialData COMPLETE ===");

                // Show dev environment switch button for authorised users
                ShowDevButtonIfAuthorised();
                // Show impersonate button for authorised admin users
                ShowImpersonateButtonIfAuthorised();
                UpdateImpersonateButtonAppearance();

                // Show connection success briefly
                if (ServiceLocator.CurrentUserName != "Not connected")
                {
                    var statusText = ServiceLocator.IsImpersonating
                        ? $"Connected as {ServiceLocator.CurrentUserName} — VIEWING AS {ServiceLocator.ImpersonatedUserName}"
                        : $"Connected as {ServiceLocator.CurrentUserName}";
                    UpdateStatus(statusText);

                    // Clear the status after a few seconds
                    await Task.Delay(3000);
                    if (StatusLabel.Text.StartsWith("Connected as"))
                    {
                        UpdateStatus("Ready");
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("LoadInitialData FAILED", ex);
                UpdateStatus($"Error loading data: {ex.Message}");
                UpdateConnectionStatus(false);
                System.Diagnostics.Debug.WriteLine($"LoadInitialData error: {ex}");
                MessageBox.Show(
                    $"Error initialising application:\n\n{ex.Message}\n\n" +
                    "The application will continue to run but some features may not be available.",
                    "Initialisation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        #endregion




        #region Daily Progress Methods
        // Replace the existing UpdateDailyProgress method with this fixed version:
        // Enhanced UpdateDailyProgress method that ensures accurate calculation:
        private void UpdateDailyProgress()
        {
            try
            {
                var selectedDate = TimeEntryDatePicker.SelectedDate ?? DateTime.Today;
                var currentUserId = ServiceLocator.CurrentUserId; // Same as main loading!

                System.Diagnostics.Debug.WriteLine($"🎯 UpdateDailyProgress using ServiceLocator ID: {currentUserId}");

                if (currentUserId == Guid.Empty) return;

                // Calculate expected hours
                _todayExpectedHours = 7.5m;
                if (_currentUserConfig != null)
                {
                    _todayExpectedHours = _currentUserConfig.GetExpectedHoursForDay(selectedDate.DayOfWeek);
                }

                // 🎯 CRITICAL: Calculate from TimeEntries collection using ServiceLocator ID
                _todayActualHours = TimeEntries
                    .Where(te => te.Date.Date == selectedDate.Date && te.TeamMemberId == currentUserId)
                    .Sum(te => te.Hours + (te.Minutes / 60.0m));

                System.Diagnostics.Debug.WriteLine($"📊 Daily Progress: Expected={_todayExpectedHours}, Actual={_todayActualHours}, Entries={TimeEntries.Count}");

                // Update progress display
                UpdateProgressWebBrowser();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateDailyProgress error: {ex.Message}");
            }
        }

        private void ShowReport(string report)
        {
            // Log to debug output
            System.Diagnostics.Debug.WriteLine(report);

            // Show in message box with copy option
            var result = MessageBox.Show(
                report + "\n\n📋 Copy to clipboard?",
                "🔍 Data Debug Report",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Clipboard.SetText(report);
                    UpdateStatus("📋 Debug report copied to clipboard");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Copy failed: {ex.Message}");
                }
            }
        }

        private async void DebugDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine("🔍 IMMEDIATE DATA DEBUG REPORT");
                report.AppendLine("=" + new string('=', 50));
                report.AppendLine();

                // Current user info
                var currentUserId = ServiceLocator.CurrentUserId;
                report.AppendLine($"👤 CURRENT USER:");
                report.AppendLine($"   ServiceLocator.CurrentUserId: {currentUserId}");
                report.AppendLine($"   _currentUser: {_currentUser?.FullName ?? "NULL"}");
                report.AppendLine($"   _currentUser.Id: {_currentUser?.Id ?? Guid.Empty}");
                report.AppendLine();

                // Date picker info
                report.AppendLine($"📅 DATE PICKERS:");
                report.AppendLine($"   FromDatePicker: {FromDatePicker.SelectedDate}");
                report.AppendLine($"   ToDatePicker: {ToDatePicker.SelectedDate}");
                report.AppendLine($"   TimeEntryDatePicker: {TimeEntryDatePicker.SelectedDate}");
                report.AppendLine($"   DateTime.Today: {DateTime.Today:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine();

                if (currentUserId == Guid.Empty)
                {
                    report.AppendLine("❌ CRITICAL: No current user ID - cannot proceed");
                    ShowReport(report.ToString());
                    return;
                }

                // Get ALL time entries
                UpdateStatus("🔍 Getting debug data...");
                var allTimeEntries = await Task.Run(() => _timeEntryService.GetTimeEntries());

                report.AppendLine($"📊 DATABASE TOTALS:");
                report.AppendLine($"   Total entries in database: {allTimeEntries?.Count ?? 0}");

                if (allTimeEntries == null || allTimeEntries.Count == 0)
                {
                    report.AppendLine("❌ NO ENTRIES IN DATABASE!");
                    ShowReport(report.ToString());
                    return;
                }

                // Find entries for current user
                var userEntries = allTimeEntries.Where(te => te.TeamMemberId == currentUserId).ToList();
                report.AppendLine($"   Entries for current user: {userEntries.Count}");
                report.AppendLine();

                if (userEntries.Count == 0)
                {
                    report.AppendLine("❌ NO ENTRIES FOR CURRENT USER!");
                    report.AppendLine();
                    report.AppendLine("🔍 CHECKING OTHER USER IDs (first 10):");
                    var distinctUsers = allTimeEntries.Select(te => te.TeamMemberId).Distinct().Take(10);
                    foreach (var userId in distinctUsers)
                    {
                        var count = allTimeEntries.Count(te => te.TeamMemberId == userId);
                        report.AppendLine($"   {userId}: {count} entries");
                    }
                    ShowReport(report.ToString());
                    return;
                }

                // Show recent entries for user
                report.AppendLine($"📝 RECENT ENTRIES FOR USER (last 10):");
                foreach (var entry in userEntries.OrderByDescending(ent => ent.Date).Take(10))
                {
                    var hours = entry.Hours + (entry.Minutes / 60.0m);
                    report.AppendLine($"   {entry.Date:yyyy-MM-dd HH:mm:ss} ({entry.Date.Kind}) - {hours:F2}h - '{entry.Comments}'");
                }
                report.AppendLine();

                // Check for today specifically
                var todayEntries = userEntries.Where(te => te.Date.Date == DateTime.Today).ToList();
                report.AppendLine($"📅 TODAY ({DateTime.Today:yyyy-MM-dd}) ENTRIES: {todayEntries.Count}");
                foreach (var entry in todayEntries)
                {
                    var hours = entry.Hours + (entry.Minutes / 60.0m);
                    report.AppendLine($"   {entry.Date:yyyy-MM-dd HH:mm:ss} - {hours:F2}h - '{entry.Comments}'");
                }
                report.AppendLine();

                // Check current date range
                var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today;
                var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;
                var rangeEntries = userEntries.Where(te => te.Date.Date >= fromDate.Date && te.Date.Date <= toDate.Date).ToList();

                report.AppendLine($"📊 ENTRIES IN CURRENT RANGE ({fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}): {rangeEntries.Count}");
                foreach (var entry in rangeEntries)
                {
                    var hours = entry.Hours + (entry.Minutes / 60.0m);
                    report.AppendLine($"   {entry.Date:yyyy-MM-dd HH:mm:ss} - {hours:F2}h - '{entry.Comments}'");
                }
                report.AppendLine();

                // Current TimeEntries collection status
                report.AppendLine($"🎯 CURRENT UI STATE:");
                report.AppendLine($"   TimeEntries.Count: {TimeEntries?.Count ?? 0}");
                report.AppendLine($"   DataGrid bound: {TimeEntriesDataGrid.ItemsSource != null}");
                report.AppendLine($"   Status: {StatusLabel.Text}");

                ShowReport(report.ToString());
                UpdateStatus("🔍 Debug data collected - check report");
            }
            catch (Exception ex)
            {
                var error = $"🔍 Debug failed: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
                ShowReport(error);
                UpdateStatus($"Debug error: {ex.Message}");
            }
        }

        // Enhanced UpdateProgressBar method with better debugging:
        private void UpdateProgressBar()
        {
            if (_webBrowserReady) UpdateProgressWebBrowser();
        }





        private Brush GetModernProgressBrush(decimal progressPercentage)
        {
            if (progressPercentage >= 100)
                return new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            else if (progressPercentage >= 75)
                return new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
            else if (progressPercentage >= 50)
                return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange
            else
                return new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
        }


        /*
        private void UpdateProgressBar()
        {
            // Update text
            ProgressText.Text = $"{_todayActualHours:F1}h";
            TargetText.Text = $"{_todayExpectedHours:F1}h";

            // Don't show progress bar if no expected hours for today (e.g., weekends)
            if (_todayExpectedHours <= 0)
            {
                ProgressBar.Width = 0;
                ProgressCanvas.Visibility = Visibility.Collapsed;
                return;
            }
            else
            {
                ProgressCanvas.Visibility = Visibility.Visible;
            }

            // Calculate progress percentage
            var progressPercentage = _todayExpectedHours > 0 ?
                Math.Min((_todayActualHours / _todayExpectedHours) * 100, 100) : 0;

            // Update progress bar width (ensure canvas has a width) - Fix type conversion
            if (ProgressCanvas.ActualWidth > 0)
            {
                var progressBarWidth = (ProgressCanvas.ActualWidth * (double)progressPercentage) / 100.0;
                ProgressBar.Width = Math.Max(0, progressBarWidth);
            }

            // Change color based on progress
            if (progressPercentage >= 100)
            {
                ProgressBar.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            }
            else if (progressPercentage >= 75)
            {
                ProgressBar.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
            }
            else if (progressPercentage >= 50)
            {
                ProgressBar.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange
            }
            else
            {
                ProgressBar.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            }

            CreateHourMarkers();
        }
        */
        
        #endregion

        private static readonly Dictionary<string, SolidColorBrush> _cachedBrushes =
    new Dictionary<string, SolidColorBrush>
    {
        ["ProgressGreen"] = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
        ["ProgressBlue"] = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
        ["ProgressOrange"] = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        ["ProgressRed"] = new SolidColorBrush(Color.FromRgb(239, 68, 68))
    };


        #region Time Entry Date Management
        private void TimeEntryDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TimeEntryDatePicker.SelectedDate.HasValue)
            {
                _lastSelectedTimeEntryDate = TimeEntryDatePicker.SelectedDate.Value;
                UpdateDailyProgress();
                UpdateDailyTimeSummary();
                UpdateBlockPreview(); // keep block preview in sync when start date changes
            }
        }
        #endregion

        #region Block Time Entry

        private void BlockModeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            BlockModePanel.Visibility = Visibility.Visible;
            // Default end date to start date so user just widens the range
            BlockEndDatePicker.SelectedDate = TimeEntryDatePicker.SelectedDate ?? DateTime.Today;
            AddTimeEntryButton.Content = "Add Block Entries";
            UpdateBlockPreview();
        }

        private void BlockModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            BlockModePanel.Visibility = Visibility.Collapsed;
            AddTimeEntryButton.Content = "Add Time Entry";
        }

        private void BlockEndDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
            => UpdateBlockPreview();

        private void SkipWeekendsCheckBox_Changed(object sender, RoutedEventArgs e)
            => UpdateBlockPreview();

        private void UpdateBlockPreview()
        {
            if (BlockModeCheckBox?.IsChecked != true || BlockPreviewBorder == null) return;

            if (!TimeEntryDatePicker.SelectedDate.HasValue || !BlockEndDatePicker.SelectedDate.HasValue)
            {
                BlockPreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var start = TimeEntryDatePicker.SelectedDate.Value.Date;
            var end   = BlockEndDatePicker.SelectedDate.Value.Date;
            bool skipWeekends = SkipWeekendsCheckBox.IsChecked == true;

            if (end < start)
            {
                BlockPreviewBorder.Visibility = Visibility.Visible;
                BlockPreviewLabel.Text = "⚠ End date must be on or after start date.";
                BlockPreviewLabel.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
                BlockPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                BlockPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165));
                AddTimeEntryButton.Content = "Add Block Entries";
                return;
            }

            var dates = GetBlockDates(start, end, skipWeekends);
            int count = dates.Count;

            if (count == 0)
            {
                BlockPreviewBorder.Visibility = Visibility.Visible;
                BlockPreviewLabel.Text = "⚠ No working days in selected range (all are weekend days).";
                BlockPreviewLabel.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
                BlockPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                BlockPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165));
                AddTimeEntryButton.Content = "Add Block Entries";
                return;
            }

            string rangeText = start.Date == end.Date
                ? start.ToString("ddd d MMM")
                : $"{start:ddd d MMM} \u2013 {end:ddd d MMM}";

            BlockPreviewBorder.Visibility = Visibility.Visible;
            BlockPreviewLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175));
            BlockPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255));
            BlockPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(191, 219, 254));
            BlockPreviewLabel.Text = $"\uD83D\uDCC5 Will create {count} {(count == 1 ? "entry" : "entries")}  \u00B7  {rangeText}" +
                                     (skipWeekends ? "\n(Weekends skipped)" : "");
            AddTimeEntryButton.Content = $"Add {count} {(count == 1 ? "Entry" : "Entries")}";
        }

        private static List<DateTime> GetBlockDates(DateTime start, DateTime end, bool skipWeekends)
        {
            var dates = new List<DateTime>();
            var current = start.Date;
            while (current <= end.Date)
            {
                if (!skipWeekends ||
                    (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday))
                    dates.Add(current);
                current = current.AddDays(1);
            }
            return dates;
        }

        private async Task AddBlockTimeEntriesAsync(decimal hours, int minutes,
            TimeEntryCategory category, string comments)
        {
            var start = TimeEntryDatePicker.SelectedDate.Value.Date;
            var end   = BlockEndDatePicker.SelectedDate.Value.Date;
            bool skipWeekends = SkipWeekendsCheckBox.IsChecked == true;
            bool isProjectMode = ProjectToggle.IsChecked == true;

            if (end < start)
            {
                MessageBox.Show("End date must be on or after start date.",
                    "Invalid Date Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dates = GetBlockDates(start, end, skipWeekends);
            if (dates.Count == 0)
            {
                MessageBox.Show("No working days found in the selected range.",
                    "No Days", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Snapshot all UI state on the UI thread before handing off to background thread
            Project selectedProject = isProjectMode ? (Project)ProjectsList.SelectedItem : null;
            Quote   selectedQuote   = !isProjectMode ? (Quote)QuotesList.SelectedItem : null;
            var userId = ServiceLocator.CurrentUserId != Guid.Empty
                         ? ServiceLocator.CurrentUserId : _currentUser.Id;

            UpdateStatus($"Creating {dates.Count} time {(dates.Count == 1 ? "entry" : "entries")}\u2026");

            // Run ALL creates inside ONE Task.Run so every call uses the same background thread.
            // The CRM SDK OrganizationServiceProxy has thread affinity — running separate
            // Task.Run calls per entry can silently fail after the first one.
            var results = await Task.Run(() =>
            {
                var list = new List<(DateTime date, bool ok)>();
                foreach (var date in dates)
                {
                    try
                    {
                        var te = new TimeEntry
                        {
                            Date         = new DateTime(date.Year, date.Month, date.Day),
                            Hours        = hours,
                            Minutes      = minutes,
                            Comments     = comments,
                            TeamMemberId = userId,
                            Category     = category
                        };

                        if (isProjectMode && selectedProject != null)
                        {
                            te.Classification = TimeEntryClassification.Project;
                            te.ProjectId      = selectedProject.Id;
                            te.ProjectName    = selectedProject.Name;
                            te.ProjectNumber  = selectedProject.Number;
                            te.ClientName     = selectedProject.Client;
                        }
                        else if (!isProjectMode && selectedQuote != null)
                        {
                            te.Classification = TimeEntryClassification.Quote;
                            te.QuoteId        = selectedQuote.Id;
                            te.QuoteName      = selectedQuote.Name;
                            te.QuoteNumber    = selectedQuote.QuoteNumber;
                            te.ClientName     = selectedQuote.Client;
                        }

                        var id = _timeEntryService.CreateTimeEntry(te);
                        list.Add((date, id != Guid.Empty));
                        System.Diagnostics.Debug.WriteLine(
                            $"Block entry {date:yyyy-MM-dd}: {(id != Guid.Empty ? "OK " + id : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        list.Add((date, false));
                        System.Diagnostics.Debug.WriteLine(
                            $"Block entry exception for {date:yyyy-MM-dd}: {ex.Message}");
                    }
                }
                return list;
            });

            int created = results.Count(r => r.ok);
            int failed  = results.Count(r => !r.ok);

            // Reset form
            HoursTextBox.Text = "0";
            MinutesTextBox.Text = "0";
            CommentsTextBox.Clear();
            ChargeableRadioButton.IsChecked = true;
            BlockModeCheckBox.IsChecked = false;

            // Expand the visible date range to cover the entries just created
            if (!FromDatePicker.SelectedDate.HasValue || start < FromDatePicker.SelectedDate.Value)
                FromDatePicker.SelectedDate = start;
            if (!ToDatePicker.SelectedDate.HasValue || end > ToDatePicker.SelectedDate.Value)
                ToDatePicker.SelectedDate = end;

            await LoadTimeEntriesAsync();
            UpdateDailyProgress();

            // Always refresh the calendar, navigating to the start month of the block if needed
            if (_currentCalendarMonth.Year != start.Year || _currentCalendarMonth.Month != start.Month)
                _currentCalendarMonth = new DateTime(start.Year, start.Month, 1);
            await LoadCalendarDataAsync();

            string resultMsg = failed == 0
                ? $"\u2713 {created} time {(created == 1 ? "entry" : "entries")} created."
                : $"{created} of {dates.Count} entries created. {failed} failed \u2014 check the date range and retry.";

            UpdateStatus(resultMsg);
        }

        #endregion

        #region Time Entry Editing





        /// <summary>
        /// Validates if a time entry can be edited
        /// </summary>
        private bool ValidateTimeEntryEdit(TimeEntry timeEntry)
        {
            if (timeEntry == null) return false;

            // Check if entry belongs to current user
            if (_currentUser == null || timeEntry.TeamMemberId != _currentUser.Id)
            {
                MessageBox.Show(
                    "You can only edit your own time entries.",
                    "Edit Restricted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            // Check if entry is still within edit window
            if (!timeEntry.IsEditable)
            {
                var lockDate = timeEntry.LockDate;
                MessageBox.Show(
                    $"This time entry cannot be edited as it was locked on {lockDate:dddd, dd MMMM yyyy} at 12:00 noon.\n\n" +
                    "Time entries can only be edited until 12 noon on the Monday following the work date.",
                    "Time Entry Locked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Opens the edit time entry dialog
        /// </summary>
        private async void EditTimeEntry(TimeEntry timeEntry)
        {
            if (!ValidateTimeEntryEdit(timeEntry))
                return;

            Window editDialog = null;

            // Determine if this is a project or quote time entry and open appropriate dialog
            if (timeEntry.Classification == TimeEntryClassification.Quote)
            {
                // Create quote edit dialog
                var quotes = await Task.Run(() => GetAvailableQuotes());
                editDialog = new EditQuoteTimeEntryDialog(timeEntry, quotes, _currentUser);
            }
            else
            {
                // 🔥 FIX: Ensure the referenced project is available for edit dialog
                var projects = await Task.Run(() => GetProjectsForEdit(timeEntry.ProjectId));
                editDialog = new EditTimeEntryDialog(timeEntry, projects, _currentUser);
            }

            if (editDialog.ShowDialog() == true)
            {
                TimeEntry updatedEntry = null;

                // Get the updated entry from the appropriate dialog
                if (editDialog is EditQuoteTimeEntryDialog quoteDialog)
                {
                    updatedEntry = quoteDialog.UpdatedTimeEntry;
                }
                else if (editDialog is EditTimeEntryDialog projectDialog)
                {
                    updatedEntry = projectDialog.UpdatedTimeEntry;
                }

                if (updatedEntry != null)
                {
                    try
                    {
                        UpdateStatus("Updating time entry...");

                        // Update in database
                        await Task.Run(() => _timeEntryService.UpdateTimeEntry(updatedEntry));

                        // Expand the visible date range so the edited entry is always shown
                        ExpandDateRangeToInclude(updatedEntry.Date);

                        // Reload all entries to ensure consistency
                        await LoadTimeEntriesAsync();

                        // Update progress after reloading entries (on UI thread)
                        await Dispatcher.InvokeAsync(() =>
                        {
                            UpdateDailyProgress();
                            UpdateStatus("Time entry updated successfully");
                        });

                        // Refresh calendar if showing current month
                        if (IsCurrentMonth())
                        {
                            await LoadCalendarDataAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Error updating time entry: {ex.Message}");
                        MessageBox.Show($"Error updating time entry: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private List<Project> GetProjectsForEdit(Guid requiredProjectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== GetProjectsForEdit for ProjectId: {requiredProjectId} ===");

                // 🔥 CRITICAL FIX: Don't use the limited Projects collection for edit dialogs
                // Load ALL projects including inactive ones for editing
                List<Project> allProjects;

                try
                {
                    System.Diagnostics.Debug.WriteLine("Loading ALL projects for edit dialog (including inactive)...");
                    allProjects = GetAllProjectsIncludingInactive();
                    System.Diagnostics.Debug.WriteLine($"✅ Retrieved {allProjects.Count} total projects for edit");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Failed to load all projects: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine("Falling back to main collection + database lookup...");

                    // Fallback: Start with main collection and add missing project
                    allProjects = Projects.ToList();

                    // Check if required project is missing from main collection
                    var existingProject = allProjects.FirstOrDefault(p => p.Id == requiredProjectId);
                    if (existingProject == null)
                    {
                        System.Diagnostics.Debug.WriteLine("❌ Required project NOT in main collection - fetching from database");

                        var missingProject = ServiceLocator.ProjectService?.GetProject(requiredProjectId);
                        if (missingProject != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"✅ Found missing project: {missingProject.Name} (Active: {missingProject.IsActive})");
                            allProjects.Add(missingProject);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("❌ Could not find required project in database");
                        }
                    }
                }

                // Sort alphabetically for consistent ordering
                var sortedProjects = allProjects.OrderBy(p => p.Name).ToList();

                // Verify the required project is now available
                var targetProject = sortedProjects.FirstOrDefault(p => p.Id == requiredProjectId);
                if (targetProject != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Target project confirmed in final list: {targetProject.Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ WARNING: Target project {requiredProjectId} still not found!");
                }

                System.Diagnostics.Debug.WriteLine($"Returning {sortedProjects.Count} projects for edit dialog");
                return sortedProjects;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Critical error in GetProjectsForEdit: {ex.Message}");
                // Emergency fallback
                return Projects?.ToList() ?? new List<Project>();
            }
        }

        private void DiagnoseProjectIssue(Guid requiredProjectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== PROJECT LOADING DIAGNOSTIC ===");
                System.Diagnostics.Debug.WriteLine($"Looking for project: {requiredProjectId}");

                // Test 1: Check main Projects collection
                var mainCollectionProject = Projects?.FirstOrDefault(p => p.Id == requiredProjectId);
                System.Diagnostics.Debug.WriteLine($"Found in main collection: {mainCollectionProject != null}");
                if (mainCollectionProject != null)
                {
                    System.Diagnostics.Debug.WriteLine($"  Name: {mainCollectionProject.Name}");
                    System.Diagnostics.Debug.WriteLine($"  Active: {mainCollectionProject.IsActive}");
                }

                // Test 2: Check GetAllProjectsIncludingInactive
                System.Diagnostics.Debug.WriteLine("Testing GetAllProjectsIncludingInactive...");
                var allProjects = GetAllProjectsIncludingInactive();
                System.Diagnostics.Debug.WriteLine($"GetAllProjectsIncludingInactive returned: {allProjects?.Count ?? 0} projects");

                var foundProject = allProjects?.FirstOrDefault(p => p.Id == requiredProjectId);
                System.Diagnostics.Debug.WriteLine($"Found in all projects: {foundProject != null}");
                if (foundProject != null)
                {
                    System.Diagnostics.Debug.WriteLine($"  Name: {foundProject.Name}");
                    System.Diagnostics.Debug.WriteLine($"  Active: {foundProject.IsActive}");
                }

                // Test 3: Direct database lookup
                System.Diagnostics.Debug.WriteLine("Testing direct ServiceLocator lookup...");
                var directProject = ServiceLocator.ProjectService?.GetProject(requiredProjectId);
                System.Diagnostics.Debug.WriteLine($"Direct lookup result: {directProject != null}");
                if (directProject != null)
                {
                    System.Diagnostics.Debug.WriteLine($"  Name: {directProject.Name}");
                    System.Diagnostics.Debug.WriteLine($"  Active: {directProject.IsActive}");
                }

                // Test 4: Show some sample projects to check the range
                System.Diagnostics.Debug.WriteLine("Sample projects from all projects (first 10):");
                if (allProjects != null)
                {
                    foreach (var project in allProjects.Take(10))
                    {
                        System.Diagnostics.Debug.WriteLine($"  {project.Id}: {project.Name} (Active: {project.IsActive})");
                    }
                }

                System.Diagnostics.Debug.WriteLine("=== END DIAGNOSTIC ===");

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Diagnostic error: {ex.Message}");
            }
        }

        private async void DebugSpecificEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine("🔍 SPECIFIC TIME ENTRY DIAGNOSTIC");
                report.AppendLine("Target Entry: TIM-9502 (Emma Hawkes, 22/09/2025, 5.00h, Tanyard)");
                report.AppendLine("=" + new string('=', 60));

                // Check current user context
                report.AppendLine($"🎯 CURRENT CONTEXT:");
                report.AppendLine($"   _currentUser: {_currentUser?.FullName ?? "NULL"}");
                report.AppendLine($"   _currentUser.Id: {_currentUser?.Id ?? Guid.Empty}");
                report.AppendLine($"   ServiceLocator.CurrentUserId: {ServiceLocator.CurrentUserId}");
                report.AppendLine($"   Date range: {FromDatePicker.SelectedDate} to {ToDatePicker.SelectedDate}");
                report.AppendLine();

                // Load all time entries
                UpdateStatus("🔍 Loading all time entries for diagnosis...");
                var allTimeEntries = await Task.Run(() => _timeEntryService.GetTimeEntries());

                report.AppendLine($"📊 DATA RETRIEVAL:");
                report.AppendLine($"   Total entries from service: {allTimeEntries?.Count ?? 0}");

                if (allTimeEntries == null || allTimeEntries.Count == 0)
                {
                    report.AppendLine("   ❌ NO ENTRIES RETURNED!");
                    ShowDiagnosticReport(report.ToString());
                    return;
                }

                // Look for September 22, 2025 entries
                var targetDate = new DateTime(2025, 9, 22);
                var dateEntries = allTimeEntries.Where(te => te.Date.Date == targetDate.Date).ToList();

                report.AppendLine($"📅 ENTRIES FOR {targetDate:dd/MM/yyyy}:");
                report.AppendLine($"   Total entries for this date: {dateEntries.Count}");
                foreach (var entry in dateEntries)
                {
                    var hours = entry.Hours + (entry.Minutes / 60.0m);
                    report.AppendLine($"     TeamMemberId: {entry.TeamMemberId} - {hours:F2}h - {entry.ProjectName} - '{entry.Comments}'");
                }
                report.AppendLine();

                // Look for 5-hour entries
                var fiveHourEntries = allTimeEntries.Where(te =>
                    Math.Abs((te.Hours + (te.Minutes / 60.0m)) - 5.0m) < 0.01m).ToList();

                report.AppendLine($"⏰ 5.00 HOUR ENTRIES:");
                report.AppendLine($"   Entries with exactly 5.00 hours: {fiveHourEntries.Count}");
                foreach (var entry in fiveHourEntries.Take(10))
                {
                    report.AppendLine($"     {entry.Date:dd/MM/yyyy} - {entry.TeamMemberId} - {entry.ProjectName}");
                }
                report.AppendLine();

                // Look for Tanyard project entries (C# 7.3 compatible)
                var tanyardEntries = allTimeEntries.Where(te =>
                    te.ProjectName != null && te.ProjectName.ToLowerInvariant().Contains("tanyard")).ToList();

                report.AppendLine($"🏗️ TANYARD PROJECT ENTRIES:");
                report.AppendLine($"   Tanyard project entries: {tanyardEntries.Count}");
                foreach (var entry in tanyardEntries)
                {
                    var hours = entry.Hours + (entry.Minutes / 60.0m);
                    report.AppendLine($"     {entry.Date:dd/MM/yyyy} - {entry.TeamMemberId} - {hours:F2}h - '{entry.Comments}'");
                }
                report.AppendLine();

                // Look for entries with "summary" or "HE meeting" in comments (TIM-9502 specific)
                var summaryEntries = allTimeEntries.Where(te =>
                    (te.Comments != null && te.Comments.ToLowerInvariant().Contains("summary")) ||
                    (te.Comments != null && te.Comments.ToLowerInvariant().Contains("meeting"))).ToList();

                report.AppendLine($"📝 ENTRIES WITH 'SUMMARY' OR 'MEETING':");
                report.AppendLine($"   Found: {summaryEntries.Count}");
                foreach (var entry in summaryEntries.Take(5))
                {
                    var hours = entry.Hours + (entry.Minutes / 60.0m);
                    report.AppendLine($"     {entry.Date:dd/MM/yyyy} - {entry.TeamMemberId} - {hours:F2}h - '{entry.Comments}'");
                }
                report.AppendLine();

                // Check current filtering results
                var currentUserId = ServiceLocator.CurrentUserId;
                var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today;
                var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;

                var currentFilteredEntries = allTimeEntries
                    .Where(te => te.TeamMemberId == currentUserId &&
                                te.Date.Date >= fromDate.Date &&
                                te.Date.Date <= toDate.Date)
                    .ToList();

                report.AppendLine($"🔍 CURRENT FILTERING RESULTS:");
                report.AppendLine($"   User filter: {currentUserId}");
                report.AppendLine($"   Date filter: {fromDate:dd/MM/yyyy} to {toDate:dd/MM/yyyy}");
                report.AppendLine($"   Filtered entries count: {currentFilteredEntries.Count}");
                report.AppendLine($"   TimeEntries collection count: {TimeEntries.Count}");
                report.AppendLine();

                // Check TeamMembers collection to find Emma Hawkes ID
                report.AppendLine($"👥 TEAM MEMBERS ANALYSIS:");
                report.AppendLine($"   TeamMembers collection count: {TeamMembers?.Count ?? 0}");

                if (TeamMembers != null)
                {
                    var emmaMembers = TeamMembers.Where(tm =>
                        tm.FullName != null &&
                        (tm.FullName.ToLowerInvariant().Contains("emma") || tm.FullName.ToLowerInvariant().Contains("hawkes"))).ToList();

                    report.AppendLine($"   Emma/Hawkes members found: {emmaMembers.Count}");
                    foreach (var member in emmaMembers)
                    {
                        report.AppendLine($"     {member.Id}: {member.FullName}");

                        // Check if this user has time entries
                        var memberEntries = allTimeEntries.Where(te => te.TeamMemberId == member.Id).Count();
                        report.AppendLine($"       Time entries for this user: {memberEntries}");
                    }
                }

                ShowDiagnosticReport(report.ToString());
                UpdateStatus("🔍 Specific entry diagnostic completed");

            }
            catch (Exception ex)
            {
                var error = $"Diagnostic failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(error);
                MessageBox.Show(error, "Diagnostic Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowDiagnosticReport(string report)
        {
            System.Diagnostics.Debug.WriteLine(report);

            var result = MessageBox.Show(report + "\n\nCopy to clipboard?",
                "Specific Entry Diagnostic", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                Clipboard.SetText(report);
            }
        }


        private List<Project> GetAvailableProjects()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== GetAvailableProjects Debug ===");

                List<Project> allProjects = null;

                // Load fresh projects including inactive ones
                if (ServiceLocator.ProjectService != null)
                {
                    System.Diagnostics.Debug.WriteLine("Using ServiceLocator.ProjectService to get ALL projects");
                    allProjects = GetAllProjectsIncludingInactive();
                    System.Diagnostics.Debug.WriteLine($"ServiceLocator returned {allProjects?.Count ?? 0} total projects");
                }

                // Fallback to existing collection if service fails
                if (allProjects == null || !allProjects.Any())
                {
                    System.Diagnostics.Debug.WriteLine("Falling back to Projects collection");
                    allProjects = Projects?.ToList() ?? new List<Project>();
                    System.Diagnostics.Debug.WriteLine($"Projects collection has {allProjects.Count} items");
                }

                if (allProjects.Any())
                {
                    System.Diagnostics.Debug.WriteLine("Available projects for edit dialog:");
                    foreach (var p in allProjects.Take(3))
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {p.Id}: {p.Name} (IsActive: {p.IsActive})");
                    }
                }

                System.Diagnostics.Debug.WriteLine("=== End GetAvailableProjects Debug ===");
                return allProjects;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetAvailableProjects: {ex.Message}");
                return Projects?.ToList() ?? new List<Project>();
            }
        }

        private List<Project> GetAllProjectsIncludingInactive()
        {
            try
            {
                if (!ServiceLocator.DataverseConnector.Connect())
                {
                    return new List<Project>();
                }

                // Query WITHOUT the active status filter
                var query = new QueryExpression("msdyn_project")
                {
                    ColumnSet = new ColumnSet(
                        "msdyn_subject",
                        "isc_projectnumbernew",
                        "msdyn_customer",
                        "statuscode"
                    ),
                    Orders = {
                new OrderExpression("msdyn_subject", OrderType.Ascending)
            }
                };

                // 🔥 CRITICAL: NO status filter here - we want ALL projects
                // This is different from the regular GetProjects() method

                query.PageInfo = new PagingInfo
                {
                    Count = 10000, // Higher limit for edit dialogs
                    PageNumber = 1
                };

                var result = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(query);

                if (result?.Entities == null)
                {
                    return new List<Project>();
                }

                var projects = result.Entities
                    .Select(Project.FromEntity)
                    .Where(p => p != null) // Only exclude null, keep inactive projects
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"GetAllProjectsIncludingInactive: Found {projects.Count} total projects (including inactive)");
                System.Diagnostics.Debug.WriteLine($"Active projects: {projects.Count(p => p.IsActive)}");
                System.Diagnostics.Debug.WriteLine($"Inactive projects: {projects.Count(p => !p.IsActive)}");

                return projects;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetAllProjectsIncludingInactive: {ex.Message}");
                return new List<Project>();
            }
        }


        /// <summary>
        /// Deletes a time entry after confirmation
        /// </summary>
        private async void DeleteTimeEntry(TimeEntry timeEntry)
        {
            if (!ValidateTimeEntryEdit(timeEntry))
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete this time entry?\n\n" +
                $"Date: {timeEntry.Date:dd/MM/yyyy}\n" +
                $"Hours: {timeEntry.Hours}h {timeEntry.Minutes}m\n" +
                $"Project: {timeEntry.ProjectName}\n" +
                $"Comments: {timeEntry.Comments}",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    UpdateStatus("Deleting time entry...");

                    // Delete from database
                    await Task.Run(() => _timeEntryService.DeleteTimeEntry(timeEntry.Id));

                    // Reload all entries to ensure consistency
                    await LoadTimeEntriesAsync();

                    // Update progress after reloading entries (on UI thread)
                    Dispatcher.Invoke(() =>
                    {
                        UpdateDailyProgress();
                    });

                    UpdateStatus("Time entry deleted successfully");

                    // Refresh calendar if showing current month
                    if (IsCurrentMonth())
                    {
                        await LoadCalendarDataAsync();
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error deleting time entry: {ex.Message}");
                    MessageBox.Show($"Error deleting time entry: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Sets up the context menu for the DataGrid
        /// </summary>
        private void SetupTimeEntriesContextMenu()
        {
            var contextMenu = new ContextMenu();

            // Edit menu item
            var editItem = new MenuItem
            {
                Header = "Edit Time Entry",
                Icon = new TextBlock { Text = "✏️", FontSize = 14 }
            };
            editItem.Click += (s, e) =>
            {
                if (TimeEntriesDataGrid.SelectedItem is TimeEntry selectedEntry)
                {
                    EditTimeEntry(selectedEntry);
                }
            };
            contextMenu.Items.Add(editItem);

            // Delete menu item
            var deleteItem = new MenuItem
            {
                Header = "Delete Time Entry",
                Icon = new TextBlock { Text = "🗑️", FontSize = 14 }
            };
            deleteItem.Click += (s, e) =>
            {
                if (TimeEntriesDataGrid.SelectedItem is TimeEntry selectedEntry)
                {
                    DeleteTimeEntry(selectedEntry);
                }
            };
            contextMenu.Items.Add(deleteItem);

            // Separator
            contextMenu.Items.Add(new Separator());

            // Add Disbursement menu item
            var addDisbursementItem = new MenuItem
            {
                Header = "💰 Add Disbursement for this project",
                Icon = new TextBlock { Text = "💰", FontSize = 14 }
            };
            addDisbursementItem.Click += (s, e) =>
            {
                if (TimeEntriesDataGrid.SelectedItem is TimeEntry selectedEntry)
                {
                    var project = _projects?.FirstOrDefault(p => p.Id == selectedEntry.ProjectId);
                    if (project != null)
                    {
                        _lastTimeEntryProject = project;
                        MainTabControl.SelectedIndex = 2;
                    }
                    else
                    {
                        MessageBox.Show("Could not find the project for this time entry.", "Project Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            };
            contextMenu.Items.Add(addDisbursementItem);

            // Set the context menu opening event to validate items
            contextMenu.Opened += (s, e) =>
            {
                var hasSelection = TimeEntriesDataGrid.SelectedItem != null;
                var canEdit = hasSelection && TimeEntriesDataGrid.SelectedItem is TimeEntry entry && entry.IsEditable && entry.TeamMemberId == _currentUser?.Id;

                editItem.IsEnabled = canEdit;
                deleteItem.IsEnabled = canEdit;
                addDisbursementItem.IsEnabled = hasSelection;

                // Update header text based on lock status
                if (hasSelection && TimeEntriesDataGrid.SelectedItem is TimeEntry selectedEntry)
                {
                    if (!selectedEntry.IsEditable)
                    {
                        editItem.Header = "Edit Time Entry (Locked)";
                        deleteItem.Header = "Delete Time Entry (Locked)";
                    }
                    else if (selectedEntry.TeamMemberId != _currentUser?.Id)
                    {
                        editItem.Header = "Edit Time Entry (Not Yours)";
                        deleteItem.Header = "Delete Time Entry (Not Yours)";
                    }
                    else
                    {
                        editItem.Header = "Edit Time Entry";
                        deleteItem.Header = "Delete Time Entry";
                    }
                }
            };

            TimeEntriesDataGrid.ContextMenu = contextMenu;
        }

        /// <summary>
        /// Handles double-click on time entries grid
        /// </summary>
        /// 
        private bool CanEditNow()
        {
            var now = DateTime.Now;
            if ((now - _lastEditAttempt).TotalMilliseconds < EDIT_COOLDOWN_MS)
            {
                System.Diagnostics.Debug.WriteLine($"Edit attempt blocked - too soon after last attempt (cooldown: {EDIT_COOLDOWN_MS}ms)");
                return false;
            }

            _lastEditAttempt = now;
            return true;
        }

        private Brush CreateColorBrushManually(byte r, byte g, byte b)
        {
            try
            {
                // Create a 1x1 pixel bitmap with the color
                var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgr32, null);

                // Write the color directly to the pixel buffer
                bitmap.Lock();
                unsafe
                {
                    int* pixels = (int*)bitmap.BackBuffer;
                    *pixels = (255 << 24) | (r << 16) | (g << 8) | b; // ARGB format
                }
                bitmap.AddDirtyRect(new Int32Rect(0, 0, 1, 1));
                bitmap.Unlock();

                // Create ImageBrush from the bitmap
                var imageBrush = new ImageBrush(bitmap);
                imageBrush.Freeze();
                return imageBrush;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manual color creation failed: {ex.Message}");
                return Brushes.Gray; // Fallback
            }
        }

        private void TimeEntriesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 🔥 CRITICAL FIX: Add comprehensive protection
            if (_isEditingTimeEntry)
            {
                System.Diagnostics.Debug.WriteLine("TimeEntriesDataGrid_MouseDoubleClick: Already editing time entry - ignoring");
                return;
            }

            if (!CanEditNow())
            {
                System.Diagnostics.Debug.WriteLine("TimeEntriesDataGrid_MouseDoubleClick: Edit cooldown active - ignoring");
                return;
            }

            // Ensure we actually clicked on a data row, not header or empty space
            var hitTest = e.OriginalSource as FrameworkElement;
            if (hitTest == null) return;

            // Check if we clicked on an actual data row
            var dataGridRow = hitTest.DataContext as TimeEntry;
            if (dataGridRow == null && TimeEntriesDataGrid.SelectedItem is TimeEntry selectedEntry)
            {
                dataGridRow = selectedEntry;
            }

            if (dataGridRow != null)
            {
                System.Diagnostics.Debug.WriteLine($"TimeEntriesDataGrid_MouseDoubleClick: Editing time entry {dataGridRow.Id}");
                EditTimeEntry(dataGridRow);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("TimeEntriesDataGrid_MouseDoubleClick: No valid time entry selected - ignoring");
            }
        }

        /// <summary>
        /// Handles key down events on the DataGrid for keyboard shortcuts
        /// </summary>
        private void TimeEntriesDataGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (TimeEntriesDataGrid.SelectedItem is TimeEntry selectedEntry)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                    case Key.F2:
                        EditTimeEntry(selectedEntry);
                        e.Handled = true;
                        break;

                    case Key.Delete:
                        if (Keyboard.Modifiers == ModifierKeys.None)
                        {
                            DeleteTimeEntry(selectedEntry);
                            e.Handled = true;
                        }
                        break;
                }
            }
        }

        #endregion

        #region Data Loading Methods
        private async Task LoadTeamMembersAsync()
        {
            try
            {
                var teamMembers = await Task.Run(() => _teamMemberService.GetTeamMembers());

                // FALLBACK: If ServiceLocator failed, try to get current user directly
                Guid fallbackUserId = Guid.Empty;
                if (ServiceLocator.CurrentUserId == Guid.Empty)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("LoadTeamMembersAsync: ServiceLocator user ID is empty, trying direct WhoAmI");
                        var authService = Services.DataverseAuthService.Instance;
                        if (authService?.IsConnected == true)
                        {
                            var whoAmI = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                            var whoAmIResponse = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)authService.OrganizationService.Execute(whoAmI);
                            fallbackUserId = whoAmIResponse.UserId;
                            System.Diagnostics.Debug.WriteLine($"LoadTeamMembersAsync: Direct WhoAmI returned: {fallbackUserId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"LoadTeamMembersAsync: Direct WhoAmI failed: {ex.Message}");
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    TeamMembers.Clear();

                    // Filter out users with # character and add to collection
                    foreach (var member in teamMembers.Where(tm => !tm.FullName.Contains("#")))
                    {
                        TeamMembers.Add(member);
                    }

                    TeamMemberComboBox.ItemsSource = TeamMembers;

                    // ENHANCED: Try multiple methods to find current user
                    _currentUser = null;

                    // Method 1: Use ServiceLocator ID
                    var userIdToFind = ServiceLocator.CurrentUserId != Guid.Empty ? ServiceLocator.CurrentUserId : fallbackUserId;

                    if (userIdToFind != Guid.Empty)
                    {
                        _currentUser = TeamMembers.FirstOrDefault(tm => tm.Id == userIdToFind);
                        if (_currentUser != null)
                        {
                            TeamMemberComboBox.SelectedItem = _currentUser;
                            System.Diagnostics.Debug.WriteLine($"LoadTeamMembersAsync: Found current user by ID: {_currentUser.FullName}");
                        }
                    }

                    // Method 2: Try to match by name if ID matching failed
                    if (_currentUser == null && !string.IsNullOrEmpty(ServiceLocator.CurrentUserName) && ServiceLocator.CurrentUserName != "Not connected")
                    {
                        var userName = ServiceLocator.CurrentUserName.ToLower();
                        _currentUser = TeamMembers.FirstOrDefault(tm =>
                            tm.FullName.ToLower().Contains(userName) ||
                            tm.Email?.ToLower().Contains(userName) == true);

                        if (_currentUser != null)
                        {
                            TeamMemberComboBox.SelectedItem = _currentUser;
                            System.Diagnostics.Debug.WriteLine($"LoadTeamMembersAsync: Found current user by name: {_currentUser.FullName}");
                        }
                    }

                    // Method 3: Last resort - show warning and use first user
                    if (_currentUser == null && TeamMembers.Any())
                    {
                        _currentUser = TeamMembers[0];
                        TeamMemberComboBox.SelectedItem = _currentUser;
                        System.Diagnostics.Debug.WriteLine($"LoadTeamMembersAsync: Could not identify current user, defaulting to first: {_currentUser.FullName}");

                        // Show warning dialog
                        MessageBox.Show(
                            $"Could not identify your user account automatically.\n\n" +
                            $"ServiceLocator UserId: {ServiceLocator.CurrentUserId}\n" +
                            $"ServiceLocator UserName: '{ServiceLocator.CurrentUserName}'\n" +
                            $"Fallback UserId: {fallbackUserId}\n\n" +
                            $"Currently showing data for: {_currentUser.FullName}\n\n" +
                            $"You can change the selected user in the dropdown if needed.",
                            "User Identification Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                    System.Diagnostics.Debug.WriteLine($"LoadTeamMembersAsync: Final current user: {_currentUser?.FullName ?? "None"}");
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading team members: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadTeamMembersAsync error: {ex.Message}");
            }
        }

        private async Task LoadColleagueConfigurationAsync()
        {
            try
            {
                if (ServiceLocator.CurrentUserId != Guid.Empty)
                {
                    _currentUserConfig = await Task.Run(() =>
                        ServiceLocator.ColleagueConfigurationService.GetColleagueConfiguration(ServiceLocator.CurrentUserId));
                }
                else
                {
                    _currentUserConfig = ColleagueConfiguration.CreateDefault();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading colleague configuration: {ex.Message}");
                _currentUserConfig = ColleagueConfiguration.CreateDefault();
            }
            UpdateDailyProgress();
        }

        private async Task LoadProjectsAsync()
        {
            try
            {
                var projects = await Task.Run(() => _projectService.GetProjects());

                Dispatcher.Invoke(() =>
                {
                    Projects.Clear();
                    foreach (var project in projects)
                    {
                        Projects.Add(project);
                    }

                    // Initially show limited projects for performance (Time Entry tab)
                    ProjectsList.ItemsSource = Projects.Take(50).ToList();

                    // Show all projects for disbursements
                    DisbursementProjectsList.ItemsSource = Projects;
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading projects: {ex.Message}");
            }
        }

        private async Task LoadDisbursementTypesAsync()
        {
            try
            {
                var allDisbursementTypes = await _disbursementService.GetAllDisbursementTypesAsync();

                // Filter out disbursement types used only by automation or not needed in UI
                var excludedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Standard Disbursement",
            "Subcontract Planning",
            "Planning Application",
            "Printing",
            "Subcontract Professional Services",
            "Sage Import"
        };

                var filteredTypes = allDisbursementTypes
                    .Where(t => !excludedTypes.Contains(t.Name))
                    .Select(t => new DisbursementType
                    {
                        Id = t.Id,
                        IdGuid = t.IdGuid,
                        Name = TransformDisbursementTypeName(t.Name),
                        Description = TransformDisbursementTypeDescription(t.Description),
                        UnitCharge = t.UnitCharge
                    })
                    .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    DisbursementTypes.Clear();
                    foreach (var type in filteredTypes)
                    {
                        DisbursementTypes.Add(type);
                    }

                    DisbursementTypeComboBox.ItemsSource = DisbursementTypes;
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading disbursement types: {ex.Message}");
            }
        }

        // Add this helper method to MainWindow.xaml.cs
        private string TransformDisbursementTypeName(string originalName)
        {
            // Transform specific names for better UI display
            switch (originalName?.Trim())
            {
                case "Mileage":
                    return "Mileage (Company Car)";
                case "Mileage (Personal)":
                case "Personal Mileage":
                case "Personal Car Mileage":
                case "Mileage - Personal":
                case "Mileage (Personal Car)":
                    return "Mileage (Personal Car)";
                case "Mileage (Company)":
                case "Company Mileage":
                case "Company Car Mileage":
                case "Mileage - Company":
                case "Mileage (Company Car)":
                    return "Mileage (Company Car)";
                case "Photocopying (B&W)":
                    return "Printing (B&W)";
                case "Photocopying (Colour)":
                    return "Printing (Colour)";
                default:
                    return originalName;
            }
        }

        // Add this helper method to MainWindow.xaml.cs
        private string TransformDisbursementTypeDescription(string originalDescription)
        {
            // Transform descriptions to match the new names
            if (originalDescription == null) return null;

            // Use simple Replace since we know the exact casing in our data
            return originalDescription
                .Replace("photocopying", "printing")
                .Replace("Photocopying", "Printing")
                .Replace("Black and white photocopying", "Black and white printing")
                .Replace("Colour photocopying", "Colour printing");
        }

        // Replace the existing LoadTimeEntriesAsync method with this fixed version:
        // Replace your existing LoadTimeEntriesAsync method in MainWindow.xaml.cs with this:
        private async Task LoadTimeEntriesAsync()
        {
            try
            {
                var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today;
                var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;

                System.Diagnostics.Debug.WriteLine($"=== LoadTimeEntriesAsync ===");
                System.Diagnostics.Debug.WriteLine($"Current User ID: {_currentUser?.Id}");
                System.Diagnostics.Debug.WriteLine($"Date Range: {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");

                // Pass the UI date range to the service so future/past entries are included
                var allTimeEntries = await Task.Run(() =>
                    _timeEntryService.GetTimeEntries(_currentUser?.Id, fromDate, toDate));

                System.Diagnostics.Debug.WriteLine($"Entries returned from database (for this user): {allTimeEntries.Count}");

                // Now filter by date range (user filter already applied at DB level)
                var filteredEntries = allTimeEntries
                    .Where(te => te.Date.Date >= fromDate.Date && te.Date.Date <= toDate.Date)
                    .OrderByDescending(te => te.Date)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Entries after date filtering: {filteredEntries.Count}");

                // Update UI on main thread
                await Dispatcher.InvokeAsync(() =>
                {
                    TimeEntries.Clear();

                    foreach (var entry in filteredEntries)
                    {
                        TimeEntries.Add(entry);
                    }

                    if (TimeEntriesDataGrid.ItemsSource != TimeEntries)
                    {
                        TimeEntriesDataGrid.ItemsSource = TimeEntries;
                    }

                    UpdateTimeEntriesSummary();
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateStatus($"Error loading time entries: {ex.Message}");
                });
            }
        }


        // Helper method for emergency mode
        private async Task ShowEmergencyAllEntries()
        {
            try
            {
                var allTimeEntries = await Task.Run(() => _timeEntryService.GetTimeEntries());
                var recentEntries = allTimeEntries.Take(50).ToList();

                await Dispatcher.InvokeAsync(() =>
                {
                    TimeEntries.Clear();
                    foreach (var entry in recentEntries)
                    {
                        TimeEntries.Add(entry);
                    }

                    if (TimeEntriesDataGrid.ItemsSource != TimeEntries)
                    {
                        TimeEntriesDataGrid.ItemsSource = TimeEntries;
                    }

                    UpdateTimeEntriesSummary();
                    UpdateStatus($"🚨 EMERGENCY: Showing {recentEntries.Count} recent entries (all users)");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Emergency mode failed: {ex.Message}");
            }
        }

        private async Task LoadDisbursementsAsync()
        {
            try
            {
                UpdateStatus("Loading disbursements...");

                // ✅ Use the BASIC method until enhanced is fixed
                var allDisbursements = await _disbursementService.GetAllDisbursementsBasicAsync();

                // OR if you want to try the enhanced (now fixed) version:
                // var allDisbursements = await _disbursementService.GetAllDisbursementsEnhanced();

                var recentDisbursements = allDisbursements
                    .Where(d => d.TeamMemberGuid == ServiceLocator.CurrentUserId)
                    .Take(50)
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    Disbursements.Clear();
                    foreach (var disbursement in recentDisbursements)
                    {
                        Disbursements.Add(disbursement);
                    }
                    DisbursementsDataGrid.ItemsSource = Disbursements;
                    UpdateDisbursementsSummary();
                });

                System.Diagnostics.Debug.WriteLine($"Loaded {recentDisbursements.Count} disbursements for current user");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading disbursements: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadDisbursementsAsync error: {ex}");
            }
        }


        private async Task<bool> TestEnhancedDisbursementFeatures()
        {
            try
            {
                // Try to load just one disbursement with enhanced features
                var testDisbursements = await _disbursementService.GetAllDisbursementsEnhanced();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UpdateTimeEntriesSummary()
        {
            var totalEntries = TimeEntries.Count;
            var totalHours = TimeEntries.Sum(te => te.TotalHours);

            TotalEntriesLabel.Text = $"Total entries: {totalEntries}";
            TotalHoursLabel.Text = $"Total: {FormatHoursMinutes(totalHours)}";
        }
        #endregion

        #region Time Entry Events
        // 1. FIXED AddTimeEntryButton_Click method - No duplicates, proper progress update
        private async void AddTimeEntryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (_currentUser == null)
                {
                    MessageBox.Show("Please select a team member.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Parse and validate hours and minutes
                if (!decimal.TryParse(HoursTextBox.Text, out decimal hours))
                {
                    hours = 0;
                }
                if (!int.TryParse(MinutesTextBox.Text, out int minutes))
                {
                    minutes = 0;
                }

                // Validate minutes range
                if (minutes < 0 || minutes >= 60)
                {
                    MessageBox.Show("Minutes must be between 0 and 59.\n\n" +
                                   "💡 Tip: If you have more than 60 minutes, convert to hours.\n" +
                                   "For example: Enter '1 hour 39 minutes' instead of '99 minutes'.",
                                   "Invalid Time Entry",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                    MinutesTextBox.Focus();
                    return;
                }

                if (hours == 0 && minutes == 0)
                {
                    MessageBox.Show("Please enter hours or minutes.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    HoursTextBox.Focus();
                    return;
                }

                // Validate single entry doesn't exceed 16 hours (reasonable maximum)
                decimal totalHoursForEntry = hours + (minutes / 60.0m);
                if (totalHoursForEntry > 16.0m)
                {
                    MessageBox.Show("Individual time entries cannot exceed 16 hours.\n\n" +
                                   "💡 For longer periods, please split across multiple entries or days.",
                                   "Entry Too Large",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    HoursTextBox.Focus();
                    return;
                }

                // Get entry date - NO DATE RESTRICTIONS for creating new entries
                // Users can create entries for ANY date (past, present, or future)
                // This allows for:
                // - Catching up on forgotten time entries
                // - Planning ahead (e.g., annual leave)
                // - Correcting historical data
                // The Monday noon lock ONLY applies to EDITING existing entries, not creation
                var entryDate = TimeEntryDatePicker.SelectedDate ?? DateTime.Today;

                // Validate 24-hour daily limit (this is the ONLY date-related validation for creation)
                decimal newTotalHours = hours + (minutes / 60.0m);

                // Calculate existing hours for this date and user
                decimal existingHours = TimeEntries
                    .Where(te => te.Date.Date == entryDate.Date && te.TeamMemberId == _currentUser.Id)
                    .Sum(te => te.Hours + (te.Minutes / 60.0m));

                decimal totalForDay = existingHours + newTotalHours;

                if (totalForDay > 24.0m)
                {
                    MessageBox.Show($"Cannot add time entry - daily limit exceeded.\n\n" +
                                  $"Current total for {entryDate:dd/MM/yyyy}: {FormatHoursMinutes(existingHours)}\n" +
                                  $"Trying to add: {FormatHoursMinutes(newTotalHours)}\n" +
                                  $"Total would be: {FormatHoursMinutes(totalForDay)}\n\n" +
                                  $"Maximum allowed is 24 hours per day.",
                                  "Daily Limit Exceeded",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);
                    return;
                }

                // Validate comments
                if (string.IsNullOrWhiteSpace(CommentsTextBox.Text))
                {
                    MessageBox.Show("Comments are required for all time entries. Please describe the work performed.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CommentsTextBox.Focus();
                    return;
                }

                // Validate selection based on toggle state
                bool isProjectMode = ProjectToggle.IsChecked == true;
                bool isQuoteMode = QuoteToggle.IsChecked == true;

                if (isProjectMode && ProjectsList.SelectedItem == null)
                {
                    MessageBox.Show("Please select a project.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (isQuoteMode && QuotesList.SelectedItem == null)
                {
                    MessageBox.Show("Please select a quote.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get selected category from radio buttons
                TimeEntryCategory selectedCategory = TimeEntryCategory.Chargeable; // Default
                if (NonChargeableRadioButton.IsChecked == true)
                    selectedCategory = TimeEntryCategory.NonChargeable;
                else if (SpeculativeRadioButton.IsChecked == true)
                    selectedCategory = TimeEntryCategory.Speculative;
                else if (HourlyRateRadioButton.IsChecked == true)
                    selectedCategory = TimeEntryCategory.HourlyRate;

                // Multi-day block entry — delegate to dedicated method
                if (BlockModeCheckBox?.IsChecked == true)
                {
                    await AddBlockTimeEntriesAsync(hours, minutes, selectedCategory, CommentsTextBox.Text.Trim());
                    return;
                }

                // ENHANCED DEBUG: Check date picker state
                System.Diagnostics.Debug.WriteLine($"=== ENHANCED DATE DEBUG ===");
                System.Diagnostics.Debug.WriteLine($"TimeEntryDatePicker.SelectedDate (raw): {TimeEntryDatePicker.SelectedDate}");
                System.Diagnostics.Debug.WriteLine($"TimeEntryDatePicker.SelectedDate.HasValue: {TimeEntryDatePicker.SelectedDate.HasValue}");

                if (TimeEntryDatePicker.SelectedDate.HasValue)
                {
                    var rawDate = TimeEntryDatePicker.SelectedDate.Value;
                    System.Diagnostics.Debug.WriteLine($"TimeEntryDatePicker.SelectedDate.Value (raw): {rawDate}");
                    System.Diagnostics.Debug.WriteLine($"TimeEntryDatePicker.SelectedDate.Value.Date: {rawDate.Date}");
                    System.Diagnostics.Debug.WriteLine($"TimeEntryDatePicker.SelectedDate.Value.Kind: {rawDate.Kind}");
                    System.Diagnostics.Debug.WriteLine($"TimeEntryDatePicker.SelectedDate.Value.ToString(): {rawDate.ToString("yyyy-MM-dd HH:mm:ss.fff")}");
                }

                System.Diagnostics.Debug.WriteLine($"DateTime.Today: {DateTime.Today}");
                System.Diagnostics.Debug.WriteLine($"DateTime.Today.Kind: {DateTime.Today.Kind}");
                System.Diagnostics.Debug.WriteLine($"DateTime.Now: {DateTime.Now}");
                System.Diagnostics.Debug.WriteLine($"DateTime.Now.Kind: {DateTime.Now.Kind}");
                System.Diagnostics.Debug.WriteLine($"DateTime.UtcNow: {DateTime.UtcNow}");

                // Check system timezone
                var timeZone = TimeZoneInfo.Local;
                System.Diagnostics.Debug.WriteLine($"Local TimeZone: {timeZone.DisplayName}");
                System.Diagnostics.Debug.WriteLine($"Is Daylight Saving Time: {timeZone.IsDaylightSavingTime(DateTime.Now)}");

                // Normalize the date to ensure consistency
                DateTime finalEntryDate;
                if (TimeEntryDatePicker.SelectedDate.HasValue)
                {
                    var datePickerValue = TimeEntryDatePicker.SelectedDate.Value;

                    // Approach 1: Just use .Date
                    var approach1 = datePickerValue.Date;
                    System.Diagnostics.Debug.WriteLine($"Approach 1 (.Date): {approach1:yyyy-MM-dd}");

                    // Approach 2: Create new DateTime with just date components (most reliable)
                    var approach2 = new DateTime(datePickerValue.Year, datePickerValue.Month, datePickerValue.Day);
                    System.Diagnostics.Debug.WriteLine($"Approach 2 (new DateTime): {approach2:yyyy-MM-dd}");

                    // Approach 3: Force to local time then get date
                    var approach3 = DateTime.SpecifyKind(datePickerValue, DateTimeKind.Local).Date;
                    System.Diagnostics.Debug.WriteLine($"Approach 3 (SpecifyKind Local): {approach3:yyyy-MM-dd}");

                    // Use approach 2 (most reliable)
                    finalEntryDate = approach2;
                    System.Diagnostics.Debug.WriteLine($"FINAL entryDate chosen: {finalEntryDate:yyyy-MM-dd}");
                }
                else
                {
                    // DatePicker is null - this shouldn't happen
                    finalEntryDate = DateTime.Today.Date;
                    TimeEntryDatePicker.SelectedDate = finalEntryDate;
                    System.Diagnostics.Debug.WriteLine($"DatePicker was null, using Today: {finalEntryDate:yyyy-MM-dd}");
                    MessageBox.Show("Date picker was invalid - reset to today", "Date Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                System.Diagnostics.Debug.WriteLine($"=== END DATE DEBUG ===");

                // Create time entry
                var timeEntry = new TimeEntry
                {
                    Date = finalEntryDate,
                    Hours = hours,
                    Minutes = minutes,
                    Comments = CommentsTextBox.Text.Trim(),
                    TeamMemberId = DHA.DSTC.WPF.Utilities.ServiceLocator.CurrentUserId != Guid.Empty
                        ? DHA.DSTC.WPF.Utilities.ServiceLocator.CurrentUserId
                        : _currentUser.Id,
                    Category = selectedCategory
                };

                // Set classification and related fields
                if (isProjectMode)
                {
                    var selectedProject = (Project)ProjectsList.SelectedItem;
                    timeEntry.Classification = TimeEntryClassification.Project;
                    timeEntry.ProjectId = selectedProject.Id;
                    timeEntry.ProjectName = selectedProject.Name;
                    timeEntry.ProjectNumber = selectedProject.Number;
                    timeEntry.ClientName = selectedProject.Client;
                }
                else if (isQuoteMode)
                {
                    var selectedQuote = (Quote)QuotesList.SelectedItem;
                    timeEntry.Classification = TimeEntryClassification.Quote;
                    timeEntry.QuoteId = selectedQuote.Id;
                    timeEntry.QuoteName = selectedQuote.Name;
                    timeEntry.QuoteNumber = selectedQuote.QuoteNumber;
                    timeEntry.ClientName = selectedQuote.Client;
                }

                // FINAL DEBUG: Log what actually got set
                System.Diagnostics.Debug.WriteLine($"=== FINAL TIME ENTRY DEBUG ===");
                System.Diagnostics.Debug.WriteLine($"TimeEntry.Date final value: {timeEntry.Date:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"TimeEntry.Date.Kind: {timeEntry.Date.Kind}");
                System.Diagnostics.Debug.WriteLine($"TimeEntry.ClientName: {timeEntry.ClientName}");

                UpdateStatus("Adding time entry...");
                var newId = await Task.Run(() => _timeEntryService.CreateTimeEntry(timeEntry));

                if (newId != Guid.Empty)
                {
                    // Clear form
                    HoursTextBox.Text = "0";
                    MinutesTextBox.Text = "0";
                    CommentsTextBox.Clear();

                    // Reset category to default
                    ChargeableRadioButton.IsChecked = true;

                    // Keep date and selection for convenience

                    // Expand the visible date range so the new entry is always shown
                    ExpandDateRangeToInclude(timeEntry.Date);

                    // Reload from database to ensure consistency
                    await LoadTimeEntriesAsync();

                    // Update progress after reloading entries
                    UpdateDailyProgress();

                    // Refresh calendar if showing current month
                    if (IsCurrentMonth())
                    {
                        await LoadCalendarDataAsync();
                    }

                    UpdateStatus("Time entry created successfully");
                }
                else
                {
                    UpdateStatus("Failed to create time entry");
                    MessageBox.Show("Failed to create time entry. Please try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error adding time entry: {ex.Message}");
                MessageBox.Show($"Error adding time entry: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                System.Diagnostics.Debug.WriteLine($"AddTimeEntryButton_Click exception: {ex}");
            }
        }

        private async void TestQ23480_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Testing Q23480...");

                var results = new List<string>();
                results.Add("=== TESTING Q23480 SPECIFICALLY ===");

                // Test 1: Raw query - get BOTH statecode and statuscode
                var rawQuery = new QueryExpression("quote")
                {
                    ColumnSet = new ColumnSet("quotenumber", "name", "statecode", "statuscode", "isc_projectnumbervisible"),
                    Criteria = new FilterExpression()
                };
                rawQuery.Criteria.AddCondition("quotenumber", ConditionOperator.Equal, "Q23480");

                var rawResult = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(rawQuery);
                results.Add($"Raw query found: {rawResult.Entities.Count} quotes");

                if (rawResult.Entities.Count > 0)
                {
                    var entity = rawResult.Entities[0];
                    var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value;
                    var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value;
                    var projectNum = entity.GetAttributeValue<string>("isc_projectnumbervisible");

                    results.Add($"Q23480 StateCode: {stateCode}");
                    results.Add($"Q23480 StatusCode: {statusCode}");
                    results.Add($"Q23480 ProjectNumberVisible: '{projectNum}'");

                    // Map the codes to human readable
                    var stateText = stateCode == 0 ? "Active" : stateCode == 1 ? "Inactive" : "Unknown";
                    var statusText = statusCode == 1 ? "Active" : statusCode == 2 ? "Won" : statusCode == 3 ? "Closed" : "Unknown";

                    results.Add($"Human readable - State: {stateText}, Status: {statusText}");

                    // Test 2: Quote.FromEntity conversion
                    var quote = Quote.FromEntity(entity);
                    results.Add($"Quote.FromEntity result: {quote?.IsActive} (IsActive)");
                    results.Add($"Quote object QuoteNumber: {quote?.QuoteNumber}");
                }
                else
                {
                    results.Add("❌ Q23480 NOT FOUND in raw query!");
                }

                // Show results
                var resultText = string.Join("\n", results);

                foreach (var result in results)
                {
                    System.Diagnostics.Debug.WriteLine(result);
                }

                MessageBox.Show(resultText, "Q23480 Detailed Diagnostic",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                UpdateStatus("Q23480 test completed");

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Diagnostic Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DiagnoseTimeEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prompt for time entry ID or details
                var inputDialog = InputDialog.ShowDialog(
                    "Enter Time Entry ID (GUID) or search criteria:\n\n" +
                    "Examples:\n" +
                    "• Full GUID: 12345678-1234-1234-1234-123456789012\n" +
                    "• Date: 2025-09-25\n" +
                    "• Hours: 1.5\n" +
                    "• Entry name/description: Meeting notes",
                    "Time Entry Diagnostic");

                if (string.IsNullOrWhiteSpace(inputDialog))
                {
                    return; // User canceled
                }

                UpdateStatus($"Diagnosing time entry: {inputDialog}...");

                var results = new List<string>();
                results.Add("=== TIME ENTRY DIAGNOSTIC ===");
                results.Add($"Search criteria: {inputDialog}");
                results.Add("");

                // Get current user context
                var currentUserId = ServiceLocator.CurrentUserId != Guid.Empty ? ServiceLocator.CurrentUserId : _currentUser?.Id ?? Guid.Empty;
                var currentUserName = ServiceLocator.CurrentUserName != "Not connected" ? ServiceLocator.CurrentUserName : _currentUser?.FullName ?? "Unknown";

                results.Add("--- CURRENT USER CONTEXT ---");
                results.Add($"ServiceLocator.CurrentUserId: {ServiceLocator.CurrentUserId}");
                results.Add($"ServiceLocator.CurrentUserName: '{ServiceLocator.CurrentUserName}'");
                results.Add($"_currentUser?.Id: {_currentUser?.Id}");
                results.Add($"_currentUser?.FullName: '{_currentUser?.FullName}'");
                results.Add($"Using User ID: {currentUserId}");
                results.Add($"Using User Name: {currentUserName}");
                results.Add("");

                if (currentUserId == Guid.Empty)
                {
                    results.Add("❌ ERROR: No valid user ID found!");
                    results.Add("This could be why time entries aren't showing.");
                    results.Add("Check authentication and user selection.");
                }
                else
                {
                    // Build query based on input
                    var query = new QueryExpression("fwp_timeentry")
                    {
                        ColumnSet = new ColumnSet(
                            "fwp_name",
                            "fwp_date",
                            "fwp_decimalhours",
                            "fwp_minutes",
                            "fwp_notes",
                            "fwp_teammember",
                            "fwp_category",
                            "fwp_classification",
                            "fwp_project",
                            "fwp_quote",
                            "createdon",
                            "createdby"
                        ),
                        Criteria = new FilterExpression()
                    };

                    // Determine search type and add appropriate filters
                    if (Guid.TryParse(inputDialog, out Guid timeEntryId))
                    {
                        // Search by GUID
                        query.Criteria.AddCondition("fwp_timeentryid", ConditionOperator.Equal, timeEntryId);
                        results.Add("Search type: GUID");
                    }
                    else if (DateTime.TryParse(inputDialog, out DateTime searchDate))
                    {
                        // Search by date
                        query.Criteria.AddCondition("fwp_date", ConditionOperator.Equal, searchDate.Date);
                        results.Add("Search type: Date");
                    }
                    else if (decimal.TryParse(inputDialog, out decimal hours))
                    {
                        // Search by hours
                        query.Criteria.AddCondition("fwp_decimalhours", ConditionOperator.Equal, hours);
                        results.Add("Search type: Hours");
                    }
                    else
                    {
                        // Text search - check multiple fields
                        results.Add("Search type: Text search (name, notes)");

                        var textSearchFilter = new FilterExpression(LogicalOperator.Or);
                        textSearchFilter.AddCondition("fwp_name", ConditionOperator.Like, $"%{inputDialog}%");
                        textSearchFilter.AddCondition("fwp_notes", ConditionOperator.Like, $"%{inputDialog}%");
                        query.Criteria.AddFilter(textSearchFilter);
                    }

                    // Add links to get related data
                    var teamMemberLink = query.AddLink("systemuser", "fwp_teammember", "systemuserid", JoinOperator.LeftOuter);
                    teamMemberLink.EntityAlias = "teammember";
                    teamMemberLink.Columns = new ColumnSet("fullname", "internalemailaddress");

                    var projectLink = query.AddLink("msdyn_project", "fwp_project", "msdyn_projectid", JoinOperator.LeftOuter);
                    projectLink.EntityAlias = "project";
                    projectLink.Columns = new ColumnSet("msdyn_subject", "isc_projectnumbernew");

                    var rawResult = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(query);
                    results.Add($"Found {rawResult.Entities.Count} time entries matching search criteria");
                    results.Add("");

                    // Filter for current user's entries specifically
                    var currentUserEntries = rawResult.Entities.Where(entity =>
                        entity.GetAttributeValue<EntityReference>("fwp_teammember")?.Id == currentUserId).ToList();

                    results.Add($"Of those, {currentUserEntries.Count} belong to current user");
                    results.Add("");

                    if (currentUserEntries.Count == 0)
                    {
                        results.Add("❌ NO TIME ENTRIES FOUND FOR CURRENT USER with the given criteria");

                        // Show what other users have entries matching the criteria
                        if (rawResult.Entities.Count > 0)
                        {
                            results.Add("");
                            results.Add("But found entries for other users:");
                            var otherUsers = rawResult.Entities
                                .Where(entity => entity.GetAttributeValue<EntityReference>("fwp_teammember")?.Id != currentUserId)
                                .GroupBy(entity => entity.GetAttributeValue<AliasedValue>("teammember.fullname")?.Value?.ToString() ?? "Unknown")
                                .ToList();

                            foreach (var userGroup in otherUsers)
                            {
                                results.Add($"  • {userGroup.Key}: {userGroup.Count()} entries");
                            }
                        }
                    }
                    else
                    {
                        // Analyze each current user entry
                        int entryIndex = 0;
                        foreach (var entity in currentUserEntries.Take(10))
                        {
                            entryIndex++;
                            results.Add($"--- TIME ENTRY {entryIndex} ---");

                            var entryId = entity.Id;
                            var entryName = entity.GetAttributeValue<string>("fwp_name") ?? "No Name";
                            var date = entity.GetAttributeValue<DateTime>("fwp_date");
                            var hours = entity.GetAttributeValue<decimal>("fwp_decimalhours");
                            var minutes = entity.GetAttributeValue<int>("fwp_minutes");
                            var notes = entity.GetAttributeValue<string>("fwp_notes");
                            var createdOn = entity.GetAttributeValue<DateTime>("createdon");

                            var projectName = entity.GetAttributeValue<AliasedValue>("project.msdyn_subject")?.Value?.ToString() ?? "No Project";

                            results.Add($"ID: {entryId}");
                            results.Add($"Name: '{entryName}'");
                            results.Add($"Date: {date:yyyy-MM-dd}");
                            results.Add($"Hours: {hours}h {minutes}m");
                            results.Add($"Notes: '{notes}'");
                            results.Add($"Project: {projectName}");
                            results.Add($"Created: {createdOn:yyyy-MM-dd HH:mm:ss}");

                            // Check filtering logic
                            results.Add("");
                            results.Add("FILTERING ANALYSIS:");

                            // Date range filtering
                            var fromDate = FromDatePicker?.SelectedDate ?? DateTime.Today.AddDays(-30);
                            var toDate = ToDatePicker?.SelectedDate ?? DateTime.Today;
                            var inDateRange = date.Date >= fromDate.Date && date.Date <= toDate.Date;
                            results.Add($"• Date range filter ({fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}): {(inDateRange ? "✅ PASS" : "❌ FAIL")}");

                            // User filtering (should always pass since we filtered for current user)
                            results.Add($"• User filter: ✅ PASS (belongs to current user)");

                            // Overall verdict
                            results.Add($"• OVERALL: Should be visible = {(inDateRange ? "✅ YES" : "❌ NO (wrong date range)")}");

                            results.Add("");
                        }

                        if (currentUserEntries.Count > 10)
                        {
                            results.Add($"... and {currentUserEntries.Count - 10} more entries (showing first 10 only)");
                            results.Add("");
                        }
                    }

                    // Test what shows in app methods
                    results.Add("--- APP METHODS TEST ---");
                    var appTimeEntries = _timeEntryService.GetTimeEntries();
                    var filteredEntries = appTimeEntries.Where(entry => entry.TeamMemberId == currentUserId).ToList();

                    results.Add($"GetTimeEntries() returned: {appTimeEntries.Count} total entries");
                    results.Add($"Filtered for current user: {filteredEntries.Count} entries");
                    results.Add($"Currently displayed: {TimeEntries.Count} entries");
                    results.Add("");

                    if (filteredEntries.Count > 0)
                    {
                        results.Add("Current user SHOULD see these entries:");
                        foreach (var entry in filteredEntries.Take(5))
                        {
                            results.Add($"  • {entry.Date:yyyy-MM-dd} - {entry.TotalHours}h - {entry.Comments}");
                        }
                        if (filteredEntries.Count > 5)
                        {
                            results.Add($"  ... and {filteredEntries.Count - 5} more");
                        }

                        results.Add("");
                        results.Add("DISCREPANCY ANALYSIS:");
                        if (filteredEntries.Count != TimeEntries.Count)
                        {
                            results.Add($"❌ MISMATCH: Should show {filteredEntries.Count}, actually showing {TimeEntries.Count}");
                            results.Add("This indicates a filtering or refresh issue in the UI.");
                        }
                        else
                        {
                            results.Add("✅ MATCH: Filtered count matches displayed count");
                        }
                    }
                    else
                    {
                        results.Add("❌ Current user would see NO entries in the app");
                        results.Add("This explains why the time entries list is empty.");
                    }
                }

                // Show results
                var resultText = string.Join("\n", results);

                foreach (var result in results)
                {
                    System.Diagnostics.Debug.WriteLine(result);
                }

                // Use a scrollable dialog for long results
                var window = new Window
                {
                    Title = "Time Entry Diagnostic Results",
                    Width = 900,
                    Height = 700,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = resultText,
                            Padding = new Thickness(10),
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize = 11
                        }
                    }
                };
                window.ShowDialog();

                UpdateStatus("Time entry diagnostic completed");

            }
            catch (Exception ex)
            {
                var error = $"Error: {ex.Message}\n\nStack: {ex.StackTrace}";
                System.Diagnostics.Debug.WriteLine(error);
                MessageBox.Show(error, "Diagnostic Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DiagnoseGetTimeEntriesMethod_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Diagnosing GetTimeEntries method...");

                var results = new List<string>();
                results.Add("=== GetTimeEntries() METHOD DIAGNOSIS ===");
                results.Add("");

                // Test the actual service method
                var serviceEntries = _timeEntryService.GetTimeEntries();
                results.Add($"TimeEntryService.GetTimeEntries() returned: {serviceEntries.Count} entries");

                if (serviceEntries.Count > 0)
                {
                    var dateGroups = serviceEntries
                        .GroupBy(entry => entry.Date.Date)
                        .OrderByDescending(group => group.Key)
                        .Take(10)
                        .ToList();

                    results.Add("");
                    results.Add("Date distribution of returned entries (last 10 dates):");

                    var todayDate = DateTime.Today;
                    bool foundToday = false;

                    foreach (var group in dateGroups)
                    {
                        var isToday = group.Key == todayDate;
                        if (isToday) foundToday = true;

                        var marker = isToday ? " <<<< TODAY" : "";
                        results.Add($"  {group.Key:yyyy-MM-dd}: {group.Count()} entries{marker}");
                    }

                    if (!foundToday)
                    {
                        results.Add("");
                        results.Add($"❌ TODAY ({todayDate:yyyy-MM-dd}) NOT FOUND in GetTimeEntries() results!");
                        results.Add("This proves the bug is in TimeEntryService.GetTimeEntries()");
                    }

                    // Show the newest and oldest entries
                    var newest = serviceEntries.OrderByDescending(entry => entry.Date).First();
                    var oldest = serviceEntries.OrderBy(entry => entry.Date).First();

                    results.Add("");
                    results.Add($"Newest entry: {newest.Date:yyyy-MM-dd HH:mm:ss}");
                    results.Add($"Oldest entry: {oldest.Date:yyyy-MM-dd HH:mm:ss}");
                }

                // Now test if entries exist in Dataverse for today
                results.Add("");
                results.Add("=== DIRECT DATAVERSE CHECK ===");

                var currentDate = DateTime.Today;
                var query = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet("fwp_date", "fwp_name", "createdon"),
                    Criteria = new FilterExpression()
                };
                query.Criteria.AddCondition("fwp_date", ConditionOperator.Equal, currentDate);

                var todaysEntries = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(query);
                results.Add($"Direct Dataverse query for today: {todaysEntries.Entities.Count} entries");

                if (todaysEntries.Entities.Count > 0)
                {
                    results.Add("Today's entries in Dataverse:");
                    foreach (var entity in todaysEntries.Entities)
                    {
                        var name = entity.GetAttributeValue<string>("fwp_name") ?? "No Name";
                        var date = entity.GetAttributeValue<DateTime>("fwp_date");
                        var created = entity.GetAttributeValue<DateTime>("createdon");

                        results.Add($"  • {name} - Date: {date:yyyy-MM-dd} - Created: {created:yyyy-MM-dd HH:mm:ss}");
                    }

                    results.Add("");
                    results.Add("❌ CRITICAL BUG CONFIRMED:");
                    results.Add($"  Dataverse has {todaysEntries.Entities.Count} entries for today");
                    results.Add($"  GetTimeEntries() returned 0 entries for today");
                    results.Add("  The bug is in TimeEntryService.GetTimeEntries() method!");
                }
                else
                {
                    results.Add("✅ No entries found in Dataverse for today - this would explain the issue");
                }

                // Show results
                var resultText = string.Join("\n", results);

                foreach (var result in results)
                {
                    System.Diagnostics.Debug.WriteLine(result);
                }

                var window = new Window
                {
                    Title = "GetTimeEntries Method Diagnosis",
                    Width = 900,
                    Height = 600,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = resultText,
                            Padding = new Thickness(10),
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize = 11
                        }
                    }
                };
                window.ShowDialog();

                UpdateStatus("GetTimeEntries diagnosis completed");

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Diagnosis Error", MessageBoxButton.OK);
            }
        }
        private async void TrackSpecificTimeEntries_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentUserId = ServiceLocator.CurrentUserId != Guid.Empty ? ServiceLocator.CurrentUserId : _currentUser?.Id ?? Guid.Empty;

                UpdateStatus("Tracking time entry processing pipeline...");

                var results = new List<string>();
                results.Add("=== TIME ENTRY PROCESSING PIPELINE TRACKING ===");
                results.Add($"Current User ID: {currentUserId}");
                results.Add("");

                // NEW STEP 0: User Context Diagnosis
                results.Add("STEP 0 - USER CONTEXT DIAGNOSIS");
                results.Add($"  ServiceLocator.CurrentUserId: {ServiceLocator.CurrentUserId}");
                results.Add($"  ServiceLocator.CurrentUserName: '{ServiceLocator.CurrentUserName}'");
                results.Add($"  _currentUser?.Id: {_currentUser?.Id}");
                results.Add($"  _currentUser?.FullName: '{_currentUser?.FullName}'");

                // Check for mismatch
                if (ServiceLocator.CurrentUserId != _currentUser?.Id)
                {
                    results.Add("  ❌ CRITICAL MISMATCH: ServiceLocator.CurrentUserId != _currentUser.Id");
                    results.Add($"     This means new entries will be created with wrong TeamMemberId!");
                    results.Add($"     ServiceLocator has: {ServiceLocator.CurrentUserId}");
                    results.Add($"     But current user is: {_currentUser?.Id}");
                }
                else if (ServiceLocator.CurrentUserId == Guid.Empty)
                {
                    results.Add("  ⚠️ WARNING: Both ServiceLocator and _currentUser have empty IDs");
                }
                else
                {
                    results.Add("  ✅ User context is synchronized correctly");
                }

                // Check actual Dataverse connection user
                try
                {
                    var whoAmIRequest = new Microsoft.Crm.Sdk.Messages.WhoAmIRequest();
                    var whoAmIResponse = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)
                        ServiceLocator.DataverseConnector._orgService.Execute(whoAmIRequest);

                    results.Add($"  Dataverse WhoAmI UserId: {whoAmIResponse.UserId}");

                    if (whoAmIResponse.UserId != currentUserId)
                    {
                        results.Add($"  ❌ AUTHENTICATION MISMATCH!");
                        results.Add($"     Dataverse connection: {whoAmIResponse.UserId}");
                        results.Add($"     Application context: {currentUserId}");
                        results.Add($"     This could cause permission issues!");
                    }
                    else
                    {
                        results.Add($"  ✅ Dataverse connection matches application context");
                    }
                }
                catch (Exception ex)
                {
                    results.Add($"  ⚠️ Could not verify Dataverse connection: {ex.Message}");
                }

                results.Add("");

                // Step 1: Use existing GetTimeEntries() method with detailed analysis
                results.Add("STEP 1 - TimeEntryService.GetTimeEntries() Analysis");
                var allEntries = _timeEntryService.GetTimeEntries();
                results.Add($"  Returned: {allEntries.Count} total entries");

                if (allEntries.Count > 0)
                {
                    var dateGroups = allEntries
                        .GroupBy(entry => entry.Date.Date)
                        .OrderByDescending(group => group.Key)
                        .Take(10)
                        .ToList();

                    results.Add("  Date distribution (last 10 dates):");
                    var todayDate = DateTime.Today;
                    bool foundToday = false;

                    foreach (var group in dateGroups)
                    {
                        var isToday = group.Key == todayDate;
                        if (isToday) foundToday = true;

                        var marker = isToday ? " <<<< TODAY" : "";
                        results.Add($"    {group.Key:yyyy-MM-dd}: {group.Count()} entries{marker}");
                    }

                    if (!foundToday)
                    {
                        results.Add($"  ❌ TODAY ({todayDate:yyyy-MM-dd}) NOT FOUND in GetTimeEntries() results!");
                    }
                    else
                    {
                        results.Add($"  ✅ TODAY ({todayDate:yyyy-MM-dd}) FOUND in GetTimeEntries() results");
                    }
                }

                // Step 2: Apply user filtering WITH DETAILED ID ANALYSIS
                results.Add("");
                results.Add("STEP 2 - User Filtering WITH ID VERIFICATION");
                var userEntries = allEntries.Where(entry => entry.TeamMemberId == currentUserId).ToList();
                results.Add($"  After user filter: {userEntries.Count} entries");

                // CRITICAL: Check what TeamMemberIds actually exist in today's entries
                var todayAllEntries = allEntries.Where(entry => entry.Date.Date == DateTime.Today).ToList();
                results.Add($"  All entries for today (before user filter): {todayAllEntries.Count}");

                if (todayAllEntries.Count > 0)
                {
                    results.Add("  Today's entries with their actual TeamMemberIds:");
                    foreach (var entry in todayAllEntries.Take(5))
                    {
                        var matchesCurrent = entry.TeamMemberId == currentUserId;
                        var marker = matchesCurrent ? " <<<< MATCHES CURRENT USER" : " <<<< DIFFERENT USER";
                        results.Add($"    • {entry.Comments} - TeamMemberId: {entry.TeamMemberId}{marker}");
                    }

                    // Show unique TeamMemberIds for today
                    var uniqueIds = todayAllEntries.Select(entry => entry.TeamMemberId).Distinct().ToList();
                    results.Add($"  Unique TeamMemberIds in today's entries: {uniqueIds.Count}");
                    foreach (var id in uniqueIds)
                    {
                        var count = todayAllEntries.Count(entry => entry.TeamMemberId == id);
                        var isCurrent = id == currentUserId;
                        var marker = isCurrent ? " <<<< THIS IS CURRENT USER" : "";
                        results.Add($"    {id}: {count} entries{marker}");
                    }
                }

                // Step 3: Apply date filtering with detailed analysis
                results.Add("");
                results.Add("STEP 3 - Date Range Filtering (CRITICAL ANALYSIS)");
                var fromDate = FromDatePicker?.SelectedDate ?? DateTime.Today.AddDays(-30);
                var toDate = ToDatePicker?.SelectedDate ?? DateTime.Today;
                results.Add($"  Date range: {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");
                results.Add($"  FromDate.Date: {fromDate.Date:yyyy-MM-dd HH:mm:ss}");
                results.Add($"  ToDate.Date: {toDate.Date:yyyy-MM-dd HH:mm:ss}");

                // Test each entry's date filtering individually
                results.Add("  Individual entry date analysis:");
                var todayEntries = userEntries.Where(entry => entry.Date.Date == DateTime.Today).ToList();

                if (todayEntries.Count > 0)
                {
                    results.Add($"  Found {todayEntries.Count} entries for TODAY before date range filter:");
                    foreach (var entry in todayEntries.Take(3))
                    {
                        var passesFromDate = entry.Date.Date >= fromDate.Date;
                        var passesToDate = entry.Date.Date <= toDate.Date;
                        var passesRange = passesFromDate && passesToDate;

                        results.Add($"    • {entry.Date:yyyy-MM-dd HH:mm:ss} - Passes: {passesRange} (>={passesFromDate}, <={passesToDate})");
                    }
                }
                else
                {
                    results.Add("  ❌ NO entries for today found in user entries!");
                }

                var dateFilteredEntries = userEntries
                    .Where(entry => entry.Date.Date >= fromDate.Date && entry.Date.Date <= toDate.Date)
                    .OrderByDescending(entry => entry.Date)
                    .ToList();
                results.Add($"  After date filter: {dateFilteredEntries.Count} entries");

                // Step 4: Enhanced TeamMember Assignment Analysis
                results.Add("");
                results.Add("STEP 4 - DEEP DIVE: TEAM MEMBER ASSIGNMENT ANALYSIS");

                var today = DateTime.Today;
                var todayQuery = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet("fwp_name", "fwp_teammember", "fwp_date", "createdon", "createdby", "modifiedby"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };
                todayQuery.Criteria.AddCondition("fwp_date", ConditionOperator.Equal, today);

                // Add TeamMember, CreatedBy, and ModifiedBy user info
                var teamMemberLink = todayQuery.AddLink("systemuser", "fwp_teammember", "systemuserid", JoinOperator.LeftOuter);
                teamMemberLink.EntityAlias = "teammember";
                teamMemberLink.Columns = new ColumnSet("fullname", "internalemailaddress");

                var createdByLink = todayQuery.AddLink("systemuser", "createdby", "systemuserid", JoinOperator.LeftOuter);
                createdByLink.EntityAlias = "creator";
                createdByLink.Columns = new ColumnSet("fullname");

                var modifiedByLink = todayQuery.AddLink("systemuser", "modifiedby", "systemuserid", JoinOperator.LeftOuter);
                modifiedByLink.EntityAlias = "modifier";
                modifiedByLink.Columns = new ColumnSet("fullname");

                var todayResults = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(todayQuery);
                results.Add($"  Today's entries with complete user analysis: {todayResults.Entities.Count}");

                foreach (var entity in todayResults.Entities.Take(5))
                {
                    var name = entity.GetAttributeValue<string>("fwp_name");
                    var teamMemberId = entity.GetAttributeValue<EntityReference>("fwp_teammember")?.Id;
                    var teamMemberName = entity.GetAttributeValue<AliasedValue>("teammember.fullname")?.Value?.ToString();
                    var createdById = entity.GetAttributeValue<EntityReference>("createdby")?.Id;
                    var creatorName = entity.GetAttributeValue<AliasedValue>("creator.fullname")?.Value?.ToString();
                    var modifiedById = entity.GetAttributeValue<EntityReference>("modifiedby")?.Id;
                    var modifierName = entity.GetAttributeValue<AliasedValue>("modifier.fullname")?.Value?.ToString();

                    results.Add($"  Entry: {name ?? "(no name)"}");
                    results.Add($"    TeamMember: {teamMemberName} ({teamMemberId})");
                    results.Add($"    CreatedBy: {creatorName} ({createdById})");
                    results.Add($"    ModifiedBy: {modifierName} ({modifiedById})");

                    // Check for mismatches
                    if (teamMemberId != createdById)
                    {
                        results.Add($"    ⚠️ MISMATCH: TeamMember ≠ CreatedBy (entry assigned to different user!)");
                    }
                    if (teamMemberId == currentUserId)
                    {
                        results.Add($"    ✅ Correctly assigned to current user");
                    }
                    else
                    {
                        results.Add($"    ❌ NOT assigned to current user");
                    }
                }

                // Step 5: UI Collection State
                results.Add("");
                results.Add("STEP 5 - UI Collection State");
                results.Add($"  TimeEntries.Count: {TimeEntries.Count}");
                results.Add($"  Should show: {dateFilteredEntries.Count} entries");
                results.Add($"  Actually showing: {TimeEntries.Count} entries");

                // Step 6: Critical Analysis and Recommendations
                results.Add("");
                results.Add("CRITICAL ANALYSIS & RECOMMENDATIONS:");

                // Analyze the root cause
                if (ServiceLocator.CurrentUserId != _currentUser?.Id && ServiceLocator.CurrentUserId != Guid.Empty)
                {
                    results.Add("  🔴 ROOT CAUSE IDENTIFIED: ServiceLocator user mismatch!");
                    results.Add("     Fix: Sync ServiceLocator.CurrentUserId with _currentUser.Id on login");
                    results.Add("     Quick workaround: Always use _currentUser.Id when creating entries");
                }

                if (dateFilteredEntries.Count != TimeEntries.Count)
                {
                    results.Add($"  ❌ UI MISMATCH DETECTED!");

                    if (dateFilteredEntries.Count > 0 && TimeEntries.Count == 0)
                    {
                        results.Add("  DIAGNOSIS: LoadTimeEntriesAsync() is not updating UI collection properly");

                        // Test LoadTimeEntriesAsync directly
                        results.Add("");
                        results.Add("TESTING LoadTimeEntriesAsync() directly...");
                        var beforeCount = TimeEntries.Count;
                        await LoadTimeEntriesAsync();
                        var afterCount = TimeEntries.Count;

                        results.Add($"  Before: {beforeCount}, After: {afterCount}");
                        if (afterCount == dateFilteredEntries.Count)
                        {
                            results.Add("  ✅ LoadTimeEntriesAsync() works when called directly!");
                            results.Add("  Issue: LoadTimeEntriesAsync() not being called at the right times");
                        }
                        else
                        {
                            results.Add("  ❌ LoadTimeEntriesAsync() still broken - issue is inside that method");
                        }
                    }
                }
                else
                {
                    results.Add($"  ✅ NO MISMATCH - Everything working correctly");
                }

                // Show results
                var resultText = string.Join("\n", results);

                foreach (var result in results)
                {
                    System.Diagnostics.Debug.WriteLine(result);
                }

                var window = new Window
                {
                    Title = "Complete Time Entry Pipeline Analysis",
                    Width = 1000,
                    Height = 800,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = resultText,
                            Padding = new Thickness(10),
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize = 10
                        }
                    }
                };
                window.ShowDialog();

                UpdateStatus("Complete pipeline analysis completed");

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Pipeline tracking error: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async void CheckSpecificEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Analyzing TIM-16249 entry...");

                var results = new List<string>();
                results.Add("=== SPECIFIC ENTRY ANALYSIS: TIM-16249 ===");
                results.Add("");

                // Current user context
                var currentUserId = ServiceLocator.CurrentUserId != Guid.Empty
                    ? ServiceLocator.CurrentUserId
                    : _currentUser?.Id ?? Guid.Empty;

                results.Add("CURRENT USER CONTEXT:");
                results.Add($"  Current User ID: {currentUserId}");
                results.Add($"  Current User Name: {_currentUser?.FullName ?? ServiceLocator.CurrentUserName}");
                results.Add("");

                // Step 1: Find the TIM-16249 entry in Dataverse
                results.Add("STEP 1 - FIND TIM-16249 IN DATAVERSE:");

                var query = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet(true), // Get ALL columns
                    Criteria = new FilterExpression(LogicalOperator.And)
                };
                query.Criteria.AddCondition("fwp_name", ConditionOperator.Equal, "TIM-16249");

                // Add links to get user details
                var teamMemberLink = query.AddLink("systemuser", "fwp_teammember", "systemuserid", JoinOperator.LeftOuter);
                teamMemberLink.EntityAlias = "teammember";
                teamMemberLink.Columns = new ColumnSet("fullname", "systemuserid", "internalemailaddress");

                var createdByLink = query.AddLink("systemuser", "createdby", "systemuserid", JoinOperator.LeftOuter);
                createdByLink.EntityAlias = "creator";
                createdByLink.Columns = new ColumnSet("fullname", "systemuserid");

                var modifiedByLink = query.AddLink("systemuser", "modifiedby", "systemuserid", JoinOperator.LeftOuter);
                modifiedByLink.EntityAlias = "modifier";
                modifiedByLink.Columns = new ColumnSet("fullname", "systemuserid");

                var searchResults = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(query);

                if (searchResults.Entities.Count == 0)
                {
                    results.Add("  ❌ TIM-16249 NOT FOUND in Dataverse!");
                }
                else
                {
                    results.Add($"  ✅ Found {searchResults.Entities.Count} entry/entries with name 'TIM-16249'");
                    results.Add("");

                    foreach (var entity in searchResults.Entities)
                    {
                        var entryId = entity.Id;

                        // Safe field extraction with null handling
                        var date = entity.Contains("fwp_date") ? entity.GetAttributeValue<DateTime>("fwp_date") : DateTime.MinValue;

                        // Handle decimal fields that might be null or different types
                        decimal decimalhours = 0;
                        if (entity.Contains("fwp_decimalhours") && entity["fwp_decimalhours"] != null)
                        {
                            var decimalValue = entity["fwp_decimalhours"];
                            if (decimalValue is decimal)
                                decimalhours = (decimal)decimalValue;
                            else if (decimalValue is double)
                                decimalhours = Convert.ToDecimal(decimalValue);
                            else if (decimalValue is int)
                                decimalhours = Convert.ToDecimal(decimalValue);
                        }

                        // Handle integer fields
                        int minutes = 0;
                        if (entity.Contains("fwp_minutes") && entity["fwp_minutes"] != null)
                        {
                            var minuteValue = entity["fwp_minutes"];
                            if (minuteValue is int)
                                minutes = (int)minuteValue;
                            else
                                minutes = Convert.ToInt32(minuteValue);
                        }

                        // Handle hours field (might be decimal or int)
                        decimal hours = 0;
                        if (entity.Contains("fwp_hours") && entity["fwp_hours"] != null)
                        {
                            var hourValue = entity["fwp_hours"];
                            if (hourValue is decimal)
                                hours = (decimal)hourValue;
                            else if (hourValue is double)
                                hours = Convert.ToDecimal(hourValue);
                            else if (hourValue is int)
                                hours = Convert.ToDecimal(hourValue);
                        }

                        // Handle durationhours field
                        int durationHours = 0;
                        if (entity.Contains("fwp_durationhours") && entity["fwp_durationhours"] != null)
                        {
                            var durationValue = entity["fwp_durationhours"];
                            if (durationValue is int)
                                durationHours = (int)durationValue;
                            else
                                durationHours = Convert.ToInt32(durationValue);
                        }

                        var notes = entity.Contains("fwp_notes") ? entity.GetAttributeValue<string>("fwp_notes") : "";
                        var createdOn = entity.Contains("createdon") ? entity.GetAttributeValue<DateTime>("createdon") : DateTime.MinValue;
                        var modifiedOn = entity.Contains("modifiedon") ? entity.GetAttributeValue<DateTime>("modifiedon") : DateTime.MinValue;

                        // Get user references
                        var teamMemberRef = entity.GetAttributeValue<EntityReference>("fwp_teammember");
                        var teamMemberId = teamMemberRef?.Id ?? Guid.Empty;
                        var teamMemberName = entity.GetAttributeValue<AliasedValue>("teammember.fullname")?.Value?.ToString() ?? "Unknown";
                        var teamMemberEmail = entity.GetAttributeValue<AliasedValue>("teammember.internalemailaddress")?.Value?.ToString() ?? "Unknown";

                        var createdByRef = entity.GetAttributeValue<EntityReference>("createdby");
                        var createdById = createdByRef?.Id ?? Guid.Empty;
                        var creatorName = entity.GetAttributeValue<AliasedValue>("creator.fullname")?.Value?.ToString() ?? "Unknown";

                        var modifiedByRef = entity.GetAttributeValue<EntityReference>("modifiedby");
                        var modifiedById = modifiedByRef?.Id ?? Guid.Empty;
                        var modifierName = entity.GetAttributeValue<AliasedValue>("modifier.fullname")?.Value?.ToString() ?? "Unknown";

                        results.Add($"ENTRY DETAILS (ID: {entryId}):");
                        results.Add($"  Date: {date:yyyy-MM-dd}");
                        results.Add($"  Created: {createdOn:yyyy-MM-dd HH:mm:ss}");
                        results.Add($"  Modified: {modifiedOn:yyyy-MM-dd HH:mm:ss}");
                        results.Add($"  Notes/Comments: {notes}");
                        results.Add("");

                        results.Add("TIME VALUES:");
                        results.Add($"  fwp_decimalhours: {decimalhours}");
                        results.Add($"  fwp_minutes: {minutes}");
                        results.Add($"  fwp_hours: {hours}");
                        results.Add($"  fwp_durationhours: {durationHours}");

                        // Debug raw values
                        results.Add("");
                        results.Add("RAW FIELD VALUES (for debugging):");
                        if (entity.Contains("fwp_decimalhours"))
                            results.Add($"  fwp_decimalhours type: {entity["fwp_decimalhours"]?.GetType()?.Name ?? "null"}");
                        if (entity.Contains("fwp_minutes"))
                            results.Add($"  fwp_minutes type: {entity["fwp_minutes"]?.GetType()?.Name ?? "null"}");
                        if (entity.Contains("fwp_hours"))
                            results.Add($"  fwp_hours type: {entity["fwp_hours"]?.GetType()?.Name ?? "null"}");
                        if (entity.Contains("fwp_durationhours"))
                            results.Add($"  fwp_durationhours type: {entity["fwp_durationhours"]?.GetType()?.Name ?? "null"}");

                        results.Add("");
                        results.Add("USER ASSIGNMENTS:");
                        results.Add($"  TeamMember: {teamMemberName} ({teamMemberId})");
                        results.Add($"  TeamMember Email: {teamMemberEmail}");
                        results.Add($"  CreatedBy: {creatorName} ({createdById})");
                        results.Add($"  ModifiedBy: {modifierName} ({modifiedById})");
                        results.Add("");

                        results.Add("CRITICAL CHECKS:");

                        // Check 1: Does TeamMemberId match current user?
                        var matchesCurrentUser = teamMemberId == currentUserId;
                        if (matchesCurrentUser)
                        {
                            results.Add($"  ✅ TeamMemberId MATCHES current user");
                        }
                        else
                        {
                            results.Add($"  ❌ TeamMemberId DOES NOT match current user!");
                            results.Add($"     Entry TeamMemberId: {teamMemberId}");
                            results.Add($"     Current User ID:    {currentUserId}");
                        }

                        // Check 2: Does CreatedBy match TeamMember?
                        if (teamMemberId == createdById)
                        {
                            results.Add($"  ✅ TeamMember matches CreatedBy (normal)");
                        }
                        else
                        {
                            results.Add($"  ⚠️ TeamMember ≠ CreatedBy (entry reassigned!)");
                            results.Add($"     This suggests a plugin/workflow changed the assignment");
                        }

                        // Check 3: Check if this shows in GetTimeEntries
                        results.Add("");
                        results.Add("CHECKING GetTimeEntries():");
                        var allEntries = _timeEntryService.GetTimeEntries();

                        var foundInGetTimeEntries = allEntries.Any(entry =>
                            entry.Id == entryId ||
                            (entry.Date.Date == date.Date && entry.Comments == notes));

                        if (foundInGetTimeEntries)
                        {
                            results.Add($"  ✅ Entry IS returned by GetTimeEntries()");

                            // Check if it passes user filter
                            var userFilteredEntry = allEntries.FirstOrDefault(entry =>
                                (entry.Id == entryId || (entry.Date.Date == date.Date && entry.Comments == notes)) &&
                                entry.TeamMemberId == currentUserId);

                            if (userFilteredEntry != null)
                            {
                                results.Add($"  ✅ Entry PASSES user filter");
                            }
                            else
                            {
                                results.Add($"  ❌ Entry FAILS user filter");

                                // Find what TeamMemberId it has in the returned data
                                var actualEntry = allEntries.FirstOrDefault(entry =>
                                    entry.Id == entryId || (entry.Date.Date == date.Date && entry.Comments == notes));
                                if (actualEntry != null)
                                {
                                    results.Add($"     In-memory TeamMemberId: {actualEntry.TeamMemberId}");
                                    results.Add($"     Current User ID:        {currentUserId}");
                                }
                            }
                        }
                        else
                        {
                            results.Add($"  ❌ Entry NOT found in GetTimeEntries() results!");
                        }

                        results.Add("");
                        results.Add("DIAGNOSIS:");
                        if (!matchesCurrentUser)
                        {
                            results.Add($"  The entry TIM-16249 is assigned to {teamMemberName} ({teamMemberId})");
                            results.Add($"  But you're logged in as {_currentUser?.FullName} ({currentUserId})");
                            results.Add($"  This is why the entry doesn't show in your view!");
                            results.Add("");
                            results.Add("  POSSIBLE CAUSES:");
                            results.Add("  1. Entry was created under wrong user context");
                            results.Add("  2. Plugin/workflow reassigned the entry after creation");
                            results.Add("  3. Manual reassignment in Dataverse");
                            results.Add("  4. Default user assignment rule in Dataverse");
                        }
                        else
                        {
                            results.Add("  Entry should be visible - check date filters and UI refresh");
                        }
                    }
                }

                // Show results
                var resultText = string.Join("\n", results);

                var window = new Window
                {
                    Title = "TIM-16249 Entry Analysis",
                    Width = 900,
                    Height = 700,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = resultText,
                            Padding = new Thickness(10),
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize = 11
                        }
                    }
                };
                window.ShowDialog();

                UpdateStatus("Entry analysis completed");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error analyzing entry: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DebugGetTimeEntries_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Debugging GetTimeEntries method...");

                var results = new List<string>();
                results.Add("=== DEBUGGING GetTimeEntries() METHOD ===");
                results.Add("");

                var targetEntryId = "c04fff07-ba9a-f011-b41b-7c1e522f5776"; // TIM-16249 ID

                // Test 1: Simple query without any links
                results.Add("TEST 1 - SIMPLE QUERY (No Links):");
                var simpleQuery = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet("fwp_name", "fwp_date", "fwp_teammember", "fwp_decimalhours", "fwp_minutes", "fwp_notes"),
                    Criteria = new FilterExpression()
                };
                simpleQuery.Criteria.AddCondition("fwp_date", ConditionOperator.Equal, DateTime.Today);

                var simpleResults = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(simpleQuery);
                results.Add($"  Simple query returned: {simpleResults.Entities.Count} entries");

                // Fixed: Changed 'e' to 'entity'
                var foundInSimple = simpleResults.Entities.Any(entity => entity.Id.ToString().Equals(targetEntryId, StringComparison.OrdinalIgnoreCase));
                results.Add($"  TIM-16249 found in simple query: {(foundInSimple ? "✅ YES" : "❌ NO")}");
                results.Add("");

                // Test 2: Query with project links (like GetTimeEntries does)
                results.Add("TEST 2 - WITH PROJECT LINKS:");
                var projectQuery = new QueryExpression("fwp_timeentry")
                {
                    ColumnSet = new ColumnSet("fwp_date", "fwp_decimalhours", "fwp_minutes",
                                            "fwp_notes", "fwp_category", "fwp_classification",
                                            "fwp_project", "fwp_quote", "fwp_teammember")
                };
                projectQuery.Criteria.AddCondition("fwp_date", ConditionOperator.Equal, DateTime.Today);

                // Add the same links as GetTimeEntries
                var projectLink = new LinkEntity
                {
                    LinkFromEntityName = "fwp_timeentry",
                    LinkFromAttributeName = "fwp_project",
                    LinkToEntityName = "msdyn_project",
                    LinkToAttributeName = "msdyn_projectid",
                    Columns = new ColumnSet("msdyn_subject", "isc_projectnumbernew", "msdyn_customer"),
                    EntityAlias = "project",
                    JoinOperator = JoinOperator.LeftOuter
                };

                var quoteLink = new LinkEntity
                {
                    LinkFromEntityName = "fwp_timeentry",
                    LinkFromAttributeName = "fwp_quote",
                    LinkToEntityName = "quote",
                    LinkToAttributeName = "quoteid",
                    Columns = new ColumnSet("quotenumber", "name", "customerid"),
                    EntityAlias = "quote",
                    JoinOperator = JoinOperator.LeftOuter
                };

                projectQuery.LinkEntities.Add(projectLink);
                projectQuery.LinkEntities.Add(quoteLink);

                var projectResults = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(projectQuery);
                results.Add($"  Query with links returned: {projectResults.Entities.Count} entries");

                // Fixed: Changed 'e' to 'entity'
                var foundWithLinks = projectResults.Entities.Any(entity => entity.Id.ToString().Equals(targetEntryId, StringComparison.OrdinalIgnoreCase));
                results.Add($"  TIM-16249 found with links: {(foundWithLinks ? "✅ YES" : "❌ NO")}");

                if (!foundWithLinks && foundInSimple)
                {
                    results.Add("  ❌ PROBLEM: Entry exists in simple query but disappears with links!");
                    results.Add("     This suggests an issue with the LinkEntity joins");
                }
                results.Add("");

                // Test 3: Check TimeEntry.FromEntity conversion
                results.Add("TEST 3 - FROMENTY CONVERSION TEST:");
                if (foundWithLinks)
                {
                    // Fixed: Changed 'e' to 'entity'
                    var targetEntity = projectResults.Entities.First(entity => entity.Id.ToString().Equals(targetEntryId, StringComparison.OrdinalIgnoreCase));

                    try
                    {
                        var timeEntry = TimeEntry.FromEntity(targetEntity);
                        if (timeEntry != null)
                        {
                            results.Add("  ✅ FromEntity conversion successful");
                            results.Add($"     ID: {timeEntry.Id}");
                            results.Add($"     Date: {timeEntry.Date:yyyy-MM-dd}");
                            results.Add($"     TeamMemberId: {timeEntry.TeamMemberId}");
                            results.Add($"     Comments: {timeEntry.Comments}");
                        }
                        else
                        {
                            results.Add("  ❌ FromEntity returned null!");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add($"  ❌ FromEntity conversion failed: {ex.Message}");
                    }
                }
                results.Add("");

                // Test 4: Call actual GetTimeEntries and see what happens
                results.Add("TEST 4 - ACTUAL GetTimeEntries() CALL:");
                var allEntries = _timeEntryService.GetTimeEntries();
                results.Add($"  GetTimeEntries() returned: {allEntries.Count} total entries");

                // Fixed: Changed 'e' to 'entry'
                var foundInActual = allEntries.Any(entry => entry.Id.ToString().Equals(targetEntryId, StringComparison.OrdinalIgnoreCase));
                results.Add($"  TIM-16249 found: {(foundInActual ? "✅ YES" : "❌ NO")}");

                if (foundInActual)
                {
                    // Fixed: Changed 'e' to 'entry'
                    var actualEntry = allEntries.First(entry => entry.Id.ToString().Equals(targetEntryId, StringComparison.OrdinalIgnoreCase));
                    results.Add($"     Found entry - TeamMemberId: {actualEntry.TeamMemberId}");
                    results.Add($"     Comments: {actualEntry.Comments}");
                }
                results.Add("");

                // Test 5: Check today's entries specifically
                results.Add("TEST 5 - TODAY'S ENTRIES ANALYSIS:");
                // Fixed: Changed 'e' to 'entry'
                var todayEntries = allEntries.Where(entry => entry.Date.Date == DateTime.Today).ToList();
                results.Add($"  Today's entries from GetTimeEntries(): {todayEntries.Count}");

                foreach (var entry in todayEntries.Take(5))
                {
                    results.Add($"    • {entry.Comments} - {entry.TeamMemberId} - {entry.TotalHours}h");
                }

                results.Add("");
                results.Add("DIAGNOSIS:");
                if (foundInSimple && !foundWithLinks)
                {
                    results.Add("  🔴 LINK ENTITY PROBLEM: Complex joins are excluding the entry");
                }
                else if (foundWithLinks && !foundInActual)
                {
                    results.Add("  🔴 CONVERSION PROBLEM: FromEntity is failing or returning null");
                }
                else if (!foundInSimple)
                {
                    results.Add("  🔴 DATA PROBLEM: Entry might not exist or have wrong date");
                }
                else
                {
                    results.Add("  🟡 UNCLEAR: Need to check other filtering logic");
                }

                // Show results
                var resultText = string.Join("\n", results);

                var window = new Window
                {
                    Title = "GetTimeEntries Debug Analysis",
                    Width = 900,
                    Height = 700,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = resultText,
                            Padding = new Thickness(10),
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize = 11
                        }
                    }
                };
                window.ShowDialog();

                UpdateStatus("GetTimeEntries debug completed");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Debug error: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async void TestMultipleQuotes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Testing multiple quotes...");

                var results = new List<string>();
                results.Add("=== TESTING MULTIPLE QUOTES ===");

                // List of quotes to test
                var quoteNumbers = new[] { "Q26586", "Q23480", "Q23369", "Q26924", "Q25482" };

                // Test 1: Raw query - get ALL quotes at once
                var rawQuery = new QueryExpression("quote")
                {
                    ColumnSet = new ColumnSet("quotenumber", "name", "statecode", "statuscode", "isc_projectnumbervisible"),
                    Criteria = new FilterExpression(LogicalOperator.Or)
                };

                // Add conditions for all quote numbers
                foreach (var quoteNum in quoteNumbers)
                {
                    rawQuery.Criteria.AddCondition("quotenumber", ConditionOperator.Equal, quoteNum);
                }

                var rawResult = ServiceLocator.DataverseConnector._orgService.RetrieveMultiple(rawQuery);
                results.Add($"Raw query found: {rawResult.Entities.Count} out of {quoteNumbers.Length} quotes");
                results.Add("");

                // Process each found quote
                var foundQuotes = new Dictionary<string, Entity>();
                foreach (var entity in rawResult.Entities)
                {
                    var quoteNumber = entity.GetAttributeValue<string>("quotenumber");
                    foundQuotes[quoteNumber] = entity;
                }

                // Analyze each quote
                foreach (var quoteNumber in quoteNumbers)
                {
                    results.Add($"--- {quoteNumber} ---");

                    if (foundQuotes.ContainsKey(quoteNumber))
                    {
                        var entity = foundQuotes[quoteNumber];
                        var name = entity.GetAttributeValue<string>("name");
                        var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value;
                        var statusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value;
                        var projectNum = entity.GetAttributeValue<string>("isc_projectnumbervisible");

                        // Map codes to human readable
                        var stateText = stateCode == 0 ? "Active" : stateCode == 1 ? "Inactive" : $"Unknown({stateCode})";
                        var statusText = GetStatusText(statusCode);

                        results.Add($"  Found: YES");
                        results.Add($"  Name: {name}");
                        results.Add($"  StateCode: {stateCode} ({stateText})");
                        results.Add($"  StatusCode: {statusCode} ({statusText})");
                        results.Add($"  ProjectNumber: '{projectNum}'");

                        // Test Quote.FromEntity conversion
                        var quote = Quote.FromEntity(entity);
                        results.Add($"  Quote.IsActive: {quote?.IsActive}");

                        // Check why it would be filtered out
                        var reasons = new List<string>();
                        if (statusCode == 0) reasons.Add("Draft");
                        if (statusCode == 2) reasons.Add("Won");
                        if (statusCode == 3) reasons.Add("Lost/Closed");
                        if (statusCode == 4) reasons.Add("Canceled");
                        if (!string.IsNullOrWhiteSpace(projectNum)) reasons.Add($"Assigned to project: {projectNum}");

                        if (reasons.Count > 0)
                        {
                            results.Add($"  ❌ Filtered out because: {string.Join(", ", reasons)}");
                        }
                        else
                        {
                            results.Add($"  ✅ Should be visible in app");
                        }
                    }
                    else
                    {
                        results.Add($"  Found: NO - Quote does not exist in Dataverse");
                    }

                    results.Add("");
                }

                // Test against GetQuotes() method
                results.Add("--- APP METHODS TEST ---");
                var allQuotes = ServiceLocator.QuoteService.GetQuotes();
                var searchResults23480 = ServiceLocator.QuoteService.SearchQuotes("23480");

                results.Add($"GetQuotes() returned: {allQuotes.Count} total quotes");
                results.Add($"SearchQuotes('23480') returned: {searchResults23480.Count} quotes");
                results.Add("");

                foreach (var quoteNumber in quoteNumbers)
                {
                    var foundInGetQuotes = allQuotes.Any(q => q.QuoteNumber == quoteNumber);
                    var searchNum = quoteNumber.Replace("Q", "");
                    var searchResults = ServiceLocator.QuoteService.SearchQuotes(searchNum);
                    var foundInSearch = searchResults.Any(q => q.QuoteNumber == quoteNumber);

                    results.Add($"{quoteNumber}: GetQuotes={foundInGetQuotes}, Search={foundInSearch}");
                }

                // Show some quotes that ARE being returned for comparison
                results.Add("");
                results.Add("Sample of quotes that ARE visible:");
                foreach (var q in allQuotes.Take(5))
                {
                    results.Add($"  {q.QuoteNumber} (StatusCode: {q.StatusCode})");
                }

                // Show results
                var resultText = string.Join("\n", results);

                foreach (var result in results)
                {
                    System.Diagnostics.Debug.WriteLine(result);
                }

                MessageBox.Show(resultText, "Multiple Quote Diagnostic",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                UpdateStatus("Multiple quote test completed");

            }
            catch (Exception ex)
            {
                var error = $"Error: {ex.Message}\n\nStack: {ex.StackTrace}";
                System.Diagnostics.Debug.WriteLine(error);
                MessageBox.Show(error, "Diagnostic Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Helper method to convert status codes to readable text
        // Helper method to convert status codes to readable text (C# 7.3 compatible)
        private string GetStatusText(int? statusCode)
        {
            switch (statusCode)
            {
                case 0:
                    return "Draft";
                case 1:
                    return "Active";
                case 2:
                    return "Won";
                case 3:
                    return "Lost";
                case 4:
                    return "Canceled";
                case 5:
                    return "Revised";
                default:
                    return $"Unknown({statusCode})";
            }
        }

        private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Cancel any existing timer
            _searchTimer?.Dispose();

            // Start a new timer that will trigger the search after a delay
            _searchTimer = new System.Threading.Timer(async _ =>
            {
                await Dispatcher.BeginInvoke(new Action(async () => await FilterProjects()));
            }, null, SearchDelayMs, Timeout.Infinite);
        }

        private void ProjectSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ProjectSearchBox.Text == "Search by name, number or client...")
            {
                ProjectSearchBox.Text = "";
            }
            // Show recent searches when the box is empty
            if (string.IsNullOrWhiteSpace(ProjectSearchBox.Text) && _searchStateManager != null)
            {
                RefreshRecentProjectSearchesUI();
                RecentProjectSearchesSection.Visibility =
                    _searchStateManager.RecentProjectSearches.Any() ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async Task FilterProjects()
        {
            var searchText = ProjectSearchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search by name, number or client...")
            {
                // If there is already an active selection, do NOT replace the ItemsSource.
                // Replacing it causes WPF to re-select the item by SelectedValuePath and call
                // ScrollIntoView, which makes the list jump to that item's position in the full
                // list – the reported "skipping down" behaviour.
                if (ProjectsList.SelectedItem != null) return;

                // Show more projects when no search term (increased from 50 to 200)
                Dispatcher.Invoke(() =>
                {
                    ProjectsList.ItemsSource = Projects.Take(200).ToList(); // Increased limit
                    UpdateStatus("Ready");
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    RecentProjectSearchesSection.Visibility = Visibility.Collapsed;
                    UpdateStatus("Searching projects...");
                });

                try
                {
                    // Use server-side search with the enhanced fuzzy logic
                    var searchResults = await Task.Run(() => _projectService.SearchProjects(searchText));

                    // Update the UI on the main thread
                    Dispatcher.Invoke(() =>
                    {
                        ProjectsList.ItemsSource = searchResults.Take(100).ToList(); // Show more search results
                        UpdateStatus($"Found {searchResults.Count} projects");
                        // Save this as a recent search
                        _searchStateManager?.AddRecentProjectSearch(searchText);
                        RefreshRecentProjectSearchesUI();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus($"Search error: {ex.Message}");

                        // Fallback to cached results if search fails
                        var fallbackResults = Projects.Where(p =>
                            (p.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (p.Number?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (p.Client?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        ).ToList();

                        ProjectsList.ItemsSource = fallbackResults;
                    });
                }
            }
        }

        private void ClearTimeEntryForm()
        {
            // Use the last selected date instead of always defaulting to today
            TimeEntryDatePicker.SelectedDate = _lastSelectedTimeEntryDate;
            HoursTextBox.Clear();
            MinutesTextBox.Clear();
            CommentsTextBox.Clear();
            ProjectsList.SelectedItem = null;
            ProjectSearchBox.Text = "Search by name, number or client...";

            // NEW: Reset category to Chargeable
            ChargeableRadioButton.IsChecked = true;

            SelectedProjectClientLabel.Text = "No project selected";
            SelectedProjectClientLabel.FontStyle = FontStyles.Italic;
            SelectedProjectClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            SelectedProjectDisciplineLabel.Visibility = Visibility.Collapsed;
        }

        private async void RefreshEntriesButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadTimeEntriesAsync();

            // Update progress after entries are loaded
            Dispatcher.Invoke(() =>
            {
                UpdateDailyProgress();
            });
        }

        private void ShowDetailedProgressForDate(DateTime date)
        {
            if (_currentUser == null) return;

            var expectedHours = _currentUserConfig?.GetExpectedHoursForDay(date.DayOfWeek) ?? 8.0m;
            var actualHours = TimeEntries
                .Where(te => te.Date.Date == date.Date && te.TeamMemberId == _currentUser.Id)
                .Sum(te => te.TotalHours);

            var entries = TimeEntries
                .Where(te => te.Date.Date == date.Date && te.TeamMemberId == _currentUser.Id)
                .OrderBy(te => te.CreatedDate)
                .ToList();

            string details = $"📊 Progress Details for {date:dddd, dd MMMM yyyy}\n\n";
            details += $"Expected: {FormatHoursMinutes(expectedHours)}\n";
            details += $"Actual: {FormatHoursMinutes(actualHours)}\n";
            details += $"Progress: {(expectedHours > 0 ? (actualHours / expectedHours * 100) : 0):F1}%\n";
            details += $"Remaining: {FormatHoursMinutes(Math.Max(0, expectedHours - actualHours))}\n\n";

            if (entries.Any())
            {
                details += $"Time Entries ({entries.Count}):\n";
                foreach (var entry in entries)
                {
                    string entryType = entry.Classification == TimeEntryClassification.Project ? "📋" : "💼";
                    string entryName = entry.Classification == TimeEntryClassification.Project ? entry.ProjectName : entry.QuoteName;
                    details += $"{entryType} {entryName}: {entry.TotalTime}\n";
                }
            }
            else
            {
                details += "No time entries for this date.";
            }

            MessageBox.Show(details,           
                "Daily Progress Details", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Silently expands the From/To date pickers to include <paramref name="date"/> if it
        /// falls outside the current range. The DateRange_Changed reload is suppressed so the
        /// caller's own LoadTimeEntriesAsync() is the single reload that occurs.
        /// </summary>
        private void ExpandDateRangeToInclude(DateTime date)
        {
            var target = date.Date;
            bool adjusted = false;
            _suppressDateRangeChange = true;
            try
            {
                if (!FromDatePicker.SelectedDate.HasValue || target < FromDatePicker.SelectedDate.Value.Date)
                {
                    FromDatePicker.SelectedDate = target;
                    adjusted = true;
                }

                if (!ToDatePicker.SelectedDate.HasValue || target > ToDatePicker.SelectedDate.Value.Date)
                {
                    ToDatePicker.SelectedDate = target;
                    adjusted = true;
                }
            }
            finally
            {
                _suppressDateRangeChange = false;
            }

            if (adjusted)
                DateRangeAdjustedLabel.Visibility = System.Windows.Visibility.Visible;
        }

        private async void DateRange_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Suppress reload while ExpandDateRangeToInclude is silently adjusting pickers
            if (_suppressDateRangeChange) return;

            // User manually changed the range — dismiss the auto-adjusted notice
            DateRangeAdjustedLabel.Visibility = System.Windows.Visibility.Collapsed;

            // Only refresh if both dates are set
            if (FromDatePicker.SelectedDate.HasValue && ToDatePicker.SelectedDate.HasValue)
            {
                await LoadTimeEntriesAsync();

                // NEW: Update progress after entries are loaded
                Dispatcher.Invoke(() =>
                {
                    UpdateDailyProgress();
                });
            }
        }
        #endregion

        #region Disbursement Events
        private async void AddDisbursementButton_Click(object sender, RoutedEventArgs e)
        {
            // Prevent any other method from modifying the Disbursements collection
            if (_isAddingDisbursement)
                return;

            try
            {
                _isAddingDisbursement = true;

                // Validate form inputs
                if (!ValidateDisbursementForm())
                    return;

                var selectedDate = DisbursementDatePicker.SelectedDate ?? DateTime.Today;

                // ✅ DEBUG: Log date handling for troubleshooting
                System.Diagnostics.Debug.WriteLine($"=== DISBURSEMENT DATE DEBUG ===");
                System.Diagnostics.Debug.WriteLine($"DatePicker.SelectedDate: {DisbursementDatePicker.SelectedDate}");
                System.Diagnostics.Debug.WriteLine($"Selected Date: {selectedDate}");
                System.Diagnostics.Debug.WriteLine($"Selected Date Kind: {selectedDate.Kind}");
                System.Diagnostics.Debug.WriteLine($"DateTime.Today: {DateTime.Today}");
                System.Diagnostics.Debug.WriteLine($"DateTime.Now: {DateTime.Now}");
                System.Diagnostics.Debug.WriteLine($"DateTime.UtcNow: {DateTime.UtcNow}");

                // Create disbursement from form fields
                var disbursement = new Disbursement
                {
                    // ✅ FIXED: Ensure we store the date component only
                    Date = selectedDate.Date,
                    Description = DisbursementDescriptionTextBox.Text.Trim(),
                    BillableToClient = true,
                    TeamMemberId = _currentUser?.Id.GetHashCode() ?? 0,
                    TeamMemberGuid = _currentUser?.Id ?? Guid.Empty,
                    TeamMemberName = _currentUser?.FullName ?? "Unknown User"
                };

                // Set project or quote based on toggle selection
                if (DisbursementProjectToggle.IsChecked == true)
                {
                    var selectedProject = DisbursementProjectsList.SelectedItem as Project;
                    if (selectedProject != null)
                    {
                        disbursement.ProjectId = selectedProject.Id.GetHashCode(); // Convert Guid to int
                        disbursement.ProjectGuid = selectedProject.Id; // Use Id property directly
                        disbursement.ProjectName = selectedProject.Name;
                        disbursement.ProjectNumber = selectedProject.Number; // Use Number property
                        disbursement.Classification = DisbursementClassification.Project;
                    }
                }
                else if (DisbursementQuoteToggle.IsChecked == true)
                {
                    var selectedQuote = DisbursementQuotesList.SelectedItem as Quote;
                    if (selectedQuote != null)
                    {
                        disbursement.QuoteId = selectedQuote.Id;
                        disbursement.QuoteName = selectedQuote.Name;
                        disbursement.QuoteNumber = selectedQuote.QuoteNumber;
                        disbursement.ClientName = selectedQuote.Client; // Use Client property
                        disbursement.Classification = DisbursementClassification.Quote;
                    }
                }

                // Set disbursement type and handle unit-based vs amount-based
                var selectedType = DisbursementTypeComboBox.SelectedItem as DisbursementType;
                if (selectedType != null)
                {
                    disbursement.DisbursementTypeId = selectedType.Id;
                    disbursement.DisbursementTypeGuid = selectedType.IdGuid;
                    disbursement.DisbursementTypeName = selectedType.Name;
                    disbursement.UnitCharge = selectedType.UnitCharge;

                    if (selectedType.IsUnitBased)
                    {
                        // Unit-based disbursement
                        if (decimal.TryParse(DisbursementUnitsTextBox.Text, out decimal units))
                        {
                            disbursement.Units = units;
                            disbursement.Amount = units * selectedType.UnitCharge;
                        }
                    }
                    else
                    {
                        // Amount-based disbursement
                        if (decimal.TryParse(DisbursementAmountTextBox.Text, out decimal amount))
                        {
                            disbursement.Amount = amount;
                            disbursement.Units = 0;
                        }
                    }
                }

                // Save to Dataverse
                UpdateStatus("Adding disbursement...");

                Guid newDisbursementId;
                if (disbursement.Classification == DisbursementClassification.Quote)
                {
                    newDisbursementId = await _disbursementService.AddDisbursementAsyncEnhanced(disbursement);
                }
                else
                {
                    newDisbursementId = await _disbursementService.AddDisbursementAsync(disbursement);
                }

                if (newDisbursementId != Guid.Empty)
                {
                    // Update the disbursement with the new ID
                    disbursement.IdGuid = newDisbursementId;
                    disbursement.Id = newDisbursementId.GetHashCode();

                    // Add to UI collection
                    Dispatcher.Invoke(() =>
                    {
                        Disbursements.Insert(0, disbursement); // Add at top
                        UpdateDisbursementsSummary();
                    });

                    // Clear the form for next entry
                    ClearDisbursementFormSafe();

                    UpdateStatus($"Disbursement added successfully - {disbursement.Description}");
                }
                else
                {
                    MessageBox.Show("Failed to add disbursement. Please try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error adding disbursement: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"AddDisbursement error: {ex}");
                MessageBox.Show($"Error adding disbursement: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isAddingDisbursement = false;
                AddDisbursementButton.IsEnabled = true;
            }
        }

        private void ParseProjectNameAndNumber(Disbursement disbursement, string fullProjectName)
        {
            try
            {
                if (fullProjectName.Contains(" - "))
                {
                    var parts = fullProjectName.Split(new[] { " - " }, 2, StringSplitOptions.None);
                    disbursement.ProjectNumber = parts[0].Trim();
                    disbursement.ProjectName = parts[1].Trim();
                }
                else
                {
                    var words = fullProjectName.Split(' ');
                    if (words.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(words[0], @"^\d+$"))
                    {
                        disbursement.ProjectNumber = words[0];
                        disbursement.ProjectName = string.Join(" ", words.Skip(1));
                    }
                    else
                    {
                        disbursement.ProjectNumber = "";
                        disbursement.ProjectName = fullProjectName;
                    }
                }
            }
            catch
            {
                disbursement.ProjectNumber = "";
                disbursement.ProjectName = fullProjectName;
            }
        }


        private void ClearDisbursementFormSafe()
        {
            try
            {
                _isClearingDisbursementForm = true;
                // ✅ FIXED: Always reset date to today and ensure consistency
                DisbursementDatePicker.SelectedDate = DateTime.Today;

                // DEBUG: Log date reset
                System.Diagnostics.Debug.WriteLine($"Form cleared - Date reset to: {DateTime.Today}");

                // Clear text fields
                DisbursementUnitsTextBox.Clear();
                DisbursementAmountTextBox.Clear();
                DisbursementDescriptionTextBox.Clear();

                // Clear disbursement type selection
                DisbursementTypeComboBox.SelectedItem = null;

                // Reset calculated amount
                if (CalculatedAmountLabel != null)
                    CalculatedAmountLabel.Text = "= £0.00";
                if (UnitChargeLabel != null)
                    UnitChargeLabel.Text = "× £0.00";

                // Hide/show appropriate panels
                if (UnitsPanel != null)
                    UnitsPanel.Visibility = Visibility.Collapsed;
                if (UnitsLabel != null)
                    UnitsLabel.Visibility = Visibility.Collapsed;
                if (AmountLabel != null)
                    AmountLabel.Visibility = Visibility.Visible;
                if (DisbursementAmountTextBox != null)
                    DisbursementAmountTextBox.Visibility = Visibility.Visible;

                Task.Delay(100).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isClearingDisbursementForm = false;
                    });
                });

                // DON'T clear project/quote selections - keep them for user convenience
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing disbursement form: {ex.Message}");
                _isClearingDisbursementForm = false;
            }
        }

        private bool ValidateDisbursementForm()
        {
            if (_isClearingDisbursementForm)
            {
                System.Diagnostics.Debug.WriteLine("ValidateDisbursementForm: Skipping validation - form is being cleared");
                return false; // Don't process during clearing
            }
            // Validate date
            if (!DisbursementDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Please select a date.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DisbursementDatePicker.Focus();
                return false;
            }

            // Validate project or quote selection
            if (DisbursementProjectToggle.IsChecked == true)
            {
                if (DisbursementProjectsList.SelectedItem == null)
                {
                    MessageBox.Show("Please select a project.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    DisbursementProjectsList.Focus();
                    return false;
                }
            }
            else if (DisbursementQuoteToggle.IsChecked == true)
            {
                if (DisbursementQuotesList.SelectedItem == null)
                {
                    MessageBox.Show("Please select a quote.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    DisbursementQuotesList.Focus();
                    return false;
                }
            }
            else
            {
                MessageBox.Show("Please select either a project or quote.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Validate disbursement type
            if (DisbursementTypeComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a disbursement type.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DisbursementTypeComboBox.Focus();
                return false;
            }

            var selectedType = DisbursementTypeComboBox.SelectedItem as DisbursementType;

            // Validate amount or units based on disbursement type
            if (selectedType.IsUnitBased)
            {
                if (string.IsNullOrWhiteSpace(DisbursementUnitsTextBox.Text) ||
                    !decimal.TryParse(DisbursementUnitsTextBox.Text, out decimal units) ||
                    units <= 0)
                {
                    MessageBox.Show("Please enter a valid number of units greater than 0.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    DisbursementUnitsTextBox.Focus();
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(DisbursementAmountTextBox.Text) ||
                    !decimal.TryParse(DisbursementAmountTextBox.Text, out decimal amount) ||
                    amount <= 0)
                {
                    MessageBox.Show("Please enter a valid amount greater than 0.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    DisbursementAmountTextBox.Focus();
                    return false;
                }
            }

            // Validate description
            if (string.IsNullOrWhiteSpace(DisbursementDescriptionTextBox.Text))
            {
                MessageBox.Show("Please enter a description.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DisbursementDescriptionTextBox.Focus();
                return false;
            }

            return true;
        }






        private void DisbursementQuoteSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Cancel any existing timer
            _disbursementQuoteSearchTimer?.Dispose();

            // Start a new timer that will trigger the search after a delay
            _disbursementQuoteSearchTimer = new System.Threading.Timer(async _ =>
            {
                await Dispatcher.BeginInvoke(new Action(async () => await FilterDisbursementQuotes()));
            }, null, 300, Timeout.Infinite); // 300ms delay
        }

        private void DisbursementQuoteSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DisbursementQuoteSearchBox.Text == "Search by name, number or client...")
            {
                DisbursementQuoteSearchBox.Text = "";
                DisbursementQuoteSearchBox.Foreground = new SolidColorBrush(Colors.Black);
            }
            if (string.IsNullOrWhiteSpace(DisbursementQuoteSearchBox.Text) && _searchStateManager != null)
            {
                RefreshRecentQuoteSearchesUI();
                DisbRecentQuoteSearchesSection.Visibility =
                    _searchStateManager.RecentQuoteSearches.Any() ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void DisbursementQuoteSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DisbursementQuoteSearchBox.Text))
            {
                DisbursementQuoteSearchBox.Text = "Search by name, number or client...";
                DisbursementQuoteSearchBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }
        }

        private async Task FilterDisbursementQuotes()
        {
            if (DisbursementQuoteSearchBox == null || DisbursementQuotesList == null) return;

            var searchText = DisbursementQuoteSearchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search by name, number or client...")
            {
                DisbursementQuotesList.ItemsSource = _quotes?.Take(50).ToList() ?? new List<Quote>();
            }
            else
            {
                DisbRecentQuoteSearchesSection.Visibility = Visibility.Collapsed;
                try
                {
                    var searchResults = await Task.Run(() => ServiceLocator.QuoteService.SearchQuotes(searchText));
                    DisbursementQuotesList.ItemsSource = searchResults.Take(50).ToList();
                    _searchStateManager?.AddRecentQuoteSearch(searchText);
                    RefreshRecentQuoteSearchesUI();
                }
                catch (Exception)
                {
                    // Fallback to local filtering
                    var filtered = _quotes?.Where(q =>
                        (q.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (q.QuoteNumber?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (q.Client?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    ).Take(50).ToList() ?? new List<Quote>();

                    DisbursementQuotesList.ItemsSource = filtered;
                }
            }
        }




        private void DisbursementProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Cancel any existing timer
            _disbursementProjectSearchTimer?.Dispose();

            // Start a new timer that will trigger the search after a delay
            _disbursementProjectSearchTimer = new System.Threading.Timer(async _ =>
            {
                await Dispatcher.BeginInvoke(new Action(async () => await FilterDisbursementProjects()));
            }, null, 300, Timeout.Infinite); // 300ms delay
        }


        private void DisbursementProjectSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DisbursementProjectSearchBox.Text == "Search by name, number or client...")
            {
                DisbursementProjectSearchBox.Text = "";
                DisbursementProjectSearchBox.Foreground = new SolidColorBrush(Colors.Black);
            }
            if (string.IsNullOrWhiteSpace(DisbursementProjectSearchBox.Text) && _searchStateManager != null)
            {
                RefreshRecentProjectSearchesUI();
                DisbRecentProjectSearchesSection.Visibility =
                    _searchStateManager.RecentProjectSearches.Any() ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void DisbursementProjectSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DisbursementProjectSearchBox.Text))
            {
                DisbursementProjectSearchBox.Text = "Search by name, number or client...";
                DisbursementProjectSearchBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }
        }

        private async Task LoadDisbursementsAsyncEnhanced()
        {
            _disbursementService.TestDisbursementTypeMapping();
            try
            {
                // Null-safe access to selected items
                var selectedProject = DisbursementProjectsList?.SelectedItem as Project;
                var selectedQuote = DisbursementQuotesList?.SelectedItem as Quote;

                if (selectedProject != null)
                {
                    // Load disbursements for specific project (existing functionality)
                    var projectDisbursements = await _disbursementService.GetDisbursementsByProjectAsync(selectedProject.Id);

                    Dispatcher.Invoke(() =>
                    {
                        Disbursements.Clear();
                        foreach (var disbursement in projectDisbursements.OrderByDescending(d => d.Date))
                        {
                            Disbursements.Add(disbursement);
                        }

                        if (DisbursementsDataGrid != null)
                        {
                            DisbursementsDataGrid.ItemsSource = Disbursements;
                        }
                        UpdateDisbursementsSummary();

                        if (DisbursementsHeaderLabel != null)
                        {
                            DisbursementsHeaderLabel.Text = $"Disbursements for {selectedProject.Name}";
                        }
                    });
                }
                else if (selectedQuote != null)
                {
                    // Load disbursements for specific quote
                    var quoteDisbursements = await _disbursementService.GetDisbursementsByQuoteAsync(selectedQuote.Id);

                    Dispatcher.Invoke(() =>
                    {
                        Disbursements.Clear();
                        foreach (var disbursement in quoteDisbursements.OrderByDescending(d => d.Date))
                        {
                            Disbursements.Add(disbursement);
                        }

                        if (DisbursementsDataGrid != null)
                        {
                            DisbursementsDataGrid.ItemsSource = Disbursements;
                        }
                        UpdateDisbursementsSummary();

                        if (DisbursementsHeaderLabel != null)
                        {
                            DisbursementsHeaderLabel.Text = $"Disbursements for {selectedQuote.Name}";
                        }
                    });
                }
                else
                {
                    // Load all user disbursements (default behaviour)
                    await LoadDisbursementsAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDisbursementsAsyncEnhanced error: {ex.Message}");
                MessageBox.Show($"Error loading disbursements: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }





        private async Task FilterDisbursementProjects()
        {
            var searchText = DisbursementProjectSearchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search by name, number or client...")
            {
                DisbursementProjectsList.ItemsSource = Projects.Take(50).ToList();
            }
            else
            {
                DisbRecentProjectSearchesSection.Visibility = Visibility.Collapsed;
                try
                {
                    var searchResults = await Task.Run(() => ServiceLocator.ProjectService.SearchProjects(searchText));
                    DisbursementProjectsList.ItemsSource = searchResults.Take(50).ToList();
                    _searchStateManager?.AddRecentProjectSearch(searchText);
                    RefreshRecentProjectSearchesUI();
                }
                catch (Exception)
                {
                    // Fallback to local filtering
                    var filtered = Projects.Where(p =>
                        (p.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (p.Number?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (p.Client?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    ).Take(50).ToList();

                    DisbursementProjectsList.ItemsSource = filtered;
                }
            }
        }

        private void ClearDisbursementForm()
        {
            try
            {
                // Reset date to today
                DisbursementDatePicker.SelectedDate = DateTime.Today;

                // Clear text fields
                DisbursementUnitsTextBox.Clear();
                DisbursementAmountTextBox.Clear();
                DisbursementDescriptionTextBox.Clear();

                // Clear disbursement type selection (this won't trigger reload because of suppression)
                DisbursementTypeComboBox.SelectedItem = null;

                // DON'T clear project/quote selections - keep them selected for convenience
                // This prevents triggering selection changed events

                // Reset calculated amount
                if (CalculatedAmountLabel != null)
                    CalculatedAmountLabel.Text = "= £0.00";
                if (UnitChargeLabel != null)
                    UnitChargeLabel.Text = "× £0.00";

                // Hide/show appropriate panels
                if (UnitsPanel != null)
                    UnitsPanel.Visibility = Visibility.Collapsed;
                if (UnitsLabel != null)
                    UnitsLabel.Visibility = Visibility.Collapsed;
                if (AmountLabel != null)
                    AmountLabel.Visibility = Visibility.Visible;
                if (DisbursementAmountTextBox != null)
                    DisbursementAmountTextBox.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing disbursement form: {ex.Message}");
            }
        }





        private void UpdateDisbursementFormVisibility()
        {
            try
            {
                var selectedType = DisbursementTypeComboBox.SelectedItem as DisbursementType;

                if (selectedType?.IsUnitBased == true)
                {
                    // Show units panel, hide amount
                    if (UnitsPanel != null) UnitsPanel.Visibility = Visibility.Visible;
                    if (UnitsLabel != null) UnitsLabel.Visibility = Visibility.Visible;
                    if (AmountLabel != null) AmountLabel.Visibility = Visibility.Collapsed;
                    if (DisbursementAmountTextBox != null) DisbursementAmountTextBox.Visibility = Visibility.Collapsed;

                    // Update unit charge label
                    if (UnitChargeLabel != null)
                    {
                        UnitChargeLabel.Text = $"× £{selectedType.UnitCharge:F2}";
                    }
                }
                else
                {
                    // Show amount, hide units panel
                    if (UnitsPanel != null) UnitsPanel.Visibility = Visibility.Collapsed;
                    if (UnitsLabel != null) UnitsLabel.Visibility = Visibility.Collapsed;
                    if (AmountLabel != null) AmountLabel.Visibility = Visibility.Visible;
                    if (DisbursementAmountTextBox != null) DisbursementAmountTextBox.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateDisbursementFormVisibility error: {ex.Message}");
            }
        }




        private async void RefreshDisbursementsButton_Click(object sender, RoutedEventArgs e)
        {
            // Skip reload if suppressed (during add operation)
            if (_suppressDisbursementReload)
                return;

            await LoadDisbursementsAsync();
        }

        private async void DisbursementProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip reload if suppressed (during add operation)
            if (_suppressDisbursementReload)
                return;

            try
            {
                var selectedProject = DisbursementProjectsList.SelectedItem as Project;

                if (selectedProject != null)
                {
                    // Update UI labels
                    if (SelectedProjectLabel != null)
                    {
                        SelectedProjectLabel.Text = $"{selectedProject.Number} - {selectedProject.Name}";
                        SelectedProjectLabel.FontStyle = FontStyles.Normal;
                        SelectedProjectLabel.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                    }

                    // Show client info if available
                    if (DisbursementProjectClientBorder != null && !string.IsNullOrEmpty(selectedProject.Client))
                    {
                        DisbursementProjectClientBorder.Visibility = Visibility.Visible;
                        if (SelectedDisbursementProjectClientLabel != null)
                        {
                            SelectedDisbursementProjectClientLabel.Text = selectedProject.Client;
                        }
                        if (SelectedDisbursementProjectDisciplineLabel != null)
                        {
                            if (!string.IsNullOrWhiteSpace(selectedProject.Discipline))
                            {
                                SelectedDisbursementProjectDisciplineLabel.Text = $"Category: {selectedProject.Discipline}";
                                SelectedDisbursementProjectDisciplineLabel.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                SelectedDisbursementProjectDisciplineLabel.Visibility = Visibility.Collapsed;
                            }
                        }
                    }

                    // Show/update Pin button
                    UpdateDisbPinProjectButtonState(selectedProject);

                    // Load disbursements for this project
                    await LoadDisbursementsForProject(selectedProject);
                }
                else
                {
                    // No project selected - show all disbursements
                    if (DisbPinProjectButton != null) DisbPinProjectButton.Visibility = Visibility.Collapsed;
                    await LoadDisbursementsAsync();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading project disbursements: {ex.Message}");
            }
        }

        /// <summary>
        /// Carries over the last time-entry project to the Disbursements tab and pre-selects it.
        /// Only activates if no project is already selected on the disbursements side.
        /// </summary>
        private void PreSelectCarryOverProject(Project project)
        {
            try
            {
                if (project == null) return;

                // Only carry over when the Project toggle is active
                if (DisbursementProjectToggle.IsChecked != true) return;

                // Don't overwrite an existing selection the user has already made
                if (DisbursementProjectsList.SelectedItem != null) return;

                // Find the matching project in the master list
                var match = Projects.FirstOrDefault(p => p.Id == project.Id);
                if (match == null) return;

                // Ensure the project is visible in the list (it may not be in the default top-50)
                var currentItems = DisbursementProjectsList.ItemsSource as System.Collections.IList;
                bool alreadyVisible = currentItems != null && currentItems.Contains(match);
                if (!alreadyVisible)
                {
                    // Prepend the carry-over project so it's always visible and at the top
                    DisbursementProjectsList.ItemsSource = new List<Project> { match }
                        .Concat(Projects.Where(p => p.Id != match.Id).Take(49))
                        .ToList();
                }

                // Select and scroll to the project
                DisbursementProjectsList.SelectedItem = match;
                DisbursementProjectsList.ScrollIntoView(match);

                // Show the carry-over banner
                CarryOverProjectLabel.Text = $"{match.Number}  –  {match.Name}";
                CarryOverBanner.Visibility = Visibility.Visible;

                System.Diagnostics.Debug.WriteLine($"Carry-over: pre-selected {match.Number} - {match.Name} on Disbursements tab");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PreSelectCarryOverProject error: {ex.Message}");
            }
        }

        /// <summary>
        /// Dismisses the carry-over banner and resets the disbursement project selection.
        /// </summary>
        private async void ClearCarryOver_Click(object sender, RoutedEventArgs e)
        {
            CarryOverBanner.Visibility = Visibility.Collapsed;
            _lastTimeEntryProject = null;
            DisbursementProjectsList.SelectedItem = null;
            // Reset list back to default unfiltered view
            await FilterDisbursementProjects();
        }

        /// <summary>
        /// Context menu "Edit" — delegates to the same logic as double-click.
        /// </summary>
        private void TimeEntriesDataGrid_MouseDoubleClick_ContextMenu(object sender, RoutedEventArgs e)
        {
            // Reuse the existing double-click handler
            TimeEntriesDataGrid_MouseDoubleClick(TimeEntriesDataGrid, null);
        }

        /// <summary>
        /// Context menu "Add Disbursement for this project" on a time entry row.
        /// Carries the project over to the Disbursements tab and switches to it.
        /// </summary>
        private void AddDisbursementFromTimeEntry_Click(object sender, RoutedEventArgs e)
        {
            var selectedEntry = TimeEntriesDataGrid.SelectedItem as TimeEntry;
            if (selectedEntry == null) return;

            Project project = null;

            // Find matching project for this time entry
            if (selectedEntry.Classification == TimeEntryClassification.Project && selectedEntry.ProjectId != Guid.Empty)
            {
                project = Projects.FirstOrDefault(p => p.Id == selectedEntry.ProjectId);
            }

            if (project == null)
            {
                MessageBox.Show(
                    "Disbursements can only be added for project-type time entries.\n\nQuote-based entries are not supported for disbursements via this shortcut.",
                    "Project Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Store as the carry-over project
            _lastTimeEntryProject = project;

            // Switch to Disbursements tab (index 2) — the SelectionChanged handler will fire PreSelectCarryOverProject
            MainTabControl.SelectedIndex = 2;
        }

        private async void DisbursementQuotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip reload if suppressed (during add operation)
            if (_suppressDisbursementReload)
                return;

            try
            {
                var selectedQuote = DisbursementQuotesList.SelectedItem as Quote;

                if (selectedQuote != null)
                {
                    // Update UI labels
                    if (SelectedProjectLabel != null)
                    {
                        SelectedProjectLabel.Text = $"{selectedQuote.QuoteNumber} - {selectedQuote.Name}";
                        SelectedProjectLabel.FontStyle = FontStyles.Normal;
                        SelectedProjectLabel.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                    }

                    // Show client info if available
                    if (DisbursementQuoteClientBorder != null && !string.IsNullOrEmpty(selectedQuote.Client))
                    {
                        DisbursementQuoteClientBorder.Visibility = Visibility.Visible;
                        if (SelectedDisbursementQuoteClientLabel != null)
                        {
                            SelectedDisbursementQuoteClientLabel.Text = selectedQuote.Client;
                        }
                    }

                    // Show/update Pin button
                    UpdateDisbPinQuoteButtonState(selectedQuote);

                    // Load disbursements for this quote
                    await LoadDisbursementsForQuote(selectedQuote);
                }
                else
                {
                    // No quote selected - show all disbursements
                    if (DisbPinQuoteButton != null) DisbPinQuoteButton.Visibility = Visibility.Collapsed;
                    await LoadDisbursementsAsync();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading quote disbursements: {ex.Message}");
            }
        }



        private async void ClearProjectSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Skip reload if suppressed (during add operation)
            if (_suppressDisbursementReload)
                return;

            // Clear selections in both toggles
            if (DisbursementProjectsList != null)
                DisbursementProjectsList.SelectedItem = null;

            if (DisbursementQuotesList != null)
                DisbursementQuotesList.SelectedItem = null;

            // Reset search boxes
            if (DisbursementProjectSearchBox != null)
            {
                DisbursementProjectSearchBox.Text = "Search by name, number or client...";
                DisbursementProjectSearchBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            if (DisbursementQuoteSearchBox != null)
            {
                DisbursementQuoteSearchBox.Text = "Search by name, number or client...";
                DisbursementQuoteSearchBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            // Reload initial data
            await Task.Run(async () =>
            {
                await FilterDisbursementProjects();
                await FilterDisbursementQuotes();
                await LoadDisbursementsAsync();
            });
        }

        private void UpdateDisbursementsSummary()
        {
            var totalDisbursements = Disbursements.Count;
            var totalAmount = Disbursements.Sum(d => d.Amount);

            // Count project vs quote disbursements
            var projectEntries = Disbursements.Count(d => d.Classification == DisbursementClassification.Project);
            var quoteEntries = Disbursements.Count(d => d.Classification == DisbursementClassification.Quote);

            // Use your actual label names from MainWindow
            if (TotalDisbursementsLabel != null)
            {
                if (quoteEntries > 0)
                {
                    // Show breakdown if there are quote disbursements
                    TotalDisbursementsLabel.Text = $"Total disbursements: {totalDisbursements} ({projectEntries} project, {quoteEntries} quote)";
                }
                else
                {
                    // Use existing format if only project disbursements
                    TotalDisbursementsLabel.Text = $"Total disbursements: {totalDisbursements}";
                }
            }

            if (TotalDisbursementAmountLabel != null)
            {
                TotalDisbursementAmountLabel.Text = $"Total amount: £{totalAmount:F2}";
            }
        }

        // New disbursement type selection handler
        private void DisbursementTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isClearingDisbursementForm)
            {
                System.Diagnostics.Debug.WriteLine("DisbursementTypeComboBox_SelectionChanged: Skipping - form is being cleared");
                return;
            }
            try
            {
                var selectedType = DisbursementTypeComboBox.SelectedItem as DisbursementType;

                if (selectedType?.IsUnitBased == true)
                {
                    // Show unit-based input for mileage, printing, and photocopying
                    if (AmountLabel != null) AmountLabel.Visibility = Visibility.Collapsed;
                    if (DisbursementAmountTextBox != null) DisbursementAmountTextBox.Visibility = Visibility.Collapsed;

                    if (UnitsLabel != null)
                    {
                        UnitsLabel.Text = GetUnitsLabel(selectedType.Name);
                        UnitsLabel.Visibility = Visibility.Visible;
                    }
                    if (UnitsPanel != null) UnitsPanel.Visibility = Visibility.Visible;
                    if (UnitChargeLabel != null) UnitChargeLabel.Text = $"× £{selectedType.UnitCharge:F2}";

                    // Clear and calculate
                    if (DisbursementUnitsTextBox != null) DisbursementUnitsTextBox.Clear();
                    UpdateCalculatedAmount(selectedType);
                }
                else if (selectedType != null) // Only show amount fields if we have a valid type
                {
                    // Show amount-based input for hotels, subsistence, etc.
                    if (UnitsLabel != null) UnitsLabel.Visibility = Visibility.Collapsed;
                    if (UnitsPanel != null) UnitsPanel.Visibility = Visibility.Collapsed;

                    if (AmountLabel != null) AmountLabel.Visibility = Visibility.Visible;
                    if (DisbursementAmountTextBox != null) DisbursementAmountTextBox.Visibility = Visibility.Visible;
                }
                else
                {
                    // selectedType is null - hide both panels (this happens during form clearing)
                    // DON'T show validation messages here as this is normal during form clearing
                    if (UnitsLabel != null) UnitsLabel.Visibility = Visibility.Collapsed;
                    if (UnitsPanel != null) UnitsPanel.Visibility = Visibility.Collapsed;
                    if (AmountLabel != null) AmountLabel.Visibility = Visibility.Collapsed;
                    if (DisbursementAmountTextBox != null) DisbursementAmountTextBox.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DisbursementTypeComboBox_SelectionChanged error: {ex.Message}");
            }
        }

        private void DisbursementUnitsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var selectedType = DisbursementTypeComboBox.SelectedItem as DisbursementType;
            if (selectedType?.IsUnitBased == true)
            {
                UpdateCalculatedAmount(selectedType);
            }
        }

        private void UpdateCalculatedAmount(DisbursementType disbursementType)
        {
            if (decimal.TryParse(DisbursementUnitsTextBox.Text, out decimal units))
            {
                var calculatedAmount = units * disbursementType.UnitCharge;
                CalculatedAmountLabel.Text = $"= £{calculatedAmount:F2}";
            }
            else
            {
                CalculatedAmountLabel.Text = "= £0.00";
            }
        }

        private string GetUnitsLabel(string typeName)
        {
            // Customise the label based on disbursement type
            var normalizedTypeName = (typeName ?? string.Empty).Trim().ToLowerInvariant();

            if (normalizedTypeName.Contains("mileage"))
            {
                return "Miles";
            }

            switch (normalizedTypeName)
            {
                case "printing (b&w)":
                case "printing (colour)":
                case "photocopying (b&w)":
                case "photocopying (colour)":
                    return "Number of Pages";
                default:
                    return "Number of Units";
            }
        }
        #endregion

        #region Calendar Methods
        private void InitializeCalendarStructure()
        {
            //CalendarGrid.Children.Clear();

            // Add day name headers
            string[] dayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int i = 0; i < 7; i++)
            {
                var headerLabel = new TextBlock
                {
                    Text = dayNames[i],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Margin = new Thickness(4, 4, 4, 4)
                };
                //CalendarGrid.Children.Add(headerLabel);
            }

            // Create 42 calendar cells - REVERT to StackPanel but with better sizing
            for (int i = 0; i < 42; i++)
            {
                var cellBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    Margin = new Thickness(1, 1, 1, 1),
                    Padding = new Thickness(4, 3, 4, 3),
                    Background = _transparentBrush,
                    Cursor = Cursors.Hand,
                    MinHeight = 80, // Increased from 65
                    MaxHeight = 100 // Increased from 85
                };

                // Use StackPanel with better layout
                var stackPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Day number label
                var dayLabel = new TextBlock
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    Margin = new Thickness(0, 0, 0, 2) // Increased bottom margin
                };

                // Hours label - positioned at bottom right
                var hoursLabel = new TextBlock
                {
                    FontSize = 8, // Reduced from 9 to ensure it fits
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Visibility = Visibility.Collapsed,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(0, 2, 0, 0) // Increased top margin for spacing
                };

                stackPanel.Children.Add(dayLabel);
                stackPanel.Children.Add(hoursLabel);
                cellBorder.Child = stackPanel;

                // Store references for fast updates
                _calendarCells[i] = cellBorder;
                _dayLabels[i] = dayLabel;
                _hoursLabels[i] = hoursLabel;

                // Add click handler with cell index
                int cellIndex = i; // Capture for closure
                cellBorder.MouseLeftButtonUp += (sender, e) => CalendarCell_Click(cellIndex);

                // Add hover effects
                cellBorder.MouseEnter += (sender, e) =>
                {
                    if (cellBorder.Background == _transparentBrush)
                    {
                        cellBorder.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)); // Light hover
                    }
                };

                cellBorder.MouseLeave += (sender, e) =>
                {
                    // Restore original background based on stored date
                    if (_calendarCellDates[cellIndex] != DateTime.MinValue)
                    {
                        UpdateSingleCalendarCell(cellIndex, _calendarCellDates[cellIndex]);
                    }
                };

                //CalendarGrid.Children.Add(cellBorder);
            }
        }





        private async void PreviousMonthButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateMonth(-1);
        }

        private async void NextMonthButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateMonth(1);
        }

        private async Task NavigateMonth(int monthOffset)
        {
            // Prevent multiple simultaneous operations (keep existing lock logic)
            lock (_calendarLock)
            {
                _calendarCancellationToken?.Cancel();
                _calendarCancellationToken = new CancellationTokenSource();
            }

            var newMonth = _currentCalendarMonth.AddMonths(monthOffset);
            System.Diagnostics.Debug.WriteLine($"🗓️ NavigateMonth: {_currentCalendarMonth:yyyy-MM} → {newMonth:yyyy-MM}");

            _currentCalendarMonth = newMonth;
            UpdateCalendarHeader();

            UpdateStatus($"Loading {newMonth:MMMM yyyy}...");

            try
            {
                await LoadCalendarDataAsync();
                System.Diagnostics.Debug.WriteLine($"✅ NavigateMonth: Successfully loaded {newMonth:yyyy-MM}");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ NavigateMonth: Cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ NavigateMonth: Error: {ex.Message}");
                UpdateStatus($"Calendar error: {ex.Message}");
            }
        }


        // 🔧 UPDATE YOUR EXISTING LoadCalendarDataAsync METHOD
        // Replace your existing LoadCalendarDataAsync with this improved version

        private async Task LoadCalendarDataAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"LoadCalendarDataAsync: Starting for user {ServiceLocator.CurrentUserId}");

                // Load user configuration
                if (_currentUserConfig == null)
                {
                    System.Diagnostics.Debug.WriteLine("Loading colleague configuration...");
                    var configService = new ColleagueConfigurationService(ServiceLocator.DataverseConnector);
                    _currentUserConfig = await Task.Run(() => configService.GetColleagueConfiguration(ServiceLocator.CurrentUserId));

                    if (_currentUserConfig == null)
                    {
                        _currentUserConfig = ColleagueConfiguration.CreateDefault();
                        System.Diagnostics.Debug.WriteLine("Using default configuration");
                    }
                }

                // ✅ Pass the calendar month's date range so future entries are included
                System.Diagnostics.Debug.WriteLine("Loading time entries for calendar...");
                var startDate = new DateTime(_currentCalendarMonth.Year, _currentCalendarMonth.Month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);
                var allTimeEntries = await Task.Run(() =>
                    _timeEntryService.GetTimeEntries(ServiceLocator.CurrentUserId, startDate, endDate));

                // Filter for the calendar date range

                var calendarEntries = allTimeEntries
                    .Where(te => te.Date >= startDate && te.Date <= endDate)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Calendar entries for {_currentCalendarMonth:MMMM yyyy}: {calendarEntries.Count}");

                // Group by date and sum hours
                _dailyHours = calendarEntries
                    .GroupBy(te => te.Date.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(te => te.TotalHours));

                System.Diagnostics.Debug.WriteLine($"Days with time entries: {_dailyHours.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadCalendarDataAsync: {ex.Message}");
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateStatus($"Calendar error: {ex.Message}");
                }));
            }

            await Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    UpdateCalendarCells();
                    UpdateStatus($"Calendar updated • {_dailyHours.Count} days with time entries");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Calendar error: {ex.Message}");
                }
            }));
        }

        private Guid GetCurrentUserId()
        {
            // Primary: Use current user object
            if (_currentUser?.Id != null && _currentUser.Id != Guid.Empty)
            {
                return _currentUser.Id;
            }

            // Fallback: Use ServiceLocator
            if (ServiceLocator.CurrentUserId != Guid.Empty)
            {
                System.Diagnostics.Debug.WriteLine("Using ServiceLocator.CurrentUserId as fallback");
                return ServiceLocator.CurrentUserId;
            }

            System.Diagnostics.Debug.WriteLine("❌ No valid current user ID found!");
            return Guid.Empty;
        }

        private (DateTime startDate, DateTime endDate) CalculateCalendarDateRange(DateTime calendarMonth)
        {
            var firstDayOfMonth = new DateTime(calendarMonth.Year, calendarMonth.Month, 1);

            // Calculate first day shown on calendar (may be from previous month)
            var startDate = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek);
            if (firstDayOfMonth.DayOfWeek == DayOfWeek.Sunday)
            {
                startDate = firstDayOfMonth.AddDays(-7); // Show full previous week
            }

            // Calculate last day shown (may be from next month)  
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
            var endDate = lastDayOfMonth.AddDays(6 - (int)lastDayOfMonth.DayOfWeek);
            if (lastDayOfMonth.DayOfWeek == DayOfWeek.Saturday)
            {
                endDate = lastDayOfMonth.AddDays(6); // Show full next week
            }

            return (startDate, endDate);
        }

        private async Task<List<TimeEntry>> GetTimeEntriesWithFallback(CancellationToken cancellationToken)
        {
            List<TimeEntry> timeEntries = null;

            try
            {
                // Primary: Try enhanced async method
                System.Diagnostics.Debug.WriteLine("Attempting GetAllTimeEntriesAsync...");
                timeEntries = await Task.Run(() => _timeEntryService.GetAllTimeEntriesAsync(), cancellationToken);

                if (timeEntries?.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"✓ GetAllTimeEntriesAsync returned {timeEntries.Count} entries");
                    return timeEntries;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetAllTimeEntriesAsync failed: {ex.Message}");
            }

            try
            {
                // Fallback: Use synchronous method
                System.Diagnostics.Debug.WriteLine("Fallback to GetTimeEntries...");
                timeEntries = await Task.Run(() => _timeEntryService.GetTimeEntries(), cancellationToken);

                if (timeEntries?.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"✓ GetTimeEntries returned {timeEntries.Count} entries");
                    return timeEntries;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTimeEntries failed: {ex.Message}");
            }

            // Last resort: Try simple direct query
            try
            {
                System.Diagnostics.Debug.WriteLine("Last resort: Simple direct query...");
                timeEntries = await GetTimeEntriesDirectQuery(cancellationToken);

                if (timeEntries?.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"✓ Direct query returned {timeEntries.Count} entries");
                    return timeEntries;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Direct query failed: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine("❌ All time entry retrieval methods failed!");
            return new List<TimeEntry>();
        }


        private async Task<List<TimeEntry>> GetTimeEntriesDirectQuery(CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!ServiceLocator.DataverseConnector.Connect())
                    {
                        throw new Exception("Failed to connect to Dataverse");
                    }

                    var query = new QueryExpression("fwp_timeentry")
                    {
                        ColumnSet = new ColumnSet(
                            "fwp_timeentryid",
                            "fwp_date",
                            "fwp_decimalhours",
                            "fwp_minutes",
                            "fwp_notes",
                            "fwp_category",
                            "fwp_classification",
                            "fwp_project",
                            "fwp_quote",
                            "fwp_teammember"),
                        Orders = { new OrderExpression("fwp_date", OrderType.Descending) }
                    };

                    var entities = ServiceLocator.DataverseConnector._orgService
                        .RetrieveMultiple(query).Entities.ToList();

                    return entities.Select(TimeEntry.FromEntity).Where(te => te != null).ToList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Direct query error: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        private async Task UpdateStatusOnUIThread(string message)
        {
            await Dispatcher.InvokeAsync(() => UpdateStatus(message));
        }



        private void UpdateCalendarHeader()
        {
            //CurrentMonthLabel.Text = _currentCalendarMonth.ToString("MMMM yyyy");
        }

        private void UpdateCalendarCells()
        {
            if (_webBrowserReady) UpdateCalendarWebBrowser();
        }

        private decimal GetExpectedHoursForDate(DateTime date)
        {
            decimal expectedHours = 0;

            try
            {
                if (_currentUserConfig != null)
                {
                    expectedHours = _currentUserConfig.GetExpectedHoursForDay(date.DayOfWeek);
                }
                else
                {
                    // Fallback for weekdays
                    expectedHours = (date.DayOfWeek >= DayOfWeek.Monday && date.DayOfWeek <= DayOfWeek.Friday) ? 7.5m : 0m;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting expected hours for {date:MM/dd}: {ex.Message}");
                expectedHours = (date.DayOfWeek >= DayOfWeek.Monday && date.DayOfWeek <= DayOfWeek.Friday) ? 7.5m : 0m;
            }

            return expectedHours;
        }

        private void UpdateSingleCalendarCell(int cellIndex, DateTime date)
        {
            var cellBorder = _calendarCells[cellIndex];
            var dayLabel = _dayLabels[cellIndex];
            var hoursLabel = _hoursLabels[cellIndex];

            // Update day number
            dayLabel.Text = date.Day.ToString();

            // Reset styling to defaults
            if (!_isIntelGPU)
            {
                cellBorder.Background = _transparentBrush;
            }
            dayLabel.Foreground = _calendarTextBrush;
            hoursLabel.Foreground = _calendarHoursBrush;
            hoursLabel.Visibility = Visibility.Collapsed;

            // Hide dot by default for Intel GPUs
            if (_isIntelGPU && _calendarDots != null && cellIndex < _calendarDots.Length)
            {
                _calendarDots[cellIndex].Visibility = Visibility.Collapsed;
            }

            decimal expectedHours = _currentUserConfig?.GetExpectedHoursForDay(date.DayOfWeek) ?? 7.5m;

            // Only apply color coding for working days
            if (expectedHours > 0)
            {
                if (_dailyHours?.TryGetValue(date.Date, out decimal hours) == true && hours > 0)
                {
                    decimal percentage = (hours / expectedHours) * 100;

                    if (_isIntelGPU)
                    {
                        // INTEL GPU: Use colored dot indicator
                        if (_calendarDots != null && cellIndex < _calendarDots.Length)
                        {
                            var dot = _calendarDots[cellIndex];
                            dot.Visibility = Visibility.Visible;

                            // Set dot color based on percentage
                            if (percentage < 50)
                            {
                                dot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                            }
                            else if (percentage < 75)
                            {
                                dot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange
                            }
                            else if (percentage < 100)
                            {
                                dot.Fill = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Light Orange
                            }
                            else
                            {
                                dot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                            }

                            // Add tooltip to show percentage
                            dot.ToolTip = $"{percentage:F0}% complete";
                        }
                    }
                    else
                    {
                        // NORMAL GPU: Use full cell background coloring
                        if (percentage < 50)
                        {
                            cellBorder.Background = _calendarRedBrush;
                        }
                        else if (percentage < 75)
                        {
                            cellBorder.Background = _calendarOrangeBrush;
                        }
                        else if (percentage < 100)
                        {
                            cellBorder.Background = _calendarLightOrangeBrush;
                        }
                        else
                        {
                            cellBorder.Background = _calendarGreenBrush;
                        }
                    }

                    hoursLabel.Text = $"{hours:F1}h/{expectedHours:F1}h";
                    hoursLabel.Visibility = Visibility.Visible;
                    hoursLabel.Foreground = _isIntelGPU ? _calendarHoursBrush : _calendarHoursContrastBrush;
                }
                else
                {
                    // Working day with NO time logged
                    if (date.Date < DateTime.Today)
                    {
                        if (_isIntelGPU)
                        {
                            // INTEL GPU: Show red dot for missing time
                            if (_calendarDots != null && cellIndex < _calendarDots.Length)
                            {
                                var dot = _calendarDots[cellIndex];
                                dot.Visibility = Visibility.Visible;
                                dot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                                dot.ToolTip = "No time logged";
                            }
                        }
                        else
                        {
                            // NORMAL GPU: Red background
                            cellBorder.Background = _calendarRedBrush;
                        }

                        hoursLabel.Text = $"0.0h/{expectedHours:F1}h";
                        hoursLabel.Visibility = Visibility.Visible;
                        hoursLabel.Foreground = _isIntelGPU ? _calendarHoursBrush : _calendarHoursContrastBrush;
                    }
                }
            }
            else
            {
                // Weekend or non-working day
                if (_dailyHours?.TryGetValue(date.Date, out decimal hours) == true && hours > 0)
                {
                    hoursLabel.Text = $"{hours:F1}h";
                    hoursLabel.Visibility = Visibility.Visible;
                    hoursLabel.Foreground = _calendarHoursBrush;

                    // Show blue dot for weekend work on Intel GPUs
                    if (_isIntelGPU && _calendarDots != null && cellIndex < _calendarDots.Length)
                    {
                        var dot = _calendarDots[cellIndex];
                        dot.Visibility = Visibility.Visible;
                        dot.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue
                        dot.ToolTip = "Weekend/holiday work";
                    }
                }
            }

            // Different styling for other months
            if (date.Month != _currentCalendarMonth.Month)
            {
                dayLabel.Foreground = _calendarOtherMonthBrush;
                if (!_isIntelGPU && cellBorder.Background == _transparentBrush)
                {
                    cellBorder.Background = _calendarLightGrayBrush;
                }
            }

            // Today highlighting
            if (date.Date == DateTime.Today)
            {
                cellBorder.BorderBrush = _calendarTodayBorderBrush;
                cellBorder.BorderThickness = new Thickness(2, 2, 2, 2);
                dayLabel.FontWeight = FontWeights.Bold;
            }
            else
            {
                cellBorder.BorderBrush = _calendarNormalBorderBrush;
                cellBorder.BorderThickness = new Thickness(1, 1, 1, 1);
                dayLabel.FontWeight = FontWeights.SemiBold;
            }
        }




        // Simple graphics detection without System.Management
        private bool MightBeIntelGraphics()
        {
            try
            {
                // Simple check using environment variables only
                var processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
                var computername = Environment.MachineName ?? "";

                // If we detect Intel processor, assume potential Intel graphics
                return processor.ToLower().Contains("intel");
            }
            catch
            {
                return true; // If we can't detect, assume yes for safety
            }
        }

        private Brush GetCalendarBrush(string colorType)
        {
            switch (colorType)
            {
                case "green":
                    return CreateColorBrushManually(220, 252, 231);
                case "lightOrange":
                    return CreateColorBrushManually(255, 237, 213);
                case "orange":
                    return CreateColorBrushManually(254, 215, 170);
                case "red":
                    return CreateColorBrushManually(254, 226, 226);
                default:
                    return Brushes.Transparent;
            }
        }

        private SolidColorBrush GetProgressBrush(string colorType)
        {
            switch (colorType)
            {
                case "green":
                    return new SolidColorBrush(Color.FromRgb(16, 185, 129));
                case "blue":
                    return new SolidColorBrush(Color.FromRgb(59, 130, 246));
                case "orange":
                    return new SolidColorBrush(Color.FromRgb(245, 158, 11));
                case "red":
                    return new SolidColorBrush(Color.FromRgb(239, 68, 68));
                default:
                    return new SolidColorBrush(Colors.Transparent);
            }
        }


        // Add the click handler method
        private async void CalendarCell_Click(int cellIndex)
        {
            try
            {
                var clickedDate = _calendarCellDates[cellIndex];

                if (clickedDate == DateTime.MinValue)
                    return;

                // Switch to Time Entries tab
                MainTabControl.SelectedIndex = 0; // Assuming Time Entries is the first tab

                // Set the date range to the clicked date (both from and to)
                FromDatePicker.SelectedDate = clickedDate;
                ToDatePicker.SelectedDate = clickedDate;

                // Set the time entry date to the clicked date
                TimeEntryDatePicker.SelectedDate = clickedDate;

                // Load time entries for that date
                await LoadTimeEntriesAsync();

                // NEW: Update progress bar for the clicked date (this will be called by the DatePicker event)
                // But we call it explicitly here too in case the date picker event doesn't fire
                UpdateDailyProgress();

                // Show status message
                UpdateStatus($"Showing time entries for {clickedDate:dddd, dd MMMM yyyy}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading calendar date: {ex.Message}");
            }
        }

        private void UpdateDailyTimeSummary()
        {
            if (_currentUser == null || TimeEntryDatePicker.SelectedDate == null) return;

            try
            {
                var selectedDate = TimeEntryDatePicker.SelectedDate.Value;
                var summary = DailyTimeValidationHelper.GetDailyTimeSummary(
                    selectedDate,
                    _currentUser.Id,
                    TimeEntries);

                // Create context-aware summary text
                string dateContext = selectedDate.Date == DateTime.Today ? "Today" : selectedDate.ToString("dd/MM/yyyy");
                string summaryText = $"{dateContext}: {FormatHoursMinutes(summary.TotalHoursLogged)} / 24h";

                if (summary.IsAtLimit)
                {
                    summaryText += " ⚠️ AT LIMIT";
                }
                else if (summary.IsNearLimit)
                {
                    summaryText += " ⚠️ NEAR LIMIT";
                }
                else if (summary.RemainingHours <= 8)
                {
                    summaryText += $" ({FormatHoursMinutes(summary.RemainingHours)} remaining)";
                }

                // Update status with the summary
                UpdateStatus(summaryText);

                // Debug output for verification
                System.Diagnostics.Debug.WriteLine($"Daily Summary for {selectedDate:yyyy-MM-dd}: {summaryText}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating daily time summary: {ex.Message}");
            }
        }

        private bool IsCurrentMonth()
        {
            var now = DateTime.Now;
            return _currentCalendarMonth.Year == now.Year && _currentCalendarMonth.Month == now.Month;
        }

        private async void TeamMemberComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedMember = TeamMemberComboBox.SelectedItem as TeamMember;
            if (selectedMember != null && selectedMember != _currentUser)
            {
                _currentUser = selectedMember;

                // Reload time entries for selected user
                await LoadTimeEntriesAsync();

                // Update progress after entries are loaded (on UI thread)
                Dispatcher.Invoke(() =>
                {
                    UpdateDailyProgress();
                });

                // Only refresh disbursements if no specific project is selected
                if (DisbursementProjectsList.SelectedItem == null)
                {
                    await LoadDisbursementsAsync();
                }
                // Note: Calendar deliberately NOT refreshed - it always shows the connected user's data
            }
        }

        private async Task LoadDisbursementQuotesAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== LoadDisbursementQuotesAsync: Starting ===");
                UpdateStatus("Loading quotes for disbursements...");

                System.Diagnostics.Debug.WriteLine("LoadDisbursementQuotesAsync: Calling QuoteService.GetQuotes()...");
                var quotes = await Task.Run(() => ServiceLocator.QuoteService.GetQuotes());

                System.Diagnostics.Debug.WriteLine($"LoadDisbursementQuotesAsync: Received {quotes?.Count ?? 0} quotes from service");

                Dispatcher.Invoke(() =>
                {
                    System.Diagnostics.Debug.WriteLine("LoadDisbursementQuotesAsync: Clearing _quotes collection");
                    // Clear and populate the quotes collection
                    _quotes.Clear();

                    var activeQuotes = quotes.Where(q => q.IsActive).ToList();
                    System.Diagnostics.Debug.WriteLine($"LoadDisbursementQuotesAsync: Found {activeQuotes.Count} active quotes (from {quotes.Count} total)");

                    foreach (var quote in activeQuotes)
                    {
                        _quotes.Add(quote);
                    }

                    System.Diagnostics.Debug.WriteLine($"LoadDisbursementQuotesAsync: Added {_quotes.Count} quotes to _quotes collection");

                    // Set the ItemsSource for the disbursement quotes DataGrid
                    if (DisbursementQuotesList != null)
                    {
                        var displayQuotes = _quotes.Take(50).ToList();
                        DisbursementQuotesList.ItemsSource = displayQuotes;
                        System.Diagnostics.Debug.WriteLine($"LoadDisbursementQuotesAsync: Set DisbursementQuotesList ItemsSource to first {displayQuotes.Count} quotes");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: LoadDisbursementQuotesAsync - DisbursementQuotesList is null!");
                    }

                    UpdateStatus($"Loaded {_quotes.Count} quotes for disbursements");
                    System.Diagnostics.Debug.WriteLine($"=== LoadDisbursementQuotesAsync: Completed - {_quotes.Count} quotes loaded ===");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDisbursementQuotesAsync ERROR: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadDisbursementQuotesAsync STACK TRACE: {ex.StackTrace}");
                Dispatcher.Invoke(() =>
                {
                    UpdateStatus($"Error loading quotes for disbursements: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"LoadDisbursementQuotesAsync: {ex.Message}");
                });
            }
        }

        #endregion

        #region Window and System Tray Events
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                _notifyIcon.ShowBalloonTip(3000, "DHA Time Management",
                    "Application minimised to system tray", WinForms.ToolTipIcon.Info);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // Save session before closing/hiding
            SaveCurrentSession();

            // If this is activation from secondary instance, don't show tray message
            if (_isActivatingFromSecondaryInstance)
            {
                _isActivatingFromSecondaryInstance = false; // Reset flag
                return; // Don't cancel or show message - allow normal window operations
            }

            if (!_isExitingFromTray)
            {
                e.Cancel = true;
                Hide();
                _notifyIcon?.ShowBalloonTip(3000, "DHA Time Management",
                    "Application is still running in the system tray", WinForms.ToolTipIcon.Info);
            }
            else
            {
                _searchTimer?.Dispose();
                _disbursementSearchTimer?.Dispose(); // Add this line
                _notifyIcon?.Dispose();
                _dateCheckTimer?.Dispose();
            }
        }

        private void ForceColorModelOverride()
        {
            try
            {
                // Clear WPF's internal color caches using reflection
                var systemColorsType = typeof(SystemColors);
                var systemResourcesType = typeof(SystemParameters);

                // Clear cached system colors (if accessible)
                var colorCacheField = systemColorsType.GetField("_colorCache",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (colorCacheField != null)
                {
                    colorCacheField.SetValue(null, null);
                }

                // Force refresh of application resources
                if (Application.Current != null)
                {
                    Application.Current.Resources.Clear();

                    // Force theme refresh
                    var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                    if (hwndSource != null)
                    {
                        hwndSource.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
                    }
                }

                System.Diagnostics.Debug.WriteLine("Intel color override applied");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Color override failed: {ex.Message}");
            }
        }

        private void ShowApplication()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            _isExitingFromTray = true;
            Close();
        }
        #endregion

        #region Utility Methods

        /// <summary>
        /// Formats a decimal hours value as hours and minutes (e.g. 1.5 → "1h 30m")
        /// </summary>
        private static string FormatHoursMinutes(decimal totalHours)
        {
            if (totalHours == 0) return "0m";

            int wholeHours = (int)Math.Floor(totalHours);
            int minutes = (int)Math.Round((totalHours - wholeHours) * 60);

            if (wholeHours == 0) return $"{minutes}m";
            if (minutes == 0) return $"{wholeHours}h";
            return $"{wholeHours}h {minutes}m";
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                // Always include version number with the status message
                var version = VersionHelper.GetDisplayVersion();
                StatusLabel.Text = $"{message} {version}";
            });
        }

        private void UpdateStatusWithoutVersion(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Text = message;
            });
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                if (isConnected)
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green

                    // Show user-friendly status with current user
                    string userName = ServiceLocator.CurrentUserName ?? "Unknown User";
                    ConnectionStatusLabel.Text = $"Connected to Dynamics as {userName}";
                }
                else
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    ConnectionStatusLabel.Text = "Disconnected";
                }
            });
        }

        private void InitializeVersionDisplay()
        {
            try
            {
                // Set initial status with version
                var version = VersionHelper.GetDisplayVersion();
                StatusLabel.Text = $"Ready {version}";

                // Also update the window title to include version
                this.Title = VersionHelper.GetApplicationTitle();

                System.Diagnostics.Debug.WriteLine($"Application version: {VersionHelper.GetFullVersion()}");
                System.Diagnostics.Debug.WriteLine($"Build date: {VersionHelper.GetBuildDate()}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing version display: {ex.Message}");
                StatusLabel.Text = "Ready v1.0.0"; // Fallback
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Quote Functionality Event Handlers

        // Add these fields to your private fields section
        

        // Add this property to your properties section
        public ObservableCollection<Quote> Quotes
        {
            get => _quotes;
            set
            {
                _quotes.Clear();
                if (value != null)
                {
                    foreach (var quote in value)
                    {
                        _quotes.Add(quote);
                    }
                }
                OnPropertyChanged(nameof(Quotes));
            }
        }

        // Toggle event handlers
        private void ProjectToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (QuoteToggle != null)
                QuoteToggle.IsChecked = false;

            UpdateSelectionPanelVisibility();

            if (SelectedProjectClientLabel != null)
            {
                SelectedProjectClientLabel.Text = "No project selected";
                SelectedProjectClientLabel.FontStyle = FontStyles.Italic;
                SelectedProjectClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }
            if (SelectedProjectDisciplineLabel != null)
                SelectedProjectDisciplineLabel.Visibility = Visibility.Collapsed;
        }

        private void QuoteToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (ProjectToggle != null)
                ProjectToggle.IsChecked = false;

            UpdateSelectionPanelVisibility();


            // Reset quote label when switching to quotes
            if (SelectedQuoteClientLabel != null)
            {
                SelectedQuoteClientLabel.Text = "No quote selected";
                SelectedQuoteClientLabel.FontStyle = FontStyles.Italic;
                SelectedQuoteClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }
        }


        private void UpdateSelectionPanelVisibility()
        {
            if (ProjectSelectionPanel != null && QuoteSelectionPanel != null)
            {
                if (ProjectToggle?.IsChecked == true)
                {
                    // Show project panel and hide quote panel
                    ProjectSelectionPanel.Visibility = Visibility.Visible;
                    QuoteSelectionPanel.Visibility = Visibility.Collapsed;

                    // Show project client label, hide quote client label
                    if (ProjectClientBorder != null)
                        ProjectClientBorder.Visibility = Visibility.Visible;
                    if (QuoteClientBorder != null)
                        QuoteClientBorder.Visibility = Visibility.Collapsed;
                }
                else if (QuoteToggle?.IsChecked == true)
                {
                    // Show quote panel and hide project panel
                    ProjectSelectionPanel.Visibility = Visibility.Collapsed;
                    QuoteSelectionPanel.Visibility = Visibility.Visible;

                    // Show quote client label, hide project client label
                    if (ProjectClientBorder != null)
                        ProjectClientBorder.Visibility = Visibility.Collapsed;
                    if (QuoteClientBorder != null)
                        QuoteClientBorder.Visibility = Visibility.Visible;
                }
            }
        }

        // Quote search event handlers
        private void QuoteSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (QuoteSearchBox.Text == "Search by name, number or client...")
            {
                QuoteSearchBox.Text = "";
            }
            // Show recent searches when the box is empty
            if (string.IsNullOrWhiteSpace(QuoteSearchBox.Text) && _searchStateManager != null)
            {
                RefreshRecentQuoteSearchesUI();
                RecentQuoteSearchesSection.Visibility =
                    _searchStateManager.RecentQuoteSearches.Any() ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private List<Quote> GetAvailableQuotes()
        {
            try
            {
                // Use the correct ServiceLocator property access
                var quoteService = ServiceLocator.QuoteService;
                if (quoteService == null)
                {
                    System.Diagnostics.Debug.WriteLine("QuoteService not available from ServiceLocator");
                    return _quotes?.ToList() ?? new List<Quote>();
                }

                var quotes = quoteService.GetQuotes();
                System.Diagnostics.Debug.WriteLine($"MainWindow.GetAvailableQuotes: Retrieved {quotes.Count} quotes from service");

                return quotes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading quotes for edit dialog: {ex.Message}");

                // Fallback to existing quotes collection
                return _quotes?.ToList() ?? new List<Quote>();
            }
        }

        private void QuoteSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Cancel any existing timer
            _searchTimer?.Dispose();

            // Start a new timer that will trigger the search after a delay
            _searchTimer = new System.Threading.Timer(async _ =>
            {
                await Dispatcher.BeginInvoke(new Action(async () => await FilterQuotes()));
            }, null, SearchDelayMs, Timeout.Infinite);
        }

        private async Task FilterQuotes()
        {
            var searchText = QuoteSearchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search by name, number or client...")
            {
                // If there is already an active selection, do NOT replace the ItemsSource.
                // Replacing it causes WPF to re-select the item by SelectedValuePath and call
                // ScrollIntoView, which makes the list jump – the reported "skipping" behaviour.
                if (QuotesList.SelectedItem != null) return;

                QuotesList.ItemsSource = _quotes.Take(50).ToList();
            }
            else
            {
                RecentQuoteSearchesSection.Visibility = Visibility.Collapsed;
                try
                {
                    // Use the quote service to search if available
                    var searchResults = await Task.Run(() => ServiceLocator.QuoteService.SearchQuotes(searchText));
                    QuotesList.ItemsSource = searchResults;
                    _searchStateManager?.AddRecentQuoteSearch(searchText);
                    RefreshRecentQuoteSearchesUI();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Quote search failed, showing local results: {ex.Message}");
                    // Fallback to local filtering
                    var filtered = _quotes.Where(q =>
                        (q.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (q.QuoteNumber?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (q.Client?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    ).Take(50).ToList();

                    QuotesList.ItemsSource = filtered;
                }
            }
        }

        // Selection changed handler for quotes
        private void QuotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            var selectedQuote = dataGrid?.SelectedItem as Quote;

            if (selectedQuote != null)
            {
                // Use the NEW quote-specific client label
                SelectedQuoteClientLabel.Text = !string.IsNullOrWhiteSpace(selectedQuote.Client)
                    ? selectedQuote.Client
                    : "No client information";
                SelectedQuoteClientLabel.FontStyle = FontStyles.Normal;
                SelectedQuoteClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));

                _selectedQuote = selectedQuote;

                // Show/update Pin button
                UpdatePinQuoteButtonState(selectedQuote);
            }
            else
            {
                SelectedQuoteClientLabel.Text = "No quote selected";
                SelectedQuoteClientLabel.FontStyle = FontStyles.Italic;
                SelectedQuoteClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                _selectedQuote = null;
                if (PinQuoteButton != null) PinQuoteButton.Visibility = Visibility.Collapsed;
            }
        }


        // Load quotes method (add this to your data loading methods)
        private async Task LoadQuotesAsync()
        {
            try
            {
                FileLogger.Info("=== LoadQuotesAsync START ===");
                System.Diagnostics.Debug.WriteLine("=== LoadQuotesAsync: Starting ===");
                UpdateStatus("Loading quotes...");

                System.Diagnostics.Debug.WriteLine("LoadQuotesAsync: Calling QuoteService.GetQuotes()...");
                FileLogger.Info("LoadQuotesAsync: Calling QuoteService.GetQuotes()...");
                var quotes = await Task.Run(() => ServiceLocator.QuoteService.GetQuotes());

                FileLogger.Info($"LoadQuotesAsync: Received {quotes?.Count ?? 0} quotes from service");
                System.Diagnostics.Debug.WriteLine($"LoadQuotesAsync: Received {quotes?.Count ?? 0} quotes from service");

                Dispatcher.Invoke(() =>
                {
                    System.Diagnostics.Debug.WriteLine("LoadQuotesAsync: Clearing Quotes collection");
                    Quotes.Clear();

                    var activeQuotes = quotes.Where(q => q.IsActive).ToList();
                    System.Diagnostics.Debug.WriteLine($"LoadQuotesAsync: Found {activeQuotes.Count} active quotes (from {quotes.Count} total)");

                    foreach (var quote in activeQuotes)
                    {
                        Quotes.Add(quote);
                    }

                    System.Diagnostics.Debug.WriteLine($"LoadQuotesAsync: Added {Quotes.Count} quotes to collection");

                    // Initially show limited quotes for performance
                    var displayQuotes = Quotes.Take(50).ToList();
                    QuotesList.ItemsSource = displayQuotes;

                    System.Diagnostics.Debug.WriteLine($"LoadQuotesAsync: Set ItemsSource to first {displayQuotes.Count} quotes");

                    UpdateStatus($"Loaded {Quotes.Count} quotes");
                    System.Diagnostics.Debug.WriteLine($"=== LoadQuotesAsync: Completed - {Quotes.Count} quotes loaded ===");
                });
            }
            catch (Exception ex)
            {
                FileLogger.Error("LoadQuotesAsync FAILED", ex);
                System.Diagnostics.Debug.WriteLine($"LoadQuotesAsync ERROR: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadQuotesAsync STACK TRACE: {ex.StackTrace}");
                Dispatcher.Invoke(() =>
                {
                    UpdateStatus($"Error loading quotes: {ex.Message}");
                    MessageBox.Show($"Error loading quotes: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void DiagnoseQuoteButton_Click(object sender, RoutedEventArgs e)
        {
            // Create a dialog for searching/diagnosing a specific quote
            var diagnoseWindow = new Window
            {
                Title = "Diagnose Quote - Why isn't my quote showing?",
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search area
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Results
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Search panel
            var searchPanel = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20)
            };

            var searchStack = new StackPanel();
            
            var headerText = new TextBlock
            {
                Text = "🔍 Search for a Quote to Diagnose",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            searchStack.Children.Add(headerText);

            var helpText = new TextBlock
            {
                Text = "Enter a quote number (e.g., Q27557, 27557) or partial quote name to investigate why it's not appearing in the app.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            searchStack.Children.Add(helpText);

            var searchInputPanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var searchTextBox = new TextBox
            {
                Width = 300,
                Height = 36,
                FontSize = 14,
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            searchTextBox.GotFocus += (s, args) => 
            {
                searchTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            };
            searchTextBox.LostFocus += (s, args) => 
            {
                searchTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            };

            var searchButton = new Button
            {
                Content = "🔎 Diagnose Quote",
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(10, 0, 0, 0),
                Height = 36,
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Medium,
                Cursor = Cursors.Hand
            };

            // Style button corners with a template
            searchButton.Template = CreateRoundedButtonTemplate();

            var loadingIndicator = new TextBlock
            {
                Text = "⏳ Searching...",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };

            searchInputPanel.Children.Add(searchTextBox);
            searchInputPanel.Children.Add(searchButton);
            searchInputPanel.Children.Add(loadingIndicator);
            searchStack.Children.Add(searchInputPanel);

            searchPanel.Child = searchStack;
            Grid.SetRow(searchPanel, 0);
            mainGrid.Children.Add(searchPanel);

            // Results area
            var resultsScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(20)
            };

            var resultsTextBlock = new TextBlock
            {
                Text = "Enter a quote number above and click 'Diagnose Quote' to see why it may not be appearing in the application.",
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
            };

            resultsScroller.Content = resultsTextBlock;
            Grid.SetRow(resultsScroller, 1);
            mainGrid.Children.Add(resultsScroller);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20, 10, 20, 20)
            };

            var copyButton = new Button
            {
                Content = "📋 Copy to Clipboard",
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 10, 0),
                IsEnabled = false
            };
            copyButton.Click += (s, args) =>
            {
                if (!string.IsNullOrWhiteSpace(resultsTextBlock.Text) && 
                    resultsTextBlock.Text != "Enter a quote number above and click 'Diagnose Quote' to see why it may not be appearing in the application.")
                {
                    Clipboard.SetText(resultsTextBlock.Text);
                    MessageBox.Show("Diagnostic report copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };

            var closeButton = new Button
            {
                Content = "Close",
                Padding = new Thickness(16, 8, 16, 8)
            };
            closeButton.Click += (s, args) => diagnoseWindow.Close();

            buttonPanel.Children.Add(copyButton);
            buttonPanel.Children.Add(closeButton);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            // Wire up search functionality
            async void PerformSearch()
            {
                var searchTerm = searchTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    MessageBox.Show("Please enter a quote number or search term.", "Search Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                searchButton.IsEnabled = false;
                loadingIndicator.Visibility = Visibility.Visible;
                resultsTextBlock.Text = "Searching...";
                resultsTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                copyButton.IsEnabled = false;

                try
                {
                    var result = await Task.Run(() => ServiceLocator.QuoteService.DiagnoseQuoteToString(searchTerm));
                    
                    resultsTextBlock.Text = result;
                    resultsTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                    copyButton.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    resultsTextBlock.Text = $"❌ Error performing diagnosis:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
                    resultsTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }
                finally
                {
                    searchButton.IsEnabled = true;
                    loadingIndicator.Visibility = Visibility.Collapsed;
                }
            }

            searchButton.Click += (s, args) => PerformSearch();
            
            // Allow Enter key to trigger search
            searchTextBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    PerformSearch();
                }
            };

            diagnoseWindow.Content = mainGrid;
            diagnoseWindow.ShowDialog();
        }

        private ControlTemplate CreateRoundedButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(contentPresenter);
            
            template.VisualTree = border;
            return template;
        }
        private async void DebugQuotesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateStatus("Generating debug report...");

                var debugReport = await Task.Run(() => ServiceLocator.QuoteService.GetQuoteDebugInfo());

                // Create a window to display the debug info
                var debugWindow = new Window
                {
                    Title = "Quote Loading Debug Report",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(20)
                };

                var textBlock = new TextBlock
                {
                    Text = debugReport,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                };

                scrollViewer.Content = textBlock;

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                grid.Children.Add(scrollViewer);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(20, 10, 20, 20)
                };

                var copyButton = new Button
                {
                    Content = "Copy to Clipboard",
                    Padding = new Thickness(16, 8, 16, 8),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                copyButton.Click += (s, args) =>
                {
                    Clipboard.SetText(debugReport);
                    MessageBox.Show("Debug report copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                };

                var closeButton = new Button
                {
                    Content = "Close",
                    Padding = new Thickness(16, 8, 16, 8)
                };
                closeButton.Click += (s, args) => debugWindow.Close();

                buttonPanel.Children.Add(copyButton);
                buttonPanel.Children.Add(closeButton);

                Grid.SetRow(buttonPanel, 1);
                grid.Children.Add(buttonPanel);

                debugWindow.Content = grid;
                debugWindow.ShowDialog();

                UpdateStatus("Debug report generated");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating debug report: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Error generating debug report");
            }
        }

        private static readonly string[] _devAuthorisedEmails = new[]
        {
            "harriet.baker@dhaplanning.co.uk",
            "harriet.hicken@dhaplanning.co.uk",
            "elizabeth.homewood@dhaplanning.co.uk",
            "stsadmin@dhaplanning.co.uk"
        };

        private const string DevEnvironmentUrl = "https://dhapd-dev.crm11.dynamics.com/";
        private const string ProdEnvironmentUrl = "https://dhapd.crm11.dynamics.com";
        private bool _isDevEnvironment = false;

        private void ShowDevButtonIfAuthorised()
        {
            Dispatcher.Invoke(() =>
            {
                var userEmail = ServiceLocator.CurrentUserEmail;
                bool isAuthorised = !string.IsNullOrEmpty(userEmail) &&
                                    _devAuthorisedEmails.Any(e => e.Equals(userEmail, StringComparison.OrdinalIgnoreCase));

                SwitchToDevButton.Visibility = isAuthorised ? Visibility.Visible : Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine($"ShowDevButtonIfAuthorised: email='{userEmail}', authorised={isAuthorised}");
            });
        }

        private void UpdateDevButtonAppearance()
        {
            if (_isDevEnvironment)
            {
                SwitchToDevButton.ToolTip = "Switch back to Production Environment";
                SwitchToDevButton.Content = "🧪 DEV ✓";
                SwitchToDevButton.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Orange
                SwitchToDevButton.Foreground = Brushes.White;
            }
            else
            {
                SwitchToDevButton.ToolTip = "Switch to Dev Environment";
                SwitchToDevButton.Content = "🧪 DEV";
                SwitchToDevButton.Background = Brushes.Transparent;
                SwitchToDevButton.Foreground = (SolidColorBrush)FindResource("PrimaryBrush");
            }
        }

        private async void SwitchToDevButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetUrl;
                string targetName;

                if (_isDevEnvironment)
                {
                    // Switch back to production
                    targetUrl = ProdEnvironmentUrl;
                    targetName = "Production";
                }
                else
                {
                    // Switch to dev
                    targetUrl = DevEnvironmentUrl;
                    targetName = "Dev";
                }

                var result = MessageBox.Show(
                    $"Switch to the {targetName} environment?\n\n" +
                    $"URL: {targetUrl}\n\n" +
                    "This will disconnect from the current environment and reconnect.\n" +
                    "You may be prompted to log in again.",
                    $"Switch to {targetName}",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                UpdateStatus($"Switching to {targetName} environment...");
                UpdateConnectionStatus(false);

                // Switch the environment URL
                var authService = Services.DataverseAuthService.Instance;
                authService.SwitchEnvironment(targetUrl);

                // Reconnect with forced auth
                bool connected = await Task.Run(() => ServiceLocator.Connect(forceReconnect: true, showMessages: false));

                if (connected)
                {
                    _isDevEnvironment = !_isDevEnvironment;
                    UpdateDevButtonAppearance();

                    // Reload all data from the new environment
                    await LoadInitialData();

                    var envLabel = _isDevEnvironment ? " [DEV]" : "";
                    Title = $"DHA Time Management{envLabel}";
                    UpdateStatus($"Connected to {targetName} as {ServiceLocator.CurrentUserName}");
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to connect to the {targetName} environment.\n\n" +
                        "The application will attempt to reconnect to the previous environment.",
                        "Connection Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Try to reconnect to original environment
                    var fallbackUrl = _isDevEnvironment ? DevEnvironmentUrl : ProdEnvironmentUrl;
                    authService.SwitchEnvironment(fallbackUrl);
                    await Task.Run(() => ServiceLocator.Connect(forceReconnect: true, showMessages: false));
                    await LoadInitialData();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error switching environment: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Error switching environment");
                System.Diagnostics.Debug.WriteLine($"SwitchToDevButton_Click error: {ex}");
            }
        }

        #endregion

        #region Impersonation

        private void ShowImpersonateButtonIfAuthorised()
        {
            Dispatcher.Invoke(() =>
            {
                bool canImpersonate = ServiceLocator.CanImpersonate();
                ImpersonateButton.Visibility = canImpersonate ? Visibility.Visible : Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine($"ShowImpersonateButtonIfAuthorised: canImpersonate={canImpersonate}");
            });
        }

        private void UpdateImpersonateButtonAppearance()
        {
            Dispatcher.Invoke(() =>
            {
                if (ServiceLocator.IsImpersonating)
                {
                    ImpersonateButton.Content = $"👤 Viewing as: {ServiceLocator.ImpersonatedUserName}";
                    ImpersonateButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
                    ImpersonateButton.Foreground = Brushes.White;
                    ImpersonateButton.ToolTip = "Click to stop impersonating and revert to your own account";
                }
                else
                {
                    ImpersonateButton.Content = "👤 Impersonate";
                    ImpersonateButton.Background = Brushes.Transparent;
                    ImpersonateButton.Foreground = (SolidColorBrush)FindResource("PrimaryBrush");
                    ImpersonateButton.ToolTip = "View the app as another user (admin only)";
                }
            });
        }

        private async void ImpersonateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If already impersonating, stop
                if (ServiceLocator.IsImpersonating)
                {
                    var stopResult = MessageBox.Show(
                        $"Stop viewing as {ServiceLocator.ImpersonatedUserName}?\n\n" +
                        "The app will reload with your own account's data.",
                        "Stop Impersonation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (stopResult != MessageBoxResult.Yes) return;

                    ServiceLocator.StopImpersonation();
                    UpdateImpersonateButtonAppearance();
                    UpdateStatus("Reverting to your own account...");

                    // Reload all data as the real user
                    await LoadInitialData();
                    return;
                }

                // Show a list of team members to pick from
                var teamMembers = await Task.Run(() => ServiceLocator.TeamMemberService.GetTeamMembers(activeOnly: true));

                if (teamMembers == null || teamMembers.Count == 0)
                {
                    MessageBox.Show("Could not load team members.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Build a simple selection dialog
                var dialog = new Window
                {
                    Title = "Impersonate User",
                    Width = 420,
                    Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize
                };

                var panel = new StackPanel { Margin = new Thickness(16) };

                panel.Children.Add(new TextBlock
                {
                    Text = "Select a user to view the app as:",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = "All Dataverse queries will execute in that user's security context.\nThis helps diagnose permission issues.",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                var searchBox = new TextBox
                {
                    Height = 28,
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(6, 4, 6, 4),
                    Tag = "Search by name..."
                };

                var listBox = new ListBox
                {
                    Height = 320,
                    DisplayMemberPath = "FullName"
                };

                // Populate with all team members
                var sortedMembers = teamMembers.OrderBy(t => t.FullName).ToList();
                foreach (var tm in sortedMembers) listBox.Items.Add(tm);

                // Simple search filter
                searchBox.TextChanged += (s2, e2) =>
                {
                    var term = searchBox.Text?.Trim().ToLower() ?? "";
                    listBox.Items.Clear();
                    foreach (var tm in sortedMembers.Where(t => string.IsNullOrEmpty(term) || t.FullName.ToLower().Contains(term)))
                        listBox.Items.Add(tm);
                };

                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                var okButton = new Button { Content = "Impersonate", Width = 100, Height = 30, IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
                var cancelButton = new Button { Content = "Cancel", Width = 80, Height = 30 };

                listBox.SelectionChanged += (s3, e3) => { okButton.IsEnabled = listBox.SelectedItem != null; };
                listBox.MouseDoubleClick += (s3, e3) => { if (listBox.SelectedItem != null) { dialog.DialogResult = true; dialog.Close(); } };
                okButton.Click += (s3, e3) => { dialog.DialogResult = true; dialog.Close(); };
                cancelButton.Click += (s3, e3) => { dialog.DialogResult = false; dialog.Close(); };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);

                panel.Children.Add(searchBox);
                panel.Children.Add(listBox);
                panel.Children.Add(buttonPanel);
                dialog.Content = panel;

                if (dialog.ShowDialog() != true) return;

                var selectedMember = listBox.SelectedItem as TeamMember;
                if (selectedMember == null) return;

                // Confirm
                var confirmResult = MessageBox.Show(
                    $"Impersonate {selectedMember.FullName}?\n\n" +
                    $"Email: {selectedMember.Email}\n" +
                    $"ID: {selectedMember.Id}\n\n" +
                    "All data queries will run with this user's Dataverse permissions.\n" +
                    "The impersonation indicator will stay visible until you click it again to stop.",
                    "Confirm Impersonation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmResult != MessageBoxResult.Yes) return;

                // Start impersonation
                bool success = ServiceLocator.StartImpersonation(selectedMember.Id, selectedMember.FullName);

                if (!success)
                {
                    MessageBox.Show("Failed to start impersonation. Check debug output for details.",
                        "Impersonation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                UpdateImpersonateButtonAppearance();
                UpdateStatus($"Now viewing as {selectedMember.FullName} — reloading data...");

                // Reload all data in the impersonated user's context
                await LoadInitialData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during impersonation: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"ImpersonateButton_Click error: {ex}");
            }
        }

        #endregion

        #region Debug Mode

        private void DebugModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FileLogger.IsEnabled)
                {
                    // Disable logging
                    FileLogger.Info("User disabled debug mode");
                    FileLogger.LogAppState();
                    FileLogger.Disable();
                    UpdateDebugModeButton();
                    UpdateStatus("Debug mode OFF");
                }
                else
                {
                    // Enable logging
                    FileLogger.Enable();
                    FileLogger.Info("User enabled debug mode");
                    FileLogger.LogAppState();
                    FileLogger.CleanupOldLogs();
                    UpdateDebugModeButton();
                    UpdateStatus($"Debug mode ON — logging to {FileLogger.CurrentLogPath}");

                    MessageBox.Show(
                        $"Debug mode is now ON.\n\n" +
                        $"All activity will be written to:\n{FileLogger.CurrentLogPath}\n\n" +
                        $"Use the app normally to reproduce the issue, then click the button again to stop logging.\n" +
                        $"Send the log file to support for analysis.",
                        "Debug Mode Enabled",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error toggling debug mode: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateDebugModeButton()
        {
            Dispatcher.Invoke(() =>
            {
                if (FileLogger.IsEnabled)
                {
                    DebugModeButton.Content = "📋 Debug ON";
                    DebugModeButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
                    DebugModeButton.Foreground = Brushes.White;
                    DebugModeButton.ToolTip = $"Debug mode is ON — logging to {FileLogger.CurrentLogPath}\nClick to stop logging.";
                }
                else
                {
                    DebugModeButton.Content = "📋 Debug Off";
                    DebugModeButton.Background = Brushes.Transparent;
                    DebugModeButton.Foreground = (SolidColorBrush)FindResource("PrimaryBrush");
                    DebugModeButton.ToolTip = "Toggle debug mode — writes all activity to a log file";
                }
            });
        }

        #endregion

        private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This method is referenced in the XAML but doesn't exist yet
            // You can leave it empty for now or add any project selection logic you need
            var dataGrid = sender as DataGrid;
            var selectedProject = dataGrid?.SelectedItem as Project;

            if (selectedProject != null)
            {
                // Update the client label with the selected project's client
                SelectedProjectClientLabel.Text = !string.IsNullOrWhiteSpace(selectedProject.Client)
                    ? selectedProject.Client
                    : "No client information";
                SelectedProjectClientLabel.FontStyle = FontStyles.Normal;
                SelectedProjectClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // TextPrimaryBrush color

                // Show discipline/category if available
                if (!string.IsNullOrWhiteSpace(selectedProject.Discipline))
                {
                    SelectedProjectDisciplineLabel.Text = $"Category: {selectedProject.Discipline}";
                    SelectedProjectDisciplineLabel.Visibility = Visibility.Visible;
                }
                else
                {
                    SelectedProjectDisciplineLabel.Visibility = Visibility.Collapsed;
                }

                // Show/update Pin button
                UpdatePinProjectButtonState(selectedProject);

                ApplyDefaultCategoryForSelectedProject(selectedProject);

                // Track for carry-over to disbursements tab
                _lastTimeEntryProject = selectedProject;

                // Debug output to verify project selection
                System.Diagnostics.Debug.WriteLine($"Project selected: {selectedProject.Number} - {selectedProject.Name}, Client: {selectedProject.Client}");
            }
            else
            {
                // No project selected
                SelectedProjectClientLabel.Text = "No project selected";
                SelectedProjectClientLabel.FontStyle = FontStyles.Italic;
                SelectedProjectClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // TextSecondaryBrush color
                SelectedProjectDisciplineLabel.Visibility = Visibility.Collapsed;
                ChargeableRadioButton.IsChecked = true;
                if (PinProjectButton != null) PinProjectButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyDefaultCategoryForSelectedProject(Project selectedProject)
        {
            if (selectedProject == null)
            {
                ChargeableRadioButton.IsChecked = true;
                return;
            }

            if (IsDhaPlanningClient(selectedProject.Client))
            {
                SpeculativeRadioButton.IsChecked = true;
            }
            else
            {
                ChargeableRadioButton.IsChecked = true;
            }
        }

        private bool IsDhaPlanningClient(string clientName)
        {
            var normalizedClientName = clientName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedClientName))
            {
                return false;
            }

            return normalizedClientName.IndexOf("DHA Planning", StringComparison.OrdinalIgnoreCase) >= 0;
        }





        #region Pinned Jobs & Recent Searches

        // ── UI refresh helpers (update all panels that share the same data) ─────────────

        private void RefreshPinnedProjectsUI()
        {
            if (_searchStateManager == null) return;
            var pinned = _searchStateManager.PinnedProjects;

            PinnedProjectsPanel.Children.Clear();
            PinnedProjectsSection.Visibility = pinned.Any() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in pinned)
                PinnedProjectsPanel.Children.Add(CreatePinnedProjectChip(item, forDisbursements: false));

            DisbPinnedProjectsPanel.Children.Clear();
            DisbPinnedProjectsSection.Visibility = pinned.Any() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in pinned)
                DisbPinnedProjectsPanel.Children.Add(CreatePinnedProjectChip(item, forDisbursements: true));
        }

        private void RefreshPinnedQuotesUI()
        {
            if (_searchStateManager == null) return;
            var pinned = _searchStateManager.PinnedQuotes;

            PinnedQuotesPanel.Children.Clear();
            PinnedQuotesSection.Visibility = pinned.Any() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in pinned)
                PinnedQuotesPanel.Children.Add(CreatePinnedQuoteChip(item, forDisbursements: false));

            DisbPinnedQuotesPanel.Children.Clear();
            DisbPinnedQuotesSection.Visibility = pinned.Any() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in pinned)
                DisbPinnedQuotesPanel.Children.Add(CreatePinnedQuoteChip(item, forDisbursements: true));
        }

        private void RefreshRecentProjectSearchesUI()
        {
            if (_searchStateManager == null) return;
            var recents = _searchStateManager.RecentProjectSearches;

            RecentProjectSearchesPanel.Children.Clear();
            foreach (var term in recents)
                RecentProjectSearchesPanel.Children.Add(CreateRecentSearchChip(term, isProject: true, forDisbursements: false));

            DisbRecentProjectSearchesPanel.Children.Clear();
            foreach (var term in recents)
                DisbRecentProjectSearchesPanel.Children.Add(CreateRecentSearchChip(term, isProject: true, forDisbursements: true));
        }

        private void RefreshRecentQuoteSearchesUI()
        {
            if (_searchStateManager == null) return;
            var recents = _searchStateManager.RecentQuoteSearches;

            RecentQuoteSearchesPanel.Children.Clear();
            foreach (var term in recents)
                RecentQuoteSearchesPanel.Children.Add(CreateRecentSearchChip(term, isProject: false, forDisbursements: false));

            DisbRecentQuoteSearchesPanel.Children.Clear();
            foreach (var term in recents)
                DisbRecentQuoteSearchesPanel.Children.Add(CreateRecentSearchChip(term, isProject: false, forDisbursements: true));
        }

        // ── Chip factory helpers ──────────────────────────────────────────────────────

        private Button MakeChipButton(string content, string tooltip, Color bgColor, Color fgColor, Color borderColor)
        {
            var chip = new Button
            {
                Content = content,
                ToolTip = tooltip,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(7, 3, 7, 3),
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Background = new SolidColorBrush(bgColor),
                Foreground = new SolidColorBrush(fgColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };

            // Rounded-pill template
            var template = new ControlTemplate(typeof(Button));
            var borderFef = new FrameworkElementFactory(typeof(Border));
            borderFef.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFef.SetBinding(Border.BorderBrushProperty,
                new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFef.SetBinding(Border.BorderThicknessProperty,
                new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFef.SetBinding(Border.PaddingProperty,
                new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            borderFef.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFef.AppendChild(cp);
            template.VisualTree = borderFef;
            chip.Template = template;

            return chip;
        }

        private Button CreatePinnedProjectChip(PinnedItem item, bool forDisbursements)
        {
            var numberPart = string.IsNullOrWhiteSpace(item.Number) ? "" : item.Number;
            var namePart = string.IsNullOrWhiteSpace(item.Name) ? "" :
                (item.Name.Length > 22 ? item.Name.Substring(0, 19) + "\u2026" : item.Name);
            var label = string.IsNullOrWhiteSpace(numberPart)
                ? namePart
                : (string.IsNullOrWhiteSpace(namePart) ? numberPart : $"{numberPart} \u2013 {namePart}");
            var chip = MakeChipButton(
                $"📌 {label}",
                $"{item.Number} — {item.Name}\nClient: {item.Client ?? "—"}\n\nClick: jump to job  •  Right-click: unpin",
                Color.FromRgb(239, 246, 255),   // light blue bg
                Color.FromRgb(37, 99, 235),     // primary blue fg
                Color.FromRgb(191, 219, 254));  // blue border
            chip.Tag = item;
            chip.Click += forDisbursements ? (RoutedEventHandler)DisbPinnedProjectChip_Click : PinnedProjectChip_Click;

            var menu = new ContextMenu();
            var unpinItem = new MenuItem { Header = "⊘ Unpin this job" };
            var capturedItem = item;
            unpinItem.Click += (s, e) => { _searchStateManager.UnpinProject(capturedItem.Id); RefreshPinnedProjectsUI(); };
            menu.Items.Add(unpinItem);
            chip.ContextMenu = menu;
            return chip;
        }

        private Button CreatePinnedQuoteChip(PinnedItem item, bool forDisbursements)
        {
            var numberPart = string.IsNullOrWhiteSpace(item.Number) ? "" : item.Number;
            var namePart = string.IsNullOrWhiteSpace(item.Name) ? "" :
                (item.Name.Length > 22 ? item.Name.Substring(0, 19) + "\u2026" : item.Name);
            var label = string.IsNullOrWhiteSpace(numberPart)
                ? namePart
                : (string.IsNullOrWhiteSpace(namePart) ? numberPart : $"{numberPart} \u2013 {namePart}");
            var chip = MakeChipButton(
                $"📌 {label}",
                $"{item.Number} — {item.Name}\nClient: {item.Client ?? "—"}\n\nClick: jump to quote  •  Right-click: unpin",
                Color.FromRgb(240, 253, 244),   // light green bg
                Color.FromRgb(22, 163, 74),     // green fg
                Color.FromRgb(187, 247, 208));  // green border
            chip.Tag = item;
            chip.Click += forDisbursements ? (RoutedEventHandler)DisbPinnedQuoteChip_Click : PinnedQuoteChip_Click;

            var menu = new ContextMenu();
            var unpinItem = new MenuItem { Header = "⊘ Unpin this quote" };
            var capturedItem = item;
            unpinItem.Click += (s, e) => { _searchStateManager.UnpinQuote(capturedItem.Id); RefreshPinnedQuotesUI(); };
            menu.Items.Add(unpinItem);
            chip.ContextMenu = menu;
            return chip;
        }

        private Button CreateRecentSearchChip(string term, bool isProject, bool forDisbursements)
        {
            var chip = MakeChipButton(
                $"🔍 {term}",
                $"Click to search: {term}",
                Color.FromRgb(241, 245, 249),   // light slate bg
                Color.FromRgb(71, 85, 105),     // slate fg
                Color.FromRgb(203, 213, 225));  // slate border
            chip.Tag = term;
            chip.FontWeight = FontWeights.Normal;

            if (isProject)
                chip.Click += forDisbursements ? (RoutedEventHandler)DisbRecentProjectSearch_Click : RecentProjectSearch_Click;
            else
                chip.Click += forDisbursements ? (RoutedEventHandler)DisbRecentQuoteSearch_Click : RecentQuoteSearch_Click;

            return chip;
        }

        // ── Pinned chip click handlers ─────────────────────────────────────────────────

        private void PinnedProjectChip_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.Tag as PinnedItem;
            if (item == null) return;
            RecentProjectSearchesSection.Visibility = Visibility.Collapsed;
            var project = Projects.FirstOrDefault(p => p.Id == item.Id);
            if (project != null)
            {
                ProjectSearchBox.Text = item.Number;
                ProjectsList.ItemsSource = new List<Project> { project };
                ProjectsList.SelectedItem = project;
            }
            else
            {
                ProjectSearchBox.Text = item.Number; // triggers search via TextChanged
            }
        }

        private void DisbPinnedProjectChip_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.Tag as PinnedItem;
            if (item == null) return;
            DisbRecentProjectSearchesSection.Visibility = Visibility.Collapsed;
            var project = Projects.FirstOrDefault(p => p.Id == item.Id);
            if (project != null)
            {
                DisbursementProjectSearchBox.Text = item.Number;
                DisbursementProjectsList.ItemsSource = new List<Project> { project };
                DisbursementProjectsList.SelectedItem = project;
            }
            else
            {
                DisbursementProjectSearchBox.Text = item.Number;
            }
        }

        private void PinnedQuoteChip_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.Tag as PinnedItem;
            if (item == null) return;
            RecentQuoteSearchesSection.Visibility = Visibility.Collapsed;
            var quote = _quotes.FirstOrDefault(q => q.Id == item.Id);
            if (quote != null)
            {
                QuoteSearchBox.Text = item.Number;
                QuotesList.ItemsSource = new List<Quote> { quote };
                QuotesList.SelectedItem = quote;
            }
            else
            {
                QuoteSearchBox.Text = item.Number;
            }
        }

        private void DisbPinnedQuoteChip_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.Tag as PinnedItem;
            if (item == null) return;
            DisbRecentQuoteSearchesSection.Visibility = Visibility.Collapsed;
            var quote = _quotes.FirstOrDefault(q => q.Id == item.Id);
            if (quote != null)
            {
                DisbursementQuoteSearchBox.Text = item.Number;
                DisbursementQuotesList.ItemsSource = new List<Quote> { quote };
                DisbursementQuotesList.SelectedItem = quote;
            }
            else
            {
                DisbursementQuoteSearchBox.Text = item.Number;
            }
        }

        // ── Recent search chip click handlers ─────────────────────────────────────────

        private void RecentProjectSearch_Click(object sender, RoutedEventArgs e)
        {
            var term = (sender as Button)?.Tag as string;
            if (term == null) return;
            RecentProjectSearchesSection.Visibility = Visibility.Collapsed;
            ProjectSearchBox.Text = term; // triggers TextChanged → FilterProjects
        }

        private void DisbRecentProjectSearch_Click(object sender, RoutedEventArgs e)
        {
            var term = (sender as Button)?.Tag as string;
            if (term == null) return;
            DisbRecentProjectSearchesSection.Visibility = Visibility.Collapsed;
            DisbursementProjectSearchBox.Text = term;
        }

        private void RecentQuoteSearch_Click(object sender, RoutedEventArgs e)
        {
            var term = (sender as Button)?.Tag as string;
            if (term == null) return;
            RecentQuoteSearchesSection.Visibility = Visibility.Collapsed;
            QuoteSearchBox.Text = term;
        }

        private void DisbRecentQuoteSearch_Click(object sender, RoutedEventArgs e)
        {
            var term = (sender as Button)?.Tag as string;
            if (term == null) return;
            DisbRecentQuoteSearchesSection.Visibility = Visibility.Collapsed;
            DisbursementQuoteSearchBox.Text = term;
        }

        // ── Pin button click handlers ─────────────────────────────────────────────────

        private void PinProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProjectsList.SelectedItem as Project;
            if (selected == null) return;
            _searchStateManager.TogglePinProject(new PinnedItem { Id = selected.Id, Number = selected.Number, Name = selected.Name, Client = selected.Client });
            UpdatePinProjectButtonState(selected);
            RefreshPinnedProjectsUI();
        }

        private void PinQuoteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = QuotesList.SelectedItem as Quote;
            if (selected == null) return;
            _searchStateManager.TogglePinQuote(new PinnedItem { Id = selected.Id, Number = selected.QuoteNumber, Name = selected.Name, Client = selected.Client });
            UpdatePinQuoteButtonState(selected);
            RefreshPinnedQuotesUI();
        }

        private void DisbPinProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = DisbursementProjectsList.SelectedItem as Project;
            if (selected == null) return;
            _searchStateManager.TogglePinProject(new PinnedItem { Id = selected.Id, Number = selected.Number, Name = selected.Name, Client = selected.Client });
            UpdateDisbPinProjectButtonState(selected);
            RefreshPinnedProjectsUI();
        }

        private void DisbPinQuoteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = DisbursementQuotesList.SelectedItem as Quote;
            if (selected == null) return;
            _searchStateManager.TogglePinQuote(new PinnedItem { Id = selected.Id, Number = selected.QuoteNumber, Name = selected.Name, Client = selected.Client });
            UpdateDisbPinQuoteButtonState(selected);
            RefreshPinnedQuotesUI();
        }

        // ── Pin button state helpers ──────────────────────────────────────────────────

        private void UpdatePinProjectButtonState(Project project)
        {
            if (project == null || PinProjectButton == null || _searchStateManager == null) return;
            bool isPinned = _searchStateManager.IsProjectPinned(project.Id);
            PinProjectButton.Content = isPinned ? "📌 Pinned" : "📌 Pin";
            PinProjectButton.Visibility = Visibility.Visible;
        }

        private void UpdatePinQuoteButtonState(Quote quote)
        {
            if (quote == null || PinQuoteButton == null || _searchStateManager == null) return;
            bool isPinned = _searchStateManager.IsQuotePinned(quote.Id);
            PinQuoteButton.Content = isPinned ? "📌 Pinned" : "📌 Pin";
            PinQuoteButton.Visibility = Visibility.Visible;
        }

        private void UpdateDisbPinProjectButtonState(Project project)
        {
            if (project == null || DisbPinProjectButton == null || _searchStateManager == null) return;
            bool isPinned = _searchStateManager.IsProjectPinned(project.Id);
            DisbPinProjectButton.Content = isPinned ? "📌 Pinned" : "📌 Pin";
            DisbPinProjectButton.Visibility = Visibility.Visible;
        }

        private void UpdateDisbPinQuoteButtonState(Quote quote)
        {
            if (quote == null || DisbPinQuoteButton == null || _searchStateManager == null) return;
            bool isPinned = _searchStateManager.IsQuotePinned(quote.Id);
            DisbPinQuoteButton.Content = isPinned ? "📌 Pinned" : "📌 Pin";
            DisbPinQuoteButton.Visibility = Visibility.Visible;
        }

        #endregion

        #region Session Management Methods

        /// <summary>
        /// Shows the application window from secondary instance activation
        /// </summary>
        public void ShowApplicationFromSecondaryInstance()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("MainWindow.ShowApplicationFromSecondaryInstance: Activating application from secondary instance");

                // Use Dispatcher to ensure we're on the UI thread
                Dispatcher.Invoke(() =>
                {
                    // If window is in system tray or minimized, restore it
                    if (WindowState == WindowState.Minimized || !IsVisible)
                    {
                        Show();
                        WindowState = WindowState.Normal;
                        System.Diagnostics.Debug.WriteLine("MainWindow.ShowApplicationFromSecondaryInstance: Window restored from minimized/hidden state");
                    }

                    // Bring to front and focus
                    Activate();
                    Focus();

                    // Ensure system tray icon remains visible if we're using it
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = true;
                    }

                    // Flash the window to draw user attention (simple version)
                    FlashWindow();
                });

                // Log the activation for diagnostics
                UpdateStatus("Application activated from secondary instance");

                System.Diagnostics.Debug.WriteLine("MainWindow.ShowApplicationFromSecondaryInstance: Application successfully activated");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.ShowApplication: Error showing application: {ex.Message}");
                UpdateStatus($"Error restoring window: {ex.Message}");

                // Fallback: try to at least make the window visible
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        Show();
                        Topmost = true;
                        Topmost = false; // Trick to bring window to front
                    });
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"MainWindow.ShowApplication: Fallback activation also failed: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>
        /// Saves the current session state to disk
        /// </summary>
        private void SaveCurrentSession()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("MainWindow.SaveCurrentSession: Saving current session state");

                var sessionData = new SessionManager.SessionData
                {
                    // User selection state
                    SelectedTeamMemberId = _currentUser?.Id,
                    LastTimeEntryDate = _lastSelectedTimeEntryDate,

                    // Filter states
                    FromDateFilter = FromDatePicker?.SelectedDate,
                    ToDateFilter = ToDatePicker?.SelectedDate,

                    // UI state
                    SelectedTabIndex = MainTabControl?.SelectedIndex ?? 0,

                    // Last selected project and disbursement type
                    LastSelectedProjectId = (DisbursementProjectsList?.SelectedItem as Project)?.Id,
                    LastSelectedDisbursementTypeId = (DisbursementTypeComboBox?.SelectedItem as DisbursementType)?.Id,

                    // Window state
                    WindowState = new SessionManager.WindowState
                    {
                        Left = Left,
                        Top = Top,
                        Width = Width,
                        Height = Height,
                        WindowStateString = WindowState.ToString(),
                        IsMaximized = WindowState == WindowState.Maximized,
                        IsMinimized = WindowState == WindowState.Minimized
                    }
                };

                if (SessionManager.SaveSession(sessionData))
                {
                    System.Diagnostics.Debug.WriteLine("MainWindow.SaveCurrentSession: Session saved successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("MainWindow.SaveCurrentSession: Failed to save session");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.SaveCurrentSession: Error saving session: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the session state from disk
        /// </summary>
        private void RestoreSession()
        {
            try
            {
                var sessionData = SessionManager.LoadSession();
                if (sessionData != null)
                {
                    System.Diagnostics.Debug.WriteLine("MainWindow.RestoreSession: Restoring session data");

                    // Restore date filters (these can be from previous sessions)
                    if (sessionData?.FromDateFilter.HasValue == true)
                    {
                        FromDatePicker.SelectedDate = sessionData.FromDateFilter.Value;
                        System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreSession: Restored FromDate: {sessionData.FromDateFilter.Value:yyyy-MM-dd}");
                    }

                    if (sessionData?.ToDateFilter.HasValue == true)
                    {
                        ToDatePicker.SelectedDate = sessionData.ToDateFilter.Value;
                        System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreSession: Restored ToDate: {sessionData.ToDateFilter.Value:yyyy-MM-dd}");
                    }

                    // FIXED: DO NOT restore last time entry date - always default to today for new entries
                    // Comment out or remove these lines:
                    /*
                    if (sessionData?.LastTimeEntryDate.HasValue == true)
                    {
                        _lastSelectedTimeEntryDate = sessionData.LastTimeEntryDate.Value;
                        TimeEntryDatePicker.SelectedDate = _lastSelectedTimeEntryDate;
                        System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreSession: Restored LastTimeEntryDate: {_lastSelectedTimeEntryDate:yyyy-MM-dd}");
                    }
                    */

                    // FIXED: Always ensure TimeEntryDatePicker is set to today for new entries
                    TimeEntryDatePicker.SelectedDate = DateTime.Today;
                    _lastSelectedTimeEntryDate = DateTime.Today;
                    System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreSession: Set TimeEntryDatePicker to today: {DateTime.Today:yyyy-MM-dd}");

                    // Restore selected tab (with bounds checking)
                    if (sessionData?.SelectedTabIndex >= 0 && sessionData.SelectedTabIndex < MainTabControl.Items.Count)
                    {
                        MainTabControl.SelectedIndex = sessionData.SelectedTabIndex;
                        System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreSession: Restored SelectedTab: {sessionData.SelectedTabIndex}");
                    }

                    // Restore project selection for disbursements (async to wait for data loading)
                    if (sessionData?.LastSelectedProjectId.HasValue == true)
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay(1000); // Wait for projects to load
                            await Dispatcher.BeginInvoke(new Action(() =>
                            {
                                RestoreProjectSelection(sessionData.LastSelectedProjectId.Value);
                            }));
                        });
                    }

                    // Restore disbursement type selection
                    if (sessionData?.LastSelectedDisbursementTypeId.HasValue == true)
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay(1000); // Wait for types to load
                            await Dispatcher.BeginInvoke(new Action(() =>
                            {
                                RestoreDisbursementTypeSelection(sessionData.LastSelectedDisbursementTypeId.Value);
                            }));
                        });
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("MainWindow.RestoreSession: No session data found");

                    // FIXED: Ensure defaults are set when no session data exists
                    TimeEntryDatePicker.SelectedDate = DateTime.Today;
                    _lastSelectedTimeEntryDate = DateTime.Today;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreSession: Error restoring session: {ex.Message}");

                // FIXED: Ensure defaults are set when session restore fails
                TimeEntryDatePicker.SelectedDate = DateTime.Today;
                _lastSelectedTimeEntryDate = DateTime.Today;
            }
        }

        /// <summary>
        /// Restores window position and state
        /// </summary>
        private void RestoreWindowState(SessionManager.WindowState windowState)
        {
            try
            {
                if (windowState == null || !windowState.IsValidPosition())
                    return;

                // Set position and size
                if (windowState.Left.HasValue) Left = windowState.Left.Value;
                if (windowState.Top.HasValue) Top = windowState.Top.Value;
                if (windowState.Width.HasValue) Width = windowState.Width.Value;
                if (windowState.Height.HasValue) Height = windowState.Height.Value;

                // Restore window state
                if (windowState.IsMaximized == true)
                {
                    WindowState = WindowState.Maximized;
                }
                else if (windowState.IsMinimized == true)
                {
                    WindowState = WindowState.Minimized;
                }
                else
                {
                    WindowState = WindowState.Normal;
                }

                System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreWindowState: Window state restored - Pos: ({Left},{Top}), Size: ({Width},{Height}), State: {WindowState}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreWindowState: Error restoring window state: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the selected project for disbursements
        /// </summary>
        private void RestoreProjectSelection(Guid projectId)
        {
            try
            {
                if (Projects?.Count > 0 && DisbursementProjectsList != null)
                {
                    var project = Projects.FirstOrDefault(p => p.Id == projectId);
                    if (project != null)
                    {
                        DisbursementProjectsList.SelectedItem = project;
                        System.Diagnostics.Debug.WriteLine($"RestoreProjectSelection: Restored project selection: {project.Name}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"RestoreProjectSelection: Project with ID {projectId} not found");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("RestoreProjectSelection: Projects collection or DataGrid is null/empty");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreProjectSelection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the selected disbursement type
        /// </summary>
        private void RestoreDisbursementTypeSelection(int disbursementTypeId)
        {
            try
            {
                var disbursementType = DisbursementTypes?.FirstOrDefault(dt => dt.Id == disbursementTypeId);
                if (disbursementType != null)
                {
                    DisbursementTypeComboBox.SelectedItem = disbursementType;
                    System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreDisbursementTypeSelection: Restored disbursement type: {disbursementType.Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreDisbursementTypeSelection: Disbursement type with ID {disbursementTypeId} not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.RestoreDisbursementTypeSelection: Error restoring disbursement type: {ex.Message}");
            }
        }

        /// <summary>
        /// Flashes the window in the taskbar to draw attention
        /// </summary>
        private void FlashWindow()
        {
            try
            {
                // Simple implementation - briefly set Topmost to draw attention
                var originalTopmost = Topmost;
                Topmost = true;

                // Reset after a brief moment
                Task.Run(async () =>
                {
                    await Task.Delay(200);
                    await Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Topmost = originalTopmost;
                    }));
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.FlashWindow: Error flashing window: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets up auto-save of session data at regular intervals
        /// </summary>
        private void SetupAutoSessionSave()
        {
            try
            {
                // Create a timer that saves session every 5 minutes
                var autoSaveTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(5)
                };

                autoSaveTimer.Tick += (sender, e) =>
                {
                    try
                    {
                        SaveCurrentSession();
                        System.Diagnostics.Debug.WriteLine("MainWindow.SetupAutoSessionSave: Auto-save completed");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"MainWindow.SetupAutoSessionSave: Auto-save failed: {ex.Message}");
                    }
                };

                autoSaveTimer.Start();
                System.Diagnostics.Debug.WriteLine("MainWindow.SetupAutoSessionSave: Auto-save timer started (5 minute interval)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow.SetupAutoSessionSave: Error setting up auto-save: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles debounced session saving when important state changes
        /// </summary>
        private void OnImportantStateChanged()
        {
            try
            {
                // Use a simple debounce mechanism without creating new fields
                Task.Run(async () =>
                {
                    await Task.Delay(2000); // Wait 2 seconds for user to stop making changes
                    SaveCurrentSession();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnImportantStateChanged: Error: {ex.Message}");
            }
        }





        #endregion


        #region Disbursement Editing Methods

        /// <summary>
        /// Sets up the context menu for the Disbursements DataGrid
        /// </summary>
        private void SetupDisbursementsContextMenu()
        {
            var contextMenu = new ContextMenu();

            // Edit menu item
            var editItem = new MenuItem
            {
                Header = "Edit Disbursement",
                Icon = new TextBlock { Text = "✏️", FontSize = 14 }
            };
            editItem.Click += (s, e) =>
            {
                if (DisbursementsDataGrid.SelectedItem is Disbursement selectedDisbursement)
                {
                    EditDisbursement(selectedDisbursement);
                }
            };
            contextMenu.Items.Add(editItem);

            // Delete menu item
            var deleteItem = new MenuItem
            {
                Header = "Delete Disbursement",
                Icon = new TextBlock { Text = "🗑️", FontSize = 14 }
            };
            deleteItem.Click += (s, e) =>
            {
                if (DisbursementsDataGrid.SelectedItem is Disbursement selectedDisbursement)
                {
                    DeleteDisbursement(selectedDisbursement);
                }
            };
            contextMenu.Items.Add(deleteItem);

            // Set the context menu opening event to validate items
            contextMenu.Opened += (s, e) =>
            {
                var hasSelection = DisbursementsDataGrid.SelectedItem != null;
                var canEdit = hasSelection && DisbursementsDataGrid.SelectedItem is Disbursement disbursement &&
                             disbursement.TeamMemberGuid == _currentUser?.Id;

                editItem.IsEnabled = canEdit;
                deleteItem.IsEnabled = canEdit;

                // Update header text based on ownership
                if (hasSelection && DisbursementsDataGrid.SelectedItem is Disbursement selectedDisbursement)
                {
                    if (selectedDisbursement.TeamMemberGuid != _currentUser?.Id)
                    {
                        editItem.Header = "Edit Disbursement (Not Yours)";
                        deleteItem.Header = "Delete Disbursement (Not Yours)";
                    }
                    else
                    {
                        editItem.Header = "Edit Disbursement";
                        deleteItem.Header = "Delete Disbursement";
                    }
                }
            };

            DisbursementsDataGrid.ContextMenu = contextMenu;
        }

        /// <summary>
        /// Handles double-click on disbursements grid
        /// </summary>
        private void DisbursementsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DisbursementsDataGrid.SelectedItem is Disbursement selectedDisbursement)
            {
                EditDisbursement(selectedDisbursement);
            }
        }

        private void FilterDisbursementsByClassification(DisbursementClassification? classification = null)
        {
            try
            {
                var filteredDisbursements = Disbursements.AsEnumerable();

                if (classification.HasValue)
                {
                    filteredDisbursements = filteredDisbursements.Where(d => d.Classification == classification.Value);
                }

                var filtered = filteredDisbursements.OrderByDescending(d => d.Date).ToList();

                DisbursementsDataGrid.ItemsSource = filtered;

                // Update header to show filter
                if (classification.HasValue)
                {
                    var classificationName = classification.Value == DisbursementClassification.Project ? "Project" : "Quote";
                    DisbursementsHeaderLabel.Text = $"{classificationName} Disbursements";
                }
                else
                {
                    DisbursementsHeaderLabel.Text = "All Disbursements";
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error filtering disbursements: {ex.Message}");
            }
        }

        private async Task LoadDisbursementsWithQuoteSupportAsync()
        {
            try
            {
                var selectedProject = DisbursementProjectsList.SelectedItem as Project;
                // If you add quote selection in the future, you can use this:
                // var selectedQuote = QuotesList?.SelectedItem as Quote;

                if (selectedProject != null)
                {
                    // Load disbursements for specific project (existing functionality)
                    var projectDisbursements = await _disbursementService.GetDisbursementsByProjectAsync(selectedProject.Id);

                    Dispatcher.Invoke(() =>
                    {
                        Disbursements.Clear();
                        foreach (var disbursement in projectDisbursements.OrderByDescending(d => d.Date))
                        {
                            Disbursements.Add(disbursement);
                        }

                        DisbursementsDataGrid.ItemsSource = Disbursements;
                        UpdateDisbursementsSummary();
                        DisbursementsHeaderLabel.Text = $"All Disbursements for {selectedProject.Name}";
                    });
                }
                else
                {
                    // Load all disbursements for current user (existing functionality)
                    if (_currentUser == null) return;

                    var allDisbursements = await _disbursementService.GetAllDisbursementsAsync();

                    var userDisbursements = allDisbursements
                        .Where(d => d.TeamMemberGuid == _currentUser.Id)
                        .OrderByDescending(d => d.Date)
                        .ToList();

                    Dispatcher.Invoke(() =>
                    {
                        Disbursements.Clear();
                        foreach (var disbursement in userDisbursements)
                        {
                            Disbursements.Add(disbursement);
                        }

                        DisbursementsDataGrid.ItemsSource = Disbursements;
                        UpdateDisbursementsSummary();
                        DisbursementsHeaderLabel.Text = "All Disbursements";
                    });
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading disbursements: {ex.Message}");
            }
        }

        private async Task EnsureQuotesAreLoaded()
        {
            try
            {
                if (Quotes == null || Quotes.Count == 0)
                {
                    await LoadQuotesAsync();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Note: Quotes not available for disbursements: {ex.Message}");
                // This is not a critical error - disbursements can still work with projects only
            }
        }

        /// <summary>
        /// Handles key down events on the Disbursements DataGrid for keyboard shortcuts
        /// </summary>
        private void DisbursementsDataGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (DisbursementsDataGrid.SelectedItem is Disbursement selectedDisbursement)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                    case Key.F2:
                        EditDisbursement(selectedDisbursement);
                        e.Handled = true;
                        break;

                    case Key.Delete:
                        if (Keyboard.Modifiers == ModifierKeys.None)
                        {
                            DeleteDisbursement(selectedDisbursement);
                            e.Handled = true;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Validates if a disbursement can be edited
        /// </summary>
        private bool ValidateDisbursementEdit(Disbursement disbursement)
        {
            if (disbursement == null)
            {
                System.Diagnostics.Debug.WriteLine("ValidateDisbursementEdit: Disbursement is null");
                return false;
            }

            // 🔥 FIX 1: Check if user is still being loaded
            if (_currentUser == null)
            {
                System.Diagnostics.Debug.WriteLine("ValidateDisbursementEdit: Current user is null, attempting to reload");

                // Try to reload current user synchronously from the loaded team members
                if (TeamMembers?.Any() == true)
                {
                    // Method 1: Try ServiceLocator first
                    var userIdToFind = ServiceLocator.CurrentUserId != Guid.Empty ? ServiceLocator.CurrentUserId : Guid.Empty;

                    if (userIdToFind != Guid.Empty)
                    {
                        _currentUser = TeamMembers.FirstOrDefault(tm => tm.Id == userIdToFind);
                        System.Diagnostics.Debug.WriteLine($"ValidateDisbursementEdit: Found user by ServiceLocator ID: {_currentUser?.FullName}");
                    }

                    // Method 2: Try by name if ID lookup failed
                    if (_currentUser == null && !string.IsNullOrEmpty(ServiceLocator.CurrentUserName) && ServiceLocator.CurrentUserName != "Not connected")
                    {
                        var userName = ServiceLocator.CurrentUserName.ToLower();
                        _currentUser = TeamMembers.FirstOrDefault(tm =>
                            tm.FullName.ToLower().Contains(userName) ||
                            tm.Email?.ToLower().Contains(userName) == true);
                        System.Diagnostics.Debug.WriteLine($"ValidateDisbursementEdit: Found user by name: {_currentUser?.FullName}");
                    }

                    // Update the UI selection if we found the user
                    if (_currentUser != null && TeamMemberComboBox != null)
                    {
                        TeamMemberComboBox.SelectedItem = _currentUser;
                        System.Diagnostics.Debug.WriteLine($"ValidateDisbursementEdit: Updated UI selection to {_currentUser.FullName}");
                    }
                }

                // If we still can't find the current user, show a helpful error
                if (_currentUser == null)
                {
                    System.Diagnostics.Debug.WriteLine("ValidateDisbursementEdit: Still no current user, showing retry dialog");

                    var result = MessageBox.Show(
                        "Unable to identify your user account at this time.\n\n" +
                        "This may happen if the application is still loading data.\n\n" +
                        "Would you like to retry, or cancel this operation?",
                        "User Authentication Issue",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.OK)
                    {
                        // Give the user one more chance by refreshing team members
                        Task.Run(async () =>
                        {
                            try
                            {
                                await LoadTeamMembersAsync();

                                // Try the validation again after a short delay
                                Dispatcher.Invoke(() =>
                                {
                                    if (_currentUser != null)
                                    {
                                        EditDisbursement(disbursement); // Retry the edit operation
                                    }
                                    else
                                    {
                                        MessageBox.Show("Still unable to identify your user account. Please restart the application if this persists.",
                                            "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"ValidateDisbursementEdit: Error during retry: {ex.Message}");
                                Dispatcher.Invoke(() =>
                                {
                                    MessageBox.Show($"Error during retry: {ex.Message}",
                                        "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                });
                            }
                        });
                    }

                    return false; // Block the edit operation
                }
            }

            // 🔥 FIX 2: Enhanced ownership validation with better error messages
            if (disbursement.TeamMemberGuid != _currentUser.Id)
            {
                // Debug logging for troubleshooting
                System.Diagnostics.Debug.WriteLine($"ValidateDisbursementEdit: Ownership check failed");
                System.Diagnostics.Debug.WriteLine($"  Disbursement.TeamMemberGuid: {disbursement.TeamMemberGuid}");
                System.Diagnostics.Debug.WriteLine($"  Current User ID: {_currentUser.Id}");
                System.Diagnostics.Debug.WriteLine($"  Disbursement Owner: {disbursement.TeamMemberName}");
                System.Diagnostics.Debug.WriteLine($"  Current User: {_currentUser.FullName}");

                // 🔥 FIX 3: Check for potential data inconsistency
                if (disbursement.TeamMemberGuid == Guid.Empty)
                {
                    MessageBox.Show(
                        "This disbursement has missing ownership information.\n\n" +
                        "This may indicate a data issue. Please contact your system administrator.",
                        "Data Integrity Issue",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    // Show clearer ownership message
                    string ownerName = !string.IsNullOrEmpty(disbursement.TeamMemberName)
                        ? disbursement.TeamMemberName
                        : "Unknown User";

                    MessageBox.Show(
                        $"This disbursement belongs to {ownerName} and cannot be edited.\n\n" +
                        $"You are currently logged in as {_currentUser.FullName}.\n\n" +
                        "You can only edit your own disbursements.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return false;
            }

            // 🔥 FIX 4: Additional validation checks
            if (disbursement.IdGuid == Guid.Empty && disbursement.Id <= 0)
            {
                System.Diagnostics.Debug.WriteLine("ValidateDisbursementEdit: Disbursement has no valid ID");
                MessageBox.Show("This disbursement has an invalid ID and cannot be edited.",
                    "Data Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"ValidateDisbursementEdit: Validation passed for disbursement {disbursement.IdGuid}");
            return true;
        }


        /// <summary>
        /// Opens the edit disbursement dialog
        /// </summary>
        private void EditDisbursement(Disbursement disbursement)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"EditDisbursement: Starting edit for disbursement {disbursement?.IdGuid}");

                // 🔥 IMPROVEMENT: Validate before attempting to open dialog
                if (!ValidateDisbursementEdit(disbursement))
                {
                    System.Diagnostics.Debug.WriteLine("EditDisbursement: Validation failed, aborting edit");
                    return;
                }

                // 🔥 IMPROVEMENT: Ensure we have required data loaded
                if (Projects == null || !Projects.Any())
                {
                    MessageBox.Show("Projects are still loading. Please wait a moment and try again.",
                        "Data Loading", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Trigger project reload in the background
                    Task.Run(async () => await LoadProjectsAsync());
                    return;
                }

                if (DisbursementTypes == null || !DisbursementTypes.Any())
                {
                    MessageBox.Show("Disbursement types are still loading. Please wait a moment and try again.",
                        "Data Loading", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Trigger disbursement types reload in the background
                    Task.Run(async () => await LoadDisbursementTypesAsync());
                    return;
                }

                // Original dialog creation logic (unchanged)
                System.Diagnostics.Debug.WriteLine("EditDisbursement: Creating edit dialog");

                var dialog = new EditDisbursementDialog(disbursement, Projects.ToList(), Quotes?.ToList(), DisbursementTypes?.ToList(), _currentUser);

                if (dialog.ShowDialog() == true && dialog.UpdatedDisbursement != null)
                {
                    // Handle successful edit...
                    // [Rest of your existing edit logic here]
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EditDisbursement: Unexpected error: {ex}");

                // Use the ErrorMessageHelper for user-friendly error messages
                var friendlyMessage = ErrorMessageHelper.GetFriendlyErrorMessage(ex.Message);
                if (friendlyMessage != null)
                {
                    MessageBox.Show(friendlyMessage, "Edit Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        /// <summary>
        /// Validates if a disbursement can be edited
        /// </summary>


        /// <summary>
        /// Deletes a disbursement after confirmation
        /// </summary>
        private async void DeleteDisbursement(Disbursement disbursement)
        {
            if (!ValidateDisbursementEdit(disbursement))
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete this disbursement?\n\n" +
                $"Date: {disbursement.Date:dd/MM/yyyy}\n" +
                $"Type: {disbursement.DisbursementTypeName}\n" +
                $"Amount: £{disbursement.Amount:F2}\n" +
                $"Description: {disbursement.Description}",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    UpdateStatus("Deleting disbursement...");

                    await Task.Run(() => _disbursementService.DeleteDisbursementAsync(disbursement.IdGuid));

                    Disbursements.Remove(disbursement);
                    UpdateDisbursementsSummary();
                    UpdateStatus("Disbursement deleted successfully");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error deleting disbursement: {ex.Message}");
                    MessageBox.Show($"Error deleting disbursement: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Disbursement Toggle Event Handlers

        private void DisbursementProjectToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (DisbursementQuoteToggle != null)
                DisbursementQuoteToggle.IsChecked = false;

            UpdateDisbursementSelectionPanelVisibility();

            // Reset project label when switching to projects
            if (SelectedDisbursementProjectClientLabel != null)
            {
                SelectedDisbursementProjectClientLabel.Text = "No project selected";
                SelectedDisbursementProjectClientLabel.FontStyle = FontStyles.Italic;
                SelectedDisbursementProjectClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }
            if (SelectedDisbursementProjectDisciplineLabel != null)
                SelectedDisbursementProjectDisciplineLabel.Visibility = Visibility.Collapsed;

            // Update the main selection label
            if (SelectedProjectLabel != null)
            {
                SelectedProjectLabel.Text = "No project selected";
            }

            // Clear any existing disbursement list filter
            ClearDisbursementSelection();
        }

        private void DisbursementQuoteToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (DisbursementProjectToggle != null)
                DisbursementProjectToggle.IsChecked = false;

            UpdateDisbursementSelectionPanelVisibility();

            // Reset quote label when switching to quotes
            if (SelectedDisbursementQuoteClientLabel != null)
            {
                SelectedDisbursementQuoteClientLabel.Text = "No quote selected";
                SelectedDisbursementQuoteClientLabel.FontStyle = FontStyles.Italic;
                SelectedDisbursementQuoteClientLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            // Update the main selection label
            if (SelectedProjectLabel != null)
            {
                SelectedProjectLabel.Text = "No quote selected";
            }

            // Clear any existing disbursement list filter
            ClearDisbursementSelection();
        }

        private void UpdateDisbursementSelectionPanelVisibility()
        {
            if (DisbursementProjectSelectionPanel != null && DisbursementQuoteSelectionPanel != null)
            {
                if (DisbursementProjectToggle?.IsChecked == true)
                {
                    // Show project panel and hide quote panel
                    DisbursementProjectSelectionPanel.Visibility = Visibility.Visible;
                    DisbursementQuoteSelectionPanel.Visibility = Visibility.Collapsed;

                    // Hide client borders initially
                    if (DisbursementProjectClientBorder != null)
                        DisbursementProjectClientBorder.Visibility = Visibility.Collapsed;
                    if (DisbursementQuoteClientBorder != null)
                        DisbursementQuoteClientBorder.Visibility = Visibility.Collapsed;
                }
                else if (DisbursementQuoteToggle?.IsChecked == true)
                {
                    // Show quote panel and hide project panel
                    DisbursementProjectSelectionPanel.Visibility = Visibility.Collapsed;
                    DisbursementQuoteSelectionPanel.Visibility = Visibility.Visible;

                    // Hide client borders initially
                    if (DisbursementProjectClientBorder != null)
                        DisbursementProjectClientBorder.Visibility = Visibility.Collapsed;
                    if (DisbursementQuoteClientBorder != null)
                        DisbursementQuoteClientBorder.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ClearDisbursementSelection()
        {
            try
            {
                // Clear project selection safely
                if (DisbursementProjectsList != null)
                {
                    DisbursementProjectsList.SelectedItem = null;
                }

                // Clear quote selection safely
                if (DisbursementQuotesList != null)
                {
                    DisbursementQuotesList.SelectedItem = null;
                }

                // Reset disbursement list to show all user disbursements
                _ = Task.Run(async () => await LoadDisbursementsAsync());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearDisbursementSelection error: {ex.Message}");
            }
        }

        #endregion

        private async Task LoadDisbursementsForProject(Project project)
        {
            try
            {
                if (project == null) return;

                var projectDisbursements = await _disbursementService.GetDisbursementsByProjectAsync(project.Id);

                Dispatcher.Invoke(() =>
                {
                    Disbursements.Clear();
                    foreach (var disbursement in projectDisbursements.OrderByDescending(d => d.Date))
                    {
                        Disbursements.Add(disbursement);
                    }

                    // Null-safe access to UI elements
                    if (DisbursementsDataGrid != null)
                    {
                        DisbursementsDataGrid.ItemsSource = Disbursements;
                    }

                    UpdateDisbursementsSummary();

                    if (DisbursementsHeaderLabel != null)
                    {
                        DisbursementsHeaderLabel.Text = $"Disbursements for {project.Name}";
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDisbursementsForProject error: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading disbursements for project: {ex.Message}",
                                   "Data Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        private async Task LoadDisbursementsForQuote(Quote quote)
        {
            try
            {
                // This will work once the database fields are added and enhanced service methods are implemented
                List<Disbursement> quoteDisbursements;

                try
                {
                    // Try the enhanced method first (will be available after DB update)
                    quoteDisbursements = await _disbursementService.GetDisbursementsByQuoteAsync(quote.Id);
                }
                catch
                {
                    // Fallback: filter existing disbursements by quote classification for now
                    var allDisbursements = await _disbursementService.GetAllDisbursementsAsync();
                    quoteDisbursements = allDisbursements
                        .Where(d => d.Classification == DisbursementClassification.Quote && d.QuoteId == quote.Id)
                        .ToList();
                }

                Dispatcher.Invoke(() =>
                {
                    Disbursements.Clear();
                    foreach (var disbursement in quoteDisbursements.OrderByDescending(d => d.Date))
                    {
                        Disbursements.Add(disbursement);
                    }

                    DisbursementsDataGrid.ItemsSource = Disbursements;
                    UpdateDisbursementsSummary();
                    DisbursementsHeaderLabel.Text = $"Disbursements for {quote.Name}";
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading quote disbursements: {ex.Message}");
                // Show empty list rather than error out completely
                Dispatcher.Invoke(() =>
                {
                    Disbursements.Clear();
                    DisbursementsDataGrid.ItemsSource = Disbursements;
                    UpdateDisbursementsSummary();
                    DisbursementsHeaderLabel.Text = "No disbursements found";
                });
            }
        }
        private void InitializeDisbursementToggles()
        {
            try
            {
                // Set default to project mode
                if (DisbursementProjectToggle != null)
                {
                    DisbursementProjectToggle.IsChecked = true;
                    UpdateDisbursementSelectionPanelVisibility();
                }

                // Initialize project list
                if (DisbursementProjectsList != null && Projects != null)
                {
                    DisbursementProjectsList.ItemsSource = Projects.Take(50).ToList();
                }

                // Initialize quote list - FIXED to use the loaded quotes
                if (DisbursementQuotesList != null && _quotes != null && _quotes.Count > 0)
                {
                    DisbursementQuotesList.ItemsSource = _quotes.Where(q => q.IsActive).Take(50).ToList();
                    System.Diagnostics.Debug.WriteLine($"InitializeDisbursementToggles: Set {_quotes.Count} quotes to DisbursementQuotesList");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("InitializeDisbursementToggles: No quotes available or DisbursementQuotesList is null");
                }

                // Set default search text
                if (DisbursementProjectSearchBox != null)
                {
                    DisbursementProjectSearchBox.Text = "Search by name, number or client...";
                    DisbursementProjectSearchBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                }

                if (DisbursementQuoteSearchBox != null)
                {
                    DisbursementQuoteSearchBox.Text = "Search by name, number or client...";
                    DisbursementQuoteSearchBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing disbursement toggles: {ex.Message}");
            }
        }



        #region Time Slot Progress Bar Enhancement

        /// <summary>
        /// Data structure to track time entries by 15-minute slots
        /// </summary>
        public class TimeSlot
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public decimal LoggedHours { get; set; }
            public bool IsOccupied => LoggedHours > 0;
            public List<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();

            public string ToolTipText => IsOccupied
                ? $"{StartTime:HH:mm}-{EndTime:HH:mm}: {FormatHoursMinutes(LoggedHours)} logged\n{string.Join("\n", TimeEntries.Select(te => $"• {te.ProjectName}: {te.TotalTime}"))}"
                : $"{StartTime:HH:mm}-{EndTime:HH:mm}: Click to log time";

            // Additional helper properties
            public bool CanAcceptMoreTime => LoggedHours < 0.25m; // 15 minutes = 0.25 hours
            public decimal RemainingCapacity => Math.Max(0, 0.25m - LoggedHours);

            public string GetStatusDescription()
            {
                if (!IsOccupied)
                    return "Available for time logging";

                if (LoggedHours >= 0.25m)
                    return "Fully logged (15 minutes)";

                var remainingMinutes = (int)((0.25m - LoggedHours) * 60);
                return $"{remainingMinutes} minutes remaining";
            }
        }

        // Private fields for time slot functionality
        private List<TimeSlot> _timeSlots = new List<TimeSlot>();
        private const int MINUTES_PER_SLOT = 15;
        private DateTime _workDayStart = new DateTime(1, 1, 1, 9, 0, 0); // 9:00 AM
        private DateTime _workDayEnd = new DateTime(1, 1, 1, 17, 30, 0);   // 5:30 PM
        private bool _isTimeSlotModeEnabled = true; // Toggle for new vs old mode

        // Rest of the time slot methods remain the same as in the first artifact...
        // (CreateTimeSlotProgressBar, CalculateSlotLoggedHours, etc.)

        #endregion


        #region WebBrowser Intel Graphics Fix

        private bool _webBrowserReady = false;

        private void InitializeWebBrowserComponents()
        {
            this.Loaded += async (s, e) =>
            {
                await Task.Delay(500);

                // Set up navigation event handler for calendar clicks
                if (CalendarWebBrowser != null)
                {
                    CalendarWebBrowser.Navigating += CalendarWebBrowser_Navigating;
                    // Remove the LoadCompleted handler - it was causing the crash
                }

                UpdateProgressWebBrowser();
                UpdateCalendarWebBrowser();
                _webBrowserReady = true;
            };
        }

        private void CalendarWebBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            // Ensure calendar content is loaded when WebBrowser is ready
            if (_webBrowserReady)
            {
                UpdateCalendarWebBrowser();
            }
        }


        private async void CalendarWebBrowser_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            if (e.Uri != null && e.Uri.Scheme == "calendar-date")
            {
                // Cancel the navigation
                e.Cancel = true;

                // Extract the date from the URL
                var dateString = e.Uri.Host;

                if (DateTime.TryParse(dateString, out DateTime selectedDate))
                {
                    try
                    {
                        // Switch to Time Entries tab
                        MainTabControl.SelectedIndex = 0;

                        // Set date pickers
                        FromDatePicker.SelectedDate = selectedDate;
                        ToDatePicker.SelectedDate = selectedDate;
                        TimeEntryDatePicker.SelectedDate = selectedDate;

                        // Refresh time entries
                        await LoadTimeEntriesAsync();

                        UpdateStatus($"Showing time entries for {selectedDate:dddd, dd MMMM yyyy}");
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Error loading date: {ex.Message}");
                    }
                }
            }
        }


        private void UpdateProgressWebBrowser()
        {
            if (ProgressWebBrowser == null) return;

            var percentage = _todayExpectedHours > 0 ?
                Math.Min((_todayActualHours / _todayExpectedHours) * 100, 100) : 0;

            var color = percentage >= 100 ? "#10b981" :
                       percentage >= 75 ? "#3b82f6" :
                       percentage >= 50 ? "#f59e0b" : "#ef4444";

            // Smaller, cleaner progress bar
            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ margin: 6px; font-family: 'Segoe UI'; background: transparent; overflow: hidden; }}
.progress {{ height: 18px; background: #e5e7eb; border-radius: 9px; overflow: hidden; margin-bottom: 6px; }}
.bar {{ height: 100%; background: {color}; width: {percentage:F0}%; transition: width 0.5s; }}
.text {{ text-align: center; font-size: 12px; color: #374151; font-weight: 500; }}
</style></head><body>
<div class='progress'><div class='bar'></div></div>
<div class='text'>{_todayActualHours:F1}h / {_todayExpectedHours:F1}h ({percentage:F0}%)</div>
</body></html>";

            ProgressWebBrowser.NavigateToString(html);
        }

        private void UpdateCalendarWebBrowser()
        {
            if (CalendarWebBrowser == null) return;

            var month = _currentCalendarMonth;
            var today = DateTime.Today;
            var firstDay = new DateTime(month.Year, month.Month, 1);
            var startDate = firstDay.AddDays(-((int)firstDay.DayOfWeek));

            var rows = new StringBuilder();

            // Larger header
            rows.AppendLine(@"
<tr style='background: #f8f9fa; font-weight: 600; font-size: 13px; color: #374151; height: 30px;'>
    <td>SUN</td><td>MON</td><td>TUE</td><td>WED</td><td>THU</td><td>FRI</td><td>SAT</td>
</tr>");

            // Calendar rows with larger cells and text
            for (int week = 0; week < 6; week++)
            {
                rows.AppendLine("<tr>");

                for (int day = 0; day < 7; day++)
                {
                    var date = startDate.AddDays(week * 7 + day);
                    var isToday = date.Date == today;
                    var isCurrentMonth = date.Month == month.Month;

                    var bgColor = "#ffffff";
                    var textColor = "#374151";
                    var hoursText = "";

                    if (_dailyHours?.ContainsKey(date) == true)
                    {
                        var hours = _dailyHours[date];
                        if (hours > 0)
                        {
                            var expectedHours = _currentUserConfig?.GetExpectedHoursForDay(date.DayOfWeek) ?? 7.5m;
                            if (expectedHours > 0)
                            {
                                var pct = (double)(hours / expectedHours) * 100;
                                bgColor = pct >= 100 ? "#f0fdf4" : pct >= 75 ? "#fff7ed" : pct >= 50 ? "#fef3c7" : "#fef2f2";
                            }
                            hoursText = $"<div style='font-size: 10px; color: #6b7280; margin-top: 2px;'>{hours:F1}h</div>";
                        }
                    }

                    if (!isCurrentMonth)
                    {
                        textColor = "#9ca3af";
                        bgColor = "#f9fafb";
                    }

                    var todayBorder = isToday ? "border: 2px solid #3b82f6;" : "border: 1px solid #e5e7eb;";
                    var dateKey = date.ToString("yyyy-MM-dd");

                    // Larger cells with bigger text
                    rows.AppendLine($@"
<td style='width: 14.28%; height: 45px; {todayBorder} background: {bgColor}; 
           vertical-align: top; padding: 4px; text-align: left;'>
    <a href='calendar-date://{dateKey}' style='text-decoration: none; color: inherit; display: block; width: 100%; height: 100%;'>
        <div style='font-size: 14px; font-weight: 600; color: {textColor}; line-height: 1;'>{date.Day}</div>
        {hoursText}
    </a>
</td>");
                }

                rows.AppendLine("</tr>");
            }

            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ margin: 4px; font-family: 'Segoe UI'; background: transparent; overflow: hidden; }}
table {{ width: 100%; border-collapse: collapse; border: 1px solid #374151; }}
td {{ border: 1px solid #e5e7eb; }}
a:hover {{ opacity: 0.8; }}
</style></head><body>
<table>{rows}</table>
</body></html>";

            CalendarWebBrowser.NavigateToString(html);

            if (CalendarTitle != null)
                CalendarTitle.Text = month.ToString("MMMM yyyy").ToUpper();
        }

        // Navigation event handlers
        private async void PreviousMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentCalendarMonth = _currentCalendarMonth.AddMonths(-1);
            try { await LoadCalendarDataAsync(); } catch { }
            UpdateCalendarWebBrowser();
        }

        private async void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentCalendarMonth = _currentCalendarMonth.AddMonths(1);
            try { await LoadCalendarDataAsync(); } catch { }
            UpdateCalendarWebBrowser();
        }

        #endregion

        public class CalendarScriptInterface
        {
            private MainWindow _mainWindow;
            public CalendarScriptInterface(MainWindow mainWindow) => _mainWindow = mainWindow;

            public void OnCalendarDateClick(string dateString)
            {
                if (DateTime.TryParse(dateString, out DateTime clickedDate))
                {
                    _mainWindow.Dispatcher.Invoke(async () =>
                    {
                        // Switch to Time Entries tab
                        _mainWindow.MainTabControl.SelectedIndex = 0;

                        // Set date pickers
                        _mainWindow.FromDatePicker.SelectedDate = clickedDate;
                        _mainWindow.ToDatePicker.SelectedDate = clickedDate;
                        _mainWindow.TimeEntryDatePicker.SelectedDate = clickedDate;

                        // Refresh time entries
                        try { await _mainWindow.LoadTimeEntriesAsync(); } catch { }
                    });
                }
            }
        }



    }



}