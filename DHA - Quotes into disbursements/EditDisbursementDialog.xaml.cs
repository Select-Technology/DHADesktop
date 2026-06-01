using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Utilities;

namespace DHA.DSTC.WPF
{
    public partial class EditDisbursementDialog : Window
    {
        private readonly Disbursement _originalDisbursement;
        private readonly List<Project> _projects;
        private readonly List<Quote> _quotes;
        private readonly List<DisbursementType> _disbursementTypes;
        private readonly TeamMember _currentUser;
        private readonly bool _isNewDisbursement;

        // Fix for double window issue - prevent events during close
        private bool _isSaving = false;
        private bool _saveCompleted = false;
        private bool _isClosing = false;

        public Disbursement UpdatedDisbursement { get; private set; }

        // Original constructor for backwards compatibility - editing existing disbursement
        public EditDisbursementDialog(Disbursement disbursement, List<Project> projects, List<DisbursementType> disbursementTypes)
            : this(disbursement, projects, new List<Quote>(), disbursementTypes, null)
        {
        }

        // Enhanced constructor for new disbursement with quote support
        public EditDisbursementDialog(Disbursement disbursement, List<Project> projects, List<Quote> quotes, List<DisbursementType> disbursementTypes, TeamMember currentUser)
        {
            InitializeComponent();

            _originalDisbursement = disbursement;
            _projects = projects;
            _quotes = quotes ?? new List<Quote>();
            _disbursementTypes = disbursementTypes;
            _currentUser = currentUser;
            _isNewDisbursement = disbursement?.IdGuid == Guid.Empty;

            LoadData();
            SetupDisbursementTypeHandling();
        }

        // Static method to create new disbursement dialog
        public static EditDisbursementDialog CreateNewDisbursementDialog(List<Project> projects, List<Quote> quotes, List<DisbursementType> disbursementTypes, TeamMember currentUser)
        {
            var newDisbursement = new Disbursement
            {
                Date = DateTime.Today,
                BillableToClient = true,
                Classification = DisbursementClassification.Project,
                IdGuid = Guid.Empty // Mark as new
            };

            var dialog = new EditDisbursementDialog(newDisbursement, projects, quotes, disbursementTypes, currentUser);
            dialog.TitleLabel.Text = "New Disbursement";
            dialog.ShowClassificationOptions();
            return dialog;
        }

        private void ShowClassificationOptions()
        {
            // Only show classification options if controls exist (new disbursement mode)
            if (ClassificationLabel != null)
                ClassificationLabel.Visibility = Visibility.Visible;
            if (ClassificationPanel != null)
                ClassificationPanel.Visibility = Visibility.Visible;
        }

