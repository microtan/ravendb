import commandBase = require("commands/commandBase");
import database = require("models/database");

class getIndexingPerfStatsCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<any> {
        var url = "/debug/indexing-perf-stats";
        return this.query<any>(url, null, this.db);
    }
}

export = getIndexingPerfStatsCommand;