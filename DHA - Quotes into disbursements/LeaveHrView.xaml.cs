using DHA.DSTC.WPF.Models;
using DHA.DSTC.WPF.Services;
using DHA.DSTC.WPF.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xrm.Sdk;

namespace DHA.DSTC.WPF
{
    /// <summary>
    /// "HR" sub-tab of Leave (parity plan A1, A2), for leave administrators: approver, additional approver, base entitlement
    /// and working week (saved through fwp_SetLeaveSettings, as HR can't write colleague configuration - it holds rates),
    /// and entitlement per leave year, including new starters and the carry-forward limit override.
    /// </summary>
    public partial class LeaveHrView : UserControl
    {
        private static readonly LeavePerson Nobody = new LeavePerson { Id = Guid.Empty, Name = "(none)" };
        private static readonly Regex ValidTime = new Regex(@"^\d\d:\d\d$");

        private LeaveService _leaveService;
        private ApproverScope _scope;
        private List<LeavePerson> _people = new List<LeavePerson>();
        private LeaveSettings _settings;
        private List<LeaveEntitlement> _entitlements = new List<LeaveEntitlement>();
        private LeaveEntitlement _editing;
        private bool _peopleLoaded;
        private readonly Dictionary<string, (TextBox start, TextBox finish, TextBox lunch, TextBlock hours)> _week =
            new Dictionary<string, (TextBox, TextBox, TextBox, TextBlock)>();

        public LeaveHrView()
        {
            InitializeComponent();
            BuildWeekGrid();
            YearEndYearTextBox.Text = DateTime.Today.Year.ToString();
        }

        /// <summary>Set by LeaveTabView each time the Leave tab is shown.</summary>
        public ApproverScope Scope
        {
            get => _scope;
            set
            {
                _scope = value;
                _peopleLoaded = false;
                if (IsVisible) _ = LoadPeopleAsync();
            }
        }

        private LeavePerson SelectedPerson => PeopleList.SelectedItem as LeavePerson;

        private void BuildWeekGrid()
        {
            foreach (var w in new[] { 90.0, 90, 90, 90 })
                WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] headers = { "", "Start", "Finish", "Break", "Hours" };
            for (int c = 0; c < headers.Length; c++)
                Place(new TextBlock { Text = headers[c], FontSize = 11, Margin = new Thickness(0, 0, 8, 2) }, 0, c);

            var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
            for (int r = 0; r < days.Length; r++)
            {
                WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Place(new TextBlock { Text = days[r], VerticalAlignment = VerticalAlignment.Center }, r + 1, 0);
                TextBox Box(int col)
                {
                    var box = new TextBox { Height = 28, Margin = new Thickness(0, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
                    box.TextChanged += (s, e) => UpdateHours();
                    Place(box, r + 1, col);
                    return box;
                }
                var start = Box(1);
                var finish = Box(2);
                var lunch = Box(3);
                var hours = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)TryFindResource("TextSecondaryBrush") };
                Place(hours, r + 1, 4);
                _week[days[r]] = (start, finish, lunch, hours);
            }
        }

        private void Place(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            WeekGrid.Children.Add(element);
        }

