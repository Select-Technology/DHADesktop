# DHA test run - 2026-10-16 10:04 (dev, 144s)

| Card | Pass | Fail | Known gap | Skipped |
|---|---|---|---|---|
| DHA-7 Finance can place a project on stop | 11 | 0 | 0 | 1 |
| DHA-8 Warning when recording time on a stopped project | 7 | 0 | 0 | 0 |
| DHA-9 Approved absence creates the time entry | 70 | 0 | 0 | 1 |
| DHA-10 Cancelling an absence deactivates the time entry | 10 | 0 | 1 | 0 |
| DHA-16 Internal source is mandatory on a quote | 2 | 2 | 0 | 1 |
| .NET unit tests (leave day calculation, leave events) | 1 | 0 | 0 | 0 |

## DHA-7 Finance can place a project on stop

- ✅ `Dha07ProjectOnStop.test_column_is_a_secured_audited_yes_no_defaulting_to_no` - pass
- ✅ `Dha07ProjectOnStop.test_everyone_is_in_the_read_profile_via_the_root_business_unit_team` - pass
- ✅ `Dha07ProjectOnStop.test_finance_profile_can_read_create_and_update` - pass
- ✅ `Dha07ProjectOnStop.test_finance_profile_has_members` - pass
- ⏭️ `Dha07ProjectOnStop.test_finance_user_can_place_and_clear_a_stop` - skip
  > No non-admin finance user in dev has the base roles needed to update a project (Julie Thorne lacks prvReadUser; Dynamics Automation lacks msdyn_actual read). Finance write access is covered by the profile-permission tests above.
- ✅ `Dha07ProjectOnStop.test_on_stop_is_on_both_project_forms` - pass
- ✅ `Dha07ProjectOnStop.test_placing_and_clearing_a_stop_is_recorded_and_audited` - pass
- ✅ `Dha07ProjectOnStop.test_project_auditing_is_on` - pass
- ✅ `Dha07ProjectOnStop.test_project_manager_can_see_the_stop` - pass
- ✅ `Dha07ProjectOnStop.test_project_manager_cannot_place_a_project_on_stop` - pass
- ✅ `Dha07ProjectOnStop.test_project_manager_cannot_take_a_project_off_stop` - pass
- ✅ `Dha07ProjectOnStop.test_read_profile_is_read_only` - pass

## DHA-8 Warning when recording time on a stopped project

- ✅ `Dha08Data.test_desktop_project_list_query_works_for_a_planner` - pass
- ✅ `Dha08Data.test_planner_sees_project_taken_off_stop_as_not_on_stop` - pass
- ✅ `Dha08Data.test_planner_sees_stopped_project_as_on_stop` - pass
- ✅ `Dha08Data.test_saving_time_against_a_stopped_project_is_not_blocked` - pass
- ✅ `Dha08FormScript.test_form_script_cases` - pass
- ✅ `Dha08FormScript.test_script_is_registered_on_the_time_entry_form` - pass
- ✅ `Dha08FormScript.test_web_resource_is_deployed` - pass

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
- ⏭️ `Dha09NewRequests.test_request_booked_on_behalf_is_owned_by_the_employee` - skip
  > Jessica has no DHA-1 in dev, so the request can't be assigned to her (booking kept); in prod everyone holds DHA-1
