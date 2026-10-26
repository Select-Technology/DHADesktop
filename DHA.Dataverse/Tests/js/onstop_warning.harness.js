// Runs the DEPLOYED fwp_projectOnStopWarning web resource (DHA-8) against a fake Dynamics form.
// Usage: node onstop_warning.harness.js <path-to-web-resource.js>
// Prints one JSON line per case and exits non-zero if any case fails.
"use strict";
const fs = require("fs");
const vm = require("vm");

const source = fs.readFileSync(process.argv[2], "utf8");
const results = [];

function makeForm({ project, onStop, apiFails }) {
    const state = { notifications: {}, alerts: [], postSave: [], retrieved: [] };
    const formContext = {
        getAttribute: (name) => name === "fwp_project"
            ? { getValue: () => project ? [{ id: "{" + project.id + "}", name: project.name, entityType: "msdyn_project" }] : null }
            : null,
        ui: {
            setFormNotification: (msg, level, id) => { state.notifications[id] = { msg, level }; },
            clearFormNotification: (id) => { delete state.notifications[id]; }
        },
        data: { entity: { addOnPostSave: (fn) => state.postSave.push(fn) } }
    };
    const Xrm = {
        WebApi: {
            retrieveRecord: (entity, id, options) => {
                state.retrieved.push({ entity, id, options });
                return apiFails ? Promise.reject(new Error("network down")) : Promise.resolve({ fwp_onstop: onStop });
            }
        },
        Navigation: { openAlertDialog: (opts) => { state.alerts.push(opts); return Promise.resolve(); } }
    };
    const ctx = { getFormContext: () => formContext };
    const sandbox = { Xrm, console: { error: () => {} } };
    vm.createContext(sandbox);
    vm.runInContext(source + "\nthis.ProjectOnStopWarning = ProjectOnStopWarning;", sandbox);
    return { api: sandbox.ProjectOnStopWarning, ctx, state };
}

const settle = () => new Promise((r) => setTimeout(r, 20));
const PROJ = { id: "11111111-2222-3333-4444-555555555555", name: "12345 - Test Job" };

async function check(name, fn) {
    try { await fn(); results.push({ name, ok: true }); }
    catch (e) { results.push({ name, ok: false, error: e.message }); }
}
function expect(cond, msg) { if (!cond) throw new Error(msg); }

(async () => {
    await check("on load, stopped project -> red ERROR notification with the agreed wording", async () => {
        const f = makeForm({ project: PROJ, onStop: true });
        f.api.onLoad(f.ctx); await settle();
        const n = f.state.notifications["fwp_project_onstop"];
        expect(n, "no notification shown");
        expect(n.level === "ERROR", "level should be ERROR (red), was " + n.level);
        expect(n.msg === "Warning: Project on stop", "wording was: " + n.msg);
        expect(f.state.retrieved[0].entity === "msdyn_project", "should read msdyn_project");
        expect(f.state.retrieved[0].id === PROJ.id, "braces must be stripped from the id");
        expect(/fwp_onstop/.test(f.state.retrieved[0].options), "should select fwp_onstop");
    });

    await check("on load, project not on stop -> no warning", async () => {
        const f = makeForm({ project: PROJ, onStop: false });
        f.api.onLoad(f.ctx); await settle();
        expect(!f.state.notifications["fwp_project_onstop"], "warning shown for a project that is not on stop");
    });

    await check("on load, flag never set (null) -> no warning", async () => {
        const f = makeForm({ project: PROJ, onStop: null });
        f.api.onLoad(f.ctx); await settle();
        expect(!f.state.notifications["fwp_project_onstop"], "null must be treated as not on stop");
    });

    await check("no project selected -> no warning and no lookup", async () => {
        const f = makeForm({ project: null, onStop: true });
        f.api.onLoad(f.ctx); await settle();
        expect(!f.state.notifications["fwp_project_onstop"], "warning shown with no project");
        expect(f.state.retrieved.length === 0, "should not query when there is no project");
    });

    await check("changing to a stopped project shows the warning; changing away clears it", async () => {
        const f = makeForm({ project: PROJ, onStop: true });
        f.api.onProjectChange(f.ctx); await settle();
        expect(f.state.notifications["fwp_project_onstop"], "not shown on change");
        const g = makeForm({ project: PROJ, onStop: false });
        g.state.notifications["fwp_project_onstop"] = { msg: "old", level: "ERROR" };
        g.api.onProjectChange(g.ctx); await settle();
        expect(!g.state.notifications["fwp_project_onstop"], "stale warning not cleared");
    });

    await check("after saving against a stopped project -> warning dialog, entry still saved (no block)", async () => {
        const f = makeForm({ project: PROJ, onStop: true });
        f.api.onLoad(f.ctx); await settle();
        expect(f.state.postSave.length === 1, "post-save handler not registered");
        f.state.postSave[0](f.ctx); await settle();
        expect(f.state.alerts.length === 1, "no dialog after save");
        expect(f.state.alerts[0].title === "Warning: Project on stop", "dialog title was " + f.state.alerts[0].title);
        expect(/saved/i.test(f.state.alerts[0].text), "dialog should say the time was saved");
    });

    await check("after saving against a normal project -> no dialog", async () => {
        const f = makeForm({ project: PROJ, onStop: false });
        f.api.onLoad(f.ctx); await settle();
        f.state.postSave[0](f.ctx); await settle();
        expect(f.state.alerts.length === 0, "dialog shown for a project not on stop");
    });

    await check("on load, no dialog (dialog is only for saves)", async () => {
        const f = makeForm({ project: PROJ, onStop: true });
        f.api.onLoad(f.ctx); await settle();
        expect(f.state.alerts.length === 0, "dialog shown on load");
    });

    await check("lookup fails -> no error thrown, no warning, form still usable", async () => {
        const f = makeForm({ project: PROJ, onStop: true, apiFails: true });
        f.api.onLoad(f.ctx); await settle();
        expect(!f.state.notifications["fwp_project_onstop"], "warning shown despite failed lookup");
    });

    for (const r of results) console.log(JSON.stringify(r));
    process.exit(results.every((r) => r.ok) ? 0 : 1);
})();
