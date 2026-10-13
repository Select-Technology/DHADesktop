"""DHA-9: loads England and Wales bank holidays from GOV.UK into fwp_publicholiday.

Idempotent - only adds dates that aren't already there. Re-run each year (GOV.UK publishes
about two years ahead), or schedule it.
    python -I seed_public_holidays.py [from-year]
"""
import os, sys, json, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

from_year = sys.argv[1] if len(sys.argv) > 1 else "2026"

s, meta, _ = dv.call("GET", "EntityDefinitions(LogicalName='fwp_publicholiday')?$select=EntitySetName")
if s >= 300:
    sys.exit(f"fwp_publicholiday not found - run provision_leave_time_entries.py first. {meta}")
entity_set = meta["EntitySetName"]

with urllib.request.urlopen("https://www.gov.uk/bank-holidays.json") as r:
    events = json.loads(r.read())["england-and-wales"]["events"]

s, d, _ = dv.call("GET", f"{entity_set}?$select=fwp_date")
existing = {row["fwp_date"][:10] for row in d["value"]}

added = 0
for e in events:
    if e["date"] < from_year or e["date"] in existing:
        continue
    s, d, _ = dv.call("POST", entity_set, {"fwp_name": e["title"], "fwp_date": e["date"]})
    if s >= 300:
        sys.exit(f"Failed adding {e['date']}: {d}")
    added += 1
    print("added", e["date"], e["title"])
print(f"{added} added, {len(existing)} already present")
