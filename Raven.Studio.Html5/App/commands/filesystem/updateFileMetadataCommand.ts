﻿import commandBase = require("commands/commandBase");
import filesystem = require("models/filesystem/filesystem");
import appUrl = require("common/appUrl");
import uploadItem = require("models/uploadItem");

class updateFileMetadataCommand extends commandBase {

    constructor(private fileName: string, private metadata: any, private fs: filesystem, private reportSaveProgress = true) {
        super();
    }

    execute(): JQueryPromise<any> {
        if (this.reportSaveProgress) {
            this.reportInfo("Saving " + this.fileName + "...");
        }

        var customHeaders = {};

        //We only want to stringify when the header's value is a json doc.
        for (var key in this.metadata) {
            if (typeof (this.metadata[key]) != "string" && typeof (this.metadata[key]) != "number")
                customHeaders[key] = JSON.stringify(this.metadata[key]);
            else {
                customHeaders[key] = this.metadata[key]
            }
        }

        var jQueryOptions: JQueryAjaxSettings = {
            headers: <any>customHeaders
        };

        var url = "/files/" + this.fileName;
        var updateTask = this.post(url, null, this.fs, jQueryOptions);

        if (this.reportSaveProgress) {
            updateTask.done(() => this.reportSuccess("Saved " + this.fileName));
            updateTask.fail((response: JQueryXHR) => {
                this.reportError("Failed to save " + this.fileName, response.responseText, response.statusText);
            });
        }

        return updateTask;
    }
}

export = updateFileMetadataCommand;