        private void LoadData()
        {
            try
            {
                // Set up combo boxes with null checks
                if (ProjectComboBox != null)
                    ProjectComboBox.ItemsSource = _projects;
                if (QuoteComboBox != null)
                    QuoteComboBox.ItemsSource = _quotes;
                if (DisbursementTypeComboBox != null)
                    DisbursementTypeComboBox.ItemsSource = _disbursementTypes;

                // Populate form with current values
                if (DatePicker != null)
                    DatePicker.SelectedDate = _originalDisbursement.Date;
                if (AmountTextBox != null)
                    AmountTextBox.Text = _originalDisbursement.Amount.ToString("0.00");
                if (DescriptionTextBox != null)
                    DescriptionTextBox.Text = _originalDisbursement.Description ?? string.Empty;
                if (BillableCheckBox != null)
                    BillableCheckBox.IsChecked = _originalDisbursement.BillableToClient;

                // 🔥 ENHANCED PROJECT SELECTION - Use same strategy as EditTimeEntryDialog
                if (!_isNewDisbursement && _originalDisbursement.Classification == DisbursementClassification.Project)
                {
                    if (ProjectRadio != null)
                        ProjectRadio.IsChecked = true;

                    // Use robust project selection with debugging
                    if (ProjectComboBox != null && _originalDisbursement.ProjectGuid != Guid.Empty)
                    {
                        System.Diagnostics.Debug.WriteLine("=== EditDisbursementDialog Project Selection Debug ===");
                        System.Diagnostics.Debug.WriteLine($"Looking for ProjectGuid: {_originalDisbursement.ProjectGuid}");
                        System.Diagnostics.Debug.WriteLine($"Projects collection count: {_projects?.Count ?? 0}");

                        // Use timer for delayed selection to ensure ComboBox is ready
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(100)
                        };

                        timer.Tick += (s, e) =>
                        {
                            timer.Stop();
                            SelectProjectWithForce(_originalDisbursement.ProjectGuid);
                        };

                        timer.Start();
                    }
                }
                else if (!_isNewDisbursement && _originalDisbursement.Classification == DisbursementClassification.Quote)
                {
                    if (QuoteRadio != null)
                        QuoteRadio.IsChecked = true;

                    // Quote selection (this already works)
                    if (QuoteComboBox != null && _originalDisbursement.QuoteId != Guid.Empty)
                    {
                        var quote = _quotes.FirstOrDefault(q => q.Id == _originalDisbursement.QuoteId);
                        if (quote != null)
                            QuoteComboBox.SelectedItem = quote;
                    }
                }
                else
                {
                    // For new disbursements, default to Project
                    if (ProjectRadio != null)
                        ProjectRadio.IsChecked = true;
                }

                // Select disbursement type
                if (DisbursementTypeComboBox != null && _originalDisbursement.DisbursementTypeId > 0)
                {
                    var disbursementType = _disbursementTypes.FirstOrDefault(dt => dt.Id == _originalDisbursement.DisbursementTypeId);
                    if (disbursementType != null)
                    {
                        DisbursementTypeComboBox.SelectedItem = disbursementType;

                        // Set units if it's a unit-based type
                        if (disbursementType.IsUnitBased && _originalDisbursement.Units > 0)
                        {
                            if (UnitsTextBox != null)
                                UnitsTextBox.Text = _originalDisbursement.Units.ToString("0.00");
                        }
                    }
                }

                // Update classification visibility
                UpdateClassificationVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SelectProjectWithForce(Guid projectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== SelectProjectWithForce {projectId} ===");
                System.Diagnostics.Debug.WriteLine($"Projects collection count: {_projects?.Count ?? 0}");

                // First verify the project exists in our collection
                var targetProject = _projects?.FirstOrDefault(p => p.Id == projectId);
                if (targetProject == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ CRITICAL: Target project not found in collection!");
                    System.Diagnostics.Debug.WriteLine($"Looking for: {projectId}");
                    System.Diagnostics.Debug.WriteLine("Available projects (first 10):");
                    if (_projects != null)
                    {
                        foreach (var p in _projects.Take(10))
                        {
                            System.Diagnostics.Debug.WriteLine($"  - {p.Id}: {p.Name}");
                        }
                    }

                    // 🔥 FIX: Handle missing project gracefully
                    await HandleMissingProject(projectId);
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Target project found: {targetProject.Name}");

                // Force combo box to update its items first
                ProjectComboBox.Items.Refresh();

                // Strategy 1: SelectedValue
                ProjectComboBox.SelectedValue = projectId;
                System.Diagnostics.Debug.WriteLine($"After SelectedValue: SelectedItem = {ProjectComboBox.SelectedItem != null}");

                if (ProjectComboBox.SelectedItem != null)
                {
                    System.Diagnostics.Debug.WriteLine("✅ SelectedValue worked");
                    return;
                }

                // Strategy 2: SelectedItem
                ProjectComboBox.SelectedItem = targetProject;
                System.Diagnostics.Debug.WriteLine($"After SelectedItem: SelectedItem = {ProjectComboBox.SelectedItem != null}");

                if (ProjectComboBox.SelectedItem != null)
                {
                    System.Diagnostics.Debug.WriteLine("✅ SelectedItem worked");
                    return;
                }

                // Strategy 3: SelectedIndex
                var index = _projects.IndexOf(targetProject);
                if (index >= 0)
                {
                    ProjectComboBox.SelectedIndex = index;
                    System.Diagnostics.Debug.WriteLine($"After SelectedIndex {index}: SelectedItem = {ProjectComboBox.SelectedItem != null}");

                    if (ProjectComboBox.SelectedItem != null)
                    {
                        System.Diagnostics.Debug.WriteLine("✅ SelectedIndex worked");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine("❌ ALL SELECTION STRATEGIES FAILED");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SelectProjectWithForce: {ex.Message}");
            }
        }



        private async Task HandleMissingProject(Guid projectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"HandleMissingProject: Attempting to load missing project {projectId}");

                // Option 1: Try to load the specific project from Dataverse
                var projectService = ServiceLocator.ProjectService;
                if (projectService != null)
                {
                    var missingProject = await Task.Run(() => projectService.GetProject(projectId));
                    if (missingProject != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Found missing project: {missingProject.Name}");

                        // Add to the collection
                        _projects.Add(missingProject);

                        // Update ComboBox
                        ProjectComboBox.Items.Refresh();

                        // Try selection again
                        ProjectComboBox.SelectedItem = missingProject;

                        if (ProjectComboBox.SelectedItem != null)
                        {
                            System.Diagnostics.Debug.WriteLine("✅ Successfully selected recovered project");
                            return;
                        }
                    }
                }

                // Option 2: Create a placeholder project so user knows what was selected
                System.Diagnostics.Debug.WriteLine("⚠️ Creating placeholder for missing project");

                var placeholderProject = new Project
                {
                    Id = projectId,
                    Name = $"[Missing Project - {projectId.ToString().Substring(0, 8)}...]",
                    Number = "MISSING",
                    Client = "Unknown Client",
                    IsActive = false
                };

                // Add placeholder to collection
                _projects.Add(placeholderProject);
                ProjectComboBox.Items.Refresh();

                // Select the placeholder
                ProjectComboBox.SelectedItem = placeholderProject;

                System.Diagnostics.Debug.WriteLine("✅ Placeholder project selected");

                // Show user-friendly warning
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(
                        "The project associated with this disbursement could not be found in your current project list. " +
                        "This may be because the project has been archived or deleted.\n\n" +
                        "A placeholder has been created so you can still edit other details of this disbursement.",
                        "Missing Project",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in HandleMissingProject: {ex.Message}");

                // Final fallback - just show a message
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(
                        "The project for this disbursement could not be loaded. Please contact support if this problem persists.",
                        "Project Loading Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }));
            }
        }


        private void SetupDisbursementTypeHandling()
        {
            if (DisbursementTypeComboBox != null)
            {
                DisbursementTypeComboBox.SelectionChanged += DisbursementTypeComboBox_SelectionChanged;
            }

            if (ProjectRadio != null)
                ProjectRadio.Checked += ClassificationRadio_Checked;
            if (QuoteRadio != null)
                QuoteRadio.Checked += ClassificationRadio_Checked;
        }

        private void ClassificationRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isClosing || _isSaving) return; // Prevent events during close
            UpdateClassificationVisibility();
        }

        private void UpdateClassificationVisibility()
        {
            if (ProjectRadio?.IsChecked == true)
            {
                if (ProjectComboBox != null)
                    ProjectComboBox.Visibility = Visibility.Visible;
                if (QuoteComboBox != null)
                    QuoteComboBox.Visibility = Visibility.Collapsed;
            }
            else if (QuoteRadio?.IsChecked == true)
            {
                if (ProjectComboBox != null)
                    ProjectComboBox.Visibility = Visibility.Collapsed;
                if (QuoteComboBox != null)
                    QuoteComboBox.Visibility = Visibility.Visible;
            }
        }

        private void DisbursementTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're closing/saving to prevent validation issues
            if (_isClosing || _isSaving || _saveCompleted)
                return;

            var selectedType = DisbursementTypeComboBox.SelectedItem as DisbursementType;
            if (selectedType != null)
            {
                if (selectedType.IsUnitBased)
                {
                    // Show units panel, hide amount panel
                    if (UnitsLabel != null)
                        UnitsLabel.Visibility = Visibility.Visible;
                    if (UnitsPanel != null)
                        UnitsPanel.Visibility = Visibility.Visible;
                    if (AmountLabel != null)
                        AmountLabel.Visibility = Visibility.Collapsed;
                    if (AmountPanel != null)
                        AmountPanel.Visibility = Visibility.Collapsed;

                    // Update unit charge display
                    if (UnitChargeLabel != null)
                        UnitChargeLabel.Text = $"× £{selectedType.UnitCharge:0.00}";

                    // Set units if editing existing unit-based disbursement
                    if (!_isNewDisbursement && _originalDisbursement.IsUnitBased)
                    {
                        if (UnitsTextBox != null)
                            UnitsTextBox.Text = _originalDisbursement.Units.ToString("0.00");
                    }
                    else
                    {
                        if (UnitsTextBox != null)
                            UnitsTextBox.Text = "";
                        if (CalculatedAmountLabel != null)
                            CalculatedAmountLabel.Text = "= £0.00";
                    }
                }
                else
                {
                    // Show amount panel, hide units panel
                    if (UnitsLabel != null)
                        UnitsLabel.Visibility = Visibility.Collapsed;
                    if (UnitsPanel != null)
                        UnitsPanel.Visibility = Visibility.Collapsed;
                    if (AmountLabel != null)
                        AmountLabel.Visibility = Visibility.Visible;
                    if (AmountPanel != null)
                        AmountPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private void UnitsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Skip calculation if we're closing/saving
            if (_isClosing || _isSaving || _saveCompleted)
                return;

            var selectedType = DisbursementTypeComboBox?.SelectedItem as DisbursementType;
            if (selectedType != null && selectedType.IsUnitBased)
            {
                if (decimal.TryParse(UnitsTextBox.Text, out decimal units))
                {
                    var calculatedAmount = units * selectedType.UnitCharge;
                    if (CalculatedAmountLabel != null)
                        CalculatedAmountLabel.Text = $"= £{calculatedAmount:0.00}";
                }
                else
                {
                    if (CalculatedAmountLabel != null)
                        CalculatedAmountLabel.Text = "= £0.00";
                }
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // EXISTING: Set flag to prevent events during save process
            _isSaving = true;

            if (!ValidateForm())
            {
                _isSaving = false; // Reset flag if validation fails
                return;
            }

            try
            {
                // NEW: Add reconciliation validation BEFORE creating UpdatedDisbursement
                var reconciliationValidation = ReconciliationValidationHelper.ValidateDisbursementModification(
                    _originalDisbursement.IdGuid, ServiceLocator.DataverseConnector);

                if (!reconciliationValidation.IsValid)
                {
                    _isSaving = false; // Reset flag
                    MessageBox.Show(reconciliationValidation.Message, "Cannot Modify Disbursement",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return; // Exit early
                }

                // Show warning if there's a reconciliation concern
                if (reconciliationValidation.Level == ReconciliationValidationLevel.Warning)
                {
                    var result = MessageBox.Show($"{reconciliationValidation.Message}\n\nDo you want to continue?",
                        "Reconciliation Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        _isSaving = false; // Reset flag
                        return; // Exit early
                    }
                }

                // EXISTING: Create updated disbursement (ALL your existing logic preserved)
                var disbursement = new Disbursement
                {
                    IdGuid = _originalDisbursement.IdGuid,
                    Id = _originalDisbursement.Id,
                    Date = DatePicker.SelectedDate.Value,
                    Description = DescriptionTextBox.Text,
                    BillableToClient = BillableCheckBox.IsChecked ?? false,
                    TeamMemberId = _currentUser?.Id.GetHashCode() ?? 0,
                    TeamMemberGuid = _currentUser?.Id ?? Guid.Empty,
                    TeamMemberName = _currentUser?.FullName ?? "Unknown User"
                };

                // EXISTING: Set project or quote based on selection (unchanged)
                if (ProjectRadio?.IsChecked == true && ProjectComboBox.SelectedItem is Project selectedProject)
                {
                    disbursement.ProjectId = selectedProject.Id.GetHashCode();
                    disbursement.ProjectGuid = selectedProject.Id;
                    disbursement.Classification = DisbursementClassification.Project;
                }
                else if (QuoteRadio?.IsChecked == true && QuoteComboBox.SelectedItem is Quote selectedQuote)
                {
                    disbursement.QuoteId = selectedQuote.Id;
                    disbursement.Classification = DisbursementClassification.Quote;
                }

                // EXISTING: Set disbursement type (unchanged)
                if (DisbursementTypeComboBox.SelectedItem is DisbursementType selectedType)
                {
                    disbursement.DisbursementTypeId = selectedType.Id;
                    disbursement.DisbursementTypeGuid = selectedType.IdGuid;
                    disbursement.DisbursementTypeName = selectedType.Name;

                    if (selectedType.IsUnitBased)
                    {
                        if (decimal.TryParse(UnitsTextBox.Text, out decimal units))
                        {
                            disbursement.Units = units;
                            disbursement.UnitCharge = selectedType.UnitCharge;
                            disbursement.Amount = units * selectedType.UnitCharge;
                        }
                    }
                    else
                    {
                        if (decimal.TryParse(AmountTextBox.Text, out decimal amount))
                        {
                            disbursement.Amount = amount;
                            disbursement.Units = 0;
                            disbursement.UnitCharge = 0;
                        }
                    }
                }

                // EXISTING: Set UpdatedDisbursement and flags (unchanged)
                UpdatedDisbursement = disbursement;
                _saveCompleted = true;
                _isClosing = true; // Prevent any further events

                // EXISTING: Small delay to prevent race condition (unchanged)
                await Task.Delay(100);

                // EXISTING: Close dialog with success (unchanged)
                DialogResult = true;
            }
            catch (Exception ex)
            {
                _isSaving = false; // Reset flag on error
                MessageBox.Show($"Error saving disbursement: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Set flags to prevent validation on cancel
            _isSaving = true;
            _saveCompleted = true;
            _isClosing = true;

            DialogResult = false;
        }

        private bool ValidateForm()
        {
            // Skip validation if we're in the save process or completed
            if (_isClosing || _isSaving || _saveCompleted)
            {
                return true;
            }

            // Validate date
            if (!DatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Please select a date.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DatePicker.Focus();
                return false;
            }

            // Validate future dates
            if (DatePicker.SelectedDate.Value.Date > DateTime.Today)
            {
                MessageBox.Show("Cannot enter disbursements for future dates.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DatePicker.Focus();
                return false;
            }

            // Validate project or quote selection
            if (ProjectRadio?.IsChecked == true)
            {
                if (ProjectComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a project.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProjectComboBox.Focus();
                    return false;
                }
            }
            else if (QuoteRadio?.IsChecked == true)
            {
                if (QuoteComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a quote.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    QuoteComboBox.Focus();
                    return false;
                }
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
                if (!decimal.TryParse(UnitsTextBox.Text, out decimal units) || units <= 0)
                {
                    MessageBox.Show("Please enter a valid number of units greater than 0.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    UnitsTextBox.Focus();
                    return false;
                }
            }
            else
            {
                if (!decimal.TryParse(AmountTextBox.Text, out decimal amount) || amount <= 0)
                {
                    MessageBox.Show("Please enter a valid amount greater than 0.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    AmountTextBox.Focus();
                    return false;
                }
            }

            // Validate description
            if (string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
            {
                MessageBox.Show("Please enter a description.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DescriptionTextBox.Focus();
                return false;
            }

            return true;
        }

        // Override OnClosing to prevent validation issues during close
        protected override void OnClosing(CancelEventArgs e)
        {
            // Set flags to prevent any validation during close
            _isClosing = true;
            _isSaving = true;
            _saveCompleted = true;

            base.OnClosing(e);
        }
    }
}