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

        // Performance optimisation - cached brushes
        private static readonly SolidColorBrush _lightRedBrush = new SolidColorBrush(Color.FromRgb(254, 226, 226));
        private static readonly SolidColorBrush _lightOrangeBrush = new SolidColorBrush(Color.FromRgb(255, 237, 213));
        private static readonly SolidColorBrush _lightGreenBrush = new SolidColorBrush(Color.FromRgb(220, 252, 231));
        private static readonly SolidColorBrush _transparentBrush = Brushes.Transparent;
        private static readonly SolidColorBrush _todayBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly SolidColorBrush _otherMonthBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));

        // Current user tracking
        private TeamMember _currentUser;

        // Project search timer
        private System.Threading.Timer _searchTimer;
        private const int SearchDelayMs = 300; // 300ms delay after typing stops
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

                if (!decimal.TryParse(DisbursementAmountTextBox.Text, out decimal amount) || amount <= 0)
                {
                    MessageBox.Show("Please enter a valid amount.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(DisbursementDescriptionTextBox.Text))
                {
                    MessageBox.Show("Please enter a description.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedProject = (Project)DisbursementProjectsList.SelectedItem;
                var selectedType = (DisbursementType)DisbursementTypeComboBox.SelectedItem;

                // Create disbursement
                var disbursement = new Disbursement
                {
                    Date = DisbursementDatePicker.SelectedDate ?? DateTime.Today,
                    Amount = amount,
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
        #endregion

        #region High-Performance Calendar Implementation
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

            // Disbursement events
            AddDisbursementButton.Click += AddDisbursementButton_Click;
            DisbursementProjectSearchBox.TextChanged += DisbursementProjectSearchBox_TextChanged;
            DisbursementProjectSearchBox.GotFocus += DisbursementProjectSearchBox_GotFocus;
            DisbursementProjectsList.SelectionChanged += DisbursementProjectsList_SelectionChanged;
            ClearProjectSelectionButton.Click += ClearProjectSelectionButton_Click;
            RefreshDisbursementsButton.Click += RefreshDisbursementsButton_Click;

            // Date picker events
            FromDatePicker.SelectedDateChanged += DateRange_Changed;
            ToDatePicker.SelectedDateChanged += DateRange_Changed;

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

        // ✅ IMPROVED: Better authentication flow in LoadInitialData
        private async void LoadInitialData()
        {
            try
            {
                UpdateStatus("Initializing...");

                // ✅ IMPROVED: Better connection logic
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

                    // ✅ IMPROVED: Better error message with retry option
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

                // ✅ NEW: Show connection success briefly
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
            TimeEntryDatePicker.SelectedDate = DateTime.Today;
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

            // Debug output to verify the calculation
            System.Diagnostics.Debug.WriteLine($"First day of month: {firstDayOfMonth:yyyy-MM-dd} ({firstDayOfMonth.DayOfWeek})");
            System.Diagnostics.Debug.WriteLine($"Calendar start date: {startDate:yyyy-MM-dd} ({startDate.DayOfWeek})");

            // Update existing cells with new data
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
                // Colour code based on hours - use cached brushes for performance
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