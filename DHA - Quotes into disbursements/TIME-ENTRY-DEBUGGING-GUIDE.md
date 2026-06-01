# Time Entry Debugging Guide

## Overview

Comprehensive debugging has been implemented for all time entry operations to track what values are being sent to Dataverse and what's actually stored.

## What's Been Added

### 1. Enhanced ToEntity() Method ([Models\TimeEntry.cs:524-620](Models\TimeEntry.cs#L524-L620))

Every time a TimeEntry is converted to a Dataverse entity, detailed debug output shows:

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║ TIME ENTRY → DATAVERSE ENTITY CONVERSION DEBUG                                ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║ Entity ID: [GUID or NEW ENTRY]
║ fwp_date: [date with timezone info]
║ fwp_notes: [comments]
║ fwp_minutes: [minutes value]
║ fwp_durationhours: [whole hours]
║ fwp_decimalhours: [decimal hours e.g., 1.5000]
║ Display: [formatted time like "1h 30m"]
║ fwp_category: [category ID and name]
║ fwp_classification: [classification ID and name]
║ fwp_project: [project GUID and name] OR fwp_quote: [quote GUID and name]
║ fwp_teammember: [team member GUID]
║ fwp_value: NOT SET (correct - this is a disbursement field) ✅
╠═══════════════════════════════════════════════════════════════════════════════╣
║ SUMMARY: [time] on [date] for [project/quote name]
╚═══════════════════════════════════════════════════════════════════════════════╝
```

**Key Fields Logged:**
- `fwp_date` - with DateTime.Kind information
- `fwp_notes` - comments/description
- `fwp_minutes` - minutes component
- `fwp_durationhours` - whole hours only (e.g., 1 for "1h 30m")
- `fwp_decimalhours` - total as decimal (e.g., 1.5 for "1h 30m")
- `fwp_category` - option set value and display name
- `fwp_classification` - Project or Quote
- `fwp_project` or `fwp_quote` - entity reference
- `fwp_teammember` - user reference
- `fwp_value` - **specifically checks if this is set (it shouldn't be for time entries)**

### 2. Create Operation Debugging ([Services\TimeEntryService.cs:581-627](Services\TimeEntryService.cs#L581-L627))

When creating a new time entry:

```
═══════════════════════════════════════════════════════════════════════════════
▶ CREATE TIME ENTRY OPERATION STARTING
═══════════════════════════════════════════════════════════════════════════════
✅ Connected to Dataverse
Converting TimeEntry to Dataverse Entity...
[ToEntity debug output appears here]
Calling Dataverse Create operation...

✅ TIME ENTRY CREATED SUCCESSFULLY
   New ID: [new GUID]
═══════════════════════════════════════════════════════════════════════════════

[Verification output appears here - see below]
```

### 3. Update Operation Debugging ([Services\TimeEntryService.cs:629-694](Services\TimeEntryService.cs#L629-L694))

When updating an existing time entry:

```
═══════════════════════════════════════════════════════════════════════════════
▶ UPDATE TIME ENTRY OPERATION STARTING
   Entry ID: [existing GUID]
═══════════════════════════════════════════════════════════════════════════════
✅ Reconciliation validation passed
✅ Connected to Dataverse
Converting TimeEntry to Dataverse Entity...
[ToEntity debug output appears here]
Calling Dataverse Update operation...

✅ TIME ENTRY UPDATED SUCCESSFULLY
   Entry ID: [GUID]
═══════════════════════════════════════════════════════════════════════════════

