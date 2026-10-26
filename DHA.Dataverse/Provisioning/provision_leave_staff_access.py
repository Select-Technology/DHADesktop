"""DHA-9 phase B: lets every member of staff book and see their OWN leave in the desktop app.

Adds leave privileges to DHA-1 (Individual) - the base role everyone holds - rather than a new role.
Leave requests and entitlements are at User ("own") level; leave types and public holidays are
reference lists, readable by everyone. Deliberately does NOT touch fwp_colleagueconfiguration: it holds
colleagues' charge rates, and the desktop app's existing access to it is a separate decision. Only adds privileges the role doesn't already have, so it
never lowers an existing access level. Idempotent.

Run after provision_leave_management.py. Requires `az login` as a System Administrator.
    python -I provision_leave_staff_access.py
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

ROLE_PREFIX = "DHA-1"
USER, ORG = "Basic", "Global"  # Dataverse depth names: Basic = own records, Global = organisation

WANTED = {
    "prvCreatefwp_leaverequest": USER,
    "prvReadfwp_leaverequest": USER,
    "prvWritefwp_leaverequest": USER,
    "prvAppendfwp_leaverequest": USER,
    "prvReadfwp_leaveentitlement": USER,
    "prvReadfwp_leavetype": ORG,
    "prvAppendTofwp_leavetype": ORG,
    "prvReadfwp_publicholiday": ORG,
    "prvAppendToUser": ORG,                      # employee/approver lookups on the request
}


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300:
        sys.exit(f"{path}: {d}")
    return d["value"]


# The root copy of DHA-1 - copies in child business units inherit its privileges
roles = [r for r in q(f"roles?$select=name,roleid,_parentrootroleid_value&$filter=startswith(name,'{ROLE_PREFIX}')")
         if r["_parentrootroleid_value"] == r["roleid"]]
if len(roles) != 1:
    sys.exit(f"Expected one root {ROLE_PREFIX} role, found {[r['name'] for r in roles]}")
role = roles[0]
print("role:", role["name"])

s, d, _ = dv.call("GET", f"RetrieveRolePrivilegesRole(RoleId={role['roleid']})")
held = {p["PrivilegeId"] for p in d["RolePrivileges"]}

# Privilege names come back in schema casing (prvReadfwp_LeaveType), so compare case-insensitively
depth = {n.lower(): d for n, d in WANTED.items()}
privs = q("privileges?$select=privilegeid,name&$filter=" + " or ".join(f"name eq '{n}'" for n in WANTED))
missing_defs = set(depth) - {p["name"].lower() for p in privs}
if missing_defs:
    print("  not found (table missing?):", sorted(missing_defs))

to_add = [p for p in privs if p["privilegeid"] not in held]
for p in privs:
    print(f"  {p['name']:<40} {'already held' if p['privilegeid'] in held else 'adding at ' + ('own' if depth[p['name'].lower()] == USER else 'organisation') + ' level'}")

if to_add:
    s, d, _ = dv.call("POST", f"roles({role['roleid']})/Microsoft.Dynamics.CRM.AddPrivilegesRole",
                      {"Privileges": [{"PrivilegeId": p["privilegeid"], "Depth": depth[p["name"].lower()], "PrivilegeName": p["name"]} for p in to_add]})
    if s >= 300:
        sys.exit(f"AddPrivilegesRole failed: {d}")
print(f"done - {len(to_add)} added")
