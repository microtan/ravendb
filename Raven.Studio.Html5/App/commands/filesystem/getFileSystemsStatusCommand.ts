﻿import commandBase = require("commands/commandBase");
import filesystem = require("models/filesystem/filesystem");

class getFileSystemsStatusCommand extends commandBase {

    execute(): JQueryPromise<string> {

        var url = "/fs/status";

        var resultsSelector = (response: any) => response.Status;
        return this.query(url, null, null, resultsSelector);
    }
}

export = getFileSystemsStatusCommand; 