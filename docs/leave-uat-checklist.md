# Leave planner: user acceptance testing

**Who:** Harriet Baker (HR) and two line managers. Select Technology supports.
**When:** in the week before the parallel run (stage 9). That's after the live data has come across from Valto and the
emails and calendar have been switched on.
**Where:** the **Leave** tab in DHA Time Management, version 1.4.2 or later.

Work through each section as the person named. Mark each check ✅, ❌ or with a note. Anything ❌ is fixed and checked
again before go-live.

Some tests book real leave. Use dates well in the future, put **"TEST"** in the details, and cancel the requests when
you've finished. Tell staff first if a test will email them or put something on the shared calendar.

---

## 1. Staff: booking my own leave (anyone)

| # | Do this | You should see | ✅/❌ |
|---|---|---|---|
| 1.1 | Open **Leave → My leave** | Your balance for this year: entitlement, taken, booked (including pending) and remaining, in days. Your approver's name. | |
| 1.2 | Compare the balance with the Valto app | The same remaining days | |
| 1.3 | Switch the balance to last year and next year | The right figures, including days carried forward | |
| 1.4 | **New Request**: Holiday Request, two full days next month, details "TEST", **Send for approval** | A summary to confirm; then the request shows as **Pending** and the balance drops by 2 straight away (pending counts) | |
| 1.5 | Book a **Morning** and then an **Afternoon** on a single day | Each counts as 0.5 days | |
| 1.6 | Book **Medical** with a start and end time (e.g. 11:00–12:30) | Counts as part of a day and doesn't come off your holiday balance | |
| 1.7 | Book **Working From Home** for a day | Needs approval, but doesn't affect your balance | |
| 1.8 | Try to book leave that overlaps your test holiday | Refused with "You already have leave booked..." | |
| 1.9 | Try to book 30 Dec to 2 Jan | Refused: book it as two requests, one each side of 1 January | |
| 1.10 | Try to send a request without details | Refused: details are required | |
| 1.11 | Under **My Requests**, try the Upcoming / Past / All dates, status and type filters | The list changes to match. Clicking a request shows its approver comments and any cancellation details | |
| 1.12 | Open **Company holidays** | This year's bank holidays (tick **Show past holidays** for earlier ones) | |
| 1.13 | **Cancel request** on a pending test request | It's withdrawn straight away and your approver is emailed that it was withdrawn | |
| 1.14 | Check your inbox after 1.4 | Nothing yet: the email goes to your approver | |

## 2. Line manager: approving (one of the two line managers)

| # | Do this | You should see | ✅/❌ |
|---|---|---|---|
| 2.1 | Check your inbox after a team member's test request | An email asking you to approve, with dates, type and their comments; your additional approver is copied in | |
| 2.2 | Open **Leave → Approvals** | The tab shows a count, e.g. "Approvals (1)". Only **your** people's requests are listed | |
| 2.3 | Click the request | Their balance before and after, their comments, and **Who's off (next 30 days)** | |
| 2.4 | Switch "Who's off" between **My people** and **Everyone** | For people who aren't yours, the leave type is hidden (shown only for your people and for Working From Home) | |
| 2.5 | **Approve** with a comment | The request leaves the list. The employee gets an "approved" email with your comment | |
| 2.6 | Check the shared Outlook leave calendar | An **"Out of Office - Name"** event for the approved days, with no leave type shown. Working From Home doesn't appear | |
| 2.7 | **Reject** another test request with a comment | The employee gets a "not approved" email with your comment | |
| 2.8 | Employee asks to cancel approved leave (they use **Cancel request** with a reason) | It appears under **Cancellation requests**. Approving it cancels the leave, removes the calendar event and emails them. Declining keeps it booked | |
| 2.9 | Open **My team**, choose someone | Their balance for last, this and next year, and their requests with filters | |
| 2.10 | **Cancel on their behalf**, giving a reason | Cancelled straight away; they're emailed naming you as the person who cancelled | |
| 2.11 | Next weekday after leaving a request waiting | A 10:00 reminder email listing the requests waiting for you | |
| 2.12 | Try to see the balance of someone who isn't yours | Not possible: they don't appear under My team | |

## 3. HR (Harriet)

| # | Do this | You should see | ✅/❌ |
|---|---|---|---|
| 3.1 | Open **Leave → Everyone** and **Approvals** | Everyone's people, and every waiting request | |
| 3.2 | **HR** tab: choose someone; change their approver, additional approver and base entitlement; **Save settings** | Saved. Their waiting requests move to the new approver, who can then see their leave | |
| 3.3 | Change a working week (e.g. make Friday a day off), **Save settings** | Hours per day and the weekly total update. A Friday booked for them afterwards counts as no leave | |
| 3.4 | Set up a **new starter**: base entitlement, then **New year** with their part-year days, **Save entitlement** | Their balance shows the part-year days | |
| 3.5 | Set someone's **Carry-forward limit** for this year (e.g. 8) | Shows instead of "standard" in the entitlement list | |
| 3.6 | **Year end** card: tick **Only [person]**, year = next year, **Create entitlements**, then **Carry forward** | A report listing what was created and carried. The carry is capped at 5, or at their limit. Running it again doesn't add twice | |
| 3.7 | **My leave → Booking for**: book leave for someone else | Booked in their name, against their balance and approver; their approver's email says it was booked on their behalf | |
| 3.8 | Ask someone in the "Leave - Book on behalf" team to do 3.7 | They can. Someone outside the team can't book for others | |
| 3.9 | Spot-check five people's balances against Valto | Remaining days match to the half day | |

## 4. After testing

- [ ] All test requests cancelled. The calendar test events are gone.
- [ ] Anything ❌ fixed and checked again.
- [ ] Email wording agreed. If not, send the changes to Select.
- [ ] **Sign-off:** HR (Harriet Baker), date: ____________

Not covered here: year-end on 1 January (it runs on its own; HR get an email with the results) and the automatic
timesheet entries (DHA-9, switched on separately when Finance have set up the "Approved Leave" Job).
