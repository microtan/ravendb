import commandBase = require("commands/commandBase");
import database = require("models/database");

class getStatusDebugChangesCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<Array<statusDebugChangesDto>> {
        var url = "/debug/changes";
        return this.query<Array<statusDebugChangesDto>>(url, null, this.db);
    }
}

export = getStatusDebugChangesCommand;