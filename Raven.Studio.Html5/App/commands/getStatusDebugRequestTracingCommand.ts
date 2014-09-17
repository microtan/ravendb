import commandBase = require("commands/commandBase");
import database = require("models/database");

class getStatusDebugRequestTracingommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<requestTracingDto[]> {
        var url = "/debug/request-tracing";
        return this.query<requestTracingDto[]>(url, null, this.db);
    }
}

export = getStatusDebugRequestTracingommand;