        private async void HrView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible && !_peopleLoaded)
                await LoadPeopleAsync();
        }

        private async Task LoadPeopleAsync()
        {
            if (_scope == null || !_scope.IsLeaveAdmin || ServiceLocator.DataverseConnector == null)
                return;
            _leaveService = _leaveService ?? new LeaveService(ServiceLocator.DataverseConnector);
            _peopleLoaded = true;
            try
            {
                _people = await Task.Run(() => _leaveService.GetEveryone());
                PeopleTitleText.Text = $"People ({_people.Count})";
                var approvers = new[] { Nobody }.Concat(_people).ToList();
                ApproverComboBox.ItemsSource = approvers;
                AdditionalApproverComboBox.ItemsSource = approvers;
                ShowPeople();
            }
            catch (Exception ex)
            {
                _peopleLoaded = false;
                MessageBox.Show($"Could not load people: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Search_Changed(object sender, TextChangedEventArgs e) => ShowPeople();

        private void ShowPeople()
        {
            var selected = SelectedPerson?.Id;
            var search = (SearchTextBox.Text ?? "").Trim();
            var rows = _people.Where(p => search.Length == 0 || (p.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            PeopleList.ItemsSource = rows;
            PeopleList.SelectedItem = rows.FirstOrDefault(p => p.Id == selected);
        }

        private async void PeopleList_SelectionChanged(object sender, SelectionChangedEventArgs e) => await LoadPersonAsync();

        private async Task LoadPersonAsync()
        {
            var person = SelectedPerson;
            if (person == null || _leaveService == null)
                return;
            PersonTitleText.Text = person.Name;
            SettingsStatusText.Text = EntStatusText.Text = "";
            YearEndPersonCheckBox.IsEnabled = true;
            YearEndPersonCheckBox.Content = $"Only {person.Name}";
            try
            {
                var result = await Task.Run(() => new
                {
                    Settings = _leaveService.GetLeaveSettings(person.Id),
                    Entitlements = _leaveService.GetEntitlements(person.Id)
                });
                if (SelectedPerson?.Id != person.Id)
                    return;
                ShowSettings(result.Settings);
                _entitlements = result.Entitlements;
                EntitlementGrid.ItemsSource = _entitlements;
                EntitlementPanel.IsEnabled = true;
                EditEntitlement(_entitlements.FirstOrDefault(x => x.Year == DateTime.Today.Year) ?? _entitlements.FirstOrDefault() ?? NewYear());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load {person.Name}'s leave settings: {ex.Message}", "Leave", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- Leave settings (A1) ----------

        private void ShowSettings(LeaveSettings s)
        {
            _settings = s;
            NoConfigText.Visibility = s.HasConfiguration ? Visibility.Collapsed : Visibility.Visible;
            SettingsPanel.IsEnabled = s.HasConfiguration;
            var approvers = (List<LeavePerson>)ApproverComboBox.ItemsSource ?? new List<LeavePerson> { Nobody };
            ApproverComboBox.SelectedItem = approvers.FirstOrDefault(p => p.Id == (s.ApproverId ?? Guid.Empty)) ?? Nobody;
            AdditionalApproverComboBox.SelectedItem = approvers.FirstOrDefault(p => p.Id == (s.AdditionalApproverId ?? Guid.Empty)) ?? Nobody;
            BaseEntitlementTextBox.Text = s.BaseEntitlement?.ToString("0.##") ?? "";

            var unreadable = new List<string>();
            foreach (var day in _week)
            {
                var times = s.Week.FirstOrDefault(w => w.Day == day.Key) ?? new WorkingTimes();
                // "0" was used in Dynamics to mark a day off
                day.Value.start.Text = times.Start == "0" ? "" : times.Start;
                day.Value.finish.Text = times.Finish == "0" ? "" : times.Finish;
                day.Value.lunch.Text = times.Break == "0" ? "" : times.Break;
                var bad = new[] { times.Start, times.Finish, times.Break }.Where(t => !string.IsNullOrEmpty(t) && t != "0" && !ValidTime.IsMatch(t)).ToList();
                if (bad.Count > 0)
                    unreadable.Add($"{day.Key} ({string.Join(", ", bad.Select(b => $"\"{b}\""))})");
            }
            WeekWarningText.Text = unreadable.Count == 0 ? ""
                : "Dynamics can't read some of these times, so it counts no hours on those days: " + string.Join("; ", unreadable)
                  + ". Correct them and save.";
            WeekWarningText.Visibility = unreadable.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateHours();
        }

        private static TimeSpan? ParseTime(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return null;
            return TimeSpan.TryParseExact(text, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t) ? t : (TimeSpan?)TimeSpan.MinValue;
        }

        private void UpdateHours()
        {
            decimal total = 0;
            foreach (var day in _week.Values)
            {
                var start = ParseTime(day.start.Text);
                var finish = ParseTime(day.finish.Text);
                var lunch = ParseTime(day.lunch.Text) ?? TimeSpan.Zero;
                if (start == null && finish == null)
                    day.hours.Text = "day off";
                else if (start == TimeSpan.MinValue || finish == TimeSpan.MinValue || lunch == TimeSpan.MinValue || start == null || finish == null)
                    day.hours.Text = "check the times";
                else
                {
                    var hours = (decimal)(finish.Value - start.Value - lunch).TotalHours;
                    day.hours.Text = hours > 0 ? $"{hours:0.##}h" : "check the times";
                    if (hours > 0) total += hours;
                }
            }
            SettingsStatusText.Text = total > 0 ? $"{total:0.##} hours a week" : "";
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var person = SelectedPerson;
            if (person == null || _settings == null || !_settings.HasConfiguration)
                return;

            decimal? baseDays = null;
            if (!string.IsNullOrWhiteSpace(BaseEntitlementTextBox.Text))
            {
                if (!decimal.TryParse(BaseEntitlementTextBox.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var d))
                {
                    MessageBox.Show("Base entitlement must be a number of days.", "Leave settings", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                baseDays = d;
            }

            var settings = new LeaveSettings
            {
                EmployeeId = person.Id,
                HasConfiguration = true,
                ApproverId = (ApproverComboBox.SelectedItem as LeavePerson)?.Id is Guid a && a != Guid.Empty ? a : (Guid?)null,
                AdditionalApproverId = (AdditionalApproverComboBox.SelectedItem as LeavePerson)?.Id is Guid b && b != Guid.Empty ? b : (Guid?)null,
                BaseEntitlement = baseDays,
                Week = _week.Select(d => new WorkingTimes
                {
                    Day = d.Key, Start = d.Value.start.Text.Trim(), Finish = d.Value.finish.Text.Trim(), Break = d.Value.lunch.Text.Trim()
                }).ToList()
            };

            SaveSettingsButton.IsEnabled = false;
            try
            {
                await Task.Run(() => _leaveService.SaveLeaveSettings(settings));
                var saved = await Task.Run(() => _leaveService.GetLeaveSettings(person.Id));
                ShowSettings(saved);
                SettingsStatusText.Text = "Saved. " + SettingsStatusText.Text;
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                MessageBox.Show(fault.Detail.Message, "Leave settings not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save: {ex.Message}", "Leave settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveSettingsButton.IsEnabled = true;
            }
        }

        // ---------- Entitlements (A2) ----------

        /// <summary>Next year without a row, starting from the base entitlement.</summary>
        private LeaveEntitlement NewYear()
        {
            int year = DateTime.Today.Year;
            while (_entitlements.Any(x => x.Year == year)) year++;
            return new LeaveEntitlement { Year = year, BaseDays = _settings?.BaseEntitlement ?? 0m };
        }

        private void EntitlementGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntitlementGrid.SelectedItem is LeaveEntitlement ent)
                EditEntitlement(ent);
        }

        private void NewYearButton_Click(object sender, RoutedEventArgs e)
        {
            EntitlementGrid.SelectedItem = null;
            EditEntitlement(NewYear());
        }

        private void EditEntitlement(LeaveEntitlement ent)
        {
            _editing = ent;
            EntYearTextBox.Text = ent.Year.ToString();
            EntYearTextBox.IsReadOnly = ent.Id != Guid.Empty; // a saved row stays in its year
            EntBaseTextBox.Text = ent.BaseDays.ToString("0.##");
            EntCarriedTextBox.Text = ent.CarriedForwardDays.ToString("0.##");
            EntAdjustmentTextBox.Text = ent.AdjustmentDays.ToString("0.##");
            EntOverrideTextBox.Text = ent.CarryForwardOverride?.ToString("0.##") ?? "";
            EntStatusText.Text = ent.Id == Guid.Empty ? $"New: {ent.Year}" : $"Editing {ent.Year}";
        }

        private async void SaveEntitlementButton_Click(object sender, RoutedEventArgs e)
        {
            var person = SelectedPerson;
            if (person == null || _editing == null)
                return;

            bool Number(TextBox box, out decimal value) =>
                decimal.TryParse(string.IsNullOrWhiteSpace(box.Text) ? "0" : box.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);
            if (!int.TryParse(EntYearTextBox.Text.Trim(), out var year) || year < 2000 || year > 2100
                || !Number(EntBaseTextBox, out var baseDays) || !Number(EntCarriedTextBox, out var carried)
                || !Number(EntAdjustmentTextBox, out var adjustment))
            {
                MessageBox.Show("Check the year and the numbers of days.", "Entitlement", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            decimal? limit = null;
            if (!string.IsNullOrWhiteSpace(EntOverrideTextBox.Text))
            {
                if (!Number(EntOverrideTextBox, out var l) || l < 0)
                {
                    MessageBox.Show("The carry-forward limit must be a number of days, or empty for the standard limit.", "Entitlement",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                limit = l;
            }

            var ent = new LeaveEntitlement
            {
                Id = _editing.Id, Year = year, BaseDays = baseDays, CarriedForwardDays = carried, AdjustmentDays = adjustment,
                CarryForwardOverride = limit
            };
            SaveEntitlementButton.IsEnabled = false;
            try
            {
                await Task.Run(() => _leaveService.SaveEntitlement(person.Id, ent));
                _entitlements = await Task.Run(() => _leaveService.GetEntitlements(person.Id));
                EntitlementGrid.ItemsSource = _entitlements;
                EditEntitlement(_entitlements.FirstOrDefault(x => x.Id == ent.Id) ?? ent);
                EntStatusText.Text = $"Saved {year}.";
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                MessageBox.Show(fault.Detail.Message, "Entitlement not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save: {ex.Message}", "Entitlement", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveEntitlementButton.IsEnabled = true;
            }
        }

        // ---------- Year end (A3, A4) ----------

        private async void GenerateYearButton_Click(object sender, RoutedEventArgs e) =>
            await RunYearEndAsync("fwp_GenerateLeaveYear", "create {0} entitlements from base entitlement for {1}");

        private async void CarryForwardButton_Click(object sender, RoutedEventArgs e) =>
            await RunYearEndAsync("fwp_CarryForwardLeave", "carry unused days from {2} into {0} for {1}");

        private async Task RunYearEndAsync(string apiName, string what)
        {
            if (_leaveService == null)
                return;
            if (!int.TryParse(YearEndYearTextBox.Text.Trim(), out var year) || year < 2000 || year > DateTime.Today.Year + 1)
            {
                MessageBox.Show($"Enter a year up to {DateTime.Today.Year + 1}.", "Year end", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var person = YearEndPersonCheckBox.IsChecked == true ? SelectedPerson : null;
            var who = person?.Name ?? "everyone";
            if (MessageBox.Show($"This will {string.Format(what, year, who, year - 1)}. Continue?", "Year end",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            GenerateYearButton.IsEnabled = CarryForwardButton.IsEnabled = false;
            try
            {
                var report = await Task.Run(() => _leaveService.RunYearEnd(apiName, year, person?.Id));
                YearEndReportTextBox.Text = report;
                YearEndReportTextBox.Visibility = Visibility.Visible;
                if (SelectedPerson != null)
                    await LoadPersonAsync();
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                MessageBox.Show(fault.Detail.Message, "Year end", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not run the year end: {ex.Message}", "Year end", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateYearButton.IsEnabled = CarryForwardButton.IsEnabled = true;
            }
        }
    }
}
