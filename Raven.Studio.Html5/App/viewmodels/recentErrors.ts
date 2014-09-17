﻿import dialog = require("plugins/dialog");
import dialogViewModelBase = require("viewmodels/dialogViewModelBase");
import alertArgs = require("common/alertArgs");
import alertType = require("common/alertType");

class recentErrors extends dialogViewModelBase {

    resizerSelector = ".dialogResizer";
   
    constructor(private errors: KnockoutObservableArray<alertArgs>) {
        super();
        var x = alertType.danger;
    }

    attached() {
        // Expand the first error.
        if (this.errors().length > 0) {
            $("#errorDetailsCollapse0").collapse("show");
        }

        this.registerResizing("recentErrorsResize");
    }

    detached() {
        super.detached();
        this.unregisterResizing("recentErrorsResize");
    }

    clear() {
        this.errors.removeAll();
    }

    close() {
        dialog.close(this);
    }

    getErrorDetails(alert: alertArgs) {
        var error = alert.errorInfo;
        if (error != null && error.stackTrace) {
            return error.stackTrace.replace("\r\n", "\n");
        }

        return alert.details;
    }

    getDangerAlertType() {
        return alertType.danger;
    }

    getWarningAlertType() {
        return alertType.warning;
    }

}

export = recentErrors; 