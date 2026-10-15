# DHA test run - 2026-10-15 14:43 (dev, 82s)

| Card | Pass | Fail | Known gap | Skipped |
|---|---|---|---|---|
| DHA-9 Approved absence creates the time entry | 56 | 0 | 1 | 0 |

## DHA-9 Approved absence creates the time entry

- ✅ `Dha09Emails.test_both_flows_exist_and_are_on` - pass
- ✅ `Dha09Emails.test_dev_redirects_every_email` - pass
- ✅ `Dha09Emails.test_request_and_approval_each_send_the_right_email_to_the_redirect` - pass
- ✅ `Dha09Decisions.test_approving_puts_generic_leave_on_the_timesheet_linked_to_the_request` - pass
- ✅ `Dha09Decisions.test_cancellation_request_keeps_leave_until_decided_and_declining_keeps_it` - pass
- ✅ `Dha09Decisions.test_changing_dates_on_approved_leave_moves_the_timesheet_entries` - pass
- ✅ `Dha09Decisions.test_deleting_approved_or_cancellation_requested_leave_is_blocked` - pass
- ✅ `Dha09Decisions.test_deleting_pending_leave_is_allowed` - pass
- ✅ `Dha09Decisions.test_rejecting_puts_nothing_on_the_timesheet` - pass
- ✅ `Dha09Decisions.test_withdrawing_a_pending_request` - pass
- ✅ `Dha09NewRequests.test_adjacent_requests_are_fine` - pass
- ✅ `Dha09NewRequests.test_approver_defaults_from_colleague_configuration` - pass
- ✅ `Dha09NewRequests.test_bank_holidays_are_not_counted` - pass
- ✅ `Dha09NewRequests.test_end_before_start_is_rejected` - pass
- ✅ `Dha09NewRequests.test_hours_ignore_what_the_client_sends` - pass
- ✅ `Dha09NewRequests.test_new_request_is_pending_named_and_has_server_calculated_hours` - pass
- ✅ `Dha09NewRequests.test_overlap_with_rejected_or_cancelled_leave_is_allowed` - pass
- ✅ `Dha09NewRequests.test_overlapping_requests_are_rejected` - pass
- ✅ `Dha09NewRequests.test_part_day_request_hours` - pass
- ✅ `Dha09NewRequests.test_part_time_colleague_hours` - pass
- ⚠️ `Dha09NewRequests.test_request_booked_on_behalf_is_owned_by_the_employee` - known gap
  > KNOWN GAP: requests are owned by whoever creates them. When HR books for someone, the employee
  > (who only has "own" access through DHA-1) can't see it. The plug-in needs to assign it to the employee.
- ✅ `Dha09Security.test_entitlements_are_user_owned` - pass
- ✅ `Dha09Security.test_hr_team_is_linked_to_the_hr_group_and_holds_leave_administrator` - pass
- ✅ `Dha09Security.test_leave_administrator_sees_everyones_leave_but_cannot_delete` - pass
- ✅ `Dha09Security.test_leave_work_did_not_grant_colleague_configuration_access` - pass
- ✅ `Dha09Security.test_staff_cannot_delete_leave` - pass
- ✅ `Dha09Security.test_staff_get_own_leave_through_dha1` - pass
- ✅ `Dha09PhaseA.test_a_project_manager_cannot_record_leave_for_someone_else` - pass
- ✅ `Dha09PhaseA.test_badly_formatted_time_is_rejected` - pass
- ✅ `Dha09PhaseA.test_bank_holiday_entries_for_everyone_on_their_working_days` - pass
- ✅ `Dha09PhaseA.test_bank_holidays_in_a_booking_are_skipped` - pass
- ✅ `Dha09PhaseA.test_colleague_with_times_but_no_hours_falls_back_to_a_standard_day` - pass
- ✅ `Dha09PhaseA.test_end_before_start_is_rejected` - pass
- ✅ `Dha09PhaseA.test_end_date_defaults_to_start_date` - pass
- ✅ `Dha09PhaseA.test_entries_are_generic_non_chargeable_approved_leave_for_the_right_person_and_job` - pass
- ✅ `Dha09PhaseA.test_full_week_gives_five_working_days_8_5_mon_to_thu_and_8_friday` - pass
- ✅ `Dha09PhaseA.test_half_day_for_a_part_timer_uses_their_day` - pass
- ✅ `Dha09PhaseA.test_half_days_at_each_end_of_a_multi_day_booking` - pass
- ✅ `Dha09PhaseA.test_part_day_11_to_13_is_two_hours` - pass
- ✅ `Dha09PhaseA.test_part_time_pattern_only_books_working_days` - pass
- ✅ `Dha09PhaseA.test_person_can_be_given_by_email` - pass
- ✅ `Dha09PhaseA.test_range_with_no_bank_holidays_creates_nothing` - pass
- ✅ `Dha09PhaseA.test_reconciled_entries_are_never_changed` - pass
- ✅ `Dha09PhaseA.test_removing_an_unknown_key_does_nothing` - pass
- ✅ `Dha09PhaseA.test_removing_twice_is_harmless` - pass
- ✅ `Dha09PhaseA.test_resending_the_same_request_changes_nothing` - pass
- ✅ `Dha09PhaseA.test_source_key_is_required` - pass
- ✅ `Dha09PhaseA.test_unknown_email_is_rejected_clearly` - pass
- ✅ `Dha09PhaseA.test_valto_afternoon_half_day_is_exactly_half_a_day` - pass
- ✅ `Dha09PhaseA.test_valto_half_days_at_both_ends_of_a_booking` - pass
- ✅ `Dha09PhaseA.test_valto_morning_half_day_is_exactly_half_a_day` - pass
- ✅ `Dha09PhaseA.test_weekend_only_booking_creates_nothing` - pass
- ✅ `Dha09PhaseA.test_whole_working_day_window_counts_expected_hours_not_elapsed` - pass
- ✅ `Dha09SharePointFlows.test_flows_exist` - pass
- ✅ `Dha09SharePointFlows.test_request_flow_records_approved_absence_and_withdraws_everything_else` - pass
- ✅ `Dha09SharePointFlows.test_request_flow_watches_the_leave_request_list` - pass
- ✅ `Dha09SharePointFlows.test_working_from_home_is_excluded_by_default` - pass