- ✅ `Dha09ParityStage1.test_additional_approver_defaults_from_configuration_and_is_shared` - pass
- ✅ `Dha09ParityStage1.test_cancellation_request_is_timestamped` - pass
- ✅ `Dha09ParityStage1.test_leave_cannot_span_two_leave_years` - pass
- ✅ `Dha09ParityStage1.test_part_day_is_a_share_of_a_day` - pass
- ✅ `Dha09ParityStage1.test_requests_are_measured_in_days` - pass
- ✅ `Dha09ParityStage1.test_two_absences_still_clash` - pass
- ✅ `Dha09ParityStage1.test_working_from_home_does_not_clash_with_an_appointment` - pass
- ✅ `Dha09ParityStage1.test_working_from_home_is_approved_but_never_on_the_timesheet` - pass
- ✅ `Dha09ParityStage1Schema.test_carry_forward_limit_is_five_days` - pass
- ✅ `Dha09ParityStage1Schema.test_entitlements_are_in_days_with_no_purchased_leave` - pass
- ✅ `Dha09ParityStage1Schema.test_leave_types_carry_behaviour_flags` - pass
- ✅ `Dha09ParityStage2.test_asking_to_cancel_approved_leave_keeps_the_reason_and_the_timesheet` - pass
- ✅ `Dha09ParityStage2.test_pending_leave_cannot_go_to_the_approver_for_cancellation` - pass
- ✅ `Dha09ParityStage2.test_withdrawing_with_a_reason_keeps_the_reason` - pass
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

## DHA-10 Cancelling an absence deactivates the time entry

- ✅ `Dha10DesktopRequests.test_approved_cancellation_deactivates_the_entries_and_keeps_the_request` - pass
- ✅ `Dha10DesktopRequests.test_entries_link_back_to_the_request` - pass
- ✅ `Dha10DesktopRequests.test_hr_cancelling_approved_leave_deactivates_the_entries` - pass
- ✅ `Dha10LeaveApp.test_cancelled_day_plus_real_work_counts_only_the_real_work` - pass
- ✅ `Dha10LeaveApp.test_cancelling_deactivates_and_never_deletes` - pass
- ✅ `Dha10LeaveApp.test_every_entry_is_traceable_to_its_absence` - pass
- ✅ `Dha10LeaveApp.test_only_this_absences_entries_are_touched` - pass
- ✅ `Dha10LeaveApp.test_re_approving_reactivates_the_same_entries_rather_than_duplicating` - pass
- ✅ `Dha10LeaveApp.test_shortening_leave_deactivates_only_the_dropped_day` - pass
- ✅ `Dha10Views.test_default_time_entry_views_show_active_only` - pass
- ⚠️ `Dha10Views.test_reconciliation_view_shows_active_only` - known gap
  > KNOWN GAP (not changed - outside the leave work): this view has no status filter, so deactivated
  > leave entries appear in it. The reconciliation process itself still needs checking too.

## DHA-16 Internal source is mandatory on a quote

- ❌ `Dha16InternalSource.test_every_active_quote_main_form_has_internal_source (form='Project quote information - details')` - FAIL
  > File "C:\Users\thalverson\Code\DHA\DHA.DSTC.WPF\DHA.Dataverse\Tests\test_dha16_internal_source.py", line 26, in test_every_active_quote_main_form_has_internal_source
  > self.assertTrue('datafieldname="isc_internalsource"' in f["formxml"],
  > AssertionError: False is not true : Internal Source is not on the 'Project quote information - details' form - a Business Required field that isn't on a form can't be filled in there
- ✅ `Dha16InternalSource.test_internal_source_is_business_required` - pass
- ✅ `Dha16InternalSource.test_internal_source_is_editable_on_the_forms` - pass
- ❌ `Dha16InternalSource.test_quote_without_internal_source_cannot_be_saved_outside_the_form` - FAIL
  > File "C:\Users\thalverson\Code\DHA\DHA.DSTC.WPF\DHA.Dataverse\Tests\test_dha16_internal_source.py", line 50, in test_quote_without_internal_source_cannot_be_saved_outside_the_form
  > self.assertGreaterEqual(s, 400, "a quote with no internal source was saved through the API")
  > AssertionError: 201 not greater than or equal to 400 : a quote with no internal source was saved through the API
- ⏭️ `Dha16InternalSource.test_report_existing_quotes_without_internal_source` - skip
  > INFO: 21 of 54 open/active quotes in dev have no internal source (card question: what happens to existing quotes?)

## .NET unit tests (leave day calculation, leave events)

- ✅ `54 unit tests` - pass
