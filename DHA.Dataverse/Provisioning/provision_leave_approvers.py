"""DHA-9 phase B: HR leave approvers.

Creates a Dataverse team linked to the HR Microsoft 365 group (HR@dhaplanning.co.uk) and gives it a
"DHA - Leave Administrator" role that can read and action everyone's leave requests and entitlements.
Membership syncs from Entra ID each time a member signs in. The desktop app shows the Approvals screen
to members of this team (and to stsadmin).

Run after provision_leave_management.py. Requires `az login` as a System Administrator. Idempotent.
    python -I provision_leave_approvers.py
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

SOL = "FWPLeaveManagement"
HR_GROUP_ID = "5760dd7d-5244-4e06-b8fa-6ce9284e9d3c"  # HR@dhaplanning.co.uk (Microsoft 365 group)
ROLE_NAME = "DHA - Leave Administrator"


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(d); sys.exit(1)


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300: print(path, d); sys.exit(1)
    return d["value"]


root_bu = q("businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null")[0]["businessunitid"]

# Role
rows = q(f"roles?$select=roleid&$filter=name eq '{ROLE_NAME}' and _businessunitid_value eq {root_bu}")
if rows:
    role = rows[0]["roleid"]
else:
    s, d, _ = dv.call("POST", "roles", {"name": ROLE_NAME, "description": "DHA-9: HR - read and approve everyone's leave, manage entitlements.",
                                        "businessunitid@odata.bind": f"/businessunits({root_bu})"},
                      solution=SOL, extra={"Prefer": "return=representation"})
    ok(s, d, "role " + ROLE_NAME)
    role = d["roleid"]

wanted = [f"prv{p}fwp_leaverequest" for p in ["Create", "Read", "Write", "Append", "AppendTo", "Share", "Assign"]] + \
         [f"prv{p}fwp_leaveentitlement" for p in ["Create", "Read", "Write", "Append", "AppendTo"]] + \
         ["prvReadfwp_leavetype", "prvAppendTofwp_leavetype", "prvReadfwp_publicholiday",
          "prvReadfwp_colleagueconfiguration", "prvReadUser", "prvAppendToUser", "prvReadTeam"]
names = " or ".join(f"name eq '{n}'" for n in wanted)
privs = q(f"privileges?$select=privilegeid,name&$filter={names}")
missing = {w.lower() for w in wanted} - {p["name"].lower() for p in privs}
if missing:
    print("  privileges not found:", missing)
s, d, _ = dv.call("POST", f"roles({role})/Microsoft.Dynamics.CRM.AddPrivilegesRole",
                  {"Privileges": [{"PrivilegeId": p["privilegeid"], "Depth": "Global", "BusinessUnitId": root_bu,
                                   "PrivilegeName": p["name"]} for p in privs]})
ok(s, d, f"  {len(privs)} privileges at organisation level")

# Team linked to the Microsoft 365 group (teamtype 3 = Entra ID Office group, membershiptype 0 = members and guests)
rows = q(f"teams?$select=teamid&$filter=azureactivedirectoryobjectid eq {HR_GROUP_ID}")
if rows:
    team = rows[0]["teamid"]
else:
    me = dv.call("GET", "WhoAmI")[1]["UserId"]
    s, d, _ = dv.call("POST", "teams", {"name": "HR", "teamtype": 3, "membershiptype": 0, "azureactivedirectoryobjectid": HR_GROUP_ID,
                                        "description": "Linked to HR@dhaplanning.co.uk. Leave approvers (DHA-9).",
                                        "businessunitid@odata.bind": f"/businessunits({root_bu})",
                                        "administratorid@odata.bind": f"/systemusers({me})"},
                      extra={"Prefer": "return=representation"})
    ok(s, d, "team HR (Microsoft 365 group)")
    team = d["teamid"]

if not q(f"teams({team})/teamroles_association?$select=roleid&$filter=roleid eq {role}"):
    s, d, _ = dv.call("POST", f"teams({team})/teamroles_association/$ref", {"@odata.id": f"{dv.ORG}/api/data/v9.2/roles({role})"})
    ok(s, d, "  role -> HR team")
print("done")
