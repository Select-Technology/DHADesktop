using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        // Collections for data binding
        private ObservableCollection<TimeEntry> _timeEntries;
        private ObservableCollection<Project> _projects;
        private ObservableCollection<TeamMember> _teamMembers;

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

        // Performance optimization - cached brushes
        private static readonly SolidColorBrush _lightRedBrush = new SolidColorBrush(Color.FromRgb(254, 226, 226));
        private static readonly SolidColorBrush _lightOrangeBrush = new SolidColorBrush(Color.FromRgb(255, 237, 213));
        private static readonly SolidColorBrush _lightGreenBrush = new SolidColorBrush(Color.FromRgb(220, 252, 231));
        private static readonly SolidColorBrush _transparentBrush = Brushes.Transparent;
        private static readonly SolidColorBrush _todayBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly SolidColorBrush _otherMonthBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
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
        #endregion

        #region Constructor and Initialization
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Initialize services using ServiceLocator
            if (!ServiceLocator.Initialize())
            {
                MessageBox.Show($"Failed to initialize services: {ServiceLocator.LastError}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            _dataverseConnector = ServiceLocator.DataverseConnector;
            _timeEntryService = ServiceLocator.TimeEntryService;
            _projectService = ServiceLocator.ProjectService;
            _teamMemberService = ServiceLocator.TeamMemberService;
            _calendarService = ServiceLocator.CalendarService;

            // Initialize collections
            TimeEntries = new ObservableCollection<TimeEntry>();
            Projects = new ObservableCollection<Project>();
            TeamMembers = new ObservableCollection<TeamMember>();
            _dailyHours = new Dictionary<DateTime, decimal>();

            // Set up calendar
            _currentCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

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

            // Calendar events
            PreviousMonthButton.Click += PreviousMonthButton_Click;
            NextMonthButton.Click += NextMonthButton_Click;

            // Team member selection
            TeamMemberComboBox.SelectionChanged += TeamMemberComboBox_SelectionChanged;

            // Window events
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
        }

        private void InitializeSystemTray()
        {
            _notifyIcon = new WinForms.NotifyIcon();

            // Try to load icon from resources - simplified approach
            try
            {
                // Use a simple approach for the icon
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            catch
            {
                // Fallback - this should always work
                _notifyIcon.Icon = System.Drawing.SystemIcons.Information;
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

        /*private async void LoadInitialData()
        {
            try
            {
                UpdateStatus("Loading data...");

                // Load team members first
                await LoadTeamMembersAsync();

                // Load projects
                await LoadProjectsAsync();

                // Load recent time entries
                await LoadTimeEntriesAsync();

                // Load calendar data
                await LoadCalendarDataAsync();

                UpdateStatus("Ready");
                UpdateConnectionStatus(true);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading data: {ex.Message}");
                UpdateConnectionStatus(false);
                MessageBox.Show($"Error initializing application: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }*/

        private async void LoadInitialData()
        {
            await TestDataverseConnection();
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
                    foreach (var member in teamMembers)
                    {
                        TeamMembers.Add(member);
                    }

                    TeamMemberComboBox.ItemsSource = TeamMembers;
                    if (TeamMembers.Any())
                    {
                        TeamMemberComboBox.SelectedIndex = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading team members: {ex.Message}");
            }
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

                    ProjectsList.ItemsSource = Projects;
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading projects: {ex.Message}");
            }
        }

        private async Task LoadTimeEntriesAsync()
        {
            try
            {
                var timeEntries = await Task.Run(() => _timeEntryService.GetTimeEntries());

                Dispatcher.Invoke(() =>
                {
                    TimeEntries.Clear();
                    foreach (var entry in timeEntries.Take(50)) // Show recent 50
                    {
                        TimeEntries.Add(entry);
                    }

                    TimeEntriesDataGrid.ItemsSource = TimeEntries;
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading time entries: {ex.Message}");
            }
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

                if (TeamMemberComboBox.SelectedItem == null)
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
                var selectedTeamMember = (TeamMember)TeamMemberComboBox.SelectedItem;

                // Create time entry
                var timeEntry = new TimeEntry
                {
                    Date = TimeEntryDatePicker.SelectedDate ?? DateTime.Today,
                    Hours = hours,
                    Minutes = minutes,
                    Comments = CommentsTextBox.Text,
                    ProjectId = selectedProject.Id,
                    TeamMemberId = selectedTeamMember.Id,
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
        }

        private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterProjects();
        }

        private void ProjectSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ProjectSearchBox.Text == "Search projects...")
            {
                ProjectSearchBox.Text = "";
            }
        }

        private void FilterProjects()
        {
            var searchText = ProjectSearchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search projects...")
            {
                ProjectsList.ItemsSource = Projects;
            }
            else
            {
                var filtered = Projects.Where(p =>
                    (p.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Number?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Client?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();

                ProjectsList.ItemsSource = filtered;
            }
        }

        private void ClearTimeEntryForm()
        {
            TimeEntryDatePicker.SelectedDate = DateTime.Today;
            HoursTextBox.Clear();
            MinutesTextBox.Clear();
            CommentsTextBox.Clear();
            ProjectsList.SelectedItem = null;
            ProjectSearchBox.Text = "Search projects...";
        }
        #endregion

        #region High-Performance Calendar Implementation
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
                    Background = _transparentBrush
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

            // Prevent future months
            if (newMonth > DateTime.Today)
            {
                MessageBox.Show("Cannot view future months.", "Navigation Restricted",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                var selectedTeamMember = TeamMemberComboBox.SelectedItem as TeamMember;
                if (selectedTeamMember == null) return;

                var cancellationToken = _calendarCancellationToken?.Token ?? CancellationToken.None;

                // Load data asynchronously without blocking UI
                var dailyHours = await Task.Run(() =>
                    _calendarService.GetMonthlyTimeEntries(selectedTeamMember.Id, _currentCalendarMonth),
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
            var startDate = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek + 1); // Start from Monday

            // Update existing cells with new data - super fast!
            for (int i = 0; i < 42; i++)
            {
                var currentDate = startDate.AddDays(i);
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
                // Color code based on hours - use cached brushes for performance
                cellBorder.Background = hours < 3
                    ? _lightRedBrush
                    : hours < 7
                        ? _lightOrangeBrush
                        : _lightGreenBrush;

                // Show hours
                hoursLabel.Text = $"{hours:F1}h";
                hoursLabel.Visibility = Visibility.Visible;
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

        private bool IsCurrentMonth()
        {
            var now = DateTime.Now;
            return _currentCalendarMonth.Year == now.Year && _currentCalendarMonth.Month == now.Month;
        }

        private async void TeamMemberComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TeamMemberComboBox.SelectedItem != null)
            {
                await LoadCalendarDataAsync();
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


        // Add this temporary test method to MainWindow.xaml.cs for debugging

        private async Task TestDataverseConnection()
        {
            try
            {
                UpdateStatus("Testing Dataverse connection...");

                // Step 1: Check settings values
                var clientId = Settings.Default.DataverseClientId;
                var tenantId = Settings.Default.DataverseTenantId;
                var envUrl = Settings.Default.DataverseEnvironmentUrl;

                MessageBox.Show($"Settings Check:\n" +
                               $"ClientId: {clientId}\n" +
                               $"TenantId: {tenantId}\n" +
                               $"Environment: {envUrl}\n\n" +
                               $"Are these correct?",
                               "Settings Verification", MessageBoxButton.OK, MessageBoxImage.Information);

                // Step 2: Test authentication service
                var authService = DataverseAuthService.Instance;

                // Force interactive login
                var connected = await authService.ConnectAsync(true);

                if (connected)
                {
                    UpdateStatus("✅ Authentication successful!");

                    // Step 3: Test actual data access
                    var connector = new DataverseConnector();
                    var result = connector.Connect(forceReconnect: true, showMessages: true);

                    if (result)
                    {
                        UpdateStatus("✅ Full Dataverse connection successful!");

                        // Step 4: Test a simple query
                        try
                        {
                            var entities = connector.RetrieveMultiple("systemuser", new[] { "fullname" });
                            UpdateStatus($"✅ Data query successful! Found {entities.Count} users.");
                        }
                        catch (Exception queryEx)
                        {
                            UpdateStatus($"❌ Data query failed: {queryEx.Message}");
                        }
                    }
                    else
                    {
                        UpdateStatus("❌ Dataverse connector failed");
                    }
                }
                else
                {
                    UpdateStatus("❌ Authentication failed");
                    MessageBox.Show("Authentication failed. This could be:\n\n" +
                                   "1. Wrong Client ID for this environment\n" +
                                   "2. App registration not configured properly\n" +
                                   "3. User doesn't have access to this environment\n" +
                                   "4. Network/firewall blocking connection",
                                   "Authentication Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Connection test failed: {ex.Message}");
                MessageBox.Show($"Detailed Error:\n\n" +
                               $"Message: {ex.Message}\n\n" +
                               $"Type: {ex.GetType().Name}\n\n" +
                               $"Inner Exception: {ex.InnerException?.Message}\n\n" +
                               $"Stack Trace: {ex.StackTrace}",
                               "Connection Error Details", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    ConnectionStatusLabel.Text = "Connected to Dataverse";
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