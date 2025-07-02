using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using DHA.DSTC.WPF.DataAccess;
using DHA.DSTC.WPF.ProjectProperties;

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

        // Collections for data binding
        private ObservableCollection<TimeEntry> _timeEntries;
        private ObservableCollection<Project> _projects;
        private ObservableCollection<TeamMember> _teamMembers;
        private ObservableCollection<Disbursement> _disbursements;
        private ObservableCollection<DisbursementType> _disbursementTypes;

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

        // Sticky time entry date
        private DateTime _lastSelectedTimeEntryDate = DateTime.Today;

        // Daily progress tracking
        private decimal _todayExpectedHours = 8.0m;
        private decimal _todayActualHours = 0.0m;
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

            // Set default date ranges
            FromDatePicker.SelectedDate = DateTime.Today;
            ToDatePicker.SelectedDate = DateTime.Today;
            TimeEntryDatePicker.SelectedDate = DateTime.Today;
            DisbursementDatePicker.SelectedDate = DateTime.Today;

            InitializeSystemTray();
            InitializeEventHandlers();
            InitializeCalendarStructure();
            LoadInitialData();
        }

        private void InitializeEventHandlers()
        {
            // Time Entry events
            AddTimeEntryButton.Click += AddTimeEntryButton_Click;
            ProjectSearchBox.TextChanged += ProjectSearchBox_TextChanged;
            ProjectSearchBox.GotFocus += ProjectSearchBox_GotFocus;
            RefreshEntriesButton.Click += RefreshEntriesButton_Click;
            TimeEntriesDataGrid.MouseDoubleClick += TimeEntriesDataGrid_MouseDoubleClick;
            TimeEntriesDataGrid.KeyDown += TimeEntriesDataGrid_KeyDown;
            TimeEntryDatePicker.SelectedDateChanged += TimeEntryDatePicker_SelectedDateChanged;

            // Disbursement events
            AddDisbursementButton.Click += AddDisbursementButton_Click;
            DisbursementProjectSearchBox.TextChanged += DisbursementProjectSearchBox_TextChanged;
            DisbursementProjectSearchBox.GotFocus += DisbursementProjectSearchBox_GotFocus;
            DisbursementProjectsList.SelectionChanged += DisbursementProjectsList_SelectionChanged;
            ClearProjectSelectionButton.Click += ClearProjectSelectionButton_Click;
            RefreshDisbursementsButton.Click += RefreshDisbursementsButton_Click;
            DisbursementTypeComboBox.SelectionChanged += DisbursementTypeComboBox_SelectionChanged;
            DisbursementUnitsTextBox.TextChanged += DisbursementUnitsTextBox_TextChanged;

            // Date picker events
            FromDatePicker.SelectedDateChanged += DateRange_Changed;
            ToDatePicker.SelectedDateChanged += DateRange_Changed;

            // Calendar events
            PreviousMonthButton.Click += PreviousMonthButton_Click;
            NextMonthButton.Click += NextMonthButton_Click;

            ProgressCanvas.SizeChanged += (s, e) => UpdateProgressBar();

            // Team member selection
            TeamMemberComboBox.SelectionChanged += TeamMemberComboBox_SelectionChanged;

            // Window events
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;

            // Set up context menu for time entries
            SetupTimeEntriesContextMenu();
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

        private async void LoadInitialData()
        {
            try
            {
                UpdateStatus("Initializing...");

                // Better connection logic
                bool connected = false;

                // First, try silent connection (use cached tokens)
                UpdateStatus("Connecting to Dataverse...");
                connected = await Task.Run(() => ServiceLocator.Connect(forceReconnect: false, showMessages: false));

                if (!connected)
                {
                    // If silent connection failed, show user what's happening
                    UpdateStatus("Authentication required...");

                    // Try with user interaction allowed
                    connected = await Task.Run(() => ServiceLocator.Connect(forceReconnect: true, showMessages: true));
                }

                if (!connected)
                {
                    UpdateStatus("Failed to connect to Dataverse");
                    UpdateConnectionStatus(false);

                    var result = MessageBox.Show(
                        "Failed to connect to Dataverse. This could be due to:\n\n" +
                        "• Network connectivity issues\n" +
                        "• Expired credentials\n" +
                        "• Authentication service problems\n\n" +
                        "Would you like to try again?",
                        "Connection Error",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Retry with fresh authentication
                        connected = await Task.Run(() => ServiceLocator.ForceReauthentication(showMessages: true));
                    }

                    if (!connected)
                    {
                        UpdateStatus("Connection failed - some features may not be available");
                        // Don't exit the app - let it run in limited mode
                        return;
                    }
                }

                UpdateStatus("Loading data...");

                // Load team members first (with filtering)
                await LoadTeamMembersAsync();
                // Load colleague configuration for current user
                await LoadColleagueConfigurationAsync();
                // Load projects
                await LoadProjectsAsync();
                // Load disbursement types
                await LoadDisbursementTypesAsync();
                // Load recent time entries for current user
                await LoadTimeEntriesAsync();
                // Load recent disbursements for current user
                await LoadDisbursementsAsync();
                // Load calendar data
                await LoadCalendarDataAsync();

                UpdateStatus("Ready");
                UpdateConnectionStatus(true);

                // Show connection success briefly
                if (ServiceLocator.CurrentUserName != "Not connected")
                {
                    UpdateStatus($"Connected as {ServiceLocator.CurrentUserName}");

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
        private void UpdateDailyProgress()
        {
            if (_currentUser == null) return;

            try
            {
                // Get today's expected hours
                var today = DateTime.Today;
                _todayExpectedHours = _currentUserConfig?.GetExpectedHoursForDay(today.DayOfWeek) ?? 8.0m;

                // Calculate today's actual hours
                _todayActualHours = TimeEntries
                    .Where(te => te.Date.Date == today && te.TeamMemberId == _currentUser.Id)
                    .Sum(te => te.TotalHours);

                // Update UI
                Dispatcher.Invoke(() =>
                {
                    UpdateProgressBar();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating daily progress: {ex.Message}");
            }
        }

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

            // Update progress bar width (ensure canvas has a width)
            if (ProgressCanvas.ActualWidth > 0)
            {
                var progressBarWidth = (ProgressCanvas.ActualWidth * progressPercentage) / 100;
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

        private void CreateHourMarkers()
        {
            ProgressCanvas.Children.Clear();

            if (_todayExpectedHours <= 0 || ProgressCanvas.ActualWidth <= 0) return;

            // Add hour markers (1h, 2h, 3h, etc.)
            for (int hour = 1; hour <= _todayExpectedHours; hour++)
            {
                var markerPosition = (ProgressCanvas.ActualWidth * hour) / _todayExpectedHours;

                // Add tick mark
                var tick = new Rectangle
                {
                    Width = 1,
                    Height = 12,
                    Fill = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Margin = new Thickness(markerPosition - 0.5, 4, 0, 0)
                };

                // Add hour label
                var label = new TextBlock
                {
                    Text = $"{hour}h",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Margin = new Thickness(markerPosition - 8, -2, 0, 0)
                };

                ProgressCanvas.Children.Add(tick);
                ProgressCanvas.Children.Add(label);
            }
        }
        #endregion

        #region Time Entry Date Management
        private void TimeEntryDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TimeEntryDatePicker.SelectedDate.HasValue)
            {
                _lastSelectedTimeEntryDate = TimeEntryDatePicker.SelectedDate.Value;
            }
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

            var editDialog = new EditTimeEntryDialog(timeEntry, Projects.ToList(), _currentUser);
            if (editDialog.ShowDialog() == true)
            {
                var updatedEntry = editDialog.UpdatedTimeEntry;

                try
                {
                    UpdateStatus("Updating time entry...");

                    // Update in database
                    await Task.Run(() => _timeEntryService.UpdateTimeEntry(updatedEntry));

                    // Update in UI collection
                    var index = TimeEntries.IndexOf(timeEntry);
                    if (index >= 0)
                    {
                        TimeEntries[index] = updatedEntry;
                        UpdateTimeEntriesSummary();
                    }

                    UpdateStatus("Time entry updated successfully");

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
            UpdateDailyProgress();
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
                $"Project: {timeEntry.ProjectName}\n" +
                $"Hours: {timeEntry.TotalHours:F1}\n" +
                $"Comments: {timeEntry.Comments}",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    UpdateStatus("Deleting time entry...");

                    await Task.Run(() => _timeEntryService.DeleteTimeEntry(timeEntry.Id));

                    TimeEntries.Remove(timeEntry);
                    UpdateTimeEntriesSummary();
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
            UpdateDailyProgress();
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

            // Set the context menu opening event to validate items
            contextMenu.Opened += (s, e) =>
            {
                var hasSelection = TimeEntriesDataGrid.SelectedItem != null;
                var canEdit = hasSelection && TimeEntriesDataGrid.SelectedItem is TimeEntry entry && entry.IsEditable && entry.TeamMemberId == _currentUser?.Id;

                editItem.IsEnabled = canEdit;
                deleteItem.IsEnabled = canEdit;

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
        private void TimeEntriesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TimeEntriesDataGrid.SelectedItem is TimeEntry selectedEntry)
            {
                EditTimeEntry(selectedEntry);
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

                Dispatcher.Invoke(() =>
                {
                    TeamMembers.Clear();

                    // Filter out users with # character and add to collection
                    foreach (var member in teamMembers.Where(tm => !tm.FullName.Contains("#")))
                    {
                        TeamMembers.Add(member);
                    }

                    TeamMemberComboBox.ItemsSource = TeamMembers;

                    // Try to find current user from ServiceLocator
                    if (ServiceLocator.CurrentUserId != Guid.Empty)
                    {
                        _currentUser = TeamMembers.FirstOrDefault(tm => tm.Id == ServiceLocator.CurrentUserId);
                        if (_currentUser != null)
                        {
                            TeamMemberComboBox.SelectedItem = _currentUser;
                        }
                    }

                    // If we couldn't find current user, select first item
                    if (TeamMemberComboBox.SelectedItem == null && TeamMembers.Any())
                    {
                        TeamMemberComboBox.SelectedIndex = 0;
                        _currentUser = TeamMembers[0];
                    }
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading team members: {ex.Message}");
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

                // Filter out 'Standard Disbursement' as it's only used by automation
                var filteredTypes = allDisbursementTypes.Where(t =>
                    !t.Name.Equals("Standard Disbursement", StringComparison.OrdinalIgnoreCase)).ToList();

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

        private async Task LoadTimeEntriesAsync()
        {
            try
            {
                if (_currentUser == null) return;

                var fromDate = FromDatePicker.SelectedDate ?? DateTime.Today;
                var toDate = ToDatePicker.SelectedDate ?? DateTime.Today;

                // Get all time entries and filter by current user and date range
                var allTimeEntries = await Task.Run(() => _timeEntryService.GetTimeEntries());

                var filteredEntries = allTimeEntries
                    .Where(te => te.TeamMemberId == _currentUser.Id)
                    .Where(te => te.Date.Date >= fromDate.Date && te.Date.Date <= toDate.Date)
                    .OrderByDescending(te => te.Date)
                    .ToList();

                Dispatcher.Invoke(() =>
                {
                    TimeEntries.Clear();
                    foreach (var entry in filteredEntries)
                    {
                        TimeEntries.Add(entry);
                    }

                    TimeEntriesDataGrid.ItemsSource = TimeEntries;
                    UpdateTimeEntriesSummary();
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading time entries: {ex.Message}");
            }
            UpdateDailyProgress();
        }

        private async Task LoadDisbursementsAsync()
        {
            try
            {
                // Check if a specific project is selected
                var selectedProject = DisbursementProjectsList.SelectedItem as Project;

                if (selectedProject != null)
                {
                    // Load all disbursements for the selected project (no date filtering)
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
                    // No project selected - show all disbursements for current user
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

        private void UpdateTimeEntriesSummary()
        {
            var totalEntries = TimeEntries.Count;
            var totalHours = TimeEntries.Sum(te => te.TotalHours);

            TotalEntriesLabel.Text = $"Total entries: {totalEntries}";
            TotalHoursLabel.Text = $"Total hours: {totalHours:F1}";
        }
        #endregion

        #region Time Entry Events
        private async void AddTimeEntryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (ProjectsList.SelectedItem == null)
                {
                    MessageBox.Show("Please select a project.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_currentUser == null)
                {
                    MessageBox.Show("Please select a team member.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!decimal.TryParse(HoursTextBox.Text, out decimal hours))
                {
                    hours = 0;
                }

                if (!int.TryParse(MinutesTextBox.Text, out int minutes))
                {
                    minutes = 0;
                }

                if (hours == 0 && minutes == 0)
                {
                    MessageBox.Show("Please enter hours or minutes.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedProject = (Project)ProjectsList.SelectedItem;

                // Create time entry
                var timeEntry = new TimeEntry
                {
                    Date = TimeEntryDatePicker.SelectedDate ?? DateTime.Today,
                    Hours = hours,
                    Minutes = minutes,
                    Comments = CommentsTextBox.Text,
                    ProjectId = selectedProject.Id,
                    TeamMemberId = _currentUser.Id,
                    ProjectName = selectedProject.Name
                };

                UpdateStatus("Adding time entry...");
                var newId = await Task.Run(() => _timeEntryService.CreateTimeEntry(timeEntry));

                if (newId != Guid.Empty)
                {
                    timeEntry.Id = newId;
                    TimeEntries.Insert(0, timeEntry);
                    ClearTimeEntryForm();
                    UpdateStatus("Time entry added successfully");
                    UpdateTimeEntriesSummary();

                    // Refresh calendar if showing current month
                    if (IsCurrentMonth())
                    {
                        await LoadCalendarDataAsync();
                    }
                }
                else
                {
                    UpdateStatus("Failed to add time entry");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error adding time entry: {ex.Message}");
                MessageBox.Show($"Error adding time entry: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            UpdateDailyProgress();
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
            if (ProjectSearchBox.Text == "Search projects...")
            {
                ProjectSearchBox.Text = "";
            }
        }

        private async Task FilterProjects()
        {
            var searchText = ProjectSearchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search projects...")
            {
                // Show recent/cached projects when no search term
                Dispatcher.Invoke(() =>
                {
                    ProjectsList.ItemsSource = Projects.Take(50).ToList(); // Limit to 50 for performance
                    UpdateStatus("Ready");
                });
            }
            else
            {
                try
                {
                    Dispatcher.Invoke(() => UpdateStatus("Searching projects..."));

                    // Use server-side search with the enhanced fuzzy logic
                    var searchResults = await Task.Run(() => _projectService.SearchProjects(searchText));

                    // Update the UI on the main thread
                    Dispatcher.Invoke(() =>
                    {
                        ProjectsList.ItemsSource = searchResults.Take(100).ToList(); // Limit results for performance
                        UpdateStatus($"Found {searchResults.Count} projects");
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
            ProjectSearchBox.Text = "Search projects...";
        }

        private async void RefreshEntriesButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadTimeEntriesAsync();
        }

        private async void DateRange_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Only refresh if both dates are set
            if (FromDatePicker.SelectedDate.HasValue && ToDatePicker.SelectedDate.HasValue)
            {
                await LoadTimeEntriesAsync();
            }
        }
        #endregion

        #region Disbursement Events
        private async void AddDisbursementButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (DisbursementProjectsList.SelectedItem == null)
                {
                    MessageBox.Show("Please select a project from the project search.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (DisbursementTypeComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a disbursement type.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_currentUser == null)
                {
                    MessageBox.Show("Please select a team member.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                decimal amount = 0;
                decimal units = 0;

                var selectedType = (DisbursementType)DisbursementTypeComboBox.SelectedItem;

                if (selectedType.IsUnitBased)
                {
                    if (!decimal.TryParse(DisbursementUnitsTextBox.Text, out units) || units <= 0)
                    {
                        MessageBox.Show("Please enter a valid number of units.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    amount = units * selectedType.UnitCharge;
                }
                else
                {
                    if (!decimal.TryParse(DisbursementAmountTextBox.Text, out amount) || amount <= 0)
                    {
                        MessageBox.Show("Please enter a valid amount.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(DisbursementDescriptionTextBox.Text))
                {
                    MessageBox.Show("Please enter a description.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedProject = (Project)DisbursementProjectsList.SelectedItem;

                // Create disbursement
                var disbursement = new Disbursement
                {
                    Date = DisbursementDatePicker.SelectedDate ?? DateTime.Today,
                    Amount = amount,
                    Units = selectedType.IsUnitBased ? units : 0,
                    UnitCharge = selectedType.UnitCharge,
                    Description = DisbursementDescriptionTextBox.Text,
                    ProjectGuid = selectedProject.Id,
                    ProjectName = selectedProject.Name,
                    TeamMemberGuid = _currentUser.Id,
                    TeamMemberName = _currentUser.FullName,
                    DisbursementTypeId = selectedType.Id,
                    DisbursementTypeName = selectedType.Name,
                    BillableToClient = true
                };

                UpdateStatus("Adding disbursement...");
                var newId = await _disbursementService.AddDisbursementAsync(disbursement);

                if (newId != Guid.Empty)
                {
                    disbursement.IdGuid = newId;
                    Disbursements.Insert(0, disbursement);
                    ClearDisbursementForm();
                    UpdateStatus("Disbursement added successfully");
                    UpdateDisbursementsSummary();
                }
                else
                {
                    UpdateStatus("Failed to add disbursement");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error adding disbursement: {ex.Message}");
                MessageBox.Show($"Error adding disbursement: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisbursementProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterDisbursementProjects();
        }

        private void DisbursementProjectSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DisbursementProjectSearchBox.Text == "Search projects...")
            {
                DisbursementProjectSearchBox.Text = "";
            }
        }

        private void FilterDisbursementProjects()
        {
            var searchText = DisbursementProjectSearchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search projects...")
            {
                DisbursementProjectsList.ItemsSource = Projects;
            }
            else
            {
                var filtered = Projects.Where(p =>
                    (p.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Number?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Client?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();

                DisbursementProjectsList.ItemsSource = filtered;
            }
        }

        private void ClearDisbursementForm()
        {
            DisbursementDatePicker.SelectedDate = DateTime.Today;
            DisbursementAmountTextBox.Clear();
            DisbursementUnitsTextBox.Clear();
            DisbursementDescriptionTextBox.Clear();
            DisbursementTypeComboBox.SelectedItem = null;
            // Note: Don't clear project selection as it affects the view
        }

        private async void RefreshDisbursementsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDisbursementsAsync();
        }

        private async void DisbursementProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update the selected project label and load disbursements
            var selectedProject = DisbursementProjectsList.SelectedItem as Project;
            if (selectedProject != null)
            {
                SelectedProjectLabel.Text = selectedProject.Name;
                SelectedProjectLabel.FontStyle = FontStyles.Normal;
                SelectedProjectLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // TextPrimaryBrush color
            }
            else
            {
                SelectedProjectLabel.Text = "No project selected";
                SelectedProjectLabel.FontStyle = FontStyles.Italic;
                SelectedProjectLabel.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // TextSecondaryBrush color
            }

            // Load disbursements for the selected project (or all if none selected)
            await LoadDisbursementsAsync();
        }

        private async void ClearProjectSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            DisbursementProjectsList.SelectedItem = null;
            DisbursementProjectSearchBox.Text = "Search projects...";
            DisbursementProjectsList.ItemsSource = Projects; // Show all projects
            await LoadDisbursementsAsync(); // This will load all disbursements for current user
        }

        private void UpdateDisbursementsSummary()
        {
            var totalDisbursements = Disbursements.Count;
            var totalAmount = Disbursements.Sum(d => d.Amount);

            TotalDisbursementsLabel.Text = $"Total disbursements: {totalDisbursements}";
            TotalDisbursementAmountLabel.Text = $"Total amount: £{totalAmount:F2}";
        }

        // New disbursement type selection handler
        private void DisbursementTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedType = DisbursementTypeComboBox.SelectedItem as DisbursementType;

            if (selectedType?.IsUnitBased == true)
            {
                // Show unit-based input
                AmountLabel.Visibility = Visibility.Collapsed;
                DisbursementAmountTextBox.Visibility = Visibility.Collapsed;

                UnitsLabel.Text = GetUnitsLabel(selectedType.Name);
                UnitsLabel.Visibility = Visibility.Visible;
                UnitsPanel.Visibility = Visibility.Visible;
                UnitChargeLabel.Text = $"× £{selectedType.UnitCharge:F2}";

                // Clear and calculate
                DisbursementUnitsTextBox.Clear();
                UpdateCalculatedAmount(selectedType);
            }
            else
            {
                // Show amount-based input
                UnitsLabel.Visibility = Visibility.Collapsed;
                UnitsPanel.Visibility = Visibility.Collapsed;

                AmountLabel.Visibility = Visibility.Visible;
                DisbursementAmountTextBox.Visibility = Visibility.Visible;
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
            switch (typeName.ToLower())
            {
                case "mileage":
                    return "Miles";
                case "photocopying (b&w)":
                case "photocopying (colour)":
                    return "Number of Copies";
                default:
                    return "Number of Units";
            }
        }
        #endregion

        #region Calendar Methods
        private void InitializeCalendarStructure()
        {
            // Create day headers (Mon, Tue, Wed, etc.)
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
                    Margin = new Thickness(4)
                };
                CalendarGrid.Children.Add(headerLabel);
            }

            // Create 42 calendar cells (6 weeks * 7 days) - reusable for performance
            for (int i = 0; i < 42; i++)
            {
                var cellBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1),
                    Padding = new Thickness(8),
                    Background = _transparentBrush,
                    Cursor = Cursors.Hand // Add hand cursor to indicate clickable
                };

                var stackPanel = new StackPanel();

                // Day number label
                var dayLabel = new TextBlock
                {
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                };

                // Hours label
                var hoursLabel = new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Visibility = Visibility.Collapsed
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

                CalendarGrid.Children.Add(cellBorder);
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
            // Prevent multiple simultaneous operations
            lock (_calendarLock)
            {
                _calendarCancellationToken?.Cancel();
                _calendarCancellationToken = new CancellationTokenSource();
            }

            var newMonth = _currentCalendarMonth.AddMonths(monthOffset);

            // REMOVED: Future month restriction - users can now navigate to future months

            _currentCalendarMonth = newMonth;
            UpdateCalendarHeader();

            UpdateStatus("Loading calendar...");

            try
            {
                await LoadCalendarDataAsync();
            }
            catch (OperationCanceledException)
            {
                // Cancelled - ignore
            }
        }

        private async Task LoadCalendarDataAsync()
        {
            try
            {
                // Always use the currently connected user from ServiceLocator, not the dropdown
                if (ServiceLocator.CurrentUserId == Guid.Empty) return;

                var cancellationToken = _calendarCancellationToken?.Token ?? CancellationToken.None;

                // Load data asynchronously without blocking UI - use connected user only
                var dailyHours = await Task.Run(() =>
                    _calendarService.GetMonthlyTimeEntries(ServiceLocator.CurrentUserId, _currentCalendarMonth),
                    cancellationToken);

                // Check if cancelled
                cancellationToken.ThrowIfCancellationRequested();

                // Update UI on main thread
                Dispatcher.Invoke(() =>
                {
                    _dailyHours = dailyHours;
                    UpdateCalendarCells();
                    UpdateStatus("Ready");
                });
            }
            catch (OperationCanceledException)
            {
                // Cancelled - don't update status
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateStatus($"Error loading calendar: {ex.Message}");
                });
            }
        }

        private void UpdateCalendarHeader()
        {
            CurrentMonthLabel.Text = _currentCalendarMonth.ToString("MMMM yyyy");
        }

        private void UpdateCalendarCells()
        {
            var firstDayOfMonth = new DateTime(_currentCalendarMonth.Year, _currentCalendarMonth.Month, 1);

            // Fix: Calculate start date properly for Monday start
            // Get the day of week as integer: Sunday=0, Monday=1, Tuesday=2, etc.
            int dayOfWeek = (int)firstDayOfMonth.DayOfWeek;

            // Calculate days to subtract to get to Monday
            // If Sunday (0), subtract 6 days; if Monday (1), subtract 0 days; if Tuesday (2), subtract 1 day, etc.
            int daysToSubtract = (dayOfWeek == 0) ? 6 : dayOfWeek - 1;
            var startDate = firstDayOfMonth.AddDays(-daysToSubtract);

            // Update existing cells with new data and store dates
            for (int i = 0; i < 42; i++)
            {
                var currentDate = startDate.AddDays(i);
                _calendarCellDates[i] = currentDate; // Store the date for this cell
                UpdateSingleCalendarCell(i, currentDate);
            }
        }

        private void UpdateSingleCalendarCell(int cellIndex, DateTime date)
        {
            var cellBorder = _calendarCells[cellIndex];
            var dayLabel = _dayLabels[cellIndex];
            var hoursLabel = _hoursLabels[cellIndex];

            // Update day number
            dayLabel.Text = date.Day.ToString();

            // Reset styling to defaults
            cellBorder.Background = _transparentBrush;
            dayLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            hoursLabel.Visibility = Visibility.Collapsed;

            // Check if this day has hours logged
            if (_dailyHours.TryGetValue(date.Date, out decimal hours))
            {
                // Get expected hours for this day of week
                decimal expectedHours = _currentUserConfig?.GetExpectedHoursForDay(date.DayOfWeek) ?? 8;

                // Calculate percentage only if expected hours > 0
                if (expectedHours > 0)
                {
                    decimal percentage = (hours / expectedHours) * 100;

                    // Colour code based on percentage of expected hours
                    if (percentage < 50)
                    {
                        cellBorder.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226)); // Light red
                    }
                    else if (percentage < 75)
                    {
                        cellBorder.Background = new SolidColorBrush(Color.FromRgb(194, 65, 12)); // Dark orange
                    }
                    else if (percentage < 100)
                    {
                        cellBorder.Background = new SolidColorBrush(Color.FromRgb(255, 237, 213)); // Light orange
                    }
                    else
                    {
                        cellBorder.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231)); // Light green
                    }

                    // Show hours and percentage
                    hoursLabel.Text = expectedHours > 0 ? $"{hours:F1}h ({percentage:F0}%)" : $"{hours:F1}h";
                    hoursLabel.Visibility = Visibility.Visible;
                }
                else
                {
                    // Weekend or non-working day - just show hours without colour coding
                    hoursLabel.Text = $"{hours:F1}h";
                    hoursLabel.Visibility = Visibility.Visible;
                }
            }

            // Different styling for current month vs other months
            if (date.Month != _currentCalendarMonth.Month)
            {
                dayLabel.Foreground = _otherMonthBrush;
            }
            else if (date.Date == DateTime.Today)
            {
                cellBorder.Background = _todayBrush;
                dayLabel.Foreground = Brushes.White;
                hoursLabel.Foreground = Brushes.White;
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

                // Set the time entry date to the clicked date (this will update the sticky date via the event handler)
                TimeEntryDatePicker.SelectedDate = clickedDate;

                // Load time entries for that date
                await LoadTimeEntriesAsync();

                // Show status message
                UpdateStatus($"Showing time entries for {clickedDate:dd/MM/yyyy}");

                // Optional: Scroll to top of time entries if there are any
                if (TimeEntries.Any())
                {
                    TimeEntriesDataGrid.ScrollIntoView(TimeEntries.First());
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error navigating to time entries: {ex.Message}");
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
            if (selectedMember != null)
            {
                _currentUser = selectedMember;
                // Only refresh time entries, NOT calendar (calendar always shows connected user)
                // Only refresh disbursements if no specific project is selected
                await LoadTimeEntriesAsync(); // Refresh time entries for selected user

                // Only refresh disbursements if no project is currently selected
                if (DisbursementProjectsList.SelectedItem == null)
                {
                    await LoadDisbursementsAsync(); // Show all disbursements for selected user
                }
                // Note: Calendar deliberately NOT refreshed - it always shows the connected user's data
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
            if (!_isExitingFromTray)
            {
                e.Cancel = true;
                Hide();
                _notifyIcon.ShowBalloonTip(3000, "DHA Time Management",
                    "Application is still running in the system tray", WinForms.ToolTipIcon.Info);
            }
            else
            {
                _notifyIcon?.Dispose();
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
        private void UpdateStatus(string message)
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}