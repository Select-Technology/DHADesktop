"""Fee extensions: registers DHA.ProjectFees.Plugins in the FWPProjectFees solution.

The September 2026 fee adjustment went to prod without its plug-in (Select Support had registered an unmanaged
"DhaProjectFeesPlugin" assembly in dev only, outside any solution), so fee extensions never updated the project.
That dev assembly also had its original-fee step registered on the wrong message. This replaces it with source-
controlled code and proper registration:

Rule kept by both plug-ins: DHA Fee = original fee + active fee extensions.

  ProjectFeePlugin            msdyn_project  Create (pre)  original fee := DHA Fee
                              msdyn_project  Update (pre)  isc_projectamount + PreImage; original follows DHA Fee until
                                                           the first extension; then only System Administrators may
                                                           change it (original fee := DHA Fee - extensions)
  FeeExtensionAppliedPlugin   fwp_feeextension Create / Update (fwp_amount, statecode, fwp_project) / Delete (post)
                                                           recalculates the project(s) from the active extensions

Steps (build DHA.ProjectFees.Plugins in Release first):
  dev      register/update in DEV and retire the old DhaProjectFeesPlugin steps there
  package  export FWPProjectFees managed from DEV to DHA.Dataverse/Solutions
  backfill PROD, before import: snapshot every project's fee fields to DHA.Dataverse/Backups, then original fee :=
           DHA Fee where blank and apply the fee extensions already logged.  --only <project number> for a pilot
  import   import the managed package into PROD
  restore  put a backfill snapshot back (uninstall FWPProjectFees first, or the guard blocks DHA Fee changes)

    python -I provision_project_fees.py dev|package|backfill|import [--dry-run] [--only 37133]
    python -I provision_project_fees.py restore <snapshot.json> [--dry-run]
"""
import base64, datetime, json, os, sys, uuid
import importlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
DEV, PROD = "https://dhapd-dev.crm11.dynamics.com", "https://dhapd.crm11.dynamics.com"
SOL, SOL_NAME, VERSION = "FWPProjectFees", "FWP Project Fees", "1.0.0.0"
PUBLISHER = "11f741bf-e290-4fc6-8729-94230f9b6c7a"  # SelectTechnology (fwp)
ASSEMBLY = "DHA.ProjectFees.Plugins"
DLL = os.path.join(HERE, "..", "..", ASSEMBLY, "bin", "Release", ASSEMBLY + ".dll")
PACKAGE = os.path.join(HERE, "..", "Solutions", f"{SOL}_{VERSION.replace('.', '_')}_managed.zip")
OLD_ASSEMBLY = "DhaProjectFeesPlugin"
BACKUPS = os.path.join(HERE, "..", "Backups")
FEE_FIELDS = ["isc_projectamount", "fwp_originalprojectfee", "isc_feeextension", "fwp_lastfeeextensiondate", "fwp_previousfeeextensiondate"]

# (plug-in class, message, entity, stage, filtering attributes, pre-image attributes)
STEPS = [
    ("ProjectFeePlugin", "Create", "msdyn_project", 20, None, None),
    ("ProjectFeePlugin", "Update", "msdyn_project", 20, "isc_projectamount", "isc_projectamount"),
    ("FeeExtensionAppliedPlugin", "Create", "fwp_feeextension", 40, None, None),
    ("FeeExtensionAppliedPlugin", "Update", "fwp_feeextension", 40, "fwp_amount,statecode,fwp_project", "fwp_project"),
    ("FeeExtensionAppliedPlugin", "Delete", "fwp_feeextension", 40, None, "fwp_project"),
]


def env(url):
    os.environ["DATAVERSE_URL"] = url
    import dv
    importlib.reload(dv)
    return dv


def ok(s, d, what):
    print(what, s)
    if s >= 300:
        print(str(d)[:1500]); sys.exit(1)


def q(dv, path):
    s, d, _ = dv.call("GET", path.replace(" ", "%20"))
    if s >= 300:
        print(path, d); sys.exit(1)
    return d["value"]


