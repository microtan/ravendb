import commandBase = require("commands/commandBase");
import database = require("models/database");
import appUrl = require("common/appUrl");

class formatIndexCommand extends commandBase {

    constructor(private db: database, private mapReduceArray: string[]) {
        super();
    }

    execute(): JQueryPromise<string[]> {
        var url = "/debug/format-index";
        return this.post(url, JSON.stringify(this.mapReduceArray), this.db);
    }
}

export = formatIndexCommand;