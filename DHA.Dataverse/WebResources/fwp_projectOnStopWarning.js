// Project On Stop warning for the Time Entry (fwp_timeentry) form - DHA-8
// Shows a red form notification when the selected project has been placed on stop by Finance
// (msdyn_project.fwp_onstop, DHA-7). The entry can still be saved; this is a warning only.
//
// Register on the fwp_timeentry main form:
//   OnLoad                 -> ProjectOnStopWarning.onLoad (pass execution context)
//   fwp_project OnChange   -> ProjectOnStopWarning.onProjectChange (pass execution context)

var ProjectOnStopWarning = {

    // Configuration
    MESSAGE: "Warning: Project on stop",
    NOTIFICATION_ID: "fwp_project_onstop",
    PROJECT_FIELD: "fwp_project",

    onLoad: function (executionContext) {
        var self = this;
        var formContext = executionContext.getFormContext();

        // Show the warning again (as a dialog) after every successful save against a stopped project
        formContext.data.entity.addOnPostSave(function (ctx) {
            self.checkProject(ctx.getFormContext(), true);
        });

        this.checkProject(formContext, false);
    },

    onProjectChange: function (executionContext) {
        this.checkProject(executionContext.getFormContext(), false);
    },

    checkProject: function (formContext, isPostSave) {
        var self = this;
        var projectAttr = formContext.getAttribute(this.PROJECT_FIELD);
        var project = projectAttr ? projectAttr.getValue() : null;

        if (!project || project.length === 0) {
            formContext.ui.clearFormNotification(this.NOTIFICATION_ID);
            return;
        }

        var projectId = project[0].id.replace("{", "").replace("}", "");

        Xrm.WebApi.retrieveRecord("msdyn_project", projectId, "?$select=fwp_onstop")
            .then(function (result) {
                if (result.fwp_onstop === true) {
                    formContext.ui.setFormNotification(self.MESSAGE, "ERROR", self.NOTIFICATION_ID);
                    if (isPostSave) {
                        Xrm.Navigation.openAlertDialog({
                            title: self.MESSAGE,
                            text: "Your time has been saved. " + project[0].name + " has been placed on stop by Finance."
                        });
                    }
                } else {
                    formContext.ui.clearFormNotification(self.NOTIFICATION_ID);
                }
            })
            .catch(function (error) {
                // Never block time entry because the check failed
                console.error("ProjectOnStopWarning: " + error.message);
            });
    }
};
