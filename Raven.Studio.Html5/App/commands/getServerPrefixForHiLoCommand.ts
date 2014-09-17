﻿import database = require("models/database");
import customFunctions = require("models/customFunctions");
import commandBase = require("commands/commandBase");

class getServerPrefixForHiLoCommand extends commandBase {

    constructor(private db: database) {
        super();

        if (!db) {
            throw new Error("Must specify database");
        }
    }

    execute(): JQueryPromise<string> {
        var resultsSelector = (queryResult: any) => queryResult.ServerPrefix;
        var url = "/docs/Raven/ServerPrefixForHilo";
        return this.query(url, null, this.db, resultsSelector);
    }
}

export = getServerPrefixForHiLoCommand;