[Verification output appears here - see below]
```

### 4. Dataverse Verification ([Services\TimeEntryService.cs:696-792](Services\TimeEntryService.cs#L696-L792))

**Most Important:** After every Create or Update, the system retrieves the record from Dataverse to show what's **actually stored**:

```
╔═══════════════════════════════════════════════════════════════════════════════╗
║ DATAVERSE VERIFICATION - AFTER CREATE/UPDATE
╠═══════════════════════════════════════════════════════════════════════════════╣
║ Entry ID: [GUID]
║
║ STORED VALUES IN DATAVERSE:
║ ─────────────────────────────────────────────────────────────────────────────
║ createdon: [timestamp]
║ fwp_category: OptionSet: 1
║ fwp_classification: OptionSet: 800470000
║ fwp_date: [stored date with Kind]
║ fwp_decimalhours: [actual decimal value stored]
║ fwp_durationhours: [actual hours value stored]
║ fwp_minutes: [actual minutes stored]
║ fwp_notes: [actual comments stored]
║ fwp_project: EntityRef: msdyn_project - [GUID] ([name])
║ fwp_teammember: EntityRef: systemuser - [GUID] ([name])
║ fwp_value: [if present - SHOULD NOT BE!]
║ ... [all other fields]
║
║ KEY FIELD SUMMARY:
║ ─────────────────────────────────────────────────────────────────────────────
║ ⏱️  fwp_decimalhours: [value]
║ ⏱️  fwp_durationhours: [value]
║ ⏱️  fwp_minutes: [value]
║ 💰 fwp_value: NOT SET ✅ (correct for time entry)
╚═══════════════════════════════════════════════════════════════════════════════╝
```

This shows **EVERY field** stored in Dataverse, allowing you to:
- Compare what you sent vs. what was stored
- Detect if unexpected fields are being set (like `fwp_value`)
- See if Dataverse is calculating/modifying any values
- Verify entity references are correct

## How to Use This Debugging

### 1. Create a New Time Entry
Simply create a time entry through the UI. Check the debug output window for the complete flow.

### 2. Edit an Existing Time Entry
Edit any time entry. The debug output will show:
- Before: What you're sending
- After: What Dataverse actually stored

### 3. Look for These Key Patterns

**✅ GOOD - Time entry without fwp_value:**
```
║ 💰 fwp_value: NOT SET ✅ (correct for time entry)
```

**⚠️ WARNING - If fwp_value appears:**
```
║ 💰 fwp_value: $123.45 ⚠️ UNEXPECTED FOR TIME ENTRY!
```

**Check hour calculations:**
```
║ fwp_durationhours: 1 (whole hours component)
║ fwp_decimalhours: 1.5000 (total as decimal)
║ fwp_minutes: 30
```
These should be consistent: 1 hour + 30 minutes = 1.5 decimal hours

### 4. Debugging Changes

If you edit a time entry from "2h 30m" to "3h 15m", you'll see:

1. **UPDATE OPERATION** debug showing the conversion
2. **DATAVERSE VERIFICATION** showing the stored values
3. Compare the before/after to see what changed

### 5. Finding Issues

If `fwp_value` is being set or calculated:
- Look for workflow rules in Dataverse
- Check for calculated fields
- Look for plugins/automations that might be setting it

## Where to View Debug Output

In Visual Studio:
1. **View → Output** (or Ctrl+Alt+O)
2. Select **Debug** from the "Show output from:" dropdown
3. Run your application and perform time entry operations

## What This Debugging Reveals

### Question: "Is fwp_value updated when a time entry is edited?"

**Answer from the code:** No, the time entry code does NOT set `fwp_value`. The debugging will show:
- In `ToEntity()`: Whether `fwp_value` is being set (it shouldn't be)
- In verification: Whether `fwp_value` exists in Dataverse after save

If `fwp_value` IS present after verification, it means:
- Dataverse has a calculated field
- A workflow/plugin is setting it
- Or there's an unexpected customization

### All Fields Tracked

The debugging logs **every single field** that gets sent and stored:
- Time fields (`fwp_decimalhours`, `fwp_durationhours`, `fwp_minutes`)
- Date fields (`fwp_date` with timezone info)
- References (`fwp_project`, `fwp_quote`, `fwp_teammember`)
- Categories and classifications
- Comments
- **Any unexpected fields like `fwp_value`**

## Example Debug Session

When you create a 2h 30m time entry, you'll see:

```
═══════════════════════════════════════════════════════════════════════════════
▶ CREATE TIME ENTRY OPERATION STARTING
═══════════════════════════════════════════════════════════════════════════════

╔═══════════════════════════════════════════════════════════════════════════════╗
║ TIME ENTRY → DATAVERSE ENTITY CONVERSION DEBUG                                ║
╠═══════════════════════════════════════════════════════════════════════════════╣
║ Entity ID: NEW ENTRY (will be generated)
║ fwp_date: 2025-12-19 00:00:00 (Kind: Utc)
║ fwp_notes: "Working on project documentation"
║ fwp_minutes: 30
║ fwp_durationhours: 2 (whole hours component)
║ fwp_decimalhours: 2.5000 (total as decimal)
║ Display: 2h 30m
║ fwp_category: 1 (Chargeable)
║ fwp_classification: 800470000 (Project)
║ fwp_project: abc123...
║   → P-001 - Sample Project
║ fwp_quote: NULL (cleared for project classification)
║ fwp_teammember: def456...
║ fwp_value: NOT SET (correct - this is a disbursement field)
╠═══════════════════════════════════════════════════════════════════════════════╣
║ SUMMARY: 2h 30m on 2025-12-19 for P-001 - Sample Project
╚═══════════════════════════════════════════════════════════════════════════════╝

✅ TIME ENTRY CREATED SUCCESSFULLY
   New ID: xyz789...

╔═══════════════════════════════════════════════════════════════════════════════╗
║ DATAVERSE VERIFICATION - AFTER CREATE
╠═══════════════════════════════════════════════════════════════════════════════╣
║ STORED VALUES IN DATAVERSE:
║ fwp_decimalhours: 2.5
║ fwp_durationhours: 2
║ fwp_minutes: 30
║ 💰 fwp_value: NOT SET ✅ (correct for time entry)
╚═══════════════════════════════════════════════════════════════════════════════╝
```

This shows both what was sent AND what was actually stored, making it easy to spot discrepancies.
