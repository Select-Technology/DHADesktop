"""DEV ONLY: DHA's real leave request types (as used in the Valto app, Sep-Oct 2026) plus test entitlements.

In prod the types come across from Valto's "Request Type Configuration" list during migration (parity plan §5),
so this script refuses to run anywhere but dev. Replaces the early test types (Annual Leave, Compassionate Leave).
    python -I seed_leave_types_dev.py
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

if "dhapd-dev" not in dv.ORG:
    sys.exit("Dev only - prod leave types come from the Valto migration.")

DAYS, HOURS = 800470000, 800470001
TYPES = [  # name, is absence, deducts from entitlement, booked in, sort
    ("Holiday Request", True, True, DAYS, 1),
    ("Working From Home", False, False, DAYS, 2),
    ("Medical", True, False, HOURS, 3),
    ("Sick Leave", True, False, DAYS, 4),
    ("Study Leave", True, False, DAYS, 5),   # deducts? - confirmed from Valto config at migration
    ("Other", True, False, DAYS, 6),
]


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300:
        sys.exit(f"{path}: {d}")
    return d["value"]


existing = {t["fwp_name"]: t["fwp_leavetypeid"] for t in q("fwp_leavetypes?$select=fwp_name,fwp_leavetypeid")}
for name, absence, deducts, units, order in TYPES:
    body = {"fwp_name": name, "fwp_isabsence": absence, "fwp_deductsfromentitlement": deducts, "fwp_forcedunits": units,
            "fwp_requiresapproval": True, "fwp_sortorder": order}
    if name in existing:
        print(name, "updated", dv.call("PATCH", f"fwp_leavetypes({existing[name]})", body)[0])
    else:
        print(name, "created", dv.call("POST", "fwp_leavetypes", body)[0])
for old in ("Annual Leave", "Compassionate Leave", "TEST Annual Leave"):
    if old in existing:
        # deactivate rather than delete: test requests may still point at it
        print(old, "deactivated", dv.call("PATCH", f"fwp_leavetypes({existing[old]})", {"statecode": 1, "statuscode": 2})[0])

# Test entitlement in days for stsadmin (Select Support): 25 days in 2026 and 2027
me = dv.call("GET", "WhoAmI")[1]["UserId"]
LE = dv.call("GET", "EntityDefinitions(LogicalName='fwp_leaveentitlement')?$select=EntitySetName")[1]["EntitySetName"]
for year in (2026, 2027):
    rows = q(f"{LE}?$select=fwp_leaveentitlementid&$filter=_fwp_employee_value eq {me} and fwp_yearstart eq {year}-01-01")
    body = {"fwp_name": f"Select Support {year}", "fwp_yearstart": f"{year}-01-01", "fwp_entitlementdays": 25,
            "fwp_carriedforwarddays": 2 if year == 2026 else 0, "fwp_Employee@odata.bind": f"/systemusers({me})",
            "ownerid@odata.bind": f"/systemusers({me})"}
    if rows:
        print(f"entitlement {year} updated", dv.call("PATCH", f"{LE}({rows[0]['fwp_leaveentitlementid']})",
                                                    {k: v for k, v in body.items() if not k.startswith(("fwp_Employee", "ownerid"))})[0])
    else:
        print(f"entitlement {year} created", dv.call("POST", LE, body)[0])
