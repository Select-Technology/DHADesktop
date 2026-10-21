"""Fee extensions: registers DHA.ProjectFees.Plugins in the FWPProjectFees solution.

The September 2026 fee adjustment went to prod without its plug-in (Select Support had registered an unmanaged
"DhaProjectFeesPlugin" assembly in dev only, outside any solution), so fee extensions never updated the project.
That dev assembly also had its original-fee step registered on the wrong message. This replaces it with source-
controlled code and proper registration:

  ProjectFeePlugin            msdyn_project  Create (pre)  original fee := DHA Fee
                              msdyn_project  Update (pre)  isc_projectamount + PreImage; original follows DHA Fee until
                                                           the first extension, then manual edits are blocked
  FeeExtensionAppliedPlugin   fwp_feeextension Create (post)  DHA Fee += amount, extension total, last/previous dates

Steps (build DHA.ProjectFees.Plugins in Release first):
  dev      register/update in DEV and retire the old DhaProjectFeesPlugin steps there
  package  export FWPProjectFees managed from DEV to DHA.Dataverse/Solutions
  backfill PROD, before import: original fee := DHA Fee where blank, then apply the fee extensions already logged
  import   import the managed package into PROD

    python -I provision_project_fees.py dev|package|backfill|import [--dry-run]
"""
import base64, os, sys, uuid
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

# (plug-in class, message, entity, stage, filtering attributes, pre-image attributes)
STEPS = [
    ("ProjectFeePlugin", "Create", "msdyn_project", 20, None, None),
    ("ProjectFeePlugin", "Update", "msdyn_project", 20, "isc_projectamount", "isc_projectamount"),
    ("FeeExtensionAppliedPlugin", "Create", "fwp_feeextension", 40, None, None),
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


def backfill(dv, dry_run):
    """PROD one-off: original fee where blank, then the fee extensions logged while the plug-in was missing.

    Every logged extension was checked against the DHA Fee audit history first: none had been applied by hand
    (37133 was raised to 31,100 and put back to 29,850 the same morning), so each one is applied exactly once.
    Re-running is safe: DHA Fee is recomputed as original + active extensions, not added to again.
    Run it BEFORE "import": once ProjectFeePlugin is registered it blocks setting the DHA Fee of a project that
    has fee extensions, which is exactly what step 2 does.
    """
    if q(dv, f"pluginassemblies?$select=pluginassemblyid&$filter=name eq '{ASSEMBLY}'"):
        sys.exit(f"{ASSEMBLY} is already registered here; run backfill before import.")
    # 1. Original fee := DHA Fee where blank (projects with no DHA Fee are left blank)
    n, url = 0, ("msdyn_projects?$select=msdyn_projectid,isc_projectamount"
                 "&$filter=fwp_originalprojectfee eq null and isc_projectamount ne null")
    pending = []
    while url:
        s, d, _ = dv.call("GET", url.replace(" ", "%20"), extra={"Prefer": "odata.maxpagesize=5000"})
        ok(s, d, "page")
        pending += d["value"]
        nxt = d.get("@odata.nextLink")
        url = nxt.split("/api/data/v9.2/", 1)[1] if nxt else None
    print(f"{len(pending)} projects need an original fee")
    extended = {e["_fwp_project_value"] for e in q(dv, "fwp_feeextensions?$select=_fwp_project_value&$filter=statecode eq 0")}
    for p in pending:
        if p["msdyn_projectid"] in extended:
            continue  # set together with its extensions below
        if not dry_run:
            s, d, _ = dv.call("PATCH", f"msdyn_projects({p['msdyn_projectid']})", {"fwp_originalprojectfee": p["isc_projectamount"]})
            if s >= 300:
                print("  failed", p["msdyn_projectid"], str(d)[:300]); continue
        n += 1
        if n % 500 == 0:
            print(f"  {n} done")
    print(f"original fee set on {n} projects" + (" (dry run)" if dry_run else ""))

    # 2. Apply the logged extensions, once per project
    for proj in sorted(extended):
        p = dv.call("GET", f"msdyn_projects({proj})?$select=isc_projectnumbernew,isc_projectamount,fwp_originalprojectfee")[1]
        exts = q(dv, f"fwp_feeextensions?$select=fwp_amount,createdon&$filter=_fwp_project_value eq {proj} and statecode eq 0"
                     "&$orderby=createdon desc")
        total = sum(e["fwp_amount"] or 0 for e in exts)
        original = p["fwp_originalprojectfee"] if p["fwp_originalprojectfee"] is not None else (p["isc_projectamount"] or 0)
        body = {"fwp_originalprojectfee": original, "isc_projectamount": original + total, "isc_feeextension": total,
                "fwp_lastfeeextensiondate": exts[0]["createdon"] if exts else None,
                "fwp_previousfeeextensiondate": exts[1]["createdon"] if len(exts) > 1 else None}
        print(f"{p['isc_projectnumbernew']}: DHA Fee {p['isc_projectamount']} -> {body['isc_projectamount']} "
              f"(original {original}, extensions {total}, last {body['fwp_lastfeeextensiondate']})")
        if not dry_run:
            ok(*dv.call("PATCH", f"msdyn_projects({proj})", body)[:2], "  applied")


def main(step, dry_run):
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
        backfill(env(PROD), dry_run)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "", "--dry-run" in sys.argv)
