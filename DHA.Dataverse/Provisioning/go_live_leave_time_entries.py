"""DHA-9 go-live in PROD, once Finance has created the "Approved Leave" Job.

    set DATAVERSE_URL=https://dhapd.crm11.dynamics.com
    python -I go_live_leave_time_entries.py <Approved Leave Job number, e.g. 36901>

1. Finds the Job by its number, checks it's called "Approved Leave", and sets fwp_LeaveProjectId to it.
2. Smoke test that writes nothing: fwp_RecordLeave no longer reports "not set" (it reaches user lookup instead).
3. Switches on "Leave Request - Record Time Entries" and "Leave - Record Bank Holidays".
4. Runs "Leave Request - Backfill Approved Leave" once (approved leave from today onwards) and waits for it.
"""
import json, os, shutil, subprocess, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dv

if len(sys.argv) != 2:
    sys.exit(__doc__)
job_number = sys.argv[1].strip()


def flow_api(env, path, method="GET", body=None):
    tok = dv.az_token("https://service.flow.microsoft.com/")
    url = f"https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/{env}/{path}"
    url += ("&" if "?" in url else "?") + "api-version=2016-11-01"
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body is not None else None, method=method,
                                 headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r:
        txt = r.read()
        return json.loads(txt) if txt else {}


def q(path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300:
        sys.exit(f"{path}: {d}")
    return d["value"]


# 1. the Job
jobs = q(f"msdyn_projects?$select=msdyn_projectid,msdyn_subject,statuscode&$filter=isc_projectnumbernew eq '{job_number}'")
if len(jobs) != 1:
    sys.exit(f"Expected one Job numbered {job_number}, found {len(jobs)}")
job = jobs[0]
if "approved leave" not in job["msdyn_subject"].lower():
    sys.exit(f"Job {job_number} is '{job['msdyn_subject']}', not 'Approved Leave' - stopping")
d = q("environmentvariabledefinitions?$select=environmentvariabledefinitionid&$filter=schemaname eq 'fwp_LeaveProjectId'")[0]
vals = q(f"environmentvariablevalues?$select=environmentvariablevalueid&$filter=_environmentvariabledefinitionid_value eq {d['environmentvariabledefinitionid']}")
body = {"value": job["msdyn_projectid"]}
s = (dv.call("PATCH", f"environmentvariablevalues({vals[0]['environmentvariablevalueid']})", body) if vals else
     dv.call("POST", "environmentvariablevalues", dict(body, schemaname="fwp_LeaveProjectId_value",
             **{"EnvironmentVariableDefinitionId@odata.bind": f"/environmentvariabledefinitions({d['environmentvariabledefinitionid']})"})))[0]
print(f"1. fwp_LeaveProjectId = {job['msdyn_projectid']} ({job_number} {job['msdyn_subject']}) -> {s}")

# 2. smoke test (no writes: an unknown email fails at user lookup, AFTER the Job setting is read)
s, d, _ = dv.call("POST", "fwp_RecordLeave", {"UserEmail": "nobody@example.invalid", "StartDate": "2027-01-04", "SourceKey": "GOLIVE-SMOKE"})
msg = json.loads(d)["error"]["message"] if s >= 300 else str(d)
if "not set" in msg:
    sys.exit(f"2. smoke test failed - setting not picked up: {msg}")
print(f"2. smoke test ok ({msg[:70]}...)")

# 3. flows on
env = dv.call("GET", "RetrieveCurrentOrganization(AccessType=Microsoft.Dynamics.CRM.EndpointAccessType'Default')")[1]["Detail"]["EnvironmentId"]
for name in ("Leave Request - Record Time Entries", "Leave - Record Bank Holidays"):
    w = q(f"workflows?$select=workflowid,statecode&$filter=category eq 5 and name eq '{name}'")[0]
    if w["statecode"] != 1:
        s, d, _ = dv.call("PATCH", f"workflows({w['workflowid']})", {"statecode": 1, "statuscode": 2})
        if s >= 300:
            sys.exit(f"3. could not switch on {name}: {d}")
    print(f"3. {name}: on")

# 4. backfill once
w = q("workflows?$select=workflowid&$filter=category eq 5 and name eq 'Leave Request - Backfill Approved Leave'")[0]["workflowid"]
started = time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime())
flow_api(env, f"flows/{w}/triggers/manual/run", "POST", {})
print("4. backfill started - waiting...")
for _ in range(120):
    runs = [r for r in flow_api(env, f"flows/{w}/runs")["value"] if r["properties"]["startTime"] >= started]
    if runs and runs[0]["properties"]["status"] not in ("Running", "Waiting"):
        print(f"   backfill {runs[0]['properties']['status']}")
        break
    time.sleep(15)
created = q("fwp_timeentries?$select=fwp_timeentryid&$filter=startswith(fwp_leavesourcekey,'LR-') and statecode eq 0")
print(f"   active leave time entries from the leave app: {len(created)}")
