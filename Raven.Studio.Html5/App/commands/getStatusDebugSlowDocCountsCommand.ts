import commandBase = require("commands/commandBase");
import database = require("models/database");
import debugDocumentStats = require("models/debugDocumentStats");

class getStatusDebugSlowDocCountsCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<debugDocumentStats> {
        var url = "/debug/sl0w-d0c-c0unts";
        var resultSelector = (result) => new debugDocumentStats(result);
        return this.query<debugDocumentStats>(url, null, this.db, resultSelector);
    }
}

export = getStatusDebugSlowDocCountsCommand;