def add_component(dv, objid, ctype):
    ok(*dv.call("POST", "AddSolutionComponent", {"ComponentId": objid, "ComponentType": ctype, "SolutionUniqueName": SOL,
                                                "AddRequiredComponents": False, "DoNotIncludeSubcomponents": False})[:2],
       f"  add component {ctype}")


def register(dv):
    if not q(dv, f"solutions?$select=solutionid&$filter=uniquename eq '{SOL}'"):
        ok(*dv.call("POST", "solutions", {"uniquename": SOL, "friendlyname": SOL_NAME, "version": VERSION,
                                         "publisherid@odata.bind": f"/publishers({PUBLISHER})"})[:2], "create solution")

    content = base64.b64encode(open(DLL, "rb").read()).decode()
    rows = q(dv, f"pluginassemblies?$select=pluginassemblyid&$filter=name eq '{ASSEMBLY}'")
    if rows:
        asm = rows[0]["pluginassemblyid"]
        ok(*dv.call("PATCH", f"pluginassemblies({asm})", {"content": content})[:2], "update plugin assembly")
    else:
        s, d, _ = dv.call("POST", "pluginassemblies", {"name": ASSEMBLY, "content": content, "isolationmode": 2, "sourcetype": 0,
                                                       "culture": "neutral", "version": "1.0.0.0"},
                          solution=SOL, extra={"Prefer": "return=representation"})
        ok(s, d, "register plugin assembly")
        asm = d["pluginassemblyid"]
    add_component(dv, asm, 91)

    types = {}
    for cls in sorted({s[0] for s in STEPS}):
        tn = f"{ASSEMBLY}.{cls}"
        rows = q(dv, f"plugintypes?$select=plugintypeid&$filter=typename eq '{tn}'")
        if rows:
            types[cls] = rows[0]["plugintypeid"]
            continue
        s, d, _ = dv.call("POST", "plugintypes", {"typename": tn, "name": tn, "friendlyname": cls,
                                                  "pluginassemblyid@odata.bind": f"/pluginassemblies({asm})"},
                          solution=SOL, extra={"Prefer": "return=representation"})
        ok(s, d, "plugin type " + cls)
        types[cls] = d["plugintypeid"]

    for cls, message, entity, stage, filtering, image in STEPS:
        name = f"{ASSEMBLY}.{cls}: {message} of {entity}"
        if q(dv, f"sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid&$filter=name eq '{name}'"):
            print("step exists:", name)
            continue
        msg = q(dv, f"sdkmessages?$select=sdkmessageid&$filter=name eq '{message}'")[0]["sdkmessageid"]
        flt = q(dv, f"sdkmessagefilters?$select=sdkmessagefilterid&$filter=primaryobjecttypecode eq '{entity}' "
                    f"and _sdkmessageid_value eq {msg}")[0]["sdkmessagefilterid"]
        body = {"name": name, "stage": stage, "mode": 0, "rank": 1, "supporteddeployment": 0,
                "eventhandler_plugintype@odata.bind": f"/plugintypes({types[cls]})",
                "sdkmessageid@odata.bind": f"/sdkmessages({msg})", "sdkmessagefilterid@odata.bind": f"/sdkmessagefilters({flt})"}
        if filtering:
            body["filteringattributes"] = filtering
        s, d, _ = dv.call("POST", "sdkmessageprocessingsteps", body, solution=SOL, extra={"Prefer": "return=representation"})
        ok(s, d, "step " + name)
        if image:
            ok(*dv.call("POST", "sdkmessageprocessingstepimages", {
                "name": "PreImage", "entityalias": "PreImage", "imagetype": 0, "messagepropertyname": "Target", "attributes": image,
                "sdkmessageprocessingstepid@odata.bind": f"/sdkmessageprocessingsteps({d['sdkmessageprocessingstepid']})"},
                solution=SOL)[:2], "  pre image")


def retire_old(dv):
    """Deletes the old unmanaged dev-only assembly, its types and steps, so extensions are not applied twice."""
    for a in q(dv, f"pluginassemblies?$select=pluginassemblyid&$filter=name eq '{OLD_ASSEMBLY}'"):
        for t in q(dv, f"plugintypes?$select=plugintypeid&$filter=_pluginassemblyid_value eq {a['pluginassemblyid']}"):
            for st in q(dv, f"sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid,name&$filter=_eventhandler_value eq {t['plugintypeid']}"):
                ok(*dv.call("DELETE", f"sdkmessageprocessingsteps({st['sdkmessageprocessingstepid']})")[:2], "delete old step " + st["name"])
            ok(*dv.call("DELETE", f"plugintypes({t['plugintypeid']})")[:2], "delete old type")
        ok(*dv.call("DELETE", f"pluginassemblies({a['pluginassemblyid']})")[:2], "delete old assembly " + OLD_ASSEMBLY)


def all_rows(dv, path):
    rows, url = [], path.replace(" ", "%20")
    while url:
        s, d, _ = dv.call("GET", url, extra={"Prefer": "odata.maxpagesize=5000"})
        ok(s, d, "  page")
        rows += d["value"]
        nxt = d.get("@odata.nextLink")
        url = nxt.split("/api/data/v9.2/", 1)[1] if nxt else None
    return rows


GUARD_STEP = f"{ASSEMBLY}.ProjectFeePlugin: Update of msdyn_project"


def same(current, new):
    """Dataverse returns money as floats and dates without fractions; compare loosely."""
    if isinstance(current, (int, float)) and isinstance(new, (int, float)):
        return abs(current - new) < 0.005
    if isinstance(current, str) and isinstance(new, str):
        return current[:19] == new[:19]
    return current == new


def require_plugin_absent(dv, step):
    if q(dv, f"sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid&$filter=name eq '{GUARD_STEP}' and statecode eq 0"):
        sys.exit(f"The {ASSEMBLY} guard step is active here; run {step} without it (backfill: before import; restore: "
                 f"uninstall {SOL} first). It blocks DHA Fee changes on projects with fee extensions.")


def backfill(dv, dry_run, only):
    """PROD one-off: original fee where blank, then the fee extensions logged while the plug-in was missing.

    Every logged extension was checked against the DHA Fee audit history first: none had been applied by hand
    (37133 was raised to 31,100 and put back to 29,850 the same morning), so applying them is DHA Fee := original +
    active extensions, with original := the current DHA Fee. Re-running is safe (nothing is added twice).
    Writes a snapshot of every project's fee fields to DHA.Dataverse/Backups first; "restore" puts it back.
    """
    require_plugin_absent(dv, "backfill")
    flt = f" and isc_projectnumbernew eq '{only}'" if only else ""
    projects = all_rows(dv, f"msdyn_projects?$select=msdyn_projectid,isc_projectnumbernew,{','.join(FEE_FIELDS)}"
                            f"&$filter=statecode ne -1{flt}")
    os.makedirs(BACKUPS, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    snapshot = os.path.join(BACKUPS, f"project_fees_before_backfill_{stamp}{'_' + only if only else ''}.json")
    with open(snapshot, "x", encoding="utf-8") as f:  # "x": a snapshot is never overwritten
        json.dump({"org": dv.ORG, "taken": stamp, "fields": FEE_FIELDS,
                   "projects": [{k: p.get(k) for k in ["msdyn_projectid", "isc_projectnumbernew"] + FEE_FIELDS} for p in projects]},
                  f, indent=1)
    print(f"snapshot of {len(projects)} projects: {os.path.normpath(snapshot)}")

    extensions = {}
    for e in all_rows(dv, "fwp_feeextensions?$select=_fwp_project_value,fwp_amount,createdon&$filter=statecode eq 0&$orderby=createdon desc"):
        extensions.setdefault(e["_fwp_project_value"], []).append(e)

    changed, failed = 0, []
    for p in projects:
        exts = extensions.get(p["msdyn_projectid"], [])
        if p["fwp_originalprojectfee"] is not None and not exts:
            continue
        if p["isc_projectamount"] is None and not exts:
            continue  # no fee to copy
        original = p["fwp_originalprojectfee"] if p["fwp_originalprojectfee"] is not None else (p["isc_projectamount"] or 0)
        body = {"fwp_originalprojectfee": original}
        if exts:
            total = sum(e["fwp_amount"] or 0 for e in exts)
            body.update({"isc_projectamount": original + total, "isc_feeextension": total,
                         "fwp_lastfeeextensiondate": exts[0]["createdon"],
                         "fwp_previousfeeextensiondate": exts[1]["createdon"] if len(exts) > 1 else None})
        if all(same(p.get(k), v) for k, v in body.items()):
            continue  # already right (re-run)
        if exts:
            print(f"{p['isc_projectnumbernew']}: DHA Fee {p['isc_projectamount']} -> {body['isc_projectamount']} "
                  f"(original {original}, extensions {total}, last {body['fwp_lastfeeextensiondate']})")
        if not dry_run:
            s, d, _ = dv.call("PATCH", f"msdyn_projects({p['msdyn_projectid']})", body)
            if s >= 300:
                failed.append((p["isc_projectnumbernew"], p["msdyn_projectid"], str(d)[:300]))
                continue
        changed += 1
        if changed % 500 == 0:
            print(f"  {changed} done")
    print(f"{changed} projects {'would be ' if dry_run else ''}updated, {len(failed)} failed")
    for f_ in failed:
        print("  FAILED", *f_)


def restore(dv, snapshot, dry_run):
    """Puts the fee fields back exactly as in a backfill snapshot, for projects whose values differ now."""
    require_plugin_absent(dv, "restore")
    data = json.load(open(snapshot, encoding="utf-8"))
    if data["org"] != dv.ORG:
        sys.exit(f"snapshot is from {data['org']}, not {dv.ORG}")
    now = {p["msdyn_projectid"]: p for p in all_rows(dv, f"msdyn_projects?$select=msdyn_projectid,{','.join(FEE_FIELDS)}")}
    n = 0
    for p in data["projects"]:
        cur = now.get(p["msdyn_projectid"])
        body = {k: p[k] for k in data["fields"] if cur is not None and cur.get(k) != p[k]}
        if not body:
            continue
        if not dry_run:
            ok(*dv.call("PATCH", f"msdyn_projects({p['msdyn_projectid']})", body)[:2], f"  restore {p['isc_projectnumbernew']}")
        n += 1
    print(f"{n} projects {'would be ' if dry_run else ''}restored")


def main(step, args):
    dry_run = "--dry-run" in args
    only = args[args.index("--only") + 1] if "--only" in args else None
    if step == "dev":
        dv = env(DEV)
        register(dv)
        retire_old(dv)
    elif step == "package":
        dv = env(DEV)
        s, d, _ = dv.call("POST", "ExportSolution", {"SolutionName": SOL, "Managed": True})
        ok(s, d, "export managed")
        with open(PACKAGE, "wb") as f:
            f.write(base64.b64decode(d["ExportSolutionFile"]))
        print("wrote", os.path.normpath(PACKAGE))
    elif step == "import":
        dv = env(PROD)
        with open(PACKAGE, "rb") as f:
            data = base64.b64encode(f.read()).decode()
        ok(*dv.call("POST", "ImportSolution", {"CustomizationFile": data, "OverwriteUnmanagedCustomizations": False,
                                               "PublishWorkflows": False, "ImportJobId": str(uuid.uuid4())})[:2], "import")
    elif step == "backfill":
        backfill(env(os.environ.get("DATAVERSE_URL", PROD)), dry_run, only)
    elif step == "restore":
        restore(env(os.environ.get("DATAVERSE_URL", PROD)), args[0], dry_run)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "", sys.argv[